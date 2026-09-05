using System;
using System.Data;
using System.Drawing;
using System.Windows.Forms;

namespace lengkubao.desktop
{
    public class ClientStatisticsControl : UserControl
    {
        private DataGridView dgvClients;
        private TextBox txtSearch;
        private Button btnSearch;
        private Button btnRefresh;
        private Button btnExport;

        public event EventHandler SearchClicked;
        public event EventHandler RefreshClicked;
        public event EventHandler ExportClicked;

        public string SearchKeyword => txtSearch.Text.Trim();

        public ClientStatisticsControl()
        {
            InitializeComponent();
            InitializeGrid();
        }

        private void InitializeComponent()
        {
            this.SuspendLayout();

            // 搜索面板
            Panel searchPanel = new Panel();
            searchPanel.Dock = DockStyle.Top;
            searchPanel.Height = 40;
            searchPanel.BackColor = Color.FromArgb(240, 240, 240);

            txtSearch = new TextBox();
            txtSearch.Location = new Point(10, 10);
            txtSearch.Size = new Size(200, 25);
            txtSearch.Font = new Font("微软雅黑", 9);
            txtSearch.Text = "输入库位名称搜索...";
            txtSearch.ForeColor = Color.Gray;

            // 然后在构造函数中添加事件处理：
            txtSearch.GotFocus += (s, e) =>
            {
                if (txtSearch.Text == "输入库位名称搜索...")
                {
                    txtSearch.Text = "";
                    txtSearch.ForeColor = Color.Black;
                }
            };

            txtSearch.LostFocus += (s, e) =>
            {
                if (string.IsNullOrWhiteSpace(txtSearch.Text))
                {
                    txtSearch.Text = "输入库位名称搜索...";
                    txtSearch.ForeColor = Color.Gray;
                }
            };

            btnSearch = new Button();
            btnSearch.Text = "搜索";
            btnSearch.Location = new Point(220, 10);
            btnSearch.Size = new Size(80, 25);
            btnSearch.BackColor = Color.FromArgb(0, 122, 204);
            btnSearch.ForeColor = Color.White;
            btnSearch.FlatStyle = FlatStyle.Flat;
            btnSearch.Font = new Font("微软雅黑", 9);
            btnSearch.Click += (s, e) => SearchClicked?.Invoke(this, EventArgs.Empty);

            btnRefresh = new Button();
            btnRefresh.Text = "刷新";
            btnRefresh.Location = new Point(310, 10);
            btnRefresh.Size = new Size(80, 25);
            btnRefresh.BackColor = Color.FromArgb(108, 117, 125);
            btnRefresh.ForeColor = Color.White;
            btnRefresh.FlatStyle = FlatStyle.Flat;
            btnRefresh.Font = new Font("微软雅黑", 9);
            btnRefresh.Click += (s, e) => RefreshClicked?.Invoke(this, EventArgs.Empty);

            btnExport = new Button();
            btnExport.Text = "导出";
            btnExport.Location = new Point(400, 10);
            btnExport.Size = new Size(80, 25);
            btnExport.BackColor = Color.FromArgb(40, 167, 69);
            btnExport.ForeColor = Color.White;
            btnExport.FlatStyle = FlatStyle.Flat;
            btnExport.Font = new Font("微软雅黑", 9);
            btnExport.Click += (s, e) => ExportClicked?.Invoke(this, EventArgs.Empty);

            searchPanel.Controls.Add(txtSearch);
            searchPanel.Controls.Add(btnSearch);
            searchPanel.Controls.Add(btnRefresh);
            searchPanel.Controls.Add(btnExport);

            // 数据表格
            dgvClients = new DataGridView();
            dgvClients.Dock = DockStyle.Fill;
            dgvClients.ReadOnly = true;
            dgvClients.AutoGenerateColumns = false;

            this.Controls.Add(dgvClients);
            this.Controls.Add(searchPanel);

            this.ResumeLayout(false);
        }

        private void InitializeGrid()
        {
            // 清除现有列
            dgvClients.Columns.Clear();

            // 创建列 - 按库位统计入库数据
            // 库位基本信息列
            CreateColumn("库位编号", "序号", 60, StatisticsUiHelper.UniformAlignment);
            CreateColumn("库位名称", "库位", 100, StatisticsUiHelper.UniformAlignment);

            CreateColumn("42型", "42型", 70, StatisticsUiHelper.UniformAlignment, "N0", Color.FromArgb(0, 0, 150));
            CreateColumn("45型", "45型", 70, StatisticsUiHelper.UniformAlignment, "N0", Color.FromArgb(0, 0, 150));
            CreateColumn("60型", "60型", 70, StatisticsUiHelper.UniformAlignment, "N0", Color.FromArgb(0, 0, 150));
            CreateColumn("48型", "48型", 70, StatisticsUiHelper.UniformAlignment, "N0", Color.FromArgb(0, 0, 150));
            CreateColumn("框", "框", 70, StatisticsUiHelper.UniformAlignment, "N0", Color.FromArgb(0, 0, 150));
            CreateColumn("精品", "精品", 70, StatisticsUiHelper.UniformAlignment, "N0", Color.FromArgb(0, 0, 150));
            CreateColumn("次型", "次型", 70, StatisticsUiHelper.UniformAlignment, "N0", Color.FromArgb(0, 0, 150));

            CreateColumn("总计数量", "总计", 80, StatisticsUiHelper.UniformAlignment, "N0", Color.Red, true);
            CreateColumn("总计金额", "金额", 90, StatisticsUiHelper.UniformAlignment, "N2", Color.FromArgb(128, 0, 128), true);
            CreateColumn("入库日期", "最近入库", 100, StatisticsUiHelper.UniformAlignment);

            StatisticsUiHelper.ApplyPremiumGridStyle(dgvClients, Color.FromArgb(15, 118, 110));
            StatisticsUiHelper.ApplyNumericColumnsFromHeaders(dgvClients);

            dgvClients.CellFormatting += DgvClients_CellFormatting;
        }

        private void CreateColumn(string name, string headerText, int width,
                                 DataGridViewContentAlignment alignment,
                                 string format = null, Color? foreColor = null, bool isTotal = false)
        {
            DataGridViewTextBoxColumn column = new DataGridViewTextBoxColumn();
            column.Name = name;
            column.HeaderText = headerText;
            column.Width = width;
            column.ReadOnly = true;
            column.SortMode = DataGridViewColumnSortMode.NotSortable;
            column.DefaultCellStyle.Alignment = StatisticsUiHelper.UniformAlignment;
            column.DefaultCellStyle.Padding = StatisticsUiHelper.UniformCellPadding;
            column.HeaderCell.Style.Alignment = StatisticsUiHelper.UniformAlignment;
            column.HeaderCell.Style.Padding = StatisticsUiHelper.UniformHeaderPadding;

            if (!string.IsNullOrEmpty(format))
                column.DefaultCellStyle.Format = format;

            if (foreColor.HasValue)
                column.DefaultCellStyle.ForeColor = foreColor.Value;

            if (isTotal)
                column.DefaultCellStyle.Font = StatisticsUiHelper.CellBoldFont;
            else
                column.DefaultCellStyle.Font = StatisticsUiHelper.CellFont;

            dgvClients.Columns.Add(column);
        }

        private void DgvClients_CellFormatting(object sender, DataGridViewCellFormattingEventArgs e)
        {
            if (e.RowIndex < 0 || e.ColumnIndex < 0) return;

            DataGridView grid = sender as DataGridView;
            if (grid == null || grid.Columns.Count == 0) return;

            string columnName = grid.Columns[e.ColumnIndex].Name;

            // 为型号列添加背景色
            string[] modelColumns = { "42型", "45型", "60型", "48型", "框", "精品", "次型" };
            if (Array.Exists(modelColumns, col => col == columnName))
            {
                // 如果型号列有数值
                if (e.Value != null && e.Value.ToString() != "0")
                {
                    e.CellStyle.BackColor = Color.FromArgb(224, 242, 254);
                    e.CellStyle.Font = StatisticsUiHelper.CellFont;
                }
            }

            if (columnName == "总计数量")
            {
                if (e.Value != null && e.Value.ToString() != "0")
                {
                    e.CellStyle.BackColor = Color.FromArgb(254, 242, 242);
                    e.CellStyle.Font = StatisticsUiHelper.CellBoldFont;
                }
            }

            if (columnName == "总计金额")
            {
                if (e.Value != null && e.Value.ToString() != "0.00")
                {
                    e.CellStyle.BackColor = Color.FromArgb(250, 245, 255);
                    e.CellStyle.Font = StatisticsUiHelper.CellBoldFont;
                }
            }
        }

        // 绑定数据
        public void BindData(DataTable data)
        {
            if (dgvClients == null) return;

            dgvClients.DataSource = null;
            dgvClients.DataSource = data;
            StatisticsUiHelper.ApplyPremiumGridStyle(dgvClients, Color.FromArgb(15, 118, 110));
            StatisticsUiHelper.ApplyNumericColumnsFromHeaders(dgvClients);
            dgvClients.Refresh();
        }

        // 清空数据
        public void ClearData()
        {
            dgvClients.DataSource = null;
        }
    }
}