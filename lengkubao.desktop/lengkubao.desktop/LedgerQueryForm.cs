using System;
using System.Collections.Generic;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace lengkubao.desktop
{
    public class LedgerQueryForm : Form
    {
        private DatabaseManager db;
        private DataGridView dgvEntries;
        private Label lblStatus;
        private Label lblSummary;
        private DateTimePicker dtpStartDate;
        private DateTimePicker dtpEndDate;
        private ComboBox cmbType;
        private ComboBox cmbCategory;
        private Button btnQuery;
        private Button btnEdit;
        private Button btnSave;

        private List<ClientSearchItem> _categoryItems = new List<ClientSearchItem>();

        private DataTable originalEntriesData;
        private bool isEditMode;

        private static readonly HashSet<string> EditableLedgerColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "entry_no", "type", "category_name", "amount", "entry_date", "remark"
        };

        public LedgerQueryForm()
        {
            db = new DatabaseManager();
            InitializeForm();
            LoadFilterData();
            LoadData();
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
            this.Text = "收支流水";
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
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));

            Panel headerPanel = QueryUiHelper.CreateModuleHeader(QueryModule.Ledger, "收支流水查询", out lblStatus);
            mainLayout.Controls.Add(headerPanel, 0, 0);

            Panel filterPanel = new Panel { Dock = DockStyle.Fill };
            QueryUiHelper.StyleFilterPanel(filterPanel);

            dtpStartDate = new DateTimePicker { Width = 132 };
            dtpEndDate = new DateTimePicker { Width = 132 };
            QueryUiHelper.StyleDatePicker(dtpStartDate);
            QueryUiHelper.StyleDatePicker(dtpEndDate);
            DateRangeSettings.ApplyTo(dtpStartDate, dtpEndDate);

            cmbType = new ComboBox { Width = 100, DropDownStyle = ComboBoxStyle.DropDownList };
            cmbType.Items.AddRange(new object[] { "全部", "收入", "支出" });
            cmbType.SelectedIndex = 0;
            QueryUiHelper.StyleComboBox(cmbType);

            cmbCategory = CreateFilterComboBox(140);

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

                cmbType.SelectedIndex = 0;
                ClientSearchHelper.ResetToPlaceholder(cmbCategory);
                DateRangeSettings.ApplyFactoryDefaultTo(dtpStartDate, dtpEndDate);
                LoadFilterData();
                LoadData();
            };

            filterPanel.Controls.Add(QueryUiHelper.CreateTwoRowQueryFilterPanel(
                QueryUiHelper.CreateFilterField("开始日期:", dtpStartDate, 132),
                QueryUiHelper.CreateFilterField("结束日期:", dtpEndDate, 132),
                new[]
                {
                    QueryUiHelper.CreateFilterField("类 型:", cmbType, 100),
                    QueryUiHelper.CreateFilterField("类 目:", cmbCategory, 140)
                },
                btnQuery,
                btnReset));
            mainLayout.Controls.Add(filterPanel, 0, 1);

            Panel infoPanel = new Panel { Dock = DockStyle.Fill };
            QueryUiHelper.StyleInfoBar(infoPanel);
            lblSummary = new Label { AutoSize = true, Text = "收入: 0.00  支出: 0.00  利润: 0.00" };
            QueryUiHelper.StyleFilterSummaryLabel(lblSummary, QueryModule.Ledger);
            infoPanel.Controls.Add(lblSummary);
            mainLayout.Controls.Add(infoPanel, 0, 2);

            dgvEntries = new DataGridView();
            QueryUiHelper.ApplyQueryGrid(dgvEntries, QueryModule.Ledger);
            dgvEntries.CellFormatting += DgvEntries_CellFormatting;
            mainLayout.Controls.Add(QueryUiHelper.CreateGridHost(dgvEntries), 0, 3);

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

        private void LoadFilterData()
        {
            try
            {
                string start = dtpStartDate.Value.ToString("yyyy-MM-dd");
                string end = dtpEndDate.Value.ToString("yyyy-MM-dd");

                _categoryItems = ClientSearchHelper.BuildDistinctFilterItems(
                    db.GetLedgerDistinctValues("category_name", start, end));

                ClientSearchHelper.BindSearchableCombo(cmbCategory, _categoryItems, FilterComboOptions);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"加载筛选数据失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void RefreshFilterOptionsFromData(DataTable entries)
        {
            if (entries == null || entries.Rows.Count == 0)
                return;

            ClientSearchHelper.MergeDistinctFromDataTable(_categoryItems, entries, "category_name");

            ClientSearchHelper.BindSearchableCombo(cmbCategory, _categoryItems, FilterComboOptions);
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

        private string GetTypeFilter()
        {
            if (cmbType.SelectedIndex == 1) return "INCOME";
            if (cmbType.SelectedIndex == 2) return "EXPENSE";
            return string.Empty;
        }

        private void LoadData()
        {
            try
            {
                Cursor = Cursors.WaitCursor;
                ExitEditMode();

                string start = dtpStartDate.Value.ToString("yyyy-MM-dd");
                string end = dtpEndDate.Value.ToString("yyyy-MM-dd");
                string typeFilter = GetTypeFilter();
                string category = ResolveFilterValue(cmbCategory, _categoryItems);

                decimal income;
                decimal expense;
                db.GetLedgerSummaryForQuery(start, end, typeFilter, category, out income, out expense);
                lblSummary.Text = $"收入: {income:N2}  支出: {expense:N2}  利润: {(income - expense):N2}";

                var data = db.GetLedgerEntriesForQuery(start, end, typeFilter, category);
                RefreshFilterOptionsFromData(data);

                originalEntriesData = data?.Copy() ?? new DataTable();
                dgvEntries.DataSource = data;
                FormatColumns();
                lblStatus.Text = $"收支流水 · 共 {data.Rows.Count} 条";
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

        private void FormatColumns()
        {
            SetHeader("entry_no", "流水号");
            SetHeader("type", "类型");
            SetHeader("category_name", "类目");
            SetHeader("amount", "金额");
            SetHeader("entry_date", "日期");
            SetHeader("remark", "备注");
            SetHeader("status", "状态");
            SetHeader("created_time", "创建时间");
            HideColumn("id");

            if (dgvEntries.Columns.Contains("amount"))
                dgvEntries.Columns["amount"].DefaultCellStyle.Format = "N2";

            QueryUiHelper.ApplyUniformColumnAlignment(dgvEntries);
        }

        private void DgvEntries_CellFormatting(object sender, DataGridViewCellFormattingEventArgs e)
        {
            if (e.RowIndex < 0 || e.ColumnIndex < 0) return;
            if (dgvEntries.Columns[e.ColumnIndex].Name != "type" || e.Value == null) return;

            string val = e.Value.ToString();
            if (val == "INCOME") e.Value = "收入";
            else if (val == "EXPENSE") e.Value = "支出";
        }

        private void BtnEdit_Click(object sender, EventArgs e)
        {
            if (dgvEntries.Rows.Count == 0 || originalEntriesData == null || originalEntriesData.Rows.Count == 0)
            {
                MessageBox.Show("没有数据可以编辑", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            isEditMode = true;
            btnEdit.Enabled = false;

            dgvEntries.ReadOnly = false;
            dgvEntries.AllowUserToAddRows = false;
            dgvEntries.AllowUserToDeleteRows = false;

            foreach (DataGridViewColumn column in dgvEntries.Columns)
            {
                column.ReadOnly = !EditableLedgerColumns.Contains(column.Name);
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

            lblStatus.Text = "编辑模式已启用 - 可编辑列已高亮，修改后请点击【保存】";
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
                dgvEntries.EndEdit();

                var modifiedRows = CollectEntryChanges();
                if (modifiedRows.Count == 0)
                {
                    if (QueryUiHelper.ConfirmExitEditWhenNoChanges(this))
                    {
                        ExitEditMode();
                        lblStatus.Text = "编辑模式已退出";
                    }
                    return;
                }

                string summary = BuildChangeSummary(modifiedRows);
                if (MessageBox.Show(
                        $"检测到 {modifiedRows.Count} 条记录有修改：\n\n{summary}\n\n确定要保存吗？",
                        "确认保存", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK)
                    return;

                Cursor = Cursors.WaitCursor;

                int savedCount = 0;
                var errors = new List<string>();

                foreach (var row in modifiedRows)
                {
                    try
                    {
                        if (SaveRowChanges(row.Id, row.Changes))
                        {
                            savedCount++;
                            ApplyChangesToOriginalRow(originalEntriesData.Rows[row.RowIndex], row.Changes);
                        }
                        else
                        {
                            errors.Add($"第{row.RowIndex + 1}行(ID={row.Id})：更新失败");
                        }
                    }
                    catch (Exception ex)
                    {
                        errors.Add($"第{row.RowIndex + 1}行(ID={row.Id})：{ex.Message}");
                    }
                }

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

        private List<ModifiedRowInfo> CollectEntryChanges()
        {
            var modifiedRows = new List<ModifiedRowInfo>();
            if (originalEntriesData == null) return modifiedRows;

            for (int i = 0; i < dgvEntries.Rows.Count; i++)
            {
                if (dgvEntries.Rows[i].IsNewRow || i >= originalEntriesData.Rows.Count) continue;

                var originalRow = originalEntriesData.Rows[i];
                var changes = new Dictionary<string, object>();

                foreach (DataGridViewColumn column in dgvEntries.Columns)
                {
                    if (!EditableLedgerColumns.Contains(column.Name)) continue;
                    if (!originalRow.Table.Columns.Contains(column.Name)) continue;

                    object currentValue = NormalizeCellValue(column.Name, dgvEntries.Rows[i].Cells[column.Index].Value);
                    object originalValue = originalRow[column.Name];

                    if (column.Name.Equals("type", StringComparison.OrdinalIgnoreCase))
                    {
                        currentValue = NormalizeLedgerType(currentValue);
                        originalValue = NormalizeLedgerType(originalValue);
                    }

                    if (ValuesAreDifferent(currentValue, originalValue))
                        changes[column.Name] = currentValue;
                }

                if (changes.Count == 0) continue;

                modifiedRows.Add(new ModifiedRowInfo
                {
                    RowIndex = i,
                    Id = Convert.ToInt32(originalRow["id"]),
                    Changes = changes
                });
            }

            return modifiedRows;
        }

        private bool SaveRowChanges(int id, Dictionary<string, object> changes)
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
            string sql = $"UPDATE ledger_entry SET {string.Join(", ", setClauses)} WHERE id = @id";
            return db.ExecuteNonQuery(sql, parameters) > 0;
        }

        private static void ApplyChangesToOriginalRow(DataRow originalRow, Dictionary<string, object> changes)
        {
            foreach (var change in changes)
            {
                if (originalRow.Table.Columns.Contains(change.Key))
                    originalRow[change.Key] = change.Value ?? DBNull.Value;
            }
        }

        private static string BuildChangeSummary(List<ModifiedRowInfo> modifiedRows)
        {
            var sb = new StringBuilder();
            foreach (var row in modifiedRows.Take(8))
            {
                sb.AppendLine($"第{row.RowIndex + 1}行 (ID:{row.Id}):");
                foreach (var change in row.Changes)
                    sb.AppendLine($"  - {change.Key}: {change.Value}");
            }

            if (modifiedRows.Count > 8)
                sb.AppendLine($"... 另有 {modifiedRows.Count - 8} 条修改");

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
            dgvEntries.ReadOnly = true;

            foreach (DataGridViewColumn column in dgvEntries.Columns)
            {
                column.HeaderCell.Style.BackColor = Color.FromArgb(142, 68, 173);
                column.HeaderCell.Style.ForeColor = Color.White;
            }
        }

        private void SetHeader(string name, string header)
        {
            if (dgvEntries.Columns.Contains(name))
                dgvEntries.Columns[name].HeaderText = header;
        }

        private void HideColumn(string name)
        {
            if (dgvEntries.Columns.Contains(name))
                dgvEntries.Columns[name].Visible = false;
        }

        private static object NormalizeCellValue(string columnName, object value)
        {
            if (value == null || value == DBNull.Value)
                return DBNull.Value;

            if (columnName.Equals("amount", StringComparison.OrdinalIgnoreCase))
                return ParseDecimal(value);

            if (columnName.Equals("type", StringComparison.OrdinalIgnoreCase))
                return NormalizeLedgerType(value);

            return value.ToString()?.Trim() ?? string.Empty;
        }

        private static string NormalizeLedgerType(object value)
        {
            string text = value?.ToString()?.Trim() ?? string.Empty;
            if (text == "收入" || text.Equals("INCOME", StringComparison.OrdinalIgnoreCase))
                return "INCOME";
            if (text == "支出" || text.Equals("EXPENSE", StringComparison.OrdinalIgnoreCase))
                return "EXPENSE";
            return text;
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
            public Dictionary<string, object> Changes { get; set; }
        }
    }
}
