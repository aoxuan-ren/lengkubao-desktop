// CompleteInboundQueryForm.cs - 简化版（只显示入库记录）
using System;
using System.Drawing;
using System.Data;
using System.Windows.Forms;
using System.Data.SQLite;
using System.IO;
using System.Collections.Generic;
using System.Text;
using System.Linq;
using System.Diagnostics; // 添加这个引用

namespace lengkubao.desktop
{
    public class CompleteInboundQueryForm : Form
    {
        private DataGridView dataGridView;
        private Label lblStatus;
        private Label lblFilterSummary;
        private int filteredRecordCount = 0;
        private DateTimePicker dtpStart;
        private DateTimePicker dtpEnd;
        private ComboBox cmbClient;
        private ComboBox cmbSpec;
        private ComboBox cmbHandler;
        private ComboBox cmbLocation;
        private List<ClientSearchItem> _clientItems = new List<ClientSearchItem>();
        private List<ClientSearchItem> _specItems = new List<ClientSearchItem>();
        private List<ClientSearchItem> _handlerItems = new List<ClientSearchItem>();
        private List<ClientSearchItem> _locationItems = new List<ClientSearchItem>();
        private DataTable currentDataTable;
        private bool isEditMode = false;
        private static readonly HashSet<string> EditableInboundColumns = new HashSet<string>
        {
            "客户名称", "商品型号", "数量", "单价", "总金额",
            "库位名称", "经手人", "单据号", "备注"
        };
        // 添加这行 - 数据操作帮助类
        private DatabaseManager dbManager;
        // 字段名到中文的映射
        private readonly Dictionary<string, string> columnChineseNames = new Dictionary<string, string>
{
    { "id", "ID" },
    { "order_no", "单据号" },           // 修改：从 bill_no 改为 order_no
    { "spec", "商品型号" },              // 修改：从 product_type_name 改为 spec
    { "client_code", "客户编码" },        // 新增
    { "client_name", "客户名称" },
    { "location", "库位名称" },           // 修改：从 location_name 改为 location
    { "date", "入库日期" },               // 修改：从 transaction_date 改为 date
    { "quantity", "数量" },
    { "unit_price", "单价" },
    { "total_amount", "总金额" },
    { "handler", "经手人" },              // 修改：从 handler_name 改为 handler
    { "creator", "创建人" },              // 新增
    { "created_time", "创建时间" },        // 修改：从 created_at 改为 created_time
    { "updated_at", "更新时间" },
    { "source_device_id", "来源设备" },
    { "source_record_id", "来源记录ID" }
};

        public CompleteInboundQueryForm()
{
    this.Text = "入库记录查询";
    this.Size = new Size(1280, 720);
    this.StartPosition = FormStartPosition.CenterScreen;
            // 设置窗口可以最大化
            this.WindowState = FormWindowState.Normal;
            this.MaximizeBox = true;
            dbManager = new DatabaseManager();

    // 创建界面
    InitializeComponents();

    // 加载筛选下拉
    LoadFilterData();

    // 加载数据
    LoadData();
}

        private static string GetActiveConnectionString()
        {
            return DatabaseManager.GetActiveConnectionString();
        }

        private static string GetActiveDbDisplayName()
        {
            return Path.GetFileName(DatabaseManager.GetDatabaseFilePath());
        }

        /// <summary>仅独立顶层打开时最大化；嵌入主窗工作区时由父容器布局，禁止 Maximized。</summary>
        private void TryMaximizeIfStandalone()
        {
            if (!TopLevel) return;
            if (WindowState != FormWindowState.Maximized)
                WindowState = FormWindowState.Maximized;
        }

        private void InitializeComponents()
        {
            QueryUiHelper.ApplyFormChrome(this);
            // 嵌入外壳时勿设 Maximized（见 Form1.ApplyEmbeddedShell）
            this.MaximizeBox = true;
            this.MinimizeBox = true;

            // 状态标签
            lblStatus = new Label
            {
                Text = $"入库记录查询  |  数据库: {GetActiveDbDisplayName()}"
            };
            QueryUiHelper.StyleTopStatusBar(lblStatus, QueryModule.Inbound);

            // 搜索面板
            Panel searchPanel = new Panel
            {
                Dock = DockStyle.Top,
                Height = 124
            };
            QueryUiHelper.StyleFilterPanel(searchPanel);

            cmbClient = CreateFilterComboBox(0, 0, 200);
            cmbSpec = CreateFilterComboBox(0, 0, 160);
            cmbHandler = CreateFilterComboBox(0, 0, 130);
            cmbLocation = CreateFilterComboBox(0, 0, 130);

            dtpStart = new DateTimePicker { Width = 132 };
            dtpEnd = new DateTimePicker { Width = 132 };
            QueryUiHelper.StyleDatePicker(dtpStart);
            QueryUiHelper.StyleDatePicker(dtpEnd);
            DateRangeSettings.ApplyTo(dtpStart, dtpEnd);

            Button btnSearch = QueryUiHelper.CreateButton("搜索", QueryButtonRole.Primary, QueryUiHelper.CompactButtonSize);
            btnSearch.Click += (s, e) =>
            {
                TryMaximizeIfStandalone();
                if (dtpStart.Value.Date > dtpEnd.Value.Date)
                {
                    MessageBox.Show("开始日期不能晚于结束日期！", "提示");
                    return;
                }
                DateRangeSettings.SaveDefault(dtpStart.Value.Date, dtpEnd.Value.Date);
                LoadData();
            };

            Button btnReset = QueryUiHelper.CreateButton("重置", QueryButtonRole.Secondary, QueryUiHelper.CompactButtonSize);
            btnReset.Click += (s, e) =>
            {
                TryMaximizeIfStandalone();

                ClientSearchHelper.ResetToPlaceholder(cmbClient);
                ClientSearchHelper.ResetToPlaceholder(cmbSpec);
                ClientSearchHelper.ResetToPlaceholder(cmbHandler);
                ClientSearchHelper.ResetToPlaceholder(cmbLocation);
                DateRangeSettings.ApplyFactoryDefaultTo(dtpStart, dtpEnd);
                LoadData();
            };

            searchPanel.Controls.Add(QueryUiHelper.CreateTwoRowQueryFilterPanel(
                QueryUiHelper.CreateFilterField("开始日期:", dtpStart, 132),
                QueryUiHelper.CreateFilterField("结束日期:", dtpEnd, 132),
                new[]
                {
                    QueryUiHelper.CreateFilterField("客 户:", cmbClient, 200),
                    QueryUiHelper.CreateFilterField("规 格:", cmbSpec, 160),
                    QueryUiHelper.CreateFilterField("库 位:", cmbLocation, 130),
                    QueryUiHelper.CreateFilterField("经手人:", cmbHandler, 130)
                },
                btnSearch,
                btnReset));

            // 数据表格（带滚动条）
            dataGridView = new DataGridView
            {
                Dock = DockStyle.Fill,
                AllowUserToAddRows = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                ScrollBars = ScrollBars.Both,
                AllowDrop = false,
                MultiSelect = true
            };
            QueryUiHelper.ApplyQueryGrid(dataGridView, QueryModule.Inbound, multiSelect: true, showRowHeaders: true);

            // 底部按钮面板
            Panel buttonPanel = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 58
            };
            QueryUiHelper.StyleToolbarPanel(buttonPanel);

            Button btnEdit = QueryUiHelper.CreateButton("编辑", QueryButtonRole.Primary);
            btnEdit.Location = new Point(10, 12);
            btnEdit.Click += BtnEdit_Click;
            buttonPanel.Controls.Add(btnEdit);

            Button btnSave = QueryUiHelper.CreateButton("保存", QueryButtonRole.Success);
            btnSave.Location = new Point(116, 12);
            btnSave.Click += BtnSave_Click;
            buttonPanel.Controls.Add(btnSave);

            Button btnCancel = QueryUiHelper.CreateButton("取消", QueryButtonRole.Warning);
            btnCancel.Location = new Point(222, 12);
            btnCancel.Click += BtnCancel_Click;
            buttonPanel.Controls.Add(btnCancel);

            Button btnDelete = QueryUiHelper.CreateButton("删除", QueryButtonRole.Danger);
            btnDelete.Location = new Point(328, 12);
            btnDelete.Click += BtnDelete_Click;
            buttonPanel.Controls.Add(btnDelete);

            Button btnExport = QueryUiHelper.CreateButton("导出报表", QueryButtonRole.Info);
            btnExport.Location = new Point(540, 12);
            btnExport.Click += BtnExport_Click;
            buttonPanel.Controls.Add(btnExport);

            Button btnPrint = QueryUiHelper.CreateButton("打印", QueryButtonRole.Info);
            btnPrint.Location = new Point(434, 12);
            btnPrint.Click += BtnPrint_Click;
            buttonPanel.Controls.Add(btnPrint);

            Button btnClose = QueryUiHelper.CreateButton("关闭", QueryButtonRole.Neutral);
            btnClose.Location = new Point(646, 12);
            btnClose.Click += (s, e) => this.Close();
            buttonPanel.Controls.Add(btnClose);

            Panel summaryPanel = new Panel
            {
                Dock = DockStyle.Top,
                Height = QueryUiHelper.FilterSummaryBarHeight
            };
            QueryUiHelper.StyleInfoBar(summaryPanel);
            lblFilterSummary = new Label { Text = QueryUiHelper.FormatFilterSummaryText(0) };
            QueryUiHelper.StyleFilterSummaryLabel(lblFilterSummary, QueryModule.Inbound);
            summaryPanel.Controls.Add(lblFilterSummary);

            Panel gridHost = QueryUiHelper.CreateGridHost(dataGridView);
            this.Controls.Add(gridHost);
            this.Controls.Add(summaryPanel);
            this.Controls.Add(searchPanel);
            this.Controls.Add(lblStatus);
            this.Controls.Add(buttonPanel);
        }

        private ComboBox CreateFilterComboBox(int x, int y, int width)
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

        private void LoadFilterData()
        {
            try
            {
                _clientItems = ClientSearchHelper.BuildItemsWithNone(dbManager.GetAllClients());
                ClientSearchHelper.BindSearchableCombo(cmbClient, _clientItems, FilterComboOptions);

                var specNames = new List<string>();
                DataTable schema = GetTableSchema("inbound_transactions");
                foreach (string candidate in new[] { "spec", "product_spec", "product_type_name" })
                {
                    string specColumn = FindColumnInSchema(schema, candidate);
                    if (specColumn == null)
                        continue;

                    string specQuery = $"SELECT DISTINCT {specColumn} FROM inbound_transactions WHERE {specColumn} IS NOT NULL AND {specColumn} != '' ORDER BY {specColumn} LIMIT 200";
                    DataTable specData = ExecuteQuery(specQuery);
                    if (specData == null)
                        continue;

                    foreach (DataRow row in specData.Rows)
                    {
                        string spec = row[0]?.ToString()?.Trim();
                        if (!string.IsNullOrEmpty(spec) && !specNames.Contains(spec))
                            specNames.Add(spec);
                    }
                }
                specNames.Sort(StringComparer.OrdinalIgnoreCase);

                _specItems = ClientSearchHelper.BuildSimpleItemsWithNone(specNames);
                ClientSearchHelper.BindSearchableCombo(cmbSpec, _specItems, FilterComboOptions);

                var handlerNames = new List<string>();
                foreach (DataRow row in dbManager.GetAllHandlers().Rows)
                {
                    string name = row["name"]?.ToString()?.Trim();
                    if (!string.IsNullOrEmpty(name) && !handlerNames.Contains(name))
                        handlerNames.Add(name);
                }
                _handlerItems = ClientSearchHelper.BuildSimpleItemsWithNone(handlerNames);
                ClientSearchHelper.BindSearchableCombo(cmbHandler, _handlerItems, FilterComboOptions);

                var locationNames = new List<string>();
                foreach (DataRow row in dbManager.GetActiveLocations().Rows)
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
                MessageBox.Show($"加载筛选数据失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private string FindColumnInSchema(DataTable schema, params string[] candidates)
        {
            if (schema == null)
                return null;

            foreach (string candidate in candidates)
            {
                foreach (DataRow row in schema.Rows)
                {
                    if (string.Equals(row["name"].ToString(), candidate, StringComparison.OrdinalIgnoreCase))
                        return row["name"].ToString();
                }
            }

            return null;
        }

        private static string EscapeSql(string value)
        {
            return value?.Replace("'", "''") ?? string.Empty;
        }

        // 修改LoadData方法，添加更详细的错误处理
        // 修改LoadData方法，添加更详细的错误处理和字段兼容性
        private void LoadData()
        {
            try
            {
                TryMaximizeIfStandalone();
                Cursor.Current = Cursors.WaitCursor;
                lblStatus.Text = "正在查询入库记录...";

                // === 添加重要调试信息 ===
                Console.WriteLine("=== 入库查询调试信息 ===");

                // 直接查询，不使用字段别名
                string whereClause = BuildWhereClause();
                string orderByClause = "ORDER BY id DESC";

                string countSql = "SELECT COUNT(*) as total FROM inbound_transactions";
                if (!string.IsNullOrEmpty(whereClause))
                    countSql += " WHERE " + whereClause;

                DataTable countData = ExecuteQuery(countSql);
                filteredRecordCount = 0;
                if (countData != null && countData.Rows.Count > 0)
                    filteredRecordCount = Convert.ToInt32(countData.Rows[0]["total"]);

                // 简单的查询语句
                string sql = $"SELECT * FROM inbound_transactions";
                if (!string.IsNullOrEmpty(whereClause))
                {
                    sql += " WHERE " + whereClause;
                }
                sql += " " + orderByClause;
                sql += " LIMIT 500";

                Console.WriteLine($">>> 执行的SQL: {sql}");

                // 执行查询
                currentDataTable = ExecuteQuery(sql);

                if (currentDataTable != null && currentDataTable.Rows.Count > 0)
                {
                    Console.WriteLine($">>> 查询成功，返回 {currentDataTable.Rows.Count} 条记录");

                    // 重要：显示所有记录的详细信息
                    Console.WriteLine($">>> 所有记录详情:");
                    for (int i = 0; i < currentDataTable.Rows.Count; i++)
                    {
                        DataRow row = currentDataTable.Rows[i];
                        Console.WriteLine($"--- 记录{i + 1} ---");
                        foreach (DataColumn col in currentDataTable.Columns)
                        {
                            Console.WriteLine($"    {col.ColumnName}: {row[col]}");
                        }

                        // 特别检查单号 RK202602087607
                        string orderNo = row["order_no"]?.ToString();
                        string spec = row["spec"]?.ToString();
                        if (orderNo == "RK202602087607")
                        {
                            Console.WriteLine($"    ⭐ 这是我们要找的单号: {spec}");
                        }
                    }

                    // 创建显示用的DataTable
                    DataTable displayTable = CreateDisplayTable(currentDataTable);

                    dataGridView.DataSource = displayTable;
                    lblStatus.Text = $"入库记录 | 记录数: {currentDataTable.Rows.Count} | 时间: {DateTime.Now:HH:mm:ss}";

                    // 设置列格式
                    FormatDataGridViewColumns();

                    // 退出编辑模式（如果有）
                    ExitEditMode();

                }
                else
                {
                    Console.WriteLine($">>> 查询返回空数据");
                    currentDataTable = currentDataTable ?? new DataTable();
                    ShowNoDataMessage("没有找到符合条件的入库记录");
                }

                UpdateFilterSummary();

                Console.WriteLine("=== 调试信息：入库查询结束 ===");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"查询失败: {ex.Message}\n\n堆栈跟踪: {ex.StackTrace}",
                    "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                ShowNoDataMessage($"查询失败: {ex.Message}");
            }
            finally
            {
                Cursor.Current = Cursors.Default;
            }
        }

        // 新增方法：构建带有字段别名的SELECT语句
        private string BuildSelectFieldsWithAliases(DataTable schema)
        {
            if (schema == null || schema.Rows.Count == 0)
                return "*";

            List<string> fields = new List<string>();

            foreach (DataRow row in schema.Rows)
            {
                string fieldName = row["name"].ToString();
                string type = row["type"].ToString().ToUpper();

                // 处理字段别名，解决同步写入和查询之间的字段名不匹配
                string fieldAlias = fieldName;

                // 同步可能使用的字段名 -> 查询期望的字段名
                if (fieldName.ToLower() == "location_code")
                {
                    fieldAlias = "location_code as location"; // 同步用location_code，查询用location
                }
                else if (fieldName.ToLower() == "product_spec")
                {
                    fieldAlias = "product_spec as spec"; // 同步用product_spec，查询用spec
                }
                else if (fieldName.ToLower() == "order_no")
                {
                    // 保持不变
                }
                else if (fieldName.ToLower() == "client_name")
                {
                    // 保持不变
                }
                else if (fieldName.ToLower() == "client_code")
                {
                    // 保持不变
                }

                fields.Add(fieldAlias);
            }

            return string.Join(", ", fields);
        }



        // 新增方法：简单加载数据（无搜索条件）
        private void LoadSimpleData()
        {
            try
            {
                string sql = "SELECT * FROM inbound_transactions ORDER BY id DESC LIMIT 500";
                currentDataTable = ExecuteQuery(sql);

                if (currentDataTable != null && currentDataTable.Rows.Count > 0)
                {
                    DataTable displayTable = CreateDisplayTable(currentDataTable);
                    dataGridView.DataSource = displayTable;
                    lblStatus.Text = $"入库记录 | 记录数: {currentDataTable.Rows.Count}";

                    FormatDataGridViewColumns();
                }
                else
                {
                    ShowNoDataMessage("表中没有数据");
                }
            }
            catch (Exception ex)
            {
                ShowNoDataMessage($"加载失败: {ex.Message}");
            }
        }


        // 添加获取表结构的方法
        private DataTable GetTableSchema(string tableName)
        {
            try
            {
                using (SQLiteConnection connection = new SQLiteConnection(GetActiveConnectionString()))
                {
                    connection.Open();

                    string sql = $"PRAGMA table_info({tableName})";

                    using (SQLiteCommand command = new SQLiteCommand(sql, connection))
                    using (SQLiteDataAdapter adapter = new SQLiteDataAdapter(command))
                    {
                        DataTable schema = new DataTable();
                        adapter.Fill(schema);
                        return schema;
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"获取表结构失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return null;
            }
        }

        // 修改查找日期字段的方法
        private string FindDateColumn(DataTable schema)
        {
            // 尝试可能的日期字段名
            string[] possibleDateColumns = { "date", "transaction_date", "inbound_date", "created_at", "updated_at", "time" };

            foreach (DataRow row in schema.Rows)
            {
                string columnName = row["name"].ToString().ToLower();

                foreach (string possibleColumn in possibleDateColumns)
                {
                    if (columnName.Contains(possibleColumn))
                    {
                        return row["name"].ToString(); // 返回原始字段名
                    }
                }
            }

            // 如果没有找到日期字段，返回第一个字段
            if (schema.Rows.Count > 0)
            {
                return schema.Rows[0]["name"].ToString();
            }

            return "";
        }

        
        // 修改BuildWhereClause方法，先检查字段是否存在
        private string BuildWhereClause()
        {
            List<string> conditions = new List<string>();

            // 首先获取表结构，找到实际存在的字段
            DataTable schema = GetTableSchema("inbound_transactions");
            if (schema == null)
                return "";

            // 查找日期字段
            string dateColumn = FindDateColumn(schema);

            // 客户筛选
            if (ClientSearchHelper.TryGetSelectedItem(cmbClient, _clientItems, out ClientSearchItem clientFilter, allowEmptySelection: true)
                && clientFilter != null)
            {
                string clientCodeColumn = FindColumnInSchema(schema, "client_code");
                string clientNameColumn = FindColumnInSchema(schema, "client_name", "customer_name");
                string clientCondition = ClientSearchHelper.BuildClientCondition(
                    clientFilter, clientCodeColumn, clientNameColumn, EscapeSql);
                if (!string.IsNullOrEmpty(clientCondition))
                    conditions.Add(clientCondition);
            }

            // 规格筛选
            if (ClientSearchHelper.TryGetSelectedItem(cmbSpec, _specItems, out ClientSearchItem specFilter, allowEmptySelection: true)
                && specFilter != null)
            {
                string specColumn = FindColumnInSchema(schema, "spec", "product_spec", "product_type_name");
                string specCondition = ClientSearchHelper.BuildFieldCondition(specFilter, specColumn, EscapeSql);
                if (!string.IsNullOrEmpty(specCondition))
                    conditions.Add(specCondition);
            }

            // 日期条件（只有在找到了日期字段时才添加）
            if (!string.IsNullOrEmpty(dateColumn))
            {
                DateTime startDate = dtpStart.Value.Date;
                DateTime endDate = dtpEnd.Value.Date.AddDays(1).AddSeconds(-1);

                // 简单的日期格式
                conditions.Add($"{dateColumn} >= '{startDate:yyyy-MM-dd}'");
                conditions.Add($"{dateColumn} <= '{endDate:yyyy-MM-dd 23:59:59}'");
            }

            // 经手人筛选
            if (ClientSearchHelper.TryGetSelectedItem(cmbHandler, _handlerItems, out ClientSearchItem handlerFilter, allowEmptySelection: true)
                && handlerFilter != null)
            {
                string handlerColumn = FindColumnInSchema(schema, "handler", "handler_name", "operator");
                string handlerCondition = ClientSearchHelper.BuildFieldCondition(handlerFilter, handlerColumn, EscapeSql);
                if (!string.IsNullOrEmpty(handlerCondition))
                    conditions.Add(handlerCondition);
            }

            // 库位筛选
            if (ClientSearchHelper.TryGetSelectedItem(cmbLocation, _locationItems, out ClientSearchItem locationFilter, allowEmptySelection: true)
                && locationFilter != null)
            {
                string locationColumn = FindColumnInSchema(schema, "location", "location_name", "warehouse");
                string locationCondition = ClientSearchHelper.BuildFieldCondition(locationFilter, locationColumn, EscapeSql);
                if (!string.IsNullOrEmpty(locationCondition))
                    conditions.Add(locationCondition);
            }

            return conditions.Count > 0 ? string.Join(" AND ", conditions) : "";
        }

        // 新增方法：查找实际存在的文本字段
        private List<string> FindExistingTextColumns(DataTable schema)
        {
            List<string> textColumns = new List<string>();

            // 优先搜索这些字段（如果存在）- 扩展搜索字段列表
            string[] preferredColumns = {
        "client_name", "customer_name", "client", "customer",
        "product_type_name", "product_name", "model", "type",
        "bill_no", "order_no", "serial_no",
        "handler_name", "handler", "operator",
        "location_name", "warehouse", "location",
        "remarks", "note", "description",
        // 添加规格和编码相关字段
        "spec", "product_spec", "specification",
        "code", "client_code", "product_code", "item_code", "batch_no"
    };

            // 首先添加存在的优先字段
            foreach (string preferred in preferredColumns)
            {
                foreach (DataRow row in schema.Rows)
                {
                    string columnName = row["name"].ToString();
                    if (columnName.ToLower() == preferred.ToLower())
                    {
                        if (!textColumns.Contains(columnName))
                        {
                            textColumns.Add(columnName);
                        }
                        break;
                    }
                }
            }

            // 如果没有找到优先字段，添加所有文本字段
            if (textColumns.Count == 0)
            {
                foreach (DataRow row in schema.Rows)
                {
                    string columnName = row["name"].ToString();
                    string type = row["type"].ToString().ToUpper();

                    // 检查是否为文本类型
                    if (type.Contains("TEXT") || type.Contains("CHAR") || type.Contains("VARCHAR"))
                    {
                        textColumns.Add(columnName);
                    }
                }
            }

            return textColumns;
        }


        private DataTable CreateDisplayTable(DataTable sourceTable)
        {
            DataTable displayTable = new DataTable();

            // 添加行号列
            displayTable.Columns.Add("行号", typeof(int));

            // 记录哪些字段被汉化了
            List<string> mappedColumns = new List<string>();
            List<string> unmappedColumns = new List<string>();

            // 添加其他列（使用中文列名）
            foreach (DataColumn column in sourceTable.Columns)
            {
                string columnName = column.ColumnName;
                string displayName = GetChineseColumnName(columnName);

                // 记录映射情况
                if (displayName != columnName)
                {
                    mappedColumns.Add($"{columnName} -> {displayName}");
                }
                else
                {
                    unmappedColumns.Add(columnName);
                }

                displayTable.Columns.Add(displayName, column.DataType);
            }

            // 显示映射信息到状态栏（调试用）
            if (unmappedColumns.Count > 0)
            {
                lblStatus.Text = $"未汉化字段: {string.Join(", ", unmappedColumns)}";
            }

            // 填充数据
            for (int i = 0; i < sourceTable.Rows.Count; i++)
            {
                DataRow sourceRow = sourceTable.Rows[i];
                DataRow displayRow = displayTable.NewRow();

                // 行号
                displayRow["行号"] = i + 1;

                // 其他数据
                foreach (DataColumn column in sourceTable.Columns)
                {
                    string displayName = GetChineseColumnName(column.ColumnName);
                    displayRow[displayName] = sourceRow[column];
                }

                displayTable.Rows.Add(displayRow);
            }

            return displayTable;
        }

        // 修改GetChineseColumnName方法，添加通用映射和智能识别
        private string GetChineseColumnName(string englishColumnName)
        {
            string lowerColumnName = englishColumnName.ToLower();

            // 首先检查预定义的映射
            if (columnChineseNames.ContainsKey(lowerColumnName))
            {
                return columnChineseNames[lowerColumnName];
            }

            // 智能识别通用字段
            Dictionary<string, string> commonMappings = new Dictionary<string, string>
    {
        { "id", "ID" },
        { "date", "日期" },
        { "time", "时间" },
        { "name", "名称" },
        { "code", "编码" },
        { "no", "编号" },
        { "type", "类型" },
        { "price", "价格" },
        { "amount", "金额" },
        { "total", "总计" },
        { "quantity", "数量" },
        { "weight", "重量" },
        { "status", "状态" },
        { "remark", "备注" },
        { "note", "备注" },
        { "description", "描述" },
        { "created", "创建时间" },
        { "updated", "更新时间" },
        { "modified", "修改时间" },
        { "operator", "操作员" },
        { "handler", "经手人" },
        { "customer", "客户" },
        { "client", "客户" },
        { "supplier", "供应商" },
        { "warehouse", "仓库" },
        { "location", "位置" },
        { "address", "地址" },
        { "phone", "电话" },
        { "mobile", "手机" },
        { "email", "邮箱" },
        { "contact", "联系人" },
        { "unit", "单位" },
        { "category", "分类" },
        { "brand", "品牌" },
        { "model", "型号" },
        { "spec", "规格" },
        { "color", "颜色" },
        { "size", "尺寸" }
    };

            // 检查完整匹配
            foreach (var mapping in commonMappings)
            {
                if (lowerColumnName == mapping.Key)
                {
                    return mapping.Value;
                }
            }

            // 检查包含关系（例如：client_name包含client）
            foreach (var mapping in commonMappings)
            {
                if (lowerColumnName.Contains(mapping.Key) && mapping.Key.Length > 2)
                {
                    // 处理组合字段，如client_name -> 客户名称
                    string baseName = mapping.Value;
                    string suffix = "";

                    // 提取后缀
                    if (lowerColumnName.Contains("_name") || lowerColumnName.Contains("name_"))
                    {
                        suffix = "名称";
                    }
                    else if (lowerColumnName.Contains("_code") || lowerColumnName.Contains("code_"))
                    {
                        suffix = "编码";
                    }
                    else if (lowerColumnName.Contains("_id") || lowerColumnName.Contains("id_"))
                    {
                        suffix = "ID";
                    }
                    else if (lowerColumnName.Contains("_date") || lowerColumnName.Contains("date_"))
                    {
                        suffix = "日期";
                    }
                    else if (lowerColumnName.Contains("_time") || lowerColumnName.Contains("time_"))
                    {
                        suffix = "时间";
                    }
                    else if (lowerColumnName.Contains("_price") || lowerColumnName.Contains("price_"))
                    {
                        suffix = "价格";
                    }
                    else if (lowerColumnName.Contains("_amount") || lowerColumnName.Contains("amount_"))
                    {
                        suffix = "金额";
                    }
                    else if (lowerColumnName.Contains("_quantity") || lowerColumnName.Contains("quantity_"))
                    {
                        suffix = "数量";
                    }

                    if (!string.IsNullOrEmpty(suffix) && !baseName.Contains(suffix))
                    {
                        return baseName + suffix;
                    }
                    else
                    {
                        return baseName;
                    }
                }
            }

            // 如果都没有匹配，尝试美化显示（去掉下划线，首字母大写）
            string[] parts = englishColumnName.Split('_');
            StringBuilder result = new StringBuilder();

            foreach (string part in parts)
            {
                if (part.Length > 0)
                {
                    result.Append(char.ToUpper(part[0]));
                    if (part.Length > 1)
                    {
                        result.Append(part.Substring(1));
                    }
                    result.Append(" ");
                }
            }

            return result.ToString().Trim();
        }


        private DataTable ExecuteQuery(string sql)
        {
            using (SQLiteConnection connection = new SQLiteConnection(GetActiveConnectionString()))
            {
                connection.Open();

                using (SQLiteCommand command = new SQLiteCommand(sql, connection))
                using (SQLiteDataAdapter adapter = new SQLiteDataAdapter(command))
                {
                    DataTable dataTable = new DataTable();
                    adapter.Fill(dataTable);
                    return dataTable;
                }
            }
        }

        private void FormatDataGridViewColumns()
        {
            if (dataGridView.Columns.Count == 0) return;

            dataGridView.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;

            // 隐藏不需要的列
            string[] hiddenColumns = { "来源设备", "来源记录ID", "单价", "总金额" };
            foreach (string colName in hiddenColumns)
            {
                if (dataGridView.Columns.Contains(colName))
                {
                    dataGridView.Columns[colName].Visible = false;
                }
            }

            foreach (DataGridViewColumn column in dataGridView.Columns)
            {
                if (!column.Visible) continue;

                column.MinimumWidth = 50;
                string header = column.HeaderText;

                if (header == "行号" || header == "ID")
                {
                    column.FillWeight = 60;
                }
                else if (header == "入库日期" || header == "创建时间" || header == "更新时间")
                {
                    column.FillWeight = 130;
                    column.DefaultCellStyle.Format = "yyyy-MM-dd HH:mm";
                }
                else if (header == "数量")
                {
                    column.FillWeight = 100;
                    column.MinimumWidth = 72;
                    column.DefaultCellStyle.Format = "N2";
                }
                else if (header == "单据号")
                {
                    column.FillWeight = 105;
                }
                else if (header == "经手人" || header == "客户编码" || header == "创建人")
                {
                    column.FillWeight = 75;
                }
                else if (header == "客户名称" || header == "库位名称")
                {
                    column.FillWeight = 88;
                }
                else if (header == "商品型号")
                {
                    column.FillWeight = 115;
                }
                else if (header == "备注")
                {
                    column.FillWeight = 120;
                }
                else
                {
                    column.FillWeight = 100;
                }

                if (header == "ID")
                {
                    column.DefaultCellStyle.BackColor = Color.LightGray;
                }
            }

            QueryUiHelper.ApplyUniformColumnAlignment(dataGridView);
        }

        private void ShowNoDataMessage(string message)
        {
            dataGridView.DataSource = null;
            dataGridView.Rows.Clear();
            dataGridView.Columns.Clear();

            dataGridView.Columns.Add("提示", "提示");
            dataGridView.Columns["提示"].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;

            int rowIndex = dataGridView.Rows.Add();
            dataGridView.Rows[rowIndex].Cells["提示"].Value = message;
            dataGridView.Rows[rowIndex].Cells["提示"].Style.ForeColor = Color.Gray;
            dataGridView.Rows[rowIndex].Cells["提示"].Style.Font = QueryUiHelper.QueryGridCellFont;
            dataGridView.Rows[rowIndex].Cells["提示"].Style.Alignment = DataGridViewContentAlignment.MiddleCenter;

            lblStatus.Text = message;
            currentDataTable = null;
            filteredRecordCount = 0;
            UpdateFilterSummary();
        }

        private void UpdateFilterSummary()
        {
            QueryUiHelper.SumQuantityAndAmount(currentDataTable, out decimal pageQuantity, out decimal pageAmount);
            QueryUiHelper.UpdateFilterSummaryLabel(lblFilterSummary, filteredRecordCount, pageQuantity, pageAmount);
        }

        // ========== 编辑功能 ==========

        private void BtnEdit_Click(object sender, EventArgs e)
        {
            if (dataGridView.Rows.Count == 0 || currentDataTable == null)
            {
                MessageBox.Show("没有数据可以编辑", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            isEditMode = true;

            dataGridView.SuspendLayout();
            try
            {
                dataGridView.ReadOnly = false;
                dataGridView.AllowUserToAddRows = false;
                dataGridView.AllowUserToDeleteRows = false;

                foreach (DataGridViewColumn column in dataGridView.Columns)
                {
                    column.ReadOnly = !EditableInboundColumns.Contains(column.HeaderText);
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
            }
            finally
            {
                dataGridView.ResumeLayout();
            }

            lblStatus.Text = "编辑模式已启用 - 可编辑列已高亮，修改后请点击【保存】";
        }

        private void BtnSave_Click(object sender, EventArgs e)
        {
            if (!isEditMode || currentDataTable == null)
            {
                MessageBox.Show("当前不是编辑模式", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            try
            {
                // 确保结束编辑状态
                dataGridView.EndEdit();

                // 获取当前DataGridView绑定的数据源
                DataTable displayTable = dataGridView.DataSource as DataTable;
                if (displayTable == null)
                {
                    MessageBox.Show("无法获取数据源", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                // 直接收集用户修改的内容 - 不使用版本跟踪
                List<ModifiedRowInfo> modifiedRows = new List<ModifiedRowInfo>();

                // 遍历所有行，找出被修改的行
                for (int i = 0; i < displayTable.Rows.Count; i++)
                {
                    DataRow displayRow = displayTable.Rows[i];

                    // 跳过空行
                    if (i >= currentDataTable.Rows.Count) continue;

                    DataRow originalRow = currentDataTable.Rows[i];
                    Dictionary<string, object> changes = new Dictionary<string, object>();

                    // 检查每一列是否有修改
                    foreach (DataColumn col in displayTable.Columns)
                    {
                        if (col.ColumnName == "行号") continue;

                        string englishColName = GetEnglishColumnName(col.ColumnName);
                        if (string.IsNullOrEmpty(englishColName)) continue;

                        // 确保原始数据表包含该列
                        if (!currentDataTable.Columns.Contains(englishColName)) continue;

                        object currentValue = displayRow[col.ColumnName];
                        object originalValue = originalRow[englishColName];

                        // 比较值是否不同（处理DBNull）
                        bool isDifferent = false;

                        if (currentValue == DBNull.Value && originalValue == DBNull.Value)
                        {
                            isDifferent = false;
                        }
                        else if (currentValue == DBNull.Value || originalValue == DBNull.Value)
                        {
                            isDifferent = true;
                        }
                        else
                        {
                            isDifferent = !currentValue.Equals(originalValue);
                        }

                        if (isDifferent)
                        {
                            changes[englishColName] = currentValue == DBNull.Value ? null : currentValue;
                        }
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
                    MessageBox.Show("没有检测到任何修改", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                // 确认保存
                DialogResult result = MessageBox.Show($"检测到 {modifiedRows.Count} 条记录有修改，确定要保存吗？\n\n修改内容：\n{GetModifiedSummary(modifiedRows, displayTable)}",
                    "确认保存", MessageBoxButtons.OKCancel, MessageBoxIcon.Question);

                if (result != DialogResult.OK)
                    return;

                Cursor.Current = Cursors.WaitCursor;

                // 保存修改
                int savedCount = 0;
                List<string> errors = new List<string>();

                foreach (var modifiedRow in modifiedRows)
                {
                    try
                    {
                        if (modifiedRow.Changes.Count == 0) continue;

                        // 构建更新语句
                        List<string> setClauses = new List<string>();
                        List<SQLiteParameter> parameters = new List<SQLiteParameter>();

                        foreach (var change in modifiedRow.Changes)
                        {
                            setClauses.Add($"[{change.Key}] = @{change.Key}");
                            parameters.Add(new SQLiteParameter($"@{change.Key}", change.Value ?? DBNull.Value));
                        }

                        // 添加更新时间
                        setClauses.Add("[updated_at] = @updated_at");
                        parameters.Add(new SQLiteParameter("@updated_at", DateTime.Now));

                        // 添加ID参数
                        parameters.Add(new SQLiteParameter("@id", modifiedRow.Id));

                        // 执行更新
                        string sql = $"UPDATE inbound_transactions SET {string.Join(", ", setClauses)} WHERE id = @id";

                        Console.WriteLine($"执行SQL: {sql}");
                        Console.WriteLine($"参数: ID={modifiedRow.Id}, 修改字段={string.Join(", ", modifiedRow.Changes.Keys)}");

                        int rowsAffected = ExecuteNonQuery(sql, parameters.ToArray());

                        if (rowsAffected > 0)
                        {
                            savedCount++;

                            // 更新原始数据表中的值
                            DataRow originalRow = currentDataTable.Rows[modifiedRow.RowIndex];
                            foreach (var change in modifiedRow.Changes)
                            {
                                if (originalRow.Table.Columns.Contains(change.Key))
                                {
                                    originalRow[change.Key] = change.Value ?? DBNull.Value;
                                }
                            }
                            originalRow["updated_at"] = DateTime.Now;
                        }
                        else
                        {
                            errors.Add($"第{modifiedRow.RowIndex + 1}行(ID={modifiedRow.Id})：更新失败");
                        }
                    }
                    catch (Exception ex)
                    {
                        errors.Add($"第{modifiedRow.RowIndex + 1}行(ID={modifiedRow.Id})：{ex.Message}");
                        Console.WriteLine($"保存行{modifiedRow.RowIndex + 1}时出错: {ex}");
                    }
                }

                // 退出编辑模式
                ExitEditMode();

                // 重新加载数据以显示最新状态
                LoadData();

                // 显示结果
                string message = $"保存成功！共保存了 {savedCount} 条记录。";
                if (errors.Count > 0)
                {
                    message += $"\n\n错误信息：\n{string.Join("\n", errors)}";
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
                MessageBox.Show($"保存失败: {ex.Message}\n\n堆栈跟踪: {ex.StackTrace}",
                    "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                Cursor.Current = Cursors.Default;
            }
        }
        /// <summary>
        /// 修改行信息类
        /// </summary>
        private class ModifiedRowInfo
        {
            public int RowIndex { get; set; }
            public int Id { get; set; }
            public Dictionary<string, object> Changes { get; set; }
        }
        /// <summary>
        /// 获取修改内容的摘要
        /// </summary>
        private string GetModifiedSummary(List<ModifiedRowInfo> modifiedRows, DataTable displayTable)
        {
            StringBuilder sb = new StringBuilder();

            foreach (var row in modifiedRows)
            {
                sb.AppendLine($"第{row.RowIndex + 1}行 (ID:{row.Id}):");

                // 获取中文列名显示
                Dictionary<string, string> chineseChanges = new Dictionary<string, string>();
                foreach (var change in row.Changes)
                {
                    // 找到对应的中文列名
                    string chineseName = change.Key;
                    foreach (DataColumn col in displayTable.Columns)
                    {
                        if (GetEnglishColumnName(col.ColumnName) == change.Key)
                        {
                            chineseName = col.ColumnName;
                            break;
                        }
                    }
                    sb.AppendLine($"  - {chineseName}: {change.Value}");
                }
            }

            return sb.ToString();
        }

        /// <summary>
        /// 改进的英文列名查找
        /// </summary>
        private string GetEnglishColumnName(string chineseColumnName)
        {
            if (string.IsNullOrEmpty(chineseColumnName)) return null;

            // 先处理特殊情况
            if (chineseColumnName == "行号") return null;

            // 在映射字典中查找
            foreach (var kvp in columnChineseNames)
            {
                if (kvp.Value == chineseColumnName)
                    return kvp.Key;
            }

            // 如果找不到，尝试常见的映射关系（根据实际数据库字段）
            var commonMappings = new Dictionary<string, string>
    {
        { "客户名称", "client_name" },
        { "商品型号", "spec" },
        { "数量", "quantity" },
        { "单价", "unit_price" },
        { "总金额", "total_amount" },
        { "库位名称", "location" },
        { "经手人", "handler" },
        { "单据号", "order_no" },
        { "入库日期", "date" },
        { "客户编码", "client_code" },
        { "创建人", "creator" },
        { "创建时间", "created_time" },
        { "更新时间", "updated_at" }
    };

            if (commonMappings.ContainsKey(chineseColumnName))
                return commonMappings[chineseColumnName];

            // 最后尝试根据常见规则转换
            string result = chineseColumnName
                .Replace(" ", "_")
                .Replace("名称", "_name")
                .Replace("编码", "_code")
                .Replace("日期", "_date")
                .Replace("时间", "_time")
                .ToLower();

            return result;
        }
        

        private int ExecuteNonQuery(string sql, params SQLiteParameter[] parameters)
        {
            using (SQLiteConnection connection = new SQLiteConnection(GetActiveConnectionString()))
            {
                connection.Open();

                using (SQLiteCommand command = new SQLiteCommand(sql, connection))
                {
                    if (parameters != null)
                    {
                        command.Parameters.AddRange(parameters);
                    }

                    return command.ExecuteNonQuery();
                }
            }
        }

        private void BtnCancel_Click(object sender, EventArgs e)
        {
            if (isEditMode)
            {
                DialogResult result = MessageBox.Show("确定要取消编辑吗？所有未保存的修改将丢失。",
                    "确认取消", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning);

                if (result == DialogResult.OK)
                {
                    ExitEditMode();
                    LoadData(); // 重新加载原始数据
                }
            }
        }

        private void ExitEditMode()
        {
            isEditMode = false;
            dataGridView.ReadOnly = true;

            foreach (DataGridViewColumn col in dataGridView.Columns)
            {
                col.HeaderCell.Style.BackColor = Color.FromArgb(142, 68, 173);
                col.HeaderCell.Style.ForeColor = Color.White;
            }

            lblStatus.Text = "编辑模式已退出";
        }

        private void BtnDelete_Click(object sender, EventArgs e)
        {
            if (dataGridView.SelectedRows.Count == 0 || currentDataTable == null)
            {
                MessageBox.Show("请先选择要删除的行", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            DialogResult result = MessageBox.Show($"确定要删除选中的 {dataGridView.SelectedRows.Count} 条记录吗？此操作不可恢复！",
                "确认删除", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning);

            if (result != DialogResult.OK)
                return;

            try
            {
                Cursor.Current = Cursors.WaitCursor;

                int deletedCount = 0;
                foreach (DataGridViewRow gridRow in dataGridView.SelectedRows)
                {
                    if (gridRow.IsNewRow) continue;

                    int rowIndex = gridRow.Index;
                    if (rowIndex >= currentDataTable.Rows.Count)
                        continue;

                    DataRow originalRow = currentDataTable.Rows[rowIndex];
                    object idValue = originalRow["id"];

                    if (idValue != null && idValue != DBNull.Value)
                    {
                        string sql = $"DELETE FROM inbound_transactions WHERE id = @id";
                        if (ExecuteNonQuery(sql, new SQLiteParameter("@id", idValue)) > 0)
                        {
                            deletedCount++;
                        }
                    }
                }

                MessageBox.Show($"删除成功！共删除了 {deletedCount} 条记录。", "删除成功",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);

                LoadData(); // 重新加载数据
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
            if (currentDataTable == null || currentDataTable.Rows.Count == 0)
            {
                MessageBox.Show("没有数据可以导出", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }

            const QueryReportKind kind = QueryReportKind.Inbound;
            ClientSearchHelper.TryGetSelectedItem(cmbClient, _clientItems, out ClientSearchItem clientFilter, allowEmptySelection: true);
            string clientName = QueryReportExportHelper.ResolveExportClientName(clientFilter);
            context = QueryReportExportHelper.BuildQueryReportContext(
                currentDataTable,
                kind,
                clientName,
                dtpStart.Value.Date,
                dtpEnd.Value.Date);
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
                MessageBox.Show($"打印失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
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

            const QueryReportKind kind = QueryReportKind.Inbound;
            ClientSearchHelper.TryGetSelectedItem(cmbClient, _clientItems, out ClientSearchItem clientFilter, allowEmptySelection: true);
            string clientName = QueryReportExportHelper.ResolveExportClientName(clientFilter);

            SaveFileDialog sfd = new SaveFileDialog
            {
                Filter = "PDF文件 (*.pdf)|*.pdf",
                FileName = QueryReportExportHelper.BuildDefaultFileName(kind, clientName),
                DefaultExt = "pdf",
                AddExtension = true,
                Title = "导出入库记录"
            };

            if (sfd.ShowDialog() != DialogResult.OK)
                return;

            try
            {
                Cursor.Current = Cursors.WaitCursor;
                string path = sfd.FileName;
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
                MessageBox.Show($"导出失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                Cursor.Current = Cursors.Default;
            }
        }
    }
}