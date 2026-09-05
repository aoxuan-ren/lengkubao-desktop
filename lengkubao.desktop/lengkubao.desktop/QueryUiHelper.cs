using System;
using System.Collections.Generic;
using System.Data;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;

namespace lengkubao.desktop
{
    internal enum QueryModule
    {
        Inbound,
        Sales,
        Packaging,
        Presale,
        Ledger
    }

    internal enum QueryButtonRole
    {
        Primary,
        Secondary,
        Success,
        Danger,
        Warning,
        Neutral,
        Info
    }

    /// <summary>
    /// 入库 / 销售 / 包装查询界面统一样式。
    /// </summary>
    internal static class QueryUiHelper
    {
        public static readonly Size DefaultButtonSize = new Size(96, 34);
        public static readonly Size CompactButtonSize = new Size(80, 30);

        /// <summary>窗体、筛选区控件字号（与 StatisticsForm 一致）</summary>
        public static readonly Font ChromeFont = new Font("微软雅黑", 11f, FontStyle.Regular);
        /// <summary>状态栏、按钮、记录数等强调文字</summary>
        public static readonly Font ChromeBoldFont = new Font("微软雅黑", 11f, FontStyle.Bold);

        /// <summary>查询表格单元格字号（小于统计页表格）</summary>
        public static readonly Font QueryGridCellFont = new Font("微软雅黑", 11f, FontStyle.Regular);
        /// <summary>查询表格表头字号</summary>
        public static readonly Font QueryGridHeaderFont = new Font("微软雅黑", 12f, FontStyle.Bold);
        /// <summary>查询表格强调单元格（状态列等）</summary>
        public static readonly Font QueryGridCellBoldFont = new Font("微软雅黑", 11f, FontStyle.Bold);

        public static Color GetAccent(QueryModule module)
        {
            switch (module)
            {
                case QueryModule.Sales:
                    return Color.FromArgb(109, 40, 217);
                case QueryModule.Packaging:
                    return Color.FromArgb(15, 118, 110);
                case QueryModule.Presale:
                    return Color.FromArgb(180, 83, 9);
                case QueryModule.Ledger:
                    return Color.FromArgb(4, 120, 87);
                default:
                    return Color.FromArgb(37, 99, 235);
            }
        }

        public static void ApplyFormChrome(Form form)
        {
            if (form == null)
                return;

            form.BackColor = StatisticsUiHelper.PanelBack;
            form.Font = ChromeFont;
        }

        public static void StyleTopStatusBar(Label lblStatus, QueryModule module)
        {
            if (lblStatus == null)
                return;

            lblStatus.Dock = DockStyle.Top;
            lblStatus.Height = 44;
            lblStatus.Padding = new Padding(16, 0, 16, 0);
            lblStatus.TextAlign = ContentAlignment.MiddleLeft;
            lblStatus.ForeColor = Color.White;
            lblStatus.BackColor = GetAccent(module);
            lblStatus.Font = ChromeBoldFont;
        }

        public static Panel CreateModuleHeader(QueryModule module, string initialText, out Label statusLabel)
        {
            var panel = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = GetAccent(module),
                Padding = new Padding(16, 0, 16, 0)
            };

            statusLabel = new Label
            {
                Dock = DockStyle.Fill,
                Text = initialText,
                ForeColor = Color.White,
                Font = ChromeBoldFont,
                TextAlign = ContentAlignment.MiddleLeft
            };
            panel.Controls.Add(statusLabel);
            return panel;
        }

        public static void StyleFilterPanel(Panel panel)
        {
            if (panel == null)
                return;

            panel.BackColor = Color.White;
            panel.Padding = new Padding(12, 8, 12, 6);
        }

        public static void StyleInfoBar(Panel panel)
        {
            if (panel == null)
                return;

            panel.BackColor = Color.FromArgb(241, 245, 249);
            panel.Padding = new Padding(12, 4, 12, 4);
        }

        public static void StyleToolbarPanel(Panel panel)
        {
            if (panel == null)
                return;

            panel.BackColor = Color.White;
            panel.Padding = new Padding(12, 8, 12, 8);
        }

        public static void StyleFilterLabel(Label label)
        {
            if (label == null)
                return;

            label.Font = ChromeFont;
            label.ForeColor = StatisticsUiHelper.CellFore;
        }

        public static void StyleTextBox(TextBox textBox, int height = 32)
        {
            if (textBox == null)
                return;

            textBox.Font = ChromeFont;
            textBox.Height = height;
            textBox.BorderStyle = BorderStyle.FixedSingle;
        }

        public static void StyleComboBox(ComboBox comboBox, int height = 32)
        {
            if (comboBox == null)
                return;

            comboBox.Font = ChromeFont;
            comboBox.Height = height;
            comboBox.FlatStyle = FlatStyle.Standard;
        }

        public static void StyleDatePicker(DateTimePicker picker, int height = 32)
        {
            if (picker == null)
                return;

            picker.Font = ChromeFont;
            picker.Height = height;
            picker.Format = DateTimePickerFormat.Short;
        }

        public const int FilterLabelWidth = 78;
        public const int FilterControlHeight = 32;
        public const int FilterFieldGapRight = 12;
        public const int FilterRowGapBottom = 4;
        public const int FilterRowHeight = 34;
        public const int FilterButtonRowHeight = 34;

        public static FlowLayoutPanel CreateFilterFlowPanel()
        {
            return new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = true,
                AutoScroll = false,
                Padding = new Padding(16, 12, 16, 6),
                BackColor = Color.White
            };
        }

        /// <summary>
        /// 查询筛选区：第一行开始/结束日期，第二行其余条件，底部操作按钮。
        /// </summary>
        public static Panel CreateTwoRowQueryFilterPanel(
            Panel startDateField,
            Panel endDateField,
            IEnumerable<Panel> secondRowFields,
            params Button[] buttons)
        {
            var container = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.White,
                Padding = new Padding(2, 2, 2, 0)
            };

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 3,
                BackColor = Color.White,
                Margin = new Padding(0),
                Padding = new Padding(0)
            };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, FilterRowHeight));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, FilterRowHeight));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, FilterButtonRowHeight));

            var row1 = CreateFilterRowPanel();
            if (startDateField != null)
                row1.Controls.Add(startDateField);
            if (endDateField != null)
                row1.Controls.Add(endDateField);

            var row2 = CreateFilterRowPanel();
            if (secondRowFields != null)
            {
                foreach (Panel field in secondRowFields)
                {
                    if (field != null)
                        row2.Controls.Add(field);
                }
            }

            row1.Dock = DockStyle.Fill;
            row2.Dock = DockStyle.Fill;
            layout.Controls.Add(row1, 0, 0);
            layout.Controls.Add(row2, 0, 1);
            layout.Controls.Add(CreateInlineFilterButtonRow(buttons), 0, 2);
            container.Controls.Add(layout);
            return container;
        }

        private static Panel CreateInlineFilterButtonRow(params Button[] buttons)
        {
            var panel = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.White,
                Padding = new Padding(0, 2, 0, 0)
            };

            int x = 0;
            foreach (Button button in buttons)
            {
                if (button == null)
                    continue;

                button.Location = new Point(x, 0);
                button.Margin = new Padding(0);
                panel.Controls.Add(button);
                x += button.Width + 10;
            }

            return panel;
        }

        private static FlowLayoutPanel CreateFilterRowPanel()
        {
            return new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                Margin = new Padding(0),
                Padding = new Padding(0),
                BackColor = Color.White
            };
        }

        public static Panel CreateFilterField(string labelText, Control control, int controlWidth = 168)
        {
            var group = new Panel
            {
                Width = FilterLabelWidth + 8 + controlWidth,
                Height = FilterRowHeight,
                Margin = new Padding(0, 0, FilterFieldGapRight, 0),
                BackColor = Color.White
            };

            var label = new Label
            {
                Text = labelText,
                Location = new Point(0, 6),
                Width = FilterLabelWidth,
                Height = FilterControlHeight,
                TextAlign = ContentAlignment.MiddleRight,
                AutoSize = false
            };
            StyleFilterLabel(label);

            control.Location = new Point(FilterLabelWidth + 8, 2);
            control.Size = new Size(controlWidth, FilterControlHeight);
            if (control is ComboBox combo)
                StyleComboBox(combo, FilterControlHeight);
            else if (control is DateTimePicker picker)
                StyleDatePicker(picker, FilterControlHeight);
            else if (control is TextBox textBox)
                StyleTextBox(textBox, FilterControlHeight);

            group.Controls.Add(label);
            group.Controls.Add(control);
            return group;
        }

        public static Panel CreateFilterButtonRow(params Button[] buttons)
        {
            var panel = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 46,
                BackColor = Color.White,
                Padding = new Padding(16, 0, 16, 10)
            };

            int x = 0;
            foreach (Button button in buttons)
            {
                if (button == null)
                    continue;

                button.Location = new Point(x, 6);
                button.Margin = new Padding(0);
                panel.Controls.Add(button);
                x += button.Width + 12;
            }

            return panel;
        }

        public const int FilterSummaryBarHeight = 34;

        public static void StyleRecordCountLabel(Label label, QueryModule module)
        {
            StyleFilterSummaryLabel(label, module);
        }

        public static void StyleFilterSummaryLabel(Label label, QueryModule module)
        {
            if (label == null)
                return;

            label.Font = ChromeBoldFont;
            label.ForeColor = GetAccent(module);
            label.TextAlign = ContentAlignment.MiddleLeft;
            label.AutoSize = false;
            label.Dock = DockStyle.Fill;
        }

        public static string FormatFilterSummaryText(int totalCount)
        {
            return $"筛选汇总：共 {totalCount} 条记录";
        }

        public static string FormatFilterSummaryWithPageTotals(int totalCount, decimal pageQuantity, decimal pageAmount)
        {
            return $"筛选汇总：共 {totalCount} 条  |  本页：数量 {pageQuantity:N0}  金额 {pageAmount:N2}";
        }

        public static void UpdateFilterSummaryLabel(Label label, int totalCount)
        {
            if (label == null)
                return;

            label.Text = FormatFilterSummaryText(totalCount);
        }

        public static void UpdateFilterSummaryLabel(Label label, int totalCount, decimal pageQuantity, decimal pageAmount)
        {
            if (label == null)
                return;

            label.Text = FormatFilterSummaryWithPageTotals(totalCount, pageQuantity, pageAmount);
        }

        /// <summary>
        /// 从当前列表数据汇总数量与金额（兼容英文字段与中文列名）。
        /// </summary>
        public static void SumQuantityAndAmount(DataTable table, out decimal quantity, out decimal amount)
        {
            quantity = 0m;
            amount = 0m;
            if (table == null || table.Rows.Count == 0)
                return;

            string qtyCol = FindColumnName(table, "quantity", "数量");
            string amtCol = FindColumnName(table, "total_amount", "总金额", "金额");

            foreach (DataRow row in table.Rows)
            {
                if (row.RowState == DataRowState.Deleted)
                    continue;

                if (qtyCol != null)
                    quantity += ParseDecimal(row[qtyCol]);
                if (amtCol != null)
                    amount += ParseDecimal(row[amtCol]);
            }
        }

        public static void SumQuantityAndAmount(DataGridView grid, out decimal quantity, out decimal amount)
        {
            quantity = 0m;
            amount = 0m;
            if (grid == null)
                return;

            if (grid.DataSource is DataTable table)
            {
                SumQuantityAndAmount(table, out quantity, out amount);
                return;
            }

            if (grid.DataSource is DataView view && view.Table != null)
            {
                SumQuantityAndAmount(view.ToTable(), out quantity, out amount);
                return;
            }

            DataGridViewColumn qtyCol = FindGridColumn(grid, "quantity", "数量");
            DataGridViewColumn amtCol = FindGridColumn(grid, "total_amount", "总金额", "金额");
            if (qtyCol == null && amtCol == null)
                return;

            foreach (DataGridViewRow row in grid.Rows)
            {
                if (row.IsNewRow)
                    continue;

                if (qtyCol != null)
                    quantity += ParseDecimal(row.Cells[qtyCol.Index].Value);
                if (amtCol != null)
                    amount += ParseDecimal(row.Cells[amtCol.Index].Value);
            }
        }

        private static string FindColumnName(DataTable table, params string[] candidates)
        {
            foreach (string candidate in candidates)
            {
                if (table.Columns.Contains(candidate))
                    return candidate;
            }

            foreach (DataColumn column in table.Columns)
            {
                string name = column.ColumnName ?? string.Empty;
                foreach (string candidate in candidates)
                {
                    if (string.Equals(name, candidate, StringComparison.OrdinalIgnoreCase))
                        return column.ColumnName;
                }
            }

            return null;
        }

        private static DataGridViewColumn FindGridColumn(DataGridView grid, params string[] candidates)
        {
            foreach (string candidate in candidates)
            {
                if (grid.Columns.Contains(candidate))
                    return grid.Columns[candidate];
            }

            foreach (DataGridViewColumn column in grid.Columns)
            {
                string name = column.Name ?? string.Empty;
                string header = column.HeaderText ?? string.Empty;
                foreach (string candidate in candidates)
                {
                    if (string.Equals(name, candidate, StringComparison.OrdinalIgnoreCase)
                        || string.Equals(header, candidate, StringComparison.OrdinalIgnoreCase))
                        return column;
                }
            }

            return null;
        }

        private static decimal ParseDecimal(object value)
        {
            if (value == null || value == DBNull.Value)
                return 0m;

            if (value is decimal dec)
                return dec;
            if (value is double dbl)
                return (decimal)dbl;
            if (value is float flt)
                return (decimal)flt;
            if (value is int i)
                return i;
            if (value is long l)
                return l;

            string text = Convert.ToString(value)?.Trim().Replace(",", "") ?? string.Empty;
            if (string.IsNullOrEmpty(text))
                return 0m;

            if (decimal.TryParse(text, NumberStyles.Any, CultureInfo.CurrentCulture, out decimal parsed)
                || decimal.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out parsed))
            {
                return parsed;
            }

            return 0m;
        }

        public static Button CreateButton(string text, QueryButtonRole role, Size? size = null)
        {
            var button = new Button
            {
                Text = text,
                Size = size ?? DefaultButtonSize,
                Font = ChromeBoldFont,
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand,
                BackColor = GetButtonColor(role)
            };
            button.FlatAppearance.BorderSize = 0;
            return button;
        }

        public static Color GetButtonColor(QueryButtonRole role)
        {
            switch (role)
            {
                case QueryButtonRole.Primary:
                    return Color.FromArgb(37, 99, 235);
                case QueryButtonRole.Secondary:
                    return Color.FromArgb(100, 116, 139);
                case QueryButtonRole.Success:
                    return Color.FromArgb(22, 163, 74);
                case QueryButtonRole.Danger:
                    return Color.FromArgb(220, 38, 38);
                case QueryButtonRole.Warning:
                    return Color.FromArgb(217, 119, 6);
                case QueryButtonRole.Info:
                    return Color.FromArgb(124, 58, 237);
                default:
                    return Color.FromArgb(148, 163, 184);
            }
        }

        public static void ApplyQueryGrid(DataGridView grid, QueryModule module, bool multiSelect = false, bool showRowHeaders = false)
        {
            if (grid == null)
                return;

            StatisticsUiHelper.ApplyPremiumGridStyle(grid, GetAccent(module));
            grid.MultiSelect = multiSelect;
            grid.ReadOnly = true;
            grid.AllowUserToResizeColumns = true;
            grid.RowHeadersVisible = showRowHeaders;

            if (showRowHeaders)
            {
                grid.RowHeadersWidth = 44;
                grid.RowHeadersDefaultCellStyle.BackColor = Color.FromArgb(241, 245, 249);
                grid.RowHeadersDefaultCellStyle.ForeColor = StatisticsUiHelper.CellFore;
                grid.RowHeadersDefaultCellStyle.Font = QueryGridCellBoldFont;
                grid.RowHeadersDefaultCellStyle.Alignment = StatisticsUiHelper.UniformAlignment;
                grid.RowHeadersDefaultCellStyle.Padding = new Padding(4, 2, 4, 2);
            }

            ApplyUniformColumnAlignment(grid);
        }

        private static void ApplyQueryGridColumnAlignment(DataGridView grid)
        {
            if (grid == null)
                return;

            var cellPadding = new Padding(4, 3, 4, 3);
            var headerPadding = new Padding(4, 4, 4, 4);

            foreach (DataGridViewColumn col in grid.Columns)
            {
                col.DefaultCellStyle.Alignment = StatisticsUiHelper.UniformAlignment;
                col.DefaultCellStyle.Padding = cellPadding;
                col.HeaderCell.Style.Alignment = StatisticsUiHelper.UniformAlignment;
                col.HeaderCell.Style.Padding = headerPadding;
            }
        }

        private static void ApplyQueryNumericColumnsFromHeaders(DataGridView grid)
        {
            if (grid == null)
                return;

            foreach (DataGridViewColumn col in grid.Columns)
            {
                string name = col.Name.Length > 0 ? col.Name : col.HeaderText;
                if (string.IsNullOrEmpty(name))
                    continue;

                if (name.Contains("金额"))
                    col.DefaultCellStyle.Format = "N2";
                else if (name.Contains("数量") || name.Contains("库存") || name.Contains("笔数")
                    || name.Contains("入库") || name.Contains("出库") || name.Contains("包装"))
                    col.DefaultCellStyle.Format = "N0";
            }
        }

        private static void ApplyQueryGridTypography(DataGridView grid)
        {
            if (grid == null)
                return;

            grid.Font = QueryGridCellFont;
            grid.ColumnHeadersHeight = 44;
            grid.RowTemplate.Height = 36;

            var cellPadding = new Padding(4, 3, 4, 3);
            var headerPadding = new Padding(4, 4, 4, 4);

            grid.ColumnHeadersDefaultCellStyle.Font = QueryGridHeaderFont;
            grid.ColumnHeadersDefaultCellStyle.Padding = headerPadding;
            grid.DefaultCellStyle.Font = QueryGridCellFont;
            grid.DefaultCellStyle.Padding = cellPadding;
            grid.AlternatingRowsDefaultCellStyle.Font = QueryGridCellFont;
            grid.AlternatingRowsDefaultCellStyle.Padding = cellPadding;
            grid.RowsDefaultCellStyle.Font = QueryGridCellFont;

            foreach (DataGridViewColumn column in grid.Columns)
            {
                column.DefaultCellStyle.Font = QueryGridCellFont;
                column.HeaderCell.Style.Font = QueryGridHeaderFont;
            }

            foreach (DataGridViewRow row in grid.Rows)
            {
                if (row.IsNewRow)
                    continue;

                row.Height = grid.RowTemplate.Height;
            }
        }

        public static void ApplyUniformColumnAlignment(DataGridView grid)
        {
            ApplyQueryGridColumnAlignment(grid);
            ApplyQueryNumericColumnsFromHeaders(grid);
            ApplyQueryGridTypography(grid);
        }

        public static Panel CreateGridHost(DataGridView grid)
        {
            var panel = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = StatisticsUiHelper.PanelBack,
                Padding = new Padding(10, 6, 10, 6)
            };
            grid.Dock = DockStyle.Fill;
            panel.Controls.Add(grid);
            return panel;
        }

        /// <summary>保存时无修改：【是】退出编辑，【否】继续编辑。</summary>
        public static bool ConfirmExitEditWhenNoChanges(IWin32Window owner = null)
        {
            var result = MessageBox.Show(
                owner,
                "没有检测到任何修改。\n\n【是】退出编辑模式\n【否】继续编辑",
                "提示",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Information,
                MessageBoxDefaultButton.Button2);
            return result == DialogResult.Yes;
        }
    }
}
