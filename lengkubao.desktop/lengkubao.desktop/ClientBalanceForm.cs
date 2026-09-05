using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace lengkubao.desktop
{
    public partial class ClientBalanceForm : Form
    {
        private ComboBox comboClient;
        private DateTimePicker dateFrom;
        private DateTimePicker dateTo;
        private Button btnQuery;
        private Button btnAddDeduction;
        private Button btnAddAdvance;
        private Button btnExport;
        private Button btnClose;
        private Button btnEdit;
        private Button btnDelete;

        private DataGridView dataGridViewDetails;
        /// <summary>最近一次查询的明细（导出合并包装单时使用，不写库）。</summary>
        private DataTable _lastDetailsData;
        private string currentClientCode = "";
        private string currentClientName = "";
        private List<ClientSearchItem> _allClients = new List<ClientSearchItem>();
        // 新增：入库统计控件
        private DataGridView dgvInboundStats;
        private Label lblInboundTitle;
        private Label lblInboundTotal;
        /// <summary>客户对账主滚动区（嵌入时单栏纵向滚动）。</summary>
        private Panel balanceMainScroll;
        /// <summary>底部「客户入库统计」带纵向滚动条的区域。</summary>
        private Panel inboundStatsScrollHost;

        // 新增：结果文本框（带滚动条）
        private RichTextBox txtResult;

        public ClientBalanceForm()
        {
            InitializeForm();
        }

        private void InitializeForm()
        {
            this.Text = "客户对账";
            this.Size = new Size(1200, 720);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.BackColor = Color.White;
            this.Font = new Font("微软雅黑", 9);

            balanceMainScroll = new Panel();
            balanceMainScroll.Dock = DockStyle.Fill;
            balanceMainScroll.AutoScroll = true;
            balanceMainScroll.BackColor = Color.White;
            balanceMainScroll.BorderStyle = BorderStyle.None;
            this.Controls.Add(balanceMainScroll);

            CreateFilterControls(balanceMainScroll);
            CreateResultPanel(balanceMainScroll);
            CreateDataGridViews(balanceMainScroll);
            CreateBottomButtons(balanceMainScroll);
            CreateInboundStatsPanel(balanceMainScroll);

            balanceMainScroll.Resize += (s, e) => BalanceApplyWidths();
            this.Load += (s, e) => BalanceApplyWidths();
        }

        private void BalanceApplyWidths()
        {
            if (balanceMainScroll == null) return;
            int w = balanceMainScroll.ClientSize.Width;
            int inner = Math.Max(280, w - 40);
            if (txtResult != null && !txtResult.IsDisposed)
            {
                txtResult.Width = inner;
            }
            if (dataGridViewDetails != null && !dataGridViewDetails.IsDisposed)
            {
                dataGridViewDetails.Width = inner;
            }
            if (inboundStatsScrollHost != null && !inboundStatsScrollHost.IsDisposed)
            {
                inboundStatsScrollHost.Width = Math.Max(260, w - 20);
            }
            if (dgvInboundStats != null && !dgvInboundStats.IsDisposed && inboundStatsScrollHost != null)
            {
                dgvInboundStats.Width = Math.Max(260, inboundStatsScrollHost.ClientSize.Width - 16);
            }
            if (lblInboundTotal != null && !lblInboundTotal.IsDisposed && inboundStatsScrollHost != null)
            {
                lblInboundTotal.Width = Math.Max(200, inboundStatsScrollHost.ClientSize.Width - 16);
            }
            RefreshBalanceMainScrollExtents();
        }

        // 修改 CreateFilterControls，接受 Panel 参数
        private void CreateFilterControls(Panel parent)
        {
            int yPos = 20;

            // 客户选择
            Label lblClient = new Label();
            lblClient.Text = "选择客户:";
            lblClient.Location = new Point(20, yPos + 3);
            lblClient.Size = new Size(80, 25);
            lblClient.TextAlign = ContentAlignment.MiddleRight;
            parent.Controls.Add(lblClient);

            comboClient = new ComboBox();
            comboClient.Location = new Point(110, yPos);
            comboClient.Size = new Size(200, 25);
            comboClient.Font = new Font("微软雅黑", 9);
            ClientSearchHelper.ApplySearchableStyle(comboClient);
            parent.Controls.Add(comboClient);

            // 开始日期
            Label lblDateFrom = new Label();
            lblDateFrom.Text = "开始日期:";
            lblDateFrom.Location = new Point(330, yPos + 3);
            lblDateFrom.Size = new Size(80, 25);
            lblDateFrom.TextAlign = ContentAlignment.MiddleRight;
            parent.Controls.Add(lblDateFrom);

            dateFrom = new DateTimePicker();
            dateFrom.Location = new Point(420, yPos);
            dateFrom.Size = new Size(120, 25);
            dateFrom.Format = DateTimePickerFormat.Short;
            parent.Controls.Add(dateFrom);

            // 结束日期
            Label lblDateTo = new Label();
            lblDateTo.Text = "结束日期:";
            lblDateTo.Location = new Point(560, yPos + 3);
            lblDateTo.Size = new Size(80, 25);
            lblDateTo.TextAlign = ContentAlignment.MiddleRight;
            parent.Controls.Add(lblDateTo);

            dateTo = new DateTimePicker();
            dateTo.Location = new Point(650, yPos);
            dateTo.Size = new Size(120, 25);
            dateTo.Format = DateTimePickerFormat.Short;
            parent.Controls.Add(dateTo);
            DateRangeSettings.ApplyTo(dateFrom, dateTo);

            // 查询按钮
            btnQuery = new Button();
            btnQuery.Text = "查询";
            btnQuery.Location = new Point(800, yPos);
            btnQuery.Size = new Size(100, 25);
            btnQuery.BackColor = Color.FromArgb(0, 122, 204);
            btnQuery.ForeColor = Color.White;
            btnQuery.Click += BtnQuery_Click;
            parent.Controls.Add(btnQuery);

            // 加载客户数据
            LoadClients();
        }

        // 修改 CreateResultPanel，接受 Panel 参数
        private void CreateResultPanel(Panel parent)
        {
            int yPos = 60;

            // 结果标题
            Label lblResultTitle = new Label();
            lblResultTitle.Text = "对账结果:";
            lblResultTitle.Location = new Point(20, yPos);
            lblResultTitle.Size = new Size(150, 25);
            lblResultTitle.Font = new Font("微软雅黑", 10, FontStyle.Bold);
            parent.Controls.Add(lblResultTitle);

            yPos += 25;

            // 使用RichTextBox替代Label，支持滚动
            txtResult = new RichTextBox();
            txtResult.Location = new Point(20, yPos);
            txtResult.Size = new Size(Math.Max(300, parent.ClientSize.Width - 40), 120);
            txtResult.Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right;
            txtResult.Font = new Font("微软雅黑", 10);
            txtResult.BackColor = Color.WhiteSmoke;
            txtResult.BorderStyle = BorderStyle.FixedSingle;
            txtResult.ReadOnly = true;
            txtResult.Multiline = true;
            txtResult.ScrollBars = RichTextBoxScrollBars.Vertical;
            txtResult.Text = "请选择客户和日期范围，然后点击【查询】按钮";
            parent.Controls.Add(txtResult);
        }

        // 修改 CreateDataGridViews，接受 Panel 参数
        private void CreateDataGridViews(Panel parent)
        {
            int yPos = 208;

            // 详细交易记录表格
            Label lblDetails = new Label();
            lblDetails.Text = "详细交易记录:";
            lblDetails.Location = new Point(20, yPos);
            lblDetails.Size = new Size(150, 25);
            lblDetails.Font = new Font("微软雅黑", 10, FontStyle.Bold);
            parent.Controls.Add(lblDetails);

            const int detailGridHeight = 240;
            dataGridViewDetails = new DataGridView();
            dataGridViewDetails.Location = new Point(20, yPos + 25);
            dataGridViewDetails.Size = new Size(Math.Max(400, parent.ClientSize.Width - 40), detailGridHeight);
            dataGridViewDetails.Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right;
            ClientBalanceGridHelper.SetupDetailsGrid(dataGridViewDetails);
            parent.Controls.Add(dataGridViewDetails);
        }

        private void UpdateDetailsGridView(DataTable detailsData)
        {
            ClientBalanceGridHelper.BindDetailsGrid(dataGridViewDetails, detailsData);
        }

        // 修改 CreateBottomButtons，接受 Panel 参数
        private void CreateBottomButtons(Panel parent)
        {
            // 与 CreateDataGridViews 中 yPos=208、表头25、明细高 240 对齐
            int yPos = 208 + 25 + 240 + 12;

            // 编辑按钮
            btnEdit = new Button();
            btnEdit.Text = "编辑";
            btnEdit.Location = new Point(20, yPos);
            btnEdit.Size = new Size(100, 30);
            btnEdit.BackColor = Color.FromArgb(52, 152, 219);
            btnEdit.ForeColor = Color.White;
            btnEdit.Click += BtnEdit_Click;
            parent.Controls.Add(btnEdit);

            // 删除按钮
            btnDelete = new Button();
            btnDelete.Text = "删除";
            btnDelete.Location = new Point(130, yPos);
            btnDelete.Size = new Size(100, 30);
            btnDelete.BackColor = Color.FromArgb(231, 76, 60);
            btnDelete.ForeColor = Color.White;
            btnDelete.Click += BtnDelete_Click;
            parent.Controls.Add(btnDelete);

            // 添加扣款按钮
            btnAddDeduction = new Button();
            btnAddDeduction.Text = "添加扣款";
            btnAddDeduction.Location = new Point(350, yPos);
            btnAddDeduction.Size = new Size(100, 30);
            btnAddDeduction.BackColor = Color.FromArgb(192, 57, 43);
            btnAddDeduction.ForeColor = Color.White;
            btnAddDeduction.Click += BtnAddDeduction_Click;
            parent.Controls.Add(btnAddDeduction);

            // 添加预支按钮
            btnAddAdvance = new Button();
            btnAddAdvance.Text = "添加预支";
            btnAddAdvance.Location = new Point(460, yPos);
            btnAddAdvance.Size = new Size(100, 30);
            btnAddAdvance.BackColor = Color.FromArgb(155, 89, 182);
            btnAddAdvance.ForeColor = Color.White;
            btnAddAdvance.Click += BtnAddAdvance_Click;
            parent.Controls.Add(btnAddAdvance);

            // 打印按钮
            Button btnPrint = new Button();
            btnPrint.Text = "打印";
            btnPrint.Location = new Point(560, yPos);
            btnPrint.Size = new Size(100, 30);
            btnPrint.BackColor = Color.FromArgb(52, 152, 219);
            btnPrint.ForeColor = Color.White;
            btnPrint.Click += BtnPrint_Click;
            parent.Controls.Add(btnPrint);

            // 导出按钮
            btnExport = new Button();
            btnExport.Text = "导出报表";
            btnExport.Location = new Point(670, yPos);
            btnExport.Size = new Size(100, 30);
            btnExport.BackColor = Color.FromArgb(46, 204, 113);
            btnExport.ForeColor = Color.White;
            btnExport.Click += BtnExport_Click;
            parent.Controls.Add(btnExport);

            // 关闭按钮
            btnClose = new Button();
            btnClose.Text = "关闭";
            btnClose.Location = new Point(780, yPos);
            btnClose.Size = new Size(100, 30);
            btnClose.BackColor = Color.FromArgb(52, 152, 219);
            btnClose.ForeColor = Color.White;
            btnClose.Click += (s, e) => this.Close();
            parent.Controls.Add(btnClose);
        }

        private void LoadClients()
        {
            try
            {
                DatabaseManager db = new DatabaseManager();
                DataTable clients = db.GetAllClients();
                _allClients = ClientSearchHelper.BuildItems(clients);

                ClientSearchHelper.BindSearchableCombo(comboClient, _allClients, new ClientSearchHelper.SearchComboOptions
                {
                    OnSelected = item =>
                    {
                        if (item != null)
                        {
                            currentClientCode = item.Code;
                            currentClientName = item.Name;
                        }
                    }
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"加载客户列表失败: {ex.Message}", "错误");
            }
        }

        private bool TryResolveClient(out ClientSearchItem client, bool showMessage = true)
        {
            client = null;
            if (ClientSearchHelper.TryGetSelectedClient(comboClient, _allClients, out client))
            {
                currentClientCode = client.Code;
                currentClientName = client.Name;
                return true;
            }

            if (showMessage)
                MessageBox.Show("请选择或搜索并选中有效客户", "提示");

            return false;
        }

        // 修改 BtnQuery_Click，添加入库统计查询
        private void BtnQuery_Click(object sender, EventArgs e)
        {
            try
            {
                if (!TryResolveClient(out ClientSearchItem client))
                    return;

                if (dateFrom.Value > dateTo.Value)
                {
                    MessageBox.Show("开始日期不能晚于结束日期！", "提示");
                    return;
                }

                DateRangeSettings.SaveDefault(dateFrom.Value.Date, dateTo.Value.Date);

                string clientName = client.Name;

                Console.WriteLine($"=== 开始查询对账 ===");
                Console.WriteLine($"客户代码: {currentClientCode}");
                Console.WriteLine($"客户名称: {clientName}");
                Console.WriteLine($"日期范围: {dateFrom.Value:yyyy-MM-dd} 到 {dateTo.Value:yyyy-MM-dd}");

                // 查询数据
                DatabaseManager db = new DatabaseManager();

                // 1. 先直接查询数据库，看看有多少数据
                try
                {
                    DataTable debugData = db.DebugAllClientData(currentClientCode, dateFrom.Value, dateTo.Value);
                    Console.WriteLine($"=== 直接查询结果: {debugData.Rows.Count} 条记录 ===");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"调试查询失败: {ex.Message}");
                }

                // 2. 调用对账方法
                DataTable detailsData = db.GetClientTransactionDetails(currentClientCode, dateFrom.Value, dateTo.Value);
                Console.WriteLine($"=== GetClientTransactionDetails 结果: {detailsData.Rows.Count} 条记录 ===");

                decimal openingOnly = db.GetClientOpeningBalanceForQuery(currentClientCode, dateFrom.Value);
                if (detailsData.Rows.Count == 0 && openingOnly == 0)
                {
                    txtResult.Text = $"【{clientName}】\n\n当前时间段内没有找到交易记录";
                    dataGridViewDetails.Rows.Clear();
                    _lastDetailsData = null;
                    LoadInboundStatistics();
                    return;
                }

                _lastDetailsData = detailsData.Copy();

                // 重新计算并显示
                RecalculateFromDetails(detailsData, clientName);

                // 更新界面
                UpdateDetailsGridView(detailsData);

                // 加载入库统计
                LoadInboundStatistics();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"查询失败: {ex.Message}", "错误");
                Console.WriteLine($"查询异常: {ex.Message}\n{ex.StackTrace}");
            }
        }
        // 新增：加载入库统计
        // 修改加载入库统计的方法 - 改为按库位分组查询
        private void LoadInboundStatistics()
        {
            try
            {
                if (string.IsNullOrEmpty(currentClientCode))
                {
                    return;
                }

                DatabaseManager db = new DatabaseManager();

                // 获取按库位和型号分组的入库统计（使用新方法）
                DataTable inboundStats = db.GetClientInboundStatisticsByLocation(currentClientCode, dateFrom.Value, dateTo.Value);
                DataTable inboundTotal = db.GetClientInboundTotal(currentClientCode, dateFrom.Value, dateTo.Value);
                lblInboundTotal.Text = ClientBalanceGridHelper.BindInboundGrid(dgvInboundStats, inboundStats, inboundTotal);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"加载入库统计失败: {ex.Message}");
                lblInboundTotal.Text = "总入库: 0 件 | 型号数: 0 | 总单数: 0";
                dgvInboundStats.Rows.Clear();
            }
        }

        private static int ReadIntColumn(DataRow row, string columnName)
        {
            if (row == null || !row.Table.Columns.Contains(columnName))
                return 0;

            object value = row[columnName];
            if (value == null || value == DBNull.Value)
                return 0;

            if (value is int intValue)
                return intValue;

            if (int.TryParse(value.ToString(), out int parsed))
                return parsed;

            if (decimal.TryParse(value.ToString(), out decimal decimalValue))
                return (int)decimalValue;

            return 0;
        }

        /// <summary>底部「客户入库统计」：置于对账主内容下方，独立滚动区域。</summary>
        private void CreateInboundStatsPanel(Panel parent)
        {
            const int detailTop = 208;
            const int detailGridH = 240;
            const int buttonsTop = detailTop + 25 + detailGridH + 12;
            const int buttonRowH = 30;
            int top = buttonsTop + buttonRowH + 16;

            inboundStatsScrollHost = new Panel();
            inboundStatsScrollHost.Location = new Point(10, top);
            const int inboundStatsHostHeight = 400;
            inboundStatsScrollHost.Size = new Size(Math.Max(260, parent.ClientSize.Width - 20), inboundStatsHostHeight);
            inboundStatsScrollHost.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top;
            inboundStatsScrollHost.AutoScroll = true;
            inboundStatsScrollHost.BackColor = Color.FromArgb(240, 248, 255);
            inboundStatsScrollHost.BorderStyle = BorderStyle.FixedSingle;
            parent.Controls.Add(inboundStatsScrollHost);

            lblInboundTitle = new Label();
            lblInboundTitle.Text = "客户入库统计";
            lblInboundTitle.Location = new Point(8, 8);
            lblInboundTitle.Size = new Size(320, 26);
            lblInboundTitle.Font = new Font("微软雅黑", 11, FontStyle.Bold);
            lblInboundTitle.ForeColor = Color.FromArgb(0, 102, 204);
            inboundStatsScrollHost.Controls.Add(lblInboundTitle);

            lblInboundTotal = new Label();
            lblInboundTotal.Text = "总入库: 0 件 | 型号数: 0 | 总单数: 0";
            lblInboundTotal.Location = new Point(8, 36);
            lblInboundTotal.Size = new Size(inboundStatsScrollHost.ClientSize.Width - 16, 22);
            lblInboundTotal.Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right;
            lblInboundTotal.Font = new Font("微软雅黑", 9, FontStyle.Bold);
            lblInboundTotal.ForeColor = Color.FromArgb(0, 102, 102);
            inboundStatsScrollHost.Controls.Add(lblInboundTotal);

            dgvInboundStats = new DataGridView();
            dgvInboundStats.Location = new Point(8, 62);
            dgvInboundStats.Size = new Size(Math.Max(260, inboundStatsScrollHost.ClientSize.Width - 16), 420);
            dgvInboundStats.Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right;
            ClientBalanceGridHelper.SetupInboundGrid(dgvInboundStats);
            inboundStatsScrollHost.Controls.Add(dgvInboundStats);

            Label lblNote = new Label();
            lblNote.Text = "※ 按库位分组显示入库数量统计（区域内可滚动查看）";
            lblNote.Location = new Point(8, 62 + 420 + 6);
            lblNote.Size = new Size(560, 20);
            lblNote.Font = new Font("微软雅黑", 8, FontStyle.Italic);
            lblNote.ForeColor = Color.Gray;
            inboundStatsScrollHost.Controls.Add(lblNote);

            inboundStatsScrollHost.AutoScrollMinSize = new Size(
                inboundStatsScrollHost.ClientSize.Width,
                lblNote.Bottom + 12);

            RefreshBalanceMainScrollExtents();
        }

        private void RefreshBalanceMainScrollExtents()
        {
            if (balanceMainScroll == null || inboundStatsScrollHost == null) return;
            int w = Math.Max(360, balanceMainScroll.ClientSize.Width);
            int h = inboundStatsScrollHost.Bottom + 32;
            balanceMainScroll.AutoScrollMinSize = new Size(w, h);
        }
        private void RecalculateFromDetails(DataTable detailsData, string clientName)
        {
            ClientBalanceSummaryResult summary = ClientBalanceSummaryHelper.Calculate(detailsData);
            ClientBalanceSummaryHelper.ApplyToRichTextBox(txtResult, summary);
        }
        // ========== 编辑功能 ==========
        private void BtnEdit_Click(object sender, EventArgs e)
        {
            if (dataGridViewDetails.SelectedRows.Count == 0)
            {
                MessageBox.Show("请先选择要编辑的行", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            DataGridViewRow selectedRow = dataGridViewDetails.SelectedRows[0];
            string type = selectedRow.Cells["Type"].Value?.ToString() ?? "";

            // 只能编辑扣款和预支
            if (type != "扣款" && type != "预支")
            {
                MessageBox.Show("只能编辑扣款和预支记录", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            string clientCode = currentClientCode;
            string clientName = currentClientName;

            // 获取原始数据
            DateTime date = Convert.ToDateTime(selectedRow.Cells["TransactionDate"].Value);
            decimal amount = Math.Abs(Convert.ToDecimal(selectedRow.Cells["Amount"].Value));
            string handler = selectedRow.Cells["Handler"].Value?.ToString() ?? "";
            string reason = selectedRow.Cells["Reason"].Value?.ToString() ?? "";

            int quantity = 0;
            decimal unitPrice = 0m;
            if (type == "扣款")
            {
                string qtyText = selectedRow.Cells["Quantity"].Value?.ToString()?.Replace(",", "") ?? "";
                string priceText = selectedRow.Cells["UnitPrice"].Value?.ToString() ?? "";
                int.TryParse(qtyText, out quantity);
                decimal.TryParse(priceText, out unitPrice);
            }

            using (var editForm = new DeductionAdvanceForm(type, clientCode, clientName))
            {
                editForm.SetEditMode(date, amount, handler, reason, quantity, unitPrice);

                if (editForm.ShowDialog() == DialogResult.OK)
                {
                    // 重新查询
                    BtnQuery_Click(null, null);
                }
            }
        }

        // ========== 删除功能 ==========
        private void BtnDelete_Click(object sender, EventArgs e)
        {
            if (dataGridViewDetails.SelectedRows.Count == 0)
            {
                MessageBox.Show("请先选择要删除的行", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            DataGridViewRow selectedRow = dataGridViewDetails.SelectedRows[0];
            string type = selectedRow.Cells["Type"].Value?.ToString() ?? "";

            // 只能删除扣款和预支
            if (type != "扣款" && type != "预支")
            {
                MessageBox.Show("只能删除扣款和预支记录", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            DialogResult result = MessageBox.Show($"确定要删除这条{type}记录吗？", "确认删除",
                MessageBoxButtons.OKCancel, MessageBoxIcon.Warning);

            if (result == DialogResult.OK)
            {
                try
                {
                    string tableName = (type == "扣款") ? "deductions" : "advances";
                    string dateField = (type == "扣款") ? "deduct_date" : "advance_date";

                    // 获取要删除的记录信息
                    DateTime date = Convert.ToDateTime(selectedRow.Cells["TransactionDate"].Value);
                    decimal amount = Math.Abs(Convert.ToDecimal(selectedRow.Cells["Amount"].Value));
                    string handler = selectedRow.Cells["Handler"].Value?.ToString() ?? "";
                    string reason = selectedRow.Cells["Reason"].Value?.ToString() ?? "";

                    DatabaseManager db = new DatabaseManager();
                    int rowsAffected;

                    if (type == "扣款")
                    {
                        rowsAffected = db.DeleteDeductionWithLedgerByBusinessKey(
                            currentClientCode, date, amount, handler, reason)
                            ? 1 : 0;
                    }
                    else
                    {
                        string sql = $@"
                DELETE FROM {tableName} 
                WHERE client_code = @clientCode 
                  AND {dateField} = @date 
                  AND amount = @amount 
                  AND handler = @handler 
                  AND reason = @reason";

                        var parameters = new Dictionary<string, object>
                        {
                            { "@clientCode", currentClientCode },
                            { "@date", date.ToString("yyyy-MM-dd") },
                            { "@amount", amount },
                            { "@handler", handler },
                            { "@reason", reason }
                        };

                        rowsAffected = db.ExecuteNonQuery(sql, parameters);
                    }

                    if (rowsAffected > 0)
                    {
                        MessageBox.Show($"{type}记录删除成功", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        // 重新查询
                        BtnQuery_Click(null, null);
                    }
                    else
                    {
                        MessageBox.Show("删除失败，记录可能不存在", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"删除失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }
        private void BtnAddDeduction_Click(object sender, EventArgs e)
        {
            if (!TryResolveClient(out ClientSearchItem client))
                return;

            string clientCode = client.Code;
            string clientName = client.Name;

            using (var deductionForm = new DeductionAdvanceForm("扣款", clientCode, clientName))
            {
                if (deductionForm.ShowDialog() == DialogResult.OK)
                {
                    // 扣款添加成功，重新查询
                    BtnQuery_Click(null, null);
                }
            }
        }

        private void BtnAddAdvance_Click(object sender, EventArgs e)
        {
            if (!TryResolveClient(out ClientSearchItem client))
                return;

            string clientCode = client.Code;
            string clientName = client.Name;

            using (var advanceForm = new DeductionAdvanceForm("预支", clientCode, clientName))
            {
                if (advanceForm.ShowDialog() == DialogResult.OK)
                {
                    // 预支添加成功，重新查询
                    BtnQuery_Click(null, null);
                }
            }
        }
        private bool TryBuildClientBalanceReportData(out ClientBalanceReportData reportData, out string exportClientName)
        {
            reportData = null;
            exportClientName = null;

            if (dataGridViewDetails.Rows.Count == 0)
            {
                MessageBox.Show("没有数据可以导出！", "提示");
                return false;
            }

            if (!TryResolveClient(out ClientSearchItem exportClient, false) && string.IsNullOrEmpty(currentClientName))
            {
                MessageBox.Show("请选择或搜索并选中有效客户", "提示");
                return false;
            }

            exportClientName = exportClient?.Name ?? currentClientName;
            if (string.IsNullOrEmpty(exportClientName))
                exportClientName = "客户";

            decimal salesTotal = 0;
            decimal packagingTotal = 0;
            decimal deductionTotal = 0;
            decimal advanceTotal = 0;
            decimal payableTotal = 0;
            CalculateFromCurrentData(ref salesTotal, ref packagingTotal, ref deductionTotal, ref advanceTotal, ref payableTotal);

            reportData = new ClientBalanceReportData
            {
                ClientName = exportClientName,
                StartDate = dateFrom.Value,
                EndDate = dateTo.Value,
                SalesTotal = salesTotal,
                PackagingTotal = packagingTotal,
                DeductionTotal = deductionTotal,
                AdvanceTotal = advanceTotal,
                PayableTotal = payableTotal,
                DetailsData = BuildDetailsDataTableForExport(),
                InboundTotalText = string.IsNullOrWhiteSpace(lblInboundTotal?.Text)
                    ? "总入库: 0 件 | 型号数: 0 | 总单数: 0"
                    : lblInboundTotal.Text,
                InboundStats = BuildInboundStatsDataTableForExport()
            };
            return true;
        }

        private void BtnPrint_Click(object sender, EventArgs e)
        {
            if (dataGridViewDetails.Rows.Count == 0)
            {
                MessageBox.Show("没有数据可以导出！", "提示");
                return;
            }

            if (!TryHandleStorageFeePromptBeforeReport())
                return;

            if (!TryHandleZeroPackagingPriceBeforeReport())
                return;

            if (!TryBuildClientBalanceReportData(out ClientBalanceReportData reportData, out _))
                return;

            try
            {
                Cursor.Current = Cursors.WaitCursor;
                ExcelExportHelper.PrintClientBalanceWithDialog(reportData);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"打印失败：{ex.Message}", "错误");
            }
            finally
            {
                Cursor.Current = Cursors.Default;
            }
        }

        private void BtnExport_Click(object sender, EventArgs e)
        {
            if (dataGridViewDetails.Rows.Count == 0)
            {
                MessageBox.Show("没有数据可以导出！", "提示");
                return;
            }

            if (!TryHandleStorageFeePromptBeforeReport())
                return;

            if (!TryHandleZeroPackagingPriceBeforeReport())
                return;

            if (!TryBuildClientBalanceReportData(out ClientBalanceReportData reportData, out string exportClientName))
                return;

            SaveFileDialog saveDialog = new SaveFileDialog();
            saveDialog.Filter = "PDF文件 (*.pdf)|*.pdf";
            saveDialog.FileName = $"{exportClientName}_对账单_{DateTime.Now:yyyyMMdd}.pdf";

            if (saveDialog.ShowDialog() != DialogResult.OK)
                return;

            try
            {
                bool ok = ExcelExportHelper.ExportClientBalancePdfReport(
                    saveDialog.FileName,
                    reportData.ClientName,
                    reportData.StartDate,
                    reportData.EndDate,
                    reportData.SalesTotal,
                    reportData.PackagingTotal,
                    reportData.DeductionTotal,
                    reportData.AdvanceTotal,
                    reportData.PayableTotal,
                    reportData.DetailsData,
                    reportData.InboundTotalText,
                    reportData.InboundStats);

                if (ok)
                {
                    MessageBox.Show($"PDF对账单已保存\n{saveDialog.FileName}", "导出成功");
                    Process.Start(saveDialog.FileName);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"导出失败：{ex.Message}", "错误");
            }
        }

        private bool TryHandleStorageFeePromptBeforeReport()
        {
            string clientCodeForCheck = currentClientCode;
            if (string.IsNullOrEmpty(clientCodeForCheck)
                && TryResolveClient(out ClientSearchItem resolvedClient, false))
            {
                clientCodeForCheck = resolvedClient.Code;
            }

            if (!string.IsNullOrEmpty(clientCodeForCheck))
            {
                var checkDb = new DatabaseManager();
                if (checkDb.HasRefrigerationFeeDeductionInRange(
                    clientCodeForCheck, dateFrom.Value.Date, dateTo.Value.Date))
                {
                    return true;
                }
            }

            int totalQuantity = GetCurrentInboundTotalQuantity();

            using (var prompt = new StorageFeeDeductionPromptForm(totalQuantity))
            {
                if (prompt.ShowDialog(this) != DialogResult.OK)
                    return false;

                if (prompt.UserAction == StorageFeeDeductionPromptForm.PromptAction.Ignored)
                    return true;

                if (prompt.UserAction != StorageFeeDeductionPromptForm.PromptAction.Confirmed)
                    return false;

                if (!TryResolveClient(out ClientSearchItem client, false) && string.IsNullOrEmpty(currentClientCode))
                {
                    MessageBox.Show("请选择或搜索并选中有效客户", "提示");
                    return false;
                }

                string clientCode = client?.Code ?? currentClientCode;
                string clientName = client?.Name ?? currentClientName;
                if (string.IsNullOrEmpty(clientName))
                    clientName = "客户";

                decimal amount = totalQuantity * prompt.UnitPrice;
                var db = new DatabaseManager();
                string handler = ResolveDefaultHandler(db);
                bool added = db.AddDeduction(
                    clientCode,
                    clientName,
                    amount,
                    dateTo.Value.Date,
                    DatabaseManager.RefrigerationFeeReason,
                    handler,
                    Environment.UserName,
                    totalQuantity,
                    prompt.UnitPrice);

                if (!added)
                {
                    MessageBox.Show("制冷费扣款添加失败，请重试。", "错误");
                    return false;
                }

                BtnQuery_Click(null, null);
                return true;
            }
        }

        /// <summary>
        /// 导出/打印前：若有单价为 0 的未结清包装类型，提示补价或忽略。
        /// 补价后按包装记账规则重算金额并刷新对账明细。
        /// </summary>
        private bool TryHandleZeroPackagingPriceBeforeReport()
        {
            var zeroPriceItems = CollectZeroPricePackagingTypes();
            if (zeroPriceItems.Count == 0)
                return true;

            using (var prompt = new PackagingZeroPricePromptForm(zeroPriceItems))
            {
                if (prompt.ShowDialog(this) != DialogResult.OK)
                    return false;

                if (prompt.UserAction == PackagingZeroPricePromptForm.PromptAction.Ignored)
                    return true;

                if (prompt.UserAction != PackagingZeroPricePromptForm.PromptAction.Confirmed)
                    return false;

                string clientCode = currentClientCode;
                if (string.IsNullOrEmpty(clientCode)
                    && TryResolveClient(out ClientSearchItem resolvedClient, false))
                {
                    clientCode = resolvedClient.Code;
                }

                if (string.IsNullOrEmpty(clientCode))
                {
                    MessageBox.Show("请选择或搜索并选中有效客户", "提示");
                    return false;
                }

                try
                {
                    var db = new DatabaseManager();
                    int totalUpdated = 0;
                    foreach (var item in prompt.ConfirmedPrices)
                    {
                        totalUpdated += db.UpdateClientZeroPackTypeUnitPrice(
                            clientCode,
                            item.PackType,
                            item.UnitPrice,
                            dateFrom.Value.Date,
                            dateTo.Value.Date);
                    }

                    if (totalUpdated <= 0)
                    {
                        MessageBox.Show("未更新到单价为 0 的包装记录，请刷新后重试。", "提示");
                        return false;
                    }

                    BtnQuery_Click(null, null);
                    return true;
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"添加包装单价失败：{ex.Message}", "错误");
                    return false;
                }
            }
        }

        private List<PackagingZeroPricePromptForm.PackTypePriceInput> CollectZeroPricePackagingTypes()
        {
            var map = new Dictionary<string, PackagingZeroPricePromptForm.PackTypePriceInput>(
                StringComparer.OrdinalIgnoreCase);

            DataTable source = _lastDetailsData;
            if (source == null || source.Rows.Count == 0)
                return new List<PackagingZeroPricePromptForm.PackTypePriceInput>();

            foreach (DataRow row in source.Rows)
            {
                if (IsSettledTransaction(row)) continue;
                if (!IsPackagingTransaction(row)) continue;

                decimal unitPrice = row["UnitPrice"] != DBNull.Value ? Convert.ToDecimal(row["UnitPrice"]) : 0m;
                if (unitPrice != 0m) continue;

                string packType = row["ItemType"]?.ToString()?.Trim() ?? "";
                if (string.IsNullOrEmpty(packType)) continue;

                decimal quantity = row["Quantity"] != DBNull.Value ? Convert.ToDecimal(row["Quantity"]) : 0m;
                if (!map.TryGetValue(packType, out var item))
                {
                    item = new PackagingZeroPricePromptForm.PackTypePriceInput
                    {
                        PackType = packType,
                        TotalQuantity = 0m
                    };
                    map[packType] = item;
                }

                item.TotalQuantity += quantity;
            }

            return new List<PackagingZeroPricePromptForm.PackTypePriceInput>(map.Values);
        }

        private int GetCurrentInboundTotalQuantity()
        {
            if (!string.IsNullOrEmpty(currentClientCode))
            {
                int fromDb = TryGetInboundTotalQuantityFromDb(currentClientCode);
                if (fromDb >= 0)
                    return fromDb;
            }

            if (!string.IsNullOrWhiteSpace(lblInboundTotal?.Text)
                && TryParseInboundTotalQuantity(lblInboundTotal.Text, out int parsed))
            {
                return parsed;
            }

            return 0;
        }

        private int TryGetInboundTotalQuantityFromDb(string clientCode)
        {
            try
            {
                var db = new DatabaseManager();
                DataTable inboundTotal = db.GetClientInboundTotal(clientCode, dateFrom.Value, dateTo.Value);
                if (inboundTotal != null && inboundTotal.Rows.Count > 0 && inboundTotal.Columns.Contains("总数量"))
                {
                    object value = inboundTotal.Rows[0]["总数量"];
                    if (value != null && int.TryParse(value.ToString(), out int qty))
                        return qty;
                }

                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"读取入库总件数失败: {ex.Message}");
                return -1;
            }
        }

        private static bool TryParseInboundTotalQuantity(string summaryText, out int quantity)
        {
            quantity = 0;
            if (string.IsNullOrWhiteSpace(summaryText))
                return false;

            int start = summaryText.IndexOf("总入库:", StringComparison.Ordinal);
            if (start < 0)
                return false;

            int end = summaryText.IndexOf("件", start, StringComparison.Ordinal);
            if (end < 0)
                return false;

            string numberPart = summaryText.Substring(start + 4, end - start - 4).Trim().Replace(",", "");
            if (!int.TryParse(numberPart, out quantity))
                return false;

            return true;
        }

        private static string ResolveDefaultHandler(DatabaseManager db)
        {
            try
            {
                DataTable handlers = db.GetAllHandlers();
                if (handlers != null && handlers.Rows.Count > 0)
                {
                    string name = handlers.Rows[0]["name"]?.ToString()?.Trim();
                    if (!string.IsNullOrEmpty(name))
                        return name;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"读取默认经手人失败: {ex.Message}");
            }

            return Environment.UserName;
        }

        private DataTable BuildDetailsDataTableForExport()
        {
            var table = new DataTable();
            table.Columns.Add("Type");
            table.Columns.Add("TransactionDate");
            table.Columns.Add("ItemType");
            table.Columns.Add("Location");
            table.Columns.Add("Quantity");
            table.Columns.Add("UnitPrice");
            table.Columns.Add("Amount");
            table.Columns.Add("Handler");
            table.Columns.Add("Reason");

            if (_lastDetailsData != null && _lastDetailsData.Rows.Count > 0)
            {
                AppendMergedDetailsForExport(table, _lastDetailsData);
                return table;
            }

            foreach (DataGridViewRow row in dataGridViewDetails.Rows)
            {
                if (row.IsNewRow) continue;
                string type = GetCellValue(row, "Type");
                if (type.Contains("已结清")) continue;
                table.Rows.Add(
                    GetCellValue(row, "Type"),
                    GetCellValue(row, "TransactionDate"),
                    GetCellValue(row, "ItemType"),
                    GetCellValue(row, "Location"),
                    GetCellValue(row, "Quantity"),
                    GetCellValue(row, "UnitPrice"),
                    GetCellValue(row, "Amount"),
                    GetCellValue(row, "Handler"),
                    GetCellValue(row, "Reason")
                );
            }

            return table;
        }

        /// <summary>
        /// 导出/打印专用：同一型号包装单合并为一行，合并行日期显示「汇总后」（仅影响导出，不写库）。
        /// </summary>
        private static void AppendMergedDetailsForExport(DataTable table, DataTable source)
        {
            var packagingGroups = new Dictionary<string, List<int>>();
            for (int i = 0; i < source.Rows.Count; i++)
            {
                DataRow row = source.Rows[i];
                if (IsSettledTransaction(row)) continue;
                if (!IsPackagingTransaction(row)) continue;

                string groupKey = GetPackagingMergeKey(row);
                if (!packagingGroups.TryGetValue(groupKey, out List<int> indices))
                {
                    indices = new List<int>();
                    packagingGroups[groupKey] = indices;
                }
                indices.Add(i);
            }

            var skipIndices = new HashSet<int>();
            var mergedAtFirstIndex = new Dictionary<int, object[]>();

            foreach (List<int> indices in packagingGroups.Values)
            {
                if (indices.Count < 2) continue;

                mergedAtFirstIndex[indices[0]] = BuildMergedPackagingExportRow(source, indices);
                for (int j = 1; j < indices.Count; j++)
                {
                    skipIndices.Add(indices[j]);
                }
            }

            for (int i = 0; i < source.Rows.Count; i++)
            {
                if (IsSettledTransaction(source.Rows[i])) continue;
                if (skipIndices.Contains(i)) continue;

                if (mergedAtFirstIndex.TryGetValue(i, out object[] mergedRow))
                {
                    table.Rows.Add(mergedRow);
                }
                else
                {
                    table.Rows.Add(BuildDetailExportRow(source.Rows[i], null));
                }
            }
        }

        private static bool IsSettledTransaction(DataRow row)
        {
            return row.Table.Columns.Contains("IsSettled")
                && row["IsSettled"] != DBNull.Value
                && Convert.ToBoolean(row["IsSettled"]);
        }

        private static bool IsPackagingTransaction(DataRow row)
        {
            return string.Equals(row["Type"]?.ToString(), "包装", StringComparison.Ordinal);
        }

        private static string GetPackagingMergeKey(DataRow row)
        {
            string displayType = GetTransactionDisplayType(row);
            string itemType = row["ItemType"]?.ToString() ?? "";
            return displayType + "\u0001" + itemType;
        }

        private static string GetTransactionDisplayType(DataRow row)
        {
            string type = row["Type"]?.ToString() ?? "";
            string displayType = type;

            if (row.Table.Columns.Contains("IsSettled")
                && row["IsSettled"] != DBNull.Value
                && Convert.ToBoolean(row["IsSettled"]))
            {
                displayType = type + "(已结清)";
            }

            if (type == "包装" && row.Table.Columns.Contains("PackFlag"))
            {
                string packFlag = row["PackFlag"]?.ToString() ?? "";
                if (packFlag == "RETURN")
                {
                    displayType = type + "(进)";
                }
                else if (packFlag == "TAKE")
                {
                    displayType = type + "(出)";
                }
            }

            return displayType;
        }

        private static object[] BuildDetailExportRow(DataRow row, string dateOverride)
        {
            string displayType = GetTransactionDisplayType(row);
            string date = dateOverride
                ?? Convert.ToDateTime(row["TransactionDate"]).ToString("yyyy-MM-dd");
            string itemType = row["ItemType"]?.ToString() ?? "";
            string location = row["Location"]?.ToString() ?? "";

            decimal quantity = row["Quantity"] != DBNull.Value ? Convert.ToDecimal(row["Quantity"]) : 0m;
            decimal unitPrice = row["UnitPrice"] != DBNull.Value ? Convert.ToDecimal(row["UnitPrice"]) : 0m;
            decimal amount = row["Amount"] != DBNull.Value ? Convert.ToDecimal(row["Amount"]) : 0m;

            string handler = row["Handler"]?.ToString() ?? "";
            string reason = row["Reason"]?.ToString() ?? "";

            return new object[]
            {
                displayType,
                date,
                itemType,
                location,
                quantity.ToString("N0"),
                unitPrice.ToString("N2"),
                amount.ToString("N2"),
                handler,
                reason
            };
        }

        private static object[] BuildMergedPackagingExportRow(DataTable source, List<int> indices)
        {
            DataRow firstRow = source.Rows[indices[0]];
            decimal totalQuantity = 0m;
            decimal totalAmount = 0m;
            decimal? singleUnitPrice = null;
            bool sameUnitPrice = true;

            foreach (int index in indices)
            {
                DataRow row = source.Rows[index];
                decimal quantity = row["Quantity"] != DBNull.Value ? Convert.ToDecimal(row["Quantity"]) : 0m;
                decimal unitPrice = row["UnitPrice"] != DBNull.Value ? Convert.ToDecimal(row["UnitPrice"]) : 0m;
                decimal amount = row["Amount"] != DBNull.Value ? Convert.ToDecimal(row["Amount"]) : 0m;

                totalQuantity += quantity;
                totalAmount += amount;

                if (!singleUnitPrice.HasValue)
                {
                    singleUnitPrice = unitPrice;
                }
                else if (singleUnitPrice.Value != unitPrice)
                {
                    sameUnitPrice = false;
                }
            }

            // 单价一致显示该单价；不一致显示「不同」（金额仍为真实合计）
            string unitPriceText = sameUnitPrice && singleUnitPrice.HasValue
                ? singleUnitPrice.Value.ToString("N2")
                : "不同";

            string displayType = GetTransactionDisplayType(firstRow);
            string itemType = firstRow["ItemType"]?.ToString() ?? "";
            string location = firstRow["Location"]?.ToString() ?? "";
            string handler = firstRow["Handler"]?.ToString() ?? "";
            string reason = $"共{indices.Count}单";

            return new object[]
            {
                displayType,
                "汇总后",
                itemType,
                location,
                totalQuantity.ToString("N0"),
                unitPriceText,
                totalAmount.ToString("N2"),
                handler,
                reason
            };
        }

        private DataTable BuildInboundStatsDataTableForExport()
        {
            var table = new DataTable();
            table.Columns.Add("库位");
            table.Columns.Add("型号");
            table.Columns.Add("入库数量");
            table.Columns.Add("单数");

            foreach (DataGridViewRow row in dgvInboundStats.Rows)
            {
                if (row.IsNewRow) continue;
                string model = row.Cells["Model"]?.Value?.ToString() ?? "";
                if (model == "暂无入库记录" || model == "加载失败") continue;

                table.Rows.Add(
                    row.Cells["Location"]?.Value?.ToString() ?? "",
                    model,
                    row.Cells["Quantity"]?.Value?.ToString() ?? "0",
                    row.Cells["OrderCount"]?.Value?.ToString() ?? "0"
                );
            }

            return table;
        }
        // 新增方法：从当前数据显示的数据重新计算（用于导出）
        private void CalculateFromCurrentData(ref decimal salesTotal, ref decimal packagingTotal,
                                    ref decimal deductionTotal, ref decimal advanceTotal, ref decimal payableTotal)
        {
            salesTotal = 0;
            packagingTotal = 0;
            deductionTotal = 0;
            advanceTotal = 0;
            payableTotal = 0;

            foreach (DataGridViewRow row in dataGridViewDetails.Rows)
            {
                if (row.IsNewRow) continue;

                string type = row.Cells["Type"].Value?.ToString() ?? "";
                string amountStr = row.Cells["Amount"].Value?.ToString() ?? "0";

                if (decimal.TryParse(amountStr, out decimal amount))
                {
                    amount = Math.Abs(amount); // 取绝对值

                    switch (type)
                    {
                        case "销售":
                        case "销售(已结清)":
                            if (type != "销售(已结清)")
                            {
                                salesTotal += amount;
                            }
                            break;

                        case "包装(出)":
                        case "包装(进)":
                        case "包装":
                            // ===== 【新增】包装类型处理 =====
                            if (!type.Contains("已结清"))
                            {
                                packagingTotal += amount;
                            }
                            break;

                        case "包装(已结清)":
                            // 已结清的不计入
                            break;

                        case "扣款":
                            deductionTotal += amount;
                            break;

                        case "预支":
                            advanceTotal += amount;
                            break;
                    }
                }
            }

            decimal totalDeductions = packagingTotal + advanceTotal + deductionTotal;
            payableTotal = salesTotal - totalDeductions;
        }
        // 修改格式化方法，优化对齐
        // 修改 FormatExportTableRow 方法
        private string FormatExportTableRow(DataGridViewRow row)
        {
            StringBuilder sb = new StringBuilder();

            // 1. 类型 (6字符，左对齐)
            string type = GetCellValue(row, "Type");

            // ===== 【新增】在导出时保持类型显示，不截断 =====
            // 确保显示完整类型（销售/销售(已结清)/包装(出)/包装(进)等）
            if (type.Length > 6)
            {
                // 如果太长，保留前几个字符
                type = type.Substring(0, Math.Min(type.Length, 8));
            }
            sb.Append(type.PadRight(8)); // 增加宽度到8字符

            // 2. 日期 (10字符，左对齐)
            string date = GetCellValue(row, "TransactionDate");
            if (date.Length >= 10)
            {
                date = date.Substring(5, 5);
            }
            sb.Append(date.PadRight(10));

            // 3. 商品 (12字符，左对齐)
            string item = GetCellValue(row, "ItemType");
            if (string.IsNullOrEmpty(item)) item = "";
            if (item.Length > 12)
            {
                item = item.Substring(0, 9) + "...";
            }
            sb.Append(item.PadRight(12));

            // 4. 库位 (8字符，左对齐)
            string location = GetCellValue(row, "Location");
            if (string.IsNullOrEmpty(location)) location = "";
            sb.Append(location.PadRight(8));

            // 5. 数量 (8字符，右对齐)
            string quantity = GetCellValue(row, "Quantity");
            if (decimal.TryParse(quantity, out decimal qty))
            {
                quantity = qty.ToString("N0");
            }
            else
            {
                quantity = "0";
            }
            sb.Append(quantity.PadLeft(8));

            // 6. 单价 (10字符，右对齐)
            string unitPrice = GetCellValue(row, "UnitPrice");
            if (decimal.TryParse(unitPrice, out decimal price))
            {
                unitPrice = price.ToString("N2");
            }
            else
            {
                unitPrice = "0.00";
            }
            sb.Append(unitPrice.PadLeft(10));

            // 7. 金额 (12字符，右对齐)
            string amount = GetCellValue(row, "Amount");
            if (decimal.TryParse(amount, out decimal amt))
            {
                amount = amt.ToString("N2");
            }
            else
            {
                amount = "0.00";
            }
            sb.Append(amount.PadLeft(12));

            // 8. 经手人 (8字符，左对齐)
            string handler = GetCellValue(row, "Handler");
            if (string.IsNullOrEmpty(handler)) handler = "";
            if (handler.Length > 8)
            {
                handler = handler.Substring(0, 5) + "...";
            }
            sb.Append(handler.PadRight(8));

            // 9. 备注 (15字符，左对齐)
            string reason = GetCellValue(row, "Reason");
            if (string.IsNullOrEmpty(reason)) reason = "";
            if (reason.Length > 15)
            {
                reason = reason.Substring(0, 12) + "...";
            }
            sb.Append(reason.PadRight(15));

            return sb.ToString();
        }

        // 辅助方法：安全获取单元格值
        private string GetCellValue(DataGridViewRow row, string columnName)
        {
            if (row.Cells[columnName] != null && row.Cells[columnName].Value != null)
            {
                return row.Cells[columnName].Value.ToString().Trim();
            }
            return "";
        }
    }
}