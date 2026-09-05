using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace lengkubao.desktop
{
    /// <summary>
    /// 多维库存统计：表格样式与简易图表控件。
    /// </summary>
    internal static class AgentDebugLog
    {
        private static readonly string[] LogPaths =
        {
            @"D:\afilessss\lengkubao.desktop\debug-256c22.log",
            @"D:\Lengkubao\project_mi\debug-256c22.log"
        };

        public static void Write(string hypothesisId, string location, string message, string dataJson = null)
        {
            // #region agent log
            try
            {
                string data = string.IsNullOrEmpty(dataJson) ? "{}" : dataJson;
                string line = "{\"sessionId\":\"256c22\",\"hypothesisId\":\"" + hypothesisId
                    + "\",\"location\":\"" + Escape(location) + "\",\"message\":\"" + Escape(message)
                    + "\",\"data\":" + data + ",\"timestamp\":" + (long)(DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalMilliseconds + "}\n";
                foreach (string path in LogPaths)
                {
                    try { File.AppendAllText(path, line, Encoding.UTF8); } catch { }
                }
                try
                {
                    File.AppendAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "debug-256c22.log"), line, Encoding.UTF8);
                }
                catch { }
            }
            catch { }
            // #endregion
        }

        internal static string Escape(string value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;
            return value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "").Replace("\n", " ");
        }
    }

    internal static class StatisticsUiHelper
    {
        public static readonly Color HeaderBack = Color.FromArgb(30, 58, 95);
        public static readonly Color HeaderFore = Color.White;
        public static readonly Color RowAltBack = Color.FromArgb(241, 245, 249);
        public static readonly Color RowBack = Color.White;
        public static readonly Color SelectionBack = Color.FromArgb(37, 99, 235);
        public static readonly Color SelectionFore = Color.White;
        public static readonly Color GridLine = Color.FromArgb(226, 232, 240);
        public static readonly Color PanelBack = Color.FromArgb(248, 250, 252);
        public static readonly Color CellFore = Color.FromArgb(30, 41, 59);
        public static readonly Color CardBorder = Color.FromArgb(203, 213, 225);

        public static readonly Font HeaderFont = new Font("微软雅黑", 14f, FontStyle.Bold);
        public static readonly Font CellFont = new Font("微软雅黑", 13f, FontStyle.Regular);
        public static readonly Font CellBoldFont = new Font("微软雅黑", 13f, FontStyle.Bold);

        /// <summary>多维库存统计表格专用字号（小于默认统计表格）</summary>
        public static readonly Font StatsGridCellFont = new Font("微软雅黑", 11f, FontStyle.Regular);
        public static readonly Font StatsGridHeaderFont = new Font("微软雅黑", 12f, FontStyle.Bold);
        public static readonly Font StatsGridCellBoldFont = new Font("微软雅黑", 11f, FontStyle.Bold);

        public static readonly DataGridViewContentAlignment UniformAlignment = DataGridViewContentAlignment.MiddleCenter;
        public static readonly Padding UniformCellPadding = new Padding(6, 4, 6, 4);
        public static readonly Padding UniformHeaderPadding = new Padding(6, 4, 6, 4);

        internal static readonly Color[] ChartPalette =
        {
            Color.FromArgb(52, 152, 219),
            Color.FromArgb(142, 68, 173),
            Color.FromArgb(46, 204, 113),
            Color.FromArgb(241, 196, 15),
            Color.FromArgb(231, 76, 60),
            Color.FromArgb(26, 188, 156),
            Color.FromArgb(155, 89, 182),
            Color.FromArgb(52, 73, 94)
        };

        public static void ApplyPremiumGridStyle(DataGridView grid, Color? accentHeader = null)
        {
            if (grid == null)
                return;

            // #region agent log
            string gridName = grid.Name ?? grid.GetType().Name;
            try
            {
            // #endregion
            Color headerBack = accentHeader ?? HeaderBack;

            grid.BackgroundColor = PanelBack;
            grid.BorderStyle = BorderStyle.None;
            grid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
            grid.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;
            grid.AdvancedColumnHeadersBorderStyle.All = DataGridViewAdvancedCellBorderStyle.None;
            grid.AdvancedColumnHeadersBorderStyle.Bottom = DataGridViewAdvancedCellBorderStyle.Single;
            grid.GridColor = GridLine;
            grid.EnableHeadersVisualStyles = false;
            grid.RowHeadersVisible = false;
            grid.AllowUserToAddRows = false;
            grid.AllowUserToResizeRows = false;
            grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            grid.MultiSelect = false;
            grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
            grid.ColumnHeadersHeight = 56;
            grid.RowTemplate.Height = 48;
            grid.ShowCellToolTips = true;
            grid.ScrollBars = ScrollBars.Both;

            grid.ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
            {
                BackColor = headerBack,
                ForeColor = HeaderFore,
                Font = HeaderFont,
                Alignment = UniformAlignment,
                Padding = UniformHeaderPadding,
                SelectionBackColor = headerBack,
                SelectionForeColor = HeaderFore,
                WrapMode = DataGridViewTriState.False
            };

            grid.DefaultCellStyle = new DataGridViewCellStyle
            {
                BackColor = RowBack,
                ForeColor = CellFore,
                Font = CellFont,
                Alignment = UniformAlignment,
                Padding = UniformCellPadding,
                SelectionBackColor = SelectionBack,
                SelectionForeColor = SelectionFore,
                WrapMode = DataGridViewTriState.False
            };

            grid.AlternatingRowsDefaultCellStyle = new DataGridViewCellStyle
            {
                BackColor = RowAltBack,
                ForeColor = CellFore,
                Font = CellFont,
                Alignment = UniformAlignment,
                Padding = UniformCellPadding,
                SelectionBackColor = SelectionBack,
                SelectionForeColor = SelectionFore,
                WrapMode = DataGridViewTriState.False
            };

            grid.RowHeadersDefaultCellStyle.SelectionBackColor = SelectionBack;
            grid.Font = CellFont;
            ApplyUniformColumnAlignment(grid);
            // #region agent log
            AgentDebugLog.Write("B", "StatisticsUiHelper.ApplyPremiumGridStyle", "ok",
                "{\"grid\":\"" + gridName + "\",\"colCount\":" + grid.Columns.Count + "}");
            }
            catch (Exception ex)
            {
                AgentDebugLog.Write("B", "StatisticsUiHelper.ApplyPremiumGridStyle", "fail",
                    "{\"grid\":\"" + gridName + "\",\"type\":\"" + ex.GetType().Name + "\",\"msg\":\"" + AgentDebugLog.Escape(ex.Message) + "\"}");
                throw;
            }
            // #endregion
        }

        public static void ApplyStatisticsGridTypography(DataGridView grid)
        {
            if (grid == null)
                return;

            var cellPadding = new Padding(4, 3, 4, 3);
            var headerPadding = new Padding(4, 3, 4, 3);

            grid.Font = StatsGridCellFont;
            grid.ColumnHeadersHeight = 40;
            grid.RowTemplate.Height = 34;

            grid.ColumnHeadersDefaultCellStyle.Font = StatsGridHeaderFont;
            grid.ColumnHeadersDefaultCellStyle.Padding = headerPadding;
            grid.DefaultCellStyle.Font = StatsGridCellFont;
            grid.DefaultCellStyle.Padding = cellPadding;
            grid.AlternatingRowsDefaultCellStyle.Font = StatsGridCellFont;
            grid.AlternatingRowsDefaultCellStyle.Padding = cellPadding;
            grid.RowsDefaultCellStyle.Font = StatsGridCellFont;

            foreach (DataGridViewColumn column in grid.Columns)
            {
                column.DefaultCellStyle.Font = StatsGridCellFont;
                column.HeaderCell.Style.Font = StatsGridHeaderFont;
            }

            foreach (DataGridViewRow row in grid.Rows)
            {
                if (row.IsNewRow)
                    continue;
                row.Height = grid.RowTemplate.Height;
            }
        }

        public static void ApplyStatisticsNumericColumnStyle(DataGridView grid, params string[] columnNames)
        {
            if (grid == null || columnNames == null)
                return;

            foreach (string name in columnNames)
            {
                if (!grid.Columns.Contains(name))
                    continue;

                var col = grid.Columns[name];
                col.DefaultCellStyle.Alignment = UniformAlignment;
                col.DefaultCellStyle.Font = StatsGridCellFont;
                if (name.Contains("金额"))
                    col.DefaultCellStyle.Format = "N2";
                else if (name.Contains("数量") || name.Contains("库存") || name.Contains("笔数")
                    || name.Contains("入库") || name.Contains("出库") || name.Contains("包装"))
                    col.DefaultCellStyle.Format = "N0";
            }
        }

        public static void ApplyStatisticsNumericColumnsFromHeaders(DataGridView grid)
        {
            if (grid == null)
                return;

            var names = new List<string>();
            foreach (DataGridViewColumn col in grid.Columns)
                names.Add(col.Name.Length > 0 ? col.Name : col.HeaderText);
            ApplyStatisticsNumericColumnStyle(grid, names.ToArray());
        }

        public static void ApplyStatisticsGridStyle(DataGridView grid, Color? accentHeader = null, params string[] numericColumns)
        {
            ApplyPremiumGridStyle(grid, accentHeader);
            if (numericColumns != null && numericColumns.Length > 0)
                ApplyStatisticsNumericColumnStyle(grid, numericColumns);
            ApplyStatisticsGridTypography(grid);
        }

        public static void PrepareStatisticsTab(TabPage tab)
        {
            if (tab == null)
                return;

            tab.BackColor = PanelBack;
            tab.Padding = new Padding(14, 10, 14, 12);
        }

        /// <summary>
        /// 连续相同值的单元格视觉合并：与上一行左侧合并列均相同时隐藏当前格文本。
        /// </summary>
        public static DataGridViewCellFormattingEventHandler CreateConsecutiveCellMergeHandler(
            DataGridView grid,
            string[] mergeColumns,
            Func<int, bool> skipRow = null)
        {
            if (grid == null || mergeColumns == null || mergeColumns.Length == 0)
                return null;

            return (sender, e) =>
            {
                if (e.RowIndex <= 0 || e.RowIndex >= grid.Rows.Count)
                    return;

                if (skipRow != null && (skipRow(e.RowIndex) || skipRow(e.RowIndex - 1)))
                    return;

                if (e.ColumnIndex < 0 || e.ColumnIndex >= grid.Columns.Count)
                    return;

                string columnName = grid.Columns[e.ColumnIndex].Name;
                int mergeIndex = Array.IndexOf(mergeColumns, columnName);
                if (mergeIndex < 0)
                    return;

                for (int i = 0; i <= mergeIndex; i++)
                {
                    string colName = mergeColumns[i];
                    if (!grid.Columns.Contains(colName))
                        return;

                    string current = grid.Rows[e.RowIndex].Cells[colName].Value?.ToString() ?? string.Empty;
                    string previous = grid.Rows[e.RowIndex - 1].Cells[colName].Value?.ToString() ?? string.Empty;
                    if (!string.Equals(current, previous, StringComparison.Ordinal))
                        return;
                }

                e.Value = string.Empty;
                e.FormattingApplied = true;
            };
        }

        /// <summary>
        /// 连续相同分组键的单元格真实合并绘制，内容水平/垂直居中。
        /// </summary>
        public static DataGridViewCellPaintingEventHandler CreateConsecutiveCellMergePaintHandler(
            DataGridView grid,
            string[] mergeColumns,
            string groupKeyColumn,
            Func<int, bool> skipRow = null)
        {
            if (grid == null || mergeColumns == null || mergeColumns.Length == 0
                || string.IsNullOrWhiteSpace(groupKeyColumn))
                return null;

            return (sender, e) =>
            {
                if (e.RowIndex < 0 || e.ColumnIndex < 0 || e.RowIndex >= grid.Rows.Count)
                    return;

                if (skipRow != null && skipRow(e.RowIndex))
                    return;

                string columnName = grid.Columns[e.ColumnIndex].Name;
                if (Array.IndexOf(mergeColumns, columnName) < 0)
                    return;

                if (!TryGetConsecutiveMergeSpan(grid, e.RowIndex, groupKeyColumn, skipRow, out int top, out int bottom))
                    return;

                bool isSelected = (e.State & DataGridViewElementStates.Selected) == DataGridViewElementStates.Selected;

                // 单行单据：选中时合并列也不显示蓝色，仅中间明细列保留选中色。
                if (bottom <= top)
                {
                    if (isSelected)
                    {
                        PaintMergeColumnCell(grid, e, e.CellBounds, drawText: true);
                        e.Handled = true;
                    }
                    return;
                }

                if (e.RowIndex > top)
                {
                    // 仅拦截默认绘制，避免覆盖首行已绘制的合并内容与文字。
                    e.Handled = true;
                    return;
                }

                Rectangle rect = GetMergedCellBounds(grid, e.ColumnIndex, top, bottom, e.CellBounds);

                Region oldClip = e.Graphics.Clip.Clone() as Region;
                try
                {
                    e.Graphics.SetClip(rect, CombineMode.Replace);
                    PaintMergeColumnCell(grid, e, rect, drawText: true);
                }
                finally
                {
                    if (oldClip != null)
                    {
                        e.Graphics.Clip = oldClip;
                        oldClip.Dispose();
                    }
                }

                e.Handled = true;
            };
        }

        private static Rectangle GetMergedCellBounds(
            DataGridView grid,
            int columnIndex,
            int top,
            int bottom,
            Rectangle fallbackBounds)
        {
            Rectangle rect = grid.GetCellDisplayRectangle(columnIndex, top, false);
            if (rect.Width <= 0 || rect.Height <= 0)
                rect = fallbackBounds;

            if (bottom > top)
            {
                Rectangle bottomRect = grid.GetCellDisplayRectangle(columnIndex, bottom, false);
                if (bottomRect.Height > 0)
                    rect.Height = bottomRect.Bottom - rect.Top;
                else
                {
                    for (int rowIndex = top + 1; rowIndex <= bottom; rowIndex++)
                    {
                        if (rowIndex >= grid.Rows.Count)
                            break;
                        rect.Height += grid.Rows[rowIndex].Height;
                    }
                }
            }

            return rect;
        }

        private static void PaintMergeColumnCell(
            DataGridView grid,
            DataGridViewCellPaintingEventArgs e,
            Rectangle bounds,
            bool drawText)
        {
            DataGridViewCellStyle style = e.CellStyle;
            Color backColor = style.BackColor;
            Color foreColor = style.ForeColor;

            using (SolidBrush backBrush = new SolidBrush(backColor))
                e.Graphics.FillRectangle(backBrush, bounds);

            using (Pen borderPen = new Pen(grid.GridColor))
            {
                e.Graphics.DrawLine(borderPen, bounds.Left, bounds.Top, bounds.Right - 1, bounds.Top);
                e.Graphics.DrawLine(borderPen, bounds.Left, bounds.Bottom - 1, bounds.Right - 1, bounds.Bottom - 1);
                e.Graphics.DrawLine(borderPen, bounds.Left, bounds.Top, bounds.Left, bounds.Bottom - 1);
                e.Graphics.DrawLine(borderPen, bounds.Right - 1, bounds.Top, bounds.Right - 1, bounds.Bottom - 1);
            }

            if (!drawText)
                return;

            string text = e.FormattedValue?.ToString() ?? string.Empty;
            if (string.IsNullOrEmpty(text))
                return;

            using (StringFormat format = new StringFormat
            {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center,
                Trimming = StringTrimming.EllipsisCharacter
            })
            {
                using (SolidBrush textBrush = new SolidBrush(foreColor))
                {
                    e.Graphics.DrawString(text, style.Font, textBrush, bounds, format);
                }
            }
        }

        private static bool TryGetConsecutiveMergeSpan(
            DataGridView grid,
            int rowIndex,
            string groupKeyColumn,
            Func<int, bool> skipRow,
            out int top,
            out int bottom)
        {
            top = bottom = rowIndex;
            if (!grid.Columns.Contains(groupKeyColumn))
                return false;

            string groupKey = GetGridRowGroupKey(grid, rowIndex, groupKeyColumn);
            if (string.IsNullOrEmpty(groupKey))
                return false;

            while (top > 0
                   && !grid.Rows[top - 1].IsNewRow
                   && (skipRow == null || !skipRow(top - 1))
                   && string.Equals(GetGridRowGroupKey(grid, top - 1, groupKeyColumn), groupKey, StringComparison.Ordinal))
            {
                top--;
            }

            while (bottom < grid.Rows.Count - 1
                   && !grid.Rows[bottom + 1].IsNewRow
                   && (skipRow == null || !skipRow(bottom + 1))
                   && string.Equals(GetGridRowGroupKey(grid, bottom + 1, groupKeyColumn), groupKey, StringComparison.Ordinal))
            {
                bottom++;
            }

            return true;
        }

        private static string GetGridRowGroupKey(DataGridView grid, int rowIndex, string groupKeyColumn)
        {
            if (grid == null || rowIndex < 0 || rowIndex >= grid.Rows.Count)
                return string.Empty;

            return grid.Rows[rowIndex].Cells[groupKeyColumn].Value?.ToString() ?? string.Empty;
        }

        /// <summary>
        /// 合并列默认居中显示。
        /// </summary>
        public static void ApplyMergeColumnAlignment(DataGridView grid, string[] mergeColumns)
        {
            if (grid == null || mergeColumns == null)
                return;

            foreach (string columnName in mergeColumns)
            {
                if (!grid.Columns.Contains(columnName))
                    continue;

                grid.Columns[columnName].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
            }
        }

        /// <summary>
        /// 按分组键为表格行设置交替背景色，便于区分不同库位块。
        /// </summary>
        public static void ApplyGroupRowColors(
            DataGridView grid,
            string[] groupKeyColumns,
            Func<int, bool> skipRow = null)
        {
            if (grid == null || grid.Rows.Count == 0 || groupKeyColumns == null || groupKeyColumns.Length == 0)
                return;

            Color[] groupColors = { RowBack, RowAltBack };
            string previousKey = null;
            int colorIndex = 0;

            for (int rowIndex = 0; rowIndex < grid.Rows.Count; rowIndex++)
            {
                if (skipRow != null && skipRow(rowIndex))
                    continue;

                string groupKey = BuildRowGroupKey(grid, rowIndex, groupKeyColumns);
                if (string.IsNullOrEmpty(groupKey))
                    continue;

                if (!string.Equals(groupKey, previousKey, StringComparison.Ordinal))
                {
                    previousKey = groupKey;
                    colorIndex = (colorIndex + 1) % groupColors.Length;
                }

                Color backColor = groupColors[colorIndex];
                foreach (DataGridViewCell cell in grid.Rows[rowIndex].Cells)
                    cell.Style.BackColor = backColor;
            }
        }

        private static string BuildRowGroupKey(DataGridView grid, int rowIndex, string[] groupKeyColumns)
        {
            if (grid == null || rowIndex < 0 || rowIndex >= grid.Rows.Count)
                return string.Empty;

            var parts = new List<string>();
            foreach (string columnName in groupKeyColumns)
            {
                if (!grid.Columns.Contains(columnName))
                    return string.Empty;

                parts.Add(grid.Rows[rowIndex].Cells[columnName].Value?.ToString() ?? string.Empty);
            }

            return string.Join("\u001f", parts);
        }

        public static void ApplyStatisticsFormChrome(Form form, TabControl tabs)
        {
            if (form != null)
            {
                form.BackColor = PanelBack;
                form.Font = new Font("微软雅黑", 11f, FontStyle.Regular);
            }

            if (tabs == null)
                return;

            tabs.Font = new Font("微软雅黑", 12f, FontStyle.Regular);
            tabs.Padding = new Point(14, 8);
            tabs.DrawMode = TabDrawMode.OwnerDrawFixed;
            tabs.SizeMode = TabSizeMode.Fixed;
            tabs.ItemSize = new Size(132, 36);
            tabs.DrawItem -= OnStatisticsTabDrawItem;
            tabs.DrawItem += OnStatisticsTabDrawItem;
        }

        private static void OnStatisticsTabDrawItem(object sender, DrawItemEventArgs e)
        {
            // #region agent log
            try
            {
            // #endregion
            var tabs = sender as TabControl;
            if (tabs == null || e.Index < 0 || e.Index >= tabs.TabPages.Count)
                return;

            bool selected = e.Index == tabs.SelectedIndex;
            var bounds = e.Bounds;
            bounds.Inflate(-2, -2);
            // #region agent log
            AgentDebugLog.Write("A", "StatisticsUiHelper.OnStatisticsTabDrawItem", "draw",
                "{\"index\":" + e.Index + ",\"bw\":" + bounds.Width + ",\"bh\":" + bounds.Height
                + ",\"tabW\":" + tabs.Width + ",\"selected\":" + (selected ? "true" : "false") + "}");
            // #endregion

            Color back = selected ? Color.White : Color.FromArgb(226, 232, 240);
            Color fore = selected ? HeaderBack : Color.FromArgb(71, 85, 105);

            using (var backBrush = new SolidBrush(back))
            using (var textBrush = new SolidBrush(fore))
            using (var borderPen = new Pen(selected ? SelectionBack : CardBorder))
            using (var font = new Font("微软雅黑", selected ? 12f : 11.5f, selected ? FontStyle.Bold : FontStyle.Regular))
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                FillRoundedRect(e.Graphics, backBrush, bounds, 6);
                if (selected)
                {
                    var accent = new Rectangle(bounds.X + 8, bounds.Bottom - 3, bounds.Width - 16, 3);
                    using (var accentBrush = new SolidBrush(SelectionBack))
                        FillRoundedRect(e.Graphics, accentBrush, accent, 2);
                }
                else
                {
                    e.Graphics.DrawRectangle(borderPen, bounds.X, bounds.Y, bounds.Width - 1, bounds.Height - 1);
                }

                var sf = new StringFormat
                {
                    Alignment = StringAlignment.Center,
                    LineAlignment = StringAlignment.Center,
                    Trimming = StringTrimming.EllipsisCharacter
                };
                e.Graphics.DrawString(tabs.TabPages[e.Index].Text, font, textBrush, bounds, sf);
            }
            // #region agent log
            }
            catch (Exception ex)
            {
                AgentDebugLog.Write("A", "StatisticsUiHelper.OnStatisticsTabDrawItem", "fail",
                    "{\"type\":\"" + ex.GetType().Name + "\",\"msg\":\"" + AgentDebugLog.Escape(ex.Message) + "\"}");
                throw;
            }
            // #endregion
        }

        public static void ApplyNumericColumnsFromHeaders(DataGridView grid)
        {
            if (grid == null)
                return;

            var names = new List<string>();
            foreach (DataGridViewColumn col in grid.Columns)
                names.Add(col.Name.Length > 0 ? col.Name : col.HeaderText);
            ApplyNumericColumnStyle(grid, names.ToArray());
        }

        internal static void FillRoundedRect(Graphics g, Brush brush, Rectangle rect, int radius)
        {
            using (var path = new GraphicsPath())
            {
                int d = radius * 2;
                path.AddArc(rect.X, rect.Y, d, d, 180, 90);
                path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
                path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
                path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
                path.CloseFigure();
                g.FillPath(brush, path);
            }
        }

        /// <summary>
        /// 统一表格各级字号，避免奇偶行或部分列回退到窗体 11pt 字体。
        /// </summary>
        public static void EnsureUniformGridCellFonts(DataGridView grid)
        {
            if (grid == null)
                return;

            grid.Font = CellFont;

            if (grid.DefaultCellStyle != null)
                grid.DefaultCellStyle.Font = CellFont;
            if (grid.AlternatingRowsDefaultCellStyle != null)
                grid.AlternatingRowsDefaultCellStyle.Font = CellFont;
            if (grid.RowsDefaultCellStyle != null)
                grid.RowsDefaultCellStyle.Font = CellFont;

            foreach (DataGridViewColumn col in grid.Columns)
                col.DefaultCellStyle.Font = CellFont;
        }

        public static void ApplyUniformColumnAlignment(DataGridView grid)
        {
            if (grid == null)
                return;

            foreach (DataGridViewColumn col in grid.Columns)
            {
                col.DefaultCellStyle.Alignment = UniformAlignment;
                col.DefaultCellStyle.Padding = UniformCellPadding;
                col.HeaderCell.Style.Alignment = UniformAlignment;
                col.HeaderCell.Style.Padding = UniformHeaderPadding;
            }

            EnsureUniformGridCellFonts(grid);
        }

        public static void ApplyNumericColumnStyle(DataGridView grid, params string[] columnNames)
        {
            if (grid == null || columnNames == null)
                return;

            foreach (string name in columnNames)
            {
                if (!grid.Columns.Contains(name))
                    continue;

                var col = grid.Columns[name];
                col.DefaultCellStyle.Alignment = UniformAlignment;
                col.DefaultCellStyle.Font = CellFont;
                if (name.Contains("金额"))
                    col.DefaultCellStyle.Format = "N2";
                else if (name.Contains("数量") || name.Contains("库存") || name.Contains("笔数")
                    || name.Contains("入库") || name.Contains("出库") || name.Contains("包装"))
                    col.DefaultCellStyle.Format = "N0";
            }
        }

        public static Panel CreateChartContainer(Control chart)
        {
            var panel = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = PanelBack,
                Padding = new Padding(12, 8, 12, 4)
            };

            chart.Dock = DockStyle.Fill;
            panel.Controls.Add(chart);
            return panel;
        }

        public static SplitContainer CreateChartGridSplit(int chartHeight = 240)
        {
            var split = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Horizontal,
                SplitterWidth = 6,
                BackColor = Color.FromArgb(245, 247, 250),
                Panel1MinSize = 160,
                Panel2MinSize = 120
            };

            try
            {
                split.SplitterDistance = chartHeight;
            }
            catch
            {
                split.SplitterDistance = Math.Max(160, chartHeight);
            }

            return split;
        }
    }

    internal sealed class StatisticsBarChartPanel : Panel
    {
        private readonly List<(string Label, double Value)> _items = new List<(string, double)>();

        public string ChartTitle { get; set; } = string.Empty;
        public string ValueSuffix { get; set; } = string.Empty;

        public StatisticsBarChartPanel()
        {
            DoubleBuffered = true;
            BackColor = StatisticsUiHelper.PanelBack;
            Resize += (s, e) => Invalidate();
        }

        public void SetData(string title, IEnumerable<(string Label, double Value)> items)
        {
            ChartTitle = title ?? string.Empty;
            _items.Clear();
            if (items != null)
            {
                foreach (var item in items)
                {
                    if (string.IsNullOrWhiteSpace(item.Label))
                        continue;
                    _items.Add((item.Label, item.Value));
                }
            }
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            var bounds = ClientRectangle;
            bounds.Inflate(-8, -6);

            using (var titleFont = new Font("微软雅黑", 12f, FontStyle.Bold))
            using (var labelFont = new Font("微软雅黑", 10f, FontStyle.Regular))
            using (var valueFont = new Font("微软雅黑", 9f, FontStyle.Regular))
            using (var subFont = new Font("微软雅黑", 10f, FontStyle.Italic))
            using (var borderPen = new Pen(Color.FromArgb(226, 232, 240)))
            {
                e.Graphics.DrawRectangle(borderPen, bounds.X, bounds.Y, bounds.Width - 1, bounds.Height - 1);

                var titleRect = new Rectangle(bounds.X + 12, bounds.Y + 10, bounds.Width - 24, 28);
                e.Graphics.DrawString(ChartTitle, titleFont, Brushes.DimGray, titleRect);

                if (_items.Count == 0)
                {
                    var emptyRect = new Rectangle(bounds.X, bounds.Y + 44, bounds.Width, bounds.Height - 52);
                    var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                    e.Graphics.DrawString("暂无图表数据", subFont, Brushes.Gray, emptyRect, sf);
                    return;
                }

                int chartTop = bounds.Y + 44;
                int chartHeight = bounds.Height - 52;
                int barAreaLeft = bounds.X + 110;
                int barAreaWidth = bounds.Width - 130;
                double maxValue = _items.Max(i => i.Value);
                if (maxValue <= 0)
                    maxValue = 1;

                int barHeight = Math.Max(18, (chartHeight - (_items.Count + 1) * 8) / _items.Count);

                for (int i = 0; i < _items.Count; i++)
                {
                    var item = _items[i];
                    int y = chartTop + i * (barHeight + 8);
                    var labelRect = new Rectangle(bounds.X + 12, y, 92, barHeight);
                    e.Graphics.DrawString(TruncateLabel(item.Label, 7), labelFont, Brushes.DimGray, labelRect,
                        new StringFormat { Alignment = StringAlignment.Far, LineAlignment = StringAlignment.Center });

                    int barWidth = (int)(barAreaWidth * (item.Value / maxValue));
                    barWidth = Math.Max(4, barWidth);
                    var barRect = new Rectangle(barAreaLeft, y + 2, barWidth, barHeight - 4);

                    Color baseColor = StatisticsUiHelper.ChartPalette[i % StatisticsUiHelper.ChartPalette.Length];
                    using (var brush = new LinearGradientBrush(barRect, Lighten(baseColor, 30), baseColor, LinearGradientMode.Horizontal))
                    {
                        StatisticsUiHelper.FillRoundedRect(e.Graphics, brush, barRect, 4);
                    }

                    string valueText = FormatValue(item.Value) + ValueSuffix;
                    e.Graphics.DrawString(valueText, valueFont, Brushes.DimGray,
                        barRect.Right + 6, y + (barHeight - valueFont.Height) / 2f);
                }
            }
        }

        private static string TruncateLabel(string text, int maxChars)
        {
            if (string.IsNullOrEmpty(text) || text.Length <= maxChars)
                return text ?? string.Empty;
            return text.Substring(0, maxChars) + "…";
        }

        private static string FormatValue(double value)
        {
            if (Math.Abs(value) >= 10000)
                return (value / 10000d).ToString("0.##") + "万";
            if (Math.Abs(value - Math.Round(value)) < 0.001)
                return ((long)value).ToString("N0");
            return value.ToString("N2");
        }

        private static Color Lighten(Color color, int amount)
        {
            return Color.FromArgb(color.A,
                Math.Min(255, color.R + amount),
                Math.Min(255, color.G + amount),
                Math.Min(255, color.B + amount));
        }

    }

    internal sealed class StatisticsTrendChartPanel : Panel
    {
        private readonly List<(string Label, double Value)> _points = new List<(string, double)>();
        private readonly List<(string Label, double Value)> _secondaryPoints = new List<(string, double)>();

        public string ChartTitle { get; set; } = string.Empty;
        public string PrimarySeriesName { get; set; } = "销售金额";
        public string SecondarySeriesName { get; set; } = "入库金额";

        public StatisticsTrendChartPanel()
        {
            DoubleBuffered = true;
            BackColor = StatisticsUiHelper.PanelBack;
            Resize += (s, e) => Invalidate();
        }

        public void SetData(string title, IEnumerable<(string Label, double Primary, double Secondary)> points)
        {
            ChartTitle = title ?? string.Empty;
            _points.Clear();
            _secondaryPoints.Clear();
            if (points != null)
            {
                foreach (var p in points)
                {
                    if (string.IsNullOrWhiteSpace(p.Label))
                        continue;
                    _points.Add((p.Label, p.Primary));
                    _secondaryPoints.Add((p.Label, p.Secondary));
                }
            }
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            var bounds = ClientRectangle;
            bounds.Inflate(-8, -6);

            using (var titleFont = new Font("微软雅黑", 12f, FontStyle.Bold))
            using (var axisFont = new Font("微软雅黑", 9f, FontStyle.Regular))
            using (var legendFont = new Font("微软雅黑", 9f, FontStyle.Regular))
            using (var borderPen = new Pen(Color.FromArgb(226, 232, 240)))
            using (var gridPen = new Pen(Color.FromArgb(236, 240, 245)))
            {
                e.Graphics.DrawRectangle(borderPen, bounds.X, bounds.Y, bounds.Width - 1, bounds.Height - 1);
                e.Graphics.DrawString(ChartTitle, titleFont, Brushes.DimGray, bounds.X + 12, bounds.Y + 10);

                if (_points.Count == 0)
                {
                    var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                    e.Graphics.DrawString("暂无趋势数据", axisFont, Brushes.Gray,
                        new Rectangle(bounds.X, bounds.Y + 40, bounds.Width, bounds.Height - 48), sf);
                    return;
                }

                var plot = new Rectangle(bounds.X + 48, bounds.Y + 52, bounds.Width - 68, bounds.Height - 88);
                DrawLegend(e.Graphics, legendFont, bounds.X + 12, bounds.Bottom - 26);

                double maxVal = 1;
                foreach (var p in _points)
                    maxVal = Math.Max(maxVal, p.Value);
                foreach (var p in _secondaryPoints)
                    maxVal = Math.Max(maxVal, p.Value);

                for (int i = 0; i <= 4; i++)
                {
                    int y = plot.Top + (int)(plot.Height * i / 4d);
                    e.Graphics.DrawLine(gridPen, plot.Left, y, plot.Right, y);
                }

                e.Graphics.DrawLine(borderPen, plot.Left, plot.Bottom, plot.Right, plot.Bottom);
                e.Graphics.DrawLine(borderPen, plot.Left, plot.Top, plot.Left, plot.Bottom);

                if (_points.Count == 1)
                {
                    DrawSeries(e.Graphics, _points, plot, maxVal, Color.FromArgb(52, 152, 219), true);
                    DrawSeries(e.Graphics, _secondaryPoints, plot, maxVal, Color.FromArgb(142, 68, 173), false);
                }
                else
                {
                    DrawSeries(e.Graphics, _points, plot, maxVal, Color.FromArgb(52, 152, 219), true);
                    DrawSeries(e.Graphics, _secondaryPoints, plot, maxVal, Color.FromArgb(142, 68, 173), false);
                }

                int labelStep = Math.Max(1, _points.Count / 8);
                for (int i = 0; i < _points.Count; i += labelStep)
                {
                    float x = plot.Left + plot.Width * (i / (float)Math.Max(1, _points.Count - 1));
                    string label = _points[i].Label;
                    if (label.Length > 8)
                        label = label.Substring(label.Length - 8);
                    e.Graphics.DrawString(label, axisFont, Brushes.Gray, x - 20, plot.Bottom + 4);
                }
            }
        }

        private void DrawLegend(Graphics g, Font font, int x, int y)
        {
            DrawLegendItem(g, font, x, y, Color.FromArgb(52, 152, 219), PrimarySeriesName);
            DrawLegendItem(g, font, x + 120, y, Color.FromArgb(142, 68, 173), SecondarySeriesName);
        }

        private static void DrawLegendItem(Graphics g, Font font, int x, int y, Color color, string text)
        {
            g.FillRectangle(new SolidBrush(color), x, y + 3, 14, 4);
            g.DrawString(text, font, Brushes.DimGray, x + 20, y);
        }

        private static void DrawSeries(Graphics g, List<(string Label, double Value)> series, Rectangle plot, double maxVal, Color color, bool fillArea)
        {
            if (series.Count == 0)
                return;

            var pts = new PointF[series.Count];
            for (int i = 0; i < series.Count; i++)
            {
                float x = plot.Left + plot.Width * (i / (float)Math.Max(1, series.Count - 1));
                float y = plot.Bottom - (float)(plot.Height * (series[i].Value / maxVal));
                pts[i] = new PointF(x, y);
            }

            if (fillArea && pts.Length > 1)
            {
                using (var path = new GraphicsPath())
                {
                    path.AddLines(pts);
                    path.AddLine(pts[pts.Length - 1].X, plot.Bottom, pts[0].X, plot.Bottom);
                    path.CloseFigure();
                    using (var brush = new SolidBrush(Color.FromArgb(40, color)))
                        g.FillPath(brush, path);
                }
            }

            using (var pen = new Pen(color, 2.5f))
            {
                if (pts.Length == 1)
                    g.FillEllipse(new SolidBrush(color), pts[0].X - 4, pts[0].Y - 4, 8, 8);
                else
                    g.DrawLines(pen, pts);
            }

            using (var brush = new SolidBrush(color))
            {
                foreach (var p in pts)
                    g.FillEllipse(brush, p.X - 3.5f, p.Y - 3.5f, 7, 7);
            }
        }
    }
}
