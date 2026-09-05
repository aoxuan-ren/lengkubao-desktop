using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace lengkubao.desktop
{
    public class HistoricalArchiveForm : Form
    {
        private ListView listArchives;
        private TabControl tabDetail;
        private TextBox txtInfo;
        private DataGridView dgvStats;
        private ComboBox cmbClient;
        private Button btnOpenReport;
        private Button btnOpenFolder;
        private Button btnRefresh;
        private SplitContainer splitMain;

        private Panel balanceScrollPanel;
        private RichTextBox txtBalanceSummary;
        private DataGridView dgvBalanceDetails;
        private Label lblInboundTotal;
        private DataGridView dgvInboundStats;

        private List<YearEndArchiveMeta> _archives = new List<YearEndArchiveMeta>();
        private string _extractedDir;
        private string _archiveDbPath;
        private string _readOnlyConnStr;
        private int _currentFiscalYear;
        private readonly DatabaseManager _db = new DatabaseManager();

        public HistoricalArchiveForm()
        {
            InitializeComponent();
            LoadArchiveList();
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            CleanupExtracted();
            base.OnFormClosed(e);
        }

        private void InitializeComponent()
        {
            Text = "历史账本查阅";
            Size = new Size(960, 720);
            MinimumSize = new Size(640, 480);
            StartPosition = FormStartPosition.CenterParent;

            splitMain = new SplitContainer
            {
                Dock = DockStyle.Fill,
                FixedPanel = FixedPanel.Panel1
            };
            Controls.Add(splitMain);
            Shown += HistoricalArchiveForm_Shown;

            listArchives = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                FullRowSelect = true,
                GridLines = true
            };
            listArchives.Columns.Add("年度", 52);
            listArchives.Columns.Add("归档时间", 130);
            listArchives.SelectedIndexChanged += ListArchives_SelectedIndexChanged;

            btnRefresh = new Button { Text = "刷新列表", Dock = DockStyle.Top, Height = 32 };
            btnRefresh.Click += (s, e) => LoadArchiveList();

            splitMain.Panel1.Controls.Add(listArchives);
            splitMain.Panel1.Controls.Add(btnRefresh);

            tabDetail = new TabControl { Dock = DockStyle.Fill };
            splitMain.Panel2.Controls.Add(tabDetail);

            var tabInfo = new TabPage("归档信息");
            txtInfo = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                Font = new Font("Consolas", 9f)
            };
            btnOpenReport = new Button { Text = "打开报表文件夹", Dock = DockStyle.Bottom, Height = 32 };
            btnOpenReport.Click += BtnOpenReport_Click;
            btnOpenFolder = new Button { Text = "打开归档目录", Dock = DockStyle.Bottom, Height = 32 };
            btnOpenFolder.Click += BtnOpenFolder_Click;
            tabInfo.Controls.Add(txtInfo);
            tabInfo.Controls.Add(btnOpenReport);
            tabInfo.Controls.Add(btnOpenFolder);

            var tabStats = new TabPage("年度统计");
            dgvStats = new DataGridView
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill
            };
            tabStats.Controls.Add(dgvStats);

            var tabBalance = new TabPage("客户对账");
            balanceScrollPanel = new Panel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                Padding = new Padding(8)
            };

            var pnlBalanceTop = new Panel { Dock = DockStyle.Top, Height = 40 };
            cmbClient = new ComboBox
            {
                Location = new Point(10, 8),
                Width = 240,
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            var btnQueryBalance = new Button { Text = "查询", Location = new Point(260, 6), Size = new Size(70, 28) };
            btnQueryBalance.Click += BtnQueryBalance_Click;
            var lblHint = new Label
            {
                Text = "日期范围固定为所选归档年度",
                Location = new Point(340, 10),
                AutoSize = true,
                ForeColor = Color.Gray
            };
            pnlBalanceTop.Controls.Add(cmbClient);
            pnlBalanceTop.Controls.Add(btnQueryBalance);
            pnlBalanceTop.Controls.Add(lblHint);

            var lblSummaryTitle = new Label
            {
                Text = "对账结果",
                Dock = DockStyle.Top,
                Height = 22,
                Font = new Font("微软雅黑", 10f, FontStyle.Bold)
            };

            txtBalanceSummary = new RichTextBox
            {
                Dock = DockStyle.Top,
                Height = 130,
                ReadOnly = true,
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = Color.WhiteSmoke,
                Font = new Font("微软雅黑", 10f)
            };

            var lblDetailsTitle = new Label
            {
                Text = "详细交易记录",
                Dock = DockStyle.Top,
                Height = 22,
                Font = new Font("微软雅黑", 10f, FontStyle.Bold)
            };

            dgvBalanceDetails = new DataGridView
            {
                Dock = DockStyle.Top,
                Height = 220
            };
            ClientBalanceGridHelper.SetupDetailsGrid(dgvBalanceDetails);

            var lblInboundTitle = new Label
            {
                Text = "客户入库统计",
                Dock = DockStyle.Top,
                Height = 22,
                Font = new Font("微软雅黑", 10f, FontStyle.Bold)
            };

            lblInboundTotal = new Label
            {
                Text = "总入库: 0 件 | 型号数: 0 | 总单数: 0",
                Dock = DockStyle.Top,
                Height = 22,
                Font = new Font("微软雅黑", 9f, FontStyle.Bold),
                ForeColor = Color.FromArgb(0, 102, 102)
            };

            dgvInboundStats = new DataGridView
            {
                Dock = DockStyle.Top,
                Height = 180
            };
            ClientBalanceGridHelper.SetupInboundGrid(dgvInboundStats);

            balanceScrollPanel.Controls.Add(dgvInboundStats);
            balanceScrollPanel.Controls.Add(lblInboundTotal);
            balanceScrollPanel.Controls.Add(lblInboundTitle);
            balanceScrollPanel.Controls.Add(dgvBalanceDetails);
            balanceScrollPanel.Controls.Add(lblDetailsTitle);
            balanceScrollPanel.Controls.Add(txtBalanceSummary);
            balanceScrollPanel.Controls.Add(lblSummaryTitle);
            balanceScrollPanel.Controls.Add(pnlBalanceTop);

            tabBalance.Controls.Add(balanceScrollPanel);

            tabDetail.TabPages.Add(tabInfo);
            tabDetail.TabPages.Add(tabStats);
            tabDetail.TabPages.Add(tabBalance);
        }

        private void HistoricalArchiveForm_Shown(object sender, EventArgs e)
        {
            ApplyMainSplitLayout();
        }

        private void ApplyMainSplitLayout()
        {
            if (splitMain == null || splitMain.IsDisposed || splitMain.ClientSize.Width <= 0)
                return;

            try
            {
                splitMain.Panel1MinSize = 180;
                splitMain.Panel2MinSize = 240;

                const int desiredLeft = 210;
                int maxLeft = splitMain.Width - splitMain.Panel2MinSize - splitMain.SplitterWidth;
                if (maxLeft < splitMain.Panel1MinSize)
                    return;

                int distance = Math.Max(splitMain.Panel1MinSize, Math.Min(desiredLeft, maxLeft));
                if (splitMain.SplitterDistance != distance)
                    splitMain.SplitterDistance = distance;
            }
            catch (ArgumentException ex)
            {
                Console.WriteLine($">>> ApplyMainSplitLayout: {ex.Message}");
            }
        }

        private void LoadArchiveList()
        {
            listArchives.Items.Clear();
            _archives = YearEndArchiveManager.ListArchives();
            foreach (var meta in _archives)
            {
                var item = new ListViewItem(meta.FiscalYear.ToString());
                item.SubItems.Add(meta.ArchivedAt.ToString("yyyy-MM-dd HH:mm"));
                item.Tag = meta;
                listArchives.Items.Add(item);
            }
        }

        private void ListArchives_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (listArchives.SelectedItems.Count == 0)
                return;

            var meta = listArchives.SelectedItems[0].Tag as YearEndArchiveMeta;
            if (meta == null)
                return;

            LoadArchive(meta);
        }

        private void LoadArchive(YearEndArchiveMeta meta)
        {
            CleanupExtracted();

            try
            {
                _extractedDir = YearEndArchiveManager.ExtractArchiveToTemp(meta.ArchiveZipPath);
                _archiveDbPath = YearEndArchiveManager.FindDbInExtractedDir(_extractedDir, meta);
                if (string.IsNullOrEmpty(_archiveDbPath))
                {
                    txtInfo.Text = "无法从归档中找到数据库文件。";
                    return;
                }

                _readOnlyConnStr = YearEndArchiveManager.BuildReadOnlyConnectionString(_archiveDbPath);
                _currentFiscalYear = meta.FiscalYear;

                txtInfo.Text =
                    $"年度：{meta.FiscalYear}\r\n" +
                    $"归档时间：{meta.ArchivedAt:yyyy-MM-dd HH:mm:ss}\r\n" +
                    $"版本：{meta.AppVersion}\r\n" +
                    $"数据库：{meta.DbFileName}\r\n" +
                    $"SHA256：{meta.DbSha256}\r\n" +
                    $"期初应收合计：{meta.OpeningBalanceTotal:N2}\r\n" +
                    $"库存结转模式：{meta.InventoryCarryoverMode}\r\n" +
                    $"归档路径：{meta.ArchiveZipPath}\r\n\r\n" +
                    $"报表文件：\r\n" + string.Join("\r\n", meta.ReportFiles ?? new List<string>());

                DateTime start = new DateTime(meta.FiscalYear, 1, 1);
                DateTime end = new DateTime(meta.FiscalYear, 12, 31);
                dgvStats.DataSource = _db.QueryArchiveSummaryByDate(_readOnlyConnStr, start, end, "月");

                LoadArchiveClients();
            }
            catch (Exception ex)
            {
                txtInfo.Text = $"加载归档失败：{ex.Message}";
            }
        }

        private void LoadArchiveClients()
        {
            cmbClient.Items.Clear();
            try
            {
                using (var conn = new System.Data.SQLite.SQLiteConnection(_readOnlyConnStr))
                {
                    conn.Open();
                    using (var cmd = new System.Data.SQLite.SQLiteCommand(
                        "SELECT code, name FROM clients WHERE status=1 ORDER BY name", conn))
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            cmbClient.Items.Add(new ClientSearchItem
                            {
                                Code = reader["code"].ToString(),
                                Name = reader["name"].ToString(),
                                DisplayText = $"{reader["code"]} - {reader["name"]}"
                            });
                        }
                    }
                }

                if (cmbClient.Items.Count > 0)
                {
                    cmbClient.SelectedIndex = 0;
                    QuerySelectedClientBalance();
                }
                else
                {
                    ClearBalanceView("该归档中没有可用客户。");
                }
            }
            catch (Exception ex)
            {
                ClearBalanceView($"加载客户列表失败：{ex.Message}");
            }
        }

        private void BtnQueryBalance_Click(object sender, EventArgs e)
        {
            QuerySelectedClientBalance();
        }

        private void QuerySelectedClientBalance()
        {
            if (string.IsNullOrEmpty(_readOnlyConnStr) || cmbClient.SelectedItem == null)
                return;

            var client = (ClientSearchItem)cmbClient.SelectedItem;
            DateTime start = new DateTime(_currentFiscalYear, 1, 1);
            DateTime end = new DateTime(_currentFiscalYear, 12, 31);

            try
            {
                DataTable details = _db.GetClientTransactionDetailsFromConnection(
                    _readOnlyConnStr, client.Code, start, end);

                if (details.Rows.Count == 0)
                {
                    ClearBalanceView($"【{client.Name}】在 {_currentFiscalYear} 年度没有交易记录。");
                    DataTable inboundStats = _db.GetClientInboundStatisticsByLocationFromConnection(
                        _readOnlyConnStr, client.Code, start, end);
                    DataTable inboundTotal = _db.GetClientInboundTotalFromConnection(
                        _readOnlyConnStr, client.Code, start, end);
                    lblInboundTotal.Text = ClientBalanceGridHelper.BindInboundGrid(dgvInboundStats, inboundStats, inboundTotal);
                    return;
                }

                ClientBalanceSummaryResult summary = ClientBalanceSummaryHelper.Calculate(details);
                ClientBalanceSummaryHelper.ApplyToRichTextBox(txtBalanceSummary, summary);
                ClientBalanceGridHelper.BindDetailsGrid(dgvBalanceDetails, details);

                DataTable stats = _db.GetClientInboundStatisticsByLocationFromConnection(
                    _readOnlyConnStr, client.Code, start, end);
                DataTable total = _db.GetClientInboundTotalFromConnection(
                    _readOnlyConnStr, client.Code, start, end);
                lblInboundTotal.Text = ClientBalanceGridHelper.BindInboundGrid(dgvInboundStats, stats, total);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"查询失败：{ex.Message}", "错误");
            }
        }

        private void ClearBalanceView(string message)
        {
            txtBalanceSummary.Text = message;
            dgvBalanceDetails.Rows.Clear();
            dgvInboundStats.Rows.Clear();
            lblInboundTotal.Text = "总入库: 0 件 | 型号数: 0 | 总单数: 0";
        }

        private void BtnOpenReport_Click(object sender, EventArgs e)
        {
            if (string.IsNullOrEmpty(_extractedDir))
                return;
            string reportsDir = Path.Combine(_extractedDir, "reports");
            if (!Directory.Exists(reportsDir))
            {
                MessageBox.Show("该归档中没有 reports 文件夹。", "提示");
                return;
            }
            Process.Start("explorer.exe", reportsDir);
        }

        private void BtnOpenFolder_Click(object sender, EventArgs e)
        {
            if (listArchives.SelectedItems.Count == 0)
                return;
            var meta = listArchives.SelectedItems[0].Tag as YearEndArchiveMeta;
            if (meta == null || string.IsNullOrEmpty(meta.ArchiveZipPath))
                return;
            Process.Start("explorer.exe", Path.GetDirectoryName(meta.ArchiveZipPath));
        }

        private void CleanupExtracted()
        {
            _readOnlyConnStr = null;
            _archiveDbPath = null;
            _currentFiscalYear = 0;
            if (string.IsNullOrEmpty(_extractedDir))
                return;
            try
            {
                Directory.Delete(_extractedDir, true);
            }
            catch { }
            _extractedDir = null;
        }
    }
}
