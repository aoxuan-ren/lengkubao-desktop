using System;
using System.Data;
using System.Data.SQLite;
using System.Drawing;
using System.Windows.Forms;
using System.Collections.Generic;

namespace lengkubao.desktop
{
    public class CustomerStatementForm : Form
    {
        private ComboBox comboCustomers;
        private DateTimePicker dateFrom;
        private DateTimePicker dateTo;
        private Button btnQuery;
        private Button btnClose;
        private Button btnExport;
        private DataGridView dataGrid;
        private Label label1, label2, label3;
        private Label lblSalesTotal, lblPackagingTotal, lblDeduction, lblAdvance, lblFinalAmount;
        private DatabaseManager dbManager;
        private List<ClientSearchItem> _clientItems = new List<ClientSearchItem>();
        private string selectedClientCode = "";
        private string selectedClientName = "";

        public CustomerStatementForm()
        {
            dbManager = new DatabaseManager();
            BuildForm();

            // 窗体显示后再加载数据
            this.Shown += CustomerStatementForm_Shown;
        }

        private void CustomerStatementForm_Shown(object sender, EventArgs e)
        {
            Console.WriteLine(">>> 窗体已显示，开始加载客户数据...");
            LoadCustomersFromDatabase();

            // 添加手动刷新按钮
            AddRefreshButton();
        }

        // 添加手动刷新按钮
        private void AddRefreshButton()
        {
            Button btnRefresh = new Button();
            btnRefresh.Text = "🔄 刷新";
            btnRefresh.Location = new Point(310, 20);
            btnRefresh.Size = new Size(60, 25);
            btnRefresh.BackColor = Color.FromArgb(108, 117, 125);
            btnRefresh.ForeColor = Color.White;
            btnRefresh.Font = new Font("微软雅黑", 8);
            btnRefresh.Click += (s, e) =>
            {
                Console.WriteLine(">>> 手动刷新客户列表");
                LoadCustomersFromDatabase();
            };

            this.Controls.Add(btnRefresh);
        }

        private void BuildForm()
        {
            // 窗体设置
            this.Text = "客户对账查询";
            this.Size = new Size(1100, 600);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.Font = new Font("微软雅黑", 9);

            // 创建控件
            comboCustomers = new ComboBox();
            dateFrom = new DateTimePicker();
            dateTo = new DateTimePicker();
            btnQuery = new Button();
            btnExport = new Button();
            btnClose = new Button();
            dataGrid = new DataGridView();

            // 创建标签
            label1 = new Label()
            {
                Text = "选择客户:",
                Location = new Point(20, 20),
                Size = new Size(80, 20)
            };

            label2 = new Label()
            {
                Text = "开始日期:",
                Location = new Point(20, 50),
                Size = new Size(80, 20)
            };

            label3 = new Label()
            {
                Text = "结束日期:",
                Location = new Point(20, 80),
                Size = new Size(80, 20)
            };

            // 设置客户下拉框 - 最简单的方式
            comboCustomers.Location = new Point(100, 20);
            comboCustomers.Size = new Size(250, 25);
            comboCustomers.Name = "comboCustomers";
            ClientSearchHelper.ApplySearchableStyle(comboCustomers);

            // 添加状态标签
            Label lblStatus = new Label();
            lblStatus.Name = "lblStatus";
            lblStatus.Text = "正在加载客户...";
            lblStatus.Location = new Point(360, 23);
            lblStatus.Size = new Size(200, 20);
            lblStatus.ForeColor = Color.Blue;

            // 设置日期选择器
            dateFrom.Location = new Point(100, 50);
            dateFrom.Size = new Size(150, 25);
            dateFrom.Format = DateTimePickerFormat.Short;

            dateTo.Location = new Point(100, 80);
            dateTo.Size = new Size(150, 25);
            dateTo.Format = DateTimePickerFormat.Short;
            DateRangeSettings.ApplyTo(dateFrom, dateTo);

            // 设置查询按钮
            btnQuery.Location = new Point(280, 50);
            btnQuery.Size = new Size(80, 45);
            btnQuery.Text = "查询";
            btnQuery.BackColor = Color.FromArgb(0, 122, 204);
            btnQuery.ForeColor = Color.White;
            btnQuery.Click += BtnQuery_Click;
            btnQuery.Enabled = false; // 初始禁用

            // 设置导出按钮
            btnExport.Location = new Point(370, 50);
            btnExport.Size = new Size(80, 45);
            btnExport.Text = "导出";
            btnExport.BackColor = Color.FromArgb(40, 167, 69);
            btnExport.ForeColor = Color.White;
            btnExport.Click += BtnExport_Click;
            btnExport.Enabled = false;

            // 设置关闭按钮
            btnClose.Location = new Point(460, 50);
            btnClose.Size = new Size(80, 45);
            btnClose.Text = "关闭";
            btnClose.BackColor = Color.FromArgb(108, 117, 125);
            btnClose.ForeColor = Color.White;
            btnClose.Click += (s, e) => this.Close();

            // 设置DataGridView
            dataGrid.Location = new Point(20, 120);
            dataGrid.Size = new Size(1050, 300);
            dataGrid.ReadOnly = true;
            dataGrid.RowHeadersVisible = false;
            dataGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            dataGrid.BackgroundColor = Color.White;
            dataGrid.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(248, 248, 248);

            // 配置表格列
            ConfigureDataGridColumns();

            // 创建统计信息标签
            CreateSummaryLabels();

            // 添加所有控件到窗体
            this.Controls.AddRange(new Control[] {
        label1, label2, label3,
        comboCustomers, lblStatus,
        dateFrom, dateTo,
        btnQuery, btnExport, btnClose,
        dataGrid,
        lblSalesTotal, lblPackagingTotal, lblDeduction, lblAdvance, lblFinalAmount
    });
        }

        private void ConfigureDataGridColumns()
        {
            dataGrid.Columns.Clear();

            dataGrid.Columns.Add("Type", "业务类型");
            dataGrid.Columns.Add("OrderNo", "单据号");
            dataGrid.Columns.Add("Date", "日期");
            dataGrid.Columns.Add("Description", "描述");
            dataGrid.Columns.Add("Quantity", "数量");
            dataGrid.Columns.Add("UnitPrice", "单价");
            dataGrid.Columns.Add("Amount", "金额");

            dataGrid.Columns["Type"].Width = 80;
            dataGrid.Columns["OrderNo"].Width = 120;
            dataGrid.Columns["Date"].Width = 100;
            dataGrid.Columns["Description"].Width = 200;
            dataGrid.Columns["Quantity"].Width = 80;
            dataGrid.Columns["UnitPrice"].Width = 100;
            dataGrid.Columns["Amount"].Width = 100;

            dataGrid.Columns["UnitPrice"].DefaultCellStyle.Format = "N2";
            dataGrid.Columns["Amount"].DefaultCellStyle.Format = "N2";
            dataGrid.Columns["Quantity"].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
            dataGrid.Columns["UnitPrice"].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
            dataGrid.Columns["Amount"].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
        }

        private void CreateSummaryLabels()
        {
            int yPos = 440;
            int labelWidth = 200;

            lblSalesTotal = new Label()
            {
                Text = "商品销售总款：0.00元",
                Location = new Point(20, yPos),
                Size = new Size(labelWidth, 20),
                Font = new Font("微软雅黑", 9, FontStyle.Bold),
                ForeColor = Color.Green
            };

            lblPackagingTotal = new Label()
            {
                Text = "包装材料总款：0.00元",
                Location = new Point(240, yPos),
                Size = new Size(labelWidth, 20),
                Font = new Font("微软雅黑", 9, FontStyle.Bold),
                ForeColor = Color.Blue
            };

            lblDeduction = new Label()
            {
                Text = "扣款总额：0.00元",
                Location = new Point(460, yPos),
                Size = new Size(labelWidth, 20),
                Font = new Font("微软雅黑", 9, FontStyle.Bold),
                ForeColor = Color.Red
            };

            lblAdvance = new Label()
            {
                Text = "预支总额：0.00元",
                Location = new Point(680, yPos),
                Size = new Size(labelWidth, 20),
                Font = new Font("微软雅黑", 9, FontStyle.Bold),
                ForeColor = Color.Orange
            };

            yPos += 30;

            lblFinalAmount = new Label()
            {
                Text = "最终应付总款：0.00元",
                Location = new Point(20, yPos),
                Size = new Size(400, 25),
                Font = new Font("微软雅黑", 12, FontStyle.Bold),
                ForeColor = Color.Purple
            };

            // 添加公式说明
            Label lblFormula = new Label()
            {
                Text = "计算公式：销售总款 - 包装总款 - 扣款总额 - 预支总额",
                Location = new Point(450, yPos),
                Size = new Size(400, 25),
                Font = new Font("微软雅黑", 9),
                ForeColor = Color.Gray
            };

            this.Controls.Add(lblFormula);
        }

        private void LoadCustomersFromDatabase()
        {
            try
            {
                // 更新状态标签
                UpdateStatusLabel("正在加载客户数据...", Color.Blue);

                _clientItems = ClientSearchHelper.BuildItems(dbManager.GetAllClients());
                ClientSearchHelper.BindSearchableCombo(comboCustomers, _clientItems);

                if (_clientItems.Count > 0)
                {
                    btnQuery.Enabled = true;
                    btnExport.Enabled = true;
                    UpdateStatusLabel($"✅ 已加载 {_clientItems.Count} 个客户", Color.Green);
                    Console.WriteLine($">>> 成功加载 {_clientItems.Count} 个客户到下拉框");
                }
                else
                {
                    UpdateStatusLabel("⚠️ 没有客户数据", Color.Orange);
                    if (MessageBox.Show("没有找到客户数据，是否添加测试客户？", "提示",
                        MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                    {
                        AddTestClients();
                    }
                }
            }
            catch (Exception ex)
            {
                UpdateStatusLabel($"❌ 加载失败: {ex.Message}", Color.Red);
                Console.WriteLine($">>> 加载客户异常: {ex.ToString()}");
                MessageBox.Show($"加载客户列表失败：{ex.Message}", "错误");
            }
        }

        // 更新状态标签
        private void UpdateStatusLabel(string text, Color color)
        {
            foreach (Control ctrl in this.Controls)
            {
                if (ctrl.Name == "lblStatus")
                {
                    ctrl.Text = text;
                    ctrl.ForeColor = color;
                    break;
                }
            }
        }

        // 添加测试客户
        private void AddTestClients()
        {
            try
            {
                string connStr = $"Data Source={System.IO.Path.Combine(Application.StartupPath, "lengkubao.db")};Version=3;";

                using (var conn = new SQLiteConnection(connStr))
                {
                    conn.Open();

                    // 添加3个测试客户
                    string sql = @"INSERT INTO clients (code, name, phone, address, status) VALUES 
                          ('C001', '张三', '13800138001', '测试地址1', 1),
                          ('C002', '李四', '13800138002', '测试地址2', 1),
                          ('C003', '王五', '13800138003', '测试地址3', 1)";

                    using (var cmd = new SQLiteCommand(sql, conn))
                    {
                        int rows = cmd.ExecuteNonQuery();
                        MessageBox.Show($"已添加 {rows} 个测试客户", "成功", MessageBoxButtons.OK, MessageBoxIcon.Information);

                        // 重新加载
                        LoadCustomersFromDatabase();
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"添加测试客户失败：{ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
        // 延迟重新加载下拉框
        private void ReloadComboBoxWithDelay()
        {
            Timer timer = new Timer();
            timer.Interval = 500;
            timer.Tick += (s, e) =>
            {
                timer.Stop();
                timer.Dispose();
                SimpleLoadCustomers();
            };
            timer.Start();
        }

        // 简单加载客户方法（备用）
        private void SimpleLoadCustomers()
        {
            LoadCustomersFromDatabase();
        }
        private void BtnQuery_Click(object sender, EventArgs e)
        {
            if (!ClientSearchHelper.TryGetSelectedClient(comboCustomers, _clientItems, out ClientSearchItem client))
            {
                MessageBox.Show("请选择客户！", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            selectedClientCode = client.Code;
            selectedClientName = client.Name;

            // 验证日期
            if (dateFrom.Value > dateTo.Value)
            {
                MessageBox.Show("开始日期不能晚于结束日期！", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            DateRangeSettings.SaveDefault(dateFrom.Value.Date, dateTo.Value.Date);
            LoadStatementData();
        }

        private void LoadStatementData()
        {
            try
            {
                Console.WriteLine($">>> 开始查询客户对账: {selectedClientName} ({selectedClientCode})");

                DateTime startDate = dateFrom.Value.Date;
                DateTime endDate = dateTo.Value.Date;

                // 1. 查询客户对账全景数据
                DataTable overview = dbManager.GetClientBalanceOverview(selectedClientCode, startDate, endDate);

                // 2. 查询详细交易记录
                DataTable transactions = dbManager.GetClientTransactionDetails(selectedClientCode, startDate, endDate);

                // 3. 显示数据
                DisplayStatementData(overview, transactions);

                Console.WriteLine(">>> 客户对账查询完成");
            }
            catch (Exception ex)
            {
                Console.WriteLine($">>> 加载对账数据异常: {ex.Message}");
                MessageBox.Show($"加载对账数据失败：{ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void DisplayStatementData(DataTable overview, DataTable transactions)
        {
            try
            {
                // 清空表格
                dataGrid.Rows.Clear();

                // 显示交易明细
                if (transactions.Rows.Count > 0)
                {
                    foreach (DataRow row in transactions.Rows)
                    {
                        string type = row["Type"].ToString();
                        string orderNo = row["OrderNo"].ToString();
                        string date = row["TransactionDate"].ToString();
                        string description = GetTransactionDescription(row, type);
                        string quantity = type == "预支"
                            ? ""
                            : Convert.ToDecimal(row["Quantity"]).ToString("N0");
                        string unitPrice = type == "预支"
                            ? ""
                            : Convert.ToDecimal(row["UnitPrice"]).ToString("N2");
                        string amount = Convert.ToDecimal(row["Amount"]).ToString("N2");

                        dataGrid.Rows.Add(type, orderNo, date, description, quantity, unitPrice, amount);
                    }
                }
                else
                {
                    dataGrid.Rows.Add("无交易记录", "", "", "指定日期范围内没有交易记录", "", "", "");
                }

                // 显示汇总信息
                if (overview.Rows.Count > 0)
                {
                    DataRow row = overview.Rows[0];

                    decimal salesTotal = Convert.ToDecimal(row["SalesTotal"]);
                    decimal packagingTotal = Convert.ToDecimal(row["PackagingTotal"]);
                    decimal deductionTotal = Convert.ToDecimal(row["DeductionTotal"]);
                    decimal advanceTotal = Convert.ToDecimal(row["AdvanceTotal"]);
                    decimal payableTotal = Convert.ToDecimal(row["PayableTotal"]);

                    // 更新显示
                    UpdateSummaryInfo(salesTotal, packagingTotal, deductionTotal, advanceTotal, payableTotal);

                    // 更新窗体标题
                    this.Text = $"客户对账查询 - {selectedClientName} ({selectedClientCode}) - {dateFrom.Value:yyyy-MM-dd} 至 {dateTo.Value:yyyy-MM-dd}";
                }
                else
                {
                    // 没有数据，重置显示
                    UpdateSummaryInfo(0, 0, 0, 0, 0);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($">>> 显示对账数据异常: {ex.Message}");
                MessageBox.Show($"显示对账数据失败：{ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private string GetTransactionDescription(DataRow row, string type)
        {
            switch (type)
            {
                case "销售":
                    return $"{row["ItemType"]} - 数量：{row["Quantity"]}";
                case "包装":
                    return $"{row["ItemType"]} - 数量：{row["Quantity"]}";
                case "扣款":
                    return row["Reason"].ToString();
                case "预支":
                    return row["Reason"].ToString();
                default:
                    return type;
            }
        }

        private void UpdateSummaryInfo(decimal salesTotal, decimal packagingTotal, decimal deduction, decimal advance, decimal finalAmount)
        {
            lblSalesTotal.Text = $"商品销售总款：{salesTotal:N2}元";
            lblPackagingTotal.Text = $"包装材料总款：{packagingTotal:N2}元";
            lblDeduction.Text = $"扣款总额：{deduction:N2}元";
            lblAdvance.Text = $"预支总额：{advance:N2}元";

            // 根据应付总款设置颜色
            if (finalAmount > 0)
            {
                lblFinalAmount.Text = $"客户应付：{finalAmount:N2}元";
                lblFinalAmount.ForeColor = Color.Red;
            }
            else if (finalAmount < 0)
            {
                lblFinalAmount.Text = $"公司应付：{Math.Abs(finalAmount):N2}元";
                lblFinalAmount.ForeColor = Color.Green;
            }
            else
            {
                lblFinalAmount.Text = "账目结清：0.00元";
                lblFinalAmount.ForeColor = Color.Blue;
            }
        }

        private void BtnExport_Click(object sender, EventArgs e)
        {
            try
            {
                if (dataGrid.Rows.Count == 0 || dataGrid.Rows[0].Cells[0].Value?.ToString() == "无交易记录")
                {
                    MessageBox.Show("没有数据可以导出！", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                SaveFileDialog saveDialog = new SaveFileDialog();
                saveDialog.Filter = "Excel文件 (*.xlsx)|*.xlsx|CSV文件 (*.csv)|*.csv";
                saveDialog.FileName = $"{selectedClientName}_对账单_{DateTime.Now:yyyyMMdd_HHmmss}";

                if (saveDialog.ShowDialog() == DialogResult.OK)
                {
                    ExportToExcel(saveDialog.FileName);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"导出失败：{ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void ExportToExcel(string filePath)
        {
            try
            {
                using (var writer = new System.IO.StreamWriter(filePath, false, System.Text.Encoding.UTF8))
                {
                    // 写入标题
                    writer.WriteLine($"客户对账单 - {selectedClientName} ({selectedClientCode})");
                    writer.WriteLine($"日期范围：{dateFrom.Value:yyyy-MM-dd} 至 {dateTo.Value:yyyy-MM-dd}");
                    writer.WriteLine($"生成时间：{DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                    writer.WriteLine();

                    // 写入汇总信息
                    writer.WriteLine("对账汇总：");
                    writer.WriteLine($"商品销售总款：{lblSalesTotal.Text.Replace("商品销售总款：", "")}");
                    writer.WriteLine($"包装材料总款：{lblPackagingTotal.Text.Replace("包装材料总款：", "")}");
                    writer.WriteLine($"扣款总额：{lblDeduction.Text.Replace("扣款总额：", "")}");
                    writer.WriteLine($"预支总额：{lblAdvance.Text.Replace("预支总额：", "")}");
                    writer.WriteLine($"最终应付总款：{lblFinalAmount.Text}");
                    writer.WriteLine();

                    // 写入交易明细标题
                    writer.WriteLine("交易明细：");
                    writer.WriteLine("业务类型,单据号,日期,描述,数量,单价,金额");

                    // 写入交易明细数据
                    foreach (DataGridViewRow row in dataGrid.Rows)
                    {
                        if (row.Cells[0].Value != null)
                        {
                            writer.WriteLine($"\"{row.Cells[0].Value}\",\"{row.Cells[1].Value}\",\"{row.Cells[2].Value}\",\"{row.Cells[3].Value}\",\"{row.Cells[4].Value}\",\"{row.Cells[5].Value}\",\"{row.Cells[6].Value}\"");
                        }
                    }
                }

                MessageBox.Show($"对账单已导出到：\n{filePath}", "导出成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"导出失败：{ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // 添加刷新客户列表的方法
        public void RefreshCustomerList()
        {
            LoadCustomersFromDatabase();
        }
    }
}