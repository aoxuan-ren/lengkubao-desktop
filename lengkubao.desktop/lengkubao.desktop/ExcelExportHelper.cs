using OfficeOpenXml;
using OfficeOpenXml.Style;
using System;
using System.Collections.Generic;
using System.Data;
using System.Drawing;
using System.Drawing.Printing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace lengkubao.desktop
{
    public sealed class PrintTableColumn
    {
        public PrintTableColumn(
            string title,
            string dataKey,
            float widthRatio,
            bool alignRight,
            int? charWidth = null,
            bool allowWrap = false)
        {
            Title = title;
            DataKey = dataKey;
            WidthRatio = widthRatio;
            AlignRight = alignRight;
            CharWidth = charWidth;
            AllowWrap = allowWrap;
        }

        public string Title { get; }
        public string DataKey { get; }
        public float WidthRatio { get; }
        public bool AlignRight { get; }
        public int? CharWidth { get; }
        public bool AllowWrap { get; }
    }

    public sealed class ClientBalanceReportData
    {
        public string ClientName { get; set; }
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public decimal SalesTotal { get; set; }
        public decimal PackagingTotal { get; set; }
        public decimal DeductionTotal { get; set; }
        public decimal AdvanceTotal { get; set; }
        public decimal PayableTotal { get; set; }
        public DataTable DetailsData { get; set; }
        public string InboundTotalText { get; set; }
        public DataTable InboundStats { get; set; }
    }

    public static class ExcelExportHelper
    {
        static ExcelExportHelper()
        {
            // EPPlus 8+ 须在实例化 ExcelPackage 前设置许可证
            ExcelPackage.License.SetNonCommercialOrganization("Lengkubao");
        }

        // 导出DataGridView到Excel
        public static bool ExportDataGridView(DataGridView dataGridView, string title = "报表")
        {
            if (dataGridView.Rows.Count == 0)
            {
                MessageBox.Show("没有数据可以导出！", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }

            SaveFileDialog saveDialog = new SaveFileDialog();
            saveDialog.Filter = "Excel文件 (*.xlsx)|*.xlsx|所有文件 (*.*)|*.*";
            saveDialog.FileName = $"{title}_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx";
            saveDialog.Title = "导出Excel文件";

            if (saveDialog.ShowDialog() != DialogResult.OK)
            {
                return false;
            }

            try
            {
                using (ExcelPackage excelPackage = new ExcelPackage())
                {
                    // 创建工作表
                    ExcelWorksheet worksheet = excelPackage.Workbook.Worksheets.Add("报表");

                    // 设置标题
                    worksheet.Cells[1, 1].Value = title;
                    worksheet.Cells[1, 1, 1, dataGridView.Columns.Count].Merge = true;
                    worksheet.Cells[1, 1].Style.Font.Bold = true;
                    worksheet.Cells[1, 1].Style.Font.Size = 14;
                    worksheet.Cells[1, 1].Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
                    worksheet.Cells[1, 1].Style.VerticalAlignment = ExcelVerticalAlignment.Center;

                    // 设置表头
                    int headerRow = 3;
                    for (int i = 0; i < dataGridView.Columns.Count; i++)
                    {
                        worksheet.Cells[headerRow, i + 1].Value = dataGridView.Columns[i].HeaderText;
                        worksheet.Cells[headerRow, i + 1].Style.Font.Bold = true;
                        worksheet.Cells[headerRow, i + 1].Style.Fill.PatternType = ExcelFillStyle.Solid;
                        worksheet.Cells[headerRow, i + 1].Style.Fill.BackgroundColor.SetColor(Color.LightGray);
                        worksheet.Cells[headerRow, i + 1].Style.Border.BorderAround(ExcelBorderStyle.Thin);
                    }

                    // 填充数据
                    int dataRow = headerRow + 1;
                    for (int row = 0; row < dataGridView.Rows.Count; row++)
                    {
                        for (int col = 0; col < dataGridView.Columns.Count; col++)
                        {
                            object value = dataGridView.Rows[row].Cells[col].Value;
                            worksheet.Cells[dataRow, col + 1].Value = value;
                            worksheet.Cells[dataRow, col + 1].Style.Border.BorderAround(ExcelBorderStyle.Thin);

                            // 如果是数字，设置格式
                            if (IsNumericValue(value))
                            {
                                worksheet.Cells[dataRow, col + 1].Style.Numberformat.Format = "#,##0.00";
                                worksheet.Cells[dataRow, col + 1].Style.HorizontalAlignment = ExcelHorizontalAlignment.Right;
                            }
                        }
                        dataRow++;
                    }

                    // 自动调整列宽
                    worksheet.Cells[worksheet.Dimension.Address].AutoFitColumns();

                    // 添加页脚
                    worksheet.HeaderFooter.OddFooter.CenteredText = $"导出时间: {DateTime.Now:yyyy-MM-dd HH:mm:ss} | 页 &P / &N";

                    // 保存文件
                    FileInfo excelFile = new FileInfo(saveDialog.FileName);
                    excelPackage.SaveAs(excelFile);

                    MessageBox.Show($"导出成功！\n文件已保存到：{saveDialog.FileName}",
                        "导出完成", MessageBoxButtons.OK, MessageBoxIcon.Information);

                    return true;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"导出失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
        }

        // 导出DataTable到Excel
        public static bool ExportDataTable(DataTable dataTable, string title = "报表", string sheetName = "数据")
        {
            if (dataTable.Rows.Count == 0)
            {
                MessageBox.Show("没有数据可以导出！", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }

            SaveFileDialog saveDialog = new SaveFileDialog();
            saveDialog.Filter = "Excel文件 (*.xlsx)|*.xlsx|所有文件 (*.*)|*.*";
            saveDialog.FileName = $"{title}_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx";
            saveDialog.Title = "导出Excel文件";

            if (saveDialog.ShowDialog() != DialogResult.OK)
            {
                return false;
            }

            try
            {
                using (ExcelPackage excelPackage = new ExcelPackage())
                {
                    ExcelWorksheet worksheet = excelPackage.Workbook.Worksheets.Add(sheetName);

                    // 设置标题
                    worksheet.Cells[1, 1].Value = title;
                    worksheet.Cells[1, 1, 1, dataTable.Columns.Count].Merge = true;
                    worksheet.Cells[1, 1].Style.Font.Bold = true;
                    worksheet.Cells[1, 1].Style.Font.Size = 14;
                    worksheet.Cells[1, 1].Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;

                    // 设置表头
                    int headerRow = 3;
                    for (int i = 0; i < dataTable.Columns.Count; i++)
                    {
                        worksheet.Cells[headerRow, i + 1].Value = dataTable.Columns[i].ColumnName;
                        worksheet.Cells[headerRow, i + 1].Style.Font.Bold = true;
                        worksheet.Cells[headerRow, i + 1].Style.Fill.PatternType = ExcelFillStyle.Solid;
                        worksheet.Cells[headerRow, i + 1].Style.Fill.BackgroundColor.SetColor(Color.LightBlue);
                    }

                    // 填充数据
                    int dataRow = headerRow + 1;
                    for (int row = 0; row < dataTable.Rows.Count; row++)
                    {
                        for (int col = 0; col < dataTable.Columns.Count; col++)
                        {
                            object value = dataTable.Rows[row][col];
                            worksheet.Cells[dataRow, col + 1].Value = value;

                            // 数值格式化
                            if (value is decimal || value is double || value is int)
                            {
                                worksheet.Cells[dataRow, col + 1].Style.Numberformat.Format = "#,##0.00";
                            }
                        }
                        dataRow++;
                    }

                    // 自动调整列宽
                    worksheet.Cells[worksheet.Dimension.Address].AutoFitColumns();

                    // 保存文件
                    FileInfo excelFile = new FileInfo(saveDialog.FileName);
                    excelPackage.SaveAs(excelFile);

                    MessageBox.Show($"导出成功！\n文件已保存到：{saveDialog.FileName}",
                        "导出完成", MessageBoxButtons.OK, MessageBoxIcon.Information);

                    return true;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"导出失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
        }

        // 导出特定类型的报表
        public static void ExportInboundReport(DateTime startDate, DateTime endDate)
        {
            try
            {
                DatabaseManager db = new DatabaseManager();
                DataTable data = db.GetInboundReport(startDate, endDate);

                string title = $"入库报表_{startDate:yyyyMMdd}_至_{endDate:yyyyMMdd}";
                ExportDataTable(data, title, "入库报表");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"生成入库报表失败: {ex.Message}", "错误");
            }
        }

        public static void ExportSalesReport(DateTime startDate, DateTime endDate)
        {
            try
            {
                DatabaseManager db = new DatabaseManager();
                DataTable data = db.GetSalesReport(startDate, endDate);

                string title = $"销售报表_{startDate:yyyyMMdd}_至_{endDate:yyyyMMdd}";
                ExportDataTable(data, title, "销售报表");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"生成销售报表失败: {ex.Message}", "错误");
            }
        }

        public static void ExportClientBalanceReport(string clientCode, DateTime startDate, DateTime endDate)
        {
            try
            {
                DatabaseManager db = new DatabaseManager();
                DataTable data = GetClientBalanceReportData(db, clientCode, startDate, endDate);
                string title = $"客户对账单_{clientCode}_{startDate:yyyyMMdd}_至_{endDate:yyyyMMdd}";
                ExportDataTable(data, title, "客户对账单");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"生成对账单失败: {ex.Message}", "错误");
            }
        }
        private static DataTable GetClientBalanceReportData(DatabaseManager db, string clientCode, DateTime startDate, DateTime endDate)
        {
            string sql = @"
        SELECT 
            '销售' as 类型,
            bill_no as 单据号,
            sale_date as 日期,
            total_amount as 金额,
            '' as 备注
        FROM sales_transactions 
        WHERE client_code = @client AND sale_date BETWEEN @start AND @end
        
        UNION ALL
        
        SELECT 
            '包装' as 类型,
            bill_no as 单据号,
            packaging_date as 日期,
            total_amount as 金额,
            remark as 备注
        FROM packaging_transactions 
        WHERE client_code = @client AND packaging_date BETWEEN @start AND @end
        
        ORDER BY 日期 DESC";

            var parameters = new Dictionary<string, object>
    {
        { "@client", clientCode },
        { "@start", startDate.ToString("yyyy-MM-dd") },
        { "@end", endDate.ToString("yyyy-MM-dd") }
    };

            return db.ExecuteQuery(sql, parameters);
        }
       
        // 在ExcelExportHelper.cs中添加这个方法（不修改现有方法）
        public static bool ExportDataTableToFile(DataTable dataTable, string fileName, string title = "报表", string sheetName = "数据")
        {
            if (dataTable.Rows.Count == 0)
            {
                MessageBox.Show("没有数据可以导出！", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }

            try
            {
                using (ExcelPackage excelPackage = new ExcelPackage())
                {
                    ExcelWorksheet worksheet = excelPackage.Workbook.Worksheets.Add(sheetName);

                    // 设置标题
                    worksheet.Cells[1, 1].Value = title;
                    worksheet.Cells[1, 1, 1, dataTable.Columns.Count].Merge = true;
                    worksheet.Cells[1, 1].Style.Font.Bold = true;
                    worksheet.Cells[1, 1].Style.Font.Size = 14;
                    worksheet.Cells[1, 1].Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;

                    // 设置表头
                    int headerRow = 3;
                    for (int i = 0; i < dataTable.Columns.Count; i++)
                    {
                        worksheet.Cells[headerRow, i + 1].Value = dataTable.Columns[i].ColumnName;
                        worksheet.Cells[headerRow, i + 1].Style.Font.Bold = true;
                        worksheet.Cells[headerRow, i + 1].Style.Fill.PatternType = ExcelFillStyle.Solid;
                        worksheet.Cells[headerRow, i + 1].Style.Fill.BackgroundColor.SetColor(Color.LightBlue);
                    }

                    // 填充数据
                    int dataRow = headerRow + 1;
                    for (int row = 0; row < dataTable.Rows.Count; row++)
                    {
                        for (int col = 0; col < dataTable.Columns.Count; col++)
                        {
                            object value = dataTable.Rows[row][col];
                            worksheet.Cells[dataRow, col + 1].Value = value;

                            // 数值格式化
                            if (value is decimal || value is double || value is int)
                            {
                                worksheet.Cells[dataRow, col + 1].Style.Numberformat.Format = "#,##0.00";
                            }
                        }
                        dataRow++;
                    }

                    // 自动调整列宽
                    worksheet.Cells[worksheet.Dimension.Address].AutoFitColumns();

                    // 保存文件
                    FileInfo excelFile = new FileInfo(fileName);
                    excelPackage.SaveAs(excelFile);

                    return true;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"导出失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
        }

        public static bool ExportSimpleTablePdf(
            string fileName,
            string title,
            string subtitle,
            DataTable data,
            IList<PrintTableColumn> columns)
        {
            try
            {
                var margins = DefaultReportMargins();
                var fallbackA4Paper = DefaultA4PaperSize();
                return PrintSimpleTableToPdf(fileName, title, subtitle, data, columns, margins, fallbackA4Paper);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"导出PDF失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
        }

        public static bool PrintSimpleTableWithDialog(
            string title,
            string subtitle,
            DataTable data,
            IList<PrintTableColumn> columns)
        {
            try
            {
                if (!HasInstalledPrinters())
                {
                    MessageBox.Show("未检测到可用打印机，请先在系统中安装打印机。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return false;
                }

                var margins = DefaultReportMargins();
                var fallbackA4Paper = DefaultA4PaperSize();

                using (var doc = new PrintDocument())
                {
                    doc.DefaultPageSettings.Landscape = false;
                    doc.DefaultPageSettings.Margins = margins;
                    doc.DocumentName = title ?? "查询报表";

                    using (var printDialog = new PrintDialog())
                    {
                        printDialog.Document = doc;
                        printDialog.UseEXDialog = true;
                        printDialog.AllowSomePages = false;
                        if (printDialog.ShowDialog() != DialogResult.OK)
                            return false;
                    }

                    doc.DefaultPageSettings.PaperSize = ResolveA4PaperSize(doc.PrinterSettings, fallbackA4Paper);
                    RunSimpleTablePrint(doc, title, subtitle, data, columns);
                    return true;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"打印失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
        }

        private static bool PrintSimpleTableToPdf(
            string fileName,
            string title,
            string subtitle,
            DataTable data,
            IList<PrintTableColumn> printColumns,
            Margins margins,
            PaperSize fallbackA4Paper)
        {
            PrinterSettings printerSettings = TryGetMicrosoftPrintToPdfPrinter();
            if (printerSettings == null)
                return false;

            printerSettings.PrintToFile = true;
            printerSettings.PrintFileName = fileName;

            using (var doc = new PrintDocument())
            {
                doc.PrinterSettings = printerSettings;
                doc.DefaultPageSettings.Landscape = false;
                doc.DefaultPageSettings.Margins = margins;
                doc.DefaultPageSettings.PaperSize = ResolveA4PaperSize(printerSettings, fallbackA4Paper);
                doc.DocumentName = title ?? "查询报表";
                RunSimpleTablePrint(doc, title, subtitle, data, printColumns);
            }

            return File.Exists(fileName);
        }

        private static void RunSimpleTablePrint(
            PrintDocument doc,
            string title,
            string subtitle,
            DataTable data,
            IList<PrintTableColumn> printColumns)
        {
            var layout = new ReportLayout();
            var state = new SimpleTableRenderState();

            using (var titleFont = new Font("Microsoft YaHei", 14f, FontStyle.Bold))
            using (var bodyFont = new Font("Microsoft YaHei", 9f, FontStyle.Regular))
            using (var tableHeaderFont = new Font("Microsoft YaHei", 9f, FontStyle.Bold))
            using (var borderPen = new Pen(Color.FromArgb(208, 208, 208), 1f))
            using (var headerBackBrush = new SolidBrush(Color.FromArgb(240, 240, 240)))
            {
                doc.PrintController = new StandardPrintController();

                doc.PrintPage += (s, e) =>
                {
                    float y = e.MarginBounds.Top;
                    float maxY = e.MarginBounds.Bottom;
                    float x = e.MarginBounds.Left;
                    float width = e.MarginBounds.Width;

                    if (!state.HasDrawnTitle)
                    {
                        float titleHeight = layout.TitleHeight;
                        float metaHeight = layout.RowHeight * 2f + layout.InnerGap;
                        if (y + titleHeight + metaHeight > maxY)
                        {
                            e.HasMorePages = true;
                            return;
                        }

                        DrawText(graphics: e.Graphics, title ?? string.Empty, titleFont, Brushes.Black,
                            new RectangleF(x, y, width, titleHeight), false, StringAlignment.Center);
                        y += titleHeight;

                        DrawText(e.Graphics, subtitle ?? string.Empty, bodyFont, Brushes.Black,
                            new RectangleF(x, y, width, layout.RowHeight), false, StringAlignment.Near);
                        y += layout.RowHeight + layout.SectionGap;

                        state.HasDrawnTitle = true;
                    }

                    float[] columnWidths = state.ColumnWidths;
                    int rowIndex = state.RowIndex;
                    bool done = DrawQueryTablePage(
                        e.Graphics,
                        data ?? new DataTable(),
                        printColumns,
                        ref columnWidths,
                        ref rowIndex,
                        tableHeaderFont,
                        bodyFont,
                        borderPen,
                        headerBackBrush,
                        x,
                        width,
                        ref y,
                        maxY,
                        layout);
                    state.ColumnWidths = columnWidths;
                    state.RowIndex = rowIndex;

                    e.HasMorePages = !done;
                };

                doc.Print();
            }
        }

        public static bool ExportClientBalancePdfReport(
            string fileName,
            string clientName,
            DateTime startDate,
            DateTime endDate,
            decimal salesTotal,
            decimal packagingTotal,
            decimal deductionTotal,
            decimal advanceTotal,
            decimal payableTotal,
            DataTable detailsData,
            string inboundTotalText,
            DataTable inboundStats)
        {
            try
            {
                var reportData = new ClientBalanceReportData
                {
                    ClientName = clientName,
                    StartDate = startDate,
                    EndDate = endDate,
                    SalesTotal = salesTotal,
                    PackagingTotal = packagingTotal,
                    DeductionTotal = deductionTotal,
                    AdvanceTotal = advanceTotal,
                    PayableTotal = payableTotal,
                    DetailsData = detailsData ?? new DataTable(),
                    InboundTotalText = string.IsNullOrWhiteSpace(inboundTotalText) ? "总入库: 0 件 | 型号数: 0 | 总单数: 0" : inboundTotalText,
                    InboundStats = inboundStats ?? new DataTable()
                };

                var margins = DefaultReportMargins();
                var fallbackA4Paper = DefaultA4PaperSize();
                return PrintReportToPdf(fileName, reportData, margins, fallbackA4Paper);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"导出PDF失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
        }

        public static bool PrintClientBalanceWithDialog(ClientBalanceReportData data)
        {
            try
            {
                if (data == null)
                {
                    MessageBox.Show("没有可打印的对账数据。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return false;
                }

                if (!HasInstalledPrinters())
                {
                    MessageBox.Show("未检测到可用打印机，请先在系统中安装打印机。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return false;
                }

                var margins = DefaultReportMargins();
                var fallbackA4Paper = DefaultA4PaperSize();

                using (var doc = new PrintDocument())
                {
                    doc.DefaultPageSettings.Landscape = false;
                    doc.DefaultPageSettings.Margins = margins;
                    doc.DocumentName = "客户对账单";

                    using (var printDialog = new PrintDialog())
                    {
                        printDialog.Document = doc;
                        printDialog.UseEXDialog = true;
                        printDialog.AllowSomePages = false;
                        if (printDialog.ShowDialog() != DialogResult.OK)
                            return false;
                    }

                    doc.DefaultPageSettings.PaperSize = ResolveA4PaperSize(doc.PrinterSettings, fallbackA4Paper);
                    RunClientBalancePrint(doc, data);
                    return true;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"打印失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
        }

        private static bool PrintReportToPdf(string fileName, ClientBalanceReportData data, Margins margins, PaperSize fallbackA4Paper)
        {
            PrinterSettings printerSettings = TryGetMicrosoftPrintToPdfPrinter();
            if (printerSettings == null)
                return false;

            printerSettings.PrintToFile = true;
            printerSettings.PrintFileName = fileName;

            using (var doc = new PrintDocument())
            {
                doc.PrinterSettings = printerSettings;
                doc.DefaultPageSettings.Landscape = false;
                doc.DefaultPageSettings.Margins = margins;
                doc.DefaultPageSettings.PaperSize = ResolveA4PaperSize(printerSettings, fallbackA4Paper);
                doc.DocumentName = "客户对账单PDF";
                RunClientBalancePrint(doc, data);
            }

            return File.Exists(fileName);
        }

        private static void RunClientBalancePrint(PrintDocument doc, ClientBalanceReportData data)
        {
            var layout = new ReportLayout();
            var state = new ReportRenderState();

            using (var titleFont = new Font("Microsoft YaHei", 14f, FontStyle.Bold))
            using (var sectionFont = new Font("Microsoft YaHei", 10f, FontStyle.Bold))
            using (var bodyFont = new Font("Microsoft YaHei", 9f, FontStyle.Regular))
            using (var tableHeaderFont = new Font("Microsoft YaHei", 9f, FontStyle.Bold))
            using (var borderPen = new Pen(Color.FromArgb(208, 208, 208), 1f))
            using (var headerBackBrush = new SolidBrush(Color.FromArgb(240, 240, 240)))
            {
                doc.PrintController = new StandardPrintController();

                doc.PrintPage += (s, e) =>
                {
                    float y = e.MarginBounds.Top;
                    float maxY = e.MarginBounds.Bottom;
                    float x = e.MarginBounds.Left;
                    float width = e.MarginBounds.Width;
                    state.PageNumber++;

                    if (!state.HasDrawnHeader)
                    {
                        if (!TryDrawHeaderAndSummary(e.Graphics, data, layout, titleFont, sectionFont, bodyFont, borderPen, headerBackBrush, x, width, ref y, maxY))
                        {
                            e.HasMorePages = true;
                            return;
                        }
                        state.HasDrawnHeader = true;
                        y += layout.SectionGap;
                    }

                    if (!state.HasDrawnDetailTitle)
                    {
                        if (!TryDrawSectionTitle(e.Graphics, "【交易明细】", sectionFont, x, ref y, maxY, layout))
                        {
                            e.HasMorePages = true;
                            return;
                        }
                        state.HasDrawnDetailTitle = true;
                    }

                    int detailRowIndex = state.DetailRowIndex;
                    bool detailDone = DrawTablePage(
                        e.Graphics,
                        data.DetailsData,
                        DetailColumns(),
                        ref detailRowIndex,
                        tableHeaderFont,
                        bodyFont,
                        borderPen,
                        headerBackBrush,
                        x,
                        width,
                        ref y,
                        maxY,
                        layout);
                    state.DetailRowIndex = detailRowIndex;
                    if (!detailDone)
                    {
                        e.HasMorePages = true;
                        return;
                    }

                    if (!state.HasDrawnInboundTitle)
                    {
                        y += layout.SectionGap;
                        if (!TryDrawSectionTitle(e.Graphics, "【入库统计】", sectionFont, x, ref y, maxY, layout))
                        {
                            e.HasMorePages = true;
                            return;
                        }

                        if (!TryDrawSingleLine(e.Graphics, data.InboundTotalText, bodyFont, x, ref y, maxY, layout))
                        {
                            e.HasMorePages = true;
                            return;
                        }
                        state.HasDrawnInboundTitle = true;
                    }

                    int inboundRowIndex = state.InboundRowIndex;
                    bool inboundDone = DrawTablePage(
                        e.Graphics,
                        data.InboundStats,
                        InboundColumns(),
                        ref inboundRowIndex,
                        tableHeaderFont,
                        bodyFont,
                        borderPen,
                        headerBackBrush,
                        x,
                        width,
                        ref y,
                        maxY,
                        layout);
                    state.InboundRowIndex = inboundRowIndex;
                    if (!inboundDone)
                    {
                        e.HasMorePages = true;
                        return;
                    }

                    e.HasMorePages = false;
                };

                doc.Print();
            }
        }

        private static Margins DefaultReportMargins()
        {
            return new Margins(40, 40, 35, 35);
        }

        private static PaperSize DefaultA4PaperSize()
        {
            return new PaperSize("A4", 827, 1169);
        }

        private static bool HasInstalledPrinters()
        {
            return PrinterSettings.InstalledPrinters.Count > 0;
        }

        private static PrinterSettings TryGetMicrosoftPrintToPdfPrinter()
        {
            string printerName = PrinterSettings.InstalledPrinters
                .Cast<string>()
                .FirstOrDefault(p => p.IndexOf("Microsoft Print to PDF", StringComparison.OrdinalIgnoreCase) >= 0);

            if (string.IsNullOrWhiteSpace(printerName))
            {
                MessageBox.Show("未找到 Microsoft Print to PDF 打印机。请先在系统启用该打印机。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return null;
            }

            var printerSettings = new PrinterSettings
            {
                PrinterName = printerName
            };
            return printerSettings;
        }

        private static bool TryDrawHeaderAndSummary(
            Graphics graphics,
            ClientBalanceReportData data,
            ReportLayout layout,
            Font titleFont,
            Font sectionFont,
            Font bodyFont,
            Pen borderPen,
            Brush headerBackBrush,
            float x,
            float width,
            ref float y,
            float maxY)
        {
            float titleHeight = layout.TitleHeight;
            float metaHeight = layout.RowHeight * 2f + layout.InnerGap;
            List<SummaryItem> summaryItems = BuildSummaryItems(data);
            float summaryHeight = layout.RowHeight + (summaryItems.Count * layout.RowHeight);
            float needed = titleHeight + metaHeight + layout.SectionGap + layout.RowHeight + summaryHeight;

            if (y + needed > maxY)
            {
                return false;
            }

            var titleRect = new RectangleF(x, y, width, titleHeight);
            DrawText(graphics, "客户对账单", titleFont, Brushes.Black, titleRect, false, StringAlignment.Center);
            y += titleHeight;

            DrawText(graphics, $"客户：{data.ClientName}", bodyFont, Brushes.Black, new RectangleF(x, y, width * 0.5f, layout.RowHeight), false, StringAlignment.Near);
            DrawText(graphics, $"期间：{data.StartDate:yyyy-MM-dd} 至 {data.EndDate:yyyy-MM-dd}", bodyFont, Brushes.Black, new RectangleF(x + width * 0.5f, y, width * 0.5f, layout.RowHeight), true, StringAlignment.Far);
            y += layout.RowHeight;

            DrawText(graphics, $"导出时间：{DateTime.Now:yyyy-MM-dd HH:mm:ss}", bodyFont, Brushes.Black, new RectangleF(x, y, width, layout.RowHeight), false, StringAlignment.Near);
            y += layout.RowHeight + layout.SectionGap;

            DrawText(graphics, "【对账结果】", sectionFont, Brushes.Black, new RectangleF(x, y, width, layout.RowHeight), false, StringAlignment.Near);
            y += layout.RowHeight;

            var summaryRect = new RectangleF(x, y, width, summaryHeight);
            graphics.DrawRectangle(borderPen, summaryRect.X, summaryRect.Y, summaryRect.Width, summaryRect.Height);

            float labelWidth = width * 0.5f;
            float valueWidth = width - labelWidth;
            for (int i = 0; i < summaryItems.Count; i++)
            {
                float rowY = y + i * layout.RowHeight;
                if (i == 0)
                {
                    graphics.FillRectangle(headerBackBrush, x, rowY, width, layout.RowHeight);
                }

                graphics.DrawLine(borderPen, x, rowY, x + width, rowY);
                graphics.DrawLine(borderPen, x + labelWidth, rowY, x + labelWidth, rowY + layout.RowHeight);

                DrawText(graphics, summaryItems[i].Label, i == 0 ? sectionFont : bodyFont, Brushes.Black, new RectangleF(x + layout.CellPadding, rowY, labelWidth - layout.CellPadding * 2f, layout.RowHeight), false, StringAlignment.Near);
                DrawText(graphics, summaryItems[i].Value, bodyFont, Brushes.Black, new RectangleF(x + labelWidth + layout.CellPadding, rowY, valueWidth - layout.CellPadding * 2f, layout.RowHeight), true, StringAlignment.Far);
            }
            graphics.DrawLine(borderPen, x, y + summaryHeight, x + width, y + summaryHeight);

            y += summaryHeight;
            return true;
        }

        private static bool TryDrawSectionTitle(Graphics graphics, string title, Font font, float x, ref float y, float maxY, ReportLayout layout)
        {
            if (y + layout.RowHeight > maxY)
            {
                return false;
            }

            DrawText(graphics, title, font, Brushes.Black, new RectangleF(x, y, 1000f, layout.RowHeight), false, StringAlignment.Near);
            y += layout.RowHeight;
            return true;
        }

        private static bool TryDrawSingleLine(Graphics graphics, string text, Font font, float x, ref float y, float maxY, ReportLayout layout)
        {
            if (y + layout.RowHeight > maxY)
            {
                return false;
            }

            DrawText(graphics, text ?? string.Empty, font, Brushes.Black, new RectangleF(x, y, 1000f, layout.RowHeight), false, StringAlignment.Near);
            y += layout.RowHeight;
            return true;
        }

        private static bool DrawQueryTablePage(
            Graphics graphics,
            DataTable table,
            IList<PrintTableColumn> columns,
            ref float[] columnWidths,
            ref int rowIndex,
            Font headerFont,
            Font bodyFont,
            Pen borderPen,
            Brush headerBackBrush,
            float x,
            float totalWidth,
            ref float y,
            float maxY,
            ReportLayout layout)
        {
            table = table ?? new DataTable();
            columns = columns ?? new List<PrintTableColumn>();
            if (columns.Count == 0)
                return true;

            if (columnWidths == null || columnWidths.Length != columns.Count)
            {
                columnWidths = ResolveQueryColumnPixelWidths(graphics, bodyFont, totalWidth, columns, layout.CellPadding);
            }

            float headerHeight = layout.HeaderRowHeight;
            float baseLineHeight = bodyFont.GetHeight(graphics) + 4f;
            float minRowHeight = layout.RowHeight;

            if (y + headerHeight > maxY)
                return false;

            DrawQueryTableHeader(graphics, columns, columnWidths, headerFont, borderPen, headerBackBrush, x, y, headerHeight, layout.CellPadding);
            y += headerHeight;

            if (table.Rows.Count == 0)
            {
                if (y + minRowHeight > maxY)
                    return false;

                graphics.DrawRectangle(borderPen, x, y, totalWidth, minRowHeight);
                DrawText(graphics, "暂无数据", bodyFont, Brushes.Black,
                    new RectangleF(x + layout.CellPadding, y, totalWidth - layout.CellPadding * 2f, minRowHeight),
                    false, StringAlignment.Near);
                y += minRowHeight;
                return true;
            }

            while (rowIndex < table.Rows.Count)
            {
                DataRow row = table.Rows[rowIndex];
                int lineCount = MeasureQueryRowLineCount(graphics, row, columns, columnWidths, bodyFont, layout.CellPadding);
                float rowHeight = Math.Max(minRowHeight, lineCount * baseLineHeight + layout.CellPadding);

                if (y + rowHeight > maxY)
                    return false;

                DrawQueryTableRow(graphics, columns, columnWidths, row, bodyFont, borderPen, x, y, rowHeight, layout.CellPadding, baseLineHeight);
                y += rowHeight;
                rowIndex++;
            }

            return true;
        }

        private static float[] ResolveQueryColumnPixelWidths(
            Graphics graphics,
            Font font,
            float totalWidth,
            IList<PrintTableColumn> columns,
            float cellPadding)
        {
            float charWidth = Math.Max(graphics.MeasureString("中", font).Width, 8f);
            float flexMinColWidth = charWidth * 6f + cellPadding * 2f;
            int count = columns.Count;
            var widths = new float[count];
            var flexIndices = new List<int>();
            float desiredFixedTotal = 0f;
            float flexRatioSum = 0f;

            for (int i = 0; i < count; i++)
            {
                PrintTableColumn column = columns[i];
                if (column.CharWidth.HasValue)
                {
                    widths[i] = column.CharWidth.Value * charWidth + cellPadding * 2f;
                    desiredFixedTotal += widths[i];
                }
                else
                {
                    flexIndices.Add(i);
                    flexRatioSum += column.WidthRatio;
                }
            }

            float flexMinTotal = flexIndices.Count * flexMinColWidth;
            float maxFixedTotal = Math.Max(0f, totalWidth - flexMinTotal);

            if (desiredFixedTotal > maxFixedTotal && desiredFixedTotal > 0f)
            {
                float scale = maxFixedTotal / desiredFixedTotal;
                desiredFixedTotal = 0f;
                for (int i = 0; i < count; i++)
                {
                    if (columns[i].CharWidth.HasValue)
                    {
                        widths[i] *= scale;
                        desiredFixedTotal += widths[i];
                    }
                }
            }

            float remaining = Math.Max(0f, totalWidth - desiredFixedTotal);
            if (flexIndices.Count > 0)
            {
                for (int j = 0; j < flexIndices.Count; j++)
                {
                    int i = flexIndices[j];
                    float width = flexRatioSum > 0f
                        ? remaining * (columns[i].WidthRatio / flexRatioSum)
                        : remaining / flexIndices.Count;
                    widths[i] = Math.Max(flexMinColWidth, width);
                }
            }

            float sum = 0f;
            for (int i = 0; i < count; i++)
                sum += widths[i];

            if (sum > totalWidth && sum > 0f)
            {
                float scale = totalWidth / sum;
                for (int i = 0; i < count; i++)
                    widths[i] *= scale;
            }

            return widths;
        }

        private static int MeasureQueryRowLineCount(
            Graphics graphics,
            DataRow row,
            IList<PrintTableColumn> columns,
            float[] columnWidths,
            Font font,
            float cellPadding)
        {
            int maxLines = 1;
            float baseLineHeight = font.GetHeight(graphics) + 4f;

            for (int i = 0; i < columns.Count; i++)
            {
                PrintTableColumn column = columns[i];
                string text = ReadQueryCell(row, column.DataKey);
                if (string.IsNullOrEmpty(text))
                    continue;

                if (column.AllowWrap && text.Length > 5)
                {
                    float innerWidth = Math.Max(6f, columnWidths[i] - cellPadding * 2f);
                    int lines = MeasureWrappedLineCount(graphics, text, font, innerWidth, baseLineHeight);
                    maxLines = Math.Max(maxLines, lines);
                }
            }

            return maxLines;
        }

        private static int MeasureWrappedLineCount(Graphics graphics, string text, Font font, float innerWidth, float lineHeight)
        {
            if (string.IsNullOrEmpty(text))
                return 1;

            using (var format = new StringFormat(StringFormatFlags.LineLimit))
            {
                format.Alignment = StringAlignment.Near;
                format.LineAlignment = StringAlignment.Near;
                format.Trimming = StringTrimming.Word;

                SizeF size = graphics.MeasureString(text, font, new SizeF(innerWidth, 10000f), format);
                return Math.Max(1, (int)Math.Ceiling(size.Height / lineHeight));
            }
        }

        private static void DrawQueryTableHeader(
            Graphics graphics,
            IList<PrintTableColumn> columns,
            float[] columnWidths,
            Font font,
            Pen borderPen,
            Brush headerBackBrush,
            float x,
            float y,
            float height,
            float cellPadding)
        {
            float totalWidth = 0f;
            for (int i = 0; i < columnWidths.Length; i++)
                totalWidth += columnWidths[i];

            graphics.FillRectangle(headerBackBrush, x, y, totalWidth, height);
            graphics.DrawRectangle(borderPen, x, y, totalWidth, height);

            float currentX = x;
            for (int i = 0; i < columns.Count; i++)
            {
                float colWidth = columnWidths[i];
                if (i > 0)
                    graphics.DrawLine(borderPen, currentX, y, currentX, y + height);

                DrawText(graphics, columns[i].Title, font, Brushes.Black,
                    new RectangleF(currentX + cellPadding, y, Math.Max(6f, colWidth - cellPadding * 2f), height),
                    columns[i].AlignRight,
                    columns[i].AlignRight ? StringAlignment.Far : StringAlignment.Near);
                currentX += colWidth;
            }
        }

        private static void DrawQueryTableRow(
            Graphics graphics,
            IList<PrintTableColumn> columns,
            float[] columnWidths,
            DataRow row,
            Font font,
            Pen borderPen,
            float x,
            float y,
            float rowHeight,
            float cellPadding,
            float lineHeight)
        {
            float tableWidth = 0f;
            for (int i = 0; i < columnWidths.Length; i++)
                tableWidth += columnWidths[i];

            graphics.DrawRectangle(borderPen, x, y, tableWidth, rowHeight);

            float currentX = x;
            for (int i = 0; i < columns.Count; i++)
            {
                PrintTableColumn column = columns[i];
                float colWidth = columnWidths[i];
                if (i > 0)
                    graphics.DrawLine(borderPen, currentX, y, currentX, y + rowHeight);

                string value = ReadQueryCell(row, column.DataKey);
                var rect = new RectangleF(
                    currentX + cellPadding,
                    y + cellPadding / 2f,
                    Math.Max(6f, colWidth - cellPadding * 2f),
                    rowHeight - cellPadding);

                var clipState = graphics.Save();
                graphics.SetClip(new RectangleF(currentX, y, colWidth, rowHeight));

                if (column.AllowWrap && value.Length > 5)
                    DrawWrappedText(graphics, value, font, Brushes.Black, rect, column.AlignRight);
                else
                    DrawText(graphics, value, font, Brushes.Black, rect, column.AlignRight,
                        column.AlignRight ? StringAlignment.Far : StringAlignment.Near);

                graphics.Restore(clipState);

                currentX += colWidth;
            }
        }

        private static string ReadQueryCell(DataRow row, string key)
        {
            if (row == null || string.IsNullOrWhiteSpace(key) || row.Table == null || !row.Table.Columns.Contains(key))
                return string.Empty;

            return Convert.ToString(row[key]) ?? string.Empty;
        }

        private static void DrawWrappedText(
            Graphics graphics,
            string text,
            Font font,
            Brush brush,
            RectangleF rect,
            bool alignRight)
        {
            using (var format = new StringFormat(StringFormatFlags.LineLimit))
            {
                format.Alignment = alignRight ? StringAlignment.Far : StringAlignment.Near;
                format.LineAlignment = StringAlignment.Center;
                format.Trimming = StringTrimming.Word;
                graphics.DrawString(text ?? string.Empty, font, brush, rect, format);
            }
        }

        private static bool DrawTablePage(
            Graphics graphics,
            DataTable table,
            List<TableColumn> columns,
            ref int rowIndex,
            Font headerFont,
            Font bodyFont,
            Pen borderPen,
            Brush headerBackBrush,
            float x,
            float width,
            ref float y,
            float maxY,
            ReportLayout layout)
        {
            table = table ?? new DataTable();
            float headerHeight = layout.HeaderRowHeight;
            float rowHeight = layout.RowHeight;

            if (y + headerHeight > maxY)
            {
                return false;
            }

            DrawTableHeader(graphics, columns, headerFont, borderPen, headerBackBrush, x, y, width, headerHeight, layout.CellPadding);
            y += headerHeight;

            if (table.Rows.Count == 0)
            {
                if (y + rowHeight > maxY)
                {
                    return false;
                }

                DrawText(graphics, "暂无数据", bodyFont, Brushes.Black, new RectangleF(x + layout.CellPadding, y, width - layout.CellPadding * 2f, rowHeight), false, StringAlignment.Near);
                graphics.DrawRectangle(borderPen, x, y, width, rowHeight);
                y += rowHeight;
                return true;
            }

            while (rowIndex < table.Rows.Count)
            {
                if (y + rowHeight > maxY)
                {
                    return false;
                }

                DataRow row = table.Rows[rowIndex];
                DrawTableRow(graphics, columns, row, bodyFont, borderPen, x, y, width, rowHeight, layout.CellPadding);
                y += rowHeight;
                rowIndex++;
            }

            return true;
        }

        private static void DrawTableHeader(
            Graphics graphics,
            List<TableColumn> columns,
            Font font,
            Pen borderPen,
            Brush headerBackBrush,
            float x,
            float y,
            float width,
            float height,
            float cellPadding)
        {
            graphics.FillRectangle(headerBackBrush, x, y, width, height);
            graphics.DrawRectangle(borderPen, x, y, width, height);

            float currentX = x;
            for (int i = 0; i < columns.Count; i++)
            {
                float colWidth = width * columns[i].WidthRatio;
                if (i > 0)
                {
                    graphics.DrawLine(borderPen, currentX, y, currentX, y + height);
                }

                DrawText(
                    graphics,
                    columns[i].Title,
                    font,
                    Brushes.Black,
                    new RectangleF(currentX + cellPadding, y, Math.Max(6f, colWidth - cellPadding * 2f), height),
                    columns[i].AlignRight,
                    columns[i].AlignRight ? StringAlignment.Far : StringAlignment.Near);
                currentX += colWidth;
            }
        }

        private static void DrawTableRow(
            Graphics graphics,
            List<TableColumn> columns,
            DataRow row,
            Font font,
            Pen borderPen,
            float x,
            float y,
            float width,
            float height,
            float cellPadding)
        {
            graphics.DrawRectangle(borderPen, x, y, width, height);

            float currentX = x;
            for (int i = 0; i < columns.Count; i++)
            {
                float colWidth = width * columns[i].WidthRatio;
                if (i > 0)
                {
                    graphics.DrawLine(borderPen, currentX, y, currentX, y + height);
                }

                string value = ReadCell(row, columns[i].DataKey);
                DrawText(
                    graphics,
                    value,
                    font,
                    Brushes.Black,
                    new RectangleF(currentX + cellPadding, y, Math.Max(6f, colWidth - cellPadding * 2f), height),
                    columns[i].AlignRight,
                    columns[i].AlignRight ? StringAlignment.Far : StringAlignment.Near);
                currentX += colWidth;
            }
        }

        private static string ReadCell(DataRow row, string key)
        {
            if (row == null || string.IsNullOrWhiteSpace(key) || row.Table == null || !row.Table.Columns.Contains(key))
            {
                return string.Empty;
            }

            return Crop(Convert.ToString(row[key]), 40);
        }

        private static void DrawText(Graphics graphics, string text, Font font, Brush brush, RectangleF rect, bool alignRight, StringAlignment horizontalAlignment)
        {
            using (var format = new StringFormat(StringFormatFlags.LineLimit))
            {
                format.Alignment = horizontalAlignment;
                format.LineAlignment = StringAlignment.Center;
                format.Trimming = StringTrimming.EllipsisCharacter;
                format.FormatFlags |= StringFormatFlags.NoWrap;
                graphics.DrawString(text ?? string.Empty, font, brush, rect, format);
            }
        }

        private static PaperSize ResolveA4PaperSize(PrinterSettings printerSettings, PaperSize fallbackA4Paper)
        {
            foreach (PaperSize size in printerSettings.PaperSizes)
            {
                if (size == null || string.IsNullOrWhiteSpace(size.PaperName))
                {
                    continue;
                }

                string name = size.PaperName.ToLowerInvariant();
                if (name.Contains("a4") || name.Contains("210 x 297"))
                {
                    return size;
                }
            }

            return fallbackA4Paper;
        }

        private static List<SummaryItem> BuildSummaryItems(ClientBalanceReportData data)
        {
            var items = new List<SummaryItem>
            {
                new SummaryItem("项目", "金额"),
                new SummaryItem("销售总款", $"{data.SalesTotal:N2} 元")
            };

            if (data.PackagingTotal != 0) items.Add(new SummaryItem("包装扣款", $"{data.PackagingTotal:N2} 元"));
            if (data.AdvanceTotal != 0) items.Add(new SummaryItem("预支款项", $"{data.AdvanceTotal:N2} 元"));
            if (data.DeductionTotal != 0) items.Add(new SummaryItem("其他扣款", $"{data.DeductionTotal:N2} 元"));

            decimal deductionAll = data.PackagingTotal + data.DeductionTotal + data.AdvanceTotal;
            items.Add(new SummaryItem("扣款合计", $"{deductionAll:N2} 元"));
            items.Add(new SummaryItem("应付总款", $"{data.PayableTotal:N2} 元"));

            string statement = data.PayableTotal > 0
                ? $"公司应向客户支付：{data.PayableTotal:N2} 元"
                : data.PayableTotal < 0
                    ? $"客户应向公司支付：{Math.Abs(data.PayableTotal):N2} 元"
                    : "账目已结清";
            items.Add(new SummaryItem("结论", statement));
            return items;
        }

        private static List<TableColumn> DetailColumns()
        {
            return new List<TableColumn>
            {
                new TableColumn("类型", "Type", 0.11f, false),
                new TableColumn("日期", "TransactionDate", 0.12f, false),
                new TableColumn("商品", "ItemType", 0.15f, false),
                new TableColumn("库位", "Location", 0.08f, false),
                new TableColumn("数量", "Quantity", 0.10f, true),
                new TableColumn("单价", "UnitPrice", 0.11f, true),
                new TableColumn("金额", "Amount", 0.12f, true),
                new TableColumn("经手人", "Handler", 0.09f, false),
                new TableColumn("备注", "Reason", 0.12f, false)
            };
        }

        private static List<TableColumn> InboundColumns()
        {
            return new List<TableColumn>
            {
                new TableColumn("库位", "库位", 0.14f, false),
                new TableColumn("型号", "型号", 0.50f, false),
                new TableColumn("入库数量", "入库数量", 0.20f, true),
                new TableColumn("单数", "单数", 0.16f, true)
            };
        }

        private sealed class SimpleTableRenderState
        {
            public bool HasDrawnTitle { get; set; }
            public int RowIndex { get; set; }
            public float[] ColumnWidths { get; set; }
        }

        private sealed class ReportLayout
        {
            public float TitleHeight { get; } = 30f;
            public float HeaderRowHeight { get; } = 24f;
            public float RowHeight { get; } = 22f;
            public float SectionGap { get; } = 10f;
            public float InnerGap { get; } = 4f;
            public float CellPadding { get; } = 4f;
        }

        private sealed class ReportRenderState
        {
            public bool HasDrawnHeader { get; set; }
            public bool HasDrawnDetailTitle { get; set; }
            public bool HasDrawnInboundTitle { get; set; }
            public int DetailRowIndex { get; set; }
            public int InboundRowIndex { get; set; }
            public int PageNumber { get; set; }
        }

        private sealed class TableColumn
        {
            public TableColumn(string title, string dataKey, float widthRatio, bool alignRight)
            {
                Title = title;
                DataKey = dataKey;
                WidthRatio = widthRatio;
                AlignRight = alignRight;
            }

            public string Title { get; }
            public string DataKey { get; }
            public float WidthRatio { get; }
            public bool AlignRight { get; }
        }

        private sealed class SummaryItem
        {
            public SummaryItem(string label, string value)
            {
                Label = label;
                Value = value;
            }

            public string Label { get; }
            public string Value { get; }
        }

        private static string Crop(string text, int maxLen)
        {
            string value = text ?? "";
            if (value.Length <= maxLen) return value;
            return value.Substring(0, Math.Max(0, maxLen - 1)) + "…";
        }

        private static bool IsNumericValue(object value)
        {
            if (value == null) return false;

            return value is int || value is decimal || value is double || value is float ||
                   value is long || value is short || value is byte;
        }

    }
}