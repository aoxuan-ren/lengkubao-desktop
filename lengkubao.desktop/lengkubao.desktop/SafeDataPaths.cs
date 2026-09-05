using System;
using System.IO;
using System.Linq;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Windows.Forms;

namespace lengkubao.desktop
{
    /// <summary>
    /// 用户数据区路径、安装目录迁移、防残留清理的自动备份与恢复。
    /// 日常读写主库在 LocalAppData；文档目录仅存放备份副本。
    /// </summary>
    internal static class SafeDataPaths
    {
        private const string AppFolderName = "LengKuBao";
        private const string AutoBackupFolderName = "冷库宝自动备份";
        private const int AutoBackupKeepCount = 14;

        public static string UserDataRoot =>
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                AppFolderName);

        public static string DataDirectory => Path.Combine(UserDataRoot, "data");

        public static string ConfigDirectory => Path.Combine(UserDataRoot, "config");

        public static string FiscalYearConfigPath =>
            Path.Combine(ConfigDirectory, "fiscal_year.ini");

        public static string InstallDataDirectory =>
            Path.Combine(Application.StartupPath, "data");

        public static string InstallFiscalYearConfigPath =>
            Path.Combine(Application.StartupPath, "config", "fiscal_year.ini");

        public static string SafeAutoBackupDirectory =>
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                AutoBackupFolderName);

        /// <summary>
        /// 若用户数据区尚无年库，则从安装目录 data 复制 lengkubao_*.db 与财年配置。
        /// </summary>
        public static void MigrateFromInstallDirectoryIfNeeded()
        {
            try
            {
                EnsureUserDirectories();

                bool userHasYearDb = Directory.Exists(DataDirectory)
                    && Directory.GetFiles(DataDirectory, "lengkubao_*.db")
                        .Any(FiscalYearService.IsValidSqliteFile);

                if (!userHasYearDb && Directory.Exists(InstallDataDirectory))
                {
                    foreach (string source in Directory.GetFiles(InstallDataDirectory, "lengkubao_*.db"))
                    {
                        if (!FiscalYearService.IsValidSqliteFile(source))
                            continue;

                        string dest = Path.Combine(DataDirectory, Path.GetFileName(source));
                        if (File.Exists(dest) && FiscalYearService.IsValidSqliteFile(dest))
                            continue;

                        Console.WriteLine($">>> 迁移年库: {source} -> {dest}");
                        CopyDatabaseWithSidecars(source, dest);
                    }
                }

                if (!File.Exists(FiscalYearConfigPath) && File.Exists(InstallFiscalYearConfigPath))
                {
                    File.Copy(InstallFiscalYearConfigPath, FiscalYearConfigPath, true);
                    Console.WriteLine($">>> 迁移财年配置: {InstallFiscalYearConfigPath} -> {FiscalYearConfigPath}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($">>> 从安装目录迁移数据失败: {ex.Message}");
            }
        }

        public static void EnsureUserDirectories()
        {
            if (!Directory.Exists(DataDirectory))
                Directory.CreateDirectory(DataDirectory);
            if (!Directory.Exists(ConfigDirectory))
                Directory.CreateDirectory(ConfigDirectory);
        }

        /// <summary>
        /// 为数据目录设置当前用户 FullControl（不设 Deny，避免影响 SQLite WAL）。
        /// 主要防护来自迁出安装目录；本方法确保目录 ACL 明确归属当前用户。
        /// </summary>
        public static void TryProtectDataDirectory()
        {
            try
            {
                EnsureUserDirectories();
                var dirInfo = new DirectoryInfo(DataDirectory);
                DirectorySecurity security = dirInfo.GetAccessControl();
                var sid = WindowsIdentity.GetCurrent().User;
                if (sid == null)
                    return;

                var rule = new FileSystemAccessRule(
                    sid,
                    FileSystemRights.FullControl,
                    InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                    PropagationFlags.None,
                    AccessControlType.Allow);

                security.ModifyAccessRule(AccessControlModification.Add, rule, out bool modified);
                if (modified)
                {
                    dirInfo.SetAccessControl(security);
                    Console.WriteLine($">>> 已为数据目录设置当前用户 FullControl: {DataDirectory}");
                }

                // 写入说明文件，提醒勿被清理软件删除
                string readme = Path.Combine(DataDirectory, "请勿删除-业务数据库.txt");
                if (!File.Exists(readme))
                {
                    File.WriteAllText(readme,
                        "本目录存放冷库宝业务数据库，请勿用「清理残留」等工具删除。\r\n" +
                        "自动备份位置：文档\\" + AutoBackupFolderName + "\r\n");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($">>> 设置数据目录保护失败（可忽略）: {ex.Message}");
            }
        }

        public static string BuildDailyBackupFileName(int year, DateTime day)
        {
            return $"lengkubao_{year}_{day:yyyyMMdd}.db";
        }

        /// <summary>静默每日备份；同一天已存在则跳过。返回备份路径或 null。</summary>
        public static string TryCreateDailyAutoBackup(string activeDbPath, int year)
        {
            try
            {
                if (string.IsNullOrEmpty(activeDbPath) || !FiscalYearService.IsValidSqliteFile(activeDbPath))
                    return null;

                if (!Directory.Exists(SafeAutoBackupDirectory))
                    Directory.CreateDirectory(SafeAutoBackupDirectory);

                string dest = Path.Combine(
                    SafeAutoBackupDirectory,
                    BuildDailyBackupFileName(year, DateTime.Now));

                if (File.Exists(dest) && new FileInfo(dest).Length > 0)
                    return dest;

                FiscalYearService.CheckpointDatabase(activeDbPath);
                File.Copy(activeDbPath, dest, true);
                TrimOldAutoBackups();
                Console.WriteLine($">>> 每日自动备份完成: {dest}");
                return dest;
            }
            catch (Exception ex)
            {
                Console.WriteLine($">>> 每日自动备份失败: {ex.Message}");
                return null;
            }
        }

        public static void TrimOldAutoBackups()
        {
            try
            {
                if (!Directory.Exists(SafeAutoBackupDirectory))
                    return;

                var files = new DirectoryInfo(SafeAutoBackupDirectory)
                    .GetFiles("lengkubao_*.db")
                    .OrderByDescending(f => f.LastWriteTimeUtc)
                    .ToList();

                for (int i = AutoBackupKeepCount; i < files.Count; i++)
                {
                    try { files[i].Delete(); }
                    catch { }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($">>> 清理旧自动备份失败: {ex.Message}");
            }
        }

        public static string FindLatestAutoBackupForYear(int year)
        {
            try
            {
                if (!Directory.Exists(SafeAutoBackupDirectory))
                    return null;

                string prefix = $"lengkubao_{year}_";
                return new DirectoryInfo(SafeAutoBackupDirectory)
                    .GetFiles("lengkubao_*.db")
                    .Where(f => f.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                                && FiscalYearService.IsValidSqliteFile(f.FullName))
                    .OrderByDescending(f => f.LastWriteTimeUtc)
                    .Select(f => f.FullName)
                    .FirstOrDefault();
            }
            catch
            {
                return null;
            }
        }

        public static string FindInstallDirectoryBackupForYear(int year)
        {
            string path = Path.Combine(InstallDataDirectory, $"lengkubao_{year}.db");
            return FiscalYearService.IsValidSqliteFile(path) ? path : null;
        }

        /// <summary>
        /// 活动年主库缺失或损坏时，提示从自动备份或安装目录残留恢复。
        /// </summary>
        public static bool TryRecoverMissingActiveDatabase(int year, string targetDbPath)
        {
            if (FiscalYearService.IsValidSqliteFile(targetDbPath))
                return false;

            string autoBackup = FindLatestAutoBackupForYear(year);
            string installCopy = FindInstallDirectoryBackupForYear(year);
            string source = autoBackup ?? installCopy;
            if (source == null)
            {
                MessageBox.Show(
                    "检测到业务数据库丢失或已损坏（可能被系统「清理残留」删除）。\n\n" +
                    $"期望路径：\n{targetDbPath}\n\n" +
                    "未找到可用自动备份。请使用「数据备份 → 恢复」选择备份文件。\n" +
                    $"自动备份目录：\n{SafeAutoBackupDirectory}\n\n" +
                    "请勿用清理软件删除「文档\\冷库宝自动备份」及本程序用户数据目录。",
                    "数据库丢失",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return false;
            }

            string sourceLabel = autoBackup != null ? "文档自动备份" : "安装目录残留副本";
            DialogResult result = MessageBox.Show(
                "检测到业务数据库丢失或已损坏（可能被系统「清理残留」删除）。\n\n" +
                $"是否从{sourceLabel}恢复？\n\n" +
                $"备份：\n{source}\n\n" +
                $"将恢复到：\n{targetDbPath}",
                "恢复数据库",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);

            if (result != DialogResult.Yes)
                return false;

            try
            {
                EnsureUserDirectories();
                CopyDatabaseWithSidecars(source, targetDbPath);
                MessageBox.Show("数据库已恢复，程序将继续启动。", "恢复成功",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"恢复失败：{ex.Message}", "错误",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
        }

        public static void CopyDatabaseWithSidecars(string sourceDb, string destDb)
        {
            string destDir = Path.GetDirectoryName(destDb);
            if (!string.IsNullOrEmpty(destDir) && !Directory.Exists(destDir))
                Directory.CreateDirectory(destDir);

            FiscalYearService.DeleteDatabaseSidecarFiles(destDb);
            File.Copy(sourceDb, destDb, true);

            foreach (string suffix in new[] { "-wal", "-shm", "-journal" })
            {
                string side = sourceDb + suffix;
                if (File.Exists(side))
                    File.Copy(side, destDb + suffix, true);
            }
        }

        public static void OpenSafeAutoBackupFolder()
        {
            if (!Directory.Exists(SafeAutoBackupDirectory))
                Directory.CreateDirectory(SafeAutoBackupDirectory);
            System.Diagnostics.Process.Start("explorer.exe", SafeAutoBackupDirectory);
        }
    }
}
