using System;
using System.Data;
using System.Drawing;
using System.Windows.Forms;

namespace lengkubao.desktop
{
    public static class ClientBalanceGridHelper
    {
        public static void SetupDetailsGrid(DataGridView grid)
        {
            grid.Columns.Clear();
            grid.BackgroundColor = Color.White;
            grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            grid.ReadOnly = true;
            grid.AllowUserToAddRows = false;
            grid.RowHeadersVisible = false;

            grid.Columns.Add("Type", "类型");
            grid.Columns.Add("TransactionDate", "日期");
            grid.Columns.Add("ItemType", "商品");
            grid.Columns.Add("Location", "库位");
            grid.Columns.Add("Quantity", "数量");
            grid.Columns.Add("UnitPrice", "单价");
            grid.Columns.Add("Amount", "金额");
            grid.Columns.Add("Handler", "经手人");
            grid.Columns.Add("Reason", "备注");
            grid.Columns.Add("TableType", "表类型");
            grid.Columns["TableType"].Visible = false;

            grid.Columns["Quantity"].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
            grid.Columns["UnitPrice"].DefaultCellStyle.Format = "N2";
            grid.Columns["Amount"].DefaultCellStyle.Format = "N2";
            grid.Columns["Amount"].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
            grid.Columns["UnitPrice"].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;

            grid.CellFormatting -= DetailsGrid_CellFormatting;
            grid.CellFormatting += DetailsGrid_CellFormatting;
        }

        private static void DetailsGrid_CellFormatting(object sender, DataGridViewCellFormattingEventArgs e)
        {
            if (sender is DataGridView grid &&
                e.ColumnIndex == grid.Columns["Amount"].Index &&
                e.Value != null &&
                decimal.TryParse(e.Value.ToString(), out decimal amount) &&
                amount < 0)
            {
                e.CellStyle.ForeColor = Color.Red;
            }
        }

        public static void BindDetailsGrid(DataGridView grid, DataTable detailsData)
        {
            grid.Rows.Clear();
            if (detailsData == null)
                return;

            foreach (DataRow row in detailsData.Rows)
            {
                string type = row["Type"].ToString();
                DateTime transactionDate = Convert.ToDateTime(row["TransactionDate"]);
                string itemType = row["ItemType"]?.ToString() ?? "";
                string location = row["Location"]?.ToString() ?? "";

                bool isSettled = row.Table.Columns.Contains("IsSettled") &&
                                 Convert.ToBoolean(row["IsSettled"]);
                string displayType = type;
                if (isSettled)
                    displayType = type + "(已结清)";

                string packFlag = "";
                if (type == "包装" && row.Table.Columns.Contains("PackFlag"))
                {
                    packFlag = row["PackFlag"]?.ToString() ?? "";
                    if (packFlag == "RETURN")
                        displayType = type + "(进)";
                    else if (packFlag == "TAKE")
                        displayType = type + "(出)";
                }

                if (itemType.Length > 25)
                    itemType = itemType.Substring(0, 22) + "...";

                string quantity = row["Quantity"] != DBNull.Value
                    ? Convert.ToDecimal(row["Quantity"]).ToString("N0")
                    : "0";
                string unitPrice = row["UnitPrice"] != DBNull.Value
                    ? Convert.ToDecimal(row["UnitPrice"]).ToString("N2")
                    : "0.00";
                string amount = row["Amount"] != DBNull.Value
                    ? Convert.ToDecimal(row["Amount"]).ToString("N2")
                    : "0.00";

                string handler = row["Handler"]?.ToString() ?? "";
                string reason = row["Reason"]?.ToString() ?? "";
                if (reason.Length > 30)
                    reason = reason.Substring(0, 27) + "...";

                string tableType = "";
                if (type == "扣款") tableType = "deductions";
                else if (type == "预支") tableType = "advances";
                else if (type == "销售") tableType = "sales_transactions";
                else if (type == "包装") tableType = "packaging_transactions";

                int rowIndex = grid.Rows.Add(
                    displayType,
                    transactionDate.ToString("yyyy-MM-dd"),
                    itemType,
                    location,
                    quantity,
                    unitPrice,
                    amount,
                    handler,
                    reason,
                    tableType);

                ApplyDetailsRowStyle(grid, rowIndex, type, isSettled, packFlag, row);
            }

            grid.Refresh();
        }

        private static void ApplyDetailsRowStyle(
            DataGridView grid,
            int rowIndex,
            string type,
            bool isSettled,
            string packFlag,
            DataRow row)
        {
            if (type == "销售")
            {
                if (isSettled)
                    StyleSettledRow(grid, rowIndex);
                else
                    grid.Rows[rowIndex].DefaultCellStyle.BackColor = Color.FromArgb(220, 240, 255);
            }
            else if (type == "包装")
            {
                if (isSettled)
                    StyleSettledRow(grid, rowIndex);
                else if (packFlag == "RETURN")
                    grid.Rows[rowIndex].DefaultCellStyle.BackColor = Color.FromArgb(255, 220, 220);
                else
                    grid.Rows[rowIndex].DefaultCellStyle.BackColor = Color.FromArgb(255, 248, 220);
            }
            else if (type == "扣款")
            {
                grid.Rows[rowIndex].DefaultCellStyle.BackColor = Color.FromArgb(255, 220, 220);
            }
            else if (type == "预支")
            {
                grid.Rows[rowIndex].DefaultCellStyle.BackColor = Color.FromArgb(255, 220, 255);
            }
            else if (type == "期初余额")
            {
                grid.Rows[rowIndex].DefaultCellStyle.BackColor = Color.FromArgb(235, 245, 255);
            }

            if (row["Amount"] != DBNull.Value && Convert.ToDecimal(row["Amount"]) < 0)
                grid.Rows[rowIndex].Cells["Amount"].Style.ForeColor = Color.Red;

            if (isSettled)
            {
                foreach (DataGridViewCell cell in grid.Rows[rowIndex].Cells)
                {
                    if (cell.Style.ForeColor != Color.Red)
                        cell.Style.ForeColor = Color.Gray;
                }
            }
        }

        private static void StyleSettledRow(DataGridView grid, int rowIndex)
        {
            grid.Rows[rowIndex].DefaultCellStyle.BackColor = Color.FromArgb(240, 240, 240);
            grid.Rows[rowIndex].DefaultCellStyle.ForeColor = Color.Gray;
            grid.Rows[rowIndex].DefaultCellStyle.Font =
                new Font(grid.Font, FontStyle.Strikeout | FontStyle.Italic);
        }

        public static void SetupInboundGrid(DataGridView grid)
        {
            grid.Columns.Clear();
            grid.BackgroundColor = Color.White;
            grid.BorderStyle = BorderStyle.Fixed3D;
            grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            grid.ReadOnly = true;
            grid.AllowUserToAddRows = false;
            grid.RowHeadersVisible = false;
            grid.Font = new Font("微软雅黑", 9);
            grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;

            grid.Columns.Add("Location", "库位");
            grid.Columns.Add("Model", "型号");
            grid.Columns.Add("Quantity", "入库数量");
            grid.Columns.Add("OrderCount", "单数");

            grid.Columns["Location"].Width = 88;
            grid.Columns["Model"].Width = 140;
            grid.Columns["Quantity"].Width = 96;
            grid.Columns["OrderCount"].Width = 72;
            grid.Columns["Quantity"].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
            grid.Columns["OrderCount"].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
            grid.Columns["Quantity"].DefaultCellStyle.Format = "N0";
            grid.RowTemplate.Height = 25;
            grid.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(245, 245, 245);

            grid.CellFormatting -= InboundGrid_CellFormatting;
            grid.CellFormatting += InboundGrid_CellFormatting;
        }

        private static void InboundGrid_CellFormatting(object sender, DataGridViewCellFormattingEventArgs e)
        {
            if (!(sender is DataGridView grid) || e.RowIndex < 0)
                return;

            if (e.ColumnIndex == grid.Columns["Location"].Index)
            {
                string model = grid.Rows[e.RowIndex].Cells["Model"].Value?.ToString() ?? "";
                if (model == "暂无入库记录" || model == "加载失败")
                    return;

                if (e.RowIndex > 0)
                {
                    string currentLocation = grid.Rows[e.RowIndex].Cells["Location"].Value?.ToString() ?? "";
                    string previousLocation = grid.Rows[e.RowIndex - 1].Cells["Location"].Value?.ToString() ?? "";
                    if (currentLocation == previousLocation)
                    {
                        e.Value = "";
                        e.FormattingApplied = true;
                    }
                }
            }

            if (e.ColumnIndex == grid.Columns["Quantity"].Index && e.Value != null &&
                int.TryParse(e.Value.ToString(), out int qty))
            {
                e.Value = qty.ToString("N0");
                e.FormattingApplied = true;
            }
        }

        public static string BindInboundGrid(DataGridView grid, DataTable inboundStats, DataTable inboundTotal)
        {
            grid.Rows.Clear();

            int totalQty = 0;
            int typeCount = 0;
            int orderCount = 0;
            if (inboundTotal != null && inboundTotal.Rows.Count > 0)
            {
                DataRow row = inboundTotal.Rows[0];
                totalQty = ReadIntColumn(row, "总数量");
                typeCount = ReadIntColumn(row, "型号种数");
                orderCount = ReadIntColumn(row, "总单数");
            }

            string summary = $"总入库: {totalQty:N0} 件 | 型号数: {typeCount} 种 | 总单数: {orderCount}";

            if (inboundStats != null && inboundStats.Rows.Count > 0)
            {
                foreach (DataRow row in inboundStats.Rows)
                {
                    grid.Rows.Add(
                        row["库位"]?.ToString() ?? "",
                        row["型号"]?.ToString() ?? "",
                        ReadIntColumn(row, "入库数量"),
                        ReadIntColumn(row, "入库单数"));
                }
                HighlightLocationGroups(grid);
                grid.AutoResizeRows();
            }

            return summary;
        }

        private static void HighlightLocationGroups(DataGridView grid)
        {
            if (grid.Rows.Count == 0)
                return;

            string currentLocation = "";
            Color[] groupColors =
            {
                Color.FromArgb(245, 245, 255),
                Color.FromArgb(235, 245, 235)
            };
            int colorIndex = 0;

            for (int i = 0; i < grid.Rows.Count; i++)
            {
                string location = grid.Rows[i].Cells["Location"].Value?.ToString() ?? "";
                if (location != currentLocation && !string.IsNullOrEmpty(location))
                {
                    currentLocation = location;
                    colorIndex = (colorIndex + 1) % 2;
                }

                if (string.IsNullOrEmpty(location))
                    continue;

                grid.Rows[i].DefaultCellStyle.BackColor = groupColors[colorIndex];
                if (i == 0 || grid.Rows[i - 1].Cells["Location"].Value?.ToString() != location)
                {
                    grid.Rows[i].Cells["Location"].Style.Font =
                        new Font(grid.Font, FontStyle.Bold);
                }
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
    }
}
