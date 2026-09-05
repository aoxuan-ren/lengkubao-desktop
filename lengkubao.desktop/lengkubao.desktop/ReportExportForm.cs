using System;
using System.Collections.Generic;
using System.Data;
using System.Drawing;
using System.Text;
using System.Windows.Forms;

namespace lengkubao.desktop
{
    public partial class ReportExportForm : Form
    {
        private DateTimePicker dtpStart;
        private DateTimePicker dtpEnd;
        private ComboBox cmbReportType;
        private ComboBox cmbClient;
        private List<ClientSearchItem> _reportTypeItems = new List<ClientSearchItem>();
        private List<ClientSearchItem> _clientItems = new List<ClientSearchItem>();
        private Button btnExport;
        private Button btnPreview;
        private RichTextBox txtPreview;

        public ReportExportForm()
        {
            InitializeComponent();
            CreateUI();
            LoadClients();
        }
        private void InitializeComponent()
        {
            // 留空，因为我们在 CreateUI 方法中创建界面
        }

        private void CreateUI()
        {
            this.Text = "📊 报表导出中心";
            this.Size = new Size(800, 600);
            this.StartPosition = FormStartPosition.CenterScreen;

            // 顶部面板
            Panel topPanel = new Panel();
            topPanel.Dock = DockStyle.Top;
            topPanel.Height = 150;
            topPanel.BackColor = Color.FromArgb(240, 240, 240);
            topPanel.Padding = new Padding(20);

            // 报表类型
            Label lblType = new Label();
            lblType.Text = "报表类型:";
            lblType.Location = new Point(20, 20);
            lblType.Size = new Size(80, 25);

            cmbReportType = new ComboBox();
            cmbReportType.Location = new Point(100, 17);
            cmbReportType.Size = new Size(200, 25);
            ClientSearchHelper.ApplySearchableStyle(cmbReportType);
            cmbReportType.SelectedIndexChanged += CmbReportType_SelectedIndexChanged;

            // 日期范围
            Label lblStart = new Label();
            lblStart.Text = "开始日期:";
            lblStart.Location = new Point(320, 20);
            lblStart.Size = new Size(80, 25);

            dtpStart = new DateTimePicker();
            dtpStart.Location = new Point(400, 17);
            dtpStart.Size = new Size(120, 25);

            Label lblEnd = new Label();
            lblEnd.Text = "结束日期:";
            lblEnd.Location = new Point(530, 20);
            lblEnd.Size = new Size(80, 25);

            dtpEnd = new DateTimePicker();
            dtpEnd.Location = new Point(610, 17);
            dtpEnd.Size = new Size(120, 25);
            DateRangeSettings.ApplyTo(dtpStart, dtpEnd);

            // 客户选择（对对账单显示）
            Label             lblClient = new Label();
            lblClient.Text = "选择客户:";
            lblClient.Name = "lblClient";
            lblClient.Location = new Point(20, 60);
            lblClient.Size = new Size(80, 25);
            lblClient.Visible = false;

            cmbClient = new ComboBox();
            cmbClient.Location = new Point(100, 57);
            cmbClient.Size = new Size(200, 25);
            cmbClient.Name = "cmbClient";
            ClientSearchHelper.ApplySearchableStyle(cmbClient);
            cmbClient.Visible = false;

            // 按钮
            btnPreview = new Button();
            btnPreview.Text = "🔍 预览报表";
            btnPreview.Location = new Point(20, 100);
            btnPreview.Size = new Size(120, 35);
            btnPreview.BackColor = Color.FromArgb(52, 152, 219);
            btnPreview.ForeColor = Color.White;
            btnPreview.Click += BtnPreview_Click;

            btnExport = new Button();
            btnExport.Text = "📤 导出Excel";
            btnExport.Location = new Point(150, 100);
            btnExport.Size = new Size(120, 35);
            btnExport.BackColor = Color.FromArgb(46, 204, 113);
            btnExport.ForeColor = Color.White;
            btnExport.Click += BtnExport_Click;

            topPanel.Controls.AddRange(new Control[]
            {
                lblType, cmbReportType,
                lblStart, dtpStart, lblEnd, dtpEnd,
                lblClient, cmbClient,
                btnPreview, btnExport
            });

            // 预览区域
            GroupBox previewGroup = new GroupBox();
            previewGroup.Text = "报表预览";
            previewGroup.Dock = DockStyle.Fill;
            previewGroup.Padding = new Padding(10);

            txtPreview = new RichTextBox();
            txtPreview.Dock = DockStyle.Fill;
            txtPreview.Font = new Font("Consolas", 10);
            txtPreview.ReadOnly = true;

            previewGroup.Controls.Add(txtPreview);

            this.Controls.Add(previewGroup);
            this.Controls.Add(topPanel);

            _reportTypeItems = ClientSearchHelper.BuildSimpleItems(new[]
            {
                "📥 入库报表",
                "📤 销售报表",
                "📈 经手人统计",
                "📦 商品统计",
                "💰 客户对账单"
            });
            ClientSearchHelper.BindSearchableCombo(cmbReportType, _reportTypeItems, new ClientSearchHelper.SearchComboOptions
            {
                PlaceholderText = ClientSearchHelper.NamePlaceholderText
            });
            if (_reportTypeItems.Count > 0)
            {
                cmbReportType.ForeColor = Color.Black;
                cmbReportType.Text = _reportTypeItems[0].DisplayText;
            }
        }

        private void LoadClients()
        {
            try
            {
                DatabaseManager db = new DatabaseManager();
                _clientItems = ClientSearchHelper.BuildItems(db.GetAllClients());
                ClientSearchHelper.BindSearchableCombo(cmbClient, _clientItems);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"加载客户列表失败: {ex.Message}", "错误");
            }
        }

        private void CmbReportType_SelectedIndexChanged(object sender, EventArgs e)
        {
            bool showClient = cmbReportType.Text.Contains("客户对账单");

            // 显示/隐藏客户选择
            foreach (Control ctrl in this.Controls[0].Controls) // topPanel
            {
                if (ctrl.Name == "lblClient" || ctrl.Name == "cmbClient")
                {
                    ctrl.Visible = showClient;
                }
            }
        }

        private void BtnPreview_Click(object sender, EventArgs e)
        {
            try
            {
                DateTime startDate = dtpStart.Value.Date;
                DateTime endDate = dtpEnd.Value.Date;

                if (startDate > endDate)
                {
                    MessageBox.Show("开始日期不能晚于结束日期！", "错误");
                    return;
                }

                DateRangeSettings.SaveDefault(startDate, endDate);

                DatabaseManager db = new DatabaseManager();
                DataTable data = null;
                string reportTitle = "";

                if (!ClientSearchHelper.TryGetSelectedItem(cmbReportType, _reportTypeItems, out ClientSearchItem reportTypeItem))
                {
                    MessageBox.Show("请选择报表类型！", "提示");
                    return;
                }

                string reportTypeName = reportTypeItem.Name;

                if (reportTypeName.Contains("入库"))
                {
                    data = db.GetInboundReport(startDate, endDate);
                    reportTitle = $"入库报表 ({startDate:yyyy-MM-dd} 至 {endDate:yyyy-MM-dd})";
                }
                else if (reportTypeName.Contains("销售报表"))
                {
                    data = db.GetSalesReport(startDate, endDate);
                    reportTitle = $"销售报表 ({startDate:yyyy-MM-dd} 至 {endDate:yyyy-MM-dd})";
                }
                else if (reportTypeName.Contains("经手人"))
                {
                    data = db.GetStatisticsReport("handler", startDate, endDate);
                    reportTitle = $"经手人统计 ({startDate:yyyy-MM-dd} 至 {endDate:yyyy-MM-dd})";
                }
                else if (reportTypeName.Contains("商品"))
                {
                    data = db.GetStatisticsReport("product", startDate, endDate);
                    reportTitle = $"商品统计 ({startDate:yyyy-MM-dd} 至 {endDate:yyyy-MM-dd})";
                }
                else if (reportTypeName.Contains("客户对账单"))
                {
                    if (!ClientSearchHelper.TryGetSelectedClient(cmbClient, _clientItems, out ClientSearchItem clientItem))
                    {
                        MessageBox.Show("请选择客户！", "提示");
                        return;
                    }

                    data = db.GetClientBalanceDetails(clientItem.Code, startDate, endDate);
                    reportTitle = $"客户对账单 - {clientItem.DisplayText} ({startDate:yyyy-MM-dd} 至 {endDate:yyyy-MM-dd})";
                }

                if (data == null || data.Rows.Count == 0)
                {
                    txtPreview.Text = "没有找到符合条件的记录。";
                    return;
                }

                // 生成预览文本
                StringBuilder preview = new StringBuilder();
                preview.AppendLine("=".PadRight(80, '='));
                preview.AppendLine(reportTitle);
                preview.AppendLine($"生成时间: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                preview.AppendLine($"记录总数: {data.Rows.Count} 条");
                preview.AppendLine("=".PadRight(80, '='));
                preview.AppendLine();

                // 表头
                foreach (DataColumn column in data.Columns)
                {
                    preview.Append($"{column.ColumnName,-15}");
                }
                preview.AppendLine();
                preview.AppendLine(new string('-', data.Columns.Count * 15));

                // 数据（只显示前20行预览）
                int maxRows = Math.Min(data.Rows.Count, 20);
                for (int i = 0; i < maxRows; i++)
                {
                    DataRow row = data.Rows[i];
                    foreach (var value in row.ItemArray)
                    {
                        preview.Append($"{value?.ToString()?.Trim(),-15}");
                    }
                    preview.AppendLine();
                }

                if (data.Rows.Count > maxRows)
                {
                    preview.AppendLine($"... 还有 {data.Rows.Count - maxRows} 条记录未显示");
                }

                txtPreview.Text = preview.ToString();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"预览失败: {ex.Message}", "错误");
            }
        }

        private void BtnExport_Click(object sender, EventArgs e)
        {
            try
            {
                DateTime startDate = dtpStart.Value.Date;
                DateTime endDate = dtpEnd.Value.Date;

                if (startDate > endDate)
                {
                    MessageBox.Show("开始日期不能晚于结束日期！", "错误");
                    return;
                }

                DateRangeSettings.SaveDefault(startDate, endDate);

                if (!ClientSearchHelper.TryGetSelectedItem(cmbReportType, _reportTypeItems, out ClientSearchItem reportTypeItem))
                {
                    MessageBox.Show("请选择报表类型！", "提示");
                    return;
                }

                string reportTypeName = reportTypeItem.Name;

                if (reportTypeName.Contains("入库"))
                {
                    ExcelExportHelper.ExportInboundReport(startDate, endDate);
                }
                else if (reportTypeName.Contains("销售报表"))
                {
                    ExcelExportHelper.ExportSalesReport(startDate, endDate);
                }
                else if (reportTypeName.Contains("经手人") || reportTypeName.Contains("商品"))
                {
                    DatabaseManager db = new DatabaseManager();
                    string reportType = reportTypeName.Contains("经手人") ? "handler" : "product";
                    DataTable data = db.GetStatisticsReport(reportType, startDate, endDate);

                    string title = $"{(reportType == "handler" ? "经手人" : "商品")}统计报表";
                    ExcelExportHelper.ExportDataTable(data, title);
                }
                else if (reportTypeName.Contains("客户对账单"))
                {
                    if (!ClientSearchHelper.TryGetSelectedClient(cmbClient, _clientItems, out ClientSearchItem clientItem))
                    {
                        MessageBox.Show("请选择客户！", "提示");
                        return;
                    }

                    ExcelExportHelper.ExportClientBalanceReport(clientItem.Code, startDate, endDate);
                }
                else
                {
                    MessageBox.Show("请选择报表类型！", "提示");
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"导出失败: {ex.Message}", "错误");
            }
        }
    }
}