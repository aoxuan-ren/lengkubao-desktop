using System;
using System.Data;
using System.Data.SQLite;
using System.IO;
using System.Windows.Forms;

namespace lengkubao.desktop
{
    public static class DatabaseMigrationHelper
    {
        public static void CheckAndAddSettleFields()
        {
            try
            {
                string dbPath = ResolveDatabasePath();
                if (string.IsNullOrEmpty(dbPath))
                    return;
                string connStr = $"Data Source={dbPath};Version=3;";

                using (var conn = new SQLiteConnection(connStr))
                {
                    conn.Open();

                    // 检查 is_settled 字段是否存在
                    bool fieldExists = CheckFieldExists(conn, "packaging_transactions", "is_settled");

                    if (!fieldExists)
                    {
                        // 添加结清相关字段
                        AddSettleFields(conn);
                        MessageBox.Show("已成功添加结清字段到包装记录表", "数据库升级",
                            MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"数据库迁移检查失败: {ex.Message}");
                // 不显示错误，避免影响正常使用
            }
        }

        private static bool CheckFieldExists(SQLiteConnection conn, string tableName, string fieldName)
        {
            string sql = $"PRAGMA table_info({tableName})";
            using (var cmd = new SQLiteCommand(sql, conn))
            using (var reader = cmd.ExecuteReader())
            {
                while (reader.Read())
                {
                    if (reader["name"].ToString().Equals(fieldName, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        private static void AddSettleFields(SQLiteConnection conn)
        {
            // 添加结清标志字段
            string sql1 = "ALTER TABLE packaging_transactions ADD COLUMN is_settled INTEGER DEFAULT 0";
            ExecuteNonQuery(conn, sql1);

            // 添加结清时间字段
            string sql2 = "ALTER TABLE packaging_transactions ADD COLUMN settled_time DATETIME";
            ExecuteNonQuery(conn, sql2);

            // 添加结清人字段
            string sql3 = "ALTER TABLE packaging_transactions ADD COLUMN settled_by TEXT";
            ExecuteNonQuery(conn, sql3);

            Console.WriteLine("已成功添加结清字段");
        }
        // 在 DatabaseMigrationHelper.cs 中添加
        public static void UpdateExistingRecords()
        {
            try
            {
                string dbPath = ResolveDatabasePath();
                if (string.IsNullOrEmpty(dbPath))
                    return;
                string connStr = $"Data Source={dbPath};Version=3;";

                using (var conn = new SQLiteConnection(connStr))
                {
                    conn.Open();

                    // 将所有 is_settled 为 NULL 的记录更新为 0
                    string sql = "UPDATE packaging_transactions SET is_settled = 0 WHERE is_settled IS NULL";

                    using (var cmd = new SQLiteCommand(sql, conn))
                    {
                        int updated = cmd.ExecuteNonQuery();
                        Console.WriteLine($"更新了 {updated} 条记录的 is_settled 字段");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"更新记录失败: {ex.Message}");
            }
        }
        private static void ExecuteNonQuery(SQLiteConnection conn, string sql)
        {
            using (var cmd = new SQLiteCommand(sql, conn))
            {
                cmd.ExecuteNonQuery();
            }
        }

        /// <summary>确保年度终结相关表存在。</summary>
        public static void EnsureYearEndTables()
        {
            try
            {
                string dbPath = ResolveDatabasePath();
                if (string.IsNullOrEmpty(dbPath))
                    return;

                string connStr = $"Data Source={dbPath};Version=3;";
                using (var conn = new SQLiteConnection(connStr))
                {
                    conn.Open();
                    if (!TableExists(conn, "year_end_settlement"))
                    {
                        ExecuteNonQuery(conn, @"
CREATE TABLE year_end_settlement (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    fiscal_year INTEGER NOT NULL UNIQUE,
    archive_path TEXT NOT NULL,
    settled_at DATETIME DEFAULT CURRENT_TIMESTAMP,
    settled_by TEXT,
    inventory_carryover TEXT,
    opening_balance_total REAL DEFAULT 0,
    opening_inventory_rows INTEGER DEFAULT 0,
    remark TEXT
)");
                    }

                    if (!TableExists(conn, "client_opening_balance"))
                    {
                        ExecuteNonQuery(conn, @"
CREATE TABLE client_opening_balance (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    client_code TEXT NOT NULL,
    client_name TEXT,
    payable_amount REAL NOT NULL,
    source_year INTEGER NOT NULL,
    effective_date TEXT NOT NULL,
    remark TEXT,
    created_time DATETIME DEFAULT CURRENT_TIMESTAMP
)");
                        ExecuteNonQuery(conn, "CREATE INDEX IF NOT EXISTS idx_opening_balance_client ON client_opening_balance(client_code)");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"年度终结表迁移失败: {ex.Message}");
            }
        }

        private static string ResolveDatabasePath()
        {
            if (FiscalYearService.IsInitialized)
            {
                string active = FiscalYearService.GetActiveDbPath();
                if (File.Exists(active))
                    return active;
                return active;
            }

            string[] paths =
            {
                Path.Combine(SafeDataPaths.DataDirectory, $"lengkubao_{DateTime.Now.Year}.db"),
                Path.Combine(SafeDataPaths.DataDirectory, "lengkubao.db"),
                Path.Combine(Application.StartupPath, "data", "lengkubao.db"),
                Path.Combine(Application.StartupPath, "lengkubao.db")
            };
            foreach (string p in paths)
            {
                if (File.Exists(p))
                    return p;
            }
            return paths[0];
        }

        private static bool TableExists(SQLiteConnection conn, string tableName)
        {
            using (var cmd = new SQLiteCommand(
                "SELECT 1 FROM sqlite_master WHERE type='table' AND name=@n LIMIT 1", conn))
            {
                cmd.Parameters.AddWithValue("@n", tableName);
                return cmd.ExecuteScalar() != null;
            }
        }
    }
}