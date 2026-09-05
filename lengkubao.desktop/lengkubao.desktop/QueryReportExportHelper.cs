using System;
using System.Collections.Generic;
using System.Data;
using System.IO;

namespace lengkubao.desktop
{
    public enum QueryReportKind
    {
        Inbound,
        Sales,
        Packaging
    }

    /// <summary>
    /// 查询窗体导出：精简关键列，供 PDF 打印。
    /// </summary>
    public static class QueryReportExportHelper
    {
        public const string AllClientsLabel = "全部客户";

        public sealed class QueryReportContext
        {
            public string Title { get; set; }
            public string Subtitle { get; set; }
            public DataTable PrintTable { get; set; }
            public List<PrintTableColumn> Columns { get; set; }
        }

        public static QueryReportContext BuildQueryReportContext(
            DataTable source,
            QueryReportKind kind,
            string clientName,
            DateTime startDate,
            DateTime endDate)
        {
            DataTable printTable = BuildPrintTable(source, kind);
            return new QueryReportContext
            {
                Title = BuildReportTitle(kind, clientName),
                Subtitle = BuildSubtitle(startDate, endDate, printTable.Rows.Count),
                PrintTable = printTable,
                Columns = GetPdfColumns(kind)
            };
        }

        private sealed class ColumnSpec
        {
            public string DisplayName { get; set; }
            public Func<DataRow, string> GetValue { get; set; }
        }

        public static string ResolveExportClientName(ClientSearchItem selectedClient)
        {
            if (selectedClient != null
                && !ClientSearchHelper.IsNoneFilter(selectedClient)
                && !string.IsNullOrWhiteSpace(selectedClient.Name))
            {
                return selectedClient.Name.Trim();
            }

            return AllClientsLabel;
        }

        public static string BuildReportTitle(QueryReportKind kind, string clientName)
        {
            return $"{NormalizeDisplayName(clientName)} {GetBaseReportTitle(kind)}";
        }

        public static string BuildDefaultFileName(QueryReportKind kind, string clientName)
        {
            return $"{SanitizeFileName(NormalizeDisplayName(clientName))}_{GetKindFilePrefix(kind)}_{DateTime.Now:yyyyMMdd}.pdf";
        }

        public static string BuildSubtitle(DateTime startDate, DateTime endDate, int rowCount)
        {
            return $"期间：{startDate:yyyy-MM-dd} 至 {endDate:yyyy-MM-dd}  |  记录数：{rowCount}  |  导出时间：{DateTime.Now:yyyy-MM-dd HH:mm:ss}";
        }

        public static DataTable BuildPrintTable(DataTable source, QueryReportKind kind)
        {
            if (source == null)
                return new DataTable();

            var specs = GetColumnSpecs(source, kind);
            var table = new DataTable();
            foreach (var spec in specs)
            {
                table.Columns.Add(spec.DisplayName, typeof(string));
            }

            foreach (DataRow row in source.Rows)
            {
                var values = new object[specs.Count];
                for (int i = 0; i < specs.Count; i++)
                {
                    values[i] = specs[i].GetValue(row) ?? string.Empty;
                }
                table.Rows.Add(values);
            }

            return table;
        }

        public static List<PrintTableColumn> GetPdfColumns(QueryReportKind kind)
        {
            switch (kind)
            {
                case QueryReportKind.Inbound:
                    return new List<PrintTableColumn>
                    {
                        Col("入库日期", "入库日期", 0.08f, false, 8),
                        Col("客户名称", "客户名称", 0.08f, false, 5),
                        Col("商品型号", "商品型号", 0.24f, false),
                        Col("库位", "库位", 0.10f, false, 4),
                        Col("数量", "数量", 0.08f, true, 6),
                        Col("单价", "单价", 0.10f, true, 8),
                        Col("总金额", "总金额", 0.12f, true, 9),
                        Col("经手人", "经手人", 0.08f, false, 5)
                    };

                case QueryReportKind.Sales:
                    return new List<PrintTableColumn>
                    {
                        Col("日期", "日期", 0.08f, false, 8),
                        Col("客户名称", "客户名称", 0.07f, false, 5),
                        Col("规格", "规格", 0.12f, false),
                        Col("库位", "库位", 0.08f, false, 4),
                        Col("数量", "数量", 0.07f, true, 6),
                        Col("单价", "单价", 0.09f, true, 8),
                        Col("总金额", "总金额", 0.10f, true, 9),
                        Col("经手人", "经手人", 0.07f, false, 5),
                        Col("状态", "状态", 0.08f, false, 4),
                        Col("备注", "备注", 0.10f, false, 5, allowWrap: true)
                    };

                case QueryReportKind.Packaging:
                    return new List<PrintTableColumn>
                    {
                        Col("日期", "日期", 0.08f, false, 8),
                        Col("客户名称", "客户名称", 0.07f, false, 5),
                        Col("包装明细", "包装明细", 0.10f, false, 6),
                        Col("包装类型", "包装类型", 0.07f, false, 5),
                        Col("数量", "数量", 0.08f, true, 6),
                        Col("单价", "单价", 0.09f, true, 8),
                        Col("总金额", "总金额", 0.10f, true, 9),
                        Col("经手人", "经手人", 0.07f, false, 5),
                        Col("备注", "备注", 0.12f, false, 5, allowWrap: true)
                    };

                default:
                    return new List<PrintTableColumn>();
            }
        }

        private static PrintTableColumn Col(
            string title,
            string dataKey,
            float widthRatio,
            bool alignRight,
            int? charWidth = null,
            bool allowWrap = false)
        {
            return new PrintTableColumn(title, dataKey, widthRatio, alignRight, charWidth, allowWrap);
        }

        private static string GetBaseReportTitle(QueryReportKind kind)
        {
            switch (kind)
            {
                case QueryReportKind.Inbound: return "入库记录报表";
                case QueryReportKind.Sales: return "销售记录报表";
                case QueryReportKind.Packaging: return "包装记录报表";
                default: return "查询报表";
            }
        }

        private static string GetKindFilePrefix(QueryReportKind kind)
        {
            switch (kind)
            {
                case QueryReportKind.Inbound: return "入库记录";
                case QueryReportKind.Sales: return "销售记录";
                case QueryReportKind.Packaging: return "包装记录";
                default: return "查询记录";
            }
        }

        private static string NormalizeDisplayName(string clientName)
        {
            return string.IsNullOrWhiteSpace(clientName) ? AllClientsLabel : clientName.Trim();
        }

        private static string SanitizeFileName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return AllClientsLabel;

            foreach (char c in Path.GetInvalidFileNameChars())
            {
                name = name.Replace(c, '_');
            }

            return name.Trim();
        }

        private static List<ColumnSpec> GetColumnSpecs(DataTable source, QueryReportKind kind)
        {
            switch (kind)
            {
                case QueryReportKind.Inbound:
                    return new List<ColumnSpec>
                    {
                        Spec(source, "date", "入库日期", FormatDate),
                        Spec(source, "client_name", "客户名称"),
                        Spec(source, "spec", "商品型号"),
                        Spec(source, "location", "库位"),
                        Spec(source, "quantity", "数量", FormatQuantity),
                        Spec(source, "unit_price", "单价", FormatMoney),
                        Spec(source, "total_amount", "总金额", FormatMoney),
                        Spec(source, "handler", "经手人")
                    };

                case QueryReportKind.Sales:
                    return new List<ColumnSpec>
                    {
                        SpecComputed("日期", row => FormatDate(GetTransactionDate(row))),
                        Spec(source, "client_name", "客户名称"),
                        Spec(source, "spec", "规格"),
                        Spec(source, "location", "库位"),
                        Spec(source, "quantity", "数量", FormatQuantity),
                        Spec(source, "unit_price", "单价", FormatMoney),
                        Spec(source, "total_amount", "总金额", FormatMoney),
                        Spec(source, "handler", "经手人"),
                        SpecComputed("状态", FormatSalesStatus),
                        Spec(source, "remarks", "备注")
                    };

                case QueryReportKind.Packaging:
                    return new List<ColumnSpec>
                    {
                        SpecComputed("日期", row => FormatDate(GetTransactionDate(row))),
                        Spec(source, "client_name", "客户名称"),
                        Spec(source, "pack_type", "包装明细"),
                        SpecComputed("包装类型", FormatPackagingType),
                        Spec(source, "quantity", "数量", FormatQuantity),
                        Spec(source, "unit_price", "单价", FormatMoney),
                        Spec(source, "total_amount", "总金额", FormatMoney),
                        Spec(source, "handler", "经手人"),
                        Spec(source, "remarks", "备注")
                    };

                default:
                    return new List<ColumnSpec>();
            }
        }

        private static ColumnSpec Spec(DataTable table, string field, string displayName, Func<object, string> formatter = null)
        {
            if (!table.Columns.Contains(field))
            {
                return new ColumnSpec
                {
                    DisplayName = displayName,
                    GetValue = _ => string.Empty
                };
            }

            return new ColumnSpec
            {
                DisplayName = displayName,
                GetValue = row =>
                {
                    object value = row[field];
                    if (value == null || value == DBNull.Value)
                        return string.Empty;
                    return formatter != null ? formatter(value) : value.ToString().Trim();
                }
            };
        }

        private static ColumnSpec SpecComputed(string displayName, Func<DataRow, string> getValue)
        {
            return new ColumnSpec
            {
                DisplayName = displayName,
                GetValue = getValue
            };
        }

        private static object GetTransactionDate(DataRow row)
        {
            if (row.Table.Columns.Contains("date") && row["date"] != DBNull.Value && row["date"] != null)
                return row["date"];
            if (row.Table.Columns.Contains("created_time") && row["created_time"] != DBNull.Value)
                return row["created_time"];
            return null;
        }

        private static string FormatSalesStatus(DataRow row)
        {
            if (row.Table.Columns.Contains("is_settled")
                && row["is_settled"] != DBNull.Value
                && Convert.ToBoolean(row["is_settled"]))
            {
                return "已结清";
            }

            if (row.Table.Columns.Contains("status") && row["status"] != DBNull.Value)
            {
                string status = row["status"].ToString().Trim();
                if (!string.IsNullOrEmpty(status))
                    return status;
            }

            return "处理中";
        }

        private static string FormatPackagingType(DataRow row)
        {
            if (row.Table.Columns.Contains("pack_flag") && row["pack_flag"] != DBNull.Value)
            {
                string flag = row["pack_flag"].ToString().Trim().ToUpperInvariant();
                if (flag == "RETURN") return "进包装";
                if (flag == "TAKE") return "出包装";
            }

            if (row.Table.Columns.Contains("packaging_type_flag") && row["packaging_type_flag"] != DBNull.Value)
            {
                string flag = row["packaging_type_flag"].ToString().Trim().ToUpperInvariant();
                if (flag == "RETURN") return "进包装";
                if (flag == "TAKE") return "出包装";
            }

            if (row.Table.Columns.Contains("total_amount") && row["total_amount"] != DBNull.Value)
            {
                decimal amount = Convert.ToDecimal(row["total_amount"]);
                return amount < 0 ? "进包装" : "出包装";
            }

            return "出包装";
        }

        private static string FormatDate(object value)
        {
            if (value == null || value == DBNull.Value)
                return string.Empty;

            try
            {
                return Convert.ToDateTime(value).ToString("yyyy-MM-dd");
            }
            catch
            {
                return value.ToString();
            }
        }

        private static string FormatQuantity(object value)
        {
            try
            {
                return Convert.ToDecimal(value).ToString("N0");
            }
            catch
            {
                return value?.ToString() ?? string.Empty;
            }
        }

        private static string FormatMoney(object value)
        {
            try
            {
                return Convert.ToDecimal(value).ToString("N2");
            }
            catch
            {
                return value?.ToString() ?? string.Empty;
            }
        }
    }
}
