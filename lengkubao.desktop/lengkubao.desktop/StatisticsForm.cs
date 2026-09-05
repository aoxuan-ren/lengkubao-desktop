using System;
using System.Collections.Generic;
using System.Data;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace lengkubao.desktop
{
    public partial class StatisticsForm : Form
    {
        private TabControl tabControl;
        private DateTimePicker dateFrom;
        private DateTimePicker dateTo;
        private Button btnExport;
        private Button btnClose;

        private DataGridView dgvByHandler;
        private DataGridView dgvByProduct;
        private DataGridView dgvByDate;
        private DataGridView dgvInventory;
        private DataGridView dgvDashboard;
        private DataGridView dgvByClient;

        private string _selectedPeriod = "日";
        private readonly Dictionary<string, Button> _periodButtons = new Dictionary<string, Button>(StringComparer.OrdinalIgnoreCase);
        private ComboBox cmbClientByStats;
        private List<ClientSearchItem> _clientItems = new List<ClientSearchItem>();
        private DataGridViewCellFormattingEventHandler _clientByStatsMergeHandler;
        private static readonly string[] ClientByStatsMergeColumns = { "客户编号", "客户名称", "库位" };
        private static readonly string[] ClientByStatsGroupColumns = { "客户编号", "库位" };
        public StatisticsForm()
        {
            InitializeForm();

            this.Shown += (s, e) =>
            {
                Console.WriteLine(">>> 窗体显示完成，开始延迟加载数据...");
                SafeLoadData();
            };
        }

        protected override void OnParentChanged(EventArgs e)
        {
            base.OnParentChanged(e);
            if (Parent != null)
                AutoScroll = false;
        }

        /// <summary>
        /// 在窗体句柄就绪后于 UI 线程执行；避免句柄未创建时调用 BeginInvoke/Invoke。
        /// </summary>
        private void RunOnUiThreadWhenReady(Action action)
        {
            if (action == null || IsDisposed)
                return;

            void Run()
            {
                if (!IsDisposed)
                    action();
            }

            if (InvokeRequired)
            {
                if (IsHandleCreated)
                    BeginInvoke(new Action(Run));
                else
                    HandleCreated += Deferred;
                return;
            }

            if (!IsHandleCreated)
            {
                HandleCreated += Deferred;
                return;
            }

            Run();

            void Deferred(object sender, EventArgs e)
            {
                HandleCreated -= Deferred;
                if (IsDisposed || !IsHandleCreated)
                    return;
                if (InvokeRequired)
                    BeginInvoke(new Action(Run));
                else
                    Run();
            }
        }

        private void SafeLoadData()
        {
            try
            {
                Console.WriteLine(">>> 安全加载数据...");
                LoadData();
            }
            catch (Exception ex)
            {
                // #region agent log
                AgentDebugLog.Write("D", "StatisticsForm.SafeLoadData", "fail",
                    "{\"type\":\"" + ex.GetType().Name + "\",\"msg\":\"" + AgentDebugLog.Escape(ex.Message) + "\"}");
                // #endregion
                Console.WriteLine($"!!! SafeLoadData异常: {ex.Message}\n{ex.StackTrace}");
                MessageBox.Show($"加载数据失败: {ex.Message}",
                               "数据加载错误",
                               MessageBoxButtons.OK,
                               MessageBoxIcon.Warning);
            }
        }

        private void InitializeForm()
        {
            // #region agent log
            AgentDebugLog.Write("C", "StatisticsForm.InitializeForm", "start", "{}");
            // #endregion
            this.Text = "多维查询统计";
            this.Size = new Size(1200, 700);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.AutoScroll = false;

            var root = new TableLayoutPanel();
            root.Dock = DockStyle.Fill;
            root.RowCount = 3;
            root.ColumnCount = 1;
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 52f));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48f));
            root.Padding = new Padding(6, 4, 6, 4);
            root.Margin = Padding.Empty;

            Panel filterPanel = new Panel();
            filterPanel.Dock = DockStyle.Fill;
            CreateFilterControls(filterPanel);
            root.Controls.Add(filterPanel, 0, 0);

            tabControl = new TabControl();
            tabControl.Dock = DockStyle.Fill;
            PopulateTabPages();
            // #region agent log
            AgentDebugLog.Write("C", "StatisticsForm.InitializeForm", "afterPopulate", "{\"tabCount\":" + tabControl.TabPages.Count + "}");
            // #endregion
            try
            {
                StatisticsUiHelper.ApplyStatisticsFormChrome(this, tabControl);
                // #region agent log
                AgentDebugLog.Write("C", "StatisticsForm.InitializeForm", "afterChrome", "{}");
                // #endregion
                ApplyAllGridStyles();
                // #region agent log
                AgentDebugLog.Write("C", "StatisticsForm.InitializeForm", "afterGridStyles", "{\"runId\":\"post-fix\"}");
                // #endregion
            }
            catch (Exception ex)
            {
                // #region agent log
                AgentDebugLog.Write("C", "StatisticsForm.InitializeForm", "styleFail",
                    "{\"type\":\"" + ex.GetType().Name + "\",\"msg\":\"" + AgentDebugLog.Escape(ex.Message) + "\"}");
                // #endregion
                throw;
            }
            root.Controls.Add(tabControl, 0, 1);

            Panel buttonPanel = new Panel();
            buttonPanel.Dock = DockStyle.Fill;
            CreateBottomButtons(buttonPanel);
            root.Controls.Add(buttonPanel, 0, 2);

            Controls.Add(root);
            // #region agent log
            AgentDebugLog.Write("C", "StatisticsForm.InitializeForm", "complete", "{}");
            // #endregion
        }

        private void CreateFilterControls(Panel parent)
        {
            int yPos = 8;

            Label lblDateFrom = new Label();
            lblDateFrom.Text = "开始日期:";
            lblDateFrom.Location = new Point(4, yPos + 3);
            lblDateFrom.Size = new Size(70, 25);
            lblDateFrom.Font = new Font("微软雅黑", 11f);
            lblDateFrom.ForeColor = StatisticsUiHelper.CellFore;
            lblDateFrom.TextAlign = ContentAlignment.MiddleRight;
            parent.Controls.Add(lblDateFrom);

            dateFrom = new DateTimePicker();
            dateFrom.Location = new Point(78, yPos);
            dateFrom.Size = new Size(120, 25);
            dateFrom.Format = DateTimePickerFormat.Short;
            parent.Controls.Add(dateFrom);

            Label lblDateTo = new Label();
            lblDateTo.Text = "结束日期:";
            lblDateTo.Location = new Point(208, yPos + 3);
            lblDateTo.Size = new Size(70, 25);
            lblDateTo.Font = new Font("微软雅黑", 11f);
            lblDateTo.ForeColor = StatisticsUiHelper.CellFore;
            lblDateTo.TextAlign = ContentAlignment.MiddleRight;
            parent.Controls.Add(lblDateTo);

            dateTo = new DateTimePicker();
            dateTo.Location = new Point(282, yPos);
            dateTo.Size = new Size(120, 25);
            dateTo.Format = DateTimePickerFormat.Short;
            parent.Controls.Add(dateTo);
            DateRangeSettings.ApplyTo(dateFrom, dateTo);

            CreatePeriodSelector(parent, 408, yPos);

            Label lblInfo = new Label();
            lblInfo.Text = "选择日期范围后切换周期查看";
            lblInfo.Location = new Point(680, yPos + 3);
            lblInfo.AutoSize = true;
            lblInfo.Font = new Font("微软雅黑", 10.5f);
            lblInfo.ForeColor = Color.FromArgb(100, 116, 139);
            parent.Controls.Add(lblInfo);
        }

        private void CreatePeriodSelector(Panel parent, int x, int y)
        {
            var periods = new[]
            {
                ("日", "按日"),
                ("周", "按周"),
                ("月", "按月"),
                ("年", "按年")
            };

            int btnX = x;
            foreach (var period in periods)
            {
                var btn = new Button
                {
                    Text = period.Item2,
                    Tag = period.Item1,
                    Location = new Point(btnX, y - 1),
                    Size = new Size(64, 30),
                    FlatStyle = FlatStyle.Flat,
                    Font = new Font("微软雅黑", 10.5f, FontStyle.Regular),
                    Cursor = Cursors.Hand
                };
                btn.FlatAppearance.BorderSize = 0;
                string key = period.Item1;
                btn.Click += (s, e) => SelectPeriod(key);
                parent.Controls.Add(btn);
                _periodButtons[key] = btn;
                btnX += btn.Width + 6;
            }

            SelectPeriod("日", reload: false);
        }

        private void SelectPeriod(string period, bool reload = true)
        {
            if (string.IsNullOrEmpty(period))
                period = "日";

            _selectedPeriod = period;
            Color activeBack = Color.FromArgb(37, 99, 235);
            Color inactiveBack = Color.FromArgb(226, 232, 240);

            foreach (var kv in _periodButtons)
            {
                bool active = string.Equals(kv.Key, period, StringComparison.OrdinalIgnoreCase);
                kv.Value.BackColor = active ? activeBack : inactiveBack;
                kv.Value.ForeColor = active ? Color.White : StatisticsUiHelper.CellFore;
                kv.Value.Font = active
                    ? new Font("微软雅黑", 10.5f, FontStyle.Bold)
                    : new Font("微软雅黑", 10.5f, FontStyle.Regular);
            }

            if (reload)
                ReloadDateStatistics();
        }

        private void ReloadDateStatistics()
        {
            if (dateFrom == null || dateTo == null || dgvByDate == null || dgvByDate.IsDisposed)
                return;

            DateTime startDate = dateFrom.Value.Date;
            DateTime endDate = dateTo.Value.Date;
            if (startDate > endDate)
                return;

            try
            {
                LoadDateData(new DatabaseManager(), startDate, endDate, _selectedPeriod);
                UpdateColumnHeaders();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"日期统计刷新失败: {ex.Message}");
            }
        }

        private void ApplyAllGridStyles()
        {
            StatisticsUiHelper.ApplyStatisticsGridStyle(dgvDashboard, null, "金额", "笔数");

            StatisticsUiHelper.ApplyStatisticsGridStyle(dgvByHandler, null,
                "业务笔数", "销售数量", "销售金额", "入库数量", "入库金额");

            StatisticsUiHelper.ApplyStatisticsGridStyle(dgvByProduct, null,
                "入库数量", "入库金额", "销售数量", "销售金额", "当前库存");

            StatisticsUiHelper.ApplyStatisticsGridStyle(dgvByDate, null,
                "业务笔数", "销售数量", "销售金额", "入库数量", "入库金额", "包装数量", "包装金额");

            StatisticsUiHelper.ApplyStatisticsGridStyle(dgvInventory, null,
                "累计入库", "预售数量", "出库数量", "当前库存", "入库金额", "出库金额");

            StatisticsUiHelper.ApplyStatisticsGridStyle(dgvByClient, Color.FromArgb(15, 118, 110));
        }

        private void PopulateTabPages()
        {
            TabPage tabDashboard = new TabPage("数据总览");
            CreateDashboardTab(tabDashboard);
            tabControl.TabPages.Add(tabDashboard);

            TabPage tabByHandler = new TabPage("按经手人统计");
            CreateByHandlerTab(tabByHandler);
            tabControl.TabPages.Add(tabByHandler);

            TabPage tabByProduct = new TabPage("按商品型号统计");
            CreateByProductTab(tabByProduct);
            tabControl.TabPages.Add(tabByProduct);

            TabPage tabByDate = new TabPage("按日期统计");
            CreateByDateTab(tabByDate);
            tabControl.TabPages.Add(tabByDate);
            tabControl.SelectedTab = tabByDate;

            TabPage tabInventory = new TabPage("库位库存查询");
            CreateInventoryTab(tabInventory);
            tabControl.TabPages.Add(tabInventory);

            TabPage tabByClient = new TabPage("按客户统计");
            CreateByClientTab(tabByClient);
            tabControl.TabPages.Add(tabByClient);
        }

        private void CreateDashboardTab(TabPage tabPage)
        {
            StatisticsUiHelper.PrepareStatisticsTab(tabPage);
            dgvDashboard = new DataGridView();
            dgvDashboard.Dock = DockStyle.Fill;
            dgvDashboard.ReadOnly = true;

            dgvDashboard.Columns.Add("统计项", "统计项");
            dgvDashboard.Columns.Add("金额", "金额");
            dgvDashboard.Columns.Add("笔数", "笔数");

            dgvDashboard.Columns["金额"].DefaultCellStyle.Format = "N2";
            dgvDashboard.Columns["统计项"].Width = 150;

            tabPage.Controls.Add(dgvDashboard);
        }

        private void CreateByHandlerTab(TabPage tabPage)
        {
            StatisticsUiHelper.PrepareStatisticsTab(tabPage);
            dgvByHandler = new DataGridView();
            dgvByHandler.Dock = DockStyle.Fill;
            dgvByHandler.ReadOnly = true;

            string[] columns = { "经手人", "业务笔数", "销售数量", "销售金额", "入库数量", "入库金额" };
            foreach (string column in columns)
            {
                dgvByHandler.Columns.Add(column, column);
            }

            dgvByHandler.Columns["销售金额"].DefaultCellStyle.Format = "N2";
            dgvByHandler.Columns["入库金额"].DefaultCellStyle.Format = "N2";

            tabPage.Controls.Add(dgvByHandler);
        }

        private void CreateByProductTab(TabPage tabPage)
        {
            StatisticsUiHelper.PrepareStatisticsTab(tabPage);
            dgvByProduct = new DataGridView();
            dgvByProduct.Dock = DockStyle.Fill;
            dgvByProduct.ReadOnly = true;

            string[] columns = { "商品型号", "入库数量", "入库金额", "销售数量", "销售金额", "当前库存" };
            foreach (string column in columns)
            {
                dgvByProduct.Columns.Add(column, column);
            }

            dgvByProduct.Columns["入库金额"].DefaultCellStyle.Format = "N2";
            dgvByProduct.Columns["销售金额"].DefaultCellStyle.Format = "N2";

            dgvByProduct.CellFormatting += (sender, e) =>
            {
                try
                {
                    if (e.RowIndex < 0 || e.ColumnIndex < 0) return;
                    var stockCol = dgvByProduct.Columns["当前库存"];
                    #region agent log
                    if (stockCol == null)
                    {
                        AgentDebugLog.Write("A", "StatisticsForm.dgvByProduct.CellFormatting",
                            "stock column missing during format",
                            "{\"columnCount\":" + dgvByProduct.Columns.Count
                            + ",\"columnNames\":\"" + AgentDebugLog.Escape(string.Join("|", dgvByProduct.Columns.Cast<DataGridViewColumn>().Select(c => c.Name)))
                            + "\",\"rowIndex\":" + e.RowIndex + ",\"colIndex\":" + e.ColumnIndex + "}");
                        return;
                    }
                    #endregion
                    if (e.ColumnIndex != stockCol.Index || e.Value == null) return;

                    if (int.TryParse(e.Value.ToString(), out int stock) && stock <= 0)
                    {
                        e.CellStyle.ForeColor = Color.FromArgb(220, 38, 38);
                        e.CellStyle.Font = StatisticsUiHelper.StatsGridCellBoldFont;
                    }
                    else if (stock > 0)
                    {
                        e.CellStyle.ForeColor = Color.FromArgb(22, 163, 74);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"商品库存列格式化错误: {ex.Message}");
                }
            };

            tabPage.Controls.Add(dgvByProduct);
        }

        private void CreateByDateTab(TabPage tabPage)
        {
            StatisticsUiHelper.PrepareStatisticsTab(tabPage);
            dgvByDate = new DataGridView();
            dgvByDate.Dock = DockStyle.Fill;
            dgvByDate.ReadOnly = true;

            string[] columns = { "期间", "业务笔数", "销售数量", "销售金额", "入库数量", "入库金额", "包装数量", "包装金额" };
            foreach (string column in columns)
            {
                dgvByDate.Columns.Add(column, column);
            }

            for (int i = 2; i < dgvByDate.Columns.Count; i++)
            {
                if (dgvByDate.Columns[i].HeaderText.Contains("金额"))
                    dgvByDate.Columns[i].DefaultCellStyle.Format = "N2";
            }

            tabPage.Controls.Add(dgvByDate);
        }

        private void CreateInventoryTab(TabPage tabPage)
        {
            StatisticsUiHelper.PrepareStatisticsTab(tabPage);
            dgvInventory = new DataGridView();
            dgvInventory.Dock = DockStyle.Fill;
            dgvInventory.ReadOnly = true;

            // 添加列
            string[] columns = { "库位", "商品型号", "累计入库", "预售数量", "出库数量", "当前库存", "入库金额", "出库金额" };
            foreach (string column in columns)
            {
                dgvInventory.Columns.Add(column, column);
            }

            // 设置列样式
            dgvInventory.Columns["入库金额"].DefaultCellStyle.Format = "N2";
            dgvInventory.Columns["出库金额"].DefaultCellStyle.Format = "N2";

            // 重要：先保存列索引，避免在事件中动态查找
            int stockColumnIndex = dgvInventory.Columns["当前库存"].Index;

            // 添加事件处理器，使用保存的索引
            dgvInventory.CellFormatting += (sender, e) =>
            {
                try
                {
                    // 基本安全检查
                    if (e.RowIndex < 0 || e.ColumnIndex < 0) return;
                    if (e.ColumnIndex != stockColumnIndex) return; // 只处理当前库存列
                    if (e.Value == null) return;

                    if (int.TryParse(e.Value.ToString(), out int stock))
                    {
                        if (stock <= 0)
                        {
                            e.CellStyle.ForeColor = Color.FromArgb(220, 38, 38);
                            e.CellStyle.Font = StatisticsUiHelper.StatsGridCellBoldFont;
                        }
                        else
                        {
                            e.CellStyle.ForeColor = Color.FromArgb(22, 163, 74);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"库存列格式化错误: {ex.Message}");
                    // 不抛出异常，防止程序崩溃
                }
            };

            tabPage.Controls.Add(dgvInventory);
        }

        private void CreateByClientTab(TabPage tabPage)
        {
            StatisticsUiHelper.PrepareStatisticsTab(tabPage);
            TableLayoutPanel tableLayout = new TableLayoutPanel();
            tableLayout.Dock = DockStyle.Fill;
            tableLayout.RowCount = 2;
            tableLayout.ColumnCount = 1;
            tableLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
            tableLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            tableLayout.Padding = new Padding(0);
            tableLayout.BackColor = StatisticsUiHelper.PanelBack;

            Panel searchPanel = new Panel();
            searchPanel.Dock = DockStyle.Fill;
            searchPanel.BackColor = Color.White;
            searchPanel.Padding = new Padding(12, 10, 12, 10);

            var searchFlow = new FlowLayoutPanel();
            searchFlow.Dock = DockStyle.Fill;
            searchFlow.FlowDirection = FlowDirection.LeftToRight;
            searchFlow.WrapContents = false;
            searchFlow.AutoSize = false;
            searchFlow.Padding = new Padding(0);
            searchFlow.Margin = new Padding(0);

            cmbClientByStats = new ComboBox
            {
                Width = 300,
                Height = 32,
                Margin = new Padding(0, 0, 10, 0),
                Font = new Font("微软雅黑", 12f),
                BackColor = Color.White,
                FlatStyle = FlatStyle.Flat
            };
            ClientSearchHelper.ApplySearchableStyle(cmbClientByStats, 360);

            Button btnSearchClient = new Button();
            btnSearchClient.Text = "搜索";
            btnSearchClient.Size = new Size(88, 32);
            btnSearchClient.Margin = new Padding(0, 0, 10, 0);
            btnSearchClient.Font = new Font("微软雅黑", 11f, FontStyle.Bold);
            btnSearchClient.BackColor = StatisticsUiHelper.SelectionBack;
            btnSearchClient.ForeColor = Color.White;
            btnSearchClient.FlatStyle = FlatStyle.Flat;
            btnSearchClient.FlatAppearance.BorderSize = 0;

            Button btnRefreshClient = new Button();
            btnRefreshClient.Text = "刷新";
            btnRefreshClient.Size = new Size(88, 32);
            btnRefreshClient.Margin = new Padding(0);
            btnRefreshClient.Font = new Font("微软雅黑", 11f);
            btnRefreshClient.BackColor = Color.FromArgb(100, 116, 139);
            btnRefreshClient.ForeColor = Color.White;
            btnRefreshClient.FlatStyle = FlatStyle.Flat;
            btnRefreshClient.FlatAppearance.BorderSize = 0;

            searchFlow.Controls.Add(cmbClientByStats);
            searchFlow.Controls.Add(btnSearchClient);
            searchFlow.Controls.Add(btnRefreshClient);
            searchPanel.Controls.Add(searchFlow);

            // 表格面板
            Panel tablePanel = new Panel();
            tablePanel.Dock = DockStyle.Fill;
            tablePanel.Padding = new Padding(0, 5, 0, 0);

            dgvByClient = new DataGridView();
            dgvByClient.Dock = DockStyle.Fill;
            dgvByClient.AutoGenerateColumns = false;
            dgvByClient.ReadOnly = true;
            dgvByClient.Name = "dgvByClient";
            dgvByClient.CellFormatting += ClientByStatsCellFormatting;

            tablePanel.Controls.Add(dgvByClient);

            tableLayout.Controls.Add(searchPanel, 0, 0);
            tableLayout.Controls.Add(tablePanel, 0, 1);

            tabPage.Controls.Add(tableLayout);

            btnSearchClient.Click += (s, e) => SearchWarehouse();
            btnRefreshClient.Click += (s, e) => RefreshWarehouse();

            cmbClientByStats.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter)
                {
                    SearchWarehouse();
                    e.Handled = true;
                    e.SuppressKeyPress = true;
                }
            };
        }

        private void LoadClientFilterData()
        {
            try
            {
                DatabaseManager dbManager = new DatabaseManager();
                _clientItems = ClientSearchHelper.BuildItems(dbManager.GetAllClients());
                if (cmbClientByStats == null || cmbClientByStats.IsDisposed)
                    return;

                ClientSearchHelper.BindSearchableCombo(cmbClientByStats, _clientItems, new ClientSearchHelper.SearchComboOptions
                {
                    PlaceholderText = ClientSearchHelper.PlaceholderText
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"加载客户筛选列表失败: {ex.Message}");
            }
        }

        private void ClientByStatsCellFormatting(object sender, DataGridViewCellFormattingEventArgs e)
        {
            if (e.RowIndex < 0 || e.ColumnIndex < 0)
                return;

            DataGridView gridView = sender as DataGridView;
            if (gridView == null || gridView.Columns[e.ColumnIndex].Name != "库存")
                return;

            if (IsClientStatsPlaceholderRow(e.RowIndex))
                return;

            if (e.Value != null && long.TryParse(e.Value.ToString(), out long stock) && stock < 0)
            {
                e.CellStyle.ForeColor = Color.FromArgb(220, 38, 38);
                e.CellStyle.Font = StatisticsUiHelper.StatsGridCellBoldFont;
            }
        }

        private bool IsClientStatsPlaceholderRow(int rowIndex)
        {
            if (dgvByClient == null || rowIndex < 0 || rowIndex >= dgvByClient.Rows.Count)
                return true;

            string model = dgvByClient.Rows[rowIndex].Cells["商品型号"]?.Value?.ToString()?.Trim() ?? string.Empty;
            if (string.IsNullOrEmpty(model))
                return true;

            string clientName = dgvByClient.Rows[rowIndex].Cells["客户名称"]?.Value?.ToString() ?? string.Empty;
            return clientName.Contains("请选择") || clientName.Contains("暂无");
        }

        private void ApplyClientByStatsDisplayEnhancements()
        {
            if (dgvByClient == null)
                return;

            if (_clientByStatsMergeHandler != null)
            {
                dgvByClient.CellFormatting -= _clientByStatsMergeHandler;
                _clientByStatsMergeHandler = null;
            }

            _clientByStatsMergeHandler = StatisticsUiHelper.CreateConsecutiveCellMergeHandler(
                dgvByClient,
                ClientByStatsMergeColumns,
                IsClientStatsPlaceholderRow);

            if (_clientByStatsMergeHandler != null)
                dgvByClient.CellFormatting += _clientByStatsMergeHandler;

            StatisticsUiHelper.ApplyGroupRowColors(
                dgvByClient,
                ClientByStatsGroupColumns,
                IsClientStatsPlaceholderRow);
        }

        private void ReloadClientStatistics(ClientSearchItem selectedClient, bool showMessage = false)
        {
            if (dgvByClient == null || dgvByClient.IsDisposed)
                return;

            DateTime startDate = dateFrom.Value.Date;
            DateTime endDate = dateTo.Value.Date;
            if (startDate > endDate)
            {
                MessageBox.Show("开始日期不能晚于结束日期！", "提示");
                return;
            }

            try
            {
                Cursor = Cursors.WaitCursor;

                DatabaseManager dbManager = new DatabaseManager();
                DataTable warehouseData = dbManager.GetClientInventoryByLocationAndSpec(
                    selectedClient?.Code ?? string.Empty,
                    selectedClient?.Name ?? string.Empty,
                    startDate,
                    endDate);

                if (warehouseData == null || warehouseData.Rows.Count == 0)
                    warehouseData = CreateEmptyClientStatsNoDataTable();

                SafeBindWarehouseData(warehouseData);

                if (showMessage)
                    MessageBox.Show($"搜索到 {warehouseData.Rows.Count} 条记录", "搜索结果");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"加载客户统计失败: {ex.Message}", "错误");
            }
            finally
            {
                Cursor = Cursors.Default;
            }
        }

        private void SearchWarehouse()
        {
            if (!ClientSearchHelper.TryGetSelectedItem(cmbClientByStats, _clientItems, out ClientSearchItem clientItem))
            {
                MessageBox.Show("请选择客户！", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                cmbClientByStats?.Focus();
                return;
            }

            ReloadClientStatistics(clientItem, showMessage: true);
        }

        private void RefreshWarehouse()
        {
            if (cmbClientByStats != null)
                ClientSearchHelper.ResetToPlaceholder(cmbClientByStats);

            SafeBindWarehouseData(CreateEmptyWarehouseTable());
            MessageBox.Show("已刷新，请选择客户后搜索", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private DataTable CreateEmptyDashboardTable()
        {
            DataTable table = new DataTable();
            table.Columns.Add("统计项", typeof(string));
            table.Columns.Add("金额", typeof(decimal));
            table.Columns.Add("笔数", typeof(int));

            string[] items = { "今日入库", "今日销售", "本月入库", "本月销售", "累计客户" };
            foreach (string item in items)
            {
                DataRow row = table.NewRow();
                row["统计项"] = item;
                row["金额"] = 0;
                row["笔数"] = 0;
                table.Rows.Add(row);
            }

            return table;
        }

        private DataTable CreateEmptyHandlerTable()
        {
            DataTable table = new DataTable();
            table.Columns.Add("经手人", typeof(string));
            table.Columns.Add("业务笔数", typeof(int));
            table.Columns.Add("销售数量", typeof(int));
            table.Columns.Add("销售金额", typeof(decimal));
            table.Columns.Add("入库数量", typeof(int));
            table.Columns.Add("入库金额", typeof(decimal));
            return table;
        }

        private DataTable CreateEmptyProductTable()
        {
            DataTable table = new DataTable();
            table.Columns.Add("商品型号", typeof(string));
            table.Columns.Add("入库数量", typeof(int));
            table.Columns.Add("入库金额", typeof(decimal));
            table.Columns.Add("销售数量", typeof(int));
            table.Columns.Add("销售金额", typeof(decimal));
            table.Columns.Add("当前库存", typeof(int));
            return table;
        }

        private DataTable CreateEmptyDateTable()
        {
            DataTable table = new DataTable();
            table.Columns.Add("期间", typeof(string));
            table.Columns.Add("业务笔数", typeof(int));
            table.Columns.Add("销售数量", typeof(int));
            table.Columns.Add("销售金额", typeof(decimal));
            table.Columns.Add("入库数量", typeof(int));
            table.Columns.Add("入库金额", typeof(decimal));
            table.Columns.Add("包装数量", typeof(int));
            table.Columns.Add("包装金额", typeof(decimal));
            return table;
        }

        private DataTable CreateEmptyInventoryTable()
        {
            DataTable table = new DataTable();
            table.Columns.Add("库位", typeof(string));
            table.Columns.Add("商品型号", typeof(string));
            table.Columns.Add("累计入库", typeof(int));
            table.Columns.Add("预售数量", typeof(int));
            table.Columns.Add("出库数量", typeof(int));
            table.Columns.Add("当前库存", typeof(int));
            table.Columns.Add("入库金额", typeof(decimal));
            table.Columns.Add("出库金额", typeof(decimal));
            return table;
        }

        private DataTable CreateEmptyWarehouseTable()
        {
            DataTable table = CreateClientStatsTableSchema();
            DataRow row = table.NewRow();
            row["客户名称"] = "请选择客户后搜索";
            table.Rows.Add(row);
            return table;
        }

        private DataTable CreateEmptyClientStatsNoDataTable()
        {
            DataTable table = CreateClientStatsTableSchema();
            DataRow row = table.NewRow();
            row["客户名称"] = "该客户在选定日期内暂无数据";
            table.Rows.Add(row);
            return table;
        }

        private static DataTable CreateClientStatsTableSchema()
        {
            DataTable table = new DataTable();
            table.Columns.Add("客户编号", typeof(string));
            table.Columns.Add("客户名称", typeof(string));
            table.Columns.Add("库位", typeof(string));
            table.Columns.Add("商品型号", typeof(string));
            table.Columns.Add("入库总数", typeof(long));
            table.Columns.Add("销售总数", typeof(long));
            table.Columns.Add("库存", typeof(long));
            return table;
        }

        private void SafeBindWarehouseData(DataTable data)
        {
            if (dgvByClient == null) return;

            try
            {
                dgvByClient.SuspendLayout();
                dgvByClient.DataSource = null;

                if (data == null || data.Rows.Count == 0)
                {
                    data = CreateEmptyWarehouseTable();
                }

                dgvByClient.Columns.Clear();

                foreach (DataColumn col in data.Columns)
                {
                    DataGridViewTextBoxColumn gridCol = new DataGridViewTextBoxColumn();
                    gridCol.Name = col.ColumnName;
                    gridCol.HeaderText = col.ColumnName;
                    gridCol.DataPropertyName = col.ColumnName;
                    gridCol.ReadOnly = true;

                    if (col.ColumnName == "客户编号")
                        gridCol.Width = 90;
                    else if (col.ColumnName == "客户名称")
                        gridCol.Width = 110;
                    else if (col.ColumnName == "库位")
                        gridCol.Width = 90;
                    else if (col.ColumnName == "商品型号")
                        gridCol.Width = 100;
                    else if (col.ColumnName == "入库总数" || col.ColumnName == "销售总数" || col.ColumnName == "库存")
                    {
                        gridCol.Width = 90;
                        gridCol.DefaultCellStyle.Format = "N0";
                    }
                    else
                    {
                        gridCol.Width = 80;
                    }

                    dgvByClient.Columns.Add(gridCol);
                }

                dgvByClient.DataSource = data;

                StatisticsUiHelper.ApplyStatisticsGridStyle(dgvByClient, Color.FromArgb(15, 118, 110));
                StatisticsUiHelper.ApplyStatisticsNumericColumnsFromHeaders(dgvByClient);
                ApplyClientByStatsDisplayEnhancements();

                dgvByClient.Refresh();

                Console.WriteLine($"库位数据显示完成：{data.Rows.Count} 行数据，{data.Columns.Count} 列");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"绑定库位数据失败: {ex.Message}");
                ShowEmptyGridMessage(dgvByClient, "库位数据显示错误");
            }
            finally
            {
                dgvByClient.ResumeLayout();
            }
        }

        private void FixAllGridColumns()
        {
            FixGridColumns(dgvDashboard);
            FixGridColumns(dgvByHandler);
            FixGridColumns(dgvByProduct);
            FixGridColumns(dgvByDate);
            FixGridColumns(dgvInventory);
        }

        private void FixGridColumns(DataGridView gridView)
        {
            if (gridView == null) return;

            #region agent log
            if (ReferenceEquals(gridView, dgvByProduct))
            {
                AgentDebugLog.Write("B", "StatisticsForm.FixGridColumns",
                    "clearing dgvByProduct columns",
                    "{\"beforeCount\":" + dgvByProduct.Columns.Count
                    + ",\"beforeNames\":\"" + AgentDebugLog.Escape(string.Join("|", dgvByProduct.Columns.Cast<DataGridViewColumn>().Select(c => c.Name))) + "\"}");
            }
            #endregion

            gridView.SuspendLayout();
            gridView.DataSource = null;
            gridView.Columns.Clear();
            gridView.AutoGenerateColumns = false;
            gridView.ResumeLayout();
        }

        private void ShowEmptyGridMessage(DataGridView gridView, string message)
        {
            if (gridView == null) return;

            try
            {
                #region agent log
                if (ReferenceEquals(gridView, dgvByProduct))
                {
                    AgentDebugLog.Write("D", "StatisticsForm.ShowEmptyGridMessage",
                        "replacing dgvByProduct columns with Message",
                        "{\"message\":\"" + AgentDebugLog.Escape(message) + "\",\"beforeCount\":" + dgvByProduct.Columns.Count + "}");
                }
                #endregion
                gridView.Columns.Clear();
                gridView.Rows.Clear();

                DataGridViewTextBoxColumn column = new DataGridViewTextBoxColumn();
                column.Name = "Message";
                column.HeaderText = "提示";
                column.ReadOnly = true;
                column.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
                gridView.Columns.Add(column);

                int rowIndex = gridView.Rows.Add();
                gridView.Rows[rowIndex].Cells["Message"].Value = message;
                gridView.Rows[rowIndex].DefaultCellStyle.ForeColor = Color.Gray;
                gridView.Rows[rowIndex].DefaultCellStyle.Font =
                    new Font("微软雅黑", 12f, FontStyle.Italic);
                gridView.Rows[rowIndex].DefaultCellStyle.Alignment = StatisticsUiHelper.UniformAlignment;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"显示空消息失败: {ex.Message}");
            }
        }

        private void CreateBottomButtons(Panel parent)
        {
            var flow = new FlowLayoutPanel();
            flow.Dock = DockStyle.Fill;
            flow.FlowDirection = FlowDirection.RightToLeft;
            flow.WrapContents = false;
            flow.Padding = new Padding(0, 2, 0, 2);
            flow.AutoSize = false;

            btnClose = new Button();
            btnClose.Text = "关闭";
            btnClose.Size = new Size(128, 38);
            btnClose.Margin = new Padding(8, 0, 0, 0);
            btnClose.BackColor = Color.FromArgb(220, 38, 38);
            btnClose.ForeColor = Color.White;
            btnClose.Font = new Font("微软雅黑", 11f, FontStyle.Bold);
            btnClose.FlatStyle = FlatStyle.Flat;
            btnClose.FlatAppearance.BorderSize = 0;
            btnClose.Click += (s, e) => Close();

            btnExport = new Button();
            btnExport.Text = "导出Excel";
            btnExport.Size = new Size(128, 38);
            btnExport.Margin = new Padding(8, 0, 0, 0);
            btnExport.BackColor = Color.FromArgb(22, 163, 74);
            btnExport.ForeColor = Color.White;
            btnExport.Font = new Font("微软雅黑", 11f, FontStyle.Bold);
            btnExport.FlatStyle = FlatStyle.Flat;
            btnExport.FlatAppearance.BorderSize = 0;
            btnExport.Click += BtnExport_Click;

            flow.Controls.Add(btnClose);
            flow.Controls.Add(btnExport);
            parent.Controls.Add(flow);
        }

        private void LoadData()
        {
            try
            {
                Cursor = Cursors.WaitCursor;

                Console.WriteLine(">>> 开始加载数据...");

                FixAllGridColumns();

                DateTime startDate = dateFrom.Value.Date;
                DateTime endDate = dateTo.Value.Date;
                if (startDate > endDate)
                {
                    MessageBox.Show("开始日期不能晚于结束日期！", "提示");
                    return;
                }

                DateRangeSettings.SaveDefault(startDate, endDate);
                string periodType = GetSelectedPeriod();

                Console.WriteLine($">>> 查询参数: {startDate:yyyy-MM-dd} 至 {endDate:yyyy-MM-dd}, 周期: {periodType}");

                DatabaseManager databaseManager = new DatabaseManager();
                LoadClientFilterData();

                LoadDashboardData(databaseManager, startDate, endDate);
                LoadHandlerData(databaseManager, startDate, endDate);
                LoadProductData(databaseManager, startDate, endDate);
                LoadDateData(databaseManager, startDate, endDate, periodType);
                LoadInventoryData(databaseManager);
                LoadClientData(databaseManager, startDate, endDate);

                UpdateColumnHeaders();

                Console.WriteLine(">>> 数据加载完成");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"!!! LoadData主方法异常: {ex.Message}\n{ex.StackTrace}");
                MessageBox.Show($"加载统计数据失败: {ex.Message}", "错误",
                               MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                Cursor = Cursors.Default;
            }
        }

        private void LoadDashboardData(DatabaseManager db, DateTime startDate, DateTime endDate)
        {
            try
            {
                Console.WriteLine(">>> 加载仪表板数据...");
                DataTable data = db.GetDashboardSummary(startDate, endDate);
                if (data == null || data.Rows.Count == 0)
                {
                    data = CreateEmptyDashboardTable();
                }
                SafeBindDataToGrid(dgvDashboard, data, "仪表板");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"仪表板数据加载失败: {ex.Message}");
                DataTable emptyData = CreateEmptyDashboardTable();
                SafeBindDataToGrid(dgvDashboard, emptyData, "仪表板");
            }
        }

        private void LoadHandlerData(DatabaseManager db, DateTime startDate, DateTime endDate)
        {
            try
            {
                Console.WriteLine(">>> 加载经手人数据...");
                DataTable data = db.GetSummaryByHandler(startDate, endDate);
                if (data == null || data.Rows.Count == 0)
                {
                    data = CreateEmptyHandlerTable();
                }
                SafeBindDataToGrid(dgvByHandler, data, "经手人统计");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"经手人数据加载失败: {ex.Message}");
                DataTable emptyData = CreateEmptyHandlerTable();
                SafeBindDataToGrid(dgvByHandler, emptyData, "经手人统计");
            }
        }

        private void LoadProductData(DatabaseManager db, DateTime startDate, DateTime endDate)
        {
            try
            {
                Console.WriteLine(">>> 加载商品数据...");
                DataTable data = db.GetSummaryByProductType(startDate, endDate);
                if (data == null || data.Rows.Count == 0)
                {
                    data = CreateEmptyProductTable();
                }
                SafeBindDataToGrid(dgvByProduct, data, "商品统计");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"商品数据加载失败: {ex.Message}");
                DataTable emptyData = CreateEmptyProductTable();
                SafeBindDataToGrid(dgvByProduct, emptyData, "商品统计");
            }
        }

        private void LoadDateData(DatabaseManager db, DateTime startDate, DateTime endDate, string periodType)
        {
            try
            {
                Console.WriteLine(">>> 加载日期数据...");
                DataTable data = db.GetSummaryByDate(startDate, endDate, periodType);
                if (data == null || data.Rows.Count == 0)
                {
                    data = CreateEmptyDateTable();
                }
                SafeBindDataToGrid(dgvByDate, data, "日期统计");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"日期数据加载失败: {ex.Message}");
                DataTable emptyData = CreateEmptyDateTable();
                SafeBindDataToGrid(dgvByDate, emptyData, "日期统计");
            }
        }

        private void LoadInventoryData(DatabaseManager db)
        {
            try
            {
                Console.WriteLine(">>> 加载库存数据...");
                DataTable data = db.GetInventoryByLocation();
                if (data == null || data.Rows.Count == 0)
                {
                    data = CreateEmptyInventoryTable();
                }
                SafeBindDataToGrid(dgvInventory, data, "库存查询");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"库存数据加载失败: {ex.Message}");
                DataTable emptyData = CreateEmptyInventoryTable();
                SafeBindDataToGrid(dgvInventory, emptyData, "库存查询");
            }
        }

        private void LoadClientData(DatabaseManager db, DateTime startDate, DateTime endDate)
        {
            try
            {
                Console.WriteLine(">>> ===== 开始加载客户统计 ===== ");
                Console.WriteLine($">>> 时间范围: {startDate:yyyy-MM-dd} 至 {endDate:yyyy-MM-dd}");

                if (dgvByClient == null || dgvByClient.IsDisposed)
                {
                    Console.WriteLine(">>> ❌ dgvByClient 未就绪，跳过");
                    return;
                }

                if (InvokeRequired || !IsHandleCreated)
                {
                    RunOnUiThreadWhenReady(() => LoadClientData(db, startDate, endDate));
                    return;
                }

                if (!dgvByClient.IsHandleCreated)
                {
                    Console.WriteLine(">>> ⚠️ dgvByClient 句柄未创建，等待...");
                    EventHandler handler = null;
                    handler = (s, e) =>
                    {
                        dgvByClient.HandleCreated -= handler;
                        RunOnUiThreadWhenReady(() => LoadClientData(db, startDate, endDate));
                    };
                    dgvByClient.HandleCreated += handler;
                    return;
                }

                Console.WriteLine(">>> ✅ 句柄已创建，显示空表提示...");
                SafeBindWarehouseData(CreateEmptyWarehouseTable());
            }
            catch (Exception ex)
            {
                Console.WriteLine($">>> ❌ LoadClientData 外层异常: {ex.Message}");
            }
        }

        private void SafeBindDataToGrid(DataGridView gridView, DataTable data, string gridName)
        {
            if (gridView == null) return;

            try
            {
                gridView.SuspendLayout();
                gridView.DataSource = null;

                if (data == null)
                {
                    Console.WriteLine($"{gridName}: 数据为null");
                    ShowEmptyGridMessage(gridView, $"{gridName}数据为空");
                    return;
                }

                Console.WriteLine($"{gridName}: 收到{data.Rows.Count}行, {data.Columns.Count}列数据");

                if (data.Rows.Count == 0)
                {
                    Console.WriteLine($"{gridName}: 数据表为空");
                    #region agent log
                    if (ReferenceEquals(gridView, dgvByProduct))
                    {
                        AgentDebugLog.Write("C", "StatisticsForm.SafeBindDataToGrid",
                            "empty product data -> ShowEmptyGridMessage",
                            "{\"gridName\":\"" + AgentDebugLog.Escape(gridName) + "\"}");
                    }
                    #endregion
                    ShowEmptyGridMessage(gridView, $"暂无{gridName}数据");
                    return;
                }

                if (gridName != "客户统计")
                {
                    gridView.Columns.Clear();
                    gridView.AutoGenerateColumns = true;
                }

                gridView.DataSource = data;
                gridView.AutoResizeColumns(DataGridViewAutoSizeColumnsMode.DisplayedCells);
                StatisticsUiHelper.ApplyStatisticsGridStyle(gridView);
                StatisticsUiHelper.ApplyStatisticsNumericColumnsFromHeaders(gridView);

                Console.WriteLine($"{gridName}: 绑定完成");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"{gridName}绑定失败: {ex.Message}\n{ex.StackTrace}");
                ShowEmptyGridMessage(gridView, $"{gridName}显示失败: {ex.Message}");
            }
            finally
            {
                gridView.ResumeLayout();
                gridView.Refresh();
            }
        }

        private string GetSelectedPeriod() =>
            string.IsNullOrEmpty(_selectedPeriod) ? "日" : _selectedPeriod;

        private void UpdateColumnHeaders()
        {
            if (dgvByDate.DataSource is DataTable dateTable && dateTable.Columns.Count > 0)
            {
                string periodName = GetSelectedPeriod();
                dgvByDate.Columns[0].HeaderText = periodName + "期";
            }
        }

        private void BtnExport_Click(object sender, EventArgs e)
        {
            try
            {
                if (tabControl.SelectedTab == null)
                {
                    MessageBox.Show("请选择一个标签页进行导出", "提示");
                    return;
                }

                SaveFileDialog saveDialog = new SaveFileDialog();
                saveDialog.Filter = "CSV文件 (*.csv)|*.csv|所有文件 (*.*)|*.*";
                saveDialog.FilterIndex = 1;

                string tabName = tabControl.SelectedTab.Text;
                saveDialog.FileName = $"{tabName}_{DateTime.Now:yyyyMMdd_HHmmss}.csv";

                if (saveDialog.ShowDialog() == DialogResult.OK)
                {
                    DataGridView currentGrid = GetCurrentGridView();
                    if (currentGrid == null || currentGrid.Rows.Count == 0)
                    {
                        MessageBox.Show("当前表格没有数据可导出", "提示");
                        return;
                    }

                    Cursor = Cursors.WaitCursor;

                    string filePath = saveDialog.FileName;

                    if (!Path.GetExtension(filePath).Equals(".csv", StringComparison.OrdinalIgnoreCase))
                    {
                        filePath = Path.ChangeExtension(filePath, ".csv");
                    }

                    ExportToCsv(currentGrid, filePath);

                    Cursor = Cursors.Default;

                    if (File.Exists(filePath))
                    {
                        DialogResult result = MessageBox.Show($"导出成功！\n文件已保存到：{filePath}\n\n是否要打开文件？",
                            "导出完成", MessageBoxButtons.YesNo, MessageBoxIcon.Information);

                        if (result == DialogResult.Yes)
                        {
                            try
                            {
                                System.Diagnostics.Process.Start(filePath);
                            }
                            catch (Exception ex)
                            {
                                MessageBox.Show($"打开文件失败: {ex.Message}\n\n文件位置: {filePath}", "提示");
                            }
                        }
                    }
                    else
                    {
                        MessageBox.Show("导出文件失败，请检查磁盘空间和权限", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }
            }
            catch (Exception ex)
            {
                Cursor = Cursors.Default;
                MessageBox.Show($"导出失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private DataGridView GetCurrentGridView()
        {
            if (tabControl.SelectedTab == null) return null;

            string tabName = tabControl.SelectedTab.Text;

            switch (tabName)
            {
                case "数据总览":
                    return dgvDashboard;
                case "按经手人统计":
                    return dgvByHandler;
                case "按商品型号统计":
                    return dgvByProduct;
                case "按日期统计":
                    return dgvByDate;
                case "库位库存查询":
                    return dgvInventory;
                case "按客户统计":
                    return dgvByClient;
                default:
                    return null;
            }
        }

        private void ExportToCsv(DataGridView gridView, string filePath)
        {
            try
            {
                Console.WriteLine($">>> 开始导出CSV: {filePath}");

                if (gridView.DataSource is DataTable dataTable)
                {
                    Console.WriteLine($">>> 从DataSource获取DataTable: {dataTable.Rows.Count}行");
                    ExportDataTableToCsv(dataTable, filePath);
                    return;
                }

                ExportGridViewToCsv(gridView, filePath);
            }
            catch (Exception ex)
            {
                Console.WriteLine($">>> CSV导出失败: {ex.Message}");
                throw;
            }
        }

        private void ExportDataTableToCsv(DataTable dataTable, string filePath)
        {
            using (StreamWriter writer = new StreamWriter(filePath, false, Encoding.UTF8))
            {
                List<string> headers = new List<string>();
                foreach (DataColumn column in dataTable.Columns)
                {
                    headers.Add(EscapeCsvField(column.ColumnName));
                }
                writer.WriteLine(string.Join(",", headers));

                for (int rowIndex = 0; rowIndex < dataTable.Rows.Count; rowIndex++)
                {
                    DataRow row = dataTable.Rows[rowIndex];
                    List<string> rowData = new List<string>();

                    foreach (DataColumn column in dataTable.Columns)
                    {
                        object cellValue = row[column];
                        string cellText = FormatCellValueForCsv(cellValue, column);
                        rowData.Add(cellText);
                    }

                    writer.WriteLine(string.Join(",", rowData));
                }
            }

            Console.WriteLine($">>> CSV导出成功: {filePath}");
        }

        private void ExportGridViewToCsv(DataGridView gridView, string filePath)
        {
            using (StreamWriter writer = new StreamWriter(filePath, false, Encoding.UTF8))
            {
                List<string> headers = new List<string>();
                for (int i = 0; i < gridView.Columns.Count; i++)
                {
                    if (gridView.Columns[i].Visible)
                    {
                        string columnName = gridView.Columns[i].HeaderText ?? $"列{i + 1}";
                        headers.Add(EscapeCsvField(columnName));
                    }
                }
                writer.WriteLine(string.Join(",", headers));

                for (int rowIndex = 0; rowIndex < gridView.Rows.Count; rowIndex++)
                {
                    DataGridViewRow row = gridView.Rows[rowIndex];
                    if (row.IsNewRow) continue;

                    List<string> rowData = new List<string>();

                    for (int colIndex = 0; colIndex < gridView.Columns.Count; colIndex++)
                    {
                        if (gridView.Columns[colIndex].Visible)
                        {
                            object cellValue = row.Cells[colIndex].Value;
                            string cellText = FormatCellValueForCsv(cellValue, gridView.Columns[colIndex]);
                            rowData.Add(cellText);
                        }
                    }

                    writer.WriteLine(string.Join(",", rowData));
                }
            }

            Console.WriteLine($">>> CSV导出成功: {filePath}");
        }

        private string EscapeCsvField(string field)
        {
            if (string.IsNullOrEmpty(field))
                return "";

            if (field.Contains(",") || field.Contains("\"") || field.Contains("\r") || field.Contains("\n"))
            {
                field = "\"" + field.Replace("\"", "\"\"") + "\"";
            }

            return field;
        }

        private string FormatCellValueForCsv(object value, DataColumn column)
        {
            if (value == null || value == DBNull.Value)
                return "";

            string formattedValue;

            if (value is decimal decimalValue)
            {
                formattedValue = decimalValue.ToString("N2");
            }
            else if (value is double doubleValue)
            {
                formattedValue = doubleValue.ToString("N2");
            }
            else if (value is float floatValue)
            {
                formattedValue = floatValue.ToString("N2");
            }
            else if (value is int || value is long)
            {
                formattedValue = value.ToString();
            }
            else if (value is DateTime dateTimeValue)
            {
                formattedValue = dateTimeValue.ToString("yyyy-MM-dd HH:mm:ss");
            }
            else if (value is bool boolValue)
            {
                formattedValue = boolValue ? "是" : "否";
            }
            else
            {
                formattedValue = value.ToString();
            }

            string columnName = column.ColumnName ?? "";
            if ((columnName.Contains("金额") || columnName.Contains("余额") || columnName.Contains("单价")) &&
                !formattedValue.Contains("."))
            {
                formattedValue += ".00";
            }

            return EscapeCsvField(formattedValue);
        }

        private string FormatCellValueForCsv(object value, DataGridViewColumn column)
        {
            if (value == null || value == DBNull.Value)
                return "";

            string formattedValue;

            if (value is decimal decimalValue)
            {
                formattedValue = decimalValue.ToString("N2");
            }
            else if (value is double doubleValue)
            {
                formattedValue = doubleValue.ToString("N2");
            }
            else if (value is float floatValue)
            {
                formattedValue = floatValue.ToString("N2");
            }
            else if (value is int || value is long)
            {
                formattedValue = value.ToString();
            }
            else if (value is DateTime dateTimeValue)
            {
                formattedValue = dateTimeValue.ToString("yyyy-MM-dd HH:mm:ss");
            }
            else if (value is bool boolValue)
            {
                formattedValue = boolValue ? "是" : "否";
            }
            else
            {
                formattedValue = value.ToString();
            }

            string columnName = column.HeaderText ?? column.Name ?? "";
            if ((columnName.Contains("金额") || columnName.Contains("余额") || columnName.Contains("单价")) &&
                !formattedValue.Contains("."))
            {
                formattedValue += ".00";
            }

            return EscapeCsvField(formattedValue);
        }
    }
}
