using System;
using System.Collections.Generic;
using System.Data;
using System.Drawing;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace lengkubao.desktop
{
    public class CompleteSalesQueryForm : Form
    {
        // 控件声明
        private DataGridView dgvSales;
        private DatabaseManager db;
        private DataTable originalData;
        private string currentTableName = "";
        // 添加编辑模式相关字段
        private bool isEditMode = false;
        private Dictionary<string, string> columnChineseNames; // 字段映射

        private static readonly HashSet<string> EditableSalesColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "client_name", "spec", "quantity", "unit_price", "total_amount",
            "location", "handler", "remarks", "status"
        };

        // 查询条件控件
        private ComboBox cmbClient;
        private ComboBox cmbSpec;
        private ComboBox cmbLocation;
        private List<ClientSearchItem> _clientItems = new List<ClientSearchItem>();
        private List<ClientSearchItem> _specItems = new List<ClientSearchItem>();
        private List<ClientSearchItem> _locationItems = new List<ClientSearchItem>();
        private DateTimePicker dtpStartDate;
        private DateTimePicker dtpEndDate;

        // 按钮
        private Button btnQuery;
        private Button btnEdit;
        private Button btnSave;
        private Button btnDelete;
        private Button btnExport;
        private Button btnClose;

        // 状态标签
        private Label lblStatus;
        private Label lblRecordCount;

        // 分页控件
        private NumericUpDown numPageNumber;
        private Label lblPageInfo;
        private int currentPage = 1;
        private int pageSize = 100;
        private int totalPages = 1;
        private int filteredRecordCount = 0;

        public CompleteSalesQueryForm()
        {
            InitializeForm();
            InitializeColumnMappings(); // 添加这一行
            // 窗体加载完成后强制刷新一次布局
            this.Shown += (s, e) =>
            {
                Application.DoEvents();
                this.Refresh();
            };
            FindAndLoadTable();
        }

        private void InitializeForm()
        {
            QueryUiHelper.ApplyFormChrome(this);
            this.Text = "报账查询";
            this.Size = new Size(1280, 720);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.FormBorderStyle = FormBorderStyle.Sizable;
            this.MaximizeBox = true;
            this.MinimizeBox = true;
            this.WindowState = FormWindowState.Normal;

            var mainLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                RowCount = 6,
                ColumnCount = 1,
                BackColor = StatisticsUiHelper.PanelBack,
                Padding = new Padding(0)
            };
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 124));
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));

            Panel headerPanel = QueryUiHelper.CreateModuleHeader(QueryModule.Sales, "报账查询", out lblStatus);
            mainLayout.Controls.Add(headerPanel, 0, 0);

            Panel queryPanel = new Panel { Dock = DockStyle.Fill };
            QueryUiHelper.StyleFilterPanel(queryPanel);
            BuildSalesQueryPanel(queryPanel);
            mainLayout.Controls.Add(queryPanel, 0, 1);

            Panel infoPanel = new Panel { Dock = DockStyle.Fill };
            QueryUiHelper.StyleInfoBar(infoPanel);
            lblRecordCount = new Label { Text = QueryUiHelper.FormatFilterSummaryText(0) };
            QueryUiHelper.StyleFilterSummaryLabel(lblRecordCount, QueryModule.Sales);
            infoPanel.Controls.Add(lblRecordCount);
            mainLayout.Controls.Add(infoPanel, 0, 2);

            dgvSales = new DataGridView();
            QueryUiHelper.ApplyQueryGrid(dgvSales, QueryModule.Sales, multiSelect: true);
            Panel gridHost = QueryUiHelper.CreateGridHost(dgvSales);
            mainLayout.Controls.Add(gridHost, 0, 3);

            Panel pagePanel = BuildSalesPagePanel();
            mainLayout.Controls.Add(pagePanel, 0, 4);

            Panel buttonPanel = BuildSalesButtonPanel();
            mainLayout.Controls.Add(buttonPanel, 0, 5);

            this.Controls.Add(mainLayout);
        }

        private void BuildSalesQueryPanel(Panel queryPanel)
        {
            cmbClient = CreateComboBox(0, 0, 200);
            cmbSpec = CreateComboBox(0, 0, 150);
            cmbLocation = CreateComboBox(0, 0, 120);

            dtpStartDate = new DateTimePicker { Width = 132 };
            dtpEndDate = new DateTimePicker { Width = 132 };
            QueryUiHelper.StyleDatePicker(dtpStartDate);
            QueryUiHelper.StyleDatePicker(dtpEndDate);
            DateRangeSettings.ApplyTo(dtpStartDate, dtpEndDate);

            btnQuery = QueryUiHelper.CreateButton("查询", QueryButtonRole.Primary, QueryUiHelper.CompactButtonSize);
            btnQuery.Click += BtnQuery_Click;

            Button btnReset = QueryUiHelper.CreateButton("重置", QueryButtonRole.Secondary, QueryUiHelper.CompactButtonSize);
            btnReset.Click += BtnReset_Click;

            queryPanel.Controls.Add(QueryUiHelper.CreateTwoRowQueryFilterPanel(
                QueryUiHelper.CreateFilterField("开始日期:", dtpStartDate, 132),
                QueryUiHelper.CreateFilterField("结束日期:", dtpEndDate, 132),
                new[]
                {
                    QueryUiHelper.CreateFilterField("客 户:", cmbClient, 200),
                    QueryUiHelper.CreateFilterField("规 格:", cmbSpec, 150),
                    QueryUiHelper.CreateFilterField("库 位:", cmbLocation, 120)
                },
                btnQuery,
                btnReset));
        }

        private Panel BuildSalesPagePanel()
        {
            Panel pagePanel = new Panel { Dock = DockStyle.Fill, BackColor = StatisticsUiHelper.PanelBack, Padding = new Padding(12, 4, 12, 4) };
            var flow = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                Padding = new Padding(0),
                BackColor = StatisticsUiHelper.PanelBack
            };

            Button btnPrevPage = QueryUiHelper.CreateButton("上一页", QueryButtonRole.Secondary, QueryUiHelper.CompactButtonSize);
            btnPrevPage.Margin = new Padding(0, 0, 8, 0);
            btnPrevPage.Click += (s, e) =>
            {
                if (currentPage > 1)
                {
                    currentPage--;
                    LoadSalesData();
                }
            };
            flow.Controls.Add(btnPrevPage);

            Label lblPage = new Label { Text = "页码:", AutoSize = true, Margin = new Padding(0, 8, 6, 0), Font = QueryUiHelper.ChromeFont };
            flow.Controls.Add(lblPage);

            numPageNumber = new NumericUpDown { Width = 64, Height = 28, Font = QueryUiHelper.ChromeFont, Minimum = 1, Maximum = 1, Value = 1, Margin = new Padding(0, 4, 8, 0) };
            numPageNumber.ValueChanged += (s, e) =>
            {
                currentPage = (int)numPageNumber.Value;
                LoadSalesData();
            };
            flow.Controls.Add(numPageNumber);

            lblPageInfo = new Label { Text = "第 1 页 / 共 1 页", AutoSize = true, Margin = new Padding(0, 8, 8, 0), Font = QueryUiHelper.ChromeFont };
            flow.Controls.Add(lblPageInfo);

            Button btnNextPage = QueryUiHelper.CreateButton("下一页", QueryButtonRole.Secondary, QueryUiHelper.CompactButtonSize);
            btnNextPage.Click += (s, e) =>
            {
                if (currentPage < totalPages)
                {
                    currentPage++;
                    LoadSalesData();
                }
            };
            flow.Controls.Add(btnNextPage);

            pagePanel.Controls.Add(flow);
            return pagePanel;
        }

        private Panel BuildSalesButtonPanel()
        {
            Panel buttonPanel = new Panel { Dock = DockStyle.Fill };
            QueryUiHelper.StyleToolbarPanel(buttonPanel);
            var flow = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                Padding = new Padding(0, 2, 0, 0)
            };

            btnEdit = QueryUiHelper.CreateButton("编辑", QueryButtonRole.Primary);
            btnEdit.Margin = new Padding(0, 0, 8, 0);
            btnEdit.Click += BtnEdit_Click;
            flow.Controls.Add(btnEdit);

            btnSave = QueryUiHelper.CreateButton("保存", QueryButtonRole.Success);
            btnSave.Margin = new Padding(0, 0, 8, 0);
            btnSave.Click += BtnSave_Click;
            flow.Controls.Add(btnSave);

            Button btnCancelEdit = QueryUiHelper.CreateButton("取消", QueryButtonRole.Neutral);
            btnCancelEdit.Margin = new Padding(0, 0, 8, 0);
            btnCancelEdit.Click += BtnCancel_Click;
            flow.Controls.Add(btnCancelEdit);

            btnDelete = QueryUiHelper.CreateButton("删除", QueryButtonRole.Danger);
            btnDelete.Margin = new Padding(0, 0, 8, 0);
            btnDelete.Click += BtnDelete_Click;
            flow.Controls.Add(btnDelete);

            btnExport = QueryUiHelper.CreateButton("导出报表", QueryButtonRole.Success);
            btnExport.Margin = new Padding(0, 0, 8, 0);
            btnExport.Click += BtnExport_Click;
            flow.Controls.Add(btnExport);

            Button btnPrint = QueryUiHelper.CreateButton("打印", QueryButtonRole.Info);
            btnPrint.Margin = new Padding(0, 0, 8, 0);
            btnPrint.Click += BtnPrint_Click;
            flow.Controls.Add(btnPrint);

            Button btnViewDetail = QueryUiHelper.CreateButton("详情", QueryButtonRole.Info);
            btnViewDetail.Margin = new Padding(0, 0, 8, 0);
            btnViewDetail.Click += BtnViewDetail_Click;
            flow.Controls.Add(btnViewDetail);

            Button btnSettle = QueryUiHelper.CreateButton("结清", QueryButtonRole.Success);
            btnSettle.Margin = new Padding(0, 0, 8, 0);
            btnSettle.Click += BtnSettle_Click;
            flow.Controls.Add(btnSettle);

            btnClose = QueryUiHelper.CreateButton("关闭", QueryButtonRole.Neutral);
            btnClose.Click += (s, e) => this.Close();
            flow.Controls.Add(btnClose);

            buttonPanel.Controls.Add(flow);
            return buttonPanel;
        }
        private void InitializeColumnMappings()
        {
            columnChineseNames = new Dictionary<string, string>
    {
        { "id", "ID" },
        { "order_no", "销售单号" },
        { "client_code", "客户编码" },
        { "client_name", "客户名称" },
        { "spec", "规格" },
        { "quantity", "数量" },
        { "unit_price", "单价" },
        { "total_amount", "总金额" },
        { "date", "销售日期" },
        { "handler", "经手人" },
        { "location", "库位" },
        { "creator", "创建人" },
        { "created_time", "创建时间" },
        { "updated_at", "更新时间" },
        { "status", "状态" },
        { "is_settled", "是否结清" },
        { "settled_time", "结清时间" },
        { "settled_by", "结清人" },
        { "remarks", "备注" }
        };
        }

        private Label CreateFormLabel(string text, int x, int y)
        {
            var label = new Label
            {
                Text = text,
                Location = new Point(x, y + 5),
                Size = new Size(70, 25),
                TextAlign = ContentAlignment.MiddleRight
            };
            QueryUiHelper.StyleFilterLabel(label);
            return label;
        }

        private ComboBox CreateComboBox(int x, int y, int width)
        {
            var combo = new ComboBox
            {
                Location = new Point(x, y),
                Size = new Size(width, QueryUiHelper.FilterControlHeight),
                BackColor = Color.White,
                FlatStyle = FlatStyle.Flat
            };
            QueryUiHelper.StyleComboBox(combo, QueryUiHelper.FilterControlHeight);
            ClientSearchHelper.ApplySearchableStyle(combo, 260);
            return combo;
        }

        private ClientSearchHelper.SearchComboOptions FilterComboOptions =>
            new ClientSearchHelper.SearchComboOptions
            {
                PlaceholderText = ClientSearchHelper.NamePlaceholderText,
                AllowEmptySelection = true
            };

        private void FindAndLoadTable()
        {
            try
            {
                Cursor.Current = Cursors.WaitCursor;
                lblStatus.Text = "正在查找销售表...";

                // 查找销售表 - 直接使用 sales_transactions 表
                currentTableName = "sales_transactions";

                // 检查表是否存在
                string checkTableSql = "SELECT name FROM sqlite_master WHERE type='table' AND name='sales_transactions'";
                db = new DatabaseManager();
                DataTable tableCheck = db.ExecuteQuery(checkTableSql);

                if (tableCheck == null || tableCheck.Rows.Count == 0)
                {
                    lblStatus.Text = "未找到销售表";
                    MessageBox.Show("未找到销售表 sales_transactions！", "提示",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);

                    // 尝试创建表
                    if (MessageBox.Show("销售表不存在，是否尝试创建？", "确认",
                        MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                    {
                        CreateSalesTable();
                        LoadFilterData();
                        LoadSalesData();
                    }
                    return;
                }

                lblStatus.Text = $"找到表：{currentTableName}，正在初始化...";

                // 加载筛选数据
                LoadFilterData();

                // 加载销售数据
                LoadSalesData();
            }
            catch (Exception ex)
            {
                lblStatus.Text = "初始化失败";
                MessageBox.Show($"初始化失败：{ex.Message}", "错误",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                Cursor.Current = Cursors.Default;
            }
        }

        // 创建销售表的SQL语句（如果需要）
        private void CreateSalesTable()
        {
            try
            {
                string createTableSql = @"CREATE TABLE IF NOT EXISTS sales_transactions (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    order_no TEXT NOT NULL,
    spec TEXT NOT NULL,
    client_code TEXT NOT NULL,
    client_name TEXT NOT NULL,
    location TEXT,
    date TEXT NOT NULL,
    quantity INTEGER DEFAULT 0,
    total_amount REAL DEFAULT 0,
    handler TEXT,
    creator TEXT,
    created_time DATETIME DEFAULT CURRENT_TIMESTAMP,
    updated_at DATETIME DEFAULT CURRENT_TIMESTAMP
)";

                int result = db.ExecuteNonQuery(createTableSql);
                if (result >= 0)
                {
                    lblStatus.Text = "销售表已创建";
                    MessageBox.Show("销售表创建成功！", "成功",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"创建销售表失败：{ex.Message}", "错误",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void LoadFilterData()
        {
            try
            {
                db = new DatabaseManager();

                _clientItems = ClientSearchHelper.BuildItemsWithNone(db.GetAllClients());
                ClientSearchHelper.BindSearchableCombo(cmbClient, _clientItems, FilterComboOptions);

                var specNames = new List<string>();
                if (!string.IsNullOrEmpty(currentTableName))
                {
                    string specQuery = $"SELECT DISTINCT spec FROM {currentTableName} WHERE spec IS NOT NULL AND spec != '' ORDER BY spec LIMIT 200";
                    DataTable specData = db.ExecuteQuery(specQuery);
                    if (specData != null)
                    {
                        foreach (DataRow row in specData.Rows)
                        {
                            string spec = row["spec"].ToString();
                            if (!string.IsNullOrEmpty(spec))
                                specNames.Add(spec);
                        }
                    }
                }
                _specItems = ClientSearchHelper.BuildSimpleItemsWithNone(specNames);
                ClientSearchHelper.BindSearchableCombo(cmbSpec, _specItems, FilterComboOptions);

                var locationNames = new List<string>();
                foreach (DataRow row in db.GetActiveLocations().Rows)
                {
                    string name = row["name"]?.ToString()?.Trim();
                    if (!string.IsNullOrEmpty(name) && !locationNames.Contains(name))
                        locationNames.Add(name);
                }
                _locationItems = ClientSearchHelper.BuildSimpleItemsWithNone(locationNames);
                ClientSearchHelper.BindSearchableCombo(cmbLocation, _locationItems, FilterComboOptions);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"加载筛选数据失败：{ex.Message}", "错误",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void LoadSalesData()
        {
            try
            {
                Cursor.Current = Cursors.WaitCursor;
                lblStatus.Text = $"正在加载数据：{currentTableName}";

                db = new DatabaseManager();

                // 构建查询条件
                List<string> conditions = new List<string>();
                var parameters = new Dictionary<string, object>();

                // 客户筛选
                if (ClientSearchHelper.TryGetSelectedItem(cmbClient, _clientItems, out ClientSearchItem clientFilter, allowEmptySelection: true)
                    && clientFilter != null)
                {
                    ClientSearchHelper.AppendClientFilter(clientFilter, conditions, parameters);
                }

                // 规格筛选
                if (ClientSearchHelper.TryGetSelectedItem(cmbSpec, _specItems, out ClientSearchItem specFilter, allowEmptySelection: true)
                    && specFilter != null)
                {
                    ClientSearchHelper.AppendFieldFilter(specFilter, "spec", conditions, parameters, "@spec");
                }

                // 日期筛选 - 尝试查找日期字段
                DataTable schema = db.ExecuteQuery($"PRAGMA table_info({currentTableName})");
                string dateField = "";
                foreach (DataRow row in schema.Rows)
                {
                    string fieldName = row["name"].ToString().ToLower();
                    if (fieldName.Contains("date") || fieldName.Contains("时间") ||
                        fieldName.Contains("销售日期") || fieldName.Contains("创建时间"))
                    {
                        dateField = row["name"].ToString();
                        break;
                    }
                }

                if (!string.IsNullOrEmpty(dateField))
                {
                    conditions.Add($"{dateField} >= @startDate");
                    parameters.Add("@startDate", dtpStartDate.Value.ToString("yyyy-MM-dd"));

                    conditions.Add($"{dateField} <= @endDate");
                    parameters.Add("@endDate", dtpEndDate.Value.ToString("yyyy-MM-dd"));
                }

                // 库位筛选
                if (ClientSearchHelper.TryGetSelectedItem(cmbLocation, _locationItems, out ClientSearchItem locationFilter, allowEmptySelection: true)
                    && locationFilter != null)
                {
                    ClientSearchHelper.AppendFieldFilter(locationFilter, "location", conditions, parameters, "@location");
                }

                // 获取总记录数
                string countWhereClause = conditions.Count > 0 ? "WHERE " + string.Join(" AND ", conditions) : "";
                string countSql = $"SELECT COUNT(*) as total FROM {currentTableName} {countWhereClause}";
                DataTable countData = db.ExecuteQuery(countSql, parameters);
                int totalRecords = 0;
                if (countData != null && countData.Rows.Count > 0)
                {
                    totalRecords = Convert.ToInt32(countData.Rows[0]["total"]);
                }
                filteredRecordCount = totalRecords;

                // 计算分页
                totalPages = (int)Math.Ceiling(totalRecords / (double)pageSize);
                if (totalPages == 0) totalPages = 1;
                if (currentPage > totalPages) currentPage = totalPages;

                // 更新分页控件
                numPageNumber.Maximum = totalPages;
                numPageNumber.Value = currentPage;
                lblPageInfo.Text = $"第 {currentPage} 页 / 共 {totalPages} 页";

                // 添加分页条件
                int offset = (currentPage - 1) * pageSize;
                string limitClause = $"LIMIT {pageSize} OFFSET {offset}";

                // 构建查询SQL
                string whereClause = conditions.Count > 0 ? "WHERE " + string.Join(" AND ", conditions) : "";
                string orderClause = "ORDER BY id DESC";
                string sql = $"SELECT * FROM {currentTableName} {whereClause} {orderClause} {limitClause}";

                originalData = db.ExecuteQuery(sql, parameters);

                if (originalData == null || originalData.Rows.Count == 0)
                {
                    lblStatus.Text = $"表 {currentTableName} 中没有符合条件的记录";
                    DisplayNoData();
                }
                else
                {
                    lblStatus.Text = $"加载成功：第 {currentPage} 页，共 {totalRecords} 条记录";
                    DisplayData();
                }

                UpdateRecordCount();
            }
            catch (Exception ex)
            {
                lblStatus.Text = "加载失败";
                MessageBox.Show($"加载数据失败：{ex.Message}", "错误",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                DisplayNoData();
            }
            finally
            {
                Cursor.Current = Cursors.Default;
            }
        }

        private void DisplayNoData()
        {
            dgvSales.Columns.Clear();
            dgvSales.Rows.Clear();

            dgvSales.Columns.Add("提示", "提示");
            int rowIndex = dgvSales.Rows.Add();
            dgvSales.Rows[rowIndex].Cells["提示"].Value = "没有找到销售记录数据";
            dgvSales.Rows[rowIndex].Cells["提示"].Style.ForeColor = Color.Red;
            dgvSales.Rows[rowIndex].Cells["提示"].Style.Font = QueryUiHelper.QueryGridCellFont;
            dgvSales.Columns["提示"].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
        }

        private void DisplayData()
        {
            try
            {
                dgvSales.Columns.Clear();

                // 检查数据表的列
                if (originalData == null || originalData.Columns.Count == 0)
                {
                    DisplayNoData();
                    return;
                }

                // 定义需要显示的列（按顺序）- 添加单价列
                string[] displayColumns = new string[]
                {
            "order_no",      // 销售单号
            "client_name",   // 客户名称
            "spec",          // 规格
            "quantity",      // 数量
            "unit_price",    // 单价 - 新增这一行
            "total_amount",  // 总金额
            "created_time",  // 创建时间
            "handler",       // 经手人
            "location",      // 库位
            "status",        // 状态
            "is_settled",    // 是否结清
            "settled_time",  // 结清时间
            "remarks"        // 备注
                };

                // 先添加需要显示的列
                foreach (string columnName in displayColumns)
                {
                    // 检查原数据表中是否有这个列
                    if (originalData.Columns.Contains(columnName))
                    {
                        string displayName = GetChineseColumnName(columnName);
                        dgvSales.Columns.Add(columnName, displayName);
                    }
                }

                // 添加其他未在displayColumns中但可能重要的列（可选）
                foreach (DataColumn column in originalData.Columns)
                {
                    string columnName = column.ColumnName;

                    // 如果列名是ID，总是显示
                    if (columnName.ToLower() == "id" && !dgvSales.Columns.Contains(columnName))
                    {
                        dgvSales.Columns.Add(columnName, GetChineseColumnName(columnName));
                    }
                    // 如果列名包含code且不在显示列表中，添加它
                    else if (columnName.ToLower().Contains("code") && !dgvSales.Columns.Contains(columnName))
                    {
                        // 只添加客户编码，其他code可能不需要
                        if (columnName.ToLower() == "client_code")
                        {
                            dgvSales.Columns.Add(columnName, GetChineseColumnName(columnName));
                        }
                    }
                }

                // 填充数据
                foreach (DataRow row in originalData.Rows)
                {
                    int rowIndex = dgvSales.Rows.Add();

                    for (int i = 0; i < dgvSales.Columns.Count; i++)
                    {
                        string columnName = dgvSales.Columns[i].Name;

                        if (originalData.Columns.Contains(columnName))
                        {
                            object value = row[columnName];

                            // 格式化特殊字段
                            if (columnName.ToLower().Contains("price") ||
                                columnName.ToLower().Contains("amount") ||
                                columnName.ToLower().Contains("单价") ||
                                columnName.ToLower().Contains("金额"))
                            {
                                if (value != DBNull.Value)
                                {
                                    try
                                    {
                                        dgvSales.Rows[rowIndex].Cells[i].Value =
                                            Convert.ToDecimal(value).ToString("0.00");
                                    }
                                    catch
                                    {
                                        dgvSales.Rows[rowIndex].Cells[i].Value = value;
                                    }
                                }
                            }
                            else if ((columnName.ToLower() == "created_time" || columnName.ToLower() == "settled_time")
                                && value != DBNull.Value)
                            {
                                try
                                {
                                    dgvSales.Rows[rowIndex].Cells[i].Value = Convert.ToDateTime(value);
                                }
                                catch
                                {
                                    dgvSales.Rows[rowIndex].Cells[i].Value = value;
                                }
                            }
                            else if (columnName.ToLower() == "is_settled")
                            {
                                // 将0/1转换为"否"/"是"
                                if (value != DBNull.Value)
                                {
                                    int isSettled = Convert.ToInt32(value);
                                    dgvSales.Rows[rowIndex].Cells[i].Value = isSettled == 1 ? "是" : "否";
                                }
                                else
                                {
                                    dgvSales.Rows[rowIndex].Cells[i].Value = "否";
                                }
                            }
                            else
                            {
                                dgvSales.Rows[rowIndex].Cells[i].Value = value;
                            }
                        }
                    }
                }

                // 设置列宽和格式
                SetupColumns();

                // 设置状态列颜色
                SetStatusColumnColors();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"显示数据失败：{ex.Message}", "错误",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
        private void SetStatusColumnColors()
        {
            // 查找状态列和结清标记列
            DataGridViewColumn statusColumn = null;
            DataGridViewColumn settledColumn = null;

            foreach (DataGridViewColumn column in dgvSales.Columns)
            {
                if (column.HeaderText == "状态" || column.HeaderText.Contains("状态"))
                {
                    statusColumn = column;
                }
                else if (column.HeaderText == "是否结清")
                {
                    settledColumn = column;
                }
            }

            // 根据状态设置行颜色
            foreach (DataGridViewRow row in dgvSales.Rows)
            {
                bool isSettled = false;

                // 从is_settled列判断是否结清
                if (settledColumn != null && row.Cells[settledColumn.Index].Value != null)
                {
                    string settledValue = row.Cells[settledColumn.Index].Value.ToString();
                    isSettled = (settledValue == "是");
                }

                // 如果已结清，整行变灰
                if (isSettled)
                {
                    foreach (DataGridViewCell cell in row.Cells)
                    {
                        cell.Style.ForeColor = Color.Gray;
                        cell.Style.BackColor = Color.FromArgb(240, 240, 240);
                        cell.Style.Font = QueryUiHelper.QueryGridCellFont;
                    }

                    // 状态列特殊标记
                    if (statusColumn != null)
                    {
                        row.Cells[statusColumn.Index].Value = "已结清";
                        row.Cells[statusColumn.Index].Style.ForeColor = Color.Green;
                        row.Cells[statusColumn.Index].Style.Font = QueryUiHelper.QueryGridCellBoldFont;
                    }
                }
                else
                {
                    // 未结清的状态列显示为"处理中"
                    if (statusColumn != null && row.Cells[statusColumn.Index].Value == null)
                    {
                        row.Cells[statusColumn.Index].Value = "处理中";
                        row.Cells[statusColumn.Index].Style.ForeColor = Color.Orange;
                    }
                }
            }
        }

        private string GetChineseColumnName(string englishName)
        {
            // 简单的英文到中文映射
            Dictionary<string, string> columnMappings = new Dictionary<string, string>
            {
                { "id", "ID" },
                { "order_no", "销售单号" },
                { "client_code", "客户编码" },
                { "client_name", "客户名称" },
                { "spec", "规格" },
                { "quantity", "数量" },
                { "unit_price", "单价" },
                { "total_amount", "总金额" },
                { "sales_date", "销售日期" },
                { "handler", "经手人" },
                { "location", "库位" },
                { "creator", "创建人" },
                { "created_time", "创建时间" },
                { "update_time", "更新时间" },
                { "payment_status", "付款状态" },
                { "delivery_status", "发货状态" },
                { "remarks", "备注" },
                 // 新增结清相关字段
        { "status", "状态" },
        { "is_settled", "是否结清" },
        { "settled_time", "结清时间" },
        { "settled_by", "结清人" }
            };

            string lowerName = englishName.ToLower();
            return columnMappings.ContainsKey(lowerName) ? columnMappings[lowerName] : englishName;
        }

        private void SetupColumns()
        {
            dgvSales.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;

            foreach (DataGridViewColumn column in dgvSales.Columns)
            {
                column.MinimumWidth = 48;
                string header = column.HeaderText;

                if (header == "创建时间")
                {
                    column.FillWeight = 128;
                    column.MinimumWidth = 124;
                    column.DefaultCellStyle.Format = "yyyy-MM-dd HH:mm";
                }
                else if (header.Contains("结清时间"))
                {
                    column.FillWeight = 108;
                    column.MinimumWidth = 118;
                    column.DefaultCellStyle.Format = "yyyy-MM-dd HH:mm";
                }
                else if (header == "是否结清")
                {
                    column.FillWeight = 88;
                    column.MinimumWidth = 84;
                }
                else if (header.Contains("总金额"))
                {
                    column.FillWeight = 96;
                    column.MinimumWidth = 82;
                    column.DefaultCellStyle.Format = "0.00";
                }
                else if (header == "状态")
                {
                    column.FillWeight = 78;
                    column.MinimumWidth = 62;
                }
                else if (header.Contains("单价"))
                {
                    column.FillWeight = 68;
                    column.DefaultCellStyle.Format = "0.00";
                }
                else if (header.Contains("数量"))
                {
                    column.FillWeight = 62;
                    column.MinimumWidth = 58;
                }
                else if (header.Contains("客户名称") || header.Contains("规格"))
                {
                    column.FillWeight = 78;
                }
                else if (header.Contains("销售单号"))
                {
                    column.FillWeight = 92;
                }
                else if (header.Contains("备注"))
                {
                    column.FillWeight = 88;
                }
                else if (header.Contains("经手人") || header.Contains("库位"))
                {
                    column.FillWeight = 62;
                }
                else if (header.Contains("日期") || header.Contains("时间"))
                {
                    column.FillWeight = 108;
                    column.DefaultCellStyle.Format = "yyyy-MM-dd HH:mm";
                }
                else
                {
                    column.FillWeight = 72;
                }
            }

            QueryUiHelper.ApplyUniformColumnAlignment(dgvSales);
        }

        private void UpdateRecordCount()
        {
            QueryUiHelper.SumQuantityAndAmount(originalData, out decimal pageQuantity, out decimal pageAmount);
            QueryUiHelper.UpdateFilterSummaryLabel(lblRecordCount, filteredRecordCount, pageQuantity, pageAmount);
        }

        private void BtnQuery_Click(object sender, EventArgs e)
        {
            if (dtpStartDate.Value.Date > dtpEndDate.Value.Date)
            {
                MessageBox.Show("开始日期不能晚于结束日期！", "提示");
                return;
            }

            DateRangeSettings.SaveDefault(dtpStartDate.Value.Date, dtpEndDate.Value.Date);
            currentPage = 1;
            LoadSalesData();
        }

        private void BtnReset_Click(object sender, EventArgs e)
        {
            ClientSearchHelper.ResetToPlaceholder(cmbClient);
            ClientSearchHelper.ResetToPlaceholder(cmbSpec);
            ClientSearchHelper.ResetToPlaceholder(cmbLocation);
            DateRangeSettings.ApplyFactoryDefaultTo(dtpStartDate, dtpEndDate);
            currentPage = 1;
            LoadSalesData();
        }

        private void BtnEdit_Click(object sender, EventArgs e)
        {
            if (dgvSales.Rows.Count == 0 || originalData == null)
            {
                MessageBox.Show("没有数据可以编辑", "提示",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // 检查是否有已结清的记录被选中
            if (dgvSales.SelectedRows.Count > 0)
            {
                int selectedRowIndex = dgvSales.SelectedRows[0].Index;

                // 检查是否已结清
                bool isSettled = false;
                foreach (DataGridViewColumn col in dgvSales.Columns)
                {
                    if (col.HeaderText == "是否结清")
                    {
                        string settledValue = dgvSales.Rows[selectedRowIndex].Cells[col.Index].Value?.ToString() ?? "";
                        isSettled = (settledValue == "是");
                        break;
                    }
                }

                if (isSettled)
                {
                    DialogResult result = MessageBox.Show("该记录已结清，编辑后可能会影响客户对账。确定要继续吗？",
                        "警告", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);

                    if (result != DialogResult.Yes)
                        return;
                }
            }

            isEditMode = true;

            // 重新加载数据并启用编辑
            EnableEditMode();
        }
        private void EnableEditMode()
        {
            try
            {
                if (originalData == null || originalData.Rows.Count == 0)
                    return;

                // 保持当前 DisplayData 的列布局与样式，仅开启可编辑列
                dgvSales.ReadOnly = false;
                dgvSales.AllowUserToAddRows = false;
                dgvSales.AllowUserToDeleteRows = false;

                ConfigureEditableColumns();

                dgvSales.CellValueChanged += DgvSales_CellValueChanged;
                dgvSales.CellEndEdit += DgvSales_CellEndEdit;

                btnEdit.Enabled = false;

                lblStatus.Text = "编辑模式已启用 - 可编辑列已高亮，修改后请点击【保存】";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"进入编辑模式失败：{ex.Message}", "错误",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
        private void DgvSales_CellValueChanged(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0 || e.ColumnIndex < 0) return;

            try
            {
                string columnName = dgvSales.Columns[e.ColumnIndex].Name;
                if (columnName != "quantity" && columnName != "unit_price")
                    return;

                int quantityColIndex = dgvSales.Columns.Contains("quantity")
                    ? dgvSales.Columns["quantity"].Index : -1;
                int priceColIndex = dgvSales.Columns.Contains("unit_price")
                    ? dgvSales.Columns["unit_price"].Index : -1;
                int totalAmountColIndex = dgvSales.Columns.Contains("total_amount")
                    ? dgvSales.Columns["total_amount"].Index : -1;

                if (quantityColIndex < 0 || priceColIndex < 0 || totalAmountColIndex < 0)
                    return;

                decimal quantity = 0;
                object quantityValue = dgvSales.Rows[e.RowIndex].Cells[quantityColIndex].Value;
                if (quantityValue != null && quantityValue != DBNull.Value)
                    decimal.TryParse(quantityValue.ToString(), out quantity);

                decimal price = 0;
                object priceValue = dgvSales.Rows[e.RowIndex].Cells[priceColIndex].Value;
                if (priceValue != null && priceValue != DBNull.Value)
                    decimal.TryParse(priceValue.ToString(), out price);

                decimal totalAmount = quantity * price;
                dgvSales.Rows[e.RowIndex].Cells[totalAmountColIndex].Value = totalAmount.ToString("0.00");
                dgvSales.InvalidateCell(totalAmountColIndex, e.RowIndex);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"自动计算总金额时出错: {ex.Message}");
            }
        }

        private void DgvSales_CellEndEdit(object sender, DataGridViewCellEventArgs e)
        {
            // 确保提交编辑，使CellValueChanged事件能够触发
            dgvSales.CommitEdit(DataGridViewDataErrorContexts.Commit);
        }
        private DataTable CreateDisplayTable(DataTable sourceTable)
        {
            DataTable dt = new DataTable();

            // 添加行号列
            dt.Columns.Add("行号", typeof(int));

            // 添加其他列（使用中文列名）- 确保数据类型正确保留
            foreach (DataColumn column in sourceTable.Columns)
            {
                string columnName = column.ColumnName;
                string displayName = GetChineseColumnName(columnName);

                // 重要：保留原始数据类型
                dt.Columns.Add(displayName, column.DataType);
            }

            // 填充数据 - 直接复制值，不进行转换
            for (int i = 0; i < sourceTable.Rows.Count; i++)
            {
                DataRow sourceRow = sourceTable.Rows[i];
                DataRow displayRow = dt.NewRow();

                displayRow["行号"] = i + 1;

                foreach (DataColumn column in sourceTable.Columns)
                {
                    string displayName = GetChineseColumnName(column.ColumnName);
                    displayRow[displayName] = sourceRow[column]; // 直接赋值，不转换
                }

                dt.Rows.Add(displayRow);
            }

            return dt;
        }

        private void ConfigureEditableColumns()
        {
            foreach (DataGridViewColumn column in dgvSales.Columns)
            {
                column.ReadOnly = !EditableSalesColumns.Contains(column.Name);
                HighlightEditableColumnHeader(column);
            }
        }

        private void HighlightEditableColumnHeader(DataGridViewColumn column)
        {
            if (!column.ReadOnly)
            {
                column.HeaderCell.Style.BackColor = Color.FromArgb(255, 255, 200);
                column.HeaderCell.Style.ForeColor = Color.Black;
            }
            else
            {
                column.HeaderCell.Style.BackColor = Color.FromArgb(142, 68, 173);
                column.HeaderCell.Style.ForeColor = Color.White;
            }
        }

        private void ExitEditMode()
        {
            isEditMode = false;

            // 移除事件订阅 - 新增代码
            dgvSales.CellValueChanged -= DgvSales_CellValueChanged;
            dgvSales.CellEndEdit -= DgvSales_CellEndEdit;

            // 恢复原始数据显示
            dgvSales.DataSource = null;
            dgvSales.Columns.Clear();
            dgvSales.Rows.Clear();
            DisplayData();

            // 恢复只读状态
            dgvSales.ReadOnly = true;

            btnEdit.Enabled = true;

            lblStatus.Text = "编辑模式已退出";
        }
        private void BtnSave_Click(object sender, EventArgs e)
        {
            if (!isEditMode || originalData == null)
            {
                MessageBox.Show("当前不是编辑模式", "提示",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            try
            {
                dgvSales.EndEdit();

                List<ModifiedRowInfo> modifiedRows = new List<ModifiedRowInfo>();

                for (int i = 0; i < dgvSales.Rows.Count; i++)
                {
                    if (dgvSales.Rows[i].IsNewRow) continue;
                    if (i >= originalData.Rows.Count) continue;

                    DataRow originalRow = originalData.Rows[i];
                    Dictionary<string, object> changes = new Dictionary<string, object>();

                    foreach (DataGridViewColumn column in dgvSales.Columns)
                    {
                        if (!EditableSalesColumns.Contains(column.Name)) continue;
                        if (!originalData.Columns.Contains(column.Name)) continue;

                        object currentValue = NormalizeCellValueForSave(column.Name,
                            dgvSales.Rows[i].Cells[column.Index].Value);
                        object originalValue = originalRow[column.Name];

                        if (ValuesAreDifferent(currentValue, originalValue))
                            changes[column.Name] = currentValue;
                    }

                    if (changes.Count > 0)
                    {
                        modifiedRows.Add(new ModifiedRowInfo
                        {
                            RowIndex = i,
                            Id = Convert.ToInt32(originalRow["id"]),
                            Changes = changes
                        });
                    }
                }

                if (modifiedRows.Count == 0)
                {
                    MessageBox.Show("没有检测到任何修改", "提示",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                // 显示修改内容供确认
                string summary = GetModifiedSummary(modifiedRows);
                DialogResult result = MessageBox.Show($"检测到 {modifiedRows.Count} 条记录有修改：\n\n{summary}\n\n确定要保存吗？",
                    "确认保存", MessageBoxButtons.OKCancel, MessageBoxIcon.Question);

                if (result != DialogResult.OK)
                    return;

                Cursor.Current = Cursors.WaitCursor;

                // 执行保存
                int savedCount = 0;
                List<string> errors = new List<string>();

                foreach (var modifiedRow in modifiedRows)
                {
                    try
                    {
                        if (modifiedRow.Changes.Count == 0) continue;

                        // 构建更新语句
                        List<string> setClauses = new List<string>();
                        var parameters = new Dictionary<string, object>();

                        foreach (var change in modifiedRow.Changes)
                        {
                            setClauses.Add($"[{change.Key}] = @{change.Key}");

                            // 确保参数值正确处理
                            object paramValue = change.Value;
                            if (paramValue == null)
                                paramValue = DBNull.Value;

                            parameters.Add($"@{change.Key}", paramValue);
                        }

                        // 添加更新时间
                        setClauses.Add("[updated_at] = @updated_at");
                        parameters.Add("@updated_at", DateTime.Now);

                        // 添加ID参数
                        parameters.Add("@id", modifiedRow.Id);

                        string sql = $"UPDATE {currentTableName} SET {string.Join(", ", setClauses)} WHERE id = @id";

                        // 调试输出
                        System.Diagnostics.Debug.WriteLine($"执行SQL: {sql}");
                        foreach (var param in parameters)
                        {
                            System.Diagnostics.Debug.WriteLine($"参数: {param.Key} = {param.Value}");
                        }

                        int rowsAffected = db.ExecuteNonQuery(sql, parameters);

                        if (rowsAffected > 0)
                        {
                            savedCount++;

                            // 更新原始数据
                            DataRow originalRow = originalData.Rows[modifiedRow.RowIndex];
                            foreach (var change in modifiedRow.Changes)
                            {
                                if (originalRow.Table.Columns.Contains(change.Key))
                                {
                                    // 对于 is_settled 字段，保存时需要转换回原始格式
                                    object valueToSave = change.Value;
                                    if (change.Key.ToLower() == "is_settled" && valueToSave != null)
                                    {
                                        if (valueToSave is string)
                                        {
                                            valueToSave = (valueToSave.ToString() == "是") ? 1 : 0;
                                        }
                                    }
                                    originalRow[change.Key] = valueToSave ?? DBNull.Value;
                                }
                            }
                            originalRow["updated_at"] = DateTime.Now;

                            System.Diagnostics.Debug.WriteLine($"成功更新记录 ID: {modifiedRow.Id}");
                        }
                        else
                        {
                            errors.Add($"第{modifiedRow.RowIndex + 1}行(ID={modifiedRow.Id})：更新失败（没有记录被更新）");
                            System.Diagnostics.Debug.WriteLine($"更新失败 ID: {modifiedRow.Id}");
                        }
                    }
                    catch (Exception ex)
                    {
                        errors.Add($"第{modifiedRow.RowIndex + 1}行(ID={modifiedRow.Id})：{ex.Message}");
                        System.Diagnostics.Debug.WriteLine($"保存异常: {ex.ToString()}");
                    }
                }

                // 退出编辑模式
                ExitEditMode();

                // 重新加载数据
                LoadSalesData();

                // 显示结果
                string message = $"保存成功！共保存了 {savedCount} 条记录。";
                if (errors.Count > 0)
                {
                    message += $"\n\n错误：\n{string.Join("\n", errors)}";
                    MessageBox.Show(message, "保存完成（有错误）",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
                else
                {
                    MessageBox.Show(message, "保存成功",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"保存失败：{ex.Message}\n\n详细信息：{ex.StackTrace}", "错误",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                Cursor.Current = Cursors.Default;
            }
        }

        private void BtnCancel_Click(object sender, EventArgs e)
        {
            DialogResult result = MessageBox.Show("确定要取消编辑吗？所有未保存的修改将丢失。",
                "确认取消", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning);

            if (result == DialogResult.OK)
            {
                ExitEditMode();
                LoadSalesData(); // 重新加载原始数据
            }
        }

        

        private object NormalizeCellValueForSave(string columnName, object cellValue)
        {
            if (cellValue == null || cellValue == DBNull.Value)
                return DBNull.Value;

            string name = columnName.ToLower();
            if (name == "quantity")
            {
                if (decimal.TryParse(cellValue.ToString(), out decimal qty))
                    return qty;
            }
            else if (name == "unit_price" || name == "total_amount")
            {
                if (decimal.TryParse(cellValue.ToString(), out decimal amount))
                    return amount;
            }

            return cellValue.ToString().Trim();
        }

        private bool ValuesAreDifferent(object currentValue, object originalValue)
        {
            if (currentValue == DBNull.Value && originalValue == DBNull.Value)
                return false;
            if (currentValue == DBNull.Value || originalValue == DBNull.Value)
                return true;

            if (originalValue is decimal || originalValue is double || originalValue is float ||
                currentValue is decimal || currentValue is double || currentValue is float)
            {
                try
                {
                    decimal currentDec = Convert.ToDecimal(currentValue);
                    decimal originalDec = Convert.ToDecimal(originalValue);
                    return Math.Abs(currentDec - originalDec) > 0.0001m;
                }
                catch
                {
                    return !string.Equals(currentValue.ToString(), originalValue.ToString(), StringComparison.Ordinal);
                }
            }

            if (originalValue is DateTime || currentValue is DateTime)
            {
                try
                {
                    DateTime currentDate = Convert.ToDateTime(currentValue);
                    DateTime originalDate = Convert.ToDateTime(originalValue);
                    return Math.Abs((currentDate - originalDate).TotalSeconds) > 1;
                }
                catch
                {
                    return !string.Equals(currentValue.ToString(), originalValue.ToString(), StringComparison.Ordinal);
                }
            }

            return !string.Equals(
                Convert.ToString(currentValue)?.Trim(),
                Convert.ToString(originalValue)?.Trim(),
                StringComparison.Ordinal);
        }

        private string GetEnglishColumnName(string chineseColumnName)
        {
            if (string.IsNullOrEmpty(chineseColumnName)) return null;

            // 在映射字典中查找
            foreach (var kvp in columnChineseNames)
            {
                if (kvp.Value == chineseColumnName)
                    return kvp.Key;
            }

            // 常见映射
            var commonMappings = new Dictionary<string, string>
    {
        { "客户名称", "client_name" },
        { "规格", "spec" },
        { "数量", "quantity" },
        { "单价", "unit_price" },
        { "总金额", "total_amount" },
        { "库位", "location" },
        { "经手人", "handler" },
        { "备注", "remarks" },
        { "状态", "status" }
    };

            if (commonMappings.ContainsKey(chineseColumnName))
                return commonMappings[chineseColumnName];

            return chineseColumnName;
        }

        private string GetModifiedSummary(List<ModifiedRowInfo> modifiedRows)
        {
            StringBuilder sb = new StringBuilder();

            foreach (var row in modifiedRows)
            {
                sb.AppendLine($"第{row.RowIndex + 1}行 (ID:{row.Id}):");

                foreach (var change in row.Changes)
                {
                    // 找到对应的中文列名
                    string chineseName = change.Key;
                    foreach (var kvp in columnChineseNames)
                    {
                        if (kvp.Key == change.Key)
                        {
                            chineseName = kvp.Value;
                            break;
                        }
                    }

                    string valueStr = change.Value?.ToString() ?? "(空)";
                    if (valueStr.Length > 20)
                        valueStr = valueStr.Substring(0, 17) + "...";

                    sb.AppendLine($"  - {chineseName}: {valueStr}");
                }
            }

            return sb.ToString();
        }

        private class ModifiedRowInfo
        {
            public int RowIndex { get; set; }
            public int Id { get; set; }
            public Dictionary<string, object> Changes { get; set; }
        }

        private void BtnSettle_Click(object sender, EventArgs e)
        {
            if (isEditMode)
            {
                MessageBox.Show("请先退出编辑模式再执行结清操作", "提示",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            DataGridViewRow selectedRow = dgvSales.SelectedRows[0];

            // 获取选中行的ID
            string id = "";
            foreach (DataGridViewColumn column in dgvSales.Columns)
            {
                if (column.HeaderText == "ID" || column.Name.ToLower() == "id")
                {
                    id = selectedRow.Cells[column.Index].Value?.ToString();
                    break;
                }
            }

            if (string.IsNullOrEmpty(id))
            {
                MessageBox.Show("无法获取记录ID", "错误",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            // 检查是否已结清 - 直接从数据库查询当前状态
            try
            {
                string checkSql = "SELECT is_settled FROM sales_transactions WHERE id = @id";
                var checkParams = new Dictionary<string, object> { { "@id", id } };
                DataTable checkResult = db.ExecuteQuery(checkSql, checkParams);

                if (checkResult.Rows.Count > 0)
                {
                    int currentIsSettled = Convert.ToInt32(checkResult.Rows[0]["is_settled"]);
                    if (currentIsSettled == 1)
                    {
                        MessageBox.Show("该记录已经结清", "提示",
                            MessageBoxButtons.OK, MessageBoxIcon.Information);
                        return;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"检查结清状态失败: {ex.Message}");
            }

            // 获取记录信息用于确认
            string orderNo = "";
            string clientName = "";
            decimal totalAmount = 0;

            foreach (DataGridViewColumn column in dgvSales.Columns)
            {
                if (column.HeaderText.Contains("销售单号"))
                    orderNo = selectedRow.Cells[column.Index].Value?.ToString() ?? "";
                else if (column.HeaderText.Contains("客户名称"))
                    clientName = selectedRow.Cells[column.Index].Value?.ToString() ?? "";
                else if (column.HeaderText.Contains("总金额"))
                    decimal.TryParse(selectedRow.Cells[column.Index].Value?.ToString() ?? "0", out totalAmount);
            }

            string confirmMessage = $"确定要将以下销售记录标记为已结清吗？\n\n" +
                                   $"销售单号：{orderNo}\n" +
                                   $"客户名称：{clientName}\n" +
                                   $"金额：{totalAmount:N2}元\n\n" +
                                   $"结清后将不再参与客户对账计算。";

            DialogResult result = MessageBox.Show(confirmMessage,
                "确认结清", MessageBoxButtons.OKCancel, MessageBoxIcon.Question);

            if (result == DialogResult.OK)
            {
                try
                {
                    Console.WriteLine($"开始结清销售记录 ID: {id}");

                    // 更新记录为已结清
                    string sql = @"UPDATE sales_transactions 
                          SET is_settled = 1,
                              status = '已结清',
                              settled_time = datetime('now', 'localtime'),
                              settled_by = @settledBy
                          WHERE id = @id";

                    var parameters = new Dictionary<string, object>
            {
                { "@id", id },
                { "@settledBy", Environment.UserName }
            };

                    int updateResult = db.ExecuteNonQuery(sql, parameters);

                    Console.WriteLine($"更新结果: {updateResult} 行受影响");

                    if (updateResult > 0)
                    {
                        // 验证更新是否成功
                        string verifySql = "SELECT is_settled, status, settled_time FROM sales_transactions WHERE id = @id";
                        DataTable verifyResult = db.ExecuteQuery(verifySql, new Dictionary<string, object> { { "@id", id } });

                        if (verifyResult.Rows.Count > 0)
                        {
                            int newIsSettled = Convert.ToInt32(verifyResult.Rows[0]["is_settled"]);
                            string newStatus = verifyResult.Rows[0]["status"]?.ToString() ?? "";
                            string newSettledTime = verifyResult.Rows[0]["settled_time"]?.ToString() ?? "";

                            Console.WriteLine($"验证结果: is_settled={newIsSettled}, status={newStatus}, settled_time={newSettledTime}");

                            MessageBox.Show($"记录已成功标记为已结清\nis_settled={newIsSettled}, status={newStatus}\n该记录将不再参与客户对账计算。",
                                "成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        }
                        else
                        {
                            MessageBox.Show("记录已成功标记为已结清\n该记录将不再参与客户对账计算。",
                                "成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        }

                        // 重新加载数据
                        LoadSalesData();
                    }
                    else
                    {
                        MessageBox.Show($"结清失败：未找到ID为 {id} 的记录", "错误",
                            MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"结清失败：{ex.Message}", "错误",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                    Console.WriteLine($"结清异常: {ex.ToString()}");
                }
            }
        }
        private void BtnDelete_Click(object sender, EventArgs e)
        {
            if (dgvSales.SelectedRows.Count == 0 || originalData == null)
            {
                MessageBox.Show("请先选择要删除的行", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            DialogResult result = MessageBox.Show($"确定要删除选中的 {dgvSales.SelectedRows.Count} 条记录吗？此操作不可恢复！",
                "确认删除", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning);

            if (result != DialogResult.OK)
                return;

            try
            {
                Cursor.Current = Cursors.WaitCursor;

                int deletedCount = 0;
                foreach (DataGridViewRow gridRow in dgvSales.SelectedRows)
                {
                    if (gridRow.IsNewRow) continue;

                    int rowIndex = gridRow.Index;
                    if (rowIndex >= originalData.Rows.Count)
                        continue;

                    DataRow originalRow = originalData.Rows[rowIndex];
                    object idValue = originalRow["id"];

                    if (idValue != null && idValue != DBNull.Value)
                    {
                        string sql = $"DELETE FROM {currentTableName} WHERE id = @id";
                        var parameters = new Dictionary<string, object> { { "@id", idValue } };

                        if (db.ExecuteNonQuery(sql, parameters) > 0)
                        {
                            deletedCount++;
                        }
                    }
                }

                MessageBox.Show($"删除成功！共删除了 {deletedCount} 条记录。", "删除成功",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);

                LoadSalesData();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"删除失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                Cursor.Current = Cursors.Default;
            }
        }

        private bool TryBuildReportContext(out QueryReportExportHelper.QueryReportContext context)
        {
            context = null;
            if (originalData == null || originalData.Rows.Count == 0)
            {
                MessageBox.Show("没有数据可以导出！", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }

            const QueryReportKind kind = QueryReportKind.Sales;
            ClientSearchHelper.TryGetSelectedItem(cmbClient, _clientItems, out ClientSearchItem clientFilter, allowEmptySelection: true);
            string clientName = QueryReportExportHelper.ResolveExportClientName(clientFilter);
            context = QueryReportExportHelper.BuildQueryReportContext(
                originalData,
                kind,
                clientName,
                dtpStartDate.Value.Date,
                dtpEndDate.Value.Date);
            return true;
        }

        private void BtnPrint_Click(object sender, EventArgs e)
        {
            if (!TryBuildReportContext(out QueryReportExportHelper.QueryReportContext context))
                return;

            try
            {
                Cursor.Current = Cursors.WaitCursor;
                ExcelExportHelper.PrintSimpleTableWithDialog(
                    context.Title,
                    context.Subtitle,
                    context.PrintTable,
                    context.Columns);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"打印失败：{ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                Cursor.Current = Cursors.Default;
            }
        }

        private void BtnExport_Click(object sender, EventArgs e)
        {
            if (!TryBuildReportContext(out QueryReportExportHelper.QueryReportContext context))
                return;

            const QueryReportKind kind = QueryReportKind.Sales;
            ClientSearchHelper.TryGetSelectedItem(cmbClient, _clientItems, out ClientSearchItem clientFilter, allowEmptySelection: true);
            string clientName = QueryReportExportHelper.ResolveExportClientName(clientFilter);

            SaveFileDialog saveDialog = new SaveFileDialog
            {
                Filter = "PDF文件 (*.pdf)|*.pdf",
                FileName = QueryReportExportHelper.BuildDefaultFileName(kind, clientName),
                DefaultExt = "pdf",
                AddExtension = true,
                Title = "导出销售记录"
            };

            if (saveDialog.ShowDialog() != DialogResult.OK)
                return;

            try
            {
                Cursor.Current = Cursors.WaitCursor;
                string path = saveDialog.FileName;
                if (!path.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
                    path = Path.ChangeExtension(path, ".pdf");

                bool ok = ExcelExportHelper.ExportSimpleTablePdf(
                    path, context.Title, context.Subtitle, context.PrintTable, context.Columns);

                if (ok)
                {
                    MessageBox.Show($"导出成功！\n{path}", "成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    Process.Start(path);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"导出失败：{ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                Cursor.Current = Cursors.Default;
            }
        }

        private void BtnViewDetail_Click(object sender, EventArgs e)
        {
            if (dgvSales.SelectedRows.Count == 0)
            {
                MessageBox.Show("请选择要查看详情的记录！", "提示",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            try
            {
                // 获取选中行的数据
                int selectedRowIndex = dgvSales.SelectedRows[0].Index;
                StringBuilder detail = new StringBuilder();

                detail.AppendLine("销售记录详情");
                detail.AppendLine("=".PadRight(40, '='));

                // 获取所有列的数据
                for (int i = 0; i < dgvSales.Columns.Count; i++)
                {
                    string columnName = dgvSales.Columns[i].HeaderText;
                    object value = dgvSales.Rows[selectedRowIndex].Cells[i].Value;

                    if (value != null)
                    {
                        detail.AppendLine($"{columnName}: {value}");
                    }
                }

                detail.AppendLine("=".PadRight(40, '='));

                // 显示详情对话框
                DetailViewForm detailForm = new DetailViewForm(detail.ToString());
                detailForm.ShowDialog();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"查看详情失败：{ex.Message}", "错误",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }

    // 详情查看窗体
    public class DetailViewForm : Form
    {
        private TextBox txtDetail;

        public DetailViewForm(string detailText)
        {
            InitializeForm(detailText);
        }

        private void InitializeForm(string detailText)
        {
            this.Text = "记录详情";
            this.Size = new Size(600, 500);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.BackColor = Color.FromArgb(240, 242, 245);
            this.FormBorderStyle = FormBorderStyle.Sizable;
            this.MaximizeBox = true;

            // 标题
            Label lblTitle = new Label();
            lblTitle.Text = "记录详情";
            lblTitle.Font = StatisticsUiHelper.HeaderFont;
            lblTitle.ForeColor = Color.FromArgb(0, 102, 204);
            lblTitle.Size = new Size(200, 30);
            lblTitle.Location = new Point(200, 20);
            lblTitle.TextAlign = ContentAlignment.MiddleCenter;
            this.Controls.Add(lblTitle);

            // 详情文本框
            txtDetail = new TextBox();
            txtDetail.Location = new Point(20, 70);
            txtDetail.Size = new Size(540, 380);
            txtDetail.Multiline = true;
            txtDetail.ReadOnly = true;
            txtDetail.Font = QueryUiHelper.ChromeFont;
            txtDetail.ScrollBars = ScrollBars.Both;
            txtDetail.Text = detailText;
            txtDetail.BackColor = Color.White;
            txtDetail.BorderStyle = BorderStyle.FixedSingle;
            this.Controls.Add(txtDetail);

            // 关闭按钮
            Button btnClose = new Button();
            btnClose.Text = "关 闭";
            btnClose.Size = new Size(100, 35);
            btnClose.Location = new Point(240, 460);
            btnClose.Font = QueryUiHelper.ChromeBoldFont;
            btnClose.BackColor = Color.FromArgb(108, 117, 125);
            btnClose.ForeColor = Color.White;
            btnClose.FlatStyle = FlatStyle.Flat;
            btnClose.FlatAppearance.BorderSize = 0;
            btnClose.Cursor = Cursors.Hand;
            btnClose.Click += (s, e) => this.Close();
            this.Controls.Add(btnClose);

            // 复制按钮
            Button btnCopy = new Button();
            btnCopy.Text = "复制内容";
            btnCopy.Size = new Size(100, 35);
            btnCopy.Location = new Point(120, 460);
            btnCopy.Font = QueryUiHelper.ChromeBoldFont;
            btnCopy.BackColor = Color.FromArgb(0, 122, 204);
            btnCopy.ForeColor = Color.White;
            btnCopy.FlatStyle = FlatStyle.Flat;
            btnCopy.FlatAppearance.BorderSize = 0;
            btnCopy.Cursor = Cursors.Hand;
            btnCopy.Click += (s, e) =>
            {
                Clipboard.SetText(txtDetail.Text);
                MessageBox.Show("内容已复制到剪贴板！", "提示",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            };
            this.Controls.Add(btnCopy);

            // 窗体大小调整
            this.Resize += (s, e) =>
            {
                if (this.WindowState == FormWindowState.Minimized)
                    return;

                int formWidth = this.ClientSize.Width;
                int formHeight = this.ClientSize.Height;

                txtDetail.Width = formWidth - 40;
                txtDetail.Height = formHeight - 130;

                btnClose.Left = (formWidth - btnClose.Width) / 2;
                btnClose.Top = formHeight - 50;

                btnCopy.Left = btnClose.Left - btnCopy.Width - 20;
                btnCopy.Top = formHeight - 50;
            };
        }
    }
}