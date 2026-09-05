using System;
using System.IO;
using System.IO.Compression;
using System.Windows.Forms;

namespace lengkubao.desktop
{
    public class YearEndSettlementResult
    {
        public bool Success { get; set; }
        public string ErrorMessage { get; set; }
        public string ArchivePath { get; set; }
    }

    public class YearEndSettlementService
    {
        private readonly DatabaseManager _db;

        public YearEndSettlementService()
        {
            _db = new DatabaseManager();
        }

        public DatabaseManager.YearEndPreviewData LoadPreview(int fiscalYear)
        {
            return _db.GetYearEndPreview(fiscalYear);
        }

        public string CreatePreSettlementBackup()
        {
            string backupDir = Path.Combine(Application.StartupPath, "数据备份");
            Directory.CreateDirectory(backupDir);

            string dbPath = DatabaseManager.GetDatabaseFilePath();
            if (string.IsNullOrEmpty(dbPath) || !File.Exists(dbPath))
                throw new InvalidOperationException("找不到数据库文件。");

            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string zipPath = Path.Combine(backupDir, $"年结前_{timestamp}.zip");

            string tempDir = Path.Combine(Path.GetTempPath(), $"lkb_prewizard_{timestamp}");
            Directory.CreateDirectory(tempDir);
            try
            {
                File.Copy(dbPath, Path.Combine(tempDir, Path.GetFileName(dbPath)), true);
                ZipFile.CreateFromDirectory(tempDir, zipPath);
            }
            finally
            {
                try { Directory.Delete(tempDir, true); } catch { }
            }
            return zipPath;
        }

        public YearEndSettlementResult ArchiveAndSettle(
            int fiscalYear,
            string inventoryCarryover,
            string settledBy)
        {
            var result = new YearEndSettlementResult();

            if (_db.IsYearEndSettled(fiscalYear))
            {
                result.ErrorMessage = $"{fiscalYear} 年度已执行过年终结，不可重复操作。";
                return result;
            }

            string dbPath = DatabaseManager.GetDatabaseFilePath();
            if (string.IsNullOrEmpty(dbPath) || !File.Exists(dbPath))
            {
                result.ErrorMessage = "找不到数据库文件。";
                return result;
            }

            try
            {
                var preview = _db.GetYearEndPreview(fiscalYear);
                string reportsDir = Path.Combine(Path.GetTempPath(), $"lkb_reports_{fiscalYear}_{DateTime.Now:yyyyMMddHHmmss}");
                YearEndReportGenerator.GenerateAllReports(_db, fiscalYear, reportsDir);

                string zipPath = YearEndArchiveManager.CreateArchivePackage(
                    fiscalYear,
                    dbPath,
                    reportsDir,
                    preview.OpeningBalanceTotal,
                    inventoryCarryover);

                _db.ExecuteYearEndSettlement(
                    fiscalYear,
                    zipPath,
                    inventoryCarryover,
                    settledBy,
                    out string settleError);

                if (!string.IsNullOrEmpty(settleError))
                {
                    result.ErrorMessage = settleError;
                    return result;
                }

                result.Success = true;
                result.ArchivePath = zipPath;
            }
            catch (Exception ex)
            {
                result.ErrorMessage = ex.Message;
            }
            finally
            {
                try
                {
                    string tempReports = Path.Combine(Path.GetTempPath(), $"lkb_reports_{fiscalYear}_");
                    foreach (string dir in Directory.GetDirectories(Path.GetTempPath(), "lkb_reports_*"))
                    {
                        if (dir.Contains(fiscalYear.ToString()))
                            Directory.Delete(dir, true);
                    }
                }
                catch { }
            }

            return result;
        }
    }
}
