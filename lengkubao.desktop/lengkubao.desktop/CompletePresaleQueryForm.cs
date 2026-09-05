using System;
using System.Collections.Generic;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace lengkubao.desktop
{
    public class CompletePresaleQueryForm : Form
    {
        private DatabaseManager db;
        private DataGridView dgvBills;
        private Label lblStatus;
        private Label lblRecordCount;
        private Label lblBuyerBalance;
        private DateTimePicker dtpStartDate;
        private DateTimePicker dtpEndDate;
        private ComboBox cmbBillNo;
        private ComboBox cmbBuyer;
        private ComboBox cmbLocation;
        private ComboBox cmbHandler;
        private ComboBox cmbDebtStatus;
        private Button btnQuery;
        private Button btnEdit;
        private Button btnSave;

        private List<ClientSearchItem> _billNoItems = new List<ClientSearchItem>();
        private List<ClientSearchItem> _buyerItems = new List<ClientSearchItem>();
        private List<ClientSearchItem> _locationItems = new List<ClientSearchItem>();
        private List<ClientSearchItem> _handlerItems = new List<ClientSearchItem>();

        private DataTable originalData;
        private bool isEditMode;

        private static readonly HashSet<string> EditableBillColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "buyer_code", "buyer_name", "location", "sale_mode", "status",
            "paid_amount", "handler", "remark", "date"
        };

        private static readonly HashSet<string> EditableItemColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "spec", "quantity", "unit_price", "item_amount"
        };

        private static readonly string[] VisibleColumnOrder =
        {
            "bill_no", "buyer_name", "location", "date", "stock_display",
            "spec", "quantity", "unit_price", "total_amount", "paid_amount", "debt_status",
            "handler", "remark"
        };

        /// <summary>同一单据多行明细时，主单字段视觉合并（不含规格/数量/单价/模式）。</summary>
        private static readonly string[] BillMergeColumns =
        {
            "bill_no", "buyer_name", "location", "date",
            "total_amount", "paid_amount", "debt_status", "handler", "remark"
        };

        private DataGridViewCellPaintingEventHandler _billMergePaintHandler;

        public CompletePresaleQueryForm()
        {
            db = new DatabaseManager();
            InitializeForm();
            LoadFilterData();
            LoadData();
            DatabaseManager.PresaleBillRemoteUpdated += OnPresaleBillRemoteUpdated;
            FormClosed += (s, e) => DatabaseManager.PresaleBillRemoteUpdated -= OnPresaleBillRemoteUpdated;
        }

        private void OnPresaleBillRemoteUpdated(string billNo)
        {
            if (IsDisposed || isEditMode) return;
            if (InvokeRequired)
            {
                BeginInvoke(new Action(() => OnPresaleBillRemoteUpdated(billNo)));
                return;
            }
            LoadData();
            if (!string.IsNullOrWhiteSpace(billNo))
                lblStatus.Text = $"已同步预售单 {billNo}";
        }

        private ClientSearchHelper.SearchComboOptions FilterComboOptions =>
            new ClientSearchHelper.SearchComboOptions
            {
                PlaceholderText = ClientSearchHelper.NamePlaceholderText,
                AllowEmptySelection = true
            };

        private void InitializeForm()
        {
            QueryUiHelper.ApplyFormChrome(this);
            this.Text = "销预售查询";
            this.Size = new Size(1280, 720);
            this.StartPosition = FormStartPosition.CenterScreen;

            var mainLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                RowCount = 5,
                ColumnCount = 1,
                BackColor = StatisticsUiHelper.PanelBack
            };
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 124));
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));

            Panel headerPanel = QueryUiHelper.CreateModuleHeader(QueryModule.Presale, "销预售查询", out lblStatus);
            mainLayout.Controls.Add(headerPanel, 0, 0);

            Panel filterPanel = new Panel { Dock = DockStyle.Fill };
            QueryUiHelper.StyleFilterPanel(filterPanel);

            dtpStartDate = new DateTimePicker { Width = 132 };
            dtpEndDate = new DateTimePicker { Width = 132 };
            QueryUiHelper.StyleDatePicker(dtpStartDate);
            QueryUiHelper.StyleDatePicker(dtpEndDate);
            DateRangeSettings.ApplyTo(dtpStartDate, dtpEndDate);

            cmbBillNo = CreateFilterComboBox(140);
            cmbBuyer = CreateFilterComboBox(160);
            cmbLocation = CreateFilterComboBox(120);
            cmbHandler = CreateFilterComboBox(120);

            cmbDebtStatus = new ComboBox { Width = 110, DropDownStyle = ComboBoxStyle.DropDownList };
            cmbDebtStatus.Items.AddRange(new object[] { "全部", "有欠款", "已结清", "有余额" });
            cmbDebtStatus.SelectedIndex = 0;
            QueryUiHelper.StyleComboBox(cmbDebtStatus);

            btnQuery = QueryUiHelper.CreateButton("搜索", QueryButtonRole.Primary, QueryUiHelper.CompactButtonSize);
            btnQuery.Click += (s, e) => ExecuteQuery();

            Button btnReset = QueryUiHelper.CreateButton("重置", QueryButtonRole.Secondary, QueryUiHelper.CompactButtonSize);
            btnReset.Click += (s, e) =>
            {
                if (isEditMode)
                {
                    MessageBox.Show("当前处于编辑模式，请先保存或取消编辑后再重置。", "提示",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                ClientSearchHelper.ResetToPlaceholder(cmbBillNo);
                ClientSearchHelper.ResetToPlaceholder(cmbBuyer);
                ClientSearchHelper.ResetToPlaceholder(cmbLocation);
                ClientSearchHelper.ResetToPlaceholder(cmbHandler);
                cmbDebtStatus.SelectedIndex = 0;
                DateRangeSettings.ApplyFactoryDefaultTo(dtpStartDate, dtpEndDate);
                LoadFilterData();
                LoadData();
            };

            filterPanel.Controls.Add(QueryUiHelper.CreateTwoRowQueryFilterPanel(
                QueryUiHelper.CreateFilterField("开始日期:", dtpStartDate, 132),
                QueryUiHelper.CreateFilterField("结束日期:", dtpEndDate, 132),
                new[]
                {
                    QueryUiHelper.CreateFilterField("单 号:", cmbBillNo, 140),
                    QueryUiHelper.CreateFilterField("买 家:", cmbBuyer, 160),
                    QueryUiHelper.CreateFilterField("库 位:", cmbLocation, 120),
                    QueryUiHelper.CreateFilterField("经手人:", cmbHandler, 120),
                    QueryUiHelper.CreateFilterField("欠 款:", cmbDebtStatus, 110)
                },
                btnQuery,
                btnReset));
            mainLayout.Controls.Add(filterPanel, 0, 1);

            Panel infoPanel = new Panel { Dock = DockStyle.Fill };
            QueryUiHelper.StyleInfoBar(infoPanel);

            var infoLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1,
                BackColor = infoPanel.BackColor
            };
            infoLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            infoLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));

            lblRecordCount = new Label
            {
                Text = QueryUiHelper.FormatFilterSummaryText(0),
                AutoSize = true,
                Anchor = AnchorStyles.Left,
                Margin = new Padding(12, 8, 0, 0)
            };
            QueryUiHelper.StyleFilterSummaryLabel(lblRecordCount, QueryModule.Presale);

            lblBuyerBalance = new Label
            {
                Text = "选中单据或筛选买家后显示欠款/余额",
                AutoSize = true,
                Anchor = AnchorStyles.Right,
                Margin = new Padding(0, 8, 12, 0),
                ForeColor = Color.Gray
            };
            lblBuyerBalance.Font = QueryUiHelper.ChromeBoldFont;

            infoLayout.Controls.Add(lblRecordCount, 0, 0);
            infoLayout.Controls.Add(lblBuyerBalance, 1, 0);
            infoPanel.Controls.Add(infoLayout);
            mainLayout.Controls.Add(infoPanel, 0, 2);

            dgvBills = new DataGridView();
            QueryUiHelper.ApplyQueryGrid(dgvBills, QueryModule.Presale);
            dgvBills.SelectionChanged += DgvBills_SelectionChanged;
            mainLayout.Controls.Add(QueryUiHelper.CreateGridHost(dgvBills), 0, 3);

            Panel buttonPanel = BuildButtonPanel();
            mainLayout.Controls.Add(buttonPanel, 0, 4);

            this.Controls.Add(mainLayout);
        }

        private ComboBox CreateFilterComboBox(int width)
        {
            var combo = new ComboBox
            {
                Width = width,
                BackColor = Color.White,
                FlatStyle = FlatStyle.Flat
            };
            QueryUiHelper.StyleComboBox(combo, QueryUiHelper.FilterControlHeight);
            ClientSearchHelper.ApplySearchableStyle(combo, 260);
            return combo;
        }

        private void LoadFilterData()
        {
            try
            {
                string start = dtpStartDate.Value.ToString("yyyy-MM-dd");
                string end = dtpEndDate.Value.ToString("yyyy-MM-dd");

                _billNoItems = ClientSearchHelper.BuildDistinctFilterItems(db.GetPresaleDistinctValues("bill_no", start, end));
                _buyerItems = ClientSearchHelper.BuildDistinctFilterItems(db.GetPresaleDistinctValues("buyer_name", start, end));
                _locationItems = ClientSearchHelper.BuildDistinctFilterItems(db.GetPresaleDistinctValues("location", start, end));
                _handlerItems = ClientSearchHelper.BuildDistinctFilterItems(db.GetPresaleDistinctValues("handler", start, end));

                ClientSearchHelper.BindSearchableCombo(cmbBillNo, _billNoItems, FilterComboOptions);
                ClientSearchHelper.BindSearchableCombo(cmbBuyer, _buyerItems, FilterComboOptions);
                ClientSearchHelper.BindSearchableCombo(cmbLocation, _locationItems, FilterComboOptions);
                ClientSearchHelper.BindSearchableCombo(cmbHandler, _handlerItems, FilterComboOptions);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"加载筛选数据失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void RefreshFilterOptionsFromData(DataTable data)
        {
            if (data == null || data.Rows.Count == 0)
                return;

            ClientSearchHelper.MergeDistinctFromDataTable(_billNoItems, data, "bill_no");
            ClientSearchHelper.MergeDistinctFromDataTable(_buyerItems, data, "buyer_name");
            ClientSearchHelper.MergeDistinctFromDataTable(_locationItems, data, "location");
            ClientSearchHelper.MergeDistinctFromDataTable(_handlerItems, data, "handler");

            ClientSearchHelper.BindSearchableCombo(cmbBillNo, _billNoItems, FilterComboOptions);
            ClientSearchHelper.BindSearchableCombo(cmbBuyer, _buyerItems, FilterComboOptions);
            ClientSearchHelper.BindSearchableCombo(cmbLocation, _locationItems, FilterComboOptions);
            ClientSearchHelper.BindSearchableCombo(cmbHandler, _handlerItems, FilterComboOptions);
        }

        private void ExecuteQuery()
        {
            if (isEditMode)
            {
                MessageBox.Show("当前处于编辑模式，请先保存或取消编辑后再查询。", "提示",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (dtpStartDate.Value.Date > dtpEndDate.Value.Date)
            {
                MessageBox.Show("开始日期不能晚于结束日期！", "提示");
                return;
            }

            DateRangeSettings.SaveDefault(dtpStartDate.Value.Date, dtpEndDate.Value.Date);
            LoadData();
        }

        private string ResolveFilterValue(ComboBox combo, List<ClientSearchItem> items)
        {
            return ClientSearchHelper.ResolveFilterValueForQuery(combo, items);
        }

        private Panel BuildButtonPanel()
        {
            Panel buttonPanel = new Panel { Dock = DockStyle.Fill };
            QueryUiHelper.StyleToolbarPanel(buttonPanel);

            btnEdit = QueryUiHelper.CreateButton("编辑", QueryButtonRole.Primary);
            btnEdit.Location = new Point(10, 12);
            btnEdit.Click += BtnEdit_Click;
            buttonPanel.Controls.Add(btnEdit);

            btnSave = QueryUiHelper.CreateButton("保存", QueryButtonRole.Success);
            btnSave.Location = new Point(116, 12);
            btnSave.Click += BtnSave_Click;
            buttonPanel.Controls.Add(btnSave);

            Button btnCancel = QueryUiHelper.CreateButton("取消", QueryButtonRole.Warning);
            btnCancel.Location = new Point(222, 12);
            btnCancel.Click += BtnCancel_Click;
            buttonPanel.Controls.Add(btnCancel);

            Button btnClose = QueryUiHelper.CreateButton("关闭", QueryButtonRole.Neutral);
            btnClose.Location = new Point(328, 12);
            btnClose.Click += (s, e) => this.Close();
            buttonPanel.Controls.Add(btnClose);

            return buttonPanel;
        }

        private void DgvBills_SelectionChanged(object sender, EventArgs e)
        {
            UpdateBuyerBalanceFromCurrentRow();
        }

        private void LoadData()
        {
            try
            {
                Cursor = Cursors.WaitCursor;
                ExitEditMode();

                string start = dtpStartDate.Value.ToString("yyyy-MM-dd");
                string end = dtpEndDate.Value.ToString("yyyy-MM-dd");
                var data = db.GetPresaleRecordsForQuery(
                    start,
                    end,
                    ResolveFilterValue(cmbBillNo, _billNoItems),
                    ResolveFilterValue(cmbBuyer, _buyerItems),
                    ResolveFilterValue(cmbLocation, _locationItems),
                    ResolveFilterValue(cmbHandler, _handlerItems),
                    GetDebtStatusFilter());

                RefreshFilterOptionsFromData(data);

                originalData = data?.Copy() ?? new DataTable();
                AppendBillDebtStatusColumn(data);

                dgvBills.DataSource = data;
                FormatGridColumns();
                lblRecordCount.Text = QueryUiHelper.FormatFilterSummaryText(data.Rows.Count);
                lblStatus.Text = $"销预售查询 · 共 {data.Rows.Count} 条";
                UpdateBuyerBalanceDisplay();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"查询失败: {ex.Message}", "错误");
            }
            finally
            {
                Cursor = Cursors.Default;
            }
        }

        private void FormatGridColumns()
        {
            ApplyGridColumnLayout();
            ColorizeDebtStatusColumn();
            AttachBillMergeHandler();
            QueryUiHelper.ApplyUniformColumnAlignment(dgvBills);
        }

        private void ApplyGridColumnLayout()
        {
            if (dgvBills.Columns.Count == 0)
                return;

            SetHeader(dgvBills, "bill_no", "单号");
            SetHeader(dgvBills, "buyer_name", "买家");
            SetHeader(dgvBills, "location", "库位");
            SetHeader(dgvBills, "stock_display", "模式");
            SetHeader(dgvBills, "sale_mode", "模式");
            SetHeader(dgvBills, "date", "日期");
            SetHeader(dgvBills, "spec", "规格");
            SetHeader(dgvBills, "quantity", "数量");
            SetHeader(dgvBills, "unit_price", "单价");
            SetHeader(dgvBills, "total_amount", "应收");
            SetHeader(dgvBills, "paid_amount", "已收");
            SetHeader(dgvBills, "debt_status", "欠款状态");
            SetHeader(dgvBills, "handler", "经手人");
            SetHeader(dgvBills, "remark", "备注");

            HideColumn(dgvBills, "bill_id");
            HideColumn(dgvBills, "item_id");
            HideColumn(dgvBills, "buyer_code");
            HideColumn(dgvBills, "item_amount");
            HideColumn(dgvBills, "status");
            HideColumn(dgvBills, "shipped_quantity");

            ApplyVisibleColumnOrder();

            if (isEditMode)
            {
                int modeDisplayIndex = dgvBills.Columns.Contains("stock_display")
                    ? dgvBills.Columns["stock_display"].DisplayIndex
                    : 4;
                HideColumn(dgvBills, "stock_display");
                if (dgvBills.Columns.Contains("sale_mode"))
                {
                    dgvBills.Columns["sale_mode"].Visible = true;
                    dgvBills.Columns["sale_mode"].HeaderText = "模式";
                    dgvBills.Columns["sale_mode"].DisplayIndex = modeDisplayIndex;
                }
            }
            else
            {
                HideColumn(dgvBills, "sale_mode");
                if (dgvBills.Columns.Contains("stock_display"))
                    dgvBills.Columns["stock_display"].Visible = true;
            }

            ApplyColumnWidths();
            StatisticsUiHelper.ApplyMergeColumnAlignment(dgvBills, BillMergeColumns);
            FormatMoneyColumns(dgvBills, "unit_price", "total_amount", "paid_amount");
        }

        private void ApplyVisibleColumnOrder()
        {
            for (int i = 0; i < VisibleColumnOrder.Length; i++)
            {
                if (dgvBills.Columns.Contains(VisibleColumnOrder[i]))
                    dgvBills.Columns[VisibleColumnOrder[i]].DisplayIndex = i;
            }
        }

        private void ApplyColumnWidths()
        {
            if (!dgvBills.Columns.Contains("bill_no"))
                return;

            var billNoColumn = dgvBills.Columns["bill_no"];
            billNoColumn.AutoSizeMode = DataGridViewAutoSizeColumnMode.None;
            billNoColumn.Width = 88;
            billNoColumn.MinimumWidth = 72;
        }

        private void AttachBillMergeHandler()
        {
            if (_billMergePaintHandler != null)
                dgvBills.CellPainting -= _billMergePaintHandler;

            _billMergePaintHandler = StatisticsUiHelper.CreateConsecutiveCellMergePaintHandler(
                dgvBills,
                BillMergeColumns,
                "bill_no",
                rowIndex => isEditMode);

            if (_billMergePaintHandler != null)
                dgvBills.CellPainting += _billMergePaintHandler;
        }

        private void ColorizeDebtStatusColumn()
        {
            if (!dgvBills.Columns.Contains("debt_status")) return;

            foreach (DataGridViewRow row in dgvBills.Rows)
            {
                if (row.IsNewRow) continue;
                string text = row.Cells["debt_status"]?.Value?.ToString() ?? "";
                if (text.StartsWith("欠款"))
                    row.Cells["debt_status"].Style.ForeColor = Color.FromArgb(180, 50, 50);
                else if (text.StartsWith("有余额"))
                    row.Cells["debt_status"].Style.ForeColor = Color.FromArgb(4, 120, 87);
                else
                    row.Cells["debt_status"].Style.ForeColor = Color.Gray;
            }
        }

        private string GetDebtStatusFilter()
        {
            switch (cmbDebtStatus?.SelectedIndex ?? 0)
            {
                case 1: return "OWED";
                case 2: return "SETTLED";
                case 3: return "CREDIT";
                default: return string.Empty;
            }
        }

        private static void AppendBillDebtStatusColumn(DataTable data)
        {
            if (data == null) return;
            if (!data.Columns.Contains("debt_status"))
                data.Columns.Add("debt_status", typeof(string));

            foreach (DataRow row in data.Rows)
                row["debt_status"] = FormatBillDebtStatus(row);
        }

        private static string FormatBillDebtStatus(DataRow row)
        {
            decimal total = row["total_amount"] != DBNull.Value ? Convert.ToDecimal(row["total_amount"]) : 0m;
            decimal paid = row["paid_amount"] != DBNull.Value ? Convert.ToDecimal(row["paid_amount"]) : 0m;
            return FormatBillDebtStatusFromAmounts(total, paid);
        }

        private static string FormatBillDebtStatusFromAmounts(decimal total, decimal paid)
        {
            decimal balance = total - paid;
            if (balance > 0) return $"欠款 {balance:N2} 元";
            if (balance < 0) return $"有余额 {Math.Abs(balance):N2} 元";
            return "已结清";
        }

        private static string FormatBuyerBalanceLabel(string buyerName, decimal balance)
        {
            string name = string.IsNullOrWhiteSpace(buyerName) ? "该买家" : buyerName;
            if (balance > 0) return $"{name} 欠款 {balance:N2} 元";
            if (balance < 0) return $"{name} 有余额 {Math.Abs(balance):N2} 元";
            return $"{name} 已结清";
        }

        private void UpdateBuyerBalanceFromCurrentRow()
        {
            if (dgvBills.CurrentRow == null || dgvBills.CurrentRow.IsNewRow)
            {
                lblBuyerBalance.Text = "选中单据或筛选买家后显示欠款/余额";
                lblBuyerBalance.ForeColor = Color.Gray;
                return;
            }

            string buyerCode = dgvBills.CurrentRow.Cells["buyer_code"]?.Value?.ToString() ?? "";
            string buyerName = dgvBills.CurrentRow.Cells["buyer_name"]?.Value?.ToString() ?? "";
            UpdateBuyerBalanceDisplay(buyerCode, buyerName);
        }

        private void UpdateBuyerBalanceDisplay()
        {
            if (ClientSearchHelper.TryGetSelectedItem(cmbBuyer, _buyerItems, out ClientSearchItem buyerFilter, allowEmptySelection: true)
                && buyerFilter != null
                && !ClientSearchHelper.IsNoneFilter(buyerFilter))
            {
                UpdateBuyerBalanceDisplay(buyerFilter.Code, buyerFilter.Name);
                return;
            }

            UpdateBuyerBalanceFromCurrentRow();
        }

        private void UpdateBuyerBalanceDisplay(string buyerCode, string buyerName)
        {
            if (string.IsNullOrWhiteSpace(buyerCode) && string.IsNullOrWhiteSpace(buyerName))
            {
                lblBuyerBalance.Text = "选中单据或筛选买家后显示欠款/余额";
                lblBuyerBalance.ForeColor = Color.Gray;
                return;
            }

            decimal balance = db.GetPresaleBuyerBalance(buyerCode, buyerName);
            lblBuyerBalance.Text = FormatBuyerBalanceLabel(buyerName, balance);
            if (balance > 0)
                lblBuyerBalance.ForeColor = Color.FromArgb(180, 50, 50);
            else if (balance < 0)
                lblBuyerBalance.ForeColor = Color.FromArgb(4, 120, 87);
            else
                lblBuyerBalance.ForeColor = Color.Gray;
        }

        private static void SetHeader(DataGridView grid, string name, string header)
        {
            if (grid.Columns.Contains(name))
                grid.Columns[name].HeaderText = header;
        }

        private static void HideColumn(DataGridView grid, string name)
        {
            if (grid.Columns.Contains(name))
                grid.Columns[name].Visible = false;
        }

        private static void FormatMoneyColumns(DataGridView grid, params string[] names)
        {
            foreach (var name in names)
            {
                if (!grid.Columns.Contains(name)) continue;
                grid.Columns[name].DefaultCellStyle.Format = "N2";
            }
        }

        private void BtnEdit_Click(object sender, EventArgs e)
        {
            if (dgvBills.Rows.Count == 0 || originalData == null || originalData.Rows.Count == 0)
            {
                MessageBox.Show("没有数据可以编辑", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            isEditMode = true;
            btnEdit.Enabled = false;

            dgvBills.CellFormatting -= DgvBills_SaleModeCellFormatting;
            SetupSaleModeComboColumn();
            ConfigureGridEditMode(dgvBills);

            dgvBills.CellValueChanged += DgvBills_CellValueChanged;
            dgvBills.CellEndEdit += DgvBills_CellEndEdit;

            ApplyGridColumnLayout();
            lblStatus.Text = "编辑模式已启用 - 可编辑列已高亮，修改后请点击【保存】";
        }

        private void SetupSaleModeComboColumn()
        {
            if (!dgvBills.Columns.Contains("sale_mode"))
                return;

            if (dgvBills.Columns["sale_mode"] is DataGridViewComboBoxColumn)
                return;

            int displayIndex = dgvBills.Columns.Contains("stock_display")
                ? dgvBills.Columns["stock_display"].DisplayIndex
                : dgvBills.Columns["sale_mode"].DisplayIndex;
            var comboColumn = new DataGridViewComboBoxColumn
            {
                Name = "sale_mode",
                HeaderText = "模式",
                DataPropertyName = "sale_mode",
                DisplayStyle = DataGridViewComboBoxDisplayStyle.Nothing,
                FlatStyle = FlatStyle.Flat,
                DisplayIndex = displayIndex
            };
            comboColumn.Items.AddRange(new object[] { PresaleHelper.SaleModePresaleLabel, PresaleHelper.SaleModeSoldLabel });

            dgvBills.Columns.Remove("sale_mode");
            dgvBills.Columns.Add(comboColumn);

            foreach (DataGridViewRow row in dgvBills.Rows)
            {
                if (row.IsNewRow) continue;
                row.Cells["sale_mode"].Value = PresaleHelper.FormatSaleModeDisplay(row.Cells["sale_mode"].Value);
            }
        }

        private void RestoreSaleModeDisplayColumn()
        {
            if (!dgvBills.Columns.Contains("sale_mode"))
                return;

            DataGridViewColumn column = dgvBills.Columns["sale_mode"];
            if (column is DataGridViewComboBoxColumn)
            {
                int displayIndex = column.DisplayIndex;
                dgvBills.Columns.Remove(column);
                dgvBills.Columns.Add(new DataGridViewTextBoxColumn
                {
                    Name = "sale_mode",
                    HeaderText = "模式",
                    DataPropertyName = "sale_mode",
                    ReadOnly = true,
                    DisplayIndex = displayIndex
                });
            }

            dgvBills.CellFormatting -= DgvBills_SaleModeCellFormatting;
            dgvBills.CellFormatting += DgvBills_SaleModeCellFormatting;
        }

        private void DgvBills_SaleModeCellFormatting(object sender, DataGridViewCellFormattingEventArgs e)
        {
            if (isEditMode || e.RowIndex < 0 || e.ColumnIndex < 0)
                return;
            if (!dgvBills.Columns.Contains("sale_mode") || e.ColumnIndex != dgvBills.Columns["sale_mode"].Index)
                return;

            e.Value = PresaleHelper.FormatSaleModeDisplay(e.Value);
        }

        private void ConfigureGridEditMode(DataGridView grid)
        {
            grid.ReadOnly = false;
            grid.AllowUserToAddRows = false;
            grid.AllowUserToDeleteRows = false;

            foreach (DataGridViewColumn column in grid.Columns)
            {
                bool isItemColumn = EditableItemColumns.Contains(column.Name);
                bool isBillColumn = EditableBillColumns.Contains(column.Name);

                if (isItemColumn)
                {
                    column.ReadOnly = false;
                    foreach (DataGridViewRow row in grid.Rows)
                    {
                        if (row.IsNewRow) continue;
                        row.Cells[column.Index].ReadOnly = !TryGetItemId(row, out _);
                    }
                    column.HeaderCell.Style.BackColor = Color.FromArgb(255, 255, 200);
                    column.HeaderCell.Style.ForeColor = Color.Black;
                }
                else if (isBillColumn)
                {
                    column.ReadOnly = false;
                    column.HeaderCell.Style.BackColor = Color.FromArgb(255, 255, 200);
                    column.HeaderCell.Style.ForeColor = Color.Black;
                }
                else
                {
                    column.ReadOnly = true;
                    column.HeaderCell.Style.BackColor = Color.FromArgb(142, 68, 173);
                    column.HeaderCell.Style.ForeColor = Color.White;
                }
            }
        }

        private static bool TryGetItemId(DataGridViewRow row, out int itemId)
        {
            itemId = 0;
            if (!row.DataGridView.Columns.Contains("item_id"))
                return false;

            object value = row.Cells["item_id"].Value;
            if (value == null || value == DBNull.Value)
                return false;

            return int.TryParse(value.ToString(), out itemId) && itemId > 0;
        }

        private void DgvBills_CellValueChanged(object sender, DataGridViewCellEventArgs e)
        {
            if (!isEditMode || e.RowIndex < 0 || e.ColumnIndex < 0) return;

            string columnName = dgvBills.Columns[e.ColumnIndex].Name;
            if (columnName == "quantity" || columnName == "unit_price")
            {
                if (!dgvBills.Columns.Contains("quantity")
                    || !dgvBills.Columns.Contains("unit_price")
                    || !dgvBills.Columns.Contains("item_amount"))
                    return;

                decimal quantity = ParseDecimal(dgvBills.Rows[e.RowIndex].Cells["quantity"].Value);
                decimal unitPrice = ParseDecimal(dgvBills.Rows[e.RowIndex].Cells["unit_price"].Value);
                dgvBills.Rows[e.RowIndex].Cells["item_amount"].Value = quantity * unitPrice;
                RecalculateBillTotalsInGrid(e.RowIndex);
                return;
            }

            if (columnName == "paid_amount")
                RecalculateBillDebtStatusInGrid(e.RowIndex);
        }

        private void RecalculateBillTotalsInGrid(int changedRowIndex)
        {
            if (!TryGetBillIdFromGridRow(changedRowIndex, out int billId))
                return;

            decimal totalAmount = 0m;
            for (int i = 0; i < dgvBills.Rows.Count; i++)
            {
                if (dgvBills.Rows[i].IsNewRow || !TryGetBillIdFromGridRow(i, out int rowBillId) || rowBillId != billId)
                    continue;

                totalAmount += ParseDecimal(dgvBills.Rows[i].Cells["item_amount"].Value);
            }

            for (int i = 0; i < dgvBills.Rows.Count; i++)
            {
                if (dgvBills.Rows[i].IsNewRow || !TryGetBillIdFromGridRow(i, out int rowBillId) || rowBillId != billId)
                    continue;

                dgvBills.Rows[i].Cells["total_amount"].Value = totalAmount;
                decimal paidAmount = ParseDecimal(dgvBills.Rows[i].Cells["paid_amount"].Value);
                dgvBills.Rows[i].Cells["debt_status"].Value = FormatBillDebtStatusFromAmounts(totalAmount, paidAmount);
            }

            ColorizeDebtStatusColumn();
            dgvBills.Invalidate();
        }

        private void RecalculateBillDebtStatusInGrid(int changedRowIndex)
        {
            if (!TryGetBillIdFromGridRow(changedRowIndex, out int billId))
                return;

            for (int i = 0; i < dgvBills.Rows.Count; i++)
            {
                if (dgvBills.Rows[i].IsNewRow || !TryGetBillIdFromGridRow(i, out int rowBillId) || rowBillId != billId)
                    continue;

                decimal totalAmount = ParseDecimal(dgvBills.Rows[i].Cells["total_amount"].Value);
                decimal paidAmount = ParseDecimal(dgvBills.Rows[i].Cells["paid_amount"].Value);
                dgvBills.Rows[i].Cells["debt_status"].Value = FormatBillDebtStatusFromAmounts(totalAmount, paidAmount);
            }

            ColorizeDebtStatusColumn();
            dgvBills.Invalidate();
        }

        private bool TryGetBillIdFromGridRow(int rowIndex, out int billId)
        {
            billId = 0;
            if (rowIndex < 0 || rowIndex >= dgvBills.Rows.Count || dgvBills.Rows[rowIndex].IsNewRow)
                return false;
            if (!dgvBills.Columns.Contains("bill_id"))
                return false;

            object value = dgvBills.Rows[rowIndex].Cells["bill_id"].Value;
            if (value == null || value == DBNull.Value)
                return false;

            return int.TryParse(value.ToString(), out billId) && billId > 0;
        }

        private void DgvBills_CellEndEdit(object sender, DataGridViewCellEventArgs e)
        {
            dgvBills.CommitEdit(DataGridViewDataErrorContexts.Commit);
        }

        private void BtnSave_Click(object sender, EventArgs e)
        {
            if (!isEditMode)
            {
                MessageBox.Show("当前不是编辑模式", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            try
            {
                dgvBills.EndEdit();

                var billChanges = CollectBillChanges();
                var itemChanges = CollectItemChanges();

                if (billChanges.Count == 0 && itemChanges.Count == 0)
                {
                    if (QueryUiHelper.ConfirmExitEditWhenNoChanges(this))
                    {
                        ExitEditMode();
                        lblStatus.Text = "编辑模式已退出";
                    }
                    return;
                }

                string summary = BuildChangeSummary(billChanges, itemChanges);
                if (MessageBox.Show(
                        $"检测到 {billChanges.Count} 条主单、{itemChanges.Count} 条明细有修改：\n\n{summary}\n\n确定要保存吗？",
                        "确认保存", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK)
                    return;

                if (!TryNormalizeSaleModeChanges(billChanges, out string saleModeError))
                {
                    MessageBox.Show(saleModeError, "保存被拒绝", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                Cursor = Cursors.WaitCursor;

                int savedCount = 0;
                var errors = new List<string>();
                var billsNeedingTotalRecalc = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var changedBillNos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (var row in billChanges)
                {
                    try
                    {
                        if (SaveRowChanges("presale_bills", row.Id, row.Changes))
                        {
                            savedCount++;
                            ApplyBillChangesToOriginalRows(row.Id, row.Changes);
                            if (!string.IsNullOrWhiteSpace(row.BillNo))
                                changedBillNos.Add(row.BillNo);
                        }
                        else
                        {
                            errors.Add($"主单 (ID={row.Id}, 单号:{row.BillNo})：更新失败");
                        }
                    }
                    catch (Exception ex)
                    {
                        errors.Add($"主单 (ID={row.Id}, 单号:{row.BillNo})：{ex.Message}");
                    }
                }

                foreach (var row in itemChanges)
                {
                    try
                    {
                        if (SaveItemRowChanges(row.Id, row.Changes))
                        {
                            savedCount++;
                            ApplyChangesToOriginalRow(originalData.Rows[row.RowIndex], row.Changes);
                            if (!string.IsNullOrWhiteSpace(row.BillNo))
                                billsNeedingTotalRecalc.Add(row.BillNo);
                        }
                        else
                        {
                            errors.Add($"明细第{row.RowIndex + 1}行(ID={row.Id})：更新失败");
                        }
                    }
                    catch (Exception ex)
                    {
                        errors.Add($"明细第{row.RowIndex + 1}行(ID={row.Id})：{ex.Message}");
                    }
                }

                foreach (string billNo in billsNeedingTotalRecalc)
                    db.RecalculatePresaleBillTotal(billNo);

                foreach (string billNo in changedBillNos)
                    db.NotifyPresaleBillChanged(billNo);
                foreach (string billNo in billsNeedingTotalRecalc)
                    db.NotifyPresaleBillChanged(billNo);

                ExitEditMode();
                LoadData();

                string message = $"保存成功！共保存了 {savedCount} 条记录。";
                if (errors.Count > 0)
                {
                    message += $"\n\n错误信息：\n{string.Join("\n", errors)}";
                    MessageBox.Show(message, "保存完成（有错误）", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
                else
                {
                    MessageBox.Show(message, "保存成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"保存失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                Cursor = Cursors.Default;
            }
        }

        private List<ModifiedRowInfo> CollectBillChanges()
        {
            var modifiedRows = new List<ModifiedRowInfo>();
            if (originalData == null) return modifiedRows;

            var billChangesMap = new Dictionary<int, ModifiedRowInfo>();

            for (int i = 0; i < dgvBills.Rows.Count; i++)
            {
                if (dgvBills.Rows[i].IsNewRow || i >= originalData.Rows.Count) continue;

                var originalRow = originalData.Rows[i];
                if (originalRow["bill_id"] == DBNull.Value) continue;

                int billId = Convert.ToInt32(originalRow["bill_id"]);
                var changes = CollectGridRowChanges(dgvBills.Rows[i], originalRow, EditableBillColumns);
                if (changes.Count == 0) continue;

                if (billChangesMap.TryGetValue(billId, out ModifiedRowInfo existing))
                {
                    foreach (var change in changes)
                        existing.Changes[change.Key] = change.Value;
                }
                else
                {
                    billChangesMap[billId] = new ModifiedRowInfo
                    {
                        RowIndex = i,
                        Id = billId,
                        BillNo = originalRow["bill_no"]?.ToString(),
                        Changes = changes
                    };
                }
            }

            modifiedRows.AddRange(billChangesMap.Values);
            return modifiedRows;
        }

        private List<ModifiedRowInfo> CollectItemChanges()
        {
            var modifiedRows = new List<ModifiedRowInfo>();
            if (originalData == null) return modifiedRows;

            for (int i = 0; i < dgvBills.Rows.Count; i++)
            {
                if (dgvBills.Rows[i].IsNewRow || i >= originalData.Rows.Count) continue;

                var originalRow = originalData.Rows[i];
                if (originalRow["item_id"] == DBNull.Value) continue;

                var changes = CollectGridRowChanges(dgvBills.Rows[i], originalRow, EditableItemColumns);
                if (changes.Count == 0) continue;

                modifiedRows.Add(new ModifiedRowInfo
                {
                    RowIndex = i,
                    Id = Convert.ToInt32(originalRow["item_id"]),
                    BillNo = originalRow["bill_no"]?.ToString(),
                    Changes = changes
                });
            }

            return modifiedRows;
        }

        private static Dictionary<string, object> CollectGridRowChanges(
            DataGridViewRow gridRow,
            DataRow originalRow,
            HashSet<string> editableColumns)
        {
            var changes = new Dictionary<string, object>();
            foreach (DataGridViewColumn column in gridRow.DataGridView.Columns)
            {
                if (!editableColumns.Contains(column.Name)) continue;
                if (!originalRow.Table.Columns.Contains(column.Name)) continue;

                object currentValue = NormalizeCellValue(column.Name, gridRow.Cells[column.Index].Value);
                object originalValue = originalRow[column.Name];
                if (ValuesAreDifferent(currentValue, originalValue))
                    changes[column.Name] = currentValue;
            }

            return changes;
        }

        private bool SaveRowChanges(string tableName, int id, Dictionary<string, object> changes)
        {
            if (changes.Count == 0) return false;

            var setClauses = new List<string>();
            var parameters = new Dictionary<string, object>();

            foreach (var change in changes)
            {
                setClauses.Add($"[{change.Key}] = @{change.Key}");
                parameters[$"@{change.Key}"] = change.Value ?? DBNull.Value;
            }

            parameters["@id"] = id;
            string sql = $"UPDATE {tableName} SET {string.Join(", ", setClauses)} WHERE id = @id";
            return db.ExecuteNonQuery(sql, parameters) > 0;
        }

        private bool SaveItemRowChanges(int itemId, Dictionary<string, object> changes)
        {
            if (changes.Count == 0) return false;

            var dbChanges = new Dictionary<string, object>(changes);
            if (dbChanges.ContainsKey("item_amount"))
            {
                dbChanges["total_amount"] = dbChanges["item_amount"];
                dbChanges.Remove("item_amount");
            }

            return SaveRowChanges("presale_items", itemId, dbChanges);
        }

        private void ApplyBillChangesToOriginalRows(int billId, Dictionary<string, object> changes)
        {
            if (originalData == null) return;

            foreach (DataRow row in originalData.Rows)
            {
                if (row["bill_id"] == DBNull.Value) continue;
                if (Convert.ToInt32(row["bill_id"]) != billId) continue;
                ApplyChangesToOriginalRow(row, changes);
            }
        }

        private static void ApplyChangesToOriginalRow(DataRow originalRow, Dictionary<string, object> changes)
        {
            foreach (var change in changes)
            {
                if (originalRow.Table.Columns.Contains(change.Key))
                    originalRow[change.Key] = change.Value ?? DBNull.Value;
            }
        }

        private static string BuildChangeSummary(List<ModifiedRowInfo> billChanges, List<ModifiedRowInfo> itemChanges)
        {
            var sb = new StringBuilder();

            foreach (var row in billChanges.Take(5))
            {
                sb.AppendLine($"主单 (ID:{row.Id}, 单号:{row.BillNo}):");
                foreach (var change in row.Changes)
                    sb.AppendLine($"  - {change.Key}: {change.Value}");
            }

            if (billChanges.Count > 5)
                sb.AppendLine($"... 另有 {billChanges.Count - 5} 条主单修改");

            foreach (var row in itemChanges.Take(5))
            {
                sb.AppendLine($"明细第{row.RowIndex + 1}行 (ID:{row.Id}, 单号:{row.BillNo}):");
                foreach (var change in row.Changes)
                    sb.AppendLine($"  - {change.Key}: {change.Value}");
            }

            if (itemChanges.Count > 5)
                sb.AppendLine($"... 另有 {itemChanges.Count - 5} 条明细修改");

            return sb.ToString().TrimEnd();
        }

        private void BtnCancel_Click(object sender, EventArgs e)
        {
            if (!isEditMode) return;

            if (MessageBox.Show("确定要取消编辑吗？所有未保存的修改将丢失。",
                    "确认取消", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK)
                return;

            ExitEditMode();
            LoadData();
        }

        private void ExitEditMode()
        {
            isEditMode = false;
            btnEdit.Enabled = true;

            dgvBills.CellValueChanged -= DgvBills_CellValueChanged;
            dgvBills.CellEndEdit -= DgvBills_CellEndEdit;

            RestoreSaleModeDisplayColumn();
            dgvBills.ReadOnly = true;

            ResetGridHeaderStyles(dgvBills);
            ApplyGridColumnLayout();
        }

        private static void ResetGridHeaderStyles(DataGridView grid)
        {
            foreach (DataGridViewColumn column in grid.Columns)
            {
                column.HeaderCell.Style.BackColor = Color.FromArgb(142, 68, 173);
                column.HeaderCell.Style.ForeColor = Color.White;
            }
        }

        private bool TryNormalizeSaleModeChanges(List<ModifiedRowInfo> billChanges, out string error)
        {
            error = null;
            if (originalData == null || billChanges == null) return true;

            foreach (var row in billChanges)
            {
                if (!row.Changes.ContainsKey("sale_mode")) continue;

                DataRow originalRow = FindOriginalBillRow(row.Id);
                if (originalRow == null) continue;

                string oldMode = PresaleHelper.ToDbSaleMode(originalRow["sale_mode"]);
                string newMode = PresaleHelper.ToDbSaleMode(row.Changes["sale_mode"]);
                if (string.IsNullOrWhiteSpace(newMode)) continue;

                if (string.Equals(oldMode, PresaleHelper.SaleModeDirectOut, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(newMode, PresaleHelper.SaleModePresale, StringComparison.OrdinalIgnoreCase))
                {
                    error = $"单号 {row.BillNo ?? row.Id.ToString()}：不允许将已售改回预售。";
                    return false;
                }

                if (string.Equals(oldMode, PresaleHelper.SaleModePresale, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(newMode, PresaleHelper.SaleModeDirectOut, StringComparison.OrdinalIgnoreCase))
                {
                    string currentStatus = row.Changes.ContainsKey("status")
                        ? row.Changes["status"]?.ToString()
                        : originalRow["status"]?.ToString();
                    if (string.Equals(currentStatus, "PRESALE", StringComparison.OrdinalIgnoreCase))
                        row.Changes["status"] = "COMPLETED";
                }

                row.Changes["sale_mode"] = newMode;
            }

            return true;
        }

        private DataRow FindOriginalBillRow(int billId)
        {
            if (originalData == null) return null;
            foreach (DataRow row in originalData.Rows)
            {
                if (row["bill_id"] == DBNull.Value) continue;
                if (Convert.ToInt32(row["bill_id"]) == billId)
                    return row;
            }
            return null;
        }

        private static object NormalizeCellValue(string columnName, object value)
        {
            if (value == null || value == DBNull.Value)
                return DBNull.Value;

            if (columnName.Equals("quantity", StringComparison.OrdinalIgnoreCase))
                return (int)Math.Round(ParseDecimal(value));

            if (columnName.Equals("unit_price", StringComparison.OrdinalIgnoreCase)
                || columnName.Equals("item_amount", StringComparison.OrdinalIgnoreCase)
                || columnName.Equals("total_amount", StringComparison.OrdinalIgnoreCase)
                || columnName.Equals("paid_amount", StringComparison.OrdinalIgnoreCase))
                return ParseDecimal(value);

            if (columnName.Equals("sale_mode", StringComparison.OrdinalIgnoreCase))
                return PresaleHelper.ToDbSaleMode(value);

            return value.ToString()?.Trim() ?? string.Empty;
        }

        private static decimal ParseDecimal(object value)
        {
            if (value == null || value == DBNull.Value) return 0m;
            decimal.TryParse(value.ToString(), out decimal result);
            return result;
        }

        private static bool ValuesAreDifferent(object currentValue, object originalValue)
        {
            if (currentValue == DBNull.Value && originalValue == DBNull.Value) return false;
            if (currentValue == DBNull.Value || originalValue == DBNull.Value) return true;

            if (currentValue is decimal || originalValue is decimal
                || currentValue is double || originalValue is double
                || currentValue is float || originalValue is float
                || currentValue is int || originalValue is int)
            {
                return ParseDecimal(currentValue) != ParseDecimal(originalValue);
            }

            return !string.Equals(Convert.ToString(currentValue), Convert.ToString(originalValue), StringComparison.Ordinal);
        }

        private class ModifiedRowInfo
        {
            public int RowIndex { get; set; }
            public int Id { get; set; }
            public string BillNo { get; set; }
            public Dictionary<string, object> Changes { get; set; }
        }
    }
}
