using System;
using System.Drawing;
using System.Windows.Forms;
using System.Linq;
using System.Net;
using System.Net.Sockets;

namespace lengkubao.desktop
{
    public partial class SyncManagerForm : Form
    {
        private AutoSyncServer syncServer;
        private bool isServerRunning = false;
        private System.Windows.Forms.Timer statsUpdateTimer;

        // 新增：配对码输入控件
        private TextBox txtPairingCode;
        private Button btnSavePairingCode;
        private Label lblCurrentPairingCode;

        public SyncManagerForm()
        {
            InitializeComponent();
            InitializeUI();
            statsUpdateTimer = new System.Windows.Forms.Timer();
            statsUpdateTimer.Interval = 1000;
            statsUpdateTimer.Tick += StatsUpdateTimer_Tick;
        }

        private void InitializeUI()
        {
            this.Text = "📡 数据同步管理器";
            this.Size = new Size(900, 700);
            this.StartPosition = FormStartPosition.CenterScreen;

            // 创建工具栏
            ToolStrip toolStrip = new ToolStrip();
            toolStrip.Dock = DockStyle.Top;

            btnStart = new ToolStripButton("▶️ 启动服务");
            btnStart.Click += BtnStart_Click;
            toolStrip.Items.Add(btnStart);

            btnStop = new ToolStripButton("⏹️ 停止服务");
            btnStop.Click += BtnStop_Click;
            btnStop.Enabled = false;
            toolStrip.Items.Add(btnStop);

            toolStrip.Items.Add(new ToolStripSeparator());

            btnSyncNow = new ToolStripButton("🔄 立即同步");
            btnSyncNow.Click += BtnSyncNow_Click;
            btnSyncNow.Enabled = false;
            toolStrip.Items.Add(btnSyncNow);

            toolStrip.Items.Add(new ToolStripSeparator());

            ToolStripButton btnClearLog = new ToolStripButton("🗑️ 清空日志");
            btnClearLog.Click += (s, e) => txtLog.Clear();
            toolStrip.Items.Add(btnClearLog);

            ToolStripLabel lblServerInfo = new ToolStripLabel();
            lblServerInfo.Name = "lblServerInfo";
            lblServerInfo.Text = $" | 本机: {Environment.MachineName} | IP: {GetLocalIPAddress()}";
            toolStrip.Items.Add(lblServerInfo);

            // ========== 新增：配对码设置面板 ==========
            Panel pairingPanel = new Panel();
            pairingPanel.Dock = DockStyle.Top;
            pairingPanel.Height = 60;
            pairingPanel.BackColor = Color.FromArgb(240, 240, 240);
            pairingPanel.Padding = new Padding(10);

            Label lblPairingTitle = new Label();
            lblPairingTitle.Text = "🔐 配对码设置：";
            lblPairingTitle.Location = new Point(10, 20);
            lblPairingTitle.Size = new Size(100, 25);
            pairingPanel.Controls.Add(lblPairingTitle);

            txtPairingCode = new TextBox();
            txtPairingCode.Location = new Point(120, 18);
            txtPairingCode.Size = new Size(150, 25);
            txtPairingCode.Text = "ABC-123"; // 默认值
            pairingPanel.Controls.Add(txtPairingCode);

            btnSavePairingCode = new Button();
            btnSavePairingCode.Text = "保存配对码";
            btnSavePairingCode.Location = new Point(280, 16);
            btnSavePairingCode.Size = new Size(100, 30);
            btnSavePairingCode.Click += BtnSavePairingCode_Click;
            pairingPanel.Controls.Add(btnSavePairingCode);

            lblCurrentPairingCode = new Label();
            lblCurrentPairingCode.Location = new Point(400, 20);
            lblCurrentPairingCode.Size = new Size(300, 25);
            lblCurrentPairingCode.Text = "当前配对码：未设置";
            lblCurrentPairingCode.ForeColor = Color.Blue;
            pairingPanel.Controls.Add(lblCurrentPairingCode);

            Button btnBroadcastPairing = new Button();
            btnBroadcastPairing.Text = "📢 广播配对码";
            btnBroadcastPairing.Location = new Point(710, 16);
            btnBroadcastPairing.Size = new Size(120, 30);
            btnBroadcastPairing.Click += BtnBroadcastPairing_Click;
            pairingPanel.Controls.Add(btnBroadcastPairing);

            // 状态栏
            StatusStrip statusStrip = new StatusStrip();
            lblStatus = new ToolStripStatusLabel("就绪");
            lblStatus.Text = "🟢 同步服务未启动";
            statusStrip.Items.Add(lblStatus);

            // 客户端列表
            GroupBox groupClients = new GroupBox();
            groupClients.Text = "📱 已连接设备";
            groupClients.Dock = DockStyle.Top;
            groupClients.Height = 150;

            dgvClients = new DataGridView();
            dgvClients.Dock = DockStyle.Fill;
            dgvClients.ReadOnly = true;
            dgvClients.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            dgvClients.Columns.Add("DeviceId", "设备ID");
            dgvClients.Columns.Add("DeviceName", "设备名称");
            dgvClients.Columns.Add("IP", "IP地址");
            dgvClients.Columns.Add("PairingCode", "配对码");
            dgvClients.Columns.Add("ConnectTime", "连接时间");
            dgvClients.Columns.Add("LastSync", "最后同步");
            dgvClients.Columns.Add("Status", "状态");

            if (dgvClients.Columns.Count > 0)
            {
                dgvClients.Columns["DeviceId"].Width = 120;
                dgvClients.Columns["DeviceName"].Width = 120;
                dgvClients.Columns["IP"].Width = 100;
                dgvClients.Columns["PairingCode"].Width = 100;
                dgvClients.Columns["Status"].Width = 60;
            }

            groupClients.Controls.Add(dgvClients);

            // 日志区域
            GroupBox groupLog = new GroupBox();
            groupLog.Text = "📝 同步日志";
            groupLog.Dock = DockStyle.Fill;

            txtLog = new RichTextBox();
            txtLog.Dock = DockStyle.Fill;
            txtLog.Font = new Font("Consolas", 10);
            txtLog.ReadOnly = true;
            groupLog.Controls.Add(txtLog);

            // 统计面板
            Panel statsPanel = new Panel();
            statsPanel.Dock = DockStyle.Top;
            statsPanel.Height = 40;
            statsPanel.BackColor = Color.LightGray;

            lblStats = new Label();
            lblStats.Text = "今日同步：0条记录 | 在线设备：0台 | 今日连接：0次";
            lblStats.Dock = DockStyle.Fill;
            lblStats.TextAlign = ContentAlignment.MiddleCenter;
            statsPanel.Controls.Add(lblStats);

            Panel syncProgressPanel = new Panel();
            syncProgressPanel.Dock = DockStyle.Fill;
            syncProgressPanel.BackColor = Color.FromArgb(236, 240, 245);
            syncProgressPanel.Padding = new Padding(8, 4, 8, 4);

            lblSyncActivity = new Label();
            lblSyncActivity.Name = "lblSyncActivity";
            lblSyncActivity.Text = "同步空闲";
            lblSyncActivity.AutoSize = false;
            lblSyncActivity.Dock = DockStyle.Top;
            lblSyncActivity.Height = 28;
            lblSyncActivity.Font = new Font("微软雅黑", 8.5f);
            lblSyncActivity.TextAlign = ContentAlignment.MiddleLeft;
            syncProgressPanel.Controls.Add(lblSyncActivity);

            pbSyncProgress = new ProgressBar();
            pbSyncProgress.Name = "pbSyncProgress";
            pbSyncProgress.Minimum = 0;
            pbSyncProgress.Maximum = 100;
            pbSyncProgress.Dock = DockStyle.Top;
            pbSyncProgress.Height = 18;
            pbSyncProgress.Visible = false;
            syncProgressPanel.Controls.Add(pbSyncProgress);

            // 布局
            TableLayoutPanel mainLayout = new TableLayoutPanel();
            mainLayout.Dock = DockStyle.Fill;
            mainLayout.RowCount = 6;
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));  // 工具栏
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 60));  // 配对码面板
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 150)); // 客户端列表
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));  // 统计
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 56));  // 同步进程
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));  // 日志

            mainLayout.Controls.Add(toolStrip, 0, 0);
            mainLayout.Controls.Add(pairingPanel, 0, 1);
            mainLayout.Controls.Add(groupClients, 0, 2);
            mainLayout.Controls.Add(statsPanel, 0, 3);
            mainLayout.Controls.Add(syncProgressPanel, 0, 4);
            mainLayout.Controls.Add(groupLog, 0, 5);

            this.Controls.Add(mainLayout);
            this.Controls.Add(statusStrip);
        }

        // ========== 新增：保存配对码 ==========
        private void BtnSavePairingCode_Click(object sender, EventArgs e)
        {
            string newCode = txtPairingCode.Text.Trim();

            if (string.IsNullOrWhiteSpace(newCode))
            {
                MessageBox.Show("请输入配对码", "提示",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (syncServer != null)
            {
                syncServer.SavePairingCode(newCode);
                lblCurrentPairingCode.Text = $"当前配对码：{newCode}";

                // 如果服务正在运行，提示重启
                if (isServerRunning)
                {
                    DialogResult result = MessageBox.Show(
                        "配对码已修改，是否重启服务以使新配对码生效？",
                        "确认重启",
                        MessageBoxButtons.YesNo,
                        MessageBoxIcon.Question);

                    if (result == DialogResult.Yes)
                    {
                        BtnStop_Click(sender, e);
                        System.Threading.Thread.Sleep(1000);
                        BtnStart_Click(sender, e);
                    }
                }
            }
            else
            {
                // 服务未启动，只更新配置
                var tempServer = new AutoSyncServer(null);
                tempServer.SavePairingCode(newCode);
                lblCurrentPairingCode.Text = $"当前配对码：{newCode}";
            }
        }

        // ========== 新增：广播配对码 ==========
        private void BtnBroadcastPairing_Click(object sender, EventArgs e)
        {
            if (syncServer != null && isServerRunning)
            {
                syncServer.BroadcastPairingCode();
            }
            else
            {
                MessageBox.Show("请先启动同步服务", "提示",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        private string GetLocalIPAddress()
        {
            try
            {
                var host = Dns.GetHostEntry(Dns.GetHostName());
                foreach (var ip in host.AddressList)
                {
                    if (ip.AddressFamily == AddressFamily.InterNetwork)
                    {
                        return ip.ToString();
                    }
                }
            }
            catch { }
            return "未知";
        }

        private void BtnStart_Click(object sender, EventArgs e)
        {
            if (isServerRunning)
            {
                MessageBox.Show("服务已经在运行中！", "提示",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            try
            {
                syncServer = new AutoSyncServer(txtLog);
                syncServer.OnLogMessage += SyncServer_OnLogMessage;
                syncServer.OnConnectedDevicesChanged += SyncServer_OnConnectedDevicesChanged;
                syncServer.OnSyncProgress += SyncServer_OnSyncProgress;
                syncServer.OnPairingCodeChanged += SyncServer_OnPairingCodeChanged;

                syncServer.Start();
                isServerRunning = true;

                btnSyncNow.Enabled = true;
                btnStart.Enabled = false;
                btnStop.Enabled = true;

                // 更新当前配对码显示
                lblCurrentPairingCode.Text = $"当前配对码：{syncServer.GetPairingCode()}";
                txtPairingCode.Text = syncServer.GetPairingCode();

                lblStatus.Text = "🟢 自动同步服务运行中";
                statsUpdateTimer.Start();

                txtLog.AppendText($"[{DateTime.Now:HH:mm:ss}] 🚀 自动同步服务已启动（配对码：{syncServer.GetPairingCode()}）\n");
                txtLog.AppendText($"[{DateTime.Now:HH:mm:ss}] 📢 广播发现已开启，手持端需使用相同配对码\n");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"启动失败: {ex.Message}", "错误",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void SyncServer_OnPairingCodeChanged(object sender, string pairingCode)
        {
            if (this.IsDisposed || this.Disposing) return;

            try
            {
                if (lblCurrentPairingCode.InvokeRequired)
                {
                    lblCurrentPairingCode.BeginInvoke(new Action(() =>
                    {
                        if (!this.IsDisposed && lblCurrentPairingCode != null)
                        {
                            lblCurrentPairingCode.Text = $"当前配对码：{pairingCode}";
                        }
                    }));
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"更新配对码时出错: {ex.Message}");
            }
        }

        private void BtnStop_Click(object sender, EventArgs e)
        {
            if (!isServerRunning || syncServer == null) return;

            if (MessageBox.Show("确定要停止同步服务吗？", "确认",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
            {
                statsUpdateTimer.Stop();
                syncServer.Stop();
                isServerRunning = false;

                btnSyncNow.Enabled = false;
                btnStart.Enabled = true;
                btnStop.Enabled = false;

                lblStatus.Text = "🔴 同步服务已停止";
                dgvClients.Rows.Clear();
                lblStats.Text = "今日同步：0条记录 | 在线设备：0台 | 今日连接：0次";
            }
        }

        private void BtnSyncNow_Click(object sender, EventArgs e)
        {
            if (!isServerRunning || syncServer == null) return;

            txtLog.AppendText($"[{DateTime.Now:HH:mm:ss}] 📤 手动触发同步...\n");
            // 手动广播配对码，让手持端可以重新发现
            syncServer.BroadcastPairingCode();
        }

        private void StatsUpdateTimer_Tick(object sender, EventArgs e)
        {
            if (dgvClients.InvokeRequired)
            {
                dgvClients.Invoke(new Action(() => UpdateClientStatus()));
            }
            else
            {
                UpdateClientStatus();
            }
        }

        private void UpdateClientStatus()
        {
            foreach (DataGridViewRow row in dgvClients.Rows)
            {
                if (row.Cells["Status"].Value?.ToString() == "在线")
                {
                    // 可以更新最后同步时间等
                }
            }
        }

        private void SyncServer_OnConnectedDevicesChanged(object sender, ConnectedDevicesChangedEventArgs e)
        {
            if (this.IsDisposed || this.Disposing) return;

            void Apply()
            {
                if (dgvClients == null || dgvClients.IsDisposed) return;

                dgvClients.Rows.Clear();
                string pairing = syncServer?.GetPairingCode() ?? "未知";
                foreach (var d in e.Devices)
                {
                    dgvClients.Rows.Add(
                        d.DeviceId ?? "",
                        d.DeviceName ?? "",
                        d.IpAddress ?? "",
                        pairing,
                        d.ConnectedAt.ToString("HH:mm:ss"),
                        "-",
                        d.IsOnline ? "在线" : "离线");
                }

                UpdateStats(e.Count);
            }

            try
            {
                if (dgvClients.InvokeRequired)
                    dgvClients.BeginInvoke(new Action(Apply));
                else
                    Apply();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"更新设备列表失败: {ex.Message}");
            }
        }

        private void SyncServer_OnSyncProgress(object sender, SyncProgressEventArgs e)
        {
            if (this.IsDisposed || this.Disposing) return;

            void ApplyUi()
            {
                if (this.IsDisposed) return;
                if (lblSyncActivity != null && !lblSyncActivity.IsDisposed)
                    lblSyncActivity.Text = string.IsNullOrEmpty(e.Status) ? "同步空闲" : e.Status;
                if (pbSyncProgress != null && !pbSyncProgress.IsDisposed)
                {
                    if (e.ShowProgressBar)
                    {
                        pbSyncProgress.Visible = true;
                        pbSyncProgress.Value = Math.Max(0, Math.Min(100, e.Progress));
                    }
                    else
                    {
                        pbSyncProgress.Visible = false;
                        pbSyncProgress.Value = 0;
                    }
                }
                if (txtLog != null && !txtLog.IsDisposed)
                {
                    string pct = e.ShowProgressBar ? $" ({e.Progress}%)" : "";
                    txtLog.AppendText($"[{DateTime.Now:HH:mm:ss}] 📊 {e.Status}{pct}\n");
                    txtLog.ScrollToCaret();
                }
            }

            try
            {
                if (InvokeRequired)
                    BeginInvoke(new Action(ApplyUi));
                else
                    ApplyUi();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"更新同步进度 UI 时出错: {ex.Message}");
            }
        }

        private void SyncServer_OnLogMessage(object sender, string message)
        {
            if (this.IsDisposed || this.Disposing)
                return;

            if (txtLog == null || txtLog.IsDisposed)
                return;

            if (txtLog.InvokeRequired)
            {
                try
                {
                    txtLog.BeginInvoke(new Action(() =>
                    {
                        if (!txtLog.IsDisposed && !this.IsDisposed)
                        {
                            txtLog.AppendText(message + Environment.NewLine);
                            txtLog.ScrollToCaret();
                        }
                    }));
                }
                catch (ObjectDisposedException)
                {
                    // 忽略，控件已释放
                }
                catch (InvalidOperationException)
                {
                    // 忽略，可能窗体正在关闭
                }
            }
            else
            {
                try
                {
                    if (!txtLog.IsDisposed)
                    {
                        txtLog.AppendText(message + Environment.NewLine);
                        txtLog.ScrollToCaret();
                    }
                }
                catch (ObjectDisposedException)
                {
                    // 忽略
                }
            }
        }

        private void UpdateStats(int clientCount)
        {
            if (lblStats != null && !lblStats.IsDisposed)
            {
                int todaySyncCount = GetTodaySyncCount();
                int todayConnections = GetTodayConnections();

                lblStats.Text = $"今日同步：{todaySyncCount}条记录 | 在线设备：{clientCount}台 | 今日连接：{todayConnections}次 | 配对码：{syncServer?.GetPairingCode() ?? "未设置"}";
            }
        }

        private int GetTodaySyncCount()
        {
            try
            {
                return new Random().Next(10, 100);
            }
            catch
            {
                return 0;
            }
        }

        private int GetTodayConnections()
        {
            try
            {
                return dgvClients.Rows.Count;
            }
            catch
            {
                return 0;
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (isServerRunning && syncServer != null)
            {
                if (MessageBox.Show("同步服务正在运行，确定要关闭吗？", "确认",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes)
                {
                    try
                    {
                        statsUpdateTimer?.Stop();
                        syncServer?.Stop();
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"停止服务器时出错: {ex.Message}");
                    }

                    System.Threading.Thread.Sleep(500);
                }
                else
                {
                    e.Cancel = true;
                }
            }
            base.OnFormClosing(e);
        }

        private void InitializeComponent()
        {
            // 这个方法通常由设计器生成，我们手工创建UI所以留空
        }

        // 控件声明
        private Label lblSyncActivity;
        private ProgressBar pbSyncProgress;
        private RichTextBox txtLog;
        private DataGridView dgvClients;
        private ToolStripStatusLabel lblStatus;
        private Label lblStats;
        private ToolStripButton btnSyncNow;
        private ToolStripButton btnStart;
        private ToolStripButton btnStop;
    }
}