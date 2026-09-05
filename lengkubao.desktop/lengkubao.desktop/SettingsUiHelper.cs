using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace lengkubao.desktop
{
    internal enum SettingsButtonRole
    {
        Primary,
        Success,
        Info,
        Warning,
        Danger,
        Secondary,
        Purple
    }

    /// <summary>
    /// 系统设置（客户/型号/包装/库位/经手人）界面统一样式与中文化。
    /// </summary>
    internal static class SettingsUiHelper
    {
        public static readonly Font ChromeFont = new Font("微软雅黑", 11f, FontStyle.Regular);
        public static readonly Font ChromeBoldFont = new Font("微软雅黑", 11f, FontStyle.Bold);
        public static readonly Font TitleFont = new Font("微软雅黑", 15f, FontStyle.Bold);
        public static readonly Font TabFont = new Font("微软雅黑", 12f, FontStyle.Regular);

        public static readonly Color Accent = Color.FromArgb(30, 58, 95);
        public static readonly Color PanelBack = StatisticsUiHelper.PanelBack;

        private static readonly Dictionary<string, string> CommonHeaders =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "id", "内部编号" },
                { "code", "编号" },
                { "name", "名称" },
                { "phone", "联系电话" },
                { "address", "地址" },
                { "description", "描述" },
                { "status", "状态" },
                { "is_active", "状态" },
                { "created_time", "创建时间" },
                { "updated_time", "更新时间" },
                { "updated_at", "更新时间" },
                { "remarks", "备注" },
                { "remark", "备注" }
            };

        private static readonly HashSet<string> HiddenColumns =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "id", "updated_at", "updated_time", "settled_by", "creator",
                "create_time", "update_time", "is_settled", "settled_time"
            };

        private static readonly HashSet<string> StatusFieldColumns =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "is_active", "status", "active", "enabled", "is_enabled", "is_settled"
            };

        private static readonly Dictionary<string, string> FieldTranslations =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "client_code", "客户编号" },
                { "client_name", "客户名称" },
                { "product_name", "产品名称" },
                { "product_type", "产品型号" },
                { "spec", "规格" },
                { "pack_type", "包装类型" },
                { "package_type", "包装类型" },
                { "pack_flag", "包装标记" },
                { "location", "库位" },
                { "warehouse", "仓库" },
                { "handler", "经手人" },
                { "operator", "操作员" },
                { "quantity", "数量" },
                { "unit_price", "单价" },
                { "total_amount", "总金额" },
                { "amount", "金额" },
                { "order_no", "单据号" },
                { "bill_no", "单据号" },
                { "date", "日期" },
                { "transaction_date", "交易日期" },
                { "sales_date", "销售日期" },
                { "inbound_date", "入库日期" },
                { "payment_status", "付款状态" },
                { "delivery_status", "发货状态" },
                { "settle_status", "结算状态" },
                { "source_device_id", "来源设备" },
                { "source_record_id", "来源记录" },
                { "notes", "备注" },
                { "description", "描述" },
                { "email", "电子邮箱" },
                { "contact", "联系人" },
                { "type", "类型" },
                { "sort_order", "排序" },
                { "sort", "排序" }
            };

        public static void ApplyFormChrome(Form form)
        {
            if (form == null)
                return;

            form.BackColor = PanelBack;
            form.Font = ChromeFont;
        }

        public static void StyleTabControl(TabControl tabs)
        {
            if (tabs == null)
                return;

            tabs.Font = TabFont;
            tabs.Padding = new Point(14, 8);
            tabs.DrawMode = TabDrawMode.Normal;
        }

        public static void StyleTabPage(TabPage tab)
        {
            if (tab == null)
                return;

            tab.BackColor = Color.White;
            tab.Padding = new Padding(12, 10, 12, 10);

            foreach (Control control in tab.Controls)
            {
                if (control is Label label && (label.Font?.Bold ?? false))
                    StyleTitleLabel(label);
                else if (control is GroupBox groupBox)
                    StyleGroupBox(groupBox);
                else if (control is TextBox textBox)
                    StyleSearchTextBox(textBox);
                else if (control is Button button)
                    StyleExistingButton(button);
                else if (control is DataGridView grid)
                    ApplySettingsGrid(grid);
                else if (control is ListBox listBox)
                    StyleListBox(listBox);
            }
        }

        public static void StyleTitleLabel(Label label)
        {
            if (label == null)
                return;

            label.Font = TitleFont;
            label.ForeColor = Accent;
        }

        public static void StyleGroupBox(GroupBox groupBox)
        {
            if (groupBox == null)
                return;

            groupBox.Font = ChromeBoldFont;
            groupBox.ForeColor = StatisticsUiHelper.CellFore;
        }

        public static void StyleSearchTextBox(TextBox textBox)
        {
            if (textBox == null)
                return;

            textBox.Font = ChromeFont;
            textBox.Height = Math.Max(textBox.Height, PlaceholderTextBoxHeight);
            textBox.BorderStyle = BorderStyle.FixedSingle;
        }

        public static void StyleListBox(ListBox listBox)
        {
            if (listBox == null)
                return;

            listBox.Font = new Font("微软雅黑", 12f, FontStyle.Regular);
            listBox.BorderStyle = BorderStyle.FixedSingle;
            listBox.BackColor = Color.White;
            listBox.IntegralHeight = false;
        }

        public static Button CreateButton(string text, SettingsButtonRole role, Size? size = null)
        {
            var button = new Button
            {
                Text = text,
                Size = size ?? new Size(100, 36),
                Font = ChromeBoldFont,
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand,
                BackColor = GetButtonColor(role)
            };
            button.FlatAppearance.BorderSize = 0;
            return button;
        }

        public static void StyleExistingButton(Button button)
        {
            if (button == null)
                return;

            button.Font = ChromeBoldFont;
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderSize = 0;
            button.Cursor = Cursors.Hand;
            button.Height = Math.Max(button.Height, 34);

            if (button.BackColor == SystemColors.Control || button.BackColor == Color.Transparent)
            {
                button.BackColor = GetButtonColor(SettingsButtonRole.Secondary);
                button.ForeColor = Color.White;
            }
        }

        public static Color GetButtonColor(SettingsButtonRole role)
        {
            switch (role)
            {
                case SettingsButtonRole.Success:
                    return Color.FromArgb(22, 163, 74);
                case SettingsButtonRole.Info:
                    return Color.FromArgb(37, 99, 235);
                case SettingsButtonRole.Warning:
                    return Color.FromArgb(217, 119, 6);
                case SettingsButtonRole.Danger:
                    return Color.FromArgb(220, 38, 38);
                case SettingsButtonRole.Purple:
                    return Color.FromArgb(124, 58, 237);
                case SettingsButtonRole.Secondary:
                    return Color.FromArgb(100, 116, 139);
                default:
                    return Color.FromArgb(30, 58, 95);
            }
        }

        public static void ApplySettingsGrid(DataGridView grid, Color? accent = null)
        {
            if (grid == null)
                return;

            StatisticsUiHelper.ApplyPremiumGridStyle(grid, accent ?? Accent);
            grid.ReadOnly = true;
            grid.AllowUserToResizeColumns = true;
            grid.RowTemplate.Height = 44;
            StatisticsUiHelper.ApplyUniformColumnAlignment(grid);

            grid.CellFormatting -= OnSettingsGridCellFormatting;
            grid.CellFormatting += OnSettingsGridCellFormatting;
        }

        /// <summary>数据绑定完成后自动刷新中文列名（避免 DataSource 重置列标题为英文）。</summary>
        public static void RegisterGridLocalization(DataGridView grid, Action<DataGridView> localize)
        {
            if (grid == null || localize == null)
                return;

            grid.Tag = localize;
            grid.DataBindingComplete -= OnGridDataBindingComplete;
            grid.DataBindingComplete += OnGridDataBindingComplete;
        }

        private static void OnGridDataBindingComplete(object sender, DataGridViewBindingCompleteEventArgs e)
        {
            if (sender is DataGridView grid && grid.Tag is Action<DataGridView> localize)
                localize(grid);
        }

        private static bool IsStatusField(string columnName) =>
            !string.IsNullOrEmpty(columnName) && StatusFieldColumns.Contains(columnName);

        /// <summary>从 DataGridView 单元格解析启用状态（兼容 1/0、bool、启用/禁用 文案）。</summary>
        public static bool TryParseGridActive(object cellValue, bool defaultValue = true)
        {
            if (cellValue == null || cellValue == DBNull.Value)
                return defaultValue;
            if (cellValue is bool flag)
                return flag;
            if (cellValue is int || cellValue is long || cellValue is short || cellValue is byte)
                return Convert.ToInt32(cellValue) != 0;
            string text = cellValue.ToString().Trim();
            if (string.Equals(text, "True", StringComparison.OrdinalIgnoreCase) || text == "1")
                return true;
            if (string.Equals(text, "False", StringComparison.OrdinalIgnoreCase) || text == "0")
                return false;
            if (text == "启用")
                return true;
            if (text == "禁用")
                return false;
            return bool.TryParse(text, out bool parsed) ? parsed : defaultValue;
        }

        private static string TranslateFieldName(string fieldName)
        {
            if (string.IsNullOrWhiteSpace(fieldName))
                return fieldName;

            if (FieldTranslations.TryGetValue(fieldName, out string translated))
                return translated;
            if (CommonHeaders.TryGetValue(fieldName, out string common))
                return common;

            return fieldName.Replace("_", " ");
        }

        private static void EnsureTextColumnsForStatusFields(DataGridView grid)
        {
            var checkColumns = new List<DataGridViewCheckBoxColumn>();
            foreach (DataGridViewColumn col in grid.Columns)
            {
                if (col is DataGridViewCheckBoxColumn checkCol && IsStatusField(checkCol.Name))
                    checkColumns.Add(checkCol);
            }

            foreach (DataGridViewCheckBoxColumn col in checkColumns)
            {
                int idx = col.Index;
                var textCol = new DataGridViewTextBoxColumn
                {
                    Name = col.Name,
                    DataPropertyName = col.DataPropertyName,
                    HeaderText = col.HeaderText,
                    DisplayIndex = col.DisplayIndex,
                    ReadOnly = col.ReadOnly,
                    Visible = col.Visible,
                    FillWeight = col.FillWeight,
                    MinimumWidth = col.MinimumWidth
                };
                grid.Columns.Remove(col);
                grid.Columns.Insert(idx, textCol);
            }
        }

        private static void OnSettingsGridCellFormatting(object sender, DataGridViewCellFormattingEventArgs e)
        {
            if (e.RowIndex < 0 || e.ColumnIndex < 0 || e.Value == null || e.Value == DBNull.Value)
                return;

            var grid = sender as DataGridView;
            if (grid == null)
                return;

            string colName = grid.Columns[e.ColumnIndex].Name;
            if (!IsStatusField(colName))
                return;

            if (e.Value is bool flag)
            {
                e.Value = flag ? "启用" : "禁用";
                e.FormattingApplied = true;
                return;
            }

            if (e.Value is int || e.Value is long || e.Value is short || e.Value is byte)
            {
                e.Value = Convert.ToInt32(e.Value) == 1 ? "启用" : "禁用";
                e.FormattingApplied = true;
                return;
            }

            string text = e.Value.ToString().Trim();
            if (string.Equals(text, "True", StringComparison.OrdinalIgnoreCase) || text == "1")
            {
                e.Value = "启用";
                e.FormattingApplied = true;
            }
            else if (string.Equals(text, "False", StringComparison.OrdinalIgnoreCase) || text == "0")
            {
                e.Value = "禁用";
                e.FormattingApplied = true;
            }
        }

        public static void LocalizeBoundColumns(DataGridView grid, IReadOnlyDictionary<string, string> headerMap = null,
            IReadOnlyCollection<string> visibleColumns = null)
        {
            if (grid == null || grid.Columns.Count == 0)
                return;

            EnsureTextColumnsForStatusFields(grid);

            HashSet<string> visibleSet = null;
            if (visibleColumns != null && visibleColumns.Count > 0)
            {
                visibleSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (string colName in visibleColumns)
                    visibleSet.Add(colName);
            }

            foreach (DataGridViewColumn col in grid.Columns)
            {
                string key = col.Name;
                if (headerMap != null && headerMap.TryGetValue(key, out string mapped))
                    col.HeaderText = mapped;
                else if (CommonHeaders.TryGetValue(key, out string common))
                    col.HeaderText = common;
                else
                    col.HeaderText = TranslateFieldName(key);

                if (string.Equals(col.HeaderText, key, StringComparison.OrdinalIgnoreCase)
                    && key.IndexOf('_') >= 0)
                    col.HeaderText = TranslateFieldName(key);

                if (HiddenColumns.Contains(key))
                    col.Visible = false;
                else if (visibleSet != null)
                    col.Visible = visibleSet.Contains(key);

                if (string.Equals(key, "created_time", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(key, "updated_time", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(key, "create_time", StringComparison.OrdinalIgnoreCase))
                {
                    col.DefaultCellStyle.Format = "yyyy-MM-dd HH:mm";
                }
            }

            StatisticsUiHelper.EnsureUniformGridCellFonts(grid);
            StatisticsUiHelper.ApplyUniformColumnAlignment(grid);
        }

        private static readonly string[] ProductVisibleColumns = { "code", "name", "is_active", "created_time" };
        private static readonly string[] PackTypeVisibleColumns = { "name", "is_active", "created_time" };

        public static void LocalizeProductTypeGrid(DataGridView grid)
        {
            if (grid == null)
                return;

            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "code", "型号编号" },
                { "name", "型号名称" },
                { "is_active", "状态" },
                { "created_time", "创建时间" }
            };
            LocalizeBoundColumns(grid, map, ProductVisibleColumns);

            if (grid.Columns["code"] != null)
                grid.Columns["code"].DisplayIndex = 0;
            if (grid.Columns["name"] != null)
                grid.Columns["name"].DisplayIndex = 1;
            if (grid.Columns["is_active"] != null)
                grid.Columns["is_active"].DisplayIndex = 2;
            if (grid.Columns["created_time"] != null)
                grid.Columns["created_time"].DisplayIndex = 3;

            if (!(grid.Tag is Action<DataGridView>))
                RegisterGridLocalization(grid, LocalizeProductTypeGrid);
        }

        public static void LocalizePackTypeGrid(DataGridView grid)
        {
            if (grid == null)
                return;

            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "name", "包装类型名称" },
                { "is_active", "状态" },
                { "created_time", "创建时间" }
            };
            LocalizeBoundColumns(grid, map, PackTypeVisibleColumns);

            if (grid.Columns["name"] != null)
                grid.Columns["name"].DisplayIndex = 0;
            if (grid.Columns["is_active"] != null)
                grid.Columns["is_active"].DisplayIndex = 1;
            if (grid.Columns["created_time"] != null)
                grid.Columns["created_time"].DisplayIndex = 2;

            if (!(grid.Tag is Action<DataGridView>))
                RegisterGridLocalization(grid, LocalizePackTypeGrid);
        }

        public static void LocalizeManualCustomerGrid(DataGridView grid)
        {
            if (grid == null)
                return;

            ApplySettingsGrid(grid);
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "code", "客户编号" },
                { "name", "客户名称" },
                { "phone", "联系电话" },
                { "address", "地址" }
            };
            LocalizeBoundColumns(grid, map, new[] { "code", "name", "phone", "address" });
        }

        public static void LocalizeLocationGrid(DataGridView grid)
        {
            if (grid == null)
                return;

            ApplySettingsGrid(grid);
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "status", "状态" },
                { "name", "库位名称" },
                { "description", "描述" }
            };
            LocalizeBoundColumns(grid, map, new[] { "status", "name", "description" });
        }

        public static void LocalizeHandlerGrid(DataGridView grid)
        {
            if (grid == null)
                return;

            ApplySettingsGrid(grid);
            if (grid.Columns["status"] != null)
            {
                grid.Columns["status"].HeaderText = "状态";
                grid.Columns["status"].FillWeight = 70;
                grid.Columns["status"].MinimumWidth = 72;
            }
            if (grid.Columns["name"] != null)
            {
                grid.Columns["name"].HeaderText = "经手人姓名";
                grid.Columns["name"].FillWeight = 200;
            }
            if (grid.Columns["id"] != null)
                grid.Columns["id"].Visible = false;
        }

        public static void LocalizeBuyerGrid(DataGridView grid)
        {
            if (grid == null)
                return;

            ApplySettingsGrid(grid);
            if (grid.Columns["status"] != null)
            {
                grid.Columns["status"].HeaderText = "状态";
                grid.Columns["status"].FillWeight = 70;
                grid.Columns["status"].MinimumWidth = 72;
                grid.Columns["status"].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
            }
            if (grid.Columns["name"] != null)
            {
                grid.Columns["name"].HeaderText = "买家名称";
                grid.Columns["name"].FillWeight = 160;
            }
            if (grid.Columns["phone"] != null)
            {
                grid.Columns["phone"].HeaderText = "联系电话";
                grid.Columns["phone"].FillWeight = 120;
                grid.Columns["phone"].MinimumWidth = 96;
            }
            if (grid.Columns["code"] != null)
            {
                grid.Columns["code"].HeaderText = "买家编号";
                grid.Columns["code"].FillWeight = 90;
            }
        }

        private const int PlaceholderTextBoxHeight = 36;
        private static readonly Color PlaceholderForeColor = Color.FromArgb(150, 150, 150);

        private sealed class PlaceholderState
        {
            public string Placeholder { get; set; }
            public bool IsShowingPlaceholder { get; set; }
        }

        public static TextBox CreatePlaceholderTextBox(string placeholder, int width = 312, string initialValue = null)
        {
            var textBox = new TextBox
            {
                Width = width,
                Height = PlaceholderTextBoxHeight,
                Font = ChromeFont,
                BorderStyle = BorderStyle.FixedSingle
            };
            BindPlaceholder(textBox, placeholder, initialValue);
            return textBox;
        }

        public static void BindPlaceholder(TextBox textBox, string placeholder, string initialValue = null)
        {
            if (textBox == null)
                return;

            textBox.Height = Math.Max(textBox.Height, PlaceholderTextBoxHeight);
            textBox.Font = ChromeFont;
            textBox.BorderStyle = BorderStyle.FixedSingle;

            var state = new PlaceholderState
            {
                Placeholder = NormalizePlaceholder(placeholder)
            };
            textBox.Tag = state;

            textBox.GotFocus -= PlaceholderTextBox_GotFocus;
            textBox.LostFocus -= PlaceholderTextBox_LostFocus;
            textBox.GotFocus += PlaceholderTextBox_GotFocus;
            textBox.LostFocus += PlaceholderTextBox_LostFocus;

            if (!string.IsNullOrWhiteSpace(initialValue))
            {
                textBox.Text = initialValue;
                textBox.ForeColor = SystemColors.WindowText;
                state.IsShowingPlaceholder = false;
            }
            else
            {
                ShowPlaceholder(textBox, state);
            }
        }

        public static string GetEffectiveText(TextBox textBox)
        {
            if (textBox == null)
                return string.Empty;

            if (textBox.Tag is PlaceholderState state && state.IsShowingPlaceholder)
                return string.Empty;

            return textBox.Text?.Trim() ?? string.Empty;
        }

        public static void ResetPlaceholder(TextBox textBox)
        {
            if (textBox?.Tag is PlaceholderState state)
                ShowPlaceholder(textBox, state);
        }

        private static string NormalizePlaceholder(string placeholder)
        {
            if (string.IsNullOrWhiteSpace(placeholder))
                return "请输入";

            return placeholder.Trim().TrimEnd(':', '：');
        }

        private static void ShowPlaceholder(TextBox textBox, PlaceholderState state)
        {
            state.IsShowingPlaceholder = true;
            textBox.ForeColor = PlaceholderForeColor;
            textBox.Text = state.Placeholder;
        }

        private static void ClearPlaceholder(TextBox textBox, PlaceholderState state)
        {
            state.IsShowingPlaceholder = false;
            textBox.ForeColor = SystemColors.WindowText;
            textBox.Text = string.Empty;
        }

        private static void PlaceholderTextBox_GotFocus(object sender, EventArgs e)
        {
            if (!(sender is TextBox textBox) || !(textBox.Tag is PlaceholderState state))
                return;

            if (state.IsShowingPlaceholder)
                ClearPlaceholder(textBox, state);
        }

        private static void PlaceholderTextBox_LostFocus(object sender, EventArgs e)
        {
            if (!(sender is TextBox textBox) || !(textBox.Tag is PlaceholderState state))
                return;

            if (string.IsNullOrWhiteSpace(textBox.Text))
                ShowPlaceholder(textBox, state);
        }

        public static string ShowInputDialog(string prompt, string title, string defaultValue = "", IWin32Window owner = null)
        {
            using (var form = new Form())
            using (var textBox = new TextBox())
            using (var buttonOk = new Button())
            using (var buttonCancel = new Button())
            {
                form.Text = title;
                form.Font = ChromeFont;
                form.BackColor = Color.White;
                form.FormBorderStyle = FormBorderStyle.FixedDialog;
                form.StartPosition = FormStartPosition.CenterParent;
                form.MinimizeBox = false;
                form.MaximizeBox = false;
                form.ClientSize = new Size(420, 118);

                string placeholder = NormalizePlaceholder(prompt);
                textBox.SetBounds(16, 16, 388, PlaceholderTextBoxHeight);
                BindPlaceholder(textBox, placeholder, string.IsNullOrWhiteSpace(defaultValue) ? null : defaultValue);

                buttonOk.Text = "确定";
                buttonCancel.Text = "取消";
                StyleExistingButton(buttonOk);
                StyleExistingButton(buttonCancel);
                buttonOk.BackColor = GetButtonColor(SettingsButtonRole.Primary);
                buttonOk.ForeColor = Color.White;
                buttonCancel.BackColor = GetButtonColor(SettingsButtonRole.Secondary);
                buttonCancel.ForeColor = Color.White;
                buttonOk.DialogResult = DialogResult.OK;
                buttonCancel.DialogResult = DialogResult.Cancel;
                buttonOk.SetBounds(228, textBox.Bottom + 16, 84, 34);
                buttonCancel.SetBounds(320, textBox.Bottom + 16, 84, 34);

                form.Controls.Add(textBox);
                form.Controls.Add(buttonOk);
                form.Controls.Add(buttonCancel);
                form.AcceptButton = buttonOk;
                form.CancelButton = buttonCancel;

                return form.ShowDialog(owner) == DialogResult.OK ? GetEffectiveText(textBox) : string.Empty;
            }
        }
    }
}
