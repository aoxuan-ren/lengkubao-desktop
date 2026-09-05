using System;
using System.Data;
using System.Drawing;
using System.IO;
using OfficeOpenXml;
using OfficeOpenXml.Style;

namespace lengkubao.desktop
{
    public static class YearEndReportGenerator
    {
        public static void GenerateAllReports(DatabaseManager db, int fiscalYear, string outputDir)
        {
            Directory.CreateDirectory(outputDir);

            DataTable profit = db.GetYearEndProfitReport(fiscalYear);
            SaveExcel(
                Path.Combine(outputDir, $"年度利润汇总_{fiscalYear}.xlsx"),
                $"年度利润汇总 {fiscalYear}",
                profit,
                "利润汇总");

            DataTable balance = db.GetYearEndClientBalanceReport(fiscalYear);
            SaveExcel(
                Path.Combine(outputDir, $"客户对账汇总_{fiscalYear}.xlsx"),
                $"客户对账汇总 {fiscalYear}",
                balance,
                "客户对账");

            DataTable inventory = db.GetInventoryByLocation();
            SaveExcel(
                Path.Combine(outputDir, $"库存结转表_{fiscalYear}.xlsx"),
                $"库存结转表 {fiscalYear}",
                inventory,
                "库存结转");
        }

        private static void SaveExcel(string filePath, string title, DataTable data, string sheetName)
        {
            try
            {
                using (var package = new ExcelPackage())
                {
                    var ws = package.Workbook.Worksheets.Add(sheetName);
                    int colCount = Math.Max(1, data.Columns.Count);

                    ws.Cells[1, 1].Value = title;
                    ws.Cells[1, 1, 1, colCount].Merge = true;
                    ws.Cells[1, 1].Style.Font.Bold = true;
                    ws.Cells[1, 1].Style.Font.Size = 14;
                    ws.Cells[1, 1].Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;

                    int headerRow = 3;
                    for (int i = 0; i < data.Columns.Count; i++)
                    {
                        ws.Cells[headerRow, i + 1].Value = data.Columns[i].ColumnName;
                        ws.Cells[headerRow, i + 1].Style.Font.Bold = true;
                        ws.Cells[headerRow, i + 1].Style.Fill.PatternType = ExcelFillStyle.Solid;
                        ws.Cells[headerRow, i + 1].Style.Fill.BackgroundColor.SetColor(Color.LightBlue);
                    }

                    int dataRow = headerRow + 1;
                    for (int r = 0; r < data.Rows.Count; r++)
                    {
                        for (int c = 0; c < data.Columns.Count; c++)
                        {
                            object value = data.Rows[r][c];
                            ws.Cells[dataRow, c + 1].Value = value;
                            if (value is decimal || value is double || value is int || value is long)
                                ws.Cells[dataRow, c + 1].Style.Numberformat.Format = "#,##0.00";
                        }
                        dataRow++;
                    }

                    if (ws.Dimension != null)
                        ws.Cells[ws.Dimension.Address].AutoFitColumns();

                    package.SaveAs(new FileInfo(filePath));
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($">>> YearEndReportGenerator 保存失败 [{filePath}]: {ex.Message}");
                throw;
            }
        }
    }
}
