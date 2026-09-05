using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Forms;
using Newtonsoft.Json;

namespace lengkubao.desktop
{
    public class YearEndArchiveMeta
    {
        public int FiscalYear { get; set; }
        public DateTime ArchivedAt { get; set; }
        public string AppVersion { get; set; }
        public string DbFileName { get; set; }
        public string DbSha256 { get; set; }
        public List<string> ReportFiles { get; set; } = new List<string>();
        public decimal OpeningBalanceTotal { get; set; }
        public string InventoryCarryoverMode { get; set; }
        public string ArchiveZipPath { get; set; }
    }

    public static class YearEndArchiveManager
    {
        public static string GetArchiveRootPath()
        {
            return Path.Combine(Application.StartupPath, "年度账本");
        }

        public static string GetYearArchiveDir(int fiscalYear)
        {
            return Path.Combine(GetArchiveRootPath(), fiscalYear.ToString());
        }

        public static string BuildArchiveZipPath(int fiscalYear)
        {
            return Path.Combine(GetYearArchiveDir(fiscalYear), $"{fiscalYear}_冷库宝年度账本.zip");
        }

        public static string ComputeSha256(string filePath)
        {
            using (var sha = SHA256.Create())
            using (var stream = File.OpenRead(filePath))
            {
                byte[] hash = sha.ComputeHash(stream);
                return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
            }
        }

        public static string CreateArchivePackage(
            int fiscalYear,
            string dbSourcePath,
            string reportsDir,
            decimal openingBalanceTotal,
            string inventoryCarryoverMode)
        {
            string yearDir = GetYearArchiveDir(fiscalYear);
            Directory.CreateDirectory(yearDir);

            string zipPath = BuildArchiveZipPath(fiscalYear);
            if (File.Exists(zipPath))
                File.Delete(zipPath);

            string tempDir = Path.Combine(Path.GetTempPath(), $"lkb_archive_{fiscalYear}_{DateTime.Now:yyyyMMddHHmmss}");
            Directory.CreateDirectory(tempDir);

            try
            {
                string dbFileName = $"lengkubao_{fiscalYear}.db";
                string tempDb = Path.Combine(tempDir, dbFileName);
                File.Copy(dbSourcePath, tempDb, true);

                string reportsDest = Path.Combine(tempDir, "reports");
                if (Directory.Exists(reportsDir))
                {
                    CopyDirectory(reportsDir, reportsDest);
                }
                else
                {
                    Directory.CreateDirectory(reportsDest);
                }

                var reportFiles = Directory.Exists(reportsDest)
                    ? Directory.GetFiles(reportsDest).Select(Path.GetFileName).ToList()
                    : new List<string>();

                var meta = new YearEndArchiveMeta
                {
                    FiscalYear = fiscalYear,
                    ArchivedAt = DateTime.Now,
                    AppVersion = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "1.0",
                    DbFileName = dbFileName,
                    DbSha256 = ComputeSha256(tempDb),
                    ReportFiles = reportFiles,
                    OpeningBalanceTotal = openingBalanceTotal,
                    InventoryCarryoverMode = inventoryCarryoverMode ?? "",
                    ArchiveZipPath = zipPath
                };

                string metaJson = JsonConvert.SerializeObject(meta, Formatting.Indented);
                File.WriteAllText(Path.Combine(tempDir, "archive_meta.json"), metaJson, Encoding.UTF8);

                ZipFile.CreateFromDirectory(tempDir, zipPath);
                return zipPath;
            }
            finally
            {
                try
                {
                    if (Directory.Exists(tempDir))
                        Directory.Delete(tempDir, true);
                }
                catch { }
            }
        }

        public static List<YearEndArchiveMeta> ListArchives()
        {
            var list = new List<YearEndArchiveMeta>();
            string root = GetArchiveRootPath();
            if (!Directory.Exists(root))
                return list;

            foreach (string yearDir in Directory.GetDirectories(root))
            {
                string dirName = Path.GetFileName(yearDir);
                if (!int.TryParse(dirName, out int year))
                    continue;

                string zipPath = BuildArchiveZipPath(year);
                if (!File.Exists(zipPath))
                {
                    string[] zips = Directory.GetFiles(yearDir, "*.zip");
                    if (zips.Length == 0)
                        continue;
                    zipPath = zips[0];
                }

                YearEndArchiveMeta meta = TryReadMetaFromZip(zipPath);
                if (meta == null)
                {
                    meta = new YearEndArchiveMeta
                    {
                        FiscalYear = year,
                        ArchivedAt = File.GetLastWriteTime(zipPath),
                        ArchiveZipPath = zipPath
                    };
                }
                else
                {
                    meta.ArchiveZipPath = zipPath;
                }
                list.Add(meta);
            }

            return list.OrderByDescending(x => x.FiscalYear).ToList();
        }

        public static YearEndArchiveMeta TryReadMetaFromZip(string zipPath)
        {
            try
            {
                using (var archive = ZipFile.OpenRead(zipPath))
                {
                    ZipArchiveEntry entry = archive.GetEntry("archive_meta.json");
                    if (entry == null)
                        return null;

                    using (var reader = new StreamReader(entry.Open(), Encoding.UTF8))
                    {
                        string json = reader.ReadToEnd();
                        return JsonConvert.DeserializeObject<YearEndArchiveMeta>(json);
                    }
                }
            }
            catch
            {
                return null;
            }
        }

        public static string ExtractArchiveToTemp(string zipPath)
        {
            string tempDir = Path.Combine(Path.GetTempPath(), $"lkb_archive_view_{DateTime.Now:yyyyMMddHHmmss}");
            Directory.CreateDirectory(tempDir);
            ZipFile.ExtractToDirectory(zipPath, tempDir);
            return tempDir;
        }

        public static string FindDbInExtractedDir(string extractedDir, YearEndArchiveMeta meta)
        {
            if (meta != null && !string.IsNullOrEmpty(meta.DbFileName))
            {
                string p = Path.Combine(extractedDir, meta.DbFileName);
                if (File.Exists(p))
                    return p;
            }

            string[] dbs = Directory.GetFiles(extractedDir, "*.db");
            return dbs.Length > 0 ? dbs[0] : null;
        }

        public static string BuildReadOnlyConnectionString(string dbPath)
        {
            return $"Data Source={dbPath};Version=3;Read Only=True;";
        }

        private static void CopyDirectory(string source, string dest)
        {
            Directory.CreateDirectory(dest);
            foreach (string file in Directory.GetFiles(source))
                File.Copy(file, Path.Combine(dest, Path.GetFileName(file)), true);
            foreach (string dir in Directory.GetDirectories(source))
                CopyDirectory(dir, Path.Combine(dest, Path.GetFileName(dir)));
        }
    }
}
