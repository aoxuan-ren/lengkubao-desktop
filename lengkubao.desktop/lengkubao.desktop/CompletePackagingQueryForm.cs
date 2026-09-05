// CompletePackagingQueryForm.cs - 修复参数顺序问题
using System;
using System.Drawing;
using System.Data;
using System.Windows.Forms;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace lengkubao.desktop
{
    public class CompletePackagingQueryForm : Form
    {
        private DataGridView dataGridView;
        private DatabaseManager db;
        private DataTable originalData;
        private DataTable displayData; // 用于显示的DataTable
        private string currentTableName = "packaging_transactions";

        private DateTimePicker dtpStart;
        private DateTimePicker dtpEnd;
        private ComboBox cmbClient;
        private ComboBox cmbPackDetail;
        private ComboBox cmbPackFlag;
        private ComboBox cmbHandler;
        private ComboBox cmbStatus;
        private List<ClientSearchItem> _clientItems = new List<ClientSearchItem>();
        private List<ClientSearchItem> _packDetailItems = new List<ClientSearchItem>();
        private List<ClientSearchItem> _packFlagItems = new List<ClientSearchItem>();
        private List<ClientSearchItem> _handlerItems = new List<ClientSearchItem>();
        private List<ClientSearchItem> _statusItems = new List<ClientSearchItem>();
        private Label lblStatus; // 上方的系统状态栏
        private Label lblFilterSummary;
        private int filteredRecordCount = 0;

        // 记录编辑状态
        private bool isEditMode = false;
        private Dictionary<string, string> columnMappings; // 显示列名到实际字段名的映射

        public CompletePackagingQueryForm()
        {
            InitializeComponents();
            FindAndLoadData();
        }

        private void InitializeComponents()
        {
            QueryUiHelper.ApplyFormChrome(this);
            this.Text = "包装记录查询";
            this.Size = new Size(1400, 720);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.FormBorderStyle = FormBorderStyle.Sizable;
            this.MaximizeBox = true;
            this.MinimizeBox = true;

            TableLayoutPanel mainLayout = new TableLayoutPanel();
            mainLayout.Dock = DockStyle.Fill;
            mainLayout.RowCount = 5;
            mainLayout.ColumnCount = 1;
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 124));
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, QueryUiHelper.FilterSummaryBarHeight));
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
            mainLayout.Padding = new Padding(0);
            mainLayout.BackColor = StatisticsUiHelper.PanelBack;
            this.Controls.Add(mainLayout);

            // 1. 系统状态栏（移到上方）
            Panel systemStatusPanel = CreateSystemStatusPanel();
            mainLayout.Controls.Add(systemStatusPanel, 0, 0);

            // 2. 搜索面板
            Panel searchPanel = CreateSearchPanel();
            mainLayout.Controls.Add(searchPanel, 0, 1);

            // 3. 筛选汇总栏
            Panel summaryPanel = new Panel { Dock = DockStyle.Fill };
            QueryUiHelper.StyleInfoBar(summaryPanel);
            lblFilterSummary = new Label { Text = QueryUiHelper.FormatFilterSummaryText(0) };
            QueryUiHelper.StyleFilterSummaryLabel(lblFilterSummary, QueryModule.Packaging);
            summaryPanel.Controls.Add(lblFilterSummary);
            mainLayout.Controls.Add(summaryPanel, 0, 2);

            // 4. 数据表格面板
            Panel gridPanel = CreateGridPanel();
            mainLayout.Controls.Add(gridPanel, 0, 3);

            // 5. 按钮面板
            Panel buttonPanel = CreateButtonPanel();
            mainLayout.Controls.Add(buttonPanel, 0, 4);
        }

        private Panel CreateSystemStatusPanel()
        {
            Panel panel = QueryUiHelper.CreateModuleHeader(QueryModule.Packaging, "系统状态：就绪", out lblStatus);
            return panel;
        }

        private Panel CreateSearchPanel()
        {
            Panel panel = new Panel { Dock = DockStyle.Fill };
            QueryUiHelper.StyleFilterPanel(panel);

            dtpStart = new DateTimePicker { Width = 132 };
            dtpEnd = new DateTimePicker { Width = 132 };
            QueryUiHelper.StyleDatePicker(dtpStart);
            QueryUiHelper.StyleDatePicker(dtpEnd);
            DateRangeSettings.ApplyTo(dtpStart, dtpEnd);

            cmbStatus = CreateFilterComboBox(0, 0, 110);
            _statusItems = ClientSearchHelper.BuildSimpleItems(new[] { "全部", "处理中", "已结清" });
            _statusItems.Insert(1, ClientSearchHelper.CreateNoneFilterItem());
            ClientSearchHelper.BindSearchableCombo(cmbStatus, _statusItems, new ClientSearchHelper.SearchComboOptions
            {
                PlaceholderText = ClientSearchHelper.NamePlaceholderText,
                AllowEmptySelection = true
            });

            cmbClient = CreateFilterComboBox(0, 0, 210);
            cmbPackDetail = CreateFilterComboBox(0, 0, 150);
            cmbPackFlag = CreateFilterComboBox(0, 0, 120);
            cmbHandler = CreateFilterComboBox(0, 0, 120);

            Button btnSearch = QueryUiHelper.CreateButton("搜索", QueryButtonRole.Primary, QueryUiHelper.CompactButtonSize);
            btnSearch.Click += (s, e) =>
            {
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
                DateRangeSettings.ApplyFactoryDefaultTo(dtpStart, dtpEnd);
                ClientSearchHelper.ResetToPlaceholder(cmbClient);
                ClientSearchHelper.ResetToPlaceholder(cmbPackDetail);
                ClientSearchHelper.ResetToPlaceholder(cmbPackFlag);
                ClientSearchHelper.ResetToPlaceholder(cmbHandler);
                ClientSearchHelper.ResetToPlaceholder(cmbStatus);
                LoadData();
            };

            Button btnBatchPrice = QueryUiHelper.CreateButton("批量改价", QueryButtonRole.Secondary, QueryUiHelper.CompactButtonSize);
            btnBatchPrice.Click += BtnBatchPrice_Click;

            panel.Controls.Add(QueryUiHelper.CreateTwoRowQueryFilterPanel(
                QueryUiHelper.CreateFilterField("开始日期:", dtpStart, 132),
                QueryUiHelper.CreateFilterField("结束日期:", dtpEnd, 132),
                new[]
                {
                    QueryUiHelper.CreateFilterField("客 户:", cmbClient, 210),
                    QueryUiHelper.CreateFilterField("包装明细:", cmbPackDetail, 150),
                    QueryUiHelper.CreateFilterField("包装类型:", cmbPackFlag, 120),
                    QueryUiHelper.CreateFilterField("经手人:", cmbHandler, 120),
                    QueryUiHelper.CreateFilterField("状 态:", cmbStatus, 110)
                },
                btnSearch,
                btnReset,
                btnBatchPrice));

            return panel;
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

        private ClientSearchHelper.SearchComboOptions ClientFilterComboOptions =>
            new ClientSearchHelper.SearchComboOptions
            {
                PlaceholderText = ClientSearchHelper.PlaceholderText,
                AllowEmptySelection = true
            };

        private void LoadFilterData()
        {
            try
            {
                db = db ?? new DatabaseManager();

                _clientItems = ClientSearchHelper.BuildItemsWithNone(db.GetAllClients());
                ClientSearchHelper.BindSearchableCombo(cmbClient, _clientItems, ClientFilterComboOptions);

                var packDetailNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (string name in db.GetActivePackTypeNames())
                {
                    if (!string.IsNullOrWhiteSpace(name))
                        packDetailNames.Add(name.Trim());
                }

                if (!string.IsNullOrEmpty(currentTableName))
                {
                    DataTable packTypeData = db.ExecuteQuery(
                        $"SELECT DISTINCT pack_type FROM {currentTableName} WHERE pack_type IS NOT NULL AND pack_type != '' ORDER BY pack_type LIMIT 200");
                    if (packTypeData != null)
                    {
                        foreach (DataRow row in packTypeData.Rows)
                        {
                            string name = row["pack_type"]?.ToString()?.Trim();
                            if (!string.IsNullOrEmpty(name))
                                packDetailNames.Add(name);
                        }
                    }
                }

                var sortedPackDetails = packDetailNames.ToList();
                sortedPackDetails.Sort(StringComparer.OrdinalIgnoreCase);
                _packDetailItems = ClientSearchHelper.BuildSimpleItemsWithNone(sortedPackDetails);
                ClientSearchHelper.BindSearchableCombo(cmbPackDetail, _packDetailItems, FilterComboOptions);

                _packFlagItems = new List<ClientSearchItem>
                {
                    new ClientSearchItem
                    {
                        Code = "TAKE",
                        Name = "出包装",
                        DisplayText = "出包装",
                        Initials = PinyinHelper.GetInitials("出包装")
                    },
                    new ClientSearchItem
                    {
                        Code = "RETURN",
                        Name = "进包装",
                        DisplayText = "进包装",
                        Initials = PinyinHelper.GetInitials("进包装")
                    }
                };
                ClientSearchHelper.PrependNoneFilterItem(_packFlagItems);
                ClientSearchHelper.BindSearchableCombo(cmbPackFlag, _packFlagItems, FilterComboOptions);

                var handlerNames = new List<string>();
                foreach (DataRow row in db.GetAllHandlers().Rows)
                {
                    string name = row["name"]?.ToString()?.Trim();
                    if (!string.IsNullOrEmpty(name) && !handlerNames.Contains(name))
                        handlerNames.Add(name);
                }
                _handlerItems = ClientSearchHelper.BuildSimpleItemsWithNone(handlerNames);
                ClientSearchHelper.BindSearchableCombo(cmbHandler, _handlerItems, FilterComboOptions);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"加载筛选数据失败：{ex.Message}", "错误",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private Panel CreateGridPanel()
        {
            dataGridView = new DataGridView();
            dataGridView.CellEndEdit += DataGridView_CellEndEdit;
            dataGridView.CellFormatting += DataGridView_CellFormatting;
            QueryUiHelper.ApplyQueryGrid(dataGridView, QueryModule.Packaging, multiSelect: true);
            return QueryUiHelper.CreateGridHost(dataGridView);
        }

        private Panel CreateButtonPanel()
        {
            Panel panel = new Panel();
            panel.Dock = DockStyle.Fill;
            QueryUiHelper.StyleToolbarPanel(panel);

            int buttonX = 10;
            int buttonSpacing = 10;

            Button btnEdit = CreateButton("编辑", QueryButtonRole.Primary, buttonX, 12);
            btnEdit.Click += BtnEdit_Click;
            panel.Controls.Add(btnEdit);
            buttonX += btnEdit.Width + buttonSpacing;

            Button btnSettle = CreateButton("结清", QueryButtonRole.Success, buttonX, 12);
            btnSettle.Click += BtnSettle_Click;
            panel.Controls.Add(btnSettle);
            buttonX += btnSettle.Width + buttonSpacing;

            Button btnSave = CreateButton("保存", QueryButtonRole.Primary, buttonX, 12);
            btnSave.Click += BtnSave_Click;
            panel.Controls.Add(btnSave);
            buttonX += btnSave.Width + buttonSpacing;

            Button btnCancel = CreateButton("取消", QueryButtonRole.Warning, buttonX, 12);
            btnCancel.Click += BtnCancel_Click;
            panel.Controls.Add(btnCancel);
            buttonX += btnCancel.Width + buttonSpacing;

            Button btnDelete = CreateButton("删除", QueryButtonRole.Danger, buttonX, 12);
            btnDelete.Click += BtnDelete_Click;
            panel.Controls.Add(btnDelete);
            buttonX += btnDelete.Width + buttonSpacing;

            Button btnRefresh = CreateButton("刷新", QueryButtonRole.Secondary, buttonX, 12);
            btnRefresh.Click += (s, e) => LoadData();
            panel.Controls.Add(btnRefresh);
            buttonX += btnRefresh.Width + buttonSpacing;

            Button btnPrint = CreateButton("打印", QueryButtonRole.Info, buttonX, 12);
            btnPrint.Click += BtnPrint_Click;
            panel.Controls.Add(btnPrint);
            buttonX += btnPrint.Width + buttonSpacing;

            Button btnExport = CreateButton("导出报表", QueryButtonRole.Info, buttonX, 12);
            btnExport.Click += BtnExport_Click;
            panel.Controls.Add(btnExport);
            buttonX += btnExport.Width + buttonSpacing;

            Button btnClose = CreateButton("关闭", QueryButtonRole.Neutral, buttonX, 12);
            btnClose.Click += (s, e) => this.Close();
            panel.Controls.Add(btnClose);

            return panel;
        }

        private Button CreateButton(string text, QueryButtonRole role, int x, int y)
        {
            var button = QueryUiHelper.CreateButton(text, role);
            button.Location = new Point(x, y);
            return button;
        }

        private void FindAndLoadData()
        {
            try
            {
                Cursor.Current = Cursors.WaitCursor;
                lblStatus.Text = "系统状态：正在查找包装表...";

                // 初始化数据库连接
                db = new DatabaseManager();

                // 检查表是否存在
                string checkTableSql = "SELECT name FROM sqlite_master WHERE type='table' AND name='packaging_transactions'";
                DataTable tableCheck = db.ExecuteQuery(checkTableSql);

                if (tableCheck == null || tableCheck.Rows.Count == 0)
                {
                    lblStatus.Text = "系统状态：未找到包装表";
                    MessageBox.Show("未找到包装表 packaging_transactions！", "错误",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);

                    // 尝试创建表
                    if (MessageBox.Show("包装表不存在，是否尝试创建？", "确认",
                        MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                    {
                        CreatePackagingTable();
                        LoadData();
                    }
                    return;
                }

                lblStatus.Text = "系统状态：正在加载数据...";
                LoadFilterData();
                LoadData();
            }
            catch (Exception ex)
            {
                lblStatus.Text = "系统状态：初始化失败";
                MessageBox.Show($"初始化失败：{ex.Message}", "错误",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                Cursor.Current = Cursors.Default;
            }
        }

        // 创建包装表的SQL语句（如果需要）
        private void CreatePackagingTable()
        {
            try
            {
                string createTableSql = @"
            CREATE TABLE IF NOT EXISTS packaging_transactions (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                order_no TEXT NOT NULL,
                client_code TEXT,
                client_name TEXT,
                pack_type TEXT,
                quantity INTEGER,
                unit_price REAL,
                total_amount REAL,
                handler TEXT,
                creator TEXT,
                date TEXT,
                created_time DATETIME DEFAULT CURRENT_TIMESTAMP,
                is_settled INTEGER DEFAULT 0,
                settled_time DATETIME,
                settled_by TEXT,
                status TEXT DEFAULT '处理中',
                remarks TEXT
            )";

                int result = db.ExecuteNonQuery(createTableSql);
                if (result >= 0)
                {
                    lblStatus.Text = "系统状态：包装表已创建";
                    MessageBox.Show("包装表创建成功！", "成功",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"创建包装表失败：{ex.Message}", "错误",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        
        private void LoadData()
        {
            try
            {
                if (string.IsNullOrEmpty(currentTableName))
                {
                    FindAndLoadData();
                    return;
                }

                Cursor.Current = Cursors.WaitCursor;
                lblStatus.Text = "系统状态：正在查询数据...";

                db = new DatabaseManager();

                // 构建查询条件
                string whereClause = "1=1";
                var parameters = new Dictionary<string, object>();

                // 获取表结构
                DataTable schema = DatabaseHelper.GetTableSchema(currentTableName);

                // 查找日期字段
                string dateField = FindDateField(schema);

                // 添加日期条件
                if (!string.IsNullOrEmpty(dateField))
                {
                    whereClause += $" AND {dateField} >= @startDate";
                    parameters.Add("@startDate", dtpStart.Value.ToString("yyyy-MM-dd"));

                    whereClause += $" AND {dateField} <= @endDate";
                    parameters.Add("@endDate", dtpEnd.Value.ToString("yyyy-MM-dd") + " 23:59:59");
                }

                // 客户筛选
                if (ClientSearchHelper.TryGetSelectedItem(cmbClient, _clientItems, out ClientSearchItem clientFilter, allowEmptySelection: true)
                    && clientFilter != null
                    && !ClientSearchHelper.IsNoneFilter(clientFilter))
                {
                    var clientConditions = new List<string>();
                    ClientSearchHelper.AppendClientFilter(clientFilter, clientConditions, parameters);
                    foreach (string condition in clientConditions)
                        whereClause += " AND " + condition;
                }

                // 包装明细筛选
                if (ClientSearchHelper.TryGetSelectedItem(cmbPackDetail, _packDetailItems, out ClientSearchItem packDetailFilter, allowEmptySelection: true)
                    && packDetailFilter != null
                    && !ClientSearchHelper.IsNoneFilter(packDetailFilter))
                {
                    string packTypeField = FindColumnInSchema(schema, "pack_type", "package_type");
                    if (!string.IsNullOrEmpty(packTypeField))
                    {
                        var packDetailConditions = new List<string>();
                        ClientSearchHelper.AppendFieldFilter(
                            packDetailFilter, packTypeField, packDetailConditions, parameters, "@packType");
                        foreach (string condition in packDetailConditions)
                            whereClause += " AND " + condition;
                    }
                }

                // 包装类型筛选（出包装/进包装）
                if (ClientSearchHelper.TryGetSelectedItem(cmbPackFlag, _packFlagItems, out ClientSearchItem packFlagFilter, allowEmptySelection: true)
                    && packFlagFilter != null
                    && !ClientSearchHelper.IsNoneFilter(packFlagFilter))
                {
                    string packFlagField = FindColumnInSchema(schema, "pack_flag", "packaging_type_flag");
                    if (!string.IsNullOrEmpty(packFlagField))
                    {
                        whereClause += $" AND {packFlagField} = @packFlag";
                        parameters.Add("@packFlag", packFlagFilter.Code);
                    }
                }

                // 经手人筛选
                if (ClientSearchHelper.TryGetSelectedItem(cmbHandler, _handlerItems, out ClientSearchItem handlerFilter, allowEmptySelection: true)
                    && handlerFilter != null
                    && !ClientSearchHelper.IsNoneFilter(handlerFilter))
                {
                    string handlerField = FindColumnInSchema(schema, "handler", "handler_name", "operator");
                    if (!string.IsNullOrEmpty(handlerField))
                    {
                        var handlerConditions = new List<string>();
                        ClientSearchHelper.AppendFieldFilter(
                            handlerFilter, handlerField, handlerConditions, parameters, "@handler");
                        foreach (string condition in handlerConditions)
                            whereClause += " AND " + condition;
                    }
                }

                // 状态筛选
                if (ClientSearchHelper.TryGetSelectedItem(cmbStatus, _statusItems, out ClientSearchItem statusItem, allowEmptySelection: true)
                    && statusItem != null
                    && statusItem.Name != "全部"
                    && !ClientSearchHelper.IsNoneFilter(statusItem))
                {
                    string statusField = FindStatusField(schema);
                    if (!string.IsNullOrEmpty(statusField))
                    {
                        whereClause += $" AND {statusField} = @status";
                        parameters.Add("@status", statusItem.Name);
                    }
                }

                // ===== 【新增】先查询所有数据（不带条件）用于调试 =====
                string debugSql = $"SELECT * FROM {currentTableName} ORDER BY id DESC";
                DataTable allData = db.ExecuteQuery(debugSql);
                Console.WriteLine($"========== 包装表所有数据调试 ==========");
                Console.WriteLine($"总记录数: {allData.Rows.Count}");

                // 显示所有记录的详细信息
                foreach (DataRow row in allData.Rows)
                {
                    string id = row["id"]?.ToString() ?? "";
                    string orderNo = row["order_no"]?.ToString() ?? "";
                    string clientCode = row["client_code"]?.ToString() ?? "";
                    string clientName = row["client_name"]?.ToString() ?? "";
                    string date = row["date"]?.ToString() ?? "";
                    string amount = row["total_amount"]?.ToString() ?? "";
                    string packFlag = row.Table.Columns.Contains("pack_flag") ? (row["pack_flag"]?.ToString() ?? "NULL") : "字段不存在";

                    Console.WriteLine($"ID:{id}, 单号:{orderNo}, 客户:{clientCode}-{clientName}, 日期:{date}, 金额:{amount}, 标记:{packFlag}");
                }
                Console.WriteLine($"========================================");

                string countSql = $"SELECT COUNT(*) as total FROM {currentTableName} WHERE {whereClause}";
                DataTable countData = db.ExecuteQuery(countSql, parameters);
                filteredRecordCount = 0;
                if (countData != null && countData.Rows.Count > 0)
                    filteredRecordCount = Convert.ToInt32(countData.Rows[0]["total"]);

                // 查询数据（带条件）
                string sql = $"SELECT * FROM {currentTableName} WHERE {whereClause} ORDER BY id DESC LIMIT 500";
                Console.WriteLine($"执行查询SQL: {sql}");
                Console.WriteLine($"参数: startDate={dtpStart.Value:yyyy-MM-dd}, endDate={dtpEnd.Value:yyyy-MM-dd 23:59:59}");

                originalData = db.ExecuteQuery(sql, parameters);

                if (originalData == null || originalData.Rows.Count == 0)
                {
                    lblStatus.Text = "系统状态：无匹配记录";
                    Console.WriteLine($"查询结果: 0 条记录");
                    originalData = new DataTable();
                }
                else
                {
                    lblStatus.Text = $"系统状态：已加载 {originalData.Rows.Count} 条记录";
                    Console.WriteLine($"查询结果: {originalData.Rows.Count} 条记录");

                    // 显示查询到的记录
                    foreach (DataRow row in originalData.Rows)
                    {
                        string id = row["id"]?.ToString() ?? "";
                        string date = row["date"]?.ToString() ?? "";
                        string amount = row["total_amount"]?.ToString() ?? "";
                        string packFlag = row.Table.Columns.Contains("pack_flag") ? (row["pack_flag"]?.ToString() ?? "NULL") : "字段不存在";
                        Console.WriteLine($"  查询到: ID={id}, 日期={date}, 金额={amount}, 标记={packFlag}");
                    }
                }

                // 构建显示数据和列映射
                BuildDisplayData();
                DisplayData();
                UpdateFilterSummary();

                // 退出编辑模式
                ExitEditMode();
            }
            catch (Exception ex)
            {
                lblStatus.Text = "系统状态：加载失败";
                filteredRecordCount = 0;
                UpdateFilterSummary();
                MessageBox.Show($"加载数据失败：{ex.Message}", "错误",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                Console.WriteLine($"加载数据异常: {ex.Message}\n{ex.StackTrace}");
            }
            finally
            {
                Cursor.Current = Cursors.Default;
            }
        }

        private string FindDateField(DataTable schema)
        {
            foreach (DataRow row in schema.Rows)
            {
                string fieldName = row["name"].ToString().ToLower();
                if (fieldName == "date" || fieldName == "transaction_date" || fieldName == "created_time")
                {
                    return row["name"].ToString();
                }
            }
            return "";
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

        private string FindStatusField(DataTable schema)
        {
            foreach (DataRow row in schema.Rows)
            {
                string fieldName = row["name"].ToString().ToLower();
                if (fieldName.Contains("status") || fieldName.Contains("状态"))
                {
                    return row["name"].ToString();
                }
            }
            return "";
        }

        private void BuildDisplayData()
        {
            // 创建用于显示的DataTable
            displayData = new DataTable();
            columnMappings = new Dictionary<string, string>();

            // 需要排除的列名
            HashSet<string> excludedColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "is_settled", "settled_by",
        "creator", "date", "settled_time",
        "source_device_id", "source_record_id"
    };

            // 添加列并建立映射
            foreach (DataColumn originalColumn in originalData.Columns)
            {
                string columnName = originalColumn.ColumnName;

                if (excludedColumns.Contains(columnName))
                    continue;

                string displayName = TranslateColumnName(columnName);
                displayData.Columns.Add(displayName, originalColumn.DataType);
                columnMappings[displayName] = columnName; // 显示名 -> 实际字段名
            }

            // 确保有包装类型标记字段，如果没有则添加
            bool hasFlagField = false;
            foreach (DataColumn col in originalData.Columns)
            {
                if (col.ColumnName.ToLower() == "pack_flag" || col.ColumnName.ToLower() == "packaging_type_flag")
                {
                    hasFlagField = true;
                    break;
                }
            }

            // 如果没有标记字段，添加一个计算列用于显示
            if (!hasFlagField)
            {
                if (!displayData.Columns.Contains("包装类型"))
                {
                    displayData.Columns.Add("包装类型", typeof(string));
                    columnMappings["包装类型"] = "calculated_flag";
                }
            }

            // 复制数据
            foreach (DataRow originalRow in originalData.Rows)
            {
                DataRow newRow = displayData.NewRow();
                int newColIndex = 0;

                for (int i = 0; i < originalData.Columns.Count; i++)
                {
                    string columnName = originalData.Columns[i].ColumnName;

                    if (excludedColumns.Contains(columnName))
                        continue;

                    newRow[newColIndex] = originalRow[i];
                    newColIndex++;
                }

                // 如果没有标记字段，根据总金额的正负判断包装类型
                if (!hasFlagField && displayData.Columns.Contains("包装类型"))
                {
                    decimal totalAmount = 0;
                    if (originalData.Columns.Contains("total_amount") && originalRow["total_amount"] != DBNull.Value)
                    {
                        totalAmount = Convert.ToDecimal(originalRow["total_amount"]);
                    }

                    // 如果总金额为负，认为是进包装，否则为出包装
                    newRow["包装类型"] = totalAmount < 0 ? "进包装" : "出包装";
                }
                else if (displayData.Columns.Contains("包装类型"))
                {
                    newRow["包装类型"] = ResolvePackFlagDisplay(originalRow);
                }

                if (displayData.Columns.Contains("状态"))
                {
                    newRow["状态"] = ResolveStatusDisplay(originalRow);
                }

                displayData.Rows.Add(newRow);
            }
        }

        private void DisplayData()
        {
            try
            {
                // 清除现有列
                dataGridView.Columns.Clear();

                // 如果没有数据
                if (displayData == null || displayData.Rows.Count == 0)
                {
                    ShowNoDataMessage();
                    return;
                }

                // 绑定数据
                dataGridView.DataSource = displayData;

                // 设置列属性
                ConfigureColumns();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"显示数据失败：{ex.Message}", "错误",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void UpdateFilterSummary()
        {
            QueryUiHelper.SumQuantityAndAmount(originalData, out decimal pageQuantity, out decimal pageAmount);
            QueryUiHelper.UpdateFilterSummaryLabel(lblFilterSummary, filteredRecordCount, pageQuantity, pageAmount);
        }

        private void ShowNoDataMessage()
        {
            dataGridView.Columns.Add("提示", "提示");
            int rowIndex = dataGridView.Rows.Add();
            dataGridView.Rows[rowIndex].Cells["提示"].Value = "没有找到包装记录数据";
            dataGridView.Rows[rowIndex].Cells["提示"].Style.ForeColor = Color.Red;
            dataGridView.Rows[rowIndex].Cells["提示"].Style.Font = QueryUiHelper.QueryGridCellFont;
            dataGridView.Rows[rowIndex].Height = 50;
            dataGridView.Columns["提示"].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
            dataGridView.Columns["提示"].HeaderCell.Style.Alignment = DataGridViewContentAlignment.MiddleCenter;
        }

        private void ConfigureColumns()
        {
            if (dataGridView.Columns.Count == 0) return;

            dataGridView.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;

            string[] hiddenColumns = { "来源设备", "来源记录ID", "source_device_id", "source_record_id" };
            foreach (string colName in hiddenColumns)
            {
                if (dataGridView.Columns.Contains(colName))
                    dataGridView.Columns[colName].Visible = false;
            }

            foreach (DataGridViewColumn column in dataGridView.Columns)
            {
                if (!column.Visible) continue;

                column.MinimumWidth = 50;
                column.SortMode = DataGridViewColumnSortMode.Automatic;

                string headerText = column.HeaderText;

                if (headerText == "ID")
                {
                    column.FillWeight = 55;
                    column.DefaultCellStyle.BackColor = Color.LightGray;
                }
                else if (headerText.Contains("单据号"))
                {
                    column.FillWeight = 105;
                }
                else if (headerText.Contains("总金额"))
                {
                    column.FillWeight = 105;
                    column.MinimumWidth = 88;
                    column.DefaultCellStyle.Format = "N2";
                }
                else if (headerText.Contains("数量"))
                {
                    column.FillWeight = 78;
                    column.DefaultCellStyle.Format = "N2";
                }
                else if (headerText.Contains("单价") || headerText.Contains("成本") ||
                         (headerText.Contains("金额") && !headerText.Contains("总金额")))
                {
                    column.FillWeight = 72;
                    column.DefaultCellStyle.Format = "N2";
                }
                else if (headerText.Contains("创建时间") || headerText.Contains("结清时间") ||
                         headerText.Contains("更新时间") || headerText.Contains("交易日期"))
                {
                    column.FillWeight = 130;
                    column.DefaultCellStyle.Format = "yyyy-MM-dd HH:mm";
                }
                else if (headerText == "状态" || headerText.Contains("包装类型") ||
                         headerText.Contains("是否结清"))
                {
                    column.FillWeight = 85;
                }
                else if (headerText.Contains("经手人") || headerText.Contains("库位") ||
                         headerText.Contains("客户编号") || headerText.Contains("客户编码") ||
                         headerText.Contains("操作员"))
                {
                    column.FillWeight = 80;
                }
                else if (headerText.Contains("包装明细") || headerText.Contains("规格") ||
                         headerText.Contains("型号") || headerText.Contains("产品"))
                {
                    column.FillWeight = 110;
                }
                else if (headerText.Contains("客户名称"))
                {
                    column.FillWeight = 90;
                }
                else if (headerText.Contains("备注") || headerText.Contains("描述"))
                {
                    column.FillWeight = 115;
                }
                else
                {
                    column.FillWeight = 100;
                }

                column.ReadOnly = true;
            }

            QueryUiHelper.ApplyUniformColumnAlignment(dataGridView);
        }

        private string ResolvePackFlagDisplay(DataRow originalRow)
        {
            if (originalData.Columns.Contains("pack_flag") && originalRow["pack_flag"] != DBNull.Value)
            {
                string flag = originalRow["pack_flag"].ToString();
                return string.Equals(flag, "RETURN", StringComparison.OrdinalIgnoreCase) ? "进包装" : "出包装";
            }

            if (originalData.Columns.Contains("packaging_type_flag") && originalRow["packaging_type_flag"] != DBNull.Value)
            {
                string flag = originalRow["packaging_type_flag"].ToString();
                return string.Equals(flag, "RETURN", StringComparison.OrdinalIgnoreCase) ? "进包装" : "出包装";
            }

            if (originalData.Columns.Contains("total_amount") && originalRow["total_amount"] != DBNull.Value
                && Convert.ToDecimal(originalRow["total_amount"]) < 0)
            {
                return "进包装";
            }

            return "出包装";
        }

        private string ResolveStatusDisplay(DataRow originalRow)
        {
            if (originalData.Columns.Contains("status") && originalRow["status"] != DBNull.Value)
            {
                string status = originalRow["status"]?.ToString()?.Trim();
                if (!string.IsNullOrEmpty(status))
                    return status;
            }

            if (originalData.Columns.Contains("is_settled") && originalRow["is_settled"] != DBNull.Value)
            {
                int isSettled = Convert.ToInt32(originalRow["is_settled"]);
                return isSettled == 1 ? "已结清" : "处理中";
            }

            return "处理中";
        }

        private void DataGridView_CellFormatting(object sender, DataGridViewCellFormattingEventArgs e)
        {
            if (e.RowIndex < 0 || e.ColumnIndex < 0)
                return;

            var grid = sender as DataGridView;
            if (grid == null || e.RowIndex >= grid.Rows.Count)
                return;

            if (grid.Rows[e.RowIndex].IsNewRow)
                return;

            string header = grid.Columns[e.ColumnIndex].HeaderText;
            string text = e.Value?.ToString()?.Trim() ?? string.Empty;

            if (header == "状态")
            {
                ApplyStatusCellStyle(e, text);
            }
            else if (header == "包装类型")
            {
                ApplyPackFlagCellStyle(e, text);
            }
        }

        private static void ApplyStatusCellStyle(DataGridViewCellFormattingEventArgs e, string status)
        {
            if (status == "已结清")
            {
                e.CellStyle.ForeColor = Color.Green;
                e.CellStyle.Font = QueryUiHelper.QueryGridCellBoldFont;
            }
            else if (status == "处理中")
            {
                e.CellStyle.ForeColor = Color.Orange;
                e.CellStyle.Font = QueryUiHelper.QueryGridCellBoldFont;
            }
        }

        private static void ApplyPackFlagCellStyle(DataGridViewCellFormattingEventArgs e, string flagValue)
        {
            if (flagValue == "出包装" || string.Equals(flagValue, "TAKE", StringComparison.OrdinalIgnoreCase))
            {
                e.CellStyle.ForeColor = Color.FromArgb(46, 125, 50);
                e.CellStyle.Font = QueryUiHelper.QueryGridCellBoldFont;
            }
            else if (flagValue == "进包装" || string.Equals(flagValue, "RETURN", StringComparison.OrdinalIgnoreCase))
            {
                e.CellStyle.ForeColor = Color.FromArgb(198, 40, 40);
                e.CellStyle.Font = QueryUiHelper.QueryGridCellBoldFont;
                e.CellStyle.BackColor = Color.FromArgb(255, 235, 238);
            }
        }

        private string TranslateColumnName(string columnName)
        {
            Dictionary<string, string> translations = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        { "id", "ID" },
        { "order_no", "单据号" },
        { "bill_no", "单据号" },
        { "client_name", "客户名称" },
        { "client_code", "客户编号" },
        { "customer_name", "客户名称" },
        { "product_name", "产品名称" },
        { "product_type", "产品型号" },
        { "quantity", "数量" },
        { "unit_price", "单价" },
        { "total_amount", "总金额" },
        { "amount", "金额" },
        { "pack_type", "包装明细" },
        { "package_type", "包装明细" },
        { "pack_flag", "包装类型" }, // 【新增】包装类型标记
        { "packaging_type_flag", "包装类型" }, // 【新增】包装类型标记
        { "settled_time", "结清时间" },
        { "status", "状态" },
        { "settle_status", "结算状态" },
        { "created_time", "创建时间" },
        { "create_time", "创建时间" },
        { "updated_at", "更新时间" },
        { "updated_time", "更新时间" },
        { "update_time", "更新时间" },
        { "transaction_date", "交易日期" },
        { "date", "日期" },
        { "handler", "经手人" },
        { "operator", "操作员" },
        { "location", "库位" },
        { "warehouse", "仓库" },
        { "notes", "备注" },
        { "remark", "备注" },
        { "remarks", "备注" },
        { "description", "描述" },
        { "material_cost", "材料成本" },
        { "labor_cost", "人工成本" },
        { "total_cost", "总成本" },
        { "creator", "创建人" },
        { "source_device_id", "来源设备" },
        { "source_record_id", "来源记录ID" },
        { "spec", "规格" }
    };

            return translations.ContainsKey(columnName) ? translations[columnName] : columnName;
        }

        private void EnterEditMode()
        {
            isEditMode = true;
            dataGridView.ReadOnly = false;

            // 设置哪些列可编辑
            foreach (DataGridViewColumn column in dataGridView.Columns)
            {
                string colName = column.HeaderText;
                // ID、单据号、包装类型、状态、创建时间等关键列不可编辑
                if (colName == "ID" || colName.Contains("单据号") ||
                    colName.Contains("包装类型") ||
                    colName.Contains("创建时间") || colName.Contains("更新时间") || colName.Contains("状态"))
                {
                    column.ReadOnly = true;
                }
                else
                {
                    column.ReadOnly = false;
                }
            }

            // 改变背景色提示编辑模式
            dataGridView.BackgroundColor = Color.FromArgb(255, 255, 240); // 浅黄色

            foreach (DataGridViewColumn col in dataGridView.Columns)
            {
                if (!col.ReadOnly)
                {
                    col.HeaderCell.Style.BackColor = Color.FromArgb(255, 255, 200);
                    col.HeaderCell.Style.ForeColor = Color.Black;
                }
                else
                {
                    col.HeaderCell.Style.BackColor = Color.FromArgb(142, 68, 173);
                    col.HeaderCell.Style.ForeColor = Color.White;
                }
            }

            lblStatus.Text = "系统状态：编辑模式 - 修改后请点击保存";
        }

        private void ExitEditMode()
        {
            isEditMode = false;
            dataGridView.ReadOnly = true;
            dataGridView.BackgroundColor = Color.White;

            foreach (DataGridViewColumn col in dataGridView.Columns)
            {
                col.HeaderCell.Style.BackColor = Color.FromArgb(142, 68, 173);
                col.HeaderCell.Style.ForeColor = Color.White;
            }

            lblStatus.Text = $"系统状态：已加载 {originalData?.Rows.Count ?? 0} 条记录";
        }

        private void DataGridView_CellEndEdit(object sender, DataGridViewCellEventArgs e)
        {
            // 单元格编辑完成后的处理
            DataGridViewRow row = dataGridView.Rows[e.RowIndex];
            DataGridViewColumn col = dataGridView.Columns[e.ColumnIndex];

            // 如果修改了数量或单价，自动计算总金额
            if (col.HeaderText.Contains("数量") || col.HeaderText.Contains("单价"))
            {
                CalculateTotalAmount(row);
            }
        }

        private void CalculateTotalAmount(DataGridViewRow row)
        {
            try
            {
                // 查找数量和单价列
                DataGridViewColumn quantityCol = null;
                DataGridViewColumn priceCol = null;
                DataGridViewColumn totalCol = null;
                DataGridViewColumn flagCol = null; // 【新增】包装类型列

                foreach (DataGridViewColumn col in dataGridView.Columns)
                {
                    string header = col.HeaderText;
                    if (header.Contains("数量")) quantityCol = col;
                    else if (header.Contains("单价")) priceCol = col;
                    else if (header.Contains("总金额")) totalCol = col;
                    else if (header.Contains("包装类型")) flagCol = col; // 【新增】
                }

                if (quantityCol != null && priceCol != null && totalCol != null)
                {
                    decimal quantity = 0;
                    decimal price = 0;

                    if (row.Cells[quantityCol.Index].Value != null)
                        decimal.TryParse(row.Cells[quantityCol.Index].Value.ToString(), out quantity);

                    if (row.Cells[priceCol.Index].Value != null)
                        decimal.TryParse(row.Cells[priceCol.Index].Value.ToString(), out price);

                    decimal total = quantity * price;

                    // 【新增】如果是进包装，金额为负值
                    if (flagCol != null && row.Cells[flagCol.Index].Value != null)
                    {
                        string flagValue = row.Cells[flagCol.Index].Value.ToString();
                        if (flagValue == "进包装")
                        {
                            total = -total;
                        }
                    }

                    row.Cells[totalCol.Index].Value = total;

                    // 根据正负设置颜色
                    if (total < 0)
                    {
                        row.Cells[totalCol.Index].Style.ForeColor = Color.Red;
                    }
                    else
                    {
                        row.Cells[totalCol.Index].Style.ForeColor = Color.Black;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"计算总金额失败: {ex.Message}");
            }
        }
        private void BtnBatchPrice_Click(object sender, EventArgs e)
        {
            if (isEditMode)
            {
                MessageBox.Show("当前处于编辑模式，请先保存或取消编辑后再批量改价。", "提示",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            try
            {
                db = db ?? new DatabaseManager();
                using (var dlg = new PackTypeBatchPriceDialog(db))
                {
                    if (dlg.ShowDialog(this) == DialogResult.OK)
                    {
                        LoadData();
                        lblStatus.Text = "系统状态：批量改价完成，数据已刷新";
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"打开批量改价失败: {ex.Message}", "错误",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void BtnEdit_Click(object sender, EventArgs e)
        {
            if (dataGridView.Rows.Count == 0)
            {
                MessageBox.Show("没有数据可以编辑", "提示",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            EnterEditMode();
            MessageBox.Show("已进入编辑模式\n\n可编辑的列：数量、单价、备注等\n\n修改后请点击【保存】按钮",
                "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void BtnCancel_Click(object sender, EventArgs e)
        {
            if (!isEditMode)
            {
                MessageBox.Show("当前不是编辑模式", "提示",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            DialogResult result = MessageBox.Show("确定取消编辑吗？所有未保存的修改将丢失。",
                "确认取消", MessageBoxButtons.YesNo, MessageBoxIcon.Question);

            if (result == DialogResult.Yes)
            {
                // 重新加载数据，放弃所有修改
                LoadData();
            }
        }

        private void BtnSave_Click(object sender, EventArgs e)
        {
            try
            {
                if (!isEditMode)
                {
                    MessageBox.Show("当前不是编辑模式，无需保存", "提示",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                // 结束编辑状态
                dataGridView.EndEdit();

                int updatedCount = 0;
                List<string> errors = new List<string>();

                // 遍历所有行，找出修改过的行
                for (int i = 0; i < displayData.Rows.Count; i++)
                {
                    DataRow displayRow = displayData.Rows[i];
                    DataRow originalRow = originalData.Rows[i];

                    // 检查是否有修改
                    bool hasChanges = false;
                    Dictionary<string, object> changedValues = new Dictionary<string, object>();

                    foreach (DataColumn col in displayData.Columns)
                    {
                        string displayColName = col.ColumnName;
                        string actualFieldName = columnMappings[displayColName];

                        object displayValue = displayRow[displayColName];
                        object originalValue = originalRow[actualFieldName];

                        // 比较值是否不同
                        if (!object.Equals(displayValue, originalValue))
                        {
                            hasChanges = true;
                            changedValues[actualFieldName] = displayValue ?? DBNull.Value;
                        }
                    }

                    // 如果有修改，保存到数据库
                    if (hasChanges)
                    {
                        // 获取ID
                        object idValue = null;
                        foreach (DataColumn col in originalData.Columns)
                        {
                            if (col.ColumnName.ToLower() == "id")
                            {
                                idValue = originalRow["id"];
                                break;
                            }
                        }

                        if (idValue == null || idValue == DBNull.Value)
                        {
                            errors.Add($"第 {i + 1} 行：无法找到ID");
                            continue;
                        }

                        // 构建更新SQL
                        string sql = $"UPDATE {currentTableName} SET ";
                        var parameters = new Dictionary<string, object>();

                        foreach (var kv in changedValues)
                        {
                            sql += $"{kv.Key} = @{kv.Key}, ";
                            parameters.Add($"@{kv.Key}", kv.Value);
                        }

                        // 添加更新时间（如果存在）
                        if (originalData.Columns.Contains("updated_at") || originalData.Columns.Contains("updated_time"))
                        {
                            string updatedColumn = originalData.Columns.Contains("updated_at") ? "updated_at" : "updated_time";
                            sql += $"{updatedColumn} = datetime('now', 'localtime'), ";
                        }

                        // 去掉最后的逗号和空格
                        sql = sql.TrimEnd(',', ' ') + $" WHERE id = @id";
                        parameters.Add("@id", idValue);

                        // 执行更新
                        int result = db.ExecuteNonQuery(sql, parameters);

                        if (result > 0)
                        {
                            updatedCount++;

                            // 更新originalData中的值
                            foreach (var kv in changedValues)
                            {
                                originalRow[kv.Key] = kv.Value;
                            }
                        }
                        else
                        {
                            errors.Add($"第 {i + 1} 行：更新失败");
                        }
                    }
                }

                // 显示保存结果
                string message = $"成功保存 {updatedCount} 条记录";
                if (errors.Count > 0)
                {
                    message += $"\n\n失败记录：\n{string.Join("\n", errors)}";
                    MessageBox.Show(message, "保存结果",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
                else
                {
                    MessageBox.Show(message, "保存结果",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                }

                if (updatedCount > 0)
                {
                    lblStatus.Text = $"系统状态：已保存 {updatedCount} 条记录";
                    ExitEditMode();

                    // 刷新显示（可选，如果需要看到最新的数据）
                    // LoadData();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"保存失败：{ex.Message}", "错误",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void BtnSettle_Click(object sender, EventArgs e)
        {
            if (dataGridView.SelectedRows.Count == 0)
            {
                MessageBox.Show("请先选择要结清的行", "提示",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            DataGridViewRow selectedRow = dataGridView.SelectedRows[0];
            int rowIndex = selectedRow.Index;

            // 检查是否已结清
            if (rowIndex < originalData.Rows.Count)
            {
                object isSettledValue = originalData.Rows[rowIndex]["is_settled"];
                if (isSettledValue != null && isSettledValue != DBNull.Value)
                {
                    int isSettled = Convert.ToInt32(isSettledValue);
                    if (isSettled == 1)
                    {
                        MessageBox.Show("该记录已经结清", "提示",
                            MessageBoxButtons.OK, MessageBoxIcon.Information);
                        return;
                    }
                }
            }

            // 获取记录信息用于确认
            string orderNo = "";
            string clientName = "";
            decimal totalAmount = 0;
            int id = 0;

            // 从 originalData 获取 ID
            if (rowIndex < originalData.Rows.Count)
            {
                id = Convert.ToInt32(originalData.Rows[rowIndex]["id"]);
            }

            foreach (DataGridViewColumn col in dataGridView.Columns)
            {
                if (col.HeaderText.Contains("单据号"))
                    orderNo = selectedRow.Cells[col.Index].Value?.ToString() ?? "";
                else if (col.HeaderText.Contains("客户"))
                    clientName = selectedRow.Cells[col.Index].Value?.ToString() ?? "";
                else if (col.HeaderText.Contains("总金额"))
                    decimal.TryParse(selectedRow.Cells[col.Index].Value?.ToString() ?? "0", out totalAmount);
            }

            string confirmMessage = $"确定要将以下包装记录标记为已结清吗？\n\n" +
                                   $"单据号：{orderNo}\n" +
                                   $"客户：{clientName}\n" +
                                   $"金额：{totalAmount:N2}元\n\n" +
                                   $"结清后将不再参与客户对账计算。";

            DialogResult result = MessageBox.Show(confirmMessage,
                "确认结清", MessageBoxButtons.OKCancel, MessageBoxIcon.Question);

            if (result == DialogResult.OK)
            {
                try
                {
                    if (id == 0)
                    {
                        MessageBox.Show("无法找到记录ID", "错误",
                            MessageBoxButtons.OK, MessageBoxIcon.Error);
                        return;
                    }

                    // 更新记录为已结清 - 同时更新 status、is_settled 和 settled_time
                    string sql = @"UPDATE packaging_transactions 
                          SET is_settled = 1,
                              status = '已结清',
                              settled_time = datetime('now', 'localtime'),  -- 使用本地时间
                              settled_by = @settledBy
                          WHERE id = @id";

                    var parameters = new Dictionary<string, object>
            {
                { "@id", id },
                { "@settledBy", Environment.UserName }
            };

                    int updateResult = db.ExecuteNonQuery(sql, parameters);

                    if (updateResult > 0)
                    {
                        // 获取当前的本地时间用于显示
                        string currentTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

                        // 更新内存中的数据
                        originalData.Rows[rowIndex]["is_settled"] = 1;
                        originalData.Rows[rowIndex]["status"] = "已结清";
                        originalData.Rows[rowIndex]["settled_time"] = currentTime;

                        // 查找状态列和结清时间列
                        DataGridViewColumn statusColumn = null;
                        DataGridViewColumn settledTimeColumn = null;

                        foreach (DataGridViewColumn col in dataGridView.Columns)
                        {
                            if (col.HeaderText == "状态" || col.HeaderText.Contains("状态"))
                            {
                                statusColumn = col;
                            }
                            else if (col.HeaderText == "结清时间" || col.HeaderText.Contains("结清时间"))
                            {
                                settledTimeColumn = col;
                            }
                        }

                        // 更新状态列显示
                        if (statusColumn != null)
                        {
                            selectedRow.Cells[statusColumn.Index].Value = "已结清";
                            selectedRow.Cells[statusColumn.Index].Style.ForeColor = Color.Green;
                            selectedRow.Cells[statusColumn.Index].Style.Font = QueryUiHelper.QueryGridCellBoldFont;
                        }

                        // 更新结清时间列显示
                        if (settledTimeColumn != null)
                        {
                            selectedRow.Cells[settledTimeColumn.Index].Value = currentTime;
                        }

                        // 整行变淡显示
                        foreach (DataGridViewCell cell in selectedRow.Cells)
                        {
                            if ((statusColumn == null || cell.ColumnIndex != statusColumn.Index) &&
                                (settledTimeColumn == null || cell.ColumnIndex != settledTimeColumn.Index))
                            {
                                cell.Style.ForeColor = Color.Gray;
                            }
                        }

                        lblStatus.Text = "系统状态：记录已结清";

                        MessageBox.Show("记录已成功标记为已结清\n该记录将不再参与客户对账计算。",
                            "成功", MessageBoxButtons.OK, MessageBoxIcon.Information);

                        // 重新加载数据以确保显示最新状态
                        LoadData();
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
                }
            }
        }

        private void BtnDelete_Click(object sender, EventArgs e)
        {
            if (dataGridView.SelectedRows.Count == 0 || originalData == null)
            {
                MessageBox.Show("请先选择要删除的行", "提示",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            // 如果在编辑模式，先退出编辑模式
            if (isEditMode)
            {
                DialogResult exitResult = MessageBox.Show("当前在编辑模式，是否退出编辑模式？",
                    "提示", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                if (exitResult == DialogResult.Yes)
                {
                    ExitEditMode();
                }
                else
                {
                    return;
                }
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
                    if (rowIndex >= originalData.Rows.Count)
                        continue;

                    DataRow originalRow = originalData.Rows[rowIndex];
                    object idValue = originalRow["id"];

                    if (idValue != null && idValue != DBNull.Value)
                    {
                        string sql = $"DELETE FROM {currentTableName} WHERE id = @id";
                        if (db.ExecuteNonQuery(sql, new Dictionary<string, object> { { "@id", idValue } }) > 0)
                        {
                            deletedCount++;
                        }
                    }
                }

                lblStatus.Text = "系统状态：记录已删除";
                MessageBox.Show($"删除成功！共删除了 {deletedCount} 条记录。", "删除成功",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);

                LoadData();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"删除失败: {ex.Message}", "错误",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
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
                MessageBox.Show("没有数据可以导出", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }

            const QueryReportKind kind = QueryReportKind.Packaging;
            ClientSearchHelper.TryGetSelectedItem(cmbClient, _clientItems, out ClientSearchItem clientFilter, allowEmptySelection: true);
            string clientName = QueryReportExportHelper.ResolveExportClientName(clientFilter);
            context = QueryReportExportHelper.BuildQueryReportContext(
                originalData,
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
                lblStatus.Text = "系统状态：正在打印...";
                ExcelExportHelper.PrintSimpleTableWithDialog(
                    context.Title,
                    context.Subtitle,
                    context.PrintTable,
                    context.Columns);
                lblStatus.Text = "系统状态：就绪";
            }
            catch (Exception ex)
            {
                lblStatus.Text = "系统状态：打印失败";
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

            const QueryReportKind kind = QueryReportKind.Packaging;
            ClientSearchHelper.TryGetSelectedItem(cmbClient, _clientItems, out ClientSearchItem clientFilter, allowEmptySelection: true);
            string clientName = QueryReportExportHelper.ResolveExportClientName(clientFilter);

            SaveFileDialog sfd = new SaveFileDialog
            {
                Filter = "PDF文件 (*.pdf)|*.pdf",
                FileName = QueryReportExportHelper.BuildDefaultFileName(kind, clientName),
                DefaultExt = "pdf",
                AddExtension = true,
                Title = "导出包装记录"
            };

            if (sfd.ShowDialog() != DialogResult.OK)
                return;

            try
            {
                Cursor.Current = Cursors.WaitCursor;
                lblStatus.Text = "系统状态：正在导出数据...";

                string path = sfd.FileName;
                if (!path.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
                    path = Path.ChangeExtension(path, ".pdf");

                bool ok = ExcelExportHelper.ExportSimpleTablePdf(
                    path, context.Title, context.Subtitle, context.PrintTable, context.Columns);

                if (ok)
                {
                    lblStatus.Text = "系统状态：导出完成";
                    MessageBox.Show($"导出成功！\n{path}", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    Process.Start(path);
                }
                else
                {
                    lblStatus.Text = "系统状态：导出失败";
                }
            }
            catch (Exception ex)
            {
                lblStatus.Text = "系统状态：导出失败";
                MessageBox.Show($"导出失败：{ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                Cursor.Current = Cursors.Default;
            }
        }
    }
}