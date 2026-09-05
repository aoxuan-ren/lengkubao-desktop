using Newtonsoft.Json.Linq;
using Newtonsoft.Json;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Xml.Linq;
using lengkubao.desktop.Sync;

namespace lengkubao.desktop
{
    public class AutoSyncServer
    {
        #region 基础组件
        private TcpListener tcpListener;
        private UdpClient broadcastListener;
        private Thread mainThread;
        private bool isRunning = false;
        private int tcpPort = 8080;
        private int broadcastPort = 8888;
        private RichTextBox logControl;
        private ConcurrentDictionary<string, DeviceClient> connectedDevices = new ConcurrentDictionary<string, DeviceClient>();
        /// <summary>每设备串行化：业务同步与全量反向同步互斥，避免快照与写入交错导致数据边界不一致。</summary>
        private readonly ConcurrentDictionary<string, SemaphoreSlim> deviceSyncLocks = new ConcurrentDictionary<string, SemaphoreSlim>();
        // 双向增量同步状态
        private readonly object deltaSyncLock = new object();
        private long nextCommitSeq = 1;
        private readonly List<ChangeLogEntry> syncChangeLog = new List<ChangeLogEntry>();
        private readonly ConcurrentDictionary<string, long> deviceLastAckSeq = new ConcurrentDictionary<string, long>();
        private readonly ConcurrentDictionary<string, bool> deviceFirstFullSyncDone = new ConcurrentDictionary<string, bool>();
        private readonly ConcurrentDictionary<string, KnownDeviceRecord> knownDevices = new ConcurrentDictionary<string, KnownDeviceRecord>();
        private readonly ConcurrentDictionary<string, long> dedupAppliedOps = new ConcurrentDictionary<string, long>();
        /// <summary>每设备 legacy PUSH_DATA 行指纹：stableKey -> sha256(syncData)，避免重连重复推送未变化单据。</summary>
        private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, string>> deviceLegacyPushFingerprints =
            new ConcurrentDictionary<string, ConcurrentDictionary<string, string>>();
        private Queue<SyncTask> syncTaskQueue = new Queue<SyncTask>();
        private object queueLock = new object();
        private FileSystemWatcher dbWatcher;
        private DateTime lastDbChangeNotifyUtc = DateTime.MinValue;
        private readonly object dbChangeDebounceLock = new object();
        private Form parentForm;
        private NotifyIcon trayIcon;
        private ContextMenuStrip trayMenu;

        private readonly DiscoveryService discoveryService;
        private readonly SyncTransport syncTransport;
        private readonly CrsqlEngine crsqlEngine;
        private readonly SyncEngine syncEngine;
        private readonly ConcurrentDictionary<string, string> transportToDeviceId = new ConcurrentDictionary<string, string>();
        private readonly ConcurrentDictionary<string, bool> transportRegistered = new ConcurrentDictionary<string, bool>();
        private readonly bool useTouchSocketTransport = true;

        private string _pairingCode;
        public string PairingCode
        {
            get => _pairingCode;
            set
            {
                if (_pairingCode != value)
                {
                    _pairingCode = value;
                    LogMessage($"🔐 配对码已更新: {_pairingCode}");
                    OnPairingCodeChanged?.Invoke(this, _pairingCode);
                    SavePairingCodeToConfig();
                    if (isRunning)
                    {
                        discoveryService?.UpdatePairingCode(_pairingCode, tcpPort);
                        RestartBroadcastService();
                    }
                    UpdateTrayMenuDisplay();
                }
            }
        }

        private string MdnsServiceName => $"LengKuBao_{_pairingCode}";
        #endregion

        #region 事件定义
        public event EventHandler<string> OnLogMessage;
        public event EventHandler<int> OnDeviceConnected;
        public event EventHandler<int> OnDeviceDisconnected;
        /// <summary>已连接设备快照，连接集合变化时触发，供 UI 全量刷新。</summary>
        public event EventHandler<ConnectedDevicesChangedEventArgs> OnConnectedDevicesChanged;
        public event EventHandler<SyncProgressEventArgs> OnSyncProgress;
        public event EventHandler<string> OnPairingCodeChanged;
        /// <summary>待同步基础配置列表变化时触发。</summary>
        public event EventHandler<PendingConfigDeltaChangedEventArgs> OnPendingConfigDeltaChanged;
        #endregion

        private static readonly HashSet<string> ConfigEntityTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "CUSTOMER", "LOCATION", "OPERATOR", "PRODUCT", "PACK_TYPE"
        };

        public AutoSyncServer(RichTextBox logControl = null, Form parentForm = null)
        {
            this.logControl = logControl;
            this.parentForm = parentForm;
            InitializePairingCode();
            LoadDeltaSyncState();
            discoveryService = new DiscoveryService(LogMessage);
            syncTransport = new SyncTransport(LogMessage);
            crsqlEngine = new CrsqlEngine(LogMessage);
            syncEngine = new SyncEngine(crsqlEngine, LogMessage);
            syncEngine.LegacyLineHandler = (clientId, line) => HandleSyncEngineLegacyLine(clientId, line);
            syncEngine.PushHandler = HandleUnifiedPush;
            syncEngine.ConfigPullHandler = HandleConfigPullCrsql;
            syncEngine.ConfigPushHandler = HandleConfigPushCrsql;
            syncTransport.OnLineReceived = HandleTouchSocketLineAsync;
            syncTransport.ClientDisconnected += transportId =>
            {
                transportRegistered.TryRemove(transportId, out _);
                if (!transportToDeviceId.TryRemove(transportId, out string deviceId))
                    return;

                if (connectedDevices.TryGetValue(deviceId, out var dev) &&
                    string.Equals(dev.TransportClientId, transportId, StringComparison.Ordinal))
                {
                    connectedDevices.TryRemove(deviceId, out _);
                    dev.Close();
                    LogMessage($"📴 TouchSocket 设备断开: {dev.DeviceName}");
                    PublishConnectedDevicesSnapshot();
                }
                else
                {
                    LogMessage($"ℹ️ 忽略旧 TouchSocket 连接断开: {transportId} (device={deviceId})");
                }
            };
            DatabaseManager.LocalConfigChangedForSync += OnLocalDatabaseConfigChangedForSync;
            DatabaseManager.LocalPresaleChangedForSync += OnLocalPresaleChangedForSync;
        }

        #region 配对码管理
        private void InitializePairingCode()
        {
            try
            {
                if (!LoadPairingCodeFromConfig())
                {
                    GenerateRandomPairingCode();
                    SavePairingCodeToConfig();
                }
                LogMessage($"🔐 初始化配对码: {_pairingCode}");
            }
            catch (Exception ex)
            {
                LogMessage($"⚠️ 初始化配对码失败: {ex.Message}");
                _pairingCode = "123456";
            }
        }

        private void GenerateRandomPairingCode()
        {
            Random random = new Random();
            _pairingCode = random.Next(100000, 999999).ToString();
        }

        private bool LoadPairingCodeFromConfig()
        {
            try
            {
                string configPath = GetConfigPath();
                if (File.Exists(configPath))
                {
                    var lines = File.ReadAllLines(configPath);
                    foreach (var line in lines)
                    {
                        if (line.StartsWith("PairingCode="))
                        {
                            _pairingCode = line.Substring("PairingCode=".Length).Trim();
                            return !string.IsNullOrEmpty(_pairingCode);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LogMessage($"⚠️ 加载配对码失败: {ex.Message}");
            }
            return false;
        }

        private void SavePairingCodeToConfig()
        {
            try
            {
                if (string.IsNullOrWhiteSpace(_pairingCode)) return;

                string configPath = GetConfigPath();
                var lines = new List<string>();

                if (File.Exists(configPath))
                {
                    lines = File.ReadAllLines(configPath).ToList();
                    for (int i = 0; i < lines.Count; i++)
                    {
                        if (lines[i].StartsWith("PairingCode="))
                        {
                            lines[i] = $"PairingCode={_pairingCode}";
                            File.WriteAllLines(configPath, lines);
                            return;
                        }
                    }
                }

                lines.Add($"PairingCode={_pairingCode}");
                File.WriteAllLines(configPath, lines);
                LogMessage($"✅ 配对码已保存到配置文件: {_pairingCode}");
            }
            catch (Exception ex)
            {
                LogMessage($"❌ 保存配对码失败: {ex.Message}");
            }
        }

        private string GetConfigPath()
        {
            string configDir = Path.Combine(Application.StartupPath, "config");
            if (!Directory.Exists(configDir))
            {
                Directory.CreateDirectory(configDir);
            }
            return Path.Combine(configDir, "sync_config.ini");
        }

        /// <summary>读取是否打开软件时自动启动同步（默认 true）。</summary>
        public bool LoadAutoStartSetting()
        {
            try
            {
                string configPath = GetConfigPath();
                if (File.Exists(configPath))
                {
                    foreach (var line in File.ReadAllLines(configPath))
                    {
                        if (line.StartsWith("AutoStartSync="))
                        {
                            var val = line.Substring("AutoStartSync=".Length).Trim();
                            if (bool.TryParse(val, out bool result)) return result;
                            return val == "1" || val.Equals("yes", StringComparison.OrdinalIgnoreCase);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LogMessage($"⚠️ 加载自动启动设置失败: {ex.Message}");
            }
            return true;
        }

        /// <summary>保存打开软件时自动启动同步选项。</summary>
        public void SaveAutoStartSetting(bool autoStart)
        {
            try
            {
                string configPath = GetConfigPath();
                var lines = new List<string>();
                bool found = false;

                if (File.Exists(configPath))
                {
                    lines = File.ReadAllLines(configPath).ToList();
                    for (int i = 0; i < lines.Count; i++)
                    {
                        if (lines[i].StartsWith("AutoStartSync="))
                        {
                            lines[i] = $"AutoStartSync={autoStart}";
                            found = true;
                            break;
                        }
                    }
                }

                if (!found)
                {
                    lines.Add($"AutoStartSync={autoStart}");
                }

                File.WriteAllLines(configPath, lines);
                LogMessage($"✅ 自动启动同步设置已保存: {autoStart}");
            }
            catch (Exception ex)
            {
                LogMessage($"❌ 保存自动启动设置失败: {ex.Message}");
            }
        }

        private bool IsValidPairingCode(string code)
        {
            if (string.IsNullOrWhiteSpace(code)) return false;
            code = code.Trim();
            if (code.Length < 3 || code.Length > 10) return false;
            return code.All(c => char.IsLetterOrDigit(c));
        }

        public void SavePairingCode(string newCode)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(newCode))
                {
                    LogMessage("❌ 配对码不能为空");
                    return;
                }
                if (!IsValidPairingCode(newCode))
                {
                    LogMessage("❌ 配对码格式不正确（只能包含字母和数字，3-10位）");
                    return;
                }
                PairingCode = newCode;
            }
            catch (Exception ex)
            {
                LogMessage($"❌ 保存配对码失败: {ex.Message}");
            }
        }

        public DialogResult ShowPairingCodeSettings()
        {
            return ShowPairingCodeSettings(null);
        }

        public DialogResult ShowPairingCodeSettings(IWin32Window owner)
        {
            using (var form = new Form())
            {
                form.Text = "设置配对码";
                form.Size = new Size(450, 280);
                form.StartPosition = FormStartPosition.CenterParent;
                form.FormBorderStyle = FormBorderStyle.FixedDialog;
                form.MaximizeBox = false;
                form.MinimizeBox = false;
                form.BackColor = Color.White;

                var lblTitle = new Label
                {
                    Text = "配对码设置",
                    Font = new Font("微软雅黑", 14, FontStyle.Bold),
                    Location = new Point(20, 15),
                    Size = new Size(400, 30),
                    ForeColor = Color.FromArgb(0, 120, 215)
                };

                var lblDesc = new Label
                {
                    Text = "配对码用于手机端连接电脑时的身份验证，\n请确保手机端输入相同的配对码。",
                    Location = new Point(20, 50),
                    Size = new Size(400, 40),
                    ForeColor = Color.Gray
                };

                var lblCurrent = new Label
                {
                    Text = "当前配对码：",
                    Location = new Point(20, 100),
                    Size = new Size(100, 30),
                    TextAlign = ContentAlignment.MiddleLeft
                };

                var txtCurrent = new TextBox
                {
                    Text = _pairingCode,
                    Location = new Point(130, 100),
                    Size = new Size(200, 30),
                    ReadOnly = true,
                    BackColor = Color.LightGray,
                    Font = new Font("Consolas", 12, FontStyle.Bold)
                };

                var btnCopy = new Button
                {
                    Text = "复制",
                    Location = new Point(340, 100),
                    Size = new Size(70, 30),
                    FlatStyle = FlatStyle.Flat
                };
                btnCopy.Click += (s, e) =>
                {
                    Clipboard.SetText(_pairingCode);
                    MessageBox.Show("配对码已复制到剪贴板", "提示",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                };

                var lblNew = new Label
                {
                    Text = "新配对码：",
                    Location = new Point(20, 140),
                    Size = new Size(100, 30),
                    TextAlign = ContentAlignment.MiddleLeft
                };

                var txtNew = new TextBox
                {
                    Location = new Point(130, 140),
                    Size = new Size(200, 30),
                    MaxLength = 10,
                    Font = new Font("Consolas", 12)
                };

                var btnGenerate = new Button
                {
                    Text = "随机生成",
                    Location = new Point(340, 140),
                    Size = new Size(70, 30),
                    FlatStyle = FlatStyle.Flat
                };
                btnGenerate.Click += (s, e) =>
                {
                    Random rand = new Random();
                    txtNew.Text = rand.Next(100000, 999999).ToString();
                };

                var lblHint = new Label
                {
                    Text = "配对码要求：3-10位字母或数字",
                    Location = new Point(130, 175),
                    Size = new Size(250, 20),
                    ForeColor = Color.Gray,
                    Font = new Font("微软雅黑", 9)
                };

                var btnOK = new Button
                {
                    Text = "确定",
                    Location = new Point(250, 210),
                    Size = new Size(80, 35),
                    FlatStyle = FlatStyle.Flat,
                    BackColor = Color.FromArgb(0, 120, 215),
                    ForeColor = Color.White,
                    DialogResult = DialogResult.OK
                };

                var btnCancel = new Button
                {
                    Text = "取消",
                    Location = new Point(340, 210),
                    Size = new Size(80, 35),
                    FlatStyle = FlatStyle.Flat,
                    DialogResult = DialogResult.Cancel
                };

                form.Controls.AddRange(new Control[] {
                    lblTitle, lblDesc, lblCurrent, txtCurrent, btnCopy,
                    lblNew, txtNew, btnGenerate, lblHint, btnOK, btnCancel
                });

                form.AcceptButton = btnOK;
                form.CancelButton = btnCancel;

                btnOK.Enabled = false;
                txtNew.TextChanged += (s, e) =>
                {
                    btnOK.Enabled = IsValidPairingCode(txtNew.Text);
                };

                DialogResult result = form.ShowDialog(owner ?? parentForm);

                if (result == DialogResult.OK && !string.IsNullOrWhiteSpace(txtNew.Text))
                {
                    SavePairingCode(txtNew.Text.Trim());
                }

                return result;
            }
        }

        public void SetupTrayIcon(NotifyIcon existingTrayIcon = null)
        {
            if (existingTrayIcon != null)
            {
                trayIcon = existingTrayIcon;
            }
            else
            {
                trayIcon = new NotifyIcon();
            }

            trayMenu = new ContextMenuStrip();

            var lblPairingCode = new ToolStripLabel($"配对码: {_pairingCode}")
            {
                Font = new Font(trayMenu.Font, FontStyle.Bold),
                ForeColor = Color.Blue
            };
            trayMenu.Items.Add(lblPairingCode);
            trayMenu.Items.Add(new ToolStripSeparator());

            var menuShowWindow = new ToolStripMenuItem("显示主窗口");
            menuShowWindow.Click += (s, e) => ShowMainWindow();
            trayMenu.Items.Add(menuShowWindow);

            var menuStart = new ToolStripMenuItem("启动服务");
            menuStart.Click += (s, e) => Start();
            trayMenu.Items.Add(menuStart);

            var menuStop = new ToolStripMenuItem("停止服务");
            menuStop.Click += (s, e) => Stop();
            trayMenu.Items.Add(menuStop);

            trayMenu.Items.Add(new ToolStripSeparator());

            var menuSetPairing = new ToolStripMenuItem("设置配对码");
            menuSetPairing.Click += (s, e) =>
            {
                ShowPairingCodeSettings();
                lblPairingCode.Text = $"配对码: {_pairingCode}";
            };
            trayMenu.Items.Add(menuSetPairing);

            var menuRandomPairing = new ToolStripMenuItem("随机生成配对码");
            menuRandomPairing.Click += (s, e) =>
            {
                GenerateRandomPairingCode();
                SavePairingCodeToConfig();
                lblPairingCode.Text = $"配对码: {_pairingCode}";
                LogMessage($"🎲 随机生成新配对码: {_pairingCode}");

                if (isRunning)
                {
                    RestartBroadcastService();
                }
            };
            trayMenu.Items.Add(menuRandomPairing);

            var menuCopyPairing = new ToolStripMenuItem("复制配对码");
            menuCopyPairing.Click += (s, e) =>
            {
                Clipboard.SetText(_pairingCode);
                LogMessage($"📋 配对码已复制到剪贴板: {_pairingCode}");
                trayIcon.ShowBalloonTip(3000, "冷库宝", "配对码已复制到剪贴板", ToolTipIcon.Info);
            };
            trayMenu.Items.Add(menuCopyPairing);

            trayMenu.Items.Add(new ToolStripSeparator());

            var menuExit = new ToolStripMenuItem("退出");
            menuExit.Click += (s, e) => Application.Exit();
            trayMenu.Items.Add(menuExit);

            trayIcon.ContextMenuStrip = trayMenu;
            trayIcon.DoubleClick += (s, e) => ShowMainWindow();

            this.OnPairingCodeChanged += (sender, code) =>
            {
                if (lblPairingCode != null)
                {
                    lblPairingCode.Text = $"配对码: {code}";
                }
            };
        }

        private void ShowMainWindow()
        {
            if (parentForm is Form1 mainForm)
            {
                mainForm.RestoreAndActivate();
            }
        }

        private void UpdateTrayMenuDisplay()
        {
            if (trayMenu != null && trayMenu.Items.Count > 0)
            {
                var firstItem = trayMenu.Items[0] as ToolStripLabel;
                if (firstItem != null)
                {
                    firstItem.Text = $"配对码: {_pairingCode}";
                }
            }
        }

        public Control CreatePairingCodeControl()
        {
            var groupBox = new GroupBox
            {
                Text = "配对码设置",
                Size = new Size(400, 100),
                BackColor = Color.White
            };

            var lblCode = new Label
            {
                Text = "当前配对码:",
                Location = new Point(15, 30),
                Size = new Size(80, 25)
            };

            var txtCode = new TextBox
            {
                Text = _pairingCode,
                Location = new Point(100, 28),
                Size = new Size(150, 25),
                ReadOnly = true,
                BackColor = Color.White,
                Font = new Font("Consolas", 10, FontStyle.Bold)
            };

            var btnChange = new Button
            {
                Text = "更改",
                Location = new Point(260, 28),
                Size = new Size(60, 25),
                FlatStyle = FlatStyle.Flat
            };

            var btnCopy = new Button
            {
                Text = "复制",
                Location = new Point(330, 28),
                Size = new Size(50, 25),
                FlatStyle = FlatStyle.Flat
            };

            btnChange.Click += (s, e) =>
            {
                ShowPairingCodeSettings(groupBox.FindForm());
                txtCode.Text = _pairingCode;
            };

            btnCopy.Click += (s, e) =>
            {
                Clipboard.SetText(_pairingCode);
                MessageBox.Show("配对码已复制到剪贴板", "提示",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            };

            groupBox.Controls.AddRange(new Control[] { lblCode, txtCode, btnChange, btnCopy });

            this.OnPairingCodeChanged += (sender, code) =>
            {
                if (txtCode.InvokeRequired)
                {
                    txtCode.BeginInvoke(new Action(() => txtCode.Text = code));
                }
                else
                {
                    txtCode.Text = code;
                }
            };

            return groupBox;
        }

        public void AddToForm(Control parent, Point location)
        {
            if (parent == null) return;
            var control = CreatePairingCodeControl();
            control.Location = location;
            parent.Controls.Add(control);
        }

        public void ShowPairingCodeQRCode()
        {
            try
            {
                string qrContent = $"lengkubao://pair?code={_pairingCode}&name={Environment.MachineName}&port={tcpPort}";
                LogMessage($"📱 配对码二维码内容: {qrContent}");
                LogMessage($"💡 提示: 可使用二维码库生成图片显示");
            }
            catch (Exception ex)
            {
                LogMessage($"⚠️ 生成二维码失败: {ex.Message}");
            }
        }

        private void RestartBroadcastService()
        {
            try
            {
                if (isRunning)
                {
                    discoveryService?.UpdatePairingCode(_pairingCode, tcpPort);
                    LogMessage($"🔄 发现服务已重启，使用新配对码: {_pairingCode}");
                    trayIcon?.ShowBalloonTip(3000, "冷库宝", $"配对码已更新为: {_pairingCode}", ToolTipIcon.Info);
                }
            }
            catch (Exception ex)
            {
                LogMessage($"❌ 重启发现服务失败: {ex.Message}");
            }
        }

        public void BroadcastPairingCode()
        {
            discoveryService?.BroadcastPairingAnnounce();
        }

        public string GetPairingCode()
        {
            return _pairingCode;
        }

        public int GetTcpPort() => tcpPort;

        public Bitmap GetPairingQrBitmap(int pixelsPerModule = 4)
        {
            return PairingQrService.CreateQrBitmap(_pairingCode, tcpPort, pixelsPerModule);
        }
        #endregion

        #region 服务启动/停止
        public void Start()
        {
            try
            {
                if (isRunning) return;
                isRunning = true;

                mainThread = new Thread(() =>
                {
                    if (useTouchSocketTransport)
                        StartTouchSocketServer();
                    else
                        StartTcpServer();
                    discoveryService.Start(_pairingCode, tcpPort);
                    TryUPnPPortMapping();
                    StartDatabaseMonitor();
                    StartHeartbeatCheck();
                    TryInitializeCrsql();

                    LogMessage("✅ 自动同步服务器已全面启动");
                    LogMessage($"📱 服务器信息：");
                    LogMessage($"   - 计算机名：{Environment.MachineName}");
                    LogMessage($"   - IP地址：{string.Join(", ", GetLocalIPAddresses())}");
                    LogMessage($"   - TCP端口：{tcpPort}");
                    LogMessage($"   - 广播端口：{broadcastPort}");
                    LogMessage($"   - 配对码：{_pairingCode} 🔐");

                    trayIcon?.ShowBalloonTip(5000, "冷库宝", $"服务器已启动\n配对码: {_pairingCode}", ToolTipIcon.Info);
                });
                mainThread.IsBackground = true;
                mainThread.Start();

                PublishPendingConfigDeltaSnapshot();
                PublishConnectedDevicesSnapshot();
                ShowConnectionQRCode();
            }
            catch (Exception ex)
            {
                LogMessage($"❌ 启动失败: {ex.Message}");
                Stop();
            }
        }

        public void Stop()
        {
            isRunning = false;

            try
            {
                foreach (var device in connectedDevices.Values)
                {
                    device.Close();
                }
                connectedDevices.Clear();
                PublishConnectedDevicesSnapshot();

                tcpListener?.Stop();
                discoveryService?.Stop();
                syncTransport?.Stop();
                dbWatcher?.Dispose();

                LogMessage("🛑 自动同步服务器已停止");
                trayIcon?.ShowBalloonTip(3000, "冷库宝", "服务器已停止", ToolTipIcon.Info);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"停止服务器错误: {ex.Message}");
            }
        }
        #endregion

        private void StartTouchSocketServer()
        {
            try
            {
                syncTransport.Start(tcpPort);
                discoveryService.BroadcastServerReady();
                LogMessage($"📡 TouchSocket 同步服务启动，端口：{tcpPort}");
            }
            catch (Exception ex)
            {
                LogMessage($"❌ TouchSocket 服务启动失败，回退 TcpListener: {ex.Message}");
                StartTcpServer();
            }
        }

        private void TryInitializeCrsql()
        {
            try
            {
                var db = new DatabaseManager();
                using (var conn = new System.Data.SQLite.SQLiteConnection(db.ConnectionString))
                {
                    conn.Open();
                    crsqlEngine.TryInitialize(conn);
                }
            }
            catch (Exception ex)
            {
                LogMessage($"ℹ️ cr-sqlite 初始化跳过: {ex.Message}");
            }
        }

        private void RemoveStaleTransportMappingsForDevice(string deviceId, string exceptTransportId)
        {
            if (string.IsNullOrWhiteSpace(deviceId)) return;

            foreach (var kv in transportToDeviceId.ToArray())
            {
                if (!string.Equals(kv.Value, deviceId, StringComparison.Ordinal)) continue;
                if (!string.IsNullOrWhiteSpace(exceptTransportId) &&
                    string.Equals(kv.Key, exceptTransportId, StringComparison.Ordinal))
                {
                    continue;
                }

                transportToDeviceId.TryRemove(kv.Key, out _);
                transportRegistered.TryRemove(kv.Key, out _);
            }
        }

        private async Task<string> HandleTouchSocketLineAsync(string transportId, string remoteIp, string line)
        {
            if (string.IsNullOrWhiteSpace(line)) return null;

            if (!transportRegistered.TryGetValue(transportId, out bool registered) || !registered)
            {
                if (!line.StartsWith("REGISTER|"))
                    return "REGISTER_FAIL|请先发送 REGISTER";

                string registerResponse = TryRegisterTouchSocketClient(transportId, remoteIp, line);
                if (registerResponse != null)
                    transportRegistered[transportId] = registerResponse.StartsWith("REGISTER_OK");
                return registerResponse;
            }

            if (!transportToDeviceId.TryGetValue(transportId, out string deviceId) ||
                !connectedDevices.TryGetValue(deviceId, out DeviceClient deviceClient))
            {
                transportRegistered.TryRemove(transportId, out _);
                return "REGISTER_FAIL|会话无效，请重新 REGISTER";
            }

            deviceClient.LastHeartbeat = DateTime.Now;
            return await Task.FromResult(HandleDeviceMessage(deviceClient, line)).ConfigureAwait(false);
        }

        private string TryRegisterTouchSocketClient(string transportId, string remoteIp, string registerMsg)
        {
            string[] parts = registerMsg.Split('|');
            if (parts.Length < 4)
                return "REGISTER_FAIL|消息格式错误";

            string deviceId = parts[1];
            string deviceName = parts[2];
            string devicePairingCode = parts[3];

            if (devicePairingCode != _pairingCode)
            {
                LogMessage($"❌ TouchSocket 配对码失败：设备={devicePairingCode} 服务器={_pairingCode}");
                return "REGISTER_FAIL|配对码不匹配";
            }

            RemoveStaleTransportMappingsForDevice(deviceId, transportId);

            if (connectedDevices.TryGetValue(deviceId, out var existingClient) &&
                !string.Equals(existingClient.TransportClientId, transportId, StringComparison.Ordinal))
            {
                existingClient.Close();
                connectedDevices.TryRemove(deviceId, out _);
            }

            var deviceClient = new DeviceClient
            {
                DeviceId = deviceId,
                DeviceName = deviceName,
                DeviceType = parts.Length > 4 ? parts[4] : "手持端",
                IpAddress = remoteIp,
                TransportClientId = transportId,
                AsyncSend = msg => syncTransport.SendAsync(transportId, msg),
                LastHeartbeat = DateTime.Now,
                ConnectedAt = DateTime.Now,
                IsOnline = true,
                PairingCode = devicePairingCode
            };

            connectedDevices[deviceId] = deviceClient;
            transportToDeviceId[transportId] = deviceId;
            UpsertKnownDevice(deviceId, deviceName, deviceClient.DeviceType, remoteIp);
            PublishConnectedDevicesSnapshot();

            string activeYearSuffix = FiscalYearService.IsInitialized
                ? $"|active_year={FiscalYearService.ActiveYear}"
                : "";
            string registerOkMsg = $"REGISTER_OK|{Environment.MachineName}|欢迎使用冷库宝|{_pairingCode}{activeYearSuffix}";
            LogMessage($"✅ TouchSocket 设备注册: {deviceName} ({deviceId})");
            PushPendingData(deviceClient);
            return registerOkMsg;
        }

        private string HandleSyncEngineLegacyLine(string transportClientId, string line)
        {
            if (!transportToDeviceId.TryGetValue(transportClientId, out string deviceId) ||
                !connectedDevices.TryGetValue(deviceId, out DeviceClient device))
                return null;

            return HandleDeviceMessage(device, line);
        }

        private (bool ok, string error) HandleUnifiedPush(string transportClientId, string opId, string type, string payloadJson)
        {
            string deviceId = transportClientId;
            if (transportToDeviceId.TryGetValue(transportClientId, out string mapped))
                deviceId = mapped;

            if (!connectedDevices.TryGetValue(deviceId, out DeviceClient device))
                return (false, "device offline");

            try
            {
                string legacyLine = type + "|" + payloadJson;
                var sem = GetDeviceSyncLock(device.DeviceId);
                sem.Wait();
                try
                {
                    DatabaseManager.SuppressLocalConfigSyncNotifications = true;
                    HandleSyncDataWithAckCore(legacyLine, device, sendLegacyAck: false);
                }
                finally
                {
                    DatabaseManager.SuppressLocalConfigSyncNotifications = false;
                    sem.Release();
                }
                return (true, null);
            }
            catch (Exception ex)
            {
                return (false, ex.Message);
            }
        }

        private string HandleDeviceMessage(DeviceClient deviceClient, string message)
        {
            LogMessage($"📥 收到消息: {message}");

            if (message.StartsWith("PING|"))
            {
                SendMessageSafely(deviceClient, "PONG|" + DateTime.Now.Ticks);
                return null;
            }

            if (message == "HEARTBEAT")
            {
                SendMessageSafely(deviceClient, "HEARTBEAT_OK");
                return null;
            }

            if (message.StartsWith("PUSH|"))
                return syncEngine.HandleLine(deviceClient.TransportClientId ?? deviceClient.DeviceId, deviceClient.IpAddress, message);

            if (message.StartsWith("CONFIG_PULL|") || message.StartsWith("CONFIG_PUSH|"))
                return syncEngine.HandleLine(deviceClient.TransportClientId ?? deviceClient.DeviceId, deviceClient.IpAddress, message);

            if (message.StartsWith("SYNC|"))
            {
                HandleSyncData(message.Substring(5), deviceClient);
                return null;
            }
            if (message.StartsWith("HELLO_SYNC|"))
            {
                HandleHelloSync(message.Substring("HELLO_SYNC|".Length), deviceClient);
                return null;
            }
            if (message.StartsWith("PUSH_CHANGES|"))
            {
                HandlePushChanges(message.Substring("PUSH_CHANGES|".Length), deviceClient);
                return null;
            }
            if (message.StartsWith("PULL_DELTA_REQ|"))
            {
                HandlePullDeltaReq(message.Substring("PULL_DELTA_REQ|".Length), deviceClient);
                return null;
            }
            if (message.StartsWith("DELTA_APPLY_ACK|"))
            {
                HandleDeltaApplyAck(message.Substring("DELTA_APPLY_ACK|".Length), deviceClient);
                return null;
            }
            if (message == "REQUEST_PENDING")
            {
                PushPendingData(deviceClient);
                return null;
            }
            if (message == "PULL_FULL_SYNC")
            {
                var devForPull = deviceClient;
                _ = Task.Run(async () =>
                {
                    var sem = GetDeviceSyncLock(devForPull.DeviceId);
                    await sem.WaitAsync().ConfigureAwait(false);
                    try { await HandlePullFullSync(devForPull).ConfigureAwait(false); }
                    finally { sem.Release(); }
                });
                return null;
            }
            if (message.StartsWith("INBOUND|") || message.StartsWith("SALES|") || message.StartsWith("PACKAGING|") ||
                message.StartsWith("ADVANCE|") || message.StartsWith("DEDUCTION|") ||
                message.StartsWith("PRESALE_OUTBOUND|") || message.StartsWith("PRESALE_PAYMENT|") ||
                message.StartsWith("PRESALE|") || message.StartsWith("LEDGER|") ||
                message.StartsWith("CUSTOMER|") || message.StartsWith("PRODUCT|") ||
                message.StartsWith("LOCATION|") || message.StartsWith("OPERATOR|"))
            {
                var devForSync = deviceClient;
                var syncMessage = message;
                _ = Task.Run(async () =>
                {
                    var sem = GetDeviceSyncLock(devForSync.DeviceId);
                    await sem.WaitAsync().ConfigureAwait(false);
                    try { HandleSyncDataWithAckCore(syncMessage, devForSync); }
                    finally { sem.Release(); }
                });
                return null;
            }
            if (message == "UPLOAD_COMPLETE")
            {
                LogMessage($"ℹ️ 手持端上报上传完成: {deviceClient.DeviceName}");
                return null;
            }
            if (message.StartsWith("REGISTER|"))
            {
                string registerOkMsg = $"REGISTER_OK|{Environment.MachineName}|欢迎使用冷库宝|{_pairingCode}" +
                    (FiscalYearService.IsInitialized ? $"|active_year={FiscalYearService.ActiveYear}" : "");
                return registerOkMsg;
            }

            LogMessage($"⚠️ 未知消息类型: {message}");
            return null;
        }

        #region 服务组件
        private void StartTcpServer()
        {
            try
            {
                tcpListener = new TcpListener(IPAddress.Any, tcpPort);
                tcpListener.Start();
                LogMessage($"📡 TCP同步服务启动，端口：{tcpPort}");
                BroadcastServerReady();

                Thread tcpThread = new Thread(() =>
                {
                    while (isRunning)
                    {
                        try
                        {
                            var client = tcpListener.AcceptTcpClient();
                            Task.Run(() => HandleTcpClient(client));
                        }
                        catch (Exception ex)
                        {
                            if (isRunning) LogMessage($"⚠️ TCP接受连接错误: {ex.Message}");
                        }
                    }
                });
                tcpThread.IsBackground = true;
                tcpThread.Start();
            }
            catch (Exception ex)
            {
                LogMessage($"❌ TCP服务启动失败: {ex.Message}");
            }
        }

        private void StartBroadcastDiscovery()
        {
            try
            {
                broadcastListener = new UdpClient(broadcastPort);
                LogMessage($"📢 UDP广播发现服务启动，端口：{broadcastPort}，配对码：{_pairingCode}");

                Thread broadcastThread = new Thread(() =>
                {
                    while (isRunning)
                    {
                        try
                        {
                            IPEndPoint clientEndPoint = new IPEndPoint(IPAddress.Any, 0);
                            byte[] data = broadcastListener.Receive(ref clientEndPoint);
                            string message = Encoding.UTF8.GetString(data);

                            LogMessage($"📥 收到UDP广播: {message} 来自 {clientEndPoint.Address}");

                            // 处理设备发现请求
                            if (message.StartsWith("DISCOVER_LENGKUBAO"))
                            {
                                string[] parts = message.Split('|');
                                if (parts.Length >= 2)
                                {
                                    string deviceId = parts[1];
                                    string deviceName = parts.Length > 2 ? parts[2] : "未知设备";

                                    LogMessage($"🔍 设备发现请求: {deviceName} ({deviceId})");

                                    // 响应服务器信息，包含配对码
                                    string response = $"LENGKUBAO_SERVER|{Environment.MachineName}|{_pairingCode}|{tcpPort}";
                                    byte[] responseData = Encoding.UTF8.GetBytes(response);
                                    broadcastListener.Send(responseData, responseData.Length, clientEndPoint);

                                    LogMessage($"📤 响应发现请求，发送配对码: {_pairingCode}");
                                }
                            }
                            // 处理配对码验证请求（通过UDP快速验证，但实际同步仍用TCP）
                            else if (message.StartsWith("PAIRING_REQUEST|"))
                            {
                                string[] parts = message.Split('|');
                                if (parts.Length >= 3)
                                {
                                    string deviceId = parts[1];
                                    string devicePairingCode = parts[2];

                                    if (devicePairingCode == _pairingCode)
                                    {
                                        LogMessage($"✅ UDP配对验证成功: 设备 {deviceId} 配对码匹配");
                                        string response = $"PAIRING_ACCEPTED|{Environment.MachineName}|欢迎连接";
                                        byte[] responseData = Encoding.UTF8.GetBytes(response);
                                        broadcastListener.Send(responseData, responseData.Length, clientEndPoint);
                                    }
                                    else
                                    {
                                        LogMessage($"❌ UDP配对验证失败: 设备配对码={devicePairingCode}，服务器配对码={_pairingCode}");
                                        string response = $"PAIRING_REJECTED|配对码错误";
                                        byte[] responseData = Encoding.UTF8.GetBytes(response);
                                        broadcastListener.Send(responseData, responseData.Length, clientEndPoint);
                                    }
                                }
                            }
                            // 兼容旧的mDNS查询（可选，用于向后兼容）
                            else if (message.StartsWith("MDNS_QUERY"))
                            {
                                string[] parts = message.Split('|');
                                string queryPairingCode = parts.Length > 1 ? parts[1] : "";

                                if (queryPairingCode == _pairingCode || string.IsNullOrEmpty(queryPairingCode))
                                {
                                    string response = $"MDNS_RESPONSE|{Environment.MachineName}|{string.Join(",", GetLocalIPAddresses())}|{tcpPort}|{_pairingCode}";
                                    byte[] responseData = Encoding.UTF8.GetBytes(response);
                                    broadcastListener.Send(responseData, responseData.Length, clientEndPoint);
                                    LogMessage($"🍎 响应mDNS查询，配对码: {_pairingCode}");
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            if (isRunning) LogMessage($"⚠️ UDP广播监听错误: {ex.Message}");
                        }
                    }
                });
                broadcastThread.IsBackground = true;
                broadcastThread.Start();

                // 额外启动一个线程定期广播服务器存在（可选）
                StartPeriodicBroadcast();
            }
            catch (Exception ex)
            {
                LogMessage($"❌ UDP广播服务启动失败: {ex.Message}");
            }
        }
        /// <summary>向各网卡所在子网广播 UDP，避免多网卡时只从虚拟网卡发出。</summary>
        private void BroadcastUdpOnAllInterfaces(string message, string logPrefix)
        {
            byte[] data = Encoding.UTF8.GetBytes(message);
            foreach (NetworkInterface nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up) continue;
                if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;

                foreach (UnicastIPAddressInformation addr in nic.GetIPProperties().UnicastAddresses)
                {
                    if (addr.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                    IPAddress mask = addr.IPv4Mask;
                    if (mask == null) continue;

                    byte[] ipBytes = addr.Address.GetAddressBytes();
                    byte[] maskBytes = mask.GetAddressBytes();
                    byte[] broadcastBytes = new byte[4];
                    for (int i = 0; i < 4; i++)
                    {
                        broadcastBytes[i] = (byte)(ipBytes[i] | (maskBytes[i] ^ 255));
                    }

                    IPAddress broadcastAddr = new IPAddress(broadcastBytes);
                    try
                    {
                        using (UdpClient udpClient = new UdpClient(new IPEndPoint(addr.Address, 0)))
                        {
                            udpClient.EnableBroadcast = true;
                            udpClient.Send(data, data.Length, new IPEndPoint(broadcastAddr, broadcastPort));
                            LogMessage($"{logPrefix} -> {broadcastAddr} (本机 {addr.Address})");
                        }
                    }
                    catch (Exception ex)
                    {
                        LogMessage($"⚠️ UDP广播失败 ({addr.Address}): {ex.Message}");
                    }
                }
            }
        }

        /// <summary>TCP 就绪后立即广播，便于手持端首次连接快速发现。</summary>
        private void BroadcastServerReady()
        {
            Thread announceThread = new Thread(() =>
            {
                string message = $"LENGKUBAO_SERVER_ANNOUNCE|{Environment.MachineName}|{_pairingCode}|{tcpPort}";
                for (int i = 0; i < 3 && isRunning; i++)
                {
                    try
                    {
                        BroadcastUdpOnAllInterfaces(message, $"📢 服务就绪广播 ({i + 1}/3)");
                    }
                    catch (Exception ex)
                    {
                        LogMessage($"⚠️ 服务就绪广播失败: {ex.Message}");
                    }

                    if (i < 2)
                    {
                        Thread.Sleep(300);
                    }
                }
            });
            announceThread.IsBackground = true;
            announceThread.Start();
        }

        // 添加定期广播服务器存在的方法（可选，帮助设备更快发现）
        private void StartPeriodicBroadcast()
        {
            Thread broadcastAnnounceThread = new Thread(() =>
            {
                while (isRunning)
                {
                    try
                    {
                        string message = $"LENGKUBAO_SERVER_ANNOUNCE|{Environment.MachineName}|{_pairingCode}|{tcpPort}";
                        BroadcastUdpOnAllInterfaces(message, "📢 广播服务器存在");
                    }
                    catch (Exception ex)
                    {
                        LogMessage($"⚠️ 定期广播失败: {ex.Message}");
                    }

                    // 每30秒广播一次
                    for (int i = 0; i < 30 && isRunning; i++)
                    {
                        Thread.Sleep(1000);
                    }
                }
            });
            broadcastAnnounceThread.IsBackground = true;
            broadcastAnnounceThread.Start();
        }
        private void TryUPnPPortMapping()
        {
            try
            {
                LogMessage($"🌐 尝试UPnP端口映射...");
            }
            catch (Exception ex)
            {
                LogMessage($"⚠️ UPnP端口映射失败: {ex.Message}");
            }
        }

        private void ShowConnectionQRCode()
        {
            try
            {
                string connectionInfo = $"{Environment.MachineName}|{string.Join(",", GetLocalIPAddresses())}|{tcpPort}|{_pairingCode}";
                LogMessage($"📱 扫码连接（配对码：{_pairingCode}）：{connectionInfo}");
            }
            catch (Exception ex)
            {
                LogMessage($"⚠️ 生成二维码失败: {ex.Message}");
            }
        }

        private void StartDatabaseMonitor()
        {
            try
            {
                string dbPath = DatabaseManager.GetDatabaseFilePath();
                string dbDir = Path.GetDirectoryName(dbPath);

                if (!string.IsNullOrEmpty(dbDir) && Directory.Exists(dbDir))
                {
                    dbWatcher = new FileSystemWatcher(dbDir, Path.GetFileName(dbPath));
                    dbWatcher.NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size;
                    dbWatcher.Changed += OnDatabaseChanged;
                    dbWatcher.EnableRaisingEvents = true;
                    LogMessage($"💾 数据库监控已启动：{dbPath}");
                }
            }
            catch (Exception ex)
            {
                LogMessage($"⚠️ 数据库监控启动失败: {ex.Message}");
            }
        }

        public void RestartDatabaseMonitor()
        {
            try
            {
                dbWatcher?.Dispose();
                dbWatcher = null;
                if (isRunning)
                    StartDatabaseMonitor();
            }
            catch (Exception ex)
            {
                LogMessage($"⚠️ 重启数据库监控失败: {ex.Message}");
            }
        }

        private void OnDatabaseChanged(object sender, FileSystemEventArgs e)
        {
            lock (dbChangeDebounceLock)
            {
                var now = DateTime.UtcNow;
                if ((now - lastDbChangeNotifyUtc).TotalSeconds < 3)
                    return;
                lastDbChangeNotifyUtc = now;
            }

            LogMessage($"🔄 检测到数据库变化，准备通知手持端拉取增量...");
            Task.Run(() =>
            {
                Thread.Sleep(500);
                NotifyDevicesDeltaAvailable(null);
            });
        }

        private void StartHeartbeatCheck()
        {
            Thread heartbeatThread = new Thread(() =>
            {
                while (isRunning)
                {
                    Thread.Sleep(30000);

                    var offlineDevices = new List<string>();
                    foreach (var device in connectedDevices.Values)
                    {
                        if (DateTime.Now - device.LastHeartbeat > TimeSpan.FromMinutes(1))
                        {
                            device.IsOnline = false;
                            offlineDevices.Add(device.DeviceId);
                            LogMessage($"💔 设备离线：{device.DeviceName} ({device.DeviceId})");
                        }
                    }

                    foreach (var deviceId in offlineDevices)
                    {
                        if (connectedDevices.TryRemove(deviceId, out var removed))
                            removed.Close();
                    }

                    if (offlineDevices.Count > 0)
                        PublishConnectedDevicesSnapshot();
                }
            });
            heartbeatThread.IsBackground = true;
            heartbeatThread.Start();
        }

        public void RefreshKnownDeviceRegistry()
        {
            PublishConnectedDevicesSnapshot();
        }

        private void PublishConnectedDevicesSnapshot()
        {
            try
            {
                var list = BuildDeviceRegistrySnapshot();
                OnConnectedDevicesChanged?.Invoke(this, new ConnectedDevicesChangedEventArgs(list));
                PublishPendingConfigDeltaSnapshot();
            }
            catch (Exception ex)
            {
                LogMessage($"⚠️ 发布连接快照失败: {ex.Message}");
            }
        }

        private static string BuildDeviceIdentityKey(string deviceName, string ipAddress)
        {
            string name = (deviceName ?? "").Trim();
            string ip = (ipAddress ?? "").Trim();
            if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(ip))
                return null;
            return name.ToUpperInvariant() + "|" + ip;
        }

        private static bool DeviceIdentityMatches(string nameA, string ipA, string nameB, string ipB)
        {
            string keyA = BuildDeviceIdentityKey(nameA, ipA);
            string keyB = BuildDeviceIdentityKey(nameB, ipB);
            return keyA != null && keyA == keyB;
        }

        private void MergeDeviceSyncState(string fromDeviceId, string toDeviceId)
        {
            if (string.IsNullOrWhiteSpace(fromDeviceId) || string.IsNullOrWhiteSpace(toDeviceId)) return;
            if (string.Equals(fromDeviceId, toDeviceId, StringComparison.Ordinal)) return;

            if (deviceLastAckSeq.TryGetValue(fromDeviceId, out long fromAck))
            {
                deviceLastAckSeq.AddOrUpdate(toDeviceId, fromAck, (_, cur) => Math.Max(cur, fromAck));
            }
            if (deviceFirstFullSyncDone.TryGetValue(fromDeviceId, out bool fromFirst))
            {
                deviceFirstFullSyncDone.AddOrUpdate(toDeviceId, fromFirst, (_, cur) => cur || fromFirst);
            }
        }

        private bool NormalizeKnownDevicesByIdentity()
        {
            if (knownDevices.IsEmpty) return false;

            var normalized = new Dictionary<string, KnownDeviceRecord>(StringComparer.OrdinalIgnoreCase);
            bool changed = false;

            foreach (var kv in knownDevices.ToArray())
            {
                var rec = kv.Value;
                if (string.IsNullOrWhiteSpace(rec.DeviceId))
                {
                    rec.DeviceId = kv.Key;
                    changed = true;
                }

                string storageKey = BuildDeviceIdentityKey(rec.DeviceName, rec.LastIpAddress) ?? kv.Key;
                if (!string.Equals(storageKey, kv.Key, StringComparison.Ordinal))
                    changed = true;

                if (normalized.TryGetValue(storageKey, out var existing))
                {
                    changed = true;
                    MergeDeviceSyncState(rec.DeviceId, existing.DeviceId);
                    MergeDeviceSyncState(existing.DeviceId, rec.DeviceId);
                    if (rec.LastConnectedAt > existing.LastConnectedAt)
                    {
                        MergeDeviceSyncState(existing.DeviceId, rec.DeviceId);
                        existing.DeviceId = rec.DeviceId;
                        existing.LastConnectedAt = rec.LastConnectedAt;
                    }
                    if (rec.LastSeenAt > existing.LastSeenAt) existing.LastSeenAt = rec.LastSeenAt;
                    if (rec.FirstRegisteredAt != default &&
                        (existing.FirstRegisteredAt == default || rec.FirstRegisteredAt < existing.FirstRegisteredAt))
                    {
                        existing.FirstRegisteredAt = rec.FirstRegisteredAt;
                    }
                    if (string.IsNullOrWhiteSpace(existing.DeviceName)) existing.DeviceName = rec.DeviceName;
                    if (string.IsNullOrWhiteSpace(existing.LastIpAddress)) existing.LastIpAddress = rec.LastIpAddress;
                    if (string.IsNullOrWhiteSpace(existing.DeviceType)) existing.DeviceType = rec.DeviceType;
                }
                else
                {
                    normalized[storageKey] = rec;
                }
            }

            if (!changed) return false;

            knownDevices.Clear();
            foreach (var kv in normalized)
                knownDevices[kv.Key] = kv.Value;
            return true;
        }

        private DeviceClient FindLiveDeviceForRecord(KnownDeviceRecord record)
        {
            DeviceClient idMatch = null;
            foreach (var pair in connectedDevices)
            {
                var dev = pair.Value;
                if (!IsDeviceChannelReady(dev)) continue;
                if (DeviceIdentityMatches(dev.DeviceName, dev.IpAddress, record.DeviceName, record.LastIpAddress))
                    return dev;
                if (string.Equals(dev.DeviceId, record.DeviceId, StringComparison.Ordinal))
                    idMatch = dev;
            }
            return idMatch;
        }

        private long GetEffectiveLastAckSeq(KnownDeviceRecord record, string preferredDeviceId = null)
        {
            string primary = preferredDeviceId ?? record.DeviceId;
            long ack = deviceLastAckSeq.GetOrAdd(primary ?? "", 0);
            if (!string.IsNullOrWhiteSpace(record.DeviceId) &&
                !string.Equals(primary, record.DeviceId, StringComparison.Ordinal))
            {
                ack = Math.Max(ack, deviceLastAckSeq.GetOrAdd(record.DeviceId, 0));
            }
            return ack;
        }

        private bool IsDeviceFirstFullSyncDoneForRecord(KnownDeviceRecord record, string preferredDeviceId = null)
        {
            string primary = preferredDeviceId ?? record.DeviceId;
            if (IsDeviceFirstFullSyncDone(primary)) return true;
            if (!string.IsNullOrWhiteSpace(record.DeviceId) &&
                !string.Equals(primary, record.DeviceId, StringComparison.Ordinal))
            {
                return IsDeviceFirstFullSyncDone(record.DeviceId);
            }
            return false;
        }

        private int GetConfigLagCountForAck(long lastAck)
        {
            lock (deltaSyncLock)
            {
                return syncChangeLog.Count(x =>
                    IsConfigEntityType(x.EntityType) && x.CommitSeq > lastAck);
            }
        }

        private string BuildDeviceSyncStatusLabel(KnownDeviceRecord record, string activeDeviceId = null)
        {
            string deviceId = activeDeviceId ?? record.DeviceId;
            long lastAck = GetEffectiveLastAckSeq(record, deviceId);
            if (!IsDeviceFirstFullSyncDoneForRecord(record, deviceId) && lastAck <= 0)
                return "待首次全量";
            int lag = GetConfigLagCountForAck(lastAck);
            return lag > 0 ? $"落后 {lag} 条" : "已同步";
        }

        private void UpsertKnownDevice(string deviceId, string deviceName, string deviceType, string ipAddress)
        {
            if (string.IsNullOrWhiteSpace(deviceId)) return;
            var now = DateTime.Now;
            string resolvedName = string.IsNullOrWhiteSpace(deviceName) ? deviceId : deviceName.Trim();
            string resolvedIp = ipAddress ?? "";
            string storageKey = BuildDeviceIdentityKey(resolvedName, resolvedIp) ?? deviceId;

            string oldDeviceId = null;
            foreach (var legacyKey in knownDevices.Keys.ToArray())
            {
                if (string.Equals(legacyKey, storageKey, StringComparison.OrdinalIgnoreCase)) continue;
                if (!knownDevices.TryGetValue(legacyKey, out var legacyRecord)) continue;
                if (!DeviceIdentityMatches(legacyRecord.DeviceName, legacyRecord.LastIpAddress, resolvedName, resolvedIp))
                    continue;

                oldDeviceId = oldDeviceId ?? legacyRecord.DeviceId;
                MergeDeviceSyncState(legacyRecord.DeviceId, deviceId);
                knownDevices.TryRemove(legacyKey, out _);
            }

            knownDevices.AddOrUpdate(
                storageKey,
                _ => new KnownDeviceRecord
                {
                    DeviceId = deviceId,
                    DeviceName = resolvedName,
                    DeviceType = deviceType ?? "手持端",
                    LastIpAddress = resolvedIp,
                    FirstRegisteredAt = now,
                    LastConnectedAt = now,
                    LastSeenAt = now
                },
                (_, existing) =>
                {
                    oldDeviceId = oldDeviceId ?? existing.DeviceId;
                    existing.DeviceName = resolvedName;
                    if (!string.IsNullOrWhiteSpace(deviceType)) existing.DeviceType = deviceType;
                    if (!string.IsNullOrWhiteSpace(resolvedIp)) existing.LastIpAddress = resolvedIp;
                    existing.LastConnectedAt = now;
                    existing.LastSeenAt = now;
                    existing.DeviceId = deviceId;
                    return existing;
                });

            if (!string.IsNullOrWhiteSpace(oldDeviceId) &&
                !string.Equals(oldDeviceId, deviceId, StringComparison.Ordinal))
            {
                MergeDeviceSyncState(oldDeviceId, deviceId);
            }

            SaveDeltaSyncState();
        }

        private void TouchKnownDeviceSeen(string deviceId)
        {
            if (string.IsNullOrWhiteSpace(deviceId)) return;
            foreach (var record in knownDevices.Values)
            {
                if (string.Equals(record.DeviceId, deviceId, StringComparison.Ordinal))
                {
                    record.LastSeenAt = DateTime.Now;
                    return;
                }
            }
        }

        public string GetDeviceSyncStatusSummary()
        {
            var parts = new List<string>();
            foreach (var record in knownDevices.Values.OrderBy(x => x.DeviceName, StringComparer.OrdinalIgnoreCase))
            {
                string label = BuildDeviceSyncStatusLabel(record);
                if (label == "已同步")
                    parts.Add($"{record.DeviceName} 已同步");
                else
                    parts.Add($"{record.DeviceName} {label}");
            }
            return parts.Count == 0 ? "" : string.Join("、", parts);
        }

        private List<ConnectedDeviceInfo> BuildDeviceRegistrySnapshot()
        {
            var result = new List<ConnectedDeviceInfo>();
            foreach (var record in knownDevices.Values.OrderBy(x => x.DeviceName, StringComparer.OrdinalIgnoreCase))
            {
                var live = FindLiveDeviceForRecord(record);
                bool isConnected = live != null;
                string activeDeviceId = isConnected ? live.DeviceId : record.DeviceId;
                long lastAck = GetEffectiveLastAckSeq(record, activeDeviceId);
                int lag = GetConfigLagCountForAck(lastAck);
                string syncLabel = BuildDeviceSyncStatusLabel(record, activeDeviceId);
                result.Add(new ConnectedDeviceInfo
                {
                    DeviceId = record.DeviceId,
                    DeviceName = record.DeviceName,
                    IpAddress = isConnected ? live.IpAddress : record.LastIpAddress,
                    DeviceType = record.DeviceType,
                    ConnectedAt = record.LastConnectedAt,
                    IsOnline = isConnected,
                    IsConnected = isConnected,
                    LastAckedSeq = lastAck,
                    ConfigLagCount = lag,
                    SyncStatusLabel = syncLabel,
                    LastConnectedAt = record.LastConnectedAt
                });
            }
            return result;
        }

        private static bool IsPhantomKnownDevice(KnownDeviceRecord rec)
        {
            if (rec == null) return true;
            bool neverConnected = rec.LastConnectedAt == default || rec.LastConnectedAt == DateTime.MinValue;
            bool noIp = string.IsNullOrWhiteSpace(rec.LastIpAddress);
            bool neverSeen = rec.LastSeenAt == default || rec.LastSeenAt == DateTime.MinValue;
            return neverConnected && noIp && neverSeen;
        }

        private bool PurgePhantomKnownDevices()
        {
            bool changed = false;
            foreach (var kv in knownDevices.ToArray())
            {
                if (!IsPhantomKnownDevice(kv.Value)) continue;
                knownDevices.TryRemove(kv.Key, out _);
                changed = true;
            }
            return changed;
        }

        public bool TryRemoveKnownDevice(string deviceId)
        {
            if (string.IsNullOrWhiteSpace(deviceId)) return false;

            foreach (var pair in connectedDevices)
            {
                var dev = pair.Value;
                if (!IsDeviceChannelReady(dev)) continue;
                if (string.Equals(dev.DeviceId, deviceId, StringComparison.Ordinal))
                    return false;
            }

            string keyToRemove = null;
            KnownDeviceRecord target = null;
            foreach (var kv in knownDevices)
            {
                if (!string.Equals(kv.Value.DeviceId, deviceId, StringComparison.Ordinal)) continue;
                keyToRemove = kv.Key;
                target = kv.Value;
                break;
            }

            if (keyToRemove == null || target == null) return false;

            var live = FindLiveDeviceForRecord(target);
            if (live != null) return false;

            if (!knownDevices.TryRemove(keyToRemove, out _)) return false;

            SaveDeltaSyncState();
            PublishConnectedDevicesSnapshot();
            return true;
        }
        #endregion

        #region 客户端处理
        private async Task HandleTcpClient(TcpClient client)
        {
            string deviceId = "";
            string deviceName = "";
            string devicePairingCode = "";
            string clientIp = ((IPEndPoint)client.Client.RemoteEndPoint).Address.ToString();
            DeviceClient deviceClient = null;
            var ownedTcpClient = client;

            try
            {
                LogMessage($"📱 新TCP连接来自: {clientIp}");

                try
                {
                    client.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);
                }
                catch { }

                using (client)
                using (var stream = client.GetStream())
                using (var reader = new StreamReader(stream, Encoding.UTF8))
                using (var writer = new StreamWriter(stream, Encoding.UTF8) { AutoFlush = true })
                {
                    LogMessage($"⏳ 等待设备注册信息从 {clientIp}...");
                    string registerMsg = reader.ReadLine();

                    if (registerMsg != null)
                    {
                        LogMessage($"📥 收到注册消息: {registerMsg}");

                        if (registerMsg.StartsWith("REGISTER|"))
                        {
                            string[] parts = registerMsg.Split('|');
                            if (parts.Length >= 4)
                            {
                                deviceId = parts[1];
                                deviceName = parts[2];
                                devicePairingCode = parts[3];

                                // 检查是否通过UDP预验证（可选标记）
                                bool udpPreVerified = parts.Length > 4 && parts[4] == "UDP_VERIFIED";

                                if (devicePairingCode != _pairingCode)
                                {
                                    LogMessage($"❌ TCP配对码验证失败：设备={devicePairingCode}，服务器={_pairingCode}，拒绝连接");
                                    writer.WriteLine("REGISTER_FAIL|配对码不匹配");
                                    return;
                                }

                                if (udpPreVerified)
                                {
                                    LogMessage($"✅ TCP连接使用UDP预验证，配对码匹配: {devicePairingCode}");
                                }
                                else
                                {
                                    LogMessage($"✅ TCP配对码验证成功: {devicePairingCode}");
                                }

                                string deviceType = parts.Length > 4 ? parts[4] : "手持端";

                                LogMessage($"✅ 设备注册: ID={deviceId}, 名称={deviceName}, 类型={deviceType}, 配对码匹配");

                                if (connectedDevices.ContainsKey(deviceId))
                                {
                                    LogMessage($"🔄 设备 {deviceName} 重新连接，关闭旧连接");
                                    connectedDevices[deviceId].Close();
                                    connectedDevices.TryRemove(deviceId, out _);
                                }

                                deviceClient = new DeviceClient
                                {
                                    DeviceId = deviceId,
                                    DeviceName = deviceName,
                                    DeviceType = deviceType,
                                    IpAddress = clientIp,
                                    TcpClient = client,
                                    Writer = writer,
                                    Reader = reader,
                                    LastHeartbeat = DateTime.Now,
                                    ConnectedAt = DateTime.Now,
                                    IsOnline = true,
                                    PairingCode = devicePairingCode
                                };

                                connectedDevices[deviceId] = deviceClient;
                                LogMessage($"✅ 设备注册成功：{deviceName} ({deviceId}) from {clientIp}");
                                UpsertKnownDevice(deviceId, deviceName, deviceType, clientIp);
                                PublishConnectedDevicesSnapshot();

                                string activeYearSuffix = FiscalYearService.IsInitialized
                                    ? $"|active_year={FiscalYearService.ActiveYear}"
                                    : "";
                                string registerOkMsg = $"REGISTER_OK|{Environment.MachineName}|欢迎使用冷库宝|{_pairingCode}{activeYearSuffix}";
                                writer.WriteLine(registerOkMsg);
                                LogMessage($"📤 发送注册确认: {registerOkMsg}");

                                PushPendingData(deviceClient);

                                string message;
                                while (isRunning && (message = reader.ReadLine()) != null)
                                {
                                    LogMessage($"📥 收到消息: {message}");
                                    deviceClient.LastHeartbeat = DateTime.Now;

                                    if (message.StartsWith("PING|"))
                                    {
                                        SendMessageSafely(deviceClient, "PONG|" + DateTime.Now.Ticks);
                                        LogMessage($"📤 回复心跳PONG");
                                        continue;
                                    }

                                    if (message == "HEARTBEAT")
                                    {
                                        SendMessageSafely(deviceClient, "HEARTBEAT_OK");
                                        LogMessage($"📤 回复心跳");
                                        continue;
                                    }

                                    if (message.StartsWith("SYNC|"))
                                    {
                                        HandleSyncData(message.Substring(5), deviceClient);
                                    }
                                    else if (message.StartsWith("HELLO_SYNC|"))
                                    {
                                        HandleHelloSync(message.Substring("HELLO_SYNC|".Length), deviceClient);
                                    }
                                    else if (message.StartsWith("PUSH_CHANGES|"))
                                    {
                                        HandlePushChanges(message.Substring("PUSH_CHANGES|".Length), deviceClient);
                                    }
                                    else if (message.StartsWith("PULL_DELTA_REQ|"))
                                    {
                                        HandlePullDeltaReq(message.Substring("PULL_DELTA_REQ|".Length), deviceClient);
                                    }
                                    else if (message.StartsWith("DELTA_APPLY_ACK|"))
                                    {
                                        HandleDeltaApplyAck(message.Substring("DELTA_APPLY_ACK|".Length), deviceClient);
                                    }
                                    else if (message == "REQUEST_PENDING")
                                    {
                                        PushPendingData(deviceClient);
                                    }
                                    else if (message == "PULL_FULL_SYNC")
                                    {
                                        // 手持端请求全量反向同步
                                        LogMessage($"📥 收到来自 {deviceClient.DeviceName} 的全量反向同步请求");
                                        var devForPull = deviceClient;
                                        _ = Task.Run(async () =>
                                        {
                                            var sem = GetDeviceSyncLock(devForPull.DeviceId);
                                            await sem.WaitAsync().ConfigureAwait(false);
                                            try
                                            {
                                                await HandlePullFullSync(devForPull).ConfigureAwait(false);
                                            }
                                            finally
                                            {
                                                sem.Release();
                                            }
                                        });
                                    }
                                    // 处理手持端发送的业务数据，并返回处理结果
                                    else if (message.StartsWith("INBOUND|") ||
                                             message.StartsWith("SALES|") ||
                                             message.StartsWith("PACKAGING|") ||
                                             message.StartsWith("ADVANCE|") ||      // 添加预支款
                                             message.StartsWith("DEDUCTION|") ||    // 添加扣款
                                             message.StartsWith("PRESALE_OUTBOUND|") ||
                                             message.StartsWith("PRESALE_PAYMENT|") ||
                                             message.StartsWith("PRESALE|") ||
                                             message.StartsWith("LEDGER|") ||
                                             message.StartsWith("CUSTOMER|") ||
                                             message.StartsWith("PRODUCT|") ||
                                             message.StartsWith("LOCATION|") ||
                                             message.StartsWith("OPERATOR|"))
                                    {
                                        var devForSync = deviceClient;
                                        var syncMessage = message;
                                        _ = Task.Run(async () =>
                                        {
                                            var sem = GetDeviceSyncLock(devForSync.DeviceId);
                                            await sem.WaitAsync().ConfigureAwait(false);
                                            try
                                            {
                                                HandleSyncDataWithAckCore(syncMessage, devForSync);
                                            }
                                            finally
                                            {
                                                sem.Release();
                                            }
                                        });
                                    }
                                    else if (message == "UPLOAD_COMPLETE")
                                    {
                                        LogMessage($"ℹ️ 手持端上报上传完成: {deviceClient.DeviceName}");
                                    }
                                    else if (message.StartsWith("REGISTER|"))
                                    {
                                        registerOkMsg = $"REGISTER_OK|{Environment.MachineName}|欢迎使用冷库宝|{_pairingCode}" +
                                            (FiscalYearService.IsInitialized ? $"|active_year={FiscalYearService.ActiveYear}" : "");
                                        SendMessageSafely(deviceClient, registerOkMsg);
                                        LogMessage($"ℹ️ 设备重复注册，已重新确认: {deviceClient.DeviceName}");
                                    }
                                    else if (message.StartsWith("PUSH|") ||
                                             message.StartsWith("CONFIG_PULL|") ||
                                             message.StartsWith("CONFIG_PUSH|"))
                                    {
                                        string response = syncEngine.HandleLine(deviceClient.DeviceId, clientIp, message);
                                        if (!string.IsNullOrEmpty(response))
                                        {
                                            lock (deviceClient.WriteLock)
                                                writer.WriteLine(response);
                                        }
                                    }
                                    else
                                    {
                                        LogMessage($"⚠️ 未知消息类型: {message}");
                                    }
                                }
                            }
                            else
                            {
                                LogMessage($"❌ 注册消息格式错误: {registerMsg}");
                                writer.WriteLine("REGISTER_FAIL|消息格式错误");
                            }
                        }
                        else
                        {
                            LogMessage($"❌ 不是注册消息: {registerMsg}");
                            writer.WriteLine("REGISTER_FAIL|不是注册消息");
                        }
                    }
                    else
                    {
                        LogMessage($"❌ 未收到注册消息，连接可能已关闭");
                    }
                }
            }
            catch (Exception ex)
            {
                LogMessage($"❌ 客户端连接异常: {clientIp} - {ex.Message}");
                LogMessage($"❌ 异常堆栈: {ex.StackTrace}");
            }
            finally
            {
                if (!string.IsNullOrEmpty(deviceId) &&
                    connectedDevices.TryGetValue(deviceId, out var stillThere) &&
                    ReferenceEquals(stillThere.TcpClient, ownedTcpClient))
                {
                    connectedDevices.TryRemove(deviceId, out _);
                    LogMessage($"📴 设备断开：{deviceName} ({deviceId})");
                    PublishConnectedDevicesSnapshot();
                }
            }
        }
        /// <summary>
        /// 处理手持端发起的全量反向同步请求（以电脑端为主）
        /// </summary>
        private async Task HandlePullFullSync(DeviceClient device)
        {
            try
            {
                LogMessage($"🔄 开始反向同步处理，设备: {device.DeviceName}");

                var deviceId = device.DeviceId;
                var requestConnection = device.TcpClient;
                if (!TryGetCurrentDevice(deviceId, out var currentDevice))
                {
                    LogMessage($"⚠️ 反向同步中止：设备已离线，deviceId={deviceId}");
                    RaiseSyncProgress("全量同步未开始：设备已离线", 0, false, device.DeviceName);
                    return;
                }

                if (!ReferenceEquals(currentDevice.TcpClient, requestConnection) &&
                    currentDevice.TcpClient != null && requestConnection != null)
                {
                    LogMessage($"⚠️ 反向同步中止：连接已切换，deviceId={deviceId}，请求连接与当前连接不一致");
                    RaiseSyncProgress("全量同步未开始：连接已切换", 0, false, device.DeviceName);
                    return;
                }

                // 通知手持端上传本地未同步数据，但不再执行无效等待
                SendMessageSafely(currentDevice, "PREPARE_UPLOAD|请上传本地未同步数据");
                LogMessage($"📤 已通知 {currentDevice.DeviceName} 上传未同步数据，立即开始下发全量数据");

                LogMessage($"📦 开始打包电脑端数据并下发全量同步");
                RaiseSyncProgress($"电脑 → 手持：正在打包全量数据（客户/库位/库存/入库统计）… [{currentDevice.DeviceName}]", 0, true, currentDevice.DeviceName);

                // 第二步：获取电脑端所有基础数据
                DatabaseManager db = new DatabaseManager();

                // 全量同步：卖家 + 买家客户
                var clients = db.GetAllClientsForFullSync();
                var clientList = new List<object>();
                foreach (DataRow row in clients.Rows)
                {
                    string code = row["code"]?.ToString() ?? "";
                    string name = row["name"]?.ToString() ?? "";
                    string phone = row.Table.Columns.Contains("phone") ? row["phone"]?.ToString() ?? "" : "";
                    string customerType = row.Table.Columns.Contains("customer_type")
                        ? row["customer_type"]?.ToString() ?? "SELLER"
                        : (code.StartsWith("MJ", StringComparison.OrdinalIgnoreCase) ? "BUYER" : "SELLER");
                    int status = row.Table.Columns.Contains("status") && row["status"] != DBNull.Value
                        ? Convert.ToInt32(row["status"])
                        : 1;

                    clientList.Add(new
                    {
                        code,
                        name,
                        phone,
                        customer_type = customerType,
                        enabled = status != 0
                    });
                }

                var productTypeList = new List<object>();
                foreach (DataRow row in db.GetProductTypesForSync().Rows)
                {
                    string code = row["code"]?.ToString() ?? "";
                    string name = row["name"]?.ToString() ?? code;
                    bool enabled = row["is_active"] == DBNull.Value || Convert.ToInt32(row["is_active"]) != 0;
                    productTypeList.Add(new { code, name, enabled });
                }

                var packTypeList = new List<object>();
                foreach (DataRow row in db.GetPackTypesForSync().Rows)
                {
                    string packName = row["name"]?.ToString() ?? "";
                    if (string.IsNullOrWhiteSpace(packName)) continue;
                    bool enabled = row["is_active"] == DBNull.Value || Convert.ToInt32(row["is_active"]) != 0;
                    packTypeList.Add(new { name = packName, enabled });
                }

                // 获取所有启用的库位（只同步名称）
                var locations = db.GetAllLocations();
                var locationList = new List<object>();
                var enabledLocationNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (DataRow row in locations.Rows)
                {
                    // 只同步启用的库位
                    if (row["status"]?.ToString() == "1")
                    {
                        var locName = row["name"]?.ToString() ?? "";
                        enabledLocationNames.Add(locName);
                        locationList.Add(new
                        {
                            name = locName
                            // 不发送 code
                        });
                    }
                }

                // 获取所有启用的经手人（只同步名称）
                var handlers = db.GetAllHandlers();
                var handlerList = new List<object>();
                foreach (DataRow row in handlers.Rows)
                {
                    handlerList.Add(new
                    {
                        name = row["name"].ToString()
                        // 不发送 code
                    });
                }

                // 获取“库位 + 型号”的当前库存汇总（用于离线快照）
                var inventoryByLocation = db.GetInventoryByLocation();
                var stockList = new List<object>();
                foreach (DataRow row in inventoryByLocation.Rows)
                {
                    var locationName = row["库位"]?.ToString() ?? "";
                    if (string.IsNullOrWhiteSpace(locationName)) continue;

                    // 只下发启用库位的库存
                    if (!enabledLocationNames.Contains(locationName)) continue;

                    var spec = row["商品型号"]?.ToString() ?? "";
                    if (string.IsNullOrWhiteSpace(spec)) continue;

                    int currentQty = 0;
                    try
                    {
                        var raw = row["当前库存"];
                        if (raw != null && raw != DBNull.Value)
                            currentQty = Convert.ToInt32(raw);
                    }
                    catch
                    {
                        currentQty = 0;
                    }

                    stockList.Add(new
                    {
                        location_name = locationName,
                        spec = spec,
                        current_quantity = currentQty
                    });
                }

                // 获取“入库统计快照”（按 date + 客户 + 库位 + 型号）
                var inboundDailyStats = db.GetInboundDailyStats();
                var inboundDailyList = new List<object>();
                foreach (DataRow row in inboundDailyStats.Rows)
                {
                    var locationName = row["location_name"]?.ToString() ?? "";
                    if (string.IsNullOrWhiteSpace(locationName)) continue;

                    // 只下发启用库位的统计
                    if (!enabledLocationNames.Contains(locationName)) continue;

                    var date = row["date"]?.ToString() ?? "";
                    var customerNo = row["customer_no"]?.ToString() ?? "";
                    var customerName = row["customer_name"]?.ToString() ?? "";
                    var spec = row["spec"]?.ToString() ?? "";

                    if (string.IsNullOrWhiteSpace(date) ||
                        string.IsNullOrWhiteSpace(customerNo) ||
                        string.IsNullOrWhiteSpace(spec))
                    {
                        continue;
                    }

                    int quantity = 0;
                    double amount = 0;
                    int orderCount = 0;

                    try
                    {
                        var rawQty = row["quantity"];
                        if (rawQty != null && rawQty != DBNull.Value) quantity = Convert.ToInt32(rawQty);
                    }
                    catch { quantity = 0; }

                    try
                    {
                        var rawAmount = row["amount"];
                        if (rawAmount != null && rawAmount != DBNull.Value) amount = Convert.ToDouble(rawAmount);
                    }
                    catch { amount = 0; }

                    try
                    {
                        var rawOrderCount = row["order_count"];
                        if (rawOrderCount != null && rawOrderCount != DBNull.Value) orderCount = Convert.ToInt32(rawOrderCount);
                    }
                    catch { orderCount = 0; }

                    inboundDailyList.Add(new
                    {
                        date = date,
                        customer_no = customerNo,
                        customer_name = customerName,
                        location_name = locationName,
                        spec = spec,
                        quantity = quantity,
                        amount = amount,
                        order_count = orderCount
                    });
                }

                // 第三步：打包所有数据
                var presaleBillPayloads = db.GetPresaleBillsForFullSync(90);
                var presalePaymentPayloads = db.GetPresalePaymentsForFullSync(90);

                var fullData = new
                {
                    sync_type = "FULL_SYNC",
                    timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                    server_name = Environment.MachineName,
                    data = new
                    {
                        clients = clientList,
                        locations = locationList,
                        handlers = handlerList,
                        product_types = productTypeList,
                        pack_types = packTypeList,
                        stocks = stockList,
                        inbound_stats = new
                        {
                            daily = inboundDailyList
                        },
                        presale_bills = presaleBillPayloads,
                        presale_payments = presalePaymentPayloads,
                    },
                    instruction = "REPLACE_ALL"  // 指示手持端替换所有数据
                };

                string jsonData = Newtonsoft.Json.JsonConvert.SerializeObject(fullData);

                // 第四步：分块发送数据
                int chunkSize = 32000;
                int totalLength = jsonData.Length;
                int chunks = (int)Math.Ceiling((double)totalLength / chunkSize);

                if (!TryGetCurrentDevice(deviceId, out currentDevice) ||
                    !ReferenceEquals(currentDevice.TcpClient, requestConnection))
                {
                    LogMessage($"⚠️ 反向同步中止：发送前连接发生切换，deviceId={deviceId}");
                    RaiseSyncProgress("电脑 → 手持：全量下发中断（发送前连接已切换）", 0, false, device.DeviceName);
                    return;
                }

                SendMessageSafely(currentDevice, $"FULL_SYNC_START|{chunks}|{totalLength}");
                LogMessage($"📤 开始发送全量数据，共 {chunks} 块，总大小 {totalLength} 字节");
                if (chunks > 0)
                    RaiseSyncProgress($"电脑 → 手持：开始下发全量同步，共 {chunks} 块 [{currentDevice.DeviceName}]", 0, true, currentDevice.DeviceName);
                else
                    RaiseSyncProgress($"电脑 → 手持：全量数据为空，正在结束 [{currentDevice.DeviceName}]", 100, true, currentDevice.DeviceName);

                for (int i = 0; i < chunks; i++)
                {
                    if (!TryGetCurrentDevice(deviceId, out currentDevice) ||
                        !ReferenceEquals(currentDevice.TcpClient, requestConnection))
                    {
                        LogMessage($"⚠️ 反向同步中止：分块发送期间连接已切换，deviceId={deviceId}，chunk={i + 1}/{chunks}");
                        RaiseSyncProgress("电脑 → 手持：全量下发中断（传输中连接已切换）", 0, false, device.DeviceName);
                        return;
                    }

                    int start = i * chunkSize;
                    int length = Math.Min(chunkSize, totalLength - start);
                    string chunk = jsonData.Substring(start, length);

                    SendMessageSafely(currentDevice, $"FULL_SYNC_DATA|{i + 1}|{chunks}|{chunk}");
                    int pct = Math.Min(100, (i + 1) * 100 / chunks);
                    RaiseSyncProgress($"电脑 → 手持：下发全量同步 {i + 1}/{chunks}（{pct}%）[{currentDevice.DeviceName}]", pct, true, currentDevice.DeviceName);
                    // 分块节流过大时会让手持端等待超时（尤其数据量大、块数多时）。
                    // 这里降低每块延迟以缩短总传输时长，同时仍避免一次性写入过快导致卡顿。
                    await Task.Delay(5);
                }

                if (!TryGetCurrentDevice(deviceId, out currentDevice) ||
                    !ReferenceEquals(currentDevice.TcpClient, requestConnection))
                {
                    LogMessage($"⚠️ 反向同步中止：结束帧发送前连接已切换，deviceId={deviceId}");
                    RaiseSyncProgress("电脑 → 手持：全量下发中断（结束前连接已切换）", 0, false, device.DeviceName);
                    return;
                }

                long snapshotCommitSeq = GetCurrentCommitSeq();
                SendMessageSafely(currentDevice, $"FULL_SYNC_END|传输完成|commit_seq={snapshotCommitSeq}");
                LogMessage($"✅ 全量数据发送完成，共 {chunks} 块，baseline commit_seq={snapshotCommitSeq}");
                RaiseSyncProgress($"电脑 → 手持：全量同步下发完成 [{currentDevice.DeviceName}]", 100, true, currentDevice.DeviceName);

                // 记录同步日志
                LogSyncResult(device.DeviceId, device.DeviceName, "FULL_SYNC",
                    DateTime.Now.ToString("yyyyMMddHHmmss"), true,
                    $"发送客户{clientList.Count}个，库位{locationList.Count}个，经手人{handlerList.Count}个，型号{productTypeList.Count}个，包装{packTypeList.Count}个，库存{stockList.Count}条");

                await Task.Delay(1500).ConfigureAwait(false);
                RaiseSyncProgress("同步空闲", 0, false);
            }
            catch (Exception ex)
            {
                LogMessage($"❌ 反向同步失败: {ex.Message}");
                RaiseSyncProgress($"电脑 → 手持：全量同步失败：{ex.Message}", 0, false, device.DeviceName);
                try
                {
                    if (TryGetCurrentDevice(device.DeviceId, out var currentDevice))
                    {
                        SendMessageSafely(currentDevice, $"FULL_SYNC_ERROR|{ex.Message}");
                    }
                }
                catch { }
            }
        }
        /// <summary>
        /// 处理同步数据并返回确认结果（带确认机制）
        /// </summary>
        private void HandleSyncDataWithAck(string message, DeviceClient device)
        {
            var devForSync = device;
            _ = Task.Run(async () =>
            {
                var sem = GetDeviceSyncLock(devForSync.DeviceId);
                await sem.WaitAsync().ConfigureAwait(false);
                try
                {
                    HandleSyncDataWithAckCore(message, devForSync);
                }
                finally
                {
                    sem.Release();
                }
            });
        }

        private bool TrySendAckToDevice(string deviceId, DeviceClient fallbackDevice, string ackMessage)
        {
            try
            {
                if (TryGetCurrentDevice(deviceId, out var currentDevice))
                {
                    SendMessageSafely(currentDevice, ackMessage);
                    return true;
                }

                if (fallbackDevice != null)
                {
                    SendMessageSafely(fallbackDevice, ackMessage);
                    return true;
                }
            }
            catch (Exception ex)
            {
                LogMessage($"⚠️ 发送ACK失败，设备可能已离线 ({deviceId}): {ex.Message}");
            }

            return false;
        }

        private void HandleSyncDataWithAckCore(string message, DeviceClient device, bool sendLegacyAck = true)
        {
            bool prevSuppress = DatabaseManager.SuppressLocalConfigSyncNotifications;
            DatabaseManager.SuppressLocalConfigSyncNotifications = true;
            try
            {
                HandleSyncDataWithAckCoreInner(message, device, sendLegacyAck);
            }
            finally
            {
                DatabaseManager.SuppressLocalConfigSyncNotifications = prevSuppress;
            }
        }

        private string HandleConfigPullCrsql(string clientId, long sinceDbVersion)
        {
            if (!crsqlEngine.IsAvailable)
                return null;

            try
            {
                var db = new DatabaseManager();
                using (var conn = new System.Data.SQLite.SQLiteConnection(db.ConnectionString))
                {
                    conn.Open();
                    crsqlEngine.TryInitialize(conn);
                    if (!crsqlEngine.IsAvailable) return null;
                    string changes = crsqlEngine.PullChangesJson(conn, sinceDbVersion, clientId);
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = "SELECT MAX(db_version) FROM crsql_changes";
                        var max = cmd.ExecuteScalar();
                        if (max != null && max != DBNull.Value)
                            syncEngine.UpdatePeerVersion(clientId, Convert.ToInt64(max));
                    }
                    return changes ?? "[]";
                }
            }
            catch (Exception ex)
            {
                LogMessage($"⚠️ CONFIG_PULL cr-sqlite 失败: {ex.Message}");
                return null;
            }
        }

        private bool HandleConfigPushCrsql(string clientId, string changesJson)
        {
            if (!crsqlEngine.IsAvailable || string.IsNullOrWhiteSpace(changesJson))
                return false;

            try
            {
                var db = new DatabaseManager();
                using (var conn = new System.Data.SQLite.SQLiteConnection(db.ConnectionString))
                {
                    conn.Open();
                    crsqlEngine.TryInitialize(conn);
                    if (!crsqlEngine.IsAvailable) return false;
                    crsqlEngine.ApplyChangesJson(conn, changesJson);
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = "SELECT MAX(db_version) FROM crsql_changes";
                        var max = cmd.ExecuteScalar();
                        if (max != null && max != DBNull.Value)
                            syncEngine.UpdatePeerVersion(clientId, Convert.ToInt64(max));
                    }
                    return true;
                }
            }
            catch (Exception ex)
            {
                LogMessage($"⚠️ CONFIG_PUSH cr-sqlite 失败: {ex.Message}");
                return false;
            }
        }

        private void HandleSyncDataWithAckCoreInner(string message, DeviceClient device, bool sendLegacyAck = true)
        {
            string dataType = "";
            string originalData = "";
            string orderNo = "";
            bool success = false;
            string errorMsg = "";
            PackagingSaveOutcome packagingSaveOutcome = PackagingSaveOutcome.SavedNew;

            try
            {
                // 解析消息类型和数据
                int separatorIndex = message.IndexOf('|');
                if (separatorIndex > 0)
                {
                    dataType = message.Substring(0, separatorIndex);
                    originalData = message.Substring(separatorIndex + 1);
                }
                else
                {
                    dataType = message;
                    originalData = "";
                }

                LogMessage($"📥 收到来自 {device.DeviceName} 的{dataType}同步数据，等待处理确认");

                string typeTitle = GetHandheldSyncTypeTitle(dataType);
                string peek = TryPeekHandheldPayloadLabel(dataType, originalData);
                string idPart = string.IsNullOrEmpty(peek) ? "" : $" {peek}";
                RaiseSyncProgress($"手持端 → 电脑：正在处理{typeTitle}{idPart} … [{device.DeviceName}]", 0, false, device.DeviceName);

                // 根据数据类型调用相应的处理方法，并获取处理结果
                switch (dataType)
                {
                    case "INBOUND":
                        success = ProcessInboundSyncDataWithAck(originalData, device, out orderNo, out errorMsg);
                        break;
                    case "SALES":
                        success = ProcessSalesSyncDataWithAck(originalData, device, out orderNo, out errorMsg);
                        break;
                    case "PACKAGING":
                        success = ProcessPackagingSyncDataWithAck(originalData, device, out orderNo, out errorMsg, out packagingSaveOutcome);
                        break;
                    case "ADVANCE":  // 新增：预支款
                        success = ProcessAdvanceSyncDataWithAck(originalData, device, out orderNo, out errorMsg);
                        break;
                    case "DEDUCTION": // 新增：扣款
                        success = ProcessDeductionSyncDataWithAck(originalData, device, out orderNo, out errorMsg);
                        break;
                    case "PRESALE":
                        success = ProcessPresaleSyncDataWithAck(originalData, device, out orderNo, out errorMsg);
                        break;
                    case "PRESALE_PAYMENT":
                        success = ProcessPresalePaymentSyncDataWithAck(originalData, device, out orderNo, out errorMsg);
                        break;
                    case "PRESALE_OUTBOUND":
                        success = ProcessPresaleOutboundSyncDataWithAck(originalData, device, out orderNo, out errorMsg);
                        break;
                    case "LEDGER":
                        success = ProcessLedgerSyncDataWithAck(originalData, device, out orderNo, out errorMsg);
                        break;
                    case "CUSTOMER":
                        success = ProcessCustomerSyncDataWithAck(originalData, device, out orderNo, out errorMsg);
                        break;
                    case "LOCATION":
                        success = ProcessLocationSyncDataWithAck(originalData, device, out orderNo, out errorMsg);
                        break;
                    case "OPERATOR":
                        success = ProcessOperatorSyncDataWithAck(originalData, device, out orderNo, out errorMsg);
                        break;
                    case "PRODUCT":
                        success = ProcessProductSyncDataWithAck(originalData, device, out orderNo, out errorMsg);
                        break;
                    default:
                        errorMsg = $"未知的数据类型: {dataType}";
                        LogMessage($"❌ {errorMsg}");
                        break;
                }

                if (sendLegacyAck)
                {
                    // 发送确认消息给手持端（legacy TYPE|json 协议）
                    if (success)
                    {
                        string ackMessage;
                        if (dataType == "PACKAGING" && packagingSaveOutcome == PackagingSaveOutcome.IdempotentMatch)
                        {
                            ackMessage = $"SYNC_SUCCESS|{dataType}|{orderNo}|数据处理成功|IDEMPOTENT|{SanitizeAckDetail(errorMsg)}";
                        }
                        else
                        {
                            ackMessage = $"SYNC_SUCCESS|{dataType}|{orderNo}|数据处理成功";
                        }
                        if (TrySendAckToDevice(device.DeviceId, device, ackMessage))
                        {
                            LogMessage($"✅ 已发送成功确认给 {device.DeviceName}: {dataType} 标识={orderNo}");
                        }
                        else
                        {
                            LogMessage($"⚠️ 保存成功但ACK未送达（设备已离线）: {dataType} 标识={orderNo}");
                        }

                        LogSyncResult(device.DeviceId, device.DeviceName, dataType, orderNo, true, "");
                        RaiseSyncProgress($"手持端 → 电脑：{typeTitle}{idPart} 已成功保存 ✓ [{device.DeviceName}]", 0, false, device.DeviceName);
                    }
                    else
                    {
                        string ackMessage = $"SYNC_FAILED|{dataType}|{orderNo ?? ""}|{errorMsg}";
                        TrySendAckToDevice(device.DeviceId, device, ackMessage);
                        LogMessage($"❌ 已发送失败确认给 {device.DeviceName}: {dataType} 错误={errorMsg}");

                        LogSyncResult(device.DeviceId, device.DeviceName, dataType, orderNo, false, errorMsg);
                        RaiseSyncProgress($"手持端 → 电脑：{typeTitle}{idPart} 失败：{errorMsg} [{device.DeviceName}]", 0, false, device.DeviceName);
                    }
                }
                else if (success)
                {
                    LogSyncResult(device.DeviceId, device.DeviceName, dataType, orderNo, true, "");
                    RaiseSyncProgress($"手持端 → 电脑：{typeTitle}{idPart} 已成功保存 ✓ [{device.DeviceName}]", 0, false, device.DeviceName);
                }
                else
                {
                    LogSyncResult(device.DeviceId, device.DeviceName, dataType, orderNo, false, errorMsg);
                    RaiseSyncProgress($"手持端 → 电脑：{typeTitle}{idPart} 失败：{errorMsg} [{device.DeviceName}]", 0, false, device.DeviceName);
                }

                // 数据保存成功后，通知其他设备（可选）
                if (success)
                {
                    CheckAndNotifyOtherDevices(message, device.DeviceId);
                }
            }
            catch (Exception ex)
            {
                LogMessage($"❌ 处理同步数据时发生异常: {ex.Message}");
                LogMessage($"❌ 异常堆栈: {ex.StackTrace}");
                RaiseSyncProgress($"手持端 → 电脑：处理同步数据异常：{ex.Message} [{device.DeviceName}]", 0, false, device.DeviceName);

                if (sendLegacyAck)
                {
                    try
                    {
                        string ackMessage = $"SYNC_ERROR|{dataType}|{ex.Message}";
                        TrySendAckToDevice(device.DeviceId, device, ackMessage);
                    }
                    catch { }
                }
                else
                {
                    throw;
                }
            }
        }

        private void HandleSyncData(string data, DeviceClient device)
        {
            var sem = GetDeviceSyncLock(device.DeviceId);
            sem.Wait();
            try
            {
                LogMessage($"📥 收到来自 {device.DeviceName} 的同步数据");
                RaiseSyncProgress($"手持端 → 电脑：正在处理同步数据（旧协议）… [{device.DeviceName}]", 0, false, device.DeviceName);
                ProcessIncomingData(data, device);
                SendMessageSafely(device, "SYNC_OK|数据接收成功");
                CheckAndNotifyOtherDevices(data, device.DeviceId);
                RaiseSyncProgress($"手持端 → 电脑：旧协议数据已接收 ✓ [{device.DeviceName}]", 0, false, device.DeviceName);
            }
            catch (Exception ex)
            {
                LogMessage($"❌ 处理同步数据失败: {ex.Message}");
                RaiseSyncProgress($"手持端 → 电脑：旧协议同步失败：{ex.Message} [{device.DeviceName}]", 0, false, device.DeviceName);
                SendMessageSafely(device, $"SYNC_ERROR|{ex.Message}");
            }
            finally
            {
                sem.Release();
            }
        }

        private SemaphoreSlim GetDeviceSyncLock(string deviceId)
        {
            return deviceSyncLocks.GetOrAdd(deviceId, _ => new SemaphoreSlim(1, 1));
        }

        private void PushPendingData(DeviceClient device)
        {
            try
            {
                LogMessage($"ℹ️ 已禁用 PC→手持业务单据下发，仅保留基础配置/库存/入库统计全量同步。设备: {device.DeviceName}");
                RaiseSyncProgress($"已向手持端应答：无逐单业务推送（请使用全量同步）[{device.DeviceName}]", 0, false, device.DeviceName);
                SendMessageSafely(device, "PUSH_COMPLETE|NO_DATA");
            }
            catch (Exception ex)
            {
                LogMessage($"❌ 推送数据失败: {ex.Message}");
            }
        }

        private void ProcessSyncQueue()
        {
            while (true)
            {
                SyncTask task = null;
                lock (queueLock)
                {
                    if (syncTaskQueue.Count > 0)
                        task = syncTaskQueue.Dequeue();
                    else
                        break;
                }

                if (task != null && connectedDevices.ContainsKey(task.DeviceId))
                {
                    var device = connectedDevices[task.DeviceId];
                    if (device.IsOnline)
                    {
                        try
                        {
                            SendMessageSafely(device, $"AUTO_SYNC|{task.Data}");
                        }
                        catch (Exception ex)
                        {
                            LogMessage($"❌ 自动同步失败: {ex.Message}");
                        }
                    }
                }
            }
        }
        #endregion

        #region 数据处理（手持端JSON格式）
        private void ProcessIncomingData(string data, DeviceClient device)
        {
            bool prevSuppress = DatabaseManager.SuppressLocalConfigSyncNotifications;
            DatabaseManager.SuppressLocalConfigSyncNotifications = true;
            try
            {
                try
                {
                    LogMessage($"📥 开始处理同步数据...");

                    if (data.StartsWith("INBOUND|"))
                    {
                        ProcessInboundSyncData(data.Substring(8), device);
                    }
                    else if (data.StartsWith("SALES|"))
                    {
                        ProcessSalesSyncData(data.Substring(6), device);
                    }
                    else if (data.StartsWith("PACKAGING|"))
                    {
                        ProcessPackagingSyncData(data.Substring(10), device);
                    }
                    else if (data.StartsWith("ADVANCE|"))  // 新增：预支款
                    {
                        ProcessAdvanceSyncData(data.Substring(8), device);
                    }
                    else if (data.StartsWith("DEDUCTION|")) // 新增：扣款
                    {
                        ProcessDeductionSyncData(data.Substring(9), device);
                    }
                    else if (data.StartsWith("PRESALE_OUTBOUND|"))
                    {
                        ProcessPresaleOutboundSyncData(data.Substring(17), device);
                    }
                    else if (data.StartsWith("PRESALE_PAYMENT|"))
                    {
                        ProcessPresalePaymentSyncData(data.Substring(15), device);
                    }
                    else if (data.StartsWith("PRESALE|"))
                    {
                        ProcessPresaleSyncData(data.Substring(8), device);
                    }
                    else if (data.StartsWith("LEDGER|"))
                    {
                        ProcessLedgerSyncData(data.Substring(7), device);
                    }
                    else if (data.StartsWith("CUSTOMER|"))
                    {
                        ProcessCustomerSyncData(data.Substring(9), device);
                    }
                    else if (data.StartsWith("LOCATION|"))
                    {
                        ProcessLocationSyncData(data.Substring(9), device);
                    }
                    else if (data.StartsWith("OPERATOR|"))
                    {
                        ProcessOperatorSyncData(data.Substring(9), device);
                    }
                    else
                    {
                        LogMessage($"⚠️ 未知的数据类型: {data.Substring(0, Math.Min(30, data.Length))}");
                    }
                }
                catch (Exception ex)
                {
                    LogMessage($"❌ 处理同步数据失败: {ex.Message}");
                    throw;
                }
            }
            finally
            {
                DatabaseManager.SuppressLocalConfigSyncNotifications = prevSuppress;
            }
        }

        private void ProcessInboundSyncData(string jsonData, DeviceClient device)
        {
            try
            {
                LogMessage($"📦 处理入库同步数据: {jsonData}");

                var json = JObject.Parse(jsonData);

                string orderNo = json["bill_no"]?.ToString() ?? json["order_no"]?.ToString();
                string clientCode = json["client_code"]?.ToString();
                string clientName = json["client_name"]?.ToString();
                string location = json["location_code"]?.ToString() ?? json["location"]?.ToString();
                string spec = json["product_spec"]?.ToString() ?? json["spec"]?.ToString();
                string quantityStr = json["quantity"]?.ToString() ?? "0";
                string unitPriceStr = json["unit_price"]?.ToString() ?? "0";
                string totalAmountStr = json["total_amount"]?.ToString() ?? "0";
                string handler = json["handler"]?.ToString() ?? device.DeviceName;
                string creator = json["creator"]?.ToString() ?? "手持端";
                string dateStr = json["date"]?.ToString() ?? DateTime.Now.ToString("yyyy-MM-dd");

                int quantity = 0;
                int.TryParse(quantityStr, out quantity);

                decimal unitPrice = 0;
                decimal.TryParse(unitPriceStr, out unitPrice);

                decimal totalAmount = 0;
                decimal.TryParse(totalAmountStr, out totalAmount);

                DateTime date;
                if (!DateTime.TryParse(dateStr, out date))
                {
                    date = DateTime.Now;
                }

                LogMessage($"📦 解析入库数据：单号={orderNo}，客户={clientName}，规格={spec}，数量={quantity}");

                // ✅ 直接使用 DatabaseManager 的 SaveInboundRecord 方法（和销售单一样）
                DatabaseManager db = new DatabaseManager();
                bool success = db.SaveInboundRecord(
                    orderNo,
                    clientCode,
                    clientName,
                    location,
                    date,
                    spec,
                    quantity,
                    unitPrice,
                    totalAmount,
                    handler,
                    creator
                );

                if (success)
                {
                    LogMessage($"✅ 入库同步成功：单号={orderNo}，规格={spec}");
                    SendMessageSafely(device, $"SYNC_OK|INBOUND|{orderNo}");
                }
                else
                {
                    LogMessage($"❌ 入库同步失败：单号={orderNo}");
                    SendMessageSafely(device, $"SYNC_ERROR|入库保存失败");
                }
            }
            catch (Exception ex)
            {
                LogMessage($"❌ 处理入库同步数据异常: {ex.Message}");
                LogMessage($"❌ 异常堆栈: {ex.StackTrace}");
                SendMessageSafely(device, $"SYNC_ERROR|{ex.Message}");
            }
        }

        // 新增：处理预支款同步数据（不带确认的版本，用于旧版兼容）
        private void ProcessAdvanceSyncData(string jsonData, DeviceClient device)
        {
            try
            {
                LogMessage($"💰 处理预支款同步数据: {jsonData}");

                var json = JObject.Parse(jsonData);

                string clientCode = json["client_code"]?.ToString();
                string clientName = json["client_name"]?.ToString();
                string amountStr = json["amount"]?.ToString() ?? "0";
                string advanceDate = json["advance_date"]?.ToString() ?? DateTime.Now.ToString("yyyy-MM-dd");
                string reason = json["reason"]?.ToString() ?? "";
                string handler = json["handler"]?.ToString() ?? device.DeviceName;
                string creator = json["creator"]?.ToString() ?? "手持端";
                string statusStr = json["status"]?.ToString() ?? "1";
                string createdTime = json["created_time"]?.ToString();

                decimal amount = 0;
                decimal.TryParse(amountStr, out amount);

                LogMessage($"💰 解析预支款数据：客户={clientName}，金额={amount}，日期={advanceDate}");

                DatabaseManager db = new DatabaseManager();
                bool success = db.SaveAdvanceRecord(
                    clientCode: clientCode,
                    clientName: clientName,
                    amount: amount,
                    advanceDate: advanceDate,
                    reason: reason,
                    handler: handler,
                    creator: creator,
                    status: 1,
                    createdTime: createdTime
                );

                if (success)
                {
                    LogMessage($"✅ 预支款同步成功：客户={clientName}，金额={amount}");
                    SendMessageSafely(device, $"SYNC_OK|ADVANCE|{clientCode}");
                }
                else
                {
                    LogMessage($"❌ 预支款同步失败：客户={clientName}");
                    SendMessageSafely(device, $"SYNC_ERROR|预支款保存失败");
                }
            }
            catch (Exception ex)
            {
                LogMessage($"❌ 处理预支款同步数据异常: {ex.Message}");
                SendMessageSafely(device, $"SYNC_ERROR|{ex.Message}");
            }
        }

        // 新增：处理扣款同步数据（不带确认的版本，用于旧版兼容）
        private void ProcessDeductionSyncData(string jsonData, DeviceClient device)
        {
            try
            {
                LogMessage($"💸 处理扣款同步数据: {jsonData}");

                var json = JObject.Parse(jsonData);

                string clientCode = json["client_code"]?.ToString();
                string clientName = json["client_name"]?.ToString();
                string amountStr = json["amount"]?.ToString() ?? "0";
                string quantityStr = json["quantity"]?.ToString() ?? "0";
                string unitPriceStr = json["unit_price"]?.ToString() ?? "0";
                string deductDate = json["deduct_date"]?.ToString() ?? DateTime.Now.ToString("yyyy-MM-dd");
                string reason = json["reason"]?.ToString() ?? "";
                string handler = json["handler"]?.ToString() ?? device.DeviceName;
                string creator = json["creator"]?.ToString() ?? "手持端";
                string statusStr = json["status"]?.ToString() ?? "1";
                string createdTime = json["created_time"]?.ToString();

                decimal amount = 0;
                decimal.TryParse(amountStr, out amount);
                int quantity = 0;
                int.TryParse(quantityStr, out quantity);
                decimal unitPrice = 0;
                decimal.TryParse(unitPriceStr, out unitPrice);

                LogMessage($"💸 解析扣款数据：客户={clientName}，数量={quantity}，单价={unitPrice}，金额={amount}，日期={deductDate}");

                DatabaseManager db = new DatabaseManager();
                bool success = db.SaveDeductionRecord(
                    clientCode: clientCode,
                    clientName: clientName,
                    amount: amount,
                    deductDate: deductDate,
                    reason: reason,
                    handler: handler,
                    creator: creator,
                    status: 1,
                    quantity: quantity,
                    unitPrice: unitPrice,
                    createdTime: createdTime
                );

                if (success)
                {
                    LogMessage($"✅ 扣款同步成功：客户={clientName}，数量={quantity}，单价={unitPrice}，金额={amount}");
                    SendMessageSafely(device, $"SYNC_OK|DEDUCTION|{clientCode}");
                }
                else
                {
                    LogMessage($"❌ 扣款同步失败：客户={clientName}");
                    SendMessageSafely(device, $"SYNC_ERROR|扣款保存失败");
                }
            }
            catch (Exception ex)
            {
                LogMessage($"❌ 处理扣款同步数据异常: {ex.Message}");
                SendMessageSafely(device, $"SYNC_ERROR|{ex.Message}");
            }
        }

        /// <summary>
        /// 处理入库同步数据并返回处理结果（带确认机制）
        /// </summary>
        private bool ProcessInboundSyncDataWithAck(string jsonData, DeviceClient device, out string orderNo, out string errorMsg)
        {
            orderNo = "";
            errorMsg = "";

            try
            {
                LogMessage($"📦 处理入库同步数据（带确认）: {jsonData}");

                var json = JObject.Parse(jsonData);

                orderNo = json["bill_no"]?.ToString() ?? json["order_no"]?.ToString();
                string clientCode = json["client_code"]?.ToString();
                string clientName = json["client_name"]?.ToString();
                string sourceDeviceId = json["source_device_id"]?.ToString() ?? device.DeviceId;
                string sourceRecordId = json["source_record_id"]?.ToString() ?? "";
                string location = json["location_code"]?.ToString() ?? json["location"]?.ToString();
                string spec = json["product_spec"]?.ToString() ?? json["spec"]?.ToString();
                string quantityStr = json["quantity"]?.ToString() ?? "0";
                string unitPriceStr = json["unit_price"]?.ToString() ?? "0";
                string totalAmountStr = json["total_amount"]?.ToString() ?? "0";
                string handler = json["handler"]?.ToString() ?? device.DeviceName;
                string creator = json["creator"]?.ToString() ?? "手持端";
                string dateStr = json["date"]?.ToString() ?? DateTime.Now.ToString("yyyy-MM-dd");

                if (string.IsNullOrEmpty(orderNo))
                {
                    errorMsg = "入库单号不能为空";
                    LogMessage($"❌ {errorMsg}");
                    return false;
                }

                int quantity = 0;
                int.TryParse(quantityStr, out quantity);

                decimal unitPrice = 0;
                decimal.TryParse(unitPriceStr, out unitPrice);

                decimal totalAmount = 0;
                decimal.TryParse(totalAmountStr, out totalAmount);

                DateTime date;
                if (!DateTime.TryParse(dateStr, out date))
                {
                    date = DateTime.Now;
                }

                LogMessage($"📦 解析入库数据：单号={orderNo}，客户={clientName}，规格={spec}，数量={quantity}");

                string businessOrderNo = orderNo;
                DatabaseManager db = new DatabaseManager();
                bool success = db.SaveInboundRecord(
                    orderNo,
                    clientCode,
                    clientName,
                    location,
                    date,
                    spec,
                    quantity,
                    unitPrice,
                    totalAmount,
                    handler,
                    creator,
                    sourceDeviceId,
                    sourceRecordId,
                    out errorMsg
                );

                if (success)
                {
                    if (!string.IsNullOrWhiteSpace(sourceRecordId))
                        orderNo = sourceRecordId;
                    LogMessage($"✅ 入库同步成功：单号={businessOrderNo}，source_record_id={sourceRecordId}，规格={spec}");
                    return true;
                }
                else
                {
                    if (!string.IsNullOrWhiteSpace(sourceRecordId) && db.ExistsInboundBySourceRecordId(sourceRecordId))
                    {
                        orderNo = sourceRecordId;
                        LogMessage($"ℹ️ 入库幂等命中：单号={businessOrderNo}，source_record_id={sourceRecordId} 已存在，按成功确认");
                        return true;
                    }
                    if (string.IsNullOrWhiteSpace(errorMsg))
                        errorMsg = "入库保存失败，请检查数据完整性";
                    if (!string.IsNullOrWhiteSpace(sourceRecordId))
                        orderNo = sourceRecordId;
                    LogMessage($"❌ {errorMsg}：单号={businessOrderNo}，source_record_id={sourceRecordId}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                errorMsg = $"处理入库数据异常: {ex.Message}";
                LogMessage($"❌ {errorMsg}");
                LogMessage($"❌ 异常堆栈: {ex.StackTrace}");
                return false;
            }
        }
        private void ProcessSalesSyncData(string jsonData, DeviceClient device)
        {
            try
            {
                LogMessage($"💰 处理销售同步数据: {jsonData}");

                var json = JObject.Parse(jsonData);

                string orderNo = json["order_no"]?.ToString() ?? json["bill_no"]?.ToString();
                string clientCode = json["client_code"]?.ToString();
                string clientName = json["client_name"]?.ToString();
                string spec = json["spec"]?.ToString() ?? json["product_spec"]?.ToString();
                string quantityStr = json["quantity"]?.ToString() ?? "0";
                string unitPriceStr = json["unit_price"]?.ToString() ?? "0";
                string totalAmountStr = json["total_amount"]?.ToString() ?? "0";
                string handler = json["handler"]?.ToString() ?? device.DeviceName;
                string creator = json["creator"]?.ToString() ?? "手持端";
                string location = json["location"]?.ToString() ?? json["location_code"]?.ToString() ?? "";
                string dateStr = json["date"]?.ToString() ?? DateTime.Now.ToString("yyyy-MM-dd");
                string sourceDeviceId = json["source_device_id"]?.ToString() ?? device.DeviceId;
                string sourceRecordId = json["source_record_id"]?.ToString() ?? "";

                int quantity = 0;
                int.TryParse(quantityStr, out quantity);

                decimal unitPrice = 0;
                decimal.TryParse(unitPriceStr, out unitPrice);

                decimal totalAmount = 0;
                decimal.TryParse(totalAmountStr, out totalAmount);

                DateTime date;
                if (!DateTime.TryParse(dateStr, out date))
                {
                    date = DateTime.Now;
                }

                LogMessage($"💰 解析销售数据：单号={orderNo}，客户={clientName}，规格={spec}，数量={quantity}");

                DatabaseManager db = new DatabaseManager();
                bool success = db.SaveSalesRecord(
                    orderNo,
                    clientCode,
                    clientName,
                    location,
                    date,
                    spec,
                    quantity,
                    unitPrice,
                    totalAmount,
                    handler,
                    creator,
                    sourceDeviceId,
                    sourceRecordId
                );

                if (success)
                {
                    LogMessage($"✅ 销售同步成功：单号={orderNo}，规格={spec}");
                }
                else
                {
                    LogMessage($"❌ 销售同步失败：单号={orderNo}");
                }
            }
            catch (Exception ex)
            {
                LogMessage($"❌ 处理销售同步数据异常: {ex.Message}");
                throw;
            }
        }
        /// <summary>
        /// 处理销售同步数据并返回处理结果（带确认机制）
        /// </summary>
        private bool ProcessSalesSyncDataWithAck(string jsonData, DeviceClient device, out string orderNo, out string errorMsg)
        {
            orderNo = "";
            errorMsg = "";

            try
            {
                LogMessage($"💰 处理销售同步数据（带确认）: {jsonData}");

                var json = JObject.Parse(jsonData);

                orderNo = json["order_no"]?.ToString() ?? json["bill_no"]?.ToString();
                string clientCode = json["client_code"]?.ToString();
                string clientName = json["client_name"]?.ToString();
                string sourceDeviceId = json["source_device_id"]?.ToString() ?? device.DeviceId;
                string sourceRecordId = json["source_record_id"]?.ToString() ?? "";
                string spec = json["spec"]?.ToString() ?? json["product_spec"]?.ToString();
                string quantityStr = json["quantity"]?.ToString() ?? "0";
                string unitPriceStr = json["unit_price"]?.ToString() ?? "0";
                string totalAmountStr = json["total_amount"]?.ToString() ?? "0";
                string handler = json["handler"]?.ToString() ?? device.DeviceName;
                string creator = json["creator"]?.ToString() ?? "手持端";
                string location = json["location"]?.ToString() ?? json["location_code"]?.ToString() ?? "";
                string dateStr = json["date"]?.ToString() ?? DateTime.Now.ToString("yyyy-MM-dd");

                if (string.IsNullOrEmpty(orderNo))
                {
                    errorMsg = "销售单号不能为空";
                    LogMessage($"❌ {errorMsg}");
                    return false;
                }

                int quantity = 0;
                int.TryParse(quantityStr, out quantity);

                decimal unitPrice = 0;
                decimal.TryParse(unitPriceStr, out unitPrice);

                decimal totalAmount = 0;
                decimal.TryParse(totalAmountStr, out totalAmount);

                DateTime date;
                if (!DateTime.TryParse(dateStr, out date))
                {
                    date = DateTime.Now;
                }

                LogMessage($"💰 解析销售数据：单号={orderNo}，客户={clientName}，规格={spec}，数量={quantity}");

                string businessOrderNo = orderNo;
                DatabaseManager db = new DatabaseManager();
                bool success = db.SaveSalesRecord(
                    orderNo,
                    clientCode,
                    clientName,
                    location,
                    date,
                    spec,
                    quantity,
                    unitPrice,
                    totalAmount,
                    handler,
                    creator,
                    sourceDeviceId,
                    sourceRecordId,
                    out errorMsg
                );

                if (success)
                {
                    if (!string.IsNullOrWhiteSpace(sourceRecordId))
                        orderNo = sourceRecordId;
                    LogMessage($"✅ 销售同步成功：单号={businessOrderNo}，source_record_id={sourceRecordId}，规格={spec}");
                    return true;
                }
                else
                {
                    if (!string.IsNullOrWhiteSpace(sourceRecordId) && db.ExistsSalesBySourceRecordId(sourceRecordId))
                    {
                        orderNo = sourceRecordId;
                        LogMessage($"ℹ️ 销售幂等命中：单号={businessOrderNo}，source_record_id={sourceRecordId} 已存在，按成功确认");
                        return true;
                    }
                    if (string.IsNullOrWhiteSpace(errorMsg))
                        errorMsg = "销售保存失败，请检查数据完整性";
                    if (!string.IsNullOrWhiteSpace(sourceRecordId))
                        orderNo = sourceRecordId;
                    LogMessage($"❌ {errorMsg}：单号={businessOrderNo}，source_record_id={sourceRecordId}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                errorMsg = $"处理销售数据异常: {ex.Message}";
                LogMessage($"❌ {errorMsg}");
                return false;
            }
        }
        private void ProcessPackagingSyncData(string jsonData, DeviceClient device)
        {
            try
            {
                LogMessage($"📦 处理包装同步数据: {jsonData}");

                var json = JObject.Parse(jsonData);

                string orderNo = json["order_no"]?.ToString() ?? json["bill_no"]?.ToString();
                string clientCode = json["client_code"]?.ToString();
                string clientName = json["client_name"]?.ToString();

                // 获取出进包装标记（TAKE-出包装, RETURN-进包装）
                // 从手持端可能以不同字段名发送，兼容多种可能性
                string packFlag = json["pack_flag"]?.ToString() ??
                                  json["packagingTypeFlag"]?.ToString() ??
                                  json["packaging_type_flag"]?.ToString() ??
                                  "TAKE"; // 默认TAKE

                string packType = json["pack_type"]?.ToString() ??
                                  json["packagingType"]?.ToString() ??
                                  json["packaging_type"]?.ToString() ?? "";

                string quantityStr = json["quantity"]?.ToString() ?? "0";
                string unitPriceStr = json["unit_price"]?.ToString() ?? "0";
                string totalAmountStr = json["total_amount"]?.ToString() ?? "0";
                string handler = json["handler"]?.ToString() ?? device.DeviceName;
                string creator = json["creator"]?.ToString() ?? "手持端";
                string dateStr = json["date"]?.ToString() ?? DateTime.Now.ToString("yyyy-MM-dd");
                string sourceDeviceId = json["source_device_id"]?.ToString() ?? device.DeviceId;
                string sourceRecordId = json["source_record_id"]?.ToString() ?? "";

                // 解析数值
                int quantity = 0;
                int.TryParse(quantityStr, out quantity);

                decimal unitPrice = 0;
                decimal.TryParse(unitPriceStr, out unitPrice);

                decimal totalAmount = 0;
                decimal.TryParse(totalAmountStr, out totalAmount);

                LogMessage($"📦 解析包装数据：单号={orderNo}，客户={clientName}，类型={packType}，数量={quantity}，标记={packFlag}");

                // 调用您提供的 SavePackagingRecord 方法
                DatabaseManager db = new DatabaseManager();
                bool success = db.SavePackagingRecord(
                    orderNo: orderNo,
                    clientCode: clientCode ?? "",
                    clientName: clientName ?? "",
                    packType: packType,
                    packFlag: packFlag,  // 传递出进包装标记
                    quantity: quantity,
                    unitPrice: unitPrice,
                    totalAmount: totalAmount,
                    handler: handler ?? "",
                    creator: creator ?? "",
                    sourceDeviceId: sourceDeviceId,
                    sourceRecordId: sourceRecordId
                );

                if (success)
                {
                    LogMessage($"✅ 包装同步成功：单号={orderNo}，类型={packType}，标记={packFlag}");

                    // 可选：向手持端发送成功确认
                    try
                    {
                        SendMessageSafely(device, $"SYNC_OK|PACKAGING|{orderNo}");
                    }
                    catch { }
                }
                else
                {
                    LogMessage($"❌ 包装同步失败：单号={orderNo}");
                    try
                    {
                        SendMessageSafely(device, $"SYNC_ERROR|包装保存失败|{orderNo}");
                    }
                    catch { }
                }
            }
            catch (Exception ex)
            {
                LogMessage($"❌ 处理包装同步数据异常: {ex.Message}");
                LogMessage($"❌ 异常堆栈: {ex.StackTrace}");

                // 向手持端发送错误信息
                try
                {
                    SendMessageSafely(device, $"SYNC_ERROR|{ex.Message}");
                }
                catch { }
            }
        }
        /// <summary>
        /// 处理包装同步数据并返回处理结果（带确认机制）
        /// </summary>
        private bool ProcessPackagingSyncDataWithAck(string jsonData, DeviceClient device, out string orderNo, out string errorMsg,
            out PackagingSaveOutcome saveOutcome)
        {
            orderNo = "";
            errorMsg = "";
            saveOutcome = PackagingSaveOutcome.OtherError;

            try
            {
                LogMessage($"📦 处理包装同步数据（带确认）: {jsonData}");

                var json = JObject.Parse(jsonData);

                orderNo = json["order_no"]?.ToString() ?? json["bill_no"]?.ToString();
                string clientCode = json["client_code"]?.ToString();
                string clientName = json["client_name"]?.ToString();
                string sourceDeviceId = json["source_device_id"]?.ToString() ?? device.DeviceId;
                string sourceRecordId = json["source_record_id"]?.ToString() ?? "";

                // 获取出进包装标记
                string packFlag = json["pack_flag"]?.ToString() ??
                                  json["packagingTypeFlag"]?.ToString() ??
                                  json["packaging_type_flag"]?.ToString() ??
                                  "TAKE";

                string packType = json["pack_type"]?.ToString() ??
                                  json["packagingType"]?.ToString() ??
                                  json["packaging_type"]?.ToString() ?? "";

                string quantityStr = json["quantity"]?.ToString() ?? "0";
                string unitPriceStr = json["unit_price"]?.ToString() ?? "0";
                string totalAmountStr = json["total_amount"]?.ToString() ?? "0";
                string handler = json["handler"]?.ToString() ?? device.DeviceName;
                string creator = json["creator"]?.ToString() ?? "手持端";
                string dateStr = json["date"]?.ToString() ?? DateTime.Now.ToString("yyyy-MM-dd");

                if (string.IsNullOrEmpty(orderNo))
                {
                    errorMsg = "包装单号不能为空";
                    LogMessage($"❌ {errorMsg}");
                    return false;
                }

                // 解析数值
                int quantity = 0;
                int.TryParse(quantityStr, out quantity);

                decimal unitPrice = 0;
                decimal.TryParse(unitPriceStr, out unitPrice);

                decimal totalAmount = 0;
                decimal.TryParse(totalAmountStr, out totalAmount);

                LogMessage($"📦 解析包装数据：单号={orderNo}，客户={clientName}，类型={packType}，数量={quantity}，标记={packFlag}");

                string businessOrderNo = orderNo;
                DatabaseManager db = new DatabaseManager();
                bool success = db.SavePackagingRecord(
                    orderNo: orderNo,
                    clientCode: clientCode ?? "",
                    clientName: clientName ?? "",
                    packType: packType,
                    packFlag: packFlag,
                    quantity: quantity,
                    unitPrice: unitPrice,
                    totalAmount: totalAmount,
                    handler: handler ?? "",
                    creator: creator ?? "",
                    sourceDeviceId: sourceDeviceId,
                    sourceRecordId: sourceRecordId,
                    out errorMsg,
                    out saveOutcome
                );

                if (success)
                {
                    if (!string.IsNullOrWhiteSpace(sourceRecordId))
                        orderNo = sourceRecordId;
                    if (saveOutcome == PackagingSaveOutcome.IdempotentMatch)
                    {
                        LogMessage($"ℹ️ 包装幂等命中：单号={businessOrderNo}，source_record_id={sourceRecordId}，{errorMsg}");
                    }
                    else
                    {
                        LogMessage($"✅ 包装同步成功：单号={businessOrderNo}，source_record_id={sourceRecordId}，类型={packType}，标记={packFlag}");
                    }
                    return true;
                }
                else
                {
                    if (string.IsNullOrWhiteSpace(errorMsg))
                    {
                        errorMsg = "包装保存失败，请检查数据完整性";
                    }
                    if (!string.IsNullOrWhiteSpace(sourceRecordId))
                        orderNo = sourceRecordId;
                    LogMessage($"❌ {errorMsg}：单号={businessOrderNo}，source_record_id={sourceRecordId}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                errorMsg = $"处理包装数据异常: {ex.Message}";
                LogMessage($"❌ {errorMsg}");
                LogMessage($"❌ 异常堆栈: {ex.StackTrace}");
                return false;
            }
        }

        private static string SanitizeAckDetail(string detail)
        {
            if (string.IsNullOrWhiteSpace(detail))
            {
                return "";
            }

            return detail.Replace("|", " ").Replace("\r", " ").Replace("\n", " ").Trim();
        }

        // 新增：处理预支款同步数据（带确认机制）
        private bool ProcessAdvanceSyncDataWithAck(string jsonData, DeviceClient device, out string clientCode, out string errorMsg)
        {
            clientCode = "";
            errorMsg = "";

            try
            {
                LogMessage($"💰 处理预支款同步数据（带确认）: {jsonData}");

                var json = JObject.Parse(jsonData);

                clientCode = json["client_code"]?.ToString();
                string clientName = json["client_name"]?.ToString();
                string sourceDeviceId = json["source_device_id"]?.ToString() ?? device.DeviceId;
                string sourceRecordId = json["source_record_id"]?.ToString() ?? "";
                string amountStr = json["amount"]?.ToString() ?? "0";
                string advanceDate = json["advance_date"]?.ToString() ?? DateTime.Now.ToString("yyyy-MM-dd");
                string reason = json["reason"]?.ToString() ?? "";
                string handler = json["handler"]?.ToString() ?? device.DeviceName;
                string creator = json["creator"]?.ToString() ?? "手持端";
                string statusStr = json["status"]?.ToString() ?? "1";
                string createdTime = json["created_time"]?.ToString();

                if (string.IsNullOrEmpty(clientCode))
                {
                    errorMsg = "客户编号不能为空";
                    LogMessage($"❌ {errorMsg}");
                    return false;
                }

                if (string.IsNullOrEmpty(clientName))
                {
                    errorMsg = "客户名称不能为空";
                    LogMessage($"❌ {errorMsg}");
                    return false;
                }

                decimal amount = 0;
                decimal.TryParse(amountStr, out amount);

                if (amount <= 0)
                {
                    errorMsg = "预支款金额必须大于0";
                    LogMessage($"❌ {errorMsg}");
                    return false;
                }

                long status = 1;
                long.TryParse(statusStr, out status);

                LogMessage($"💰 解析预支款数据：客户={clientName}({clientCode})，金额={amount}，日期={advanceDate}");

                string businessClientCode = clientCode;
                DatabaseManager db = new DatabaseManager();
                bool success = db.SaveAdvanceRecord(
                    clientCode: clientCode,
                    clientName: clientName,
                    amount: amount,
                    advanceDate: advanceDate,
                    reason: reason,
                    handler: handler,
                    creator: creator,
                    status: status,
                    sourceDeviceId: sourceDeviceId,
                    sourceRecordId: sourceRecordId,
                    createdTime: createdTime,
                    out errorMsg
                );

                if (success)
                {
                    if (!string.IsNullOrWhiteSpace(sourceRecordId))
                        clientCode = sourceRecordId;
                    LogMessage($"✅ 预支款同步成功：客户={clientName}，source_record_id={sourceRecordId}，金额={amount}");
                    return true;
                }
                else
                {
                    if (string.IsNullOrWhiteSpace(errorMsg))
                        errorMsg = "预支款保存失败，请检查数据完整性";
                    if (!string.IsNullOrWhiteSpace(sourceRecordId))
                        clientCode = sourceRecordId;
                    LogMessage($"❌ {errorMsg}：客户={clientName}({businessClientCode})，source_record_id={sourceRecordId}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                errorMsg = $"处理预支款数据异常: {ex.Message}";
                LogMessage($"❌ {errorMsg}");
                LogMessage($"❌ 异常堆栈: {ex.StackTrace}");
                return false;
            }
        }

        // 新增：处理扣款同步数据（带确认机制）
        private bool ProcessDeductionSyncDataWithAck(string jsonData, DeviceClient device, out string clientCode, out string errorMsg)
        {
            clientCode = "";
            errorMsg = "";

            try
            {
                LogMessage($"💸 处理扣款同步数据（带确认）: {jsonData}");

                var json = JObject.Parse(jsonData);

                clientCode = json["client_code"]?.ToString();
                string clientName = json["client_name"]?.ToString();
                string sourceDeviceId = json["source_device_id"]?.ToString() ?? device.DeviceId;
                string sourceRecordId = json["source_record_id"]?.ToString() ?? "";
                string amountStr = json["amount"]?.ToString() ?? "0";
                string quantityStr = json["quantity"]?.ToString() ?? "0";
                string unitPriceStr = json["unit_price"]?.ToString() ?? "0";
                string deductDate = json["deduct_date"]?.ToString() ?? DateTime.Now.ToString("yyyy-MM-dd");
                string reason = json["reason"]?.ToString() ?? "";
                string handler = json["handler"]?.ToString() ?? device.DeviceName;
                string creator = json["creator"]?.ToString() ?? "手持端";
                string statusStr = json["status"]?.ToString() ?? "1";
                string createdTime = json["created_time"]?.ToString();

                if (string.IsNullOrEmpty(clientCode))
                {
                    errorMsg = "客户编号不能为空";
                    LogMessage($"❌ {errorMsg}");
                    return false;
                }

                if (string.IsNullOrEmpty(clientName))
                {
                    errorMsg = "客户名称不能为空";
                    LogMessage($"❌ {errorMsg}");
                    return false;
                }

                decimal amount = 0;
                decimal.TryParse(amountStr, out amount);
                int quantity = 0;
                int.TryParse(quantityStr, out quantity);
                decimal unitPrice = 0;
                decimal.TryParse(unitPriceStr, out unitPrice);

                if (amount <= 0)
                {
                    errorMsg = "扣款金额必须大于0";
                    LogMessage($"❌ {errorMsg}");
                    return false;
                }

                long status = 1;
                long.TryParse(statusStr, out status);

                LogMessage($"💸 解析扣款数据：客户={clientName}({clientCode})，数量={quantity}，单价={unitPrice}，金额={amount}，日期={deductDate}");

                string businessClientCode = clientCode;
                DatabaseManager db = new DatabaseManager();
                bool success = db.SaveDeductionRecord(
                    clientCode: clientCode,
                    clientName: clientName,
                    amount: amount,
                    deductDate: deductDate,
                    reason: reason,
                    handler: handler,
                    creator: creator,
                    status: status,
                    sourceDeviceId: sourceDeviceId,
                    sourceRecordId: sourceRecordId,
                    quantity: quantity,
                    unitPrice: unitPrice,
                    createdTime: createdTime,
                    out errorMsg
                );

                if (success)
                {
                    if (!string.IsNullOrWhiteSpace(sourceRecordId))
                        clientCode = sourceRecordId;
                    LogMessage($"✅ 扣款同步成功：客户={clientName}，source_record_id={sourceRecordId}，金额={amount}");
                    return true;
                }
                else
                {
                    if (string.IsNullOrWhiteSpace(errorMsg))
                        errorMsg = "扣款保存失败，请检查数据完整性";
                    if (!string.IsNullOrWhiteSpace(sourceRecordId))
                        clientCode = sourceRecordId;
                    LogMessage($"❌ {errorMsg}：客户={clientName}({businessClientCode})，source_record_id={sourceRecordId}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                errorMsg = $"处理扣款数据异常: {ex.Message}";
                LogMessage($"❌ {errorMsg}");
                LogMessage($"❌ 异常堆栈: {ex.StackTrace}");
                return false;
            }
        }

        private static bool ValidatePresaleFiscalYear(JObject json, out string errorMsg)
        {
            errorMsg = null;
            if (!FiscalYearService.IsInitialized) return true;
            var token = json["fiscal_year"];
            if (token == null || token.Type == JTokenType.Null) return true;
            if (int.TryParse(token.ToString(), out int fiscalYear) && fiscalYear != FiscalYearService.ActiveYear)
            {
                errorMsg = $"年份不一致：上传年份={fiscalYear}，PC活跃年份={FiscalYearService.ActiveYear}";
                return false;
            }
            return true;
        }

        private bool ProcessPresaleSyncDataWithAck(string jsonData, DeviceClient device, out string billNo, out string errorMsg)
        {
            billNo = "";
            errorMsg = "";
            try
            {
                LogMessage($"📦 处理预售单同步数据（带确认）: {jsonData}");
                var json = JObject.Parse(jsonData);
                if (!ValidatePresaleFiscalYear(json, out errorMsg))
                {
                    LogMessage($"❌ 预售单年份校验失败: {errorMsg}");
                    return false;
                }
                billNo = json["bill_no"]?.ToString() ?? "";
                string sourceRecordId = json["source_record_id"]?.ToString() ?? "";
                if (string.IsNullOrWhiteSpace(billNo))
                {
                    errorMsg = "预售单号不能为空";
                    return false;
                }

                string businessBillNo = billNo;
                DatabaseManager db = new DatabaseManager();
                string incomingSaleMode = json["sale_mode"]?.ToString() ?? "";
                string incomingStatus = json["bill_status"]?.ToString() ?? json["status"]?.ToString() ?? "";
                bool success = db.SavePresaleBillFromSync(jsonData, out errorMsg);

                if (success)
                {
                    if (!string.IsNullOrWhiteSpace(sourceRecordId))
                        billNo = sourceRecordId;
                    LogMessage($"✅ 预售单已落库: 单号={businessBillNo}，source_record_id={sourceRecordId}，mode={incomingSaleMode} status={incomingStatus}");
                    AppendPresaleChangeLogFromBillNo(db, businessBillNo, json["source_device_id"]?.ToString() ?? device?.DeviceId ?? "");
                    return true;
                }

                if (string.IsNullOrWhiteSpace(errorMsg))
                    errorMsg = "预售单保存失败";
                if (!string.IsNullOrWhiteSpace(sourceRecordId))
                    billNo = sourceRecordId;
                LogMessage($"❌ {errorMsg}：单号={businessBillNo}，source_record_id={sourceRecordId}");
                return false;
            }
            catch (Exception ex)
            {
                errorMsg = $"处理预售单数据异常: {ex.Message}";
                LogMessage($"❌ {errorMsg}");
                return false;
            }
        }

        private bool ProcessPresaleOutboundSyncDataWithAck(string jsonData, DeviceClient device, out string billNo, out string errorMsg)
        {
            billNo = "";
            errorMsg = "";
            try
            {
                LogMessage($"📦 处理预售出库同步数据（带确认）: {jsonData}");
                var json = JObject.Parse(jsonData);
                if (!ValidatePresaleFiscalYear(json, out errorMsg))
                {
                    LogMessage($"❌ 预售出库年份校验失败: {errorMsg}");
                    return false;
                }
                billNo = json["bill_no"]?.ToString() ?? "";
                string sourceRecordId = json["source_record_id"]?.ToString() ?? "";
                if (string.IsNullOrWhiteSpace(billNo))
                {
                    errorMsg = "预售单号不能为空";
                    return false;
                }

                DatabaseManager db = new DatabaseManager();
                bool success = db.SavePresaleOutboundFromSync(jsonData);
                if (!string.IsNullOrWhiteSpace(sourceRecordId))
                    billNo = sourceRecordId;

                if (!success)
                {
                    errorMsg = "预售出库保存失败";
                    return false;
                }
                AppendPresaleChangeLogFromJson(jsonData, "PRESALE_OUTBOUND", json["source_device_id"]?.ToString() ?? device?.DeviceId ?? "");
                return true;
            }
            catch (Exception ex)
            {
                errorMsg = $"处理预售出库数据异常: {ex.Message}";
                LogMessage($"❌ {errorMsg}");
                return false;
            }
        }

        private bool ProcessPresalePaymentSyncDataWithAck(string jsonData, DeviceClient device, out string billNo, out string errorMsg)
        {
            billNo = "";
            errorMsg = "";
            try
            {
                LogMessage($"💰 处理预售收款同步数据（带确认）: {jsonData}");
                var json = JObject.Parse(jsonData);
                if (!ValidatePresaleFiscalYear(json, out errorMsg))
                {
                    LogMessage($"❌ 预售收款年份校验失败: {errorMsg}");
                    return false;
                }
                billNo = json["bill_no"]?.ToString() ?? "";
                string sourceRecordId = json["source_record_id"]?.ToString() ?? "";
                if (string.IsNullOrWhiteSpace(billNo))
                {
                    errorMsg = "预售单号不能为空";
                    return false;
                }

                DatabaseManager db = new DatabaseManager();
                bool success = db.SavePresalePaymentFromSync(jsonData);
                if (!string.IsNullOrWhiteSpace(sourceRecordId))
                    billNo = sourceRecordId;

                if (!success)
                {
                    errorMsg = "预售收款保存失败";
                    return false;
                }
                AppendPresaleChangeLogFromJson(jsonData, "PRESALE_PAYMENT", json["source_device_id"]?.ToString() ?? device?.DeviceId ?? "");
                return true;
            }
            catch (Exception ex)
            {
                errorMsg = $"处理预售收款数据异常: {ex.Message}";
                LogMessage($"❌ {errorMsg}");
                return false;
            }
        }

        private bool ProcessLedgerSyncDataWithAck(string jsonData, DeviceClient device, out string entryNo, out string errorMsg)
        {
            entryNo = "";
            errorMsg = "";
            try
            {
                LogMessage($"📒 处理收支流水同步数据（带确认）: {jsonData}");
                var json = JObject.Parse(jsonData);
                entryNo = json["entry_no"]?.ToString() ?? "";
                string sourceRecordId = json["source_record_id"]?.ToString() ?? "";
                if (string.IsNullOrWhiteSpace(entryNo))
                {
                    errorMsg = "流水号不能为空";
                    return false;
                }

                DatabaseManager db = new DatabaseManager();
                bool success = db.SaveLedgerEntryFromSync(jsonData);
                if (!string.IsNullOrWhiteSpace(sourceRecordId))
                    entryNo = sourceRecordId;

                if (!success)
                {
                    errorMsg = "收支流水保存失败";
                    return false;
                }
                return true;
            }
            catch (Exception ex)
            {
                errorMsg = $"处理收支流水数据异常: {ex.Message}";
                LogMessage($"❌ {errorMsg}");
                return false;
            }
        }

        private void ProcessPresaleSyncData(string jsonData, DeviceClient device)
        {
            try
            {
                DatabaseManager db = new DatabaseManager();
                if (db.SavePresaleBillFromSync(jsonData))
                    LogMessage("✅ 预售单同步成功（无确认模式）");
            }
            catch (Exception ex)
            {
                LogMessage($"❌ 预售单同步失败: {ex.Message}");
            }
        }

        private void ProcessPresaleOutboundSyncData(string jsonData, DeviceClient device)
        {
            try
            {
                DatabaseManager db = new DatabaseManager();
                if (db.SavePresaleOutboundFromSync(jsonData))
                    LogMessage("✅ 预售出库同步成功（无确认模式）");
            }
            catch (Exception ex)
            {
                LogMessage($"❌ 预售出库同步失败: {ex.Message}");
            }
        }

        private void ProcessPresalePaymentSyncData(string jsonData, DeviceClient device)
        {
            try
            {
                DatabaseManager db = new DatabaseManager();
                if (db.SavePresalePaymentFromSync(jsonData))
                    LogMessage("✅ 预售收款同步成功（无确认模式）");
            }
            catch (Exception ex)
            {
                LogMessage($"❌ 预售收款同步失败: {ex.Message}");
            }
        }

        private void ProcessLedgerSyncData(string jsonData, DeviceClient device)
        {
            try
            {
                DatabaseManager db = new DatabaseManager();
                if (db.SaveLedgerEntryFromSync(jsonData))
                    LogMessage("✅ 收支流水同步成功（无确认模式）");
            }
            catch (Exception ex)
            {
                LogMessage($"❌ 收支流水同步失败: {ex.Message}");
            }
        }

        private void ProcessCustomerSyncData(string jsonData, DeviceClient device)
        {
            try
            {
                LogMessage($"👤 处理客户同步数据: {jsonData}");

                var json = JObject.Parse(jsonData);

                string code = json["code"]?.ToString();
                string name = json["name"]?.ToString();
                string phone = json["phone"]?.ToString() ?? "";
                string contact = json["contact"]?.ToString() ?? "";
                string address = json["address"]?.ToString() ?? "";
                string customerType = json["customer_type"]?.ToString();
                int? status = null;
                if (json["enabled"] != null)
                    status = json["enabled"].ToObject<bool>() ? 1 : 0;
                else if (json["status"] != null)
                    status = json["status"].ToObject<int>() != 0 ? 1 : 0;

                LogMessage($"👤 解析客户数据：编号={code}，名称={name}，电话={phone}");

                DatabaseManager db = new DatabaseManager();
                bool success = db.UpsertClientFromSync(code, name, contact, phone, address, customerType, status);
                if (success)
                {
                    LogMessage($"✅ 客户/买家保存成功：{name} ({code})");
                    var syncPayload = new JObject
                    {
                        ["code"] = code,
                        ["name"] = name,
                        ["phone"] = phone,
                        ["customer_type"] = customerType ?? "",
                        ["enabled"] = status == null || status != 0
                    };
                    string originOpId = $"legacy_{device.DeviceId}_{code}_{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";
                    AppendChangeLog("CUSTOMER", code, "UPSERT", syncPayload, device.DeviceId, originOpId);
                    SaveDeltaSyncState();
                }
            }
            catch (Exception ex)
            {
                LogMessage($"❌ 处理客户同步数据异常: {ex.Message}");
            }
        }

        private void ProcessProductSyncData(string jsonData, DeviceClient device)
        {
            try
            {
                LogMessage($"📦 处理商品同步数据: {jsonData}");

                var json = JObject.Parse(jsonData);

                string name = json["name"]?.ToString();
                string code = json["code"]?.ToString() ?? "";
                string category = json["category"]?.ToString() ?? "";
                bool isActive = json["is_active"]?.ToString() == "true";

                LogMessage($"📦 解析商品数据：名称={name}，编号={code}");

                DatabaseManager db = new DatabaseManager();

                bool success = db.AddProductType(name, isActive);
                if (success)
                {
                    LogMessage($"✅ 商品添加成功：{name}");
                }
                else
                {
                    LogMessage($"⚠️ 商品已存在或添加失败：{name}");
                }
            }
            catch (Exception ex)
            {
                LogMessage($"❌ 处理商品同步数据异常: {ex.Message}");
            }
        }

        private void ProcessLocationSyncData(string jsonData, DeviceClient device)
        {
            try
            {
                LogMessage($"📍 处理库位同步数据: {jsonData}");

                var json = JObject.Parse(jsonData);

                string name = json["name"]?.ToString();
                string code = json["code"]?.ToString() ?? "";
                string description = json["description"]?.ToString() ?? "";
                bool isActive = json["is_active"]?.ToString() == "true";

                LogMessage($"📍 解析库位数据：名称={name}，编号={code}");

                DatabaseManager db = new DatabaseManager();

                bool success = db.AddLocation(name, description);
                if (success)
                {
                    LogMessage($"✅ 库位添加成功：{name}");
                }
                else
                {
                    LogMessage($"⚠️ 库位已存在或添加失败：{name}");
                }
            }
            catch (Exception ex)
            {
                LogMessage($"❌ 处理库位同步数据异常: {ex.Message}");
            }
        }

        private void ProcessOperatorSyncData(string jsonData, DeviceClient device)
        {
            try
            {
                LogMessage($"👤 处理经手人同步数据: {jsonData}");

                var json = JObject.Parse(jsonData);

                string name = json["name"]?.ToString();
                string code = json["code"]?.ToString() ?? "";
                string phone = json["phone"]?.ToString() ?? "";
                bool isActive = json["is_active"]?.ToString() == "true";

                LogMessage($"👤 解析经手人数据：姓名={name}，编号={code}");

                DatabaseManager db = new DatabaseManager();

                bool success = db.AddHandler(name);
                if (success)
                {
                    LogMessage($"✅ 经手人添加成功：{name}");
                }
                else
                {
                    LogMessage($"⚠️ 经手人已存在或添加失败：{name}");
                }
            }
            catch (Exception ex)
            {
                LogMessage($"❌ 处理经手人同步数据异常: {ex.Message}");
            }
        }
        #endregion

        /// <summary>
        /// 处理客户同步数据并返回处理结果（带确认机制）
        /// </summary>
        // 处理客户同步数据
        private bool ProcessCustomerSyncDataWithAck(string jsonData, DeviceClient device, out string orderNo, out string errorMsg)
        {
            orderNo = "";
            errorMsg = "";

            try
            {
                LogMessage($"👤 处理客户同步数据（带确认）: {jsonData}");

                var json = JObject.Parse(jsonData);

                string code = json["code"]?.ToString();
                string name = json["name"]?.ToString();
                string phone = json["phone"]?.ToString() ?? "";
                string contact = json["contact"]?.ToString() ?? "";
                string address = json["address"]?.ToString() ?? "";
                string customerType = json["customer_type"]?.ToString();
                int? status = null;
                if (json["enabled"] != null)
                    status = json["enabled"].ToObject<bool>() ? 1 : 0;
                else if (json["status"] != null)
                    status = json["status"].ToObject<int>() != 0 ? 1 : 0;

                if (string.IsNullOrEmpty(code))
                {
                    errorMsg = "客户编号不能为空";
                    LogMessage($"❌ {errorMsg}");
                    return false;
                }

                if (string.IsNullOrEmpty(name))
                {
                    errorMsg = "客户名称不能为空";
                    LogMessage($"❌ {errorMsg}");
                    return false;
                }

                LogMessage($"👤 解析客户数据：编号={code}，名称={name}");

                DatabaseManager db = new DatabaseManager();
                bool success = db.UpsertClientFromSync(code, name, contact, phone, address, customerType, status);

                if (success)
                {
                    LogMessage($"✅ 客户/买家保存成功：{name} ({code})");
                }
                else
                {
                    errorMsg = "客户保存失败";
                    LogMessage($"❌ {errorMsg}：{name} ({code})");
                }

                orderNo = code;
                return success;
            }
            catch (Exception ex)
            {
                errorMsg = $"处理客户数据异常: {ex.Message}";
                LogMessage($"❌ {errorMsg}");
                return false;
            }
        }

        /// <summary>
        /// 处理商品同步数据并返回处理结果（带确认机制）
        /// </summary>
        private bool ProcessProductSyncDataWithAck(string jsonData, DeviceClient device, out string orderNo, out string errorMsg)
        {
            orderNo = "";
            errorMsg = "";

            try
            {
                LogMessage($"📦 处理商品同步数据（带确认）: {jsonData}");

                var json = JObject.Parse(jsonData);

                string name = json["name"]?.ToString();
                string code = json["code"]?.ToString() ?? "";
                string category = json["category"]?.ToString() ?? "";
                bool isActive = json["is_active"]?.ToString() == "true";

                if (string.IsNullOrEmpty(name))
                {
                    errorMsg = "商品名称不能为空";
                    LogMessage($"❌ {errorMsg}");
                    return false;
                }

                LogMessage($"📦 解析商品数据：名称={name}，编号={code}");

                DatabaseManager db = new DatabaseManager();
                bool success = db.AddProductType(name, isActive);

                if (success)
                {
                    LogMessage($"✅ 商品添加成功：{name}");
                    orderNo = code ?? name;
                }
                else
                {
                    errorMsg = "商品已存在或添加失败";
                    LogMessage($"⚠️ {errorMsg}：{name}");
                }

                return success;
            }
            catch (Exception ex)
            {
                errorMsg = $"处理商品数据异常: {ex.Message}";
                LogMessage($"❌ {errorMsg}");
                return false;
            }
        }

        // 处理库位同步数据
        private bool ProcessLocationSyncDataWithAck(string jsonData, DeviceClient device, out string orderNo, out string errorMsg)
        {
            orderNo = "";
            errorMsg = "";

            try
            {
                LogMessage($"📍 处理库位同步数据（带确认）: {jsonData}");

                var json = JObject.Parse(jsonData);

                string name = json["name"]?.ToString();
                string description = json["description"]?.ToString() ?? "";

                if (string.IsNullOrEmpty(name))
                {
                    errorMsg = "库位名称不能为空";
                    LogMessage($"❌ {errorMsg}");
                    return false;
                }

                LogMessage($"📍 解析库位数据：名称={name}");

                DatabaseManager db = new DatabaseManager();
                bool success = db.AddLocation(name, description);

                if (success)
                {
                    LogMessage($"✅ 库位添加成功：{name}");
                    orderNo = name;
                    return true;
                }
                else
                {
                    errorMsg = "库位保存失败";
                    LogMessage($"❌ {errorMsg}：{name}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                errorMsg = $"处理库位数据异常: {ex.Message}";
                LogMessage($"❌ {errorMsg}");
                return false;
            }
        }

        // 处理经手人同步数据
        private bool ProcessOperatorSyncDataWithAck(string jsonData, DeviceClient device, out string orderNo, out string errorMsg)
        {
            orderNo = "";
            errorMsg = "";

            try
            {
                LogMessage($"👤 处理经手人同步数据（带确认）: {jsonData}");

                var json = JObject.Parse(jsonData);

                string name = json["name"]?.ToString();

                if (string.IsNullOrEmpty(name))
                {
                    errorMsg = "经手人姓名不能为空";
                    LogMessage($"❌ {errorMsg}");
                    return false;
                }

                LogMessage($"👤 解析经手人数据：姓名={name}");

                DatabaseManager db = new DatabaseManager();
                bool success = db.AddHandler(name);

                if (success)
                {
                    LogMessage($"✅ 经手人添加成功：{name}");
                    orderNo = name;
                    return true;
                }
                else
                {
                    errorMsg = "经手人保存失败";
                    LogMessage($"⚠️ {errorMsg}：{name}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                errorMsg = $"处理经手人数据异常: {ex.Message}";
                LogMessage($"❌ {errorMsg}");
                return false;
            }
        }

        #region 待同步数据获取
        private List<string> GetPendingData(string deviceId)
        {
            LogMessage($"ℹ️ GetPendingData 已停用：PC 不再向手持下发业务单据，device={deviceId}");
            return new List<string>();
        }

        private static string HashLegacySyncLine(string syncData)
        {
            using (var sha = SHA256.Create())
            {
                return Convert.ToBase64String(sha.ComputeHash(Encoding.UTF8.GetBytes(syncData ?? "")));
            }
        }

        private static string ComputeStableLegacyKey(string syncData)
        {
            if (string.IsNullOrEmpty(syncData)) return "";
            int pipe = syncData.IndexOf('|');
            if (pipe <= 0 || pipe >= syncData.Length - 1) return syncData;
            string kind = syncData.Substring(0, pipe);
            string json = syncData.Substring(pipe + 1);
            try
            {
                var jo = JObject.Parse(json);
                switch (kind)
                {
                    case "INBOUND":
                        return $"INBOUND|{jo["bill_no"]}|{jo["product_spec"]}";
                    case "SALES":
                        return $"SALES|{jo["order_no"]}|{jo["spec"]}";
                    case "PACKAGING":
                        return $"PACKAGING|{jo["order_no"]}|{jo["pack_type"]}|{jo["pack_flag"]}";
                    case "ADVANCE":
                        return $"ADVANCE|{jo["client_code"]}|{jo["advance_date"]}|{jo["amount"]}|{jo["reason"]}|{jo["handler"]}";
                    case "DEDUCTION":
                        return $"DEDUCTION|{jo["client_code"]}|{jo["deduct_date"]}|{jo["amount"]}|{jo["quantity"]}|{jo["unit_price"]}|{jo["reason"]}|{jo["handler"]}";
                    case "PRESALE":
                        return $"PRESALE|{jo["bill_no"]}|{jo["bill_status"]}|{jo["paid_amount"]}|{jo["total_amount"]}";
                    case "PRESALE_PAYMENT":
                        return $"PRESALE_PAYMENT|{jo["bill_no"]}|{jo["pay_time"]}|{jo["amount"]}|{jo["pay_method"]}";
                    case "PRESALE_OUTBOUND":
                        return $"PRESALE_OUTBOUND|{jo["bill_no"]}|{jo["ship_time"]}|{jo["items"]}";
                    case "LEDGER":
                        return $"LEDGER|{jo["entry_no"]}|{jo["type"]}|{jo["amount"]}|{jo["entry_date"]}|{jo["status"]}";
                    default:
                        return syncData;
                }
            }
            catch
            {
                return syncData;
            }
        }

        private ConcurrentDictionary<string, string> GetOrCreateLegacyFingerprintMap(string deviceId)
        {
            return deviceLegacyPushFingerprints.GetOrAdd(deviceId ?? "", _ => new ConcurrentDictionary<string, string>());
        }

        private List<string> FilterLegacyPending(string deviceId, List<string> raw)
        {
            if (raw == null || raw.Count == 0) return raw ?? new List<string>();
            var map = GetOrCreateLegacyFingerprintMap(deviceId);
            var result = new List<string>(raw.Count);
            int skipped = 0;
            foreach (var line in raw)
            {
                string key = ComputeStableLegacyKey(line);
                string hash = HashLegacySyncLine(line);
                if (map.TryGetValue(key, out var oldHash) && oldHash == hash)
                {
                    skipped++;
                    continue;
                }
                result.Add(line);
            }
            return result;
        }

        private void RecordLegacyPushFingerprints(string deviceId, List<string> sentLines)
        {
            if (string.IsNullOrEmpty(deviceId) || sentLines == null || sentLines.Count == 0) return;
            var map = GetOrCreateLegacyFingerprintMap(deviceId);
            foreach (var line in sentLines)
            {
                string key = ComputeStableLegacyKey(line);
                map[key] = HashLegacySyncLine(line);
            }
            SaveDeltaSyncState();
        }

        private List<string> GetLocalIPAddresses()
        {
            var addresses = new List<string>();
            var host = Dns.GetHostEntry(Dns.GetHostName());
            foreach (var ip in host.AddressList)
            {
                if (ip.AddressFamily == AddressFamily.InterNetwork)
                {
                    addresses.Add(ip.ToString());
                }
            }
            return addresses;
        }

        private void RaiseSyncProgress(string status, int progress, bool showProgressBar, string deviceName = null)
        {
            try
            {
                OnSyncProgress?.Invoke(this, new SyncProgressEventArgs
                {
                    Status = status ?? "",
                    Progress = Math.Max(0, Math.Min(100, progress)),
                    ShowProgressBar = showProgressBar,
                    DeviceName = deviceName
                });
            }
            catch { }
        }

        private static string GetHandheldSyncTypeTitle(string dataType)
        {
            switch (dataType)
            {
                case "INBOUND": return "入库单";
                case "SALES": return "销售单";
                case "PACKAGING": return "包装单";
                case "ADVANCE": return "预支款";
                case "DEDUCTION": return "扣款";
                case "PRESALE": return "预售单";
                case "PRESALE_PAYMENT": return "预售收款";
                case "PRESALE_OUTBOUND": return "预售出库";
                case "LEDGER": return "收支流水";
                case "CUSTOMER": return "客户资料";
                case "LOCATION": return "库位";
                case "OPERATOR": return "经手人";
                default: return string.IsNullOrEmpty(dataType) ? "数据" : dataType;
            }
        }

        /// <summary>从手持 JSON 中取简短标识用于界面提示（失败时返回空）。</summary>
        private static string TryPeekHandheldPayloadLabel(string dataType, string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return "";
            var t = json.TrimStart();
            if (t.Length == 0 || t[0] != '{' && t[0] != '[') return "";
            try
            {
                var jo = JObject.Parse(json);
                switch (dataType)
                {
                    case "INBOUND":
                        return jo["bill_no"]?.ToString() ?? jo["order_no"]?.ToString() ?? "";
                    case "SALES":
                    case "PACKAGING":
                        return jo["order_no"]?.ToString() ?? jo["bill_no"]?.ToString() ?? "";
                    case "ADVANCE":
                    case "DEDUCTION":
                        return jo["client_code"]?.ToString() ?? jo["customer_no"]?.ToString() ?? jo["customerNo"]?.ToString() ?? "";
                    case "PRESALE":
                    case "PRESALE_PAYMENT":
                    case "PRESALE_OUTBOUND":
                        return jo["bill_no"]?.ToString() ?? "";
                    case "LEDGER":
                        return jo["entry_no"]?.ToString() ?? "";
                    case "CUSTOMER":
                        return jo["customer_no"]?.ToString() ?? jo["code"]?.ToString() ?? jo["name"]?.ToString() ?? "";
                    case "LOCATION":
                        return jo["name"]?.ToString() ?? jo["location_name"]?.ToString() ?? "";
                    case "OPERATOR":
                        return jo["name"]?.ToString() ?? "";
                    default:
                        return "";
                }
            }
            catch
            {
                return "";
            }
        }

        private void LogMessage(string message)
        {
            string timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
            string logMessage = $"[{timestamp}] {message}";
            Console.WriteLine(logMessage);

            if (logControl != null && !logControl.IsDisposed)
            {
                try
                {
                    if (logControl.InvokeRequired)
                    {
                        logControl.BeginInvoke(new Action(() =>
                        {
                            if (!logControl.IsDisposed)
                            {
                                logControl.AppendText(logMessage + Environment.NewLine);
                                logControl.ScrollToCaret();
                            }
                        }));
                    }
                    else
                    {
                        logControl.AppendText(logMessage + Environment.NewLine);
                        logControl.ScrollToCaret();
                    }
                }
                catch { }
            }

            OnLogMessage?.Invoke(this, logMessage);
        }

        private void HandleHelloSync(string payload, DeviceClient device)
        {
            try
            {
                var json = JObject.Parse(payload);
                string helloDeviceId = json["device_id"]?.ToString() ?? device.DeviceId;
                long lastAck = json["last_acked_seq"]?.Value<long?>() ?? 0L;
                bool firstDone = json["first_full_sync_done"]?.Value<bool?>() ?? false;
                deviceLastAckSeq.AddOrUpdate(helloDeviceId, lastAck, (_, old) => Math.Max(old, lastAck));
                deviceFirstFullSyncDone.AddOrUpdate(helloDeviceId, firstDone, (key, oldValue) => firstDone);
                TouchKnownDeviceSeen(helloDeviceId);
                SaveDeltaSyncState();
                PublishConnectedDevicesSnapshot();
                LogMessage($"🤝 HELLO_SYNC: device={helloDeviceId}, lastAck={lastAck}, firstDone={firstDone}");
                SendMessageSafely(device, $"SYNC_SERVER_READY|DELTA_READY|seq={GetCurrentCommitSeq()}|active_year={FiscalYearService.ActiveYear}");
            }
            catch (Exception ex)
            {
                LogMessage($"❌ HELLO_SYNC 处理失败: {ex.Message}");
            }
        }

        private void HandlePushChanges(string payload, DeviceClient device)
        {
            var ackedOps = new JArray();
            try
            {
                var req = JObject.Parse(payload);
                var ops = req["ops"] as JArray ?? new JArray();
                long appliedMaxSeq = 0L;
                var changedEntityTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (var token in ops)
                {
                    var op = token as JObject;
                    if (op == null) continue;
                    string originDeviceId = op["origin_device_id"]?.ToString() ?? device.DeviceId;
                    string originOpId = op["origin_op_id"]?.ToString() ?? "";
                    string entityType = op["entity_type"]?.ToString() ?? "";
                    string opType = op["op_type"]?.ToString() ?? "UPSERT";
                    string entityKey = op["entity_key"]?.ToString() ?? "";
                    var payloadObj = op["payload"] as JObject ?? new JObject();

                    if (string.IsNullOrWhiteSpace(originOpId))
                    {
                        originOpId = $"{originDeviceId}_{entityType}_{entityKey}_{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";
                    }

                    string dedupKey = $"{originDeviceId}|{originOpId}";
                    if (dedupAppliedOps.TryGetValue(dedupKey, out var existingSeq))
                    {
                        ackedOps.Add(new JObject
                        {
                            ["origin_op_id"] = originOpId,
                            ["commit_seq"] = existingSeq
                        });
                        continue;
                    }

                    bool isConfigEntity = ConfigEntityTypes.Contains(entityType);
                    bool isHandheldDelete = isConfigEntity
                        && string.Equals(opType, "DELETE", StringComparison.OrdinalIgnoreCase)
                        && !string.Equals(originDeviceId, "PC_LOCAL", StringComparison.OrdinalIgnoreCase);

                    if (isHandheldDelete)
                    {
                        dedupAppliedOps[dedupKey] = 0;
                        ackedOps.Add(new JObject
                        {
                            ["origin_op_id"] = originOpId,
                            ["commit_seq"] = 0,
                            ["skipped"] = true
                        });
                        LogMessage($"⏭️ 忽略手持端配置删除: {entityType}/{entityKey}（删除以 PC 为准）");
                        continue;
                    }

                    if (!ApplyConfigOperation(entityType, opType, payloadObj, out var applyError))
                    {
                        LogMessage($"⚠️ 增量操作应用失败: {entityType}/{opType}/{entityKey}, err={applyError}");
                        continue;
                    }

                    var commitSeq = AppendChangeLog(entityType, entityKey, opType, payloadObj, originDeviceId, originOpId);
                    dedupAppliedOps[dedupKey] = commitSeq;
                    appliedMaxSeq = Math.Max(appliedMaxSeq, commitSeq);
                    if (!string.IsNullOrWhiteSpace(entityType))
                        changedEntityTypes.Add(entityType);

                    ackedOps.Add(new JObject
                    {
                        ["origin_op_id"] = originOpId,
                        ["commit_seq"] = commitSeq
                    });
                }

                if (appliedMaxSeq > 0)
                {
                    deviceLastAckSeq.AddOrUpdate(device.DeviceId, appliedMaxSeq, (_, old) => Math.Max(old, appliedMaxSeq));
                    NotifyDevicesDeltaAvailable(device.DeviceId);
                    PublishConnectedDevicesSnapshot();
                    foreach (var entityType in changedEntityTypes)
                    {
                        DatabaseManager.RaiseConfigDataChanged(entityType);
                    }
                    LogMessage($"✅ 手持端配置已应用 {changedEntityTypes.Count} 类实体，seq 至 {appliedMaxSeq}，已通知其他设备");
                }
                SaveDeltaSyncState();

                var ack = new JObject
                {
                    ["acked_ops"] = ackedOps,
                    ["server_commit_seq"] = GetCurrentCommitSeq()
                };
                SendMessageSafely(device, $"PUSH_CHANGES_ACK|{ToSingleLineJson(ack)}");
            }
            catch (Exception ex)
            {
                LogMessage($"❌ PUSH_CHANGES 处理失败: {ex.Message}");
                SendMessageSafely(device, $"PUSH_CHANGES_ACK|{ToSingleLineJson(new JObject { ["acked_ops"] = ackedOps, ["error"] = ex.Message })}");
            }
        }

        private void HandlePullDeltaReq(string payload, DeviceClient device)
        {
            try
            {
                var req = JObject.Parse(payload);
                string reqDeviceId = req["device_id"]?.ToString() ?? device.DeviceId;
                long lastAckedSeq = req["last_acked_seq"]?.Value<long?>() ?? 0L;
                JObject delta;
                if (!IsDeviceFirstFullSyncDone(reqDeviceId))
                {
                    long toSeq = GetCurrentCommitSeq();
                    deviceLastAckSeq.AddOrUpdate(reqDeviceId, lastAckedSeq, (_, old) => Math.Max(old, lastAckedSeq));
                    delta = new JObject
                    {
                        ["from_seq"] = lastAckedSeq,
                        ["to_seq"] = toSeq,
                        ["ops"] = new JArray(),
                        ["skip_reason"] = "FIRST_FULL_SYNC_PENDING"
                    };
                    LogMessage($"⏭️ 首次全量未完成，跳过历史增量: device={reqDeviceId}, fromSeq={lastAckedSeq}, toSeq={toSeq}");
                }
                else
                {
                    delta = BuildDeltaResponse(reqDeviceId, lastAckedSeq);
                }
                SendMessageSafely(device, $"PULL_DELTA_RESP|{ToSingleLineJson(delta)}");
            }
            catch (Exception ex)
            {
                LogMessage($"❌ PULL_DELTA_REQ 处理失败: {ex.Message}");
                SendMessageSafely(device, $"RESYNC_REQUIRED|{ex.Message}");
            }
        }

        private void HandleDeltaApplyAck(string payload, DeviceClient device)
        {
            try
            {
                var req = JObject.Parse(payload);
                long ackedSeq = req["acked_seq"]?.Value<long?>() ?? 0L;
                deviceLastAckSeq.AddOrUpdate(device.DeviceId, ackedSeq, (_, old) => Math.Max(old, ackedSeq));
                deviceFirstFullSyncDone.AddOrUpdate(device.DeviceId, true, (key, oldValue) => true);
                TouchKnownDeviceSeen(device.DeviceId);
                SaveDeltaSyncState();
                LogMessage($"✅ DELTA_APPLY_ACK: device={device.DeviceId}, ackedSeq={ackedSeq}");
                PublishConnectedDevicesSnapshot();
            }
            catch (Exception ex)
            {
                LogMessage($"❌ DELTA_APPLY_ACK 处理失败: {ex.Message}");
            }
        }

        private bool ApplyConfigOperation(string entityType, string opType, JObject payload, out string error)
        {
            error = "";
            bool prevSuppress = DatabaseManager.SuppressLocalConfigSyncNotifications;
            DatabaseManager.SuppressLocalConfigSyncNotifications = true;
            try
            {
                var db = new DatabaseManager();
                entityType = (entityType ?? "").ToUpperInvariant();
                opType = (opType ?? "UPSERT").ToUpperInvariant();

                if (entityType == "CUSTOMER")
                {
                    string code = payload["code"]?.ToString() ?? "";
                    string name = payload["name"]?.ToString() ?? code;
                    string phone = payload["phone"]?.ToString() ?? "";
                    string contact = payload["contact"]?.ToString() ?? "";
                    string address = payload["address"]?.ToString() ?? "";
                    string customerType = payload["customer_type"]?.ToString();
                    int? status = null;
                    if (payload["enabled"] != null)
                        status = payload["enabled"].ToObject<bool>() ? 1 : 0;
                    else if (payload["status"] != null)
                        status = payload["status"].ToObject<int>() != 0 ? 1 : 0;

                    if (string.IsNullOrWhiteSpace(code))
                    {
                        error = "客户编号为空";
                        return false;
                    }
                    if (opType == "DELETE")
                    {
                        return db.DisableClientByCode(code);
                    }
                    return db.UpsertClientFromSync(code, name, contact, phone, address, customerType, status);
                }

                if (entityType == "LOCATION")
                {
                    string name = payload["name"]?.ToString();
                    if (string.IsNullOrWhiteSpace(name)) name = payload["code"]?.ToString();
                    if (string.IsNullOrWhiteSpace(name))
                    {
                        error = "库位名称为空";
                        return false;
                    }
                    if (opType == "DELETE")
                    {
                        // 软删除：状态置 0
                        int rows = db.ExecuteNonQuery(
                            "UPDATE locations SET status = 0 WHERE name = @name OR (code IS NOT NULL AND code != '' AND code = @code)",
                            new Dictionary<string, object>
                            {
                                { "@name", name },
                                { "@code", payload["code"]?.ToString() ?? name }
                            }
                        );
                        return rows >= 0;
                    }
                    string code = payload["code"]?.ToString() ?? name;
                    string description = payload["description"]?.ToString() ?? "";
                    bool hasEnabled = payload["enabled"] != null;
                    bool hasStatus = payload["status"] != null;
                    if (!hasEnabled && !hasStatus)
                    {
                        return db.AddLocation(name, description);
                    }
                    int statusVal = 1;
                    if (hasEnabled)
                        statusVal = payload["enabled"].ToObject<bool>() ? 1 : 0;
                    else if (hasStatus)
                        statusVal = payload["status"].ToObject<int>() != 0 ? 1 : 0;
                    return db.UpsertLocationForSync(name, code, description, statusVal);
                }

                if (entityType == "OPERATOR")
                {
                    string name = payload["name"]?.ToString();
                    if (string.IsNullOrWhiteSpace(name)) name = payload["code"]?.ToString();
                    if (string.IsNullOrWhiteSpace(name))
                    {
                        error = "经手人名称为空";
                        return false;
                    }
                    if (opType == "DELETE")
                    {
                        int rows = db.ExecuteNonQuery(
                            "UPDATE handlers SET status = 0 WHERE name = @name",
                            new Dictionary<string, object> { { "@name", name } }
                        );
                        return rows >= 0;
                    }
                    return db.AddHandler(name);
                }

                if (entityType == "PRODUCT")
                {
                    string code = payload["code"]?.ToString() ?? "";
                    string name = payload["name"]?.ToString() ?? code;
                    bool enabled = true;
                    if (payload["enabled"] != null)
                        enabled = payload["enabled"].ToObject<bool>();
                    else if (payload["is_active"] != null)
                        enabled = payload["is_active"].ToObject<bool>();

                    if (string.IsNullOrWhiteSpace(name) && string.IsNullOrWhiteSpace(code))
                    {
                        error = "商品型号名称为空";
                        return false;
                    }
                    if (opType == "DELETE")
                    {
                        return db.DisableProductTypeForSync(code, name);
                    }
                    return db.UpsertProductTypeForSync(code, name, enabled);
                }

                if (entityType == "PACK_TYPE")
                {
                    string code = payload["code"]?.ToString() ?? "";
                    string name = payload["name"]?.ToString() ?? code;
                    bool enabled = true;
                    if (payload["enabled"] != null)
                        enabled = payload["enabled"].ToObject<bool>();
                    else if (payload["is_active"] != null)
                        enabled = payload["is_active"].ToObject<bool>();

                    if (string.IsNullOrWhiteSpace(name))
                    {
                        error = "包装类型名称为空";
                        return false;
                    }
                    if (opType == "DELETE")
                    {
                        return db.DisablePackTypeForSync(code, name);
                    }
                    return db.UpsertPackTypeForSync(code, name, enabled);
                }

                error = $"不支持的实体类型: {entityType}";
                return false;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
            finally
            {
                DatabaseManager.SuppressLocalConfigSyncNotifications = prevSuppress;
            }
        }

        private void OnLocalDatabaseConfigChangedForSync(object sender, LocalConfigDeltaEventArgs e)
        {
            if (e == null || string.IsNullOrWhiteSpace(e.EntityType)) return;
            try
            {
                string originOpId = $"pc_{e.EntityType}_{e.EntityKey}_{Guid.NewGuid():N}";
                var payload = e.Payload ?? new JObject();
                AppendChangeLog(e.EntityType, e.EntityKey ?? "", e.OpType ?? "UPSERT", payload, "PC_LOCAL", originOpId);
                SaveDeltaSyncState();
                LogMessage($"📝 本地配置已记入增量日志: {e.EntityType}/{e.EntityKey}");
                NotifyOtherDevicesDeltaAvailable("PC_LOCAL");
                PublishConnectedDevicesSnapshot();
            }
            catch (Exception ex)
            {
                LogMessage($"⚠️ 本地配置增量记录失败: {ex.Message}");
            }
        }

        private long AppendChangeLog(string entityType, string entityKey, string opType, JObject payload, string originDeviceId, string originOpId)
        {
            lock (deltaSyncLock)
            {
                long seq = nextCommitSeq++;
                syncChangeLog.Add(new ChangeLogEntry
                {
                    CommitSeq = seq,
                    EntityType = entityType ?? "",
                    EntityKey = entityKey ?? "",
                    OpType = opType ?? "UPSERT",
                    PayloadJson = payload?.ToString() ?? "{}",
                    ServerCommitTime = DateTime.UtcNow,
                    OriginDeviceId = originDeviceId ?? "",
                    OriginOpId = originOpId ?? "",
                });
                if (syncChangeLog.Count > 20000)
                {
                    syncChangeLog.RemoveRange(0, syncChangeLog.Count - 20000);
                }
                return seq;
            }
        }

        private JObject BuildDeltaResponse(string deviceId, long lastAckedSeq)
        {
            var result = new JObject();
            lock (deltaSyncLock)
            {
                deviceLastAckSeq.AddOrUpdate(deviceId, lastAckedSeq, (_, old) => Math.Max(old, lastAckedSeq));
                var ops = new JArray();
                foreach (var item in syncChangeLog.Where(x => x.CommitSeq > lastAckedSeq))
                {
                    ops.Add(new JObject
                    {
                        ["commit_seq"] = item.CommitSeq,
                        ["entity_type"] = item.EntityType,
                        ["entity_key"] = item.EntityKey,
                        ["op_type"] = item.OpType,
                        ["payload"] = JObject.Parse(string.IsNullOrWhiteSpace(item.PayloadJson) ? "{}" : item.PayloadJson),
                        ["origin_device_id"] = item.OriginDeviceId,
                        ["origin_op_id"] = item.OriginOpId
                    });
                }

                result["from_seq"] = lastAckedSeq;
                result["to_seq"] = GetCurrentCommitSeq();
                result["ops"] = ops;
            }
            return result;
        }

        private long GetCurrentCommitSeq()
        {
            lock (deltaSyncLock)
            {
                return nextCommitSeq - 1;
            }
        }

        /// <summary>设备是否已完成首次全量基线对齐（未完成则不下发历史增量）。</summary>
        private bool IsDeviceFirstFullSyncDone(string deviceId)
        {
            if (string.IsNullOrWhiteSpace(deviceId)) return false;
            if (deviceFirstFullSyncDone.TryGetValue(deviceId, out bool firstDone))
            {
                return firstDone;
            }
            // 兼容旧状态：已有 ACK 游标的老设备视为已完成首次全量
            if (deviceLastAckSeq.TryGetValue(deviceId, out long ackedSeq) && ackedSeq > 0)
            {
                return true;
            }
            return false;
        }

        /// <summary>清空增量变更日志与设备游标（不影响业务数据库）。</summary>
        public void ResetDeltaSyncState()
        {
            lock (deltaSyncLock)
            {
                nextCommitSeq = 1;
                syncChangeLog.Clear();
            }
            deviceLastAckSeq.Clear();
            deviceFirstFullSyncDone.Clear();
            dedupAppliedOps.Clear();
            SaveDeltaSyncState();
            LogMessage("🧹 已重置增量同步状态（change_log 已清空，commit_seq 从 0 开始）");
        }

        private string GetDeltaSyncStatePath()
        {
            string configDir = Path.Combine(Application.StartupPath, "config");
            if (!Directory.Exists(configDir))
            {
                Directory.CreateDirectory(configDir);
            }
            return Path.Combine(configDir, "sync_delta_state.json");
        }

        private void LoadDeltaSyncState()
        {
            try
            {
                string path = GetDeltaSyncStatePath();
                if (!File.Exists(path)) return;

                var text = File.ReadAllText(path);
                if (string.IsNullOrWhiteSpace(text)) return;
                var root = JObject.Parse(text);

                lock (deltaSyncLock)
                {
                    nextCommitSeq = root["next_commit_seq"]?.Value<long?>() ?? 1L;
                    syncChangeLog.Clear();
                    var logs = root["change_log"] as JArray;
                    if (logs != null)
                    {
                        foreach (var t in logs.OfType<JObject>())
                        {
                            syncChangeLog.Add(new ChangeLogEntry
                            {
                                CommitSeq = t["commit_seq"]?.Value<long?>() ?? 0L,
                                EntityType = t["entity_type"]?.ToString() ?? "",
                                EntityKey = t["entity_key"]?.ToString() ?? "",
                                OpType = t["op_type"]?.ToString() ?? "UPSERT",
                                PayloadJson = t["payload_json"]?.ToString() ?? "{}",
                                OriginDeviceId = t["origin_device_id"]?.ToString() ?? "",
                                OriginOpId = t["origin_op_id"]?.ToString() ?? "",
                                ServerCommitTime = t["server_commit_time"]?.Value<DateTime?>() ?? DateTime.UtcNow,
                            });
                        }
                    }
                }

                deviceLastAckSeq.Clear();
                var cursorObj = root["device_last_ack_seq"] as JObject;
                if (cursorObj != null)
                {
                    foreach (var p in cursorObj.Properties())
                    {
                        deviceLastAckSeq[p.Name] = p.Value.Value<long>();
                    }
                }

                deviceFirstFullSyncDone.Clear();
                var firstDoneObj = root["device_first_full_sync_done"] as JObject;
                if (firstDoneObj != null)
                {
                    foreach (var p in firstDoneObj.Properties())
                    {
                        deviceFirstFullSyncDone[p.Name] = p.Value.Value<bool>();
                    }
                }

                dedupAppliedOps.Clear();
                var dedupObj = root["dedup_applied_ops"] as JObject;
                if (dedupObj != null)
                {
                    foreach (var p in dedupObj.Properties())
                    {
                        dedupAppliedOps[p.Name] = p.Value.Value<long>();
                    }
                }

                deviceLegacyPushFingerprints.Clear();
                var legacyRoot = root["device_legacy_push"] as JObject;
                if (legacyRoot != null)
                {
                    foreach (var devProp in legacyRoot.Properties())
                    {
                        var inner = new ConcurrentDictionary<string, string>();
                        var perDevice = devProp.Value as JObject;
                        if (perDevice != null)
                        {
                            foreach (var kv in perDevice.Properties())
                            {
                                inner[kv.Name] = kv.Value?.ToString() ?? "";
                            }
                        }
                        deviceLegacyPushFingerprints[devProp.Name] = inner;
                    }
                }

                knownDevices.Clear();
                var knownRoot = root["known_devices"] as JObject;
                if (knownRoot != null)
                {
                    foreach (var devProp in knownRoot.Properties())
                    {
                        var obj = devProp.Value as JObject;
                        if (obj == null) continue;
                        string storedDeviceId = obj["device_id"]?.ToString();
                        if (string.IsNullOrWhiteSpace(storedDeviceId))
                            storedDeviceId = devProp.Name;
                        knownDevices[devProp.Name] = new KnownDeviceRecord
                        {
                            DeviceId = storedDeviceId,
                            DeviceName = obj["device_name"]?.ToString() ?? storedDeviceId,
                            DeviceType = obj["device_type"]?.ToString() ?? "手持端",
                            LastIpAddress = obj["last_ip"]?.ToString() ?? "",
                            FirstRegisteredAt = obj["first_registered_at"]?.Value<DateTime?>() ?? DateTime.MinValue,
                            LastConnectedAt = obj["last_connected_at"]?.Value<DateTime?>() ?? DateTime.MinValue,
                            LastSeenAt = obj["last_seen_at"]?.Value<DateTime?>() ?? DateTime.MinValue
                        };
                    }
                }

                if (PurgePhantomKnownDevices() | NormalizeKnownDevicesByIdentity())
                    SaveDeltaSyncState();
            }
            catch (Exception ex)
            {
                LogMessage($"⚠️ 加载增量同步状态失败: {ex.Message}");
            }
        }

        private void SaveDeltaSyncState()
        {
            try
            {
                var root = new JObject();
                lock (deltaSyncLock)
                {
                    root["next_commit_seq"] = nextCommitSeq;
                    var logs = new JArray();
                    foreach (var item in syncChangeLog)
                    {
                        logs.Add(new JObject
                        {
                            ["commit_seq"] = item.CommitSeq,
                            ["entity_type"] = item.EntityType,
                            ["entity_key"] = item.EntityKey,
                            ["op_type"] = item.OpType,
                            ["payload_json"] = item.PayloadJson ?? "{}",
                            ["origin_device_id"] = item.OriginDeviceId ?? "",
                            ["origin_op_id"] = item.OriginOpId ?? "",
                            ["server_commit_time"] = item.ServerCommitTime
                        });
                    }
                    root["change_log"] = logs;
                }

                var cursorObj = new JObject();
                foreach (var kv in deviceLastAckSeq)
                {
                    cursorObj[kv.Key] = kv.Value;
                }
                root["device_last_ack_seq"] = cursorObj;

                var firstDoneObj = new JObject();
                foreach (var kv in deviceFirstFullSyncDone)
                {
                    firstDoneObj[kv.Key] = kv.Value;
                }
                root["device_first_full_sync_done"] = firstDoneObj;

                var dedupObj = new JObject();
                foreach (var kv in dedupAppliedOps)
                {
                    dedupObj[kv.Key] = kv.Value;
                }
                root["dedup_applied_ops"] = dedupObj;

                var legacyPushObj = new JObject();
                foreach (var devKv in deviceLegacyPushFingerprints)
                {
                    var per = new JObject();
                    foreach (var kv in devKv.Value)
                    {
                        per[kv.Key] = kv.Value;
                    }
                    legacyPushObj[devKv.Key] = per;
                }
                root["device_legacy_push"] = legacyPushObj;

                var knownObj = new JObject();
                foreach (var kv in knownDevices)
                {
                    knownObj[kv.Key] = new JObject
                    {
                        ["device_id"] = kv.Value.DeviceId ?? kv.Key,
                        ["device_name"] = kv.Value.DeviceName ?? kv.Key,
                        ["device_type"] = kv.Value.DeviceType ?? "手持端",
                        ["last_ip"] = kv.Value.LastIpAddress ?? "",
                        ["first_registered_at"] = kv.Value.FirstRegisteredAt,
                        ["last_connected_at"] = kv.Value.LastConnectedAt,
                        ["last_seen_at"] = kv.Value.LastSeenAt
                    };
                }
                root["known_devices"] = knownObj;

                File.WriteAllText(GetDeltaSyncStatePath(), root.ToString());
            }
            catch (Exception ex)
            {
                LogMessage($"⚠️ 保存增量同步状态失败: {ex.Message}");
            }
        }

        private bool TryGetCurrentDevice(string deviceId, out DeviceClient deviceClient)
        {
            if (!string.IsNullOrEmpty(deviceId) && connectedDevices.TryGetValue(deviceId, out var current))
            {
                deviceClient = current;
                return true;
            }

            deviceClient = null;
            return false;
        }

        private static bool IsDeviceChannelReady(DeviceClient dev)
        {
            if (dev == null || !dev.IsOnline) return false;
            if (dev.TcpClient != null && dev.TcpClient.Connected) return true;
            return dev.AsyncSend != null;
        }

        private void SendMessageSafely(DeviceClient device, string message)
        {
            if (device == null)
                throw new InvalidOperationException("设备写入通道不可用");

            if (device.AsyncSend != null)
            {
                device.AsyncSend(message).GetAwaiter().GetResult();
                return;
            }

            if (device.Writer == null)
                throw new InvalidOperationException("设备写入通道不可用");

            lock (device.WriteLock)
            {
                device.Writer.WriteLine(message);
            }
        }

        private string ToSingleLineJson(JToken token)
        {
            return token?.ToString(Formatting.None) ?? "{}";
        }

        private void OnLocalPresaleChangedForSync(object sender, LocalConfigDeltaEventArgs e)
        {
            if (e == null || string.IsNullOrWhiteSpace(e.EntityType)) return;
            try
            {
                string originOpId = $"pc_{e.EntityType}_{e.EntityKey}_{Guid.NewGuid():N}";
                var payload = e.Payload ?? new JObject();
                AppendChangeLog(e.EntityType, e.EntityKey ?? "", e.OpType ?? "UPSERT", payload, "PC_LOCAL", originOpId);
                SaveDeltaSyncState();
                NotifyOtherDevicesDeltaAvailable("PC_LOCAL");
                LogMessage($"📝 本地预售变更已记入增量日志: {e.EntityType}/{e.EntityKey}");
            }
            catch (Exception ex)
            {
                LogMessage($"⚠️ 本地预售增量记录失败: {ex.Message}");
            }
        }

        private void AppendPresaleChangeLogFromJson(string jsonData, string entityType, string originDeviceId)
        {
            try
            {
                var json = JObject.Parse(jsonData);
                string sourceRecordId = json["source_record_id"]?.ToString() ?? "";
                string entityKey = !string.IsNullOrWhiteSpace(sourceRecordId)
                    ? sourceRecordId
                    : json["bill_no"]?.ToString() ?? "";
                if (string.IsNullOrWhiteSpace(entityKey)) return;
                string originOpId = $"{originDeviceId}_{entityType}_{entityKey}_{Guid.NewGuid():N}";
                AppendChangeLog(entityType, entityKey, "UPSERT", json, originDeviceId, originOpId);
                SaveDeltaSyncState();
            }
            catch (Exception ex)
            {
                LogMessage($"⚠️ 预售增量日志写入失败: {ex.Message}");
            }
        }

        /// <summary>以 PC 库内最新数据写入增量，避免 changelog 与库内 mode 不一致。</summary>
        private void AppendPresaleChangeLogFromBillNo(DatabaseManager db, string billNo, string originDeviceId)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(billNo)) return;
                var payload = db.BuildPresaleBillSyncPayload(billNo);
                if (payload == null)
                {
                    LogMessage($"⚠️ 预售增量跳过：未找到单据 {billNo}");
                    return;
                }
                string sourceRecordId = payload["source_record_id"]?.ToString() ?? "";
                string entityKey = !string.IsNullOrWhiteSpace(sourceRecordId) ? sourceRecordId : billNo;
                string originOpId = $"{originDeviceId}_PRESALE_{entityKey}_{Guid.NewGuid():N}";
                AppendChangeLog("PRESALE", entityKey, "UPSERT", payload, originDeviceId, originOpId);
                SaveDeltaSyncState();
                LogMessage($"📝 预售增量已记录: {billNo} mode={payload["sale_mode"]} status={payload["bill_status"]}");
            }
            catch (Exception ex)
            {
                LogMessage($"⚠️ 预售增量日志写入失败: {ex.Message}");
            }
        }

        private void NotifyOtherDevicesDeltaAvailable(string sourceDeviceId)
        {
            NotifyDevicesDeltaAvailable(sourceDeviceId);
        }

        /// <param name="excludeDeviceId">为 null 时通知所有在线手持端；否则跳过来源设备。</param>
        private void NotifyDevicesDeltaAvailable(string excludeDeviceId)
        {
            try
            {
                long toSeq = GetCurrentCommitSeq();
                if (toSeq <= 0) return;
                foreach (var pair in connectedDevices.ToArray())
                {
                    if (!string.IsNullOrEmpty(excludeDeviceId) &&
                        string.Equals(pair.Key, excludeDeviceId, StringComparison.Ordinal))
                    {
                        continue;
                    }
                    var dev = pair.Value;
                    if (IsDeviceChannelReady(dev))
                    {
                        SendMessageSafely(dev, $"DELTA_AVAILABLE|{toSeq}");
                        LogMessage($"📣 已通知 {dev.DeviceName} 拉取增量 seq={toSeq}");
                    }
                }
            }
            catch (Exception ex)
            {
                LogMessage($"⚠️ 通知设备拉取增量失败: {ex.Message}");
            }
        }

        private void CheckAndNotifyOtherDevices(string data, string sourceDeviceId)
        {
            NotifyOtherDevicesDeltaAvailable(sourceDeviceId);
        }

        #region 待同步基础配置（PC → 手持）

        public IReadOnlyList<PendingConfigDeltaItem> GetPendingConfigDeltaItems()
        {
            lock (deltaSyncLock)
            {
                long threshold = GetPendingConfigAckThreshold();
                return syncChangeLog
                    .Where(x => IsConfigEntityType(x.EntityType) && x.CommitSeq > threshold)
                    .OrderBy(x => x.CommitSeq)
                    .Select(ToPendingConfigDeltaItem)
                    .ToList();
            }
        }

        /// <summary>通知所有在线手持端拉取增量（含待同步基础配置）。</summary>
        public int PushPendingConfigDeltaToHandhelds()
        {
            var pending = GetPendingConfigDeltaItems();
            if (pending.Count == 0)
            {
                LogMessage("ℹ️ 无待同步基础配置");
                return 0;
            }

            int notified = 0;
            long toSeq = GetCurrentCommitSeq();
            foreach (var pair in connectedDevices.ToArray())
            {
                var dev = pair.Value;
                if (!IsDeviceChannelReady(dev))
                    continue;
                SendMessageSafely(dev, $"DELTA_AVAILABLE|{toSeq}");
                LogMessage($"📣 已通知 {dev.DeviceName} 同步基础配置 seq={toSeq}，待传 {pending.Count} 条");
                notified++;
            }

            if (notified == 0)
            {
                LogMessage("⚠️ 无在线手持端，无法下发基础配置");
            }
            else
            {
                RaiseSyncProgress($"正在同步 {pending.Count} 条基础配置到 {notified} 台手持端", 0, false);
            }
            return notified;
        }

        public int GetConnectedOnlineDeviceCount()
        {
            return connectedDevices.Values.Count(d => d != null && d.IsOnline);
        }

        private static bool IsConfigEntityType(string entityType)
        {
            return !string.IsNullOrWhiteSpace(entityType) && ConfigEntityTypes.Contains(entityType.Trim());
        }

        private long GetPendingConfigAckThreshold()
        {
            var onlineDeviceIds = connectedDevices.Values
                .Where(d => d != null && d.IsOnline)
                .Select(d => d.DeviceId)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .ToList();

            if (onlineDeviceIds.Count == 0)
            {
                if (deviceLastAckSeq.IsEmpty)
                    return 0;
                return deviceLastAckSeq.Values.Max();
            }

            long minAck = long.MaxValue;
            foreach (var deviceId in onlineDeviceIds)
            {
                long ack = deviceLastAckSeq.GetOrAdd(deviceId, 0);
                if (ack < minAck)
                    minAck = ack;
            }
            return minAck == long.MaxValue ? 0 : minAck;
        }

        private static PendingConfigDeltaItem ToPendingConfigDeltaItem(ChangeLogEntry entry)
        {
            return new PendingConfigDeltaItem
            {
                CommitSeq = entry.CommitSeq,
                EntityType = entry.EntityType ?? "",
                EntityTypeLabel = FormatConfigEntityTypeLabel(entry.EntityType),
                EntityKey = entry.EntityKey ?? "",
                OpType = entry.OpType ?? "UPSERT",
                OpTypeLabel = FormatConfigOpTypeLabel(entry.OpType),
                DisplayName = ExtractConfigDisplayName(entry),
                CommitTime = entry.ServerCommitTime.ToLocalTime(),
            };
        }

        private static string FormatConfigEntityTypeLabel(string entityType)
        {
            switch ((entityType ?? "").ToUpperInvariant())
            {
                case "CUSTOMER": return "客户";
                case "LOCATION": return "库位";
                case "OPERATOR": return "经手人";
                case "PRODUCT": return "商品型号";
                case "PACK_TYPE": return "包装类型";
                default: return entityType ?? "";
            }
        }

        private static string FormatConfigOpTypeLabel(string opType)
        {
            return string.Equals(opType, "DELETE", StringComparison.OrdinalIgnoreCase) ? "删除" : "更新";
        }

        private static string ExtractConfigDisplayName(ChangeLogEntry entry)
        {
            try
            {
                var jo = JObject.Parse(string.IsNullOrWhiteSpace(entry.PayloadJson) ? "{}" : entry.PayloadJson);
                string name = jo["name"]?.ToString();
                string code = jo["code"]?.ToString();
                if (!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(code))
                    return $"{name} ({code})";
                if (!string.IsNullOrWhiteSpace(name))
                    return name;
                if (!string.IsNullOrWhiteSpace(code))
                    return code;
            }
            catch { }
            return entry.EntityKey ?? "";
        }

        private void PublishPendingConfigDeltaSnapshot()
        {
            try
            {
                var list = GetPendingConfigDeltaItems();
                OnPendingConfigDeltaChanged?.Invoke(this, new PendingConfigDeltaChangedEventArgs(list));
            }
            catch (Exception ex)
            {
                LogMessage($"⚠️ 刷新待同步基础配置列表失败: {ex.Message}");
            }
        }

        #endregion

        #region 内部类定义
        private class KnownDeviceRecord
        {
            public string DeviceId { get; set; }
            public string DeviceName { get; set; }
            public string DeviceType { get; set; }
            public string LastIpAddress { get; set; }
            public DateTime FirstRegisteredAt { get; set; }
            public DateTime LastConnectedAt { get; set; }
            public DateTime LastSeenAt { get; set; }
        }

        private class DeviceClient
        {
            public string DeviceId { get; set; }
            public string DeviceName { get; set; }
            public string DeviceType { get; set; }
            public string IpAddress { get; set; }
            public string TransportClientId { get; set; }
            public Func<string, Task> AsyncSend { get; set; }
            public TcpClient TcpClient { get; set; }
            public StreamWriter Writer { get; set; }
            public StreamReader Reader { get; set; }
            public object WriteLock { get; } = new object();
            public DateTime LastHeartbeat { get; set; }
            public DateTime ConnectedAt { get; set; }
            public bool IsOnline { get; set; }
            public string PairingCode { get; set; }

            public void Close()
            {
                try
                {
                    Writer?.Close();
                    Reader?.Close();
                    TcpClient?.Close();
                }
                catch { }
            }
        }

        private class SyncTask
        {
            public string DeviceId { get; set; }
            public string TaskType { get; set; }
            public string Data { get; set; }
        }

        private class ChangeLogEntry
        {
            public long CommitSeq { get; set; }
            public string EntityType { get; set; }
            public string EntityKey { get; set; }
            public string OpType { get; set; }
            public string PayloadJson { get; set; }
            public DateTime ServerCommitTime { get; set; }
            public string OriginDeviceId { get; set; }
            public string OriginOpId { get; set; }
        }
        /// <summary>
        /// 记录同步结果到日志文件
        /// </summary>
        private void LogSyncResult(string deviceId, string deviceName, string dataType, string orderNo, bool success, string errorMsg)
        {
            try
            {
                string logDir = Path.Combine(Application.StartupPath, "logs", "sync");
                if (!Directory.Exists(logDir))
                {
                    Directory.CreateDirectory(logDir);
                }

                string logFile = Path.Combine(logDir, $"sync_{DateTime.Now:yyyyMMdd}.log");
                string logEntry = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}|{deviceId}|{deviceName}|{dataType}|{orderNo ?? ""}|{(success ? "SUCCESS" : "FAILED")}|{errorMsg}";

                File.AppendAllText(logFile, logEntry + Environment.NewLine);
            }
            catch (Exception ex)
            {
                LogMessage($"⚠️ 记录同步日志失败: {ex.Message}");
            }
        }
        #endregion
    }

    public class ConnectedDeviceInfo
    {
        public string DeviceId { get; set; }
        public string DeviceName { get; set; }
        public string IpAddress { get; set; }
        public string DeviceType { get; set; }
        public DateTime ConnectedAt { get; set; }
        public bool IsOnline { get; set; }
        public bool IsConnected { get; set; }
        public long LastAckedSeq { get; set; }
        public int ConfigLagCount { get; set; }
        public string SyncStatusLabel { get; set; }
        public DateTime LastConnectedAt { get; set; }
    }

    public class ConnectedDevicesChangedEventArgs : EventArgs
    {
        public IReadOnlyList<ConnectedDeviceInfo> Devices { get; }
        public int Count => Devices.Count;
        public ConnectedDevicesChangedEventArgs(IReadOnlyList<ConnectedDeviceInfo> devices)
        {
            Devices = devices ?? Array.Empty<ConnectedDeviceInfo>();
        }
    }

    public class SyncProgressEventArgs : EventArgs
    {
        /// <summary>当前状态说明（始终展示）。</summary>
        public string Status { get; set; }
        /// <summary>0–100，仅在 ShowProgressBar 为 true 时有效。</summary>
        public int Progress { get; set; }
        /// <summary>true：电脑向手持下发大块数据（如全量同步），显示进度条；false：手持向电脑推送，仅文案。</summary>
        public bool ShowProgressBar { get; set; }
        public string DeviceName { get; set; }
    }

    public class PendingConfigDeltaItem
    {
        public long CommitSeq { get; set; }
        public string EntityType { get; set; }
        public string EntityTypeLabel { get; set; }
        public string EntityKey { get; set; }
        public string OpType { get; set; }
        public string OpTypeLabel { get; set; }
        public string DisplayName { get; set; }
        public DateTime CommitTime { get; set; }
    }

    public class PendingConfigDeltaChangedEventArgs : EventArgs
    {
        public IReadOnlyList<PendingConfigDeltaItem> Items { get; }
        public int Count => Items?.Count ?? 0;

        public PendingConfigDeltaChangedEventArgs(IReadOnlyList<PendingConfigDeltaItem> items)
        {
            Items = items ?? Array.Empty<PendingConfigDeltaItem>();
        }
    }
}
#endregion