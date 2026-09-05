using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace lengkubao.desktop
{
    /// <summary>管理按年份隔离的数据库文件与活跃年份配置。</summary>
    public static class FiscalYearService
    {
        private const int MinYear = 2000;
        private const int MaxYear = 2100;
        private const string SqliteMagic = "SQLite format 3";

        private static int _activeYear;
        private static bool _initialized;

        public static event EventHandler YearChanged;

        public static bool IsInitialized => _initialized;

        public static int ActiveYear => _activeYear;

        public static string DataDirectory => SafeDataPaths.DataDirectory;

        public static string ConfigFilePath => SafeDataPaths.FiscalYearConfigPath;

        public static void Initialize()
        {
            if (_initialized)
                return;

            SafeDataPaths.EnsureUserDirectories();
            SafeDataPaths.MigrateFromInstallDirectoryIfNeeded();
            EnsureDirectoriesExist();
            MigrateLegacyDatabaseIfNeeded();
            LoadActiveYearFromConfig();

            string activePath = GetDbPathForYear(_activeYear);
            if (!IsValidSqliteFile(activePath))
                SafeDataPaths.TryRecoverMissingActiveDatabase(_activeYear, activePath);

            SafeDataPaths.TryProtectDataDirectory();

            _initialized = true;
            Console.WriteLine($">>> FiscalYearService 已初始化，活跃年份: {_activeYear}");
            Console.WriteLine($">>> 业务库目录: {DataDirectory}");
            Console.WriteLine($">>> 自动备份目录: {SafeDataPaths.SafeAutoBackupDirectory}");
        }

        public static string GetDbPathForYear(int year)
        {
            ValidateYear(year);
            return Path.Combine(DataDirectory, $"lengkubao_{year}.db");
        }

        public static string GetActiveDbPath()
        {
            if (!_initialized)
                Initialize();
            return GetDbPathForYear(_activeYear);
        }

        public static List<int> ListAvailableYears()
        {
            EnsureDirectoriesExist();
            var years = new HashSet<int>();

            if (Directory.Exists(DataDirectory))
            {
                foreach (string file in Directory.GetFiles(DataDirectory, "lengkubao_*.db"))
                {
                    if (!IsValidSqliteFile(file))
                        continue;

                    string name = Path.GetFileNameWithoutExtension(file);
                    if (name.StartsWith("lengkubao_", StringComparison.OrdinalIgnoreCase) &&
                        int.TryParse(name.Substring("lengkubao_".Length), out int year))
                    {
                        years.Add(year);
                    }
                }
            }

            if (_initialized || File.Exists(ConfigFilePath))
            {
                if (!_initialized)
                    LoadActiveYearFromConfig();

                string activePath = GetDbPathForYear(_activeYear);
                if (IsValidSqliteFile(activePath) || !File.Exists(activePath))
                    years.Add(_activeYear);
            }

            return years.OrderByDescending(y => y).ToList();
        }

        public static void SwitchToYear(int year)
        {
            ValidateYear(year);
            if (year == _activeYear)
                return;

            string dbPath = GetDbPathForYear(year);
            if (File.Exists(dbPath) && !IsValidSqliteFile(dbPath))
                throw new InvalidOperationException($"年份 {year} 的数据库文件已损坏，无法切换。");

            _activeYear = year;
            SaveActiveYearToConfig();
            YearChanged?.Invoke(null, EventArgs.Empty);
        }

        /// <summary>将当前活跃库复制到对应年份文件（同路径时仅 checkpoint 落盘）。</summary>
        public static void SaveCurrentYear(bool overwriteWithoutPrompt = false)
        {
            string source = Path.GetFullPath(GetActiveDbPath());
            string target = Path.GetFullPath(GetDbPathForYear(_activeYear));

            if (!File.Exists(source))
            {
                new DatabaseManager();
                source = Path.GetFullPath(GetActiveDbPath());
                if (!File.Exists(source))
                    throw new InvalidOperationException("当前年份数据库不存在，无法保存。");
            }

            if (!IsValidSqliteFile(source))
                throw new InvalidOperationException("当前年份数据库文件无效或已损坏，无法保存。");

            SafeCopySqliteDatabase(source, target, overwriteWithoutPrompt);
        }

        public static void SaveCurrentYearWithOverwrite()
        {
            SaveCurrentYear(true);
        }

        public static void CreateNewYear(int year)
        {
            ValidateYear(year);
            string targetPath = Path.GetFullPath(GetDbPathForYear(year));
            if (File.Exists(targetPath))
            {
                if (IsValidSqliteFile(targetPath))
                    throw new InvalidOperationException($"年份 {year} 的数据库已存在。");
                DeleteDatabaseSidecarFiles(targetPath);
            }

            int sourceYear = _activeYear;
            string sourcePath = ResolveSourceDatabasePath(sourceYear);

            SQLiteConnection.ClearAllPools();
            SaveCurrentYear(true);

            try
            {
                DatabaseManager.CreateYearDatabaseFromMaster(sourcePath, targetPath);
                SwitchToYear(year);
            }
            catch
            {
                DeleteDatabaseSidecarFiles(targetPath);
                throw;
            }
        }

        public static bool IsValidSqliteFile(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
                return false;

            try
            {
                var info = new FileInfo(path);
                if (info.Length < 16)
                    return false;

                using (var fs = File.OpenRead(path))
                {
                    byte[] header = new byte[16];
                    if (fs.Read(header, 0, 16) < 16)
                        return false;
                    return Encoding.UTF8.GetString(header, 0, 15) == SqliteMagic;
                }
            }
            catch
            {
                return false;
            }
        }

        internal static void CheckpointDatabase(string dbPath)
        {
            if (string.IsNullOrEmpty(dbPath) || !File.Exists(dbPath))
                return;

            using (var conn = new SQLiteConnection(BuildConnectionString(dbPath)))
            {
                conn.Open();
                using (var cmd = new SQLiteCommand("PRAGMA wal_checkpoint(FULL);", conn))
                    cmd.ExecuteNonQuery();
            }
        }

        internal static string BuildConnectionString(string dbPath)
        {
            return $"Data Source={dbPath};Version=3;";
        }

        private static string ResolveSourceDatabasePath(int sourceYear)
        {
            string path = Path.GetFullPath(GetDbPathForYear(sourceYear));
            if (!File.Exists(path))
            {
                new DatabaseManager();
                path = Path.GetFullPath(GetDbPathForYear(sourceYear));
            }

            if (IsValidSqliteFile(path))
                return path;

            string legacyPath = FindLegacyDatabasePath();
            if (legacyPath != null && IsValidSqliteFile(legacyPath))
            {
                Console.WriteLine($">>> 源年份库无效，回退使用旧库: {legacyPath}");
                return Path.GetFullPath(legacyPath);
            }

            throw new InvalidOperationException(
                $"源年份 {sourceYear} 的数据库无效或已损坏，无法新建年份。请先从备份恢复 lengkubao_{sourceYear}.db。");
        }

        private static void SafeCopySqliteDatabase(string source, string target, bool overwrite)
        {
            if (string.Equals(source, target, StringComparison.OrdinalIgnoreCase))
            {
                CheckpointDatabase(source);
                return;
            }

            if (File.Exists(target) && !overwrite)
                throw new IOException($"目标数据库已存在: {target}");

            SQLiteConnection.ClearAllPools();
            CheckpointDatabase(source);

            EnsureDirectoriesExist();
            DeleteDatabaseSidecarFiles(target);
            File.Copy(source, target, true);
        }

        internal static void DeleteDatabaseSidecarFiles(string dbPath)
        {
            if (string.IsNullOrEmpty(dbPath))
                return;

            TryDeleteFile(dbPath);
            TryDeleteFile(dbPath + "-wal");
            TryDeleteFile(dbPath + "-shm");
            TryDeleteFile(dbPath + "-journal");
        }

        private static void TryDeleteFile(string path)
        {
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch (Exception ex)
            {
                Console.WriteLine($">>> 删除文件失败 {path}: {ex.Message}");
            }
        }

        private static void EnsureDirectoriesExist()
        {
            if (!Directory.Exists(DataDirectory))
                Directory.CreateDirectory(DataDirectory);

            string configDir = Path.GetDirectoryName(ConfigFilePath);
            if (!string.IsNullOrEmpty(configDir) && !Directory.Exists(configDir))
                Directory.CreateDirectory(configDir);
        }

        private static void LoadActiveYearFromConfig()
        {
            if (File.Exists(ConfigFilePath))
            {
                foreach (string line in File.ReadAllLines(ConfigFilePath, Encoding.UTF8))
                {
                    string trimmed = line.Trim();
                    if (trimmed.StartsWith("ActiveYear=", StringComparison.OrdinalIgnoreCase))
                    {
                        string value = trimmed.Substring("ActiveYear=".Length).Trim();
                        if (int.TryParse(value, out int year) && year >= MinYear && year <= MaxYear)
                        {
                            _activeYear = year;
                            return;
                        }
                    }
                }
            }

            _activeYear = DateTime.Now.Year;
            SaveActiveYearToConfig();
        }

        private static void SaveActiveYearToConfig()
        {
            EnsureDirectoriesExist();
            File.WriteAllText(ConfigFilePath, $"ActiveYear={_activeYear}{Environment.NewLine}", Encoding.UTF8);
        }

        private static void MigrateLegacyDatabaseIfNeeded()
        {
            if (File.Exists(ConfigFilePath))
                return;

            if (Directory.Exists(DataDirectory))
            {
                bool hasYearDb = Directory.GetFiles(DataDirectory, "lengkubao_*.db")
                    .Any(IsValidSqliteFile);
                if (hasYearDb)
                {
                    _activeYear = DateTime.Now.Year;
                    SaveActiveYearToConfig();
                    return;
                }
            }

            string legacyPath = FindLegacyDatabasePath();
            if (legacyPath == null || !IsValidSqliteFile(legacyPath))
            {
                _activeYear = DateTime.Now.Year;
                SaveActiveYearToConfig();
                return;
            }

            int defaultYear = DateTime.Now.Year;
            int chosenYear = PromptMigrationYear(defaultYear);
            if (chosenYear < MinYear || chosenYear > MaxYear)
                chosenYear = defaultYear;

            string targetPath = GetDbPathForYear(chosenYear);
            EnsureDirectoriesExist();
            SafeCopySqliteDatabase(Path.GetFullPath(legacyPath), Path.GetFullPath(targetPath), true);
            _activeYear = chosenYear;
            SaveActiveYearToConfig();

            MessageBox.Show(
                $"已将现有数据迁移为 {chosenYear} 年账本。\n\n" +
                $"新文件：{targetPath}\n" +
                $"原文件保留：{legacyPath}",
                "年度数据迁移",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }

        private static int PromptMigrationYear(int defaultYear)
        {
            using (var form = new Form())
            {
                form.Text = "年度数据迁移";
                form.FormBorderStyle = FormBorderStyle.FixedDialog;
                form.StartPosition = FormStartPosition.CenterScreen;
                form.MaximizeBox = false;
                form.MinimizeBox = false;
                form.ClientSize = new System.Drawing.Size(360, 150);

                var lbl = new Label
                {
                    Text = "检测到旧版单一数据库。请指定该数据所属年份：",
                    Location = new System.Drawing.Point(12, 12),
                    Size = new System.Drawing.Size(336, 40)
                };

                var num = new NumericUpDown
                {
                    Minimum = MinYear,
                    Maximum = MaxYear,
                    Value = Math.Max(MinYear, Math.Min(MaxYear, defaultYear)),
                    Location = new System.Drawing.Point(12, 58),
                    Size = new System.Drawing.Size(120, 23)
                };

                var btnOk = new Button
                {
                    Text = "确定",
                    DialogResult = DialogResult.OK,
                    Location = new System.Drawing.Point(180, 100),
                    Size = new System.Drawing.Size(80, 28)
                };

                var btnCancel = new Button
                {
                    Text = "取消",
                    DialogResult = DialogResult.Cancel,
                    Location = new System.Drawing.Point(268, 100),
                    Size = new System.Drawing.Size(80, 28)
                };

                form.Controls.AddRange(new Control[] { lbl, num, btnOk, btnCancel });
                form.AcceptButton = btnOk;
                form.CancelButton = btnCancel;

                return form.ShowDialog() == DialogResult.OK ? (int)num.Value : defaultYear;
            }
        }

        private static string FindLegacyDatabasePath()
        {
            string[] possiblePaths =
            {
                Path.Combine(SafeDataPaths.DataDirectory, "lengkubao.db"),
                Path.Combine(Application.StartupPath, "data", "lengkubao.db"),
                Path.Combine(Application.StartupPath, "lengkubao.db"),
                Path.Combine(SafeDataPaths.UserDataRoot, "lengkubao.db")
            };

            foreach (string path in possiblePaths)
            {
                if (File.Exists(path))
                    return path;
            }

            return null;
        }

        private static void ValidateYear(int year)
        {
            if (year < MinYear || year > MaxYear)
                throw new ArgumentOutOfRangeException(nameof(year), $"年份须在 {MinYear}–{MaxYear} 之间。");
        }
    }
}
