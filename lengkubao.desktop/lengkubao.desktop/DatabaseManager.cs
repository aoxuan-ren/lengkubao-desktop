using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Data.SQLite;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using Newtonsoft.Json.Linq;

namespace lengkubao.desktop
{
    /// <summary>PC 本地修改基础资料时，供 AutoSyncServer 写入增量变更日志。</summary>
    public class LocalConfigDeltaEventArgs : EventArgs
    {
        public string EntityType { get; set; }
        public string EntityKey { get; set; }
        public string OpType { get; set; }
        public JObject Payload { get; set; }
    }

    public class BatchSalesResult
    {
        public bool Success { get; set; }
        public string ErrorMessage { get; set; }
        public int SuccessCount { get; set; }
        public int TotalQuantity { get; set; }
        public decimal TotalAmount { get; set; }
    }

    public class BatchSaleAllocationInfo
    {
        public DataTable Allocations { get; set; } = new DataTable();
        public int OrphanSoldQty { get; set; }
    }

    public enum PackagingSaveOutcome
    {
        SavedNew,
        IdempotentMatch,
        BusinessKeyDuplicate,
        ContentMismatch,
        OtherError
    }

    public enum BuyerDeleteResult
    {
        Failed,
        Deleted,
        Disabled
    }

    public class DatabaseManager
    {
        private string connectionString;
        private readonly string _explicitDbPath;

        private static readonly string[] MasterDataTables =
        {
            "clients", "product_types", "pack_types", "locations", "handlers"
        };

        public const string RefrigerationFeeReason = "制冷费";
        private const string LegacyStorageFeeReason = "库费";
        private const string MigrationRefrigerationFeeUnifyV1 = "refrigeration_fee_unify_v1";

        public static bool IsRefrigerationFeeReason(string reason)
        {
            if (string.IsNullOrWhiteSpace(reason)) return false;
            string trimmed = reason.Trim();
            return trimmed == RefrigerationFeeReason || trimmed == LegacyStorageFeeReason;
        }

        /// <summary>仅在 PC 界面或本机逻辑成功写入数据库后触发。</summary>
        public static event EventHandler<LocalConfigDeltaEventArgs> LocalConfigChangedForSync;

        /// <summary>配置数据已写入数据库（PC 本地或手持同步），供已打开的设置界面等刷新。</summary>
        public static event EventHandler<LocalConfigDeltaEventArgs> ConfigDataChanged;

        /// <summary>手持端同步写入数据库时置为 true，避免再次记入 PC 本地增量日志。</summary>
        public static bool SuppressLocalConfigSyncNotifications { get; set; }

        public static void RaiseConfigDataChanged(string entityType, string entityKey = "", string opType = "UPSERT")
        {
            try
            {
                ConfigDataChanged?.Invoke(null, new LocalConfigDeltaEventArgs
                {
                    EntityType = entityType ?? "",
                    EntityKey = entityKey ?? "",
                    OpType = opType ?? "UPSERT",
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($">>> RaiseConfigDataChanged 异常: {ex.Message}");
            }
        }

        private static void NotifyLocalConfigDelta(string entityType, string entityKey, string opType, JObject payload)
        {
            if (SuppressLocalConfigSyncNotifications) return;
            try
            {
                var args = new LocalConfigDeltaEventArgs
                {
                    EntityType = entityType,
                    EntityKey = entityKey ?? "",
                    OpType = opType ?? "UPSERT",
                    Payload = payload ?? new JObject()
                };
                LocalConfigChangedForSync?.Invoke(null, args);
                ConfigDataChanged?.Invoke(null, args);
            }
            catch (Exception ex)
            {
                Console.WriteLine($">>> NotifyLocalConfigDelta 异常: {ex.Message}");
            }
        }

        public DatabaseManager()
        {
            try
            {
                // 1. 获取正确的数据库路径
                connectionString = GetConnectionString();

                // 2. 测试连接
                TestConnection();

                // 3. 初始化/更新数据库结构
                InitializeDatabase();

                Console.WriteLine($">>> DatabaseManager 初始化成功");
            }
            catch (Exception ex)
            {
                Console.WriteLine($">>> DatabaseManager 初始化失败: {ex.Message}");
                // 使用降级方案
                connectionString = "Data Source=lengkubao.db;Version=3;";
            }
        }

        public DatabaseManager(string explicitDbPath, bool skipSeedData = false)
        {
            _explicitDbPath = explicitDbPath ?? throw new ArgumentNullException(nameof(explicitDbPath));
            try
            {
                connectionString = BuildConnectionString(_explicitDbPath);
                TestConnection();
                InitializeDatabase(skipSeedData);
                Console.WriteLine($">>> DatabaseManager 初始化成功（指定路径）: {_explicitDbPath}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($">>> DatabaseManager 指定路径初始化失败: {ex.Message}");
                throw;
            }
        }

        private static string BuildConnectionString(string dbPath)
        {
            string directory = Path.GetDirectoryName(dbPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                Directory.CreateDirectory(directory);
            return $"Data Source={dbPath};Version=3;";
        }

        /// <summary>从源库复制基础资料到目标库，目标库业务表为空。</summary>
        public static void CreateYearDatabaseFromMaster(string sourceDbPath, string targetDbPath)
        {
            sourceDbPath = Path.GetFullPath(sourceDbPath ?? "");
            targetDbPath = Path.GetFullPath(targetDbPath ?? "");

            if (!File.Exists(sourceDbPath))
                throw new FileNotFoundException("源年份数据库不存在。", sourceDbPath);
            if (!FiscalYearService.IsValidSqliteFile(sourceDbPath))
                throw new InvalidOperationException($"源数据库不是有效的 SQLite 文件: {sourceDbPath}");
            if (File.Exists(targetDbPath))
                throw new InvalidOperationException("目标年份数据库已存在。");

            SQLiteConnection.ClearAllPools();
            FiscalYearService.CheckpointDatabase(sourceDbPath);

            try
            {
                new DatabaseManager(targetDbPath, skipSeedData: true);

                string attachPath = sourceDbPath.Replace("'", "''");
                using (var connection = new SQLiteConnection(BuildConnectionString(targetDbPath)))
                {
                    connection.Open();
                    using (var attachCmd = new SQLiteCommand($"ATTACH DATABASE '{attachPath}' AS srcdb", connection))
                        attachCmd.ExecuteNonQuery();

                    using (var tx = connection.BeginTransaction())
                    {
                        foreach (string table in MasterDataTables)
                        {
                            using (var countCmd = new SQLiteCommand(
                                $"SELECT COUNT(*) FROM srcdb.sqlite_master WHERE type='table' AND name=@name", connection, tx))
                            {
                                countCmd.Parameters.AddWithValue("@name", table);
                                object count = countCmd.ExecuteScalar();
                                if (Convert.ToInt32(count) == 0)
                                    continue;
                            }

                            using (var insertCmd = new SQLiteCommand(
                                $"INSERT INTO main.{table} SELECT * FROM srcdb.{table}", connection, tx))
                            {
                                insertCmd.ExecuteNonQuery();
                            }
                        }
                        tx.Commit();
                    }

                    using (var detachCmd = new SQLiteCommand("DETACH DATABASE srcdb", connection))
                        detachCmd.ExecuteNonQuery();
                }
            }
            catch
            {
                FiscalYearService.DeleteDatabaseSidecarFiles(targetDbPath);
                throw;
            }
        }
        public void CloseAllConnections()
        {
            // SQLite 连接是自动管理的，这个方法主要是为了接口兼容
            // 如果有连接池或长时间连接，可以在这里关闭
            Console.WriteLine("数据库连接已关闭");
        }
        public object ExecuteScalar(string sql, Dictionary<string, object> parameters = null)
        {
            try
            {
                using (SQLiteConnection conn = new SQLiteConnection(ConnectionString))
                using (SQLiteCommand cmd = new SQLiteCommand(sql, conn))
                {
                    if (parameters != null)
                    {
                        foreach (var param in parameters)
                        {
                            cmd.Parameters.AddWithValue(param.Key, param.Value ?? DBNull.Value);
                        }
                    }
                    conn.Open();
                    return cmd.ExecuteScalar();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"执行Scalar查询失败: {ex.Message}");
                return null;
            }
        }

        // 重载版本，不需要参数
        public object ExecuteScalar(string sql)
        {
            return ExecuteScalar(sql, null);
        }


        // 连接测试方法
        public bool TestConnection()
        {
            try
            {
                using (var connection = new SQLiteConnection(connectionString))
                {
                    connection.Open();
                    Console.WriteLine("数据库连接测试成功");
                    return true;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"数据库连接测试失败: {ex.Message}");
                return false;
            }
        }

        public void FixClientsTable()
        {
            Console.WriteLine("=== 修复 clients 表 ===");

            try
            {
                using (SQLiteConnection conn = new SQLiteConnection(ConnectionString))
                {
                    conn.Open();

                    // 查看表结构
                    Console.WriteLine(">>> clients 表当前结构:");
                    DataTable schema = conn.GetSchema("Columns", new string[] { null, null, "clients", null });
                    foreach (DataRow row in schema.Rows)
                    {
                        Console.WriteLine($"  - {row["COLUMN_NAME"]} ({row["DATA_TYPE"]})");
                    }

                    // 检查并添加缺少的字段（不添加created_和updated_at，因为SQLite有限制）
                    string[] columnsToCheck = new string[]
                    {
                "balance",
                "contact",
                "status",
                "customer_type"
                    };

                    foreach (string column in columnsToCheck)
                    {
                        if (!ColumnExists(conn, "clients", column))
                        {
                            Console.WriteLine($">>> 添加字段: {column}");

                            string dataType = "TEXT";
                            if (column == "balance") dataType = "DECIMAL(10,2)";
                            if (column == "status") dataType = "INTEGER DEFAULT 1";
                            if (column == "customer_type") dataType = "TEXT DEFAULT 'SELLER'";

                            string sql = $"ALTER TABLE clients ADD COLUMN {column} {dataType}";
                            using (SQLiteCommand cmd = new SQLiteCommand(sql, conn))
                            {
                                cmd.ExecuteNonQuery();
                                Console.WriteLine($">>> 字段 {column} 添加成功");
                            }
                        }
                        else
                        {
                            Console.WriteLine($">>> 字段已存在: {column}");
                        }
                    }

                    ExecuteNonQuery(@"
UPDATE clients
SET customer_type = 'BUYER'
WHERE (customer_type IS NULL OR customer_type = '' OR customer_type = 'SELLER')
  AND UPPER(code) LIKE 'MJ%'");
                    ExecuteNonQuery(@"
UPDATE clients
SET customer_type = 'SELLER'
WHERE customer_type IS NULL OR customer_type = ''");
                }

                Console.WriteLine(">>> clients 表修复完成");
            }
            catch (Exception ex)
            {
                Console.WriteLine($">>> clients 表修复失败: {ex.Message}");
            }
        }
        public void FixInboundTransactionsTable()
        {
            Console.WriteLine("=== 修复 inbound_transactions 表 ===");

            try
            {
                using (SQLiteConnection conn = new SQLiteConnection(ConnectionString))
                {
                    conn.Open();

                    // 查看表结构
                    Console.WriteLine(">>> inbound_transactions 表当前结构:");
                    DataTable schema = conn.GetSchema("Columns", new string[] { null, null, "inbound_transactions", null });
                    foreach (DataRow row in schema.Rows)
                    {
                        Console.WriteLine($"  - {row["COLUMN_NAME"]} ({row["DATA_TYPE"]})");
                    }

                    // 检查是否有date字段
                    if (!ColumnExists(conn, "inbound_transactions", "date"))
                    {
                        Console.WriteLine(">>> 添加date字段到inbound_transactions表");
                        string sql = "ALTER TABLE inbound_transactions ADD COLUMN date TEXT";
                        ExecuteNonQuery(sql);
                    }

                    // 检查是否有client_code字段
                    if (!ColumnExists(conn, "inbound_transactions", "client_code"))
                    {
                        Console.WriteLine(">>> 添加client_code字段到inbound_transactions表");
                        string sql = "ALTER TABLE inbound_transactions ADD COLUMN client_code TEXT";
                        ExecuteNonQuery(sql);
                    }

                    Console.WriteLine(">>> inbound_transactions 表修复完成");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($">>> inbound_transactions 表修复失败: {ex.Message}");
            }
        }

        public bool AddClient(string code, string name, string contact, string phone, string address)
        {
            try
            {
                using (var connection = new SQLiteConnection(connectionString))
                {
                    connection.Open();

                    var command = new SQLiteCommand(@"
                INSERT INTO clients (code, name, contact, phone, address, status, customer_type) 
                VALUES (@code, @name, @contact, @phone, @address, 1, 'SELLER')", connection);

                    command.Parameters.AddWithValue("@code", code);
                    command.Parameters.AddWithValue("@name", name);
                    command.Parameters.AddWithValue("@contact", contact);
                    command.Parameters.AddWithValue("@phone", phone);
                    command.Parameters.AddWithValue("@address", address);

                    int result = command.ExecuteNonQuery();
                    if (result > 0)
                    {
                        NotifyLocalConfigDelta(
                            "CUSTOMER",
                            code,
                            "UPSERT",
                            BuildCustomerSyncPayload(code, name, phone, ClientTypeSeller, true));
                    }
                    return result > 0;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"添加客户失败: {ex.Message}");
                return false;
            }
        }
        // 在DatabaseManager类中添加这个方法（放在合适的位置，比如其他CreateEmpty...Table方法附近）

        private DataTable CreateEmptyDashboardTable()
        {
            DataTable table = new DataTable();
            table.Columns.Add("统计项", typeof(string));
            table.Columns.Add("金额", typeof(decimal));
            table.Columns.Add("笔数", typeof(int));

            // 添加几行默认数据
            string[] items = { "今日入库", "今日销售", "本月入库", "本月销售", "累计客户" };
            foreach (string item in items)
            {
                DataRow row = table.NewRow();
                row["统计项"] = item;
                row["金额"] = 0;
                row["笔数"] = 0;
                table.Rows.Add(row);
            }

            return table;
        }

        // 如果需要，也可以添加其他CreateEmpty...Table方法
        private DataTable CreateEmptyClientSummaryTable()
        {
            DataTable table = new DataTable();
            table.Columns.Add("客户编号", typeof(string));
            table.Columns.Add("客户名称", typeof(string));
            table.Columns.Add("业务笔数", typeof(int));
            table.Columns.Add("销售数量", typeof(int));
            table.Columns.Add("销售金额", typeof(decimal));
            table.Columns.Add("入库数量", typeof(int));
            table.Columns.Add("入库金额", typeof(decimal));
            table.Columns.Add("当前余额", typeof(decimal));

            // 添加一行提示数据
            DataRow row = table.NewRow();
            row["客户名称"] = "暂无客户统计数据";
            table.Rows.Add(row);

            return table;
        }
        // 初始化客户表（如果没有的话）
        public void InitializeClientsTable()
        {
            try
            {
                string sql = @"
            CREATE TABLE IF NOT EXISTS clients (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                code TEXT UNIQUE NOT NULL,      -- 客户编号，唯一
                name TEXT NOT NULL,            -- 客户名称
                phone TEXT,                    -- 电话
                address TEXT,                  -- 地址
                balance DECIMAL(10,2) DEFAULT 0, -- 余额
                created_time DATETIME DEFAULT CURRENT_TIMESTAMP,
                updated_at DATETIME DEFAULT CURRENT_TIMESTAMP
            )";

                ExecuteQuery(sql);

                // 创建索引
                ExecuteQuery("CREATE INDEX IF NOT EXISTS idx_clients_code ON clients(code)");
                ExecuteQuery("CREATE INDEX IF NOT EXISTS idx_clients_name ON clients(name)");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"初始化客户表失败: {ex.Message}");
            }
        }
        // 生成客户编号（KH001-KH999格式）
        public string GenerateClientCode()
        {
            try
            {
                // 查找当前最大编号（以KH开头的编号）
                string sql = "SELECT MAX(code) as max_code FROM clients WHERE code LIKE 'KH%' AND LENGTH(code) = 5";
                DataTable dt = ExecuteQuery(sql);

                if (dt.Rows.Count > 0 && dt.Rows[0]["max_code"] != DBNull.Value)
                {
                    string maxCode = dt.Rows[0]["max_code"].ToString();

                    // 提取数字部分
                    if (maxCode.StartsWith("KH") && maxCode.Length == 5)
                    {
                        string numberPart = maxCode.Substring(2); // 提取后3位数字
                        if (int.TryParse(numberPart, out int maxNumber) && maxNumber < 999)
                        {
                            int nextNumber = maxNumber + 1;
                            return $"KH{nextNumber:D3}"; // 格式化为3位数字，不足补0
                        }
                    }
                }

                // 如果没有找到有效的KH开头的编号，从KH001开始
                return "KH001";
            }
            catch (Exception ex)
            {
                Console.WriteLine($"生成客户编号失败: {ex.Message}");
                return "KH001";
            }
        }

        public const string ClientTypeSeller = "SELLER";
        public const string ClientTypeBuyer = "BUYER";

        public static string ResolveClientTypeFromCode(string code)
        {
            if (!string.IsNullOrWhiteSpace(code) &&
                code.StartsWith("MJ", StringComparison.OrdinalIgnoreCase))
            {
                return ClientTypeBuyer;
            }
            return ClientTypeSeller;
        }

        private static JObject BuildCustomerSyncPayload(string code, string name, string phone, string customerType, bool enabled)
        {
            return new JObject
            {
                ["code"] = code ?? "",
                ["name"] = name ?? "",
                ["phone"] = phone ?? "",
                ["customer_type"] = string.IsNullOrWhiteSpace(customerType) ? ClientTypeSeller : customerType,
                ["enabled"] = enabled
            };
        }

        private void NotifyCustomerSyncRow(string code)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(code))
                    return;

                FixClientsTable();
                string sql = @"
SELECT code, name, phone, status,
       COALESCE(customer_type, CASE WHEN UPPER(code) LIKE 'MJ%' THEN 'BUYER' ELSE 'SELLER' END) AS customer_type
FROM clients
WHERE code = @code";
                DataTable dt = ExecuteQuery(sql, new Dictionary<string, object> { { "@code", code } });
                if (dt.Rows.Count == 0)
                    return;

                DataRow row = dt.Rows[0];
                string name = row["name"]?.ToString() ?? "";
                string phone = row["phone"]?.ToString() ?? "";
                string customerType = row["customer_type"]?.ToString() ?? ResolveClientTypeFromCode(code);
                int status = row["status"] != DBNull.Value ? Convert.ToInt32(row["status"]) : 1;
                NotifyLocalConfigDelta(
                    "CUSTOMER",
                    code,
                    "UPSERT",
                    BuildCustomerSyncPayload(code, name, phone, customerType, status != 0));
            }
            catch (Exception ex)
            {
                Console.WriteLine($">>> NotifyCustomerSyncRow 异常: {ex.Message}");
            }
        }

        public bool DisableClientByCode(string clientCode)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(clientCode))
                    return false;

                int rowsAffected = ExecuteNonQuery(
                    "UPDATE clients SET status = 0, updated_at = datetime('now') WHERE code = @code",
                    new Dictionary<string, object> { { "@code", clientCode } });

                if (rowsAffected > 0)
                    NotifyCustomerSyncRow(clientCode);

                return rowsAffected > 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"禁用客户失败: {ex.Message}");
                return false;
            }
        }

        public string GenerateBuyerCode()
        {
            try
            {
                string sql = "SELECT MAX(code) as max_code FROM clients WHERE UPPER(code) LIKE 'MJ%' AND LENGTH(code) = 5";
                DataTable dt = ExecuteQuery(sql);

                if (dt.Rows.Count > 0 && dt.Rows[0]["max_code"] != DBNull.Value)
                {
                    string maxCode = dt.Rows[0]["max_code"].ToString();
                    if (maxCode.StartsWith("MJ", StringComparison.OrdinalIgnoreCase) && maxCode.Length == 5)
                    {
                        string numberPart = maxCode.Substring(2);
                        if (int.TryParse(numberPart, out int maxNumber) && maxNumber < 999)
                        {
                            return $"MJ{maxNumber + 1:D3}";
                        }
                    }
                }

                return "MJ001";
            }
            catch (Exception ex)
            {
                Console.WriteLine($"生成买家编号失败: {ex.Message}");
                return "MJ001";
            }
        }

        public DataTable GetAllBuyersWithDetails()
        {
            try
            {
                FixClientsTable();
                string sql = @"
SELECT code, name, phone, status
FROM clients
WHERE COALESCE(customer_type, CASE WHEN UPPER(code) LIKE 'MJ%' THEN 'BUYER' ELSE 'SELLER' END) = 'BUYER'
ORDER BY status DESC, code ASC";
                return ExecuteQuery(sql);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"获取买家列表失败: {ex.Message}");
                return new DataTable();
            }
        }

        public bool AddBuyer(string name, string phone = "")
        {
            try
            {
                if (string.IsNullOrWhiteSpace(name))
                    return false;

                string code = GenerateBuyerCode();
                using (var connection = new SQLiteConnection(connectionString))
                {
                    connection.Open();
                    var command = new SQLiteCommand(@"
INSERT INTO clients (code, name, contact, phone, address, status, customer_type)
VALUES (@code, @name, '', @phone, '', 1, 'BUYER')", connection);
                    command.Parameters.AddWithValue("@code", code);
                    command.Parameters.AddWithValue("@name", name.Trim());
                    command.Parameters.AddWithValue("@phone", phone?.Trim() ?? "");
                    int result = command.ExecuteNonQuery();
                    if (result > 0)
                    {
                        NotifyLocalConfigDelta(
                            "CUSTOMER",
                            code,
                            "UPSERT",
                            BuildCustomerSyncPayload(code, name.Trim(), phone?.Trim() ?? "", ClientTypeBuyer, true));
                    }
                    return result > 0;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"添加买家失败: {ex.Message}");
                return false;
            }
        }

        public bool UpdateBuyer(string buyerCode, string name, string phone = "")
        {
            try
            {
                if (string.IsNullOrWhiteSpace(buyerCode) || string.IsNullOrWhiteSpace(name))
                    return false;

                string sql = @"
UPDATE clients
SET name = @name,
    phone = @phone,
    updated_at = datetime('now')
WHERE code = @code
  AND COALESCE(customer_type, CASE WHEN UPPER(code) LIKE 'MJ%' THEN 'BUYER' ELSE 'SELLER' END) = 'BUYER'";

                int rowsAffected = ExecuteNonQuery(sql, new Dictionary<string, object>
                {
                    { "@code", buyerCode },
                    { "@name", name.Trim() },
                    { "@phone", phone?.Trim() ?? "" }
                });

                if (rowsAffected > 0)
                    NotifyCustomerSyncRow(buyerCode);

                return rowsAffected > 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"更新买家失败: {ex.Message}");
                return false;
            }
        }

        public bool ToggleBuyerStatus(string buyerCode)
        {
            try
            {
                string sql = @"
UPDATE clients
SET status = CASE WHEN COALESCE(status, 1) = 1 THEN 0 ELSE 1 END,
    updated_at = datetime('now')
WHERE code = @code
  AND COALESCE(customer_type, CASE WHEN UPPER(code) LIKE 'MJ%' THEN 'BUYER' ELSE 'SELLER' END) = 'BUYER'";

                int rowsAffected = ExecuteNonQuery(sql, new Dictionary<string, object>
                {
                    { "@code", buyerCode }
                });

                if (rowsAffected > 0)
                    NotifyCustomerSyncRow(buyerCode);

                return rowsAffected > 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"切换买家状态失败: {ex.Message}");
                return false;
            }
        }

        public BuyerDeleteResult DeleteBuyer(string buyerCode)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(buyerCode))
                    return BuyerDeleteResult.Failed;

                string checkSql = @"
SELECT COUNT(*) FROM presale_bills WHERE buyer_code = @code
UNION ALL
SELECT COUNT(*) FROM presale_payments WHERE buyer_code = @code";

                DataTable dt = ExecuteQuery(checkSql, new Dictionary<string, object>
                {
                    { "@code", buyerCode }
                });

                foreach (DataRow row in dt.Rows)
                {
                    if (Convert.ToInt32(row[0]) > 0)
                    {
                        bool disabled = ExecuteNonQuery(
                            "UPDATE clients SET status = 0, updated_at = datetime('now') WHERE code = @code",
                            new Dictionary<string, object> { { "@code", buyerCode } }) > 0;
                        if (disabled)
                            NotifyCustomerSyncRow(buyerCode);
                        return disabled ? BuyerDeleteResult.Disabled : BuyerDeleteResult.Failed;
                    }
                }

                int rowsAffected = ExecuteNonQuery(
                    "DELETE FROM clients WHERE code = @code",
                    new Dictionary<string, object> { { "@code", buyerCode } });

                if (rowsAffected > 0)
                {
                    NotifyLocalConfigDelta("CUSTOMER", buyerCode, "DELETE", new JObject { ["code"] = buyerCode });
                }
                return rowsAffected > 0 ? BuyerDeleteResult.Deleted : BuyerDeleteResult.Failed;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"删除买家失败: {ex.Message}");
                return BuyerDeleteResult.Failed;
            }
        }

        public bool UpsertClientFromSync(string code, string name, string contact, string phone, string address)
        {
            return UpsertClientFromSync(code, name, contact, phone, address, null, null);
        }

        public bool UpsertClientFromSync(string code, string name, string contact, string phone, string address, string customerType, int? status)
        {
            try
            {
                FixClientsTable();
                string resolvedType = string.IsNullOrWhiteSpace(customerType)
                    ? ResolveClientTypeFromCode(code)
                    : customerType.Trim().ToUpperInvariant();
                int statusVal = status ?? 1;

                if (CheckClientExists(code))
                {
                    string sql = @"
UPDATE clients
SET name = @name,
    contact = @contact,
    phone = @phone,
    address = @address,
    customer_type = @customerType,
    status = @status,
    updated_at = datetime('now')
WHERE code = @code";
                    return ExecuteNonQuery(sql, new Dictionary<string, object>
                    {
                        { "@code", code },
                        { "@name", name ?? "" },
                        { "@contact", contact ?? "" },
                        { "@phone", phone ?? "" },
                        { "@address", address ?? "" },
                        { "@customerType", resolvedType },
                        { "@status", statusVal }
                    }) > 0;
                }

                using (var connection = new SQLiteConnection(connectionString))
                {
                    connection.Open();
                    var command = new SQLiteCommand(@"
INSERT INTO clients (code, name, contact, phone, address, status, customer_type)
VALUES (@code, @name, @contact, @phone, @address, @status, @customerType)", connection);
                    command.Parameters.AddWithValue("@code", code);
                    command.Parameters.AddWithValue("@name", name ?? "");
                    command.Parameters.AddWithValue("@contact", contact ?? "");
                    command.Parameters.AddWithValue("@phone", phone ?? "");
                    command.Parameters.AddWithValue("@address", address ?? "");
                    command.Parameters.AddWithValue("@customerType", resolvedType);
                    command.Parameters.AddWithValue("@status", statusVal);
                    return command.ExecuteNonQuery() > 0;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"同步保存客户/买家失败: {ex.Message}");
                return false;
            }
        }

        // 修复搜索客户方法
        public DataTable SearchClients(string keyword)
        {
            try
            {
                Console.WriteLine($">>> [SearchClients] 开始搜索，关键词: '{keyword}'");

                if (string.IsNullOrWhiteSpace(keyword))
                {
                    // 如果没有关键词，返回所有客户
                    return GetAllClients();
                }

                // 构建SQL查询
                string sql = @"
            SELECT id, code, name, contact, phone, address, balance, created_time 
            FROM clients 
            WHERE (code LIKE @keyword OR name LIKE @keyword OR phone LIKE @keyword) 
            AND status = 1
            AND COALESCE(customer_type, CASE WHEN UPPER(code) LIKE 'MJ%' THEN 'BUYER' ELSE 'SELLER' END) = 'SELLER'
            ORDER BY code ASC";

                var result = ExecuteQuery(sql, new Dictionary<string, object>
        {
            { "@keyword", $"%{keyword}%" }
        });

                Console.WriteLine($">>> [SearchClients] 搜索结果: {result.Rows.Count} 条记录");
                return result;
            }
            catch (Exception ex)
            {
                Console.WriteLine($">>> [SearchClients] 异常: {ex.Message}");
                return new DataTable();
            }
        }
        // 确保 GetAllClients 方法正确
        public DataTable GetAllClients()
        {
            try
            {
                Console.WriteLine(">>> [GetAllClients] 开始获取客户数据 - 无过滤版本");

                // 最简单的查询，无任何WHERE条件
                string sql = "SELECT * FROM clients ORDER BY code ASC";

                DataTable result = ExecuteQuery(sql);

                Console.WriteLine($">>> [GetAllClients] 查询完成，返回 {result.Rows.Count} 行数据");

                return result;
            }
            catch (Exception ex)
            {
                Console.WriteLine($">>> [GetAllClients] 异常: {ex.Message}");
                return new DataTable();
            }
        }

        /// <summary>
        /// 全量同步：全部客户（卖家 + 买家）
        /// </summary>
        public DataTable GetAllClientsForFullSync()
        {
            try
            {
                const string sql = @"SELECT code, name, phone, status,
COALESCE(customer_type, CASE WHEN UPPER(code) LIKE 'MJ%' THEN 'BUYER' ELSE 'SELLER' END) AS customer_type
FROM clients
ORDER BY code ASC";
                return ExecuteQuery(sql);
            }
            catch (Exception ex)
            {
                Console.WriteLine($">>> [GetAllClientsForFullSync] 异常: {ex.Message}");
                return new DataTable();
            }
        }

        /// <summary>全量同步：商品型号</summary>
        public DataTable GetProductTypesForSync()
        {
            try
            {
                const string sql = @"SELECT code, name, is_active FROM product_types ORDER BY code ASC";
                return ExecuteQuery(sql);
            }
            catch (Exception ex)
            {
                Console.WriteLine($">>> [GetProductTypesForSync] 异常: {ex.Message}");
                return new DataTable();
            }
        }

        /// <summary>全量同步：包装类型</summary>
        public DataTable GetPackTypesForSync()
        {
            try
            {
                const string sql = @"SELECT name, is_active FROM pack_types ORDER BY name ASC";
                return ExecuteQuery(sql);
            }
            catch (Exception ex)
            {
                Console.WriteLine($">>> [GetPackTypesForSync] 异常: {ex.Message}");
                return new DataTable();
            }
        }

        /// <summary>
        /// 全量同步用手持「客户列表」：仅卖家（与 SystemSettingsForm 客户 Tab 一致）
        /// </summary>
        public DataTable GetSellerClientsForSync()
        {
            try
            {
                const string sql = @"SELECT * FROM clients
WHERE COALESCE(customer_type, CASE WHEN UPPER(code) LIKE 'MJ%' THEN 'BUYER' ELSE 'SELLER' END) = 'SELLER'
ORDER BY code ASC";
                DataTable result = ExecuteQuery(sql);
                Console.WriteLine($">>> [GetSellerClientsForSync] 查询完成，返回 {result.Rows.Count} 行卖家数据");
                return result;
            }
            catch (Exception ex)
            {
                Console.WriteLine($">>> [GetSellerClientsForSync] 异常: {ex.Message}");
                return new DataTable();
            }
        }
        public void CheckInboundTableStructure()
        {
            try
            {
                using (SQLiteConnection conn = new SQLiteConnection(ConnectionString))
                {
                    conn.Open();

                    Console.WriteLine("=== inbound_transactions表结构 ===");
                    string sql = "PRAGMA table_info(inbound_transactions)";

                    using (SQLiteCommand cmd = new SQLiteCommand(sql, conn))
                    {
                        using (SQLiteDataReader reader = cmd.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                Console.WriteLine($"字段: {reader["name"]}, 类型: {reader["type"]}");
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"检查表结构失败: {ex.Message}");
            }
        }
        // 测试数据库连接和表结构
        public void TestDatabaseConnection()
        {
            try
            {
                Console.WriteLine("=== 数据库连接测试 ===");

                // 测试连接
                string testSql = "SELECT 1 as test";
                var result = ExecuteQuery(testSql);
                Console.WriteLine($">>> 数据库连接测试: {(result.Rows.Count > 0 ? "成功" : "失败")}");

                // 测试客户表
                var clients = ExecuteQuery("SELECT COUNT(*) as count FROM clients");
                if (clients.Rows.Count > 0)
                {
                    Console.WriteLine($">>> 客户表记录数: {clients.Rows[0]["count"]}");
                }

                // 查看表结构
                var schema = ExecuteQuery("PRAGMA table_info(clients)");
                Console.WriteLine(">>> 客户表结构:");
                foreach (DataRow row in schema.Rows)
                {
                    Console.WriteLine($"   列: {row["name"]}, 类型: {row["type"]}");
                }

                Console.WriteLine("=== 数据库测试完成 ===");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"数据库测试失败: {ex.Message}");
            }
        }

        // 获取所有客户（完整信息）
        public DataTable GetAllClientsWithDetails()
        {
            try
            {
                string sql = @"
            SELECT code, name, contact, phone, address, balance, created_time
            FROM clients 
            ORDER BY code ASC";

                return ExecuteQuery(sql);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"获取客户列表失败: {ex.Message}");
                return new DataTable();
            }
        }
        // 更新客户信息
        // 在 DatabaseManager.cs 中修复 UpdateClient 方法
        public bool UpdateClient(string clientCode, string name, string contact, string phone, string address)
        {
            try
            {
                Console.WriteLine($">>> [DatabaseManager.UpdateClient] 开始更新客户: Code={clientCode}");
                Console.WriteLine($">>> 参数: Name={name}, Contact={contact}, Phone={phone}, Address={address}");

                // ✅ 根据您的表结构，所有字段都存在，直接使用完整更新
                string sql = @"
            UPDATE clients 
            SET name = @name, 
                contact = @contact, 
                phone = @phone, 
                address = @address,
                updated_at = datetime('now')
            WHERE code = @code";

                var parameters = new Dictionary<string, object>
        {
            { "@code", clientCode },
            { "@name", name ?? "" },
            { "@contact", contact ?? "" },
            { "@phone", phone ?? "" },
            { "@address", address ?? "" }
        };

                int rowsAffected = ExecuteNonQuery(sql, parameters);

                Console.WriteLine($">>> 影响行数: {rowsAffected}");

                if (rowsAffected > 0)
                    NotifyCustomerSyncRow(clientCode);

                return rowsAffected > 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine($">>> [DatabaseManager.UpdateClient] 异常: {ex.Message}");
                return false;
            }
        }
        // 2. 根据编号删除客户
        public bool DeleteClientByCode(string clientCode)
        {
            try
            {
                // 先检查客户是否有相关业务记录
                string checkSql = @"
                    SELECT COUNT(*) as count 
                    FROM inbound_transactions 
                    WHERE client_code = @code
                    UNION ALL
                    SELECT COUNT(*) 
                    FROM sales_transactions 
                    WHERE client_code = @code";

                DataTable dt = ExecuteQuery(checkSql, new Dictionary<string, object>
                {
                    { "@code", clientCode }
                });

                bool hasRecords = false;
                foreach (DataRow row in dt.Rows)
                {
                    if (Convert.ToInt32(row[0]) > 0)
                    {
                        hasRecords = true;
                        break;
                    }
                }

                if (hasRecords)
                {
                    return DisableClientByCode(clientCode);
                }
                else
                {
                    // 如果没有业务记录，直接删除
                    string deleteSql = "DELETE FROM clients WHERE code = @code";
                    int rowsAffected = ExecuteNonQuery(deleteSql, new Dictionary<string, object>
                    {
                        { "@code", clientCode }
                    });

                    if (rowsAffected > 0)
                    {
                        NotifyLocalConfigDelta("CUSTOMER", clientCode, "DELETE", new JObject { ["code"] = clientCode });
                    }
                    return rowsAffected > 0;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"删除客户失败: {ex.Message}");
                return false;
            }
        }
        // 执行带参数的SQL（非查询）
        public int ExecuteNonQueryWithParams(string sql, Dictionary<string, object> parameters = null)
        {
            try
            {
                // 确保 ConnectionString 存在
                string connectionString = GetConnectionString();

                using (var connection = new SQLiteConnection(connectionString))
                {
                    connection.Open();
                    using (var command = new SQLiteCommand(sql, connection))
                    {
                        if (parameters != null)
                        {
                            foreach (var param in parameters)
                            {
                                command.Parameters.AddWithValue(param.Key, param.Value);
                            }
                        }

                        return command.ExecuteNonQuery();
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"执行非查询失败: {ex.Message}");
                return 0;
            }
        }

        // 获取数据库连接字符串
        private string GetConnectionString()
        {
            try
            {
                return BuildConnectionString(GetDatabasePath());
            }
            catch (Exception ex)
            {
                Console.WriteLine($">>> 获取连接字符串失败: {ex.Message}");
                return "Data Source=lengkubao.db;Version=3;";
            }
        }

        #region 商品型号管理方法

        /// <summary>
        /// 生成新的型号编号（SP01, SP02, ...）
        /// </summary>
        public string GenerateProductCode()
        {
            try
            {
                using (var connection = new SQLiteConnection(connectionString))
                {
                    connection.Open();

                    // 获取最大的编号数字部分
                    var command = new SQLiteCommand(@"
                SELECT code FROM product_types 
                WHERE code LIKE 'SP%' 
                ORDER BY CAST(SUBSTR(code, 3) AS INTEGER) DESC 
                LIMIT 1", connection);

                    var result = command.ExecuteScalar();

                    if (result != null && result != DBNull.Value)
                    {
                        string lastCode = result.ToString();
                        if (lastCode.Length > 2 && int.TryParse(lastCode.Substring(2), out int lastNumber))
                        {
                            int newNumber = lastNumber + 1;
                            return $"SP{newNumber:D2}"; // D2 保证两位数，如01,02
                        }
                    }

                    // 如果没有记录，从SP01开始
                    return "SP01";
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"生成型号编号失败: {ex.Message}");
                return "SP01"; // 出错时返回默认值
            }
        }

        /// <summary>
        /// 获取所有型号（包含编号）
        /// </summary>
        public DataTable GetAllProductTypes()
        {
            var dataTable = new DataTable();
            try
            {
                using (var connection = new SQLiteConnection(connectionString))
                {
                    connection.Open();
                    var command = new SQLiteCommand(@"
                SELECT id, code, name, is_active, created_time
                FROM product_types 
                ORDER BY CAST(SUBSTR(code, 3) AS INTEGER)", connection); // 按编号数字排序

                    var adapter = new SQLiteDataAdapter(command);
                    adapter.Fill(dataTable);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"获取型号列表失败: {ex.Message}");
            }
            return dataTable;
        }

        /// <summary>
        /// 搜索型号（包含编号）
        /// </summary>
        public DataTable SearchProductTypes(string keyword)
        {
            var dataTable = new DataTable();
            try
            {
                using (var connection = new SQLiteConnection(connectionString))
                {
                    connection.Open();
                    var command = new SQLiteCommand(@"
                SELECT id, code, name, is_active, created_time
                FROM product_types 
                WHERE name LIKE @keyword OR code LIKE @keyword
                ORDER BY CAST(SUBSTR(code, 3) AS INTEGER)", connection);

                    command.Parameters.AddWithValue("@keyword", $"%{keyword}%");
                    var adapter = new SQLiteDataAdapter(command);
                    adapter.Fill(dataTable);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"搜索型号失败: {ex.Message}");
            }
            return dataTable;
        }

        /// <summary>
        /// 添加型号（自动生成编号）
        /// </summary>
        public bool AddProductType(string name, bool isActive = true)
        {
            try
            {
                string code = GenerateProductCode();

                using (var connection = new SQLiteConnection(connectionString))
                {
                    connection.Open();

                    // 检查编号是否已存在（防止并发问题）
                    var checkCommand = new SQLiteCommand("SELECT COUNT(*) FROM product_types WHERE code = @code", connection);
                    checkCommand.Parameters.AddWithValue("@code", code);
                    int count = Convert.ToInt32(checkCommand.ExecuteScalar());

                    // 如果编号已存在，重新生成
                    while (count > 0)
                    {
                        if (int.TryParse(code.Substring(2), out int lastNumber))
                        {
                            code = $"SP{lastNumber + 1:D2}";
                        }
                        else
                        {
                            code = GenerateProductCode(); // 重新生成
                        }
                        checkCommand.Parameters["@code"].Value = code;
                        count = Convert.ToInt32(checkCommand.ExecuteScalar());
                    }

                    var command = new SQLiteCommand(@"
                INSERT INTO product_types (code, name, is_active, created_time) 
                VALUES (@code, @name, @isActive, CURRENT_TIMESTAMP)", connection);

                    command.Parameters.AddWithValue("@code", code);
                    command.Parameters.AddWithValue("@name", name);
                    command.Parameters.AddWithValue("@isActive", isActive ? 1 : 0);

                    int result = command.ExecuteNonQuery();
                    if (result > 0)
                    {
                        Console.WriteLine($"型号添加成功，编号: {code}");
                        NotifyLocalConfigDelta(
                            "PRODUCT",
                            code,
                            "UPSERT",
                            new JObject
                            {
                                ["code"] = code,
                                ["name"] = name,
                                ["enabled"] = isActive
                            });
                        return true;
                    }
                    return false;
                }
            }
            catch (SQLiteException ex)
            {
                if (ex.Message.Contains("UNIQUE"))
                {
                    if (ex.Message.Contains("product_types.name"))
                        Console.WriteLine($"型号名称 '{name}' 已存在！");
                    else if (ex.Message.Contains("product_types.code"))
                        Console.WriteLine($"型号编号生成冲突，请重试！");
                }
                else
                {
                    Console.WriteLine($"添加型号失败: {ex.Message}");
                }
                return false;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"添加型号失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 更新型号（保持编号不变）
        /// </summary>
        public bool UpdateProductType(int id, string name, bool isActive)
        {
            try
            {
                using (var connection = new SQLiteConnection(connectionString))
                {
                    connection.Open();

                    string code = null;
                    string oldName = null;
                    using (var getRow = new SQLiteCommand(
                        "SELECT code, name FROM product_types WHERE id = @id", connection))
                    {
                        getRow.Parameters.AddWithValue("@id", id);
                        using (var reader = getRow.ExecuteReader())
                        {
                            if (!reader.Read())
                                return false;
                            code = reader["code"]?.ToString()?.Trim();
                            oldName = reader["name"]?.ToString()?.Trim();
                        }
                    }

                    var command = new SQLiteCommand(@"
                UPDATE product_types 
                SET name = @name, is_active = @isActive 
                WHERE id = @id", connection);

                    command.Parameters.AddWithValue("@name", name);
                    command.Parameters.AddWithValue("@isActive", isActive ? 1 : 0);
                    command.Parameters.AddWithValue("@id", id);

                    int result = command.ExecuteNonQuery();
                    if (result > 0)
                    {
                        string trimmedName = name?.Trim() ?? "";
                        string entityKey = !string.IsNullOrWhiteSpace(code) ? code
                            : !string.IsNullOrWhiteSpace(trimmedName) ? trimmedName
                            : id.ToString();
                        Console.WriteLine($"型号更新成功，编号: {entityKey}");
                        var payload = new JObject
                        {
                            ["code"] = code ?? "",
                            ["name"] = trimmedName,
                            ["enabled"] = isActive
                        };
                        if (!string.IsNullOrWhiteSpace(oldName) &&
                            !string.Equals(oldName, trimmedName, StringComparison.Ordinal))
                        {
                            payload["previous_name"] = oldName;
                        }
                        NotifyLocalConfigDelta("PRODUCT", entityKey, "UPSERT", payload);
                        return true;
                    }
                    return false;
                }
            }
            catch (SQLiteException ex)
            {
                if (ex.Message.Contains("UNIQUE"))
                {
                    Console.WriteLine($"型号名称 '{name}' 已存在！");
                }
                else
                {
                    Console.WriteLine($"更新型号失败: {ex.Message}");
                }
                return false;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"更新型号失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>增量同步：按 code/name 插入或更新商品型号。</summary>
        public bool UpsertProductTypeForSync(string code, string name, bool isActive, string category = null)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(name) && string.IsNullOrWhiteSpace(code))
                    return false;

                using (var connection = new SQLiteConnection(connectionString))
                {
                    connection.Open();
                    string lookupCode = code?.Trim();
                    string lookupName = name?.Trim();

                    int existingId = 0;
                    string existingCode = lookupCode;
                    using (var find = new SQLiteCommand(@"
SELECT id, code FROM product_types
WHERE (@code != '' AND code = @code) OR name = @name
LIMIT 1", connection))
                    {
                        find.Parameters.AddWithValue("@code", lookupCode ?? "");
                        find.Parameters.AddWithValue("@name", lookupName ?? lookupCode ?? "");
                        using (var reader = find.ExecuteReader())
                        {
                            if (reader.Read())
                            {
                                existingId = Convert.ToInt32(reader["id"]);
                                existingCode = reader["code"]?.ToString() ?? lookupCode;
                            }
                        }
                    }

                    if (existingId > 0)
                    {
                        using (var cmd = new SQLiteCommand(@"
UPDATE product_types SET name = @name, is_active = @isActive WHERE id = @id", connection))
                        {
                            cmd.Parameters.AddWithValue("@name", lookupName ?? lookupCode);
                            cmd.Parameters.AddWithValue("@isActive", isActive ? 1 : 0);
                            cmd.Parameters.AddWithValue("@id", existingId);
                            return cmd.ExecuteNonQuery() > 0;
                        }
                    }

                    if (string.IsNullOrWhiteSpace(lookupCode))
                        lookupCode = GenerateProductCode();

                    using (var insert = new SQLiteCommand(@"
INSERT INTO product_types (code, name, is_active, created_time)
VALUES (@code, @name, @isActive, CURRENT_TIMESTAMP)", connection))
                    {
                        insert.Parameters.AddWithValue("@code", lookupCode);
                        insert.Parameters.AddWithValue("@name", lookupName ?? lookupCode);
                        insert.Parameters.AddWithValue("@isActive", isActive ? 1 : 0);
                        return insert.ExecuteNonQuery() > 0;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"UpsertProductTypeForSync 失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>增量同步：软禁用商品型号。</summary>
        public bool DisableProductTypeForSync(string code, string name)
        {
            try
            {
                using (var connection = new SQLiteConnection(connectionString))
                {
                    connection.Open();
                    using (var cmd = new SQLiteCommand(@"
UPDATE product_types SET is_active = 0
WHERE (@code != '' AND code = @code) OR name = @name", connection))
                    {
                        cmd.Parameters.AddWithValue("@code", code ?? "");
                        cmd.Parameters.AddWithValue("@name", name ?? code ?? "");
                        return cmd.ExecuteNonQuery() >= 0;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"DisableProductTypeForSync 失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>增量同步：按 name 插入或更新包装类型。</summary>
        public bool UpsertPackTypeForSync(string code, string name, bool isActive)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(name))
                    name = code;
                if (string.IsNullOrWhiteSpace(name))
                    return false;

                using (var connection = new SQLiteConnection(connectionString))
                {
                    connection.Open();
                    using (var find = new SQLiteCommand("SELECT id FROM pack_types WHERE name = @name LIMIT 1", connection))
                    {
                        find.Parameters.AddWithValue("@name", name.Trim());
                        var found = find.ExecuteScalar();
                        if (found != null && found != DBNull.Value)
                        {
                            using (var cmd = new SQLiteCommand(@"
UPDATE pack_types SET is_active = @isActive WHERE name = @name", connection))
                            {
                                cmd.Parameters.AddWithValue("@name", name.Trim());
                                cmd.Parameters.AddWithValue("@isActive", isActive ? 1 : 0);
                                return cmd.ExecuteNonQuery() > 0;
                            }
                        }
                    }

                    using (var insert = new SQLiteCommand(@"
INSERT INTO pack_types (name, is_active, created_time)
VALUES (@name, @isActive, CURRENT_TIMESTAMP)", connection))
                    {
                        insert.Parameters.AddWithValue("@name", name.Trim());
                        insert.Parameters.AddWithValue("@isActive", isActive ? 1 : 0);
                        return insert.ExecuteNonQuery() > 0;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"UpsertPackTypeForSync 失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>增量同步：软禁用包装类型。</summary>
        public bool DisablePackTypeForSync(string code, string name)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(name))
                    name = code;
                if (string.IsNullOrWhiteSpace(name))
                    return false;

                using (var connection = new SQLiteConnection(connectionString))
                {
                    connection.Open();
                    using (var cmd = new SQLiteCommand(@"
UPDATE pack_types SET is_active = 0 WHERE name = @name", connection))
                    {
                        cmd.Parameters.AddWithValue("@name", name.Trim());
                        return cmd.ExecuteNonQuery() >= 0;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"DisablePackTypeForSync 失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 删除型号（物理删除）
        /// </summary>
        public bool DeleteProductType(int id)
        {
            try
            {
                using (var connection = new SQLiteConnection(connectionString))
                {
                    connection.Open();

                    string name = null;
                    string code = null;
                    using (var getRow = new SQLiteCommand("SELECT name, code FROM product_types WHERE id = @id", connection))
                    {
                        getRow.Parameters.AddWithValue("@id", id);
                        using (var reader = getRow.ExecuteReader())
                        {
                            if (!reader.Read())
                            {
                                Console.WriteLine($">>> [DeleteProductType] 型号不存在: id={id}");
                                return false;
                            }
                            name = reader["name"]?.ToString()?.Trim();
                            code = reader["code"]?.ToString()?.Trim();
                        }
                    }

                    int referenceCount = CountProductTypeReferences(connection, name, code);
                    if (referenceCount > 0)
                    {
                        string label = string.IsNullOrEmpty(code) ? name : $"{code} {name}";
                        Console.WriteLine($">>> [DeleteProductType] 型号 [{label}] 被 {referenceCount} 条业务记录引用，不能删除");
                        return false;
                    }

                    using (var command = new SQLiteCommand("DELETE FROM product_types WHERE id = @id", connection))
                    {
                        command.Parameters.AddWithValue("@id", id);
                        int result = command.ExecuteNonQuery();
                        if (result > 0)
                        {
                            Console.WriteLine($">>> [DeleteProductType] 型号 [{code ?? name}] 删除成功");
                            NotifyLocalConfigDelta(
                                "PRODUCT",
                                code ?? name ?? id.ToString(),
                                "DELETE",
                                new JObject
                                {
                                    ["code"] = code ?? "",
                                    ["name"] = name ?? ""
                                });
                            return true;
                        }
                    }
                    return false;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"删除型号失败: {ex.Message}");
                return false;
            }
        }

        private static int CountProductTypeReferences(SQLiteConnection connection, string name, string code)
        {
            var sqlParts = new List<string>
            {
                "SELECT spec FROM inbound_transactions WHERE spec = @name",
                "SELECT spec FROM sales_transactions WHERE spec = @name",
                "SELECT spec FROM presale_items WHERE spec = @name"
            };

            if (!string.IsNullOrWhiteSpace(code))
            {
                sqlParts.Add("SELECT spec FROM inbound_transactions WHERE spec = @code");
                sqlParts.Add("SELECT spec FROM sales_transactions WHERE spec = @code");
                sqlParts.Add("SELECT spec FROM presale_items WHERE spec = @code");
            }

            using (var command = new SQLiteCommand($@"
                SELECT COUNT(*) FROM (
                    {string.Join(" UNION ALL ", sqlParts)}
                )", connection))
            {
                command.Parameters.AddWithValue("@name", name ?? string.Empty);
                command.Parameters.AddWithValue("@code", code ?? string.Empty);
                return Convert.ToInt32(command.ExecuteScalar());
            }
        }

        /// <summary>
        /// 切换型号状态
        /// </summary>
        public bool ToggleProductStatus(int id, bool isActive)
        {
            try
            {
                using (var connection = new SQLiteConnection(connectionString))
                {
                    connection.Open();

                    var getCodeCommand = new SQLiteCommand("SELECT code FROM product_types WHERE id = @id", connection);
                    getCodeCommand.Parameters.AddWithValue("@id", id);
                    string code = getCodeCommand.ExecuteScalar()?.ToString() ?? "未知";

                    var command = new SQLiteCommand(@"
                UPDATE product_types 
                SET is_active = @isActive 
                WHERE id = @id", connection);

                    command.Parameters.AddWithValue("@isActive", isActive ? 1 : 0);
                    command.Parameters.AddWithValue("@id", id);

                    int result = command.ExecuteNonQuery();
                    if (result > 0)
                    {
                        string status = isActive ? "启用" : "禁用";
                        Console.WriteLine($"型号 [{code}] {status}成功");
                        using (var nameCmd = new SQLiteCommand(
                            "SELECT name FROM product_types WHERE id = @id", connection))
                        {
                            nameCmd.Parameters.AddWithValue("@id", id);
                            string name = nameCmd.ExecuteScalar()?.ToString() ?? code;
                            NotifyLocalConfigDelta(
                                "PRODUCT",
                                code,
                                "UPSERT",
                                new JObject
                                {
                                    ["code"] = code,
                                    ["name"] = name,
                                    ["enabled"] = isActive
                                });
                        }
                        return true;
                    }
                    return false;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"操作失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 根据编号获取型号信息
        /// </summary>
        public DataRow GetProductTypeByCode(string code)
        {
            try
            {
                using (var connection = new SQLiteConnection(connectionString))
                {
                    connection.Open();
                    var command = new SQLiteCommand(@"
                SELECT id, code, name, is_active, created_time
                FROM product_types 
                WHERE code = @code", connection);

                    command.Parameters.AddWithValue("@code", code);
                    var adapter = new SQLiteDataAdapter(command);
                    var dt = new DataTable();
                    adapter.Fill(dt);

                    if (dt.Rows.Count > 0)
                        return dt.Rows[0];
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"获取型号信息失败: {ex.Message}");
            }
            return null;
        }

        #endregion

        #region 包装类型字典管理（pack_types）

        public DataTable GetAllPackTypes()
        {
            var dataTable = new DataTable();
            try
            {
                using (var connection = new SQLiteConnection(connectionString))
                {
                    connection.Open();
                    var command = new SQLiteCommand(@"
                SELECT id, name, is_active, created_time
                FROM pack_types
                ORDER BY name", connection);
                    var adapter = new SQLiteDataAdapter(command);
                    adapter.Fill(dataTable);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"获取包装类型列表失败: {ex.Message}");
            }
            return dataTable;
        }

        public DataTable SearchPackTypes(string keyword)
        {
            var dataTable = new DataTable();
            try
            {
                using (var connection = new SQLiteConnection(connectionString))
                {
                    connection.Open();
                    var command = new SQLiteCommand(@"
                SELECT id, name, is_active, created_time
                FROM pack_types
                WHERE name LIKE @keyword
                ORDER BY name", connection);
                    command.Parameters.AddWithValue("@keyword", $"%{keyword}%");
                    var adapter = new SQLiteDataAdapter(command);
                    adapter.Fill(dataTable);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"搜索包装类型失败: {ex.Message}");
            }
            return dataTable;
        }

        /// <summary>
        /// 仅返回启用中的包装类型名称，供包装记账等下拉使用。
        /// </summary>
        public List<string> GetActivePackTypeNames()
        {
            var list = new List<string>();
            try
            {
                using (var connection = new SQLiteConnection(connectionString))
                {
                    connection.Open();
                    var command = new SQLiteCommand(
                        "SELECT name FROM pack_types WHERE is_active = 1 ORDER BY name", connection);
                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            string n = reader["name"]?.ToString();
                            if (!string.IsNullOrWhiteSpace(n))
                                list.Add(n);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"获取启用包装类型失败: {ex.Message}");
            }
            return list;
        }

        public bool AddPackType(string name, bool isActive = true)
        {
            try
            {
                using (var connection = new SQLiteConnection(connectionString))
                {
                    connection.Open();
                    var command = new SQLiteCommand(@"
                INSERT INTO pack_types (name, is_active, created_time)
                VALUES (@name, @isActive, CURRENT_TIMESTAMP)", connection);
                    command.Parameters.AddWithValue("@name", name.Trim());
                    command.Parameters.AddWithValue("@isActive", isActive ? 1 : 0);
                    bool ok = command.ExecuteNonQuery() > 0;
                    if (ok)
                    {
                        NotifyLocalConfigDelta(
                            "PACK_TYPE",
                            name.Trim(),
                            "UPSERT",
                            new JObject
                            {
                                ["name"] = name.Trim(),
                                ["enabled"] = isActive
                            });
                    }
                    return ok;
                }
            }
            catch (SQLiteException ex)
            {
                if (ex.Message.Contains("UNIQUE"))
                    Console.WriteLine($"包装类型名称 '{name}' 已存在！");
                else
                    Console.WriteLine($"添加包装类型失败: {ex.Message}");
                return false;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"添加包装类型失败: {ex.Message}");
                return false;
            }
        }

        public bool UpdatePackType(int id, string name, bool isActive)
        {
            try
            {
                using (var connection = new SQLiteConnection(connectionString))
                {
                    connection.Open();

                    string oldName = null;
                    using (var getCmd = new SQLiteCommand("SELECT name FROM pack_types WHERE id = @id", connection))
                    {
                        getCmd.Parameters.AddWithValue("@id", id);
                        oldName = getCmd.ExecuteScalar()?.ToString();
                    }

                    string trimmedName = name.Trim();
                    var command = new SQLiteCommand(@"
                UPDATE pack_types
                SET name = @name, is_active = @isActive
                WHERE id = @id", connection);
                    command.Parameters.AddWithValue("@name", trimmedName);
                    command.Parameters.AddWithValue("@isActive", isActive ? 1 : 0);
                    command.Parameters.AddWithValue("@id", id);
                    bool ok = command.ExecuteNonQuery() > 0;
                    if (ok)
                    {
                        var payload = new JObject
                        {
                            ["name"] = trimmedName,
                            ["enabled"] = isActive
                        };
                        if (!string.IsNullOrWhiteSpace(oldName) &&
                            !string.Equals(oldName, trimmedName, StringComparison.Ordinal))
                        {
                            payload["previous_name"] = oldName;
                        }
                        NotifyLocalConfigDelta("PACK_TYPE", trimmedName, "UPSERT", payload);
                    }
                    return ok;
                }
            }
            catch (SQLiteException ex)
            {
                if (ex.Message.Contains("UNIQUE"))
                    Console.WriteLine($"包装类型名称 '{name}' 已存在！");
                else
                    Console.WriteLine($"更新包装类型失败: {ex.Message}");
                return false;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"更新包装类型失败: {ex.Message}");
                return false;
            }
        }

        public bool TogglePackTypeStatus(int id, bool isActive)
        {
            try
            {
                using (var connection = new SQLiteConnection(connectionString))
                {
                    connection.Open();

                    string packName = null;
                    using (var getCmd = new SQLiteCommand("SELECT name FROM pack_types WHERE id = @id", connection))
                    {
                        getCmd.Parameters.AddWithValue("@id", id);
                        packName = getCmd.ExecuteScalar()?.ToString();
                    }

                    var command = new SQLiteCommand(@"
                UPDATE pack_types
                SET is_active = @isActive
                WHERE id = @id", connection);
                    command.Parameters.AddWithValue("@isActive", isActive ? 1 : 0);
                    command.Parameters.AddWithValue("@id", id);
                    bool ok = command.ExecuteNonQuery() > 0;
                    if (ok && !string.IsNullOrWhiteSpace(packName))
                    {
                        NotifyLocalConfigDelta(
                            "PACK_TYPE",
                            packName,
                            "UPSERT",
                            new JObject
                            {
                                ["name"] = packName,
                                ["enabled"] = isActive
                            });
                    }
                    return ok;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"切换包装类型状态失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 删除包装类型（物理删除）
        /// </summary>
        public bool DeletePackType(int id)
        {
            try
            {
                using (var connection = new SQLiteConnection(connectionString))
                {
                    connection.Open();

                    string name = null;
                    using (var getRow = new SQLiteCommand("SELECT name FROM pack_types WHERE id = @id", connection))
                    {
                        getRow.Parameters.AddWithValue("@id", id);
                        name = getRow.ExecuteScalar()?.ToString()?.Trim();
                    }

                    if (string.IsNullOrEmpty(name))
                    {
                        Console.WriteLine($">>> [DeletePackType] 包装类型不存在: id={id}");
                        return false;
                    }

                    using (var checkCommand = new SQLiteCommand(
                        "SELECT COUNT(*) FROM packaging_transactions WHERE pack_type = @name", connection))
                    {
                        checkCommand.Parameters.AddWithValue("@name", name);
                        int referenceCount = Convert.ToInt32(checkCommand.ExecuteScalar());
                        if (referenceCount > 0)
                        {
                            Console.WriteLine($">>> [DeletePackType] 包装类型 [{name}] 被 {referenceCount} 条业务记录引用，不能删除");
                            return false;
                        }
                    }

                    using (var command = new SQLiteCommand("DELETE FROM pack_types WHERE id = @id", connection))
                    {
                        command.Parameters.AddWithValue("@id", id);
                        int result = command.ExecuteNonQuery();
                        if (result > 0)
                        {
                            Console.WriteLine($">>> [DeletePackType] 包装类型 [{name}] 删除成功");
                            NotifyLocalConfigDelta(
                                "PACK_TYPE",
                                name,
                                "DELETE",
                                new JObject { ["name"] = name });
                            return true;
                        }
                    }
                    return false;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"删除包装类型失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 获取各包装明细的当前单价（最新未结清记录）及待更新条数，供批量改价对话框使用。
        /// </summary>
        public DataTable GetPackTypePriceSummary()
        {
            var dataTable = new DataTable();
            dataTable.Columns.Add("pack_type", typeof(string));
            dataTable.Columns.Add("current_unit_price", typeof(decimal));
            dataTable.Columns.Add("pending_count", typeof(int));

            try
            {
                using (var connection = new SQLiteConnection(connectionString))
                {
                    connection.Open();
                    string sql = @"
SELECT
    t.pack_type,
    COALESCE((
        SELECT pt.unit_price
        FROM packaging_transactions pt
        WHERE pt.pack_type = t.pack_type
          AND COALESCE(pt.is_settled, 0) = 0
        ORDER BY pt.id DESC
        LIMIT 1
    ), 0) AS current_unit_price,
    COALESCE((
        SELECT COUNT(*)
        FROM packaging_transactions pt
        WHERE pt.pack_type = t.pack_type
          AND COALESCE(pt.is_settled, 0) = 0
    ), 0) AS pending_count
FROM (
    SELECT name AS pack_type FROM pack_types WHERE is_active = 1
    UNION
    SELECT DISTINCT pack_type FROM packaging_transactions
    WHERE pack_type IS NOT NULL AND TRIM(pack_type) != ''
) t
ORDER BY t.pack_type";

                    using (var command = new SQLiteCommand(sql, connection))
                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            string packType = reader["pack_type"]?.ToString()?.Trim();
                            if (string.IsNullOrWhiteSpace(packType))
                                continue;

                            decimal currentPrice = 0m;
                            if (reader["current_unit_price"] != DBNull.Value)
                                decimal.TryParse(reader["current_unit_price"].ToString(), out currentPrice);

                            int pendingCount = 0;
                            if (reader["pending_count"] != DBNull.Value)
                                int.TryParse(reader["pending_count"].ToString(), out pendingCount);

                            dataTable.Rows.Add(packType, currentPrice, pendingCount);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"获取包装明细单价汇总失败: {ex.Message}");
            }

            return dataTable;
        }

        /// <summary>
        /// 批量更新指定包装明细下所有未结清记录的单价，并同步 packaging_items。
        /// </summary>
        public int BatchUpdatePackTypeUnitPrice(string packType, decimal unitPrice)
        {
            if (string.IsNullOrWhiteSpace(packType))
                return 0;

            try
            {
                using (var connection = new SQLiteConnection(connectionString))
                {
                    connection.Open();
                    using (var transaction = connection.BeginTransaction())
                    {
                        string updateTransactionsSql = @"
UPDATE packaging_transactions
SET unit_price = @price,
    total_amount = CASE
        WHEN COALESCE(pack_flag, 'TAKE') = 'RETURN' THEN -(@price * quantity)
        ELSE @price * quantity
    END
WHERE pack_type = @packType
  AND COALESCE(is_settled, 0) = 0";

                        int affected;
                        using (var command = new SQLiteCommand(updateTransactionsSql, connection, transaction))
                        {
                            command.Parameters.AddWithValue("@price", unitPrice);
                            command.Parameters.AddWithValue("@packType", packType.Trim());
                            affected = command.ExecuteNonQuery();
                        }

                        string updateItemsSql = @"
UPDATE packaging_items
SET unit_price = @price,
    total_amount = @price * quantity
WHERE pack_type = @packType
  AND order_no IN (
      SELECT order_no FROM packaging_transactions
      WHERE pack_type = @packType AND COALESCE(is_settled, 0) = 0
  )";

                        using (var command = new SQLiteCommand(updateItemsSql, connection, transaction))
                        {
                            command.Parameters.AddWithValue("@price", unitPrice);
                            command.Parameters.AddWithValue("@packType", packType.Trim());
                            command.ExecuteNonQuery();
                        }

                        transaction.Commit();
                        return affected;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"批量更新包装单价失败 [{packType}]: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// 为指定客户、日期范围内单价为 0 的未结清包装记录补单价，并按包装记账规则重算金额（进包装为负）。
        /// </summary>
        public int UpdateClientZeroPackTypeUnitPrice(
            string clientCode,
            string packType,
            decimal unitPrice,
            DateTime startDate,
            DateTime endDate)
        {
            if (string.IsNullOrWhiteSpace(clientCode) || string.IsNullOrWhiteSpace(packType) || unitPrice <= 0)
                return 0;

            try
            {
                using (var connection = new SQLiteConnection(connectionString))
                {
                    connection.Open();
                    using (var transaction = connection.BeginTransaction())
                    {
                        string updateTransactionsSql = @"
UPDATE packaging_transactions
SET unit_price = @price,
    total_amount = CASE
        WHEN COALESCE(pack_flag, 'TAKE') = 'RETURN' THEN -(@price * quantity)
        ELSE @price * quantity
    END
WHERE client_code = @clientCode
  AND pack_type = @packType
  AND COALESCE(is_settled, 0) = 0
  AND COALESCE(unit_price, 0) = 0
  AND date BETWEEN @startDate AND @endDate";

                        int affected;
                        using (var command = new SQLiteCommand(updateTransactionsSql, connection, transaction))
                        {
                            command.Parameters.AddWithValue("@price", unitPrice);
                            command.Parameters.AddWithValue("@clientCode", clientCode.Trim());
                            command.Parameters.AddWithValue("@packType", packType.Trim());
                            command.Parameters.AddWithValue("@startDate", startDate.Date.ToString("yyyy-MM-dd"));
                            command.Parameters.AddWithValue("@endDate", endDate.Date.ToString("yyyy-MM-dd 23:59:59"));
                            affected = command.ExecuteNonQuery();
                        }

                        string updateItemsSql = @"
UPDATE packaging_items
SET unit_price = @price,
    total_amount = @price * quantity
WHERE pack_type = @packType
  AND order_no IN (
      SELECT order_no FROM packaging_transactions
      WHERE client_code = @clientCode
        AND pack_type = @packType
        AND COALESCE(is_settled, 0) = 0
        AND date BETWEEN @startDate AND @endDate
  )";

                        using (var command = new SQLiteCommand(updateItemsSql, connection, transaction))
                        {
                            command.Parameters.AddWithValue("@price", unitPrice);
                            command.Parameters.AddWithValue("@clientCode", clientCode.Trim());
                            command.Parameters.AddWithValue("@packType", packType.Trim());
                            command.Parameters.AddWithValue("@startDate", startDate.Date.ToString("yyyy-MM-dd"));
                            command.Parameters.AddWithValue("@endDate", endDate.Date.ToString("yyyy-MM-dd 23:59:59"));
                            command.ExecuteNonQuery();
                        }

                        transaction.Commit();
                        return affected;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"客户包装补单价失败 [{clientCode}/{packType}]: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// 查询指定库位+规格下可用于批量销售的入库记录。
        /// </summary>
        public DataTable GetInboundRecordsForBatchSale(string location, string spec)
        {
            var dataTable = new DataTable();
            if (string.IsNullOrWhiteSpace(spec))
                return dataTable;

            string storedLocation = (location ?? string.Empty).Trim();
            string storedSpec = spec.Trim();

            try
            {
                using (var connection = new SQLiteConnection(connectionString))
                {
                    connection.Open();
                    string sql = @"
SELECT id, order_no, client_code, client_name, location, spec, quantity, date
FROM inbound_transactions
WHERE COALESCE(location, '') = @location
  AND spec = @spec
  AND COALESCE(quantity, 0) > 0
ORDER BY date ASC, id ASC";

                    using (var command = new SQLiteCommand(sql, connection))
                    {
                        command.Parameters.AddWithValue("@location", storedLocation);
                        command.Parameters.AddWithValue("@spec", storedSpec);
                        using (var adapter = new SQLiteDataAdapter(command))
                        {
                            adapter.Fill(dataTable);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"获取批量销售入库记录失败 [{storedLocation}/{storedSpec}]: {ex.Message}");
            }

            return dataTable;
        }

        /// <summary>
        /// 获取指定库位+规格的当前库存（入库 − 预售预占 − 销预售出库扣减）。
        /// </summary>
        public int GetCurrentStockByLocationAndSpec(string location, string spec)
        {
            if (string.IsNullOrWhiteSpace(spec))
                return 0;

            string storedLocation = (location ?? string.Empty).Trim();
            string storedSpec = spec.Trim();

            try
            {
                using (var connection = new SQLiteConnection(connectionString))
                {
                    connection.Open();
                    string sql = @"
SELECT
    COALESCE((
        SELECT SUM(COALESCE(quantity, 0))
        FROM inbound_transactions
        WHERE COALESCE(location, '') = @location AND spec = @spec
    ), 0)
  - COALESCE((
        SELECT SUM(
            MAX(0, COALESCE(i.quantity, 0) - COALESCE(i.shipped_quantity, 0))
        )
        FROM presale_bills b
        INNER JOIN presale_items i ON b.bill_no = i.bill_no
        WHERE COALESCE(b.location, '') = @location
          AND i.spec = @spec
          AND b.status != 'CANCELLED'
          AND b.sale_mode = 'PRESALE'
          AND b.status IN ('PRESALE', 'SHIPPED')
    ), 0)
  - COALESCE((
        SELECT SUM(
            CASE
                WHEN COALESCE(i.shipped_quantity, 0) > 0 THEN COALESCE(i.shipped_quantity, 0)
                WHEN b.sale_mode = 'DIRECT_OUT' OR b.status IN ('SHIPPED', 'COMPLETED') THEN COALESCE(i.quantity, 0)
                ELSE 0
            END
        )
        FROM presale_bills b
        INNER JOIN presale_items i ON b.bill_no = i.bill_no
        WHERE COALESCE(b.location, '') = @location
          AND i.spec = @spec
          AND b.status != 'CANCELLED'
          AND (
                b.sale_mode = 'DIRECT_OUT'
                OR b.status IN ('SHIPPED', 'COMPLETED')
              )
    ), 0) AS stock";

                    using (var command = new SQLiteCommand(sql, connection))
                    {
                        command.Parameters.AddWithValue("@location", storedLocation);
                        command.Parameters.AddWithValue("@spec", storedSpec);
                        object result = command.ExecuteScalar();
                        if (result == null || result == DBNull.Value)
                            return 0;
                        return Convert.ToInt32(result);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"获取库位库存失败 [{storedLocation}/{storedSpec}]: {ex.Message}");
                return 0;
            }
        }

        /// <summary>
        /// 按客户计算指定库位+规格下的入库、已售与可售数量（用于批量销售预览与写入）。
        /// 客户键优先 client_code，并通过 clients 表将名称映射到编号。
        /// </summary>
        public DataTable GetBatchSaleAllocationsByClient(string location, string spec)
        {
            return GetBatchSaleAllocationInfo(location, spec).Allocations;
        }

        public BatchSaleAllocationInfo GetBatchSaleAllocationInfo(string location, string spec)
        {
            var info = new BatchSaleAllocationInfo();
            info.Allocations.Columns.Add("client_code", typeof(string));
            info.Allocations.Columns.Add("client_name", typeof(string));
            info.Allocations.Columns.Add("inbound_qty", typeof(int));
            info.Allocations.Columns.Add("sold_qty", typeof(int));
            info.Allocations.Columns.Add("sellable_qty", typeof(int));

            if (string.IsNullOrWhiteSpace(spec))
                return info;

            string storedLocation = (location ?? string.Empty).Trim();
            string storedSpec = spec.Trim();

            try
            {
                using (var connection = new SQLiteConnection(connectionString))
                {
                    connection.Open();
                    string sql = @"
WITH client_directory AS (
    SELECT TRIM(code) AS code, TRIM(name) AS name
    FROM clients
    WHERE TRIM(COALESCE(code, '')) != '' OR TRIM(COALESCE(name, '')) != ''
),
inbound_by_client AS (
    SELECT
        COALESCE(
            NULLIF(TRIM(it.client_code), ''),
            (SELECT d.code FROM client_directory d WHERE d.name = TRIM(it.client_name) LIMIT 1),
            NULLIF(TRIM(it.client_name), '')
        ) AS client_key,
        COALESCE(
            NULLIF(TRIM(it.client_code), ''),
            (SELECT d.code FROM client_directory d WHERE d.name = TRIM(it.client_name) LIMIT 1),
            ''
        ) AS client_code,
        COALESCE(
            NULLIF(TRIM(it.client_name), ''),
            (SELECT d.name FROM client_directory d WHERE d.code = TRIM(it.client_code) LIMIT 1),
            TRIM(it.client_name)
        ) AS client_name,
        SUM(COALESCE(it.quantity, 0)) AS inbound_qty
    FROM inbound_transactions it
    WHERE COALESCE(it.location, '') = @location
      AND it.spec = @spec
      AND COALESCE(it.quantity, 0) > 0
    GROUP BY client_key
),
sales_by_client AS (
    SELECT
        COALESCE(
            NULLIF(TRIM(st.client_code), ''),
            (SELECT d.code FROM client_directory d WHERE d.name = TRIM(st.client_name) LIMIT 1),
            NULLIF(TRIM(st.client_name), '')
        ) AS client_key,
        SUM(COALESCE(st.quantity, 0)) AS sold_qty
    FROM sales_transactions st
    WHERE COALESCE(st.location, '') = @location
      AND st.spec = @spec
    GROUP BY client_key
)
SELECT
    i.client_code,
    i.client_name,
    i.inbound_qty,
    COALESCE(s.sold_qty, 0) AS sold_qty,
    i.inbound_qty - COALESCE(s.sold_qty, 0) AS sellable_qty
FROM inbound_by_client i
LEFT JOIN sales_by_client s ON i.client_key = s.client_key
WHERE i.inbound_qty - COALESCE(s.sold_qty, 0) > 0
ORDER BY i.client_name ASC";

                    using (var command = new SQLiteCommand(sql, connection))
                    {
                        command.Parameters.AddWithValue("@location", storedLocation);
                        command.Parameters.AddWithValue("@spec", storedSpec);
                        using (var adapter = new SQLiteDataAdapter(command))
                        {
                            adapter.Fill(info.Allocations);
                        }
                    }

                    string orphanSql = @"
WITH client_directory AS (
    SELECT TRIM(code) AS code, TRIM(name) AS name
    FROM clients
    WHERE TRIM(COALESCE(code, '')) != '' OR TRIM(COALESCE(name, '')) != ''
),
inbound_by_client AS (
    SELECT
        COALESCE(
            NULLIF(TRIM(it.client_code), ''),
            (SELECT d.code FROM client_directory d WHERE d.name = TRIM(it.client_name) LIMIT 1),
            NULLIF(TRIM(it.client_name), '')
        ) AS client_key
    FROM inbound_transactions it
    WHERE COALESCE(it.location, '') = @location
      AND it.spec = @spec
      AND COALESCE(it.quantity, 0) > 0
    GROUP BY client_key
),
sales_by_client AS (
    SELECT
        COALESCE(
            NULLIF(TRIM(st.client_code), ''),
            (SELECT d.code FROM client_directory d WHERE d.name = TRIM(st.client_name) LIMIT 1),
            NULLIF(TRIM(st.client_name), '')
        ) AS client_key,
        SUM(COALESCE(st.quantity, 0)) AS sold_qty
    FROM sales_transactions st
    WHERE COALESCE(st.location, '') = @location
      AND st.spec = @spec
    GROUP BY client_key
)
SELECT COALESCE(SUM(s.sold_qty), 0)
FROM sales_by_client s
WHERE NOT EXISTS (
    SELECT 1 FROM inbound_by_client i WHERE i.client_key = s.client_key
)";

                    using (var orphanCmd = new SQLiteCommand(orphanSql, connection))
                    {
                        orphanCmd.Parameters.AddWithValue("@location", storedLocation);
                        orphanCmd.Parameters.AddWithValue("@spec", storedSpec);
                        object orphanResult = orphanCmd.ExecuteScalar();
                        if (orphanResult != null && orphanResult != DBNull.Value)
                            info.OrphanSoldQty = Convert.ToInt32(orphanResult);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"获取批量销售客户分摊失败 [{storedLocation}/{storedSpec}]: {ex.Message}");
            }

            return info;
        }

        /// <summary>
        /// 按客户待报数量批量生成报价单（统一单价，每个客户一条报账记录，不影响预售库存）。
        /// </summary>
        public BatchSalesResult BatchCreateSalesFromInbound(string location, string spec, decimal unitPrice, string handler, string creator)
        {
            var result = new BatchSalesResult();
            if (string.IsNullOrWhiteSpace(spec))
            {
                result.ErrorMessage = "规格不能为空。";
                return result;
            }

            string storedLocation = (location ?? string.Empty).Trim();
            string storedSpec = spec.Trim();
            string storedHandler = (handler ?? string.Empty).Trim();
            string storedCreator = (creator ?? Environment.UserName).Trim();

            BatchSaleAllocationInfo allocationInfo = GetBatchSaleAllocationInfo(storedLocation, storedSpec);
            DataTable allocations = allocationInfo.Allocations;
            if (allocations.Rows.Count == 0)
            {
                result.ErrorMessage = "当前没有待报价客户。";
                return result;
            }

            int totalSellable = 0;
            foreach (DataRow row in allocations.Rows)
            {
                if (row["sellable_qty"] != DBNull.Value)
                    totalSellable += Convert.ToInt32(row["sellable_qty"]);
            }

            if (totalSellable <= 0)
            {
                result.ErrorMessage = "当前没有待报价客户。";
                return result;
            }

            try
            {
                using (var connection = new SQLiteConnection(connectionString))
                {
                    connection.Open();
                    EnsureSourceColumns(connection, "sales_transactions");
                    using (var transaction = connection.BeginTransaction())
                    {
                        string insertSql = @"
INSERT INTO sales_transactions
(order_no, client_code, client_name, location, date, spec, quantity, unit_price, total_amount, handler, creator, source_device_id, source_record_id)
VALUES (@orderNo, @clientCode, @clientName, @location, @date, @spec, @quantity, @unitPrice, @totalAmount, @handler, @creator, @sourceDeviceId, @sourceRecordId)";

                        int index = 0;
                        foreach (DataRow row in allocations.Rows)
                        {
                            int quantity = row["sellable_qty"] != DBNull.Value ? Convert.ToInt32(row["sellable_qty"]) : 0;
                            if (quantity <= 0)
                                continue;

                            string clientCode = row["client_code"]?.ToString() ?? string.Empty;
                            string clientName = row["client_name"]?.ToString() ?? string.Empty;
                            decimal totalAmount = quantity * unitPrice;
                            string orderNo = "XS" + DateTime.Now.ToString("yyyyMMddHHmmssfff") + index.ToString("D3");

                            using (var command = new SQLiteCommand(insertSql, connection, transaction))
                            {
                                command.Parameters.AddWithValue("@orderNo", orderNo);
                                command.Parameters.AddWithValue("@clientCode", clientCode);
                                command.Parameters.AddWithValue("@clientName", clientName);
                                command.Parameters.AddWithValue("@location", storedLocation);
                                command.Parameters.AddWithValue("@date", DateTime.Now.ToString("yyyy-MM-dd"));
                                command.Parameters.AddWithValue("@spec", storedSpec);
                                command.Parameters.AddWithValue("@quantity", quantity);
                                command.Parameters.AddWithValue("@unitPrice", unitPrice);
                                command.Parameters.AddWithValue("@totalAmount", totalAmount);
                                command.Parameters.AddWithValue("@handler", storedHandler);
                                command.Parameters.AddWithValue("@creator", storedCreator);
                                command.Parameters.AddWithValue("@sourceDeviceId", string.Empty);
                                command.Parameters.AddWithValue("@sourceRecordId", string.Empty);
                                command.ExecuteNonQuery();
                            }

                            result.SuccessCount++;
                            result.TotalQuantity += quantity;
                            result.TotalAmount += totalAmount;
                            index++;
                        }

                        transaction.Commit();
                    }
                }

                result.Success = result.SuccessCount > 0;
                if (!result.Success)
                    result.ErrorMessage = "没有可生成的报价单。";
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = $"批量报价失败: {ex.Message}";
                Console.WriteLine(result.ErrorMessage);
            }

            return result;
        }

        #endregion


        #region 库位管理方法
        // 在 DatabaseManager.cs 中添加这个方法
        public DataTable GetActiveLocations()
        {
            var dataTable = new DataTable();
            try
            {
                using (var connection = new SQLiteConnection(connectionString))
                {
                    connection.Open();
                    var command = new SQLiteCommand(@"
                SELECT name, description 
                FROM locations 
                WHERE status = 1  -- 只返回启用的库位
                ORDER BY name", connection);

                    var adapter = new SQLiteDataAdapter(command);
                    adapter.Fill(dataTable);

                    Console.WriteLine($"[GetActiveLocations] 获取到 {dataTable.Rows.Count} 个可用库位");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"获取可用库位失败: {ex.Message}");
            }
            return dataTable;
        }
        public DataTable GetAllLocations()
        {
            var dataTable = new DataTable();
            try
            {
                using (var connection = new SQLiteConnection(connectionString))
                {
                    connection.Open();
                    var command = new SQLiteCommand(@"
                SELECT name, description, status 
                FROM locations 
                ORDER BY status DESC, name", connection);  // 启用的排在前面

                    var adapter = new SQLiteDataAdapter(command);
                    adapter.Fill(dataTable);

                    Console.WriteLine($"[GetAllLocations] 获取到 {dataTable.Rows.Count} 条库位记录");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"获取库位数据失败: {ex.Message}");
            }
            return dataTable;
        }

        public DataTable SearchLocations(string keyword)
        {
            var dataTable = new DataTable();
            try
            {
                using (var connection = new SQLiteConnection(connectionString))
                {
                    connection.Open();
                    var command = new SQLiteCommand(@"
                SELECT name, description, status 
                FROM locations 
                WHERE name LIKE @keyword 
                ORDER BY status DESC, name", connection);

                    command.Parameters.AddWithValue("@keyword", $"%{keyword}%");
                    var adapter = new SQLiteDataAdapter(command);
                    adapter.Fill(dataTable);

                    Console.WriteLine($"[SearchLocations] 搜索到 {dataTable.Rows.Count} 条记录");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"搜索库位失败: {ex.Message}");
            }
            return dataTable;
        }

        public bool AddLocation(string name, string description = "")
        {
            try
            {
                // 先检查是否存在
                string checkSql = "SELECT COUNT(*) FROM locations WHERE name = @name";
                int exists = Convert.ToInt32(ExecuteScalar(checkSql, new Dictionary<string, object>
        {
            { "@name", name }
        }));

                if (exists > 0)
                {
                    Console.WriteLine($"⏭️ 库位 [{name}] 已存在，跳过");
                    return true; // 返回成功，但实际没插入
                }

                using (var connection = new SQLiteConnection(connectionString))
                {
                    connection.Open();
                    var command = new SQLiteCommand(@"
                INSERT INTO locations (name, description, status) 
                VALUES (@name, @description, 1)", connection);

                    command.Parameters.AddWithValue("@name", name);
                    command.Parameters.AddWithValue("@description", description ?? "");
                    bool inserted = command.ExecuteNonQuery() > 0;
                    if (inserted)
                    {
                        var payload = new JObject
                        {
                            ["code"] = name,
                            ["name"] = name,
                            ["description"] = description ?? "",
                            ["enabled"] = true
                        };
                        NotifyLocalConfigDelta("LOCATION", name, "UPSERT", payload);
                    }
                    return inserted;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"添加库位失败: {ex.Message}");
                return false;
            }
        }

        public bool ToggleLocationStatus(string name)
        {
            try
            {
                using (var connection = new SQLiteConnection(connectionString))
                {
                    connection.Open();

                    // 先获取当前状态
                    var getStatusCommand = new SQLiteCommand(@"
                SELECT status FROM locations WHERE name = @name", connection);
                    getStatusCommand.Parameters.AddWithValue("@name", name);

                    object result = getStatusCommand.ExecuteScalar();
                    if (result == null || result == DBNull.Value)
                        return false;

                    int currentStatus = Convert.ToInt32(result);
                    int newStatus = currentStatus == 1 ? 0 : 1;

                    // 更新状态
                    var updateCommand = new SQLiteCommand(@"
                UPDATE locations 
                SET status = @newStatus 
                WHERE name = @name", connection);

                    updateCommand.Parameters.AddWithValue("@name", name);
                    updateCommand.Parameters.AddWithValue("@newStatus", newStatus);

                    int rowsAffected = updateCommand.ExecuteNonQuery();
                    Console.WriteLine($">>> 切换库位 [{name}] 状态: {currentStatus} -> {newStatus}, 影响行数: {rowsAffected}");

                    if (rowsAffected > 0)
                    {
                        using (var readRow = new SQLiteCommand(
                            "SELECT name, code, description, status FROM locations WHERE name = @name", connection))
                        {
                            readRow.Parameters.AddWithValue("@name", name);
                            using (var reader = readRow.ExecuteReader())
                            {
                                if (reader.Read())
                                {
                                    string locName = reader["name"]?.ToString() ?? name;
                                    string locCode = reader["code"]?.ToString();
                                    if (string.IsNullOrWhiteSpace(locCode)) locCode = locName;
                                    var payload = new JObject
                                    {
                                        ["code"] = locCode,
                                        ["name"] = locName,
                                        ["description"] = reader["description"]?.ToString() ?? "",
                                        ["enabled"] = Convert.ToInt32(reader["status"]) != 0
                                    };
                                    NotifyLocalConfigDelta("LOCATION", locCode, "UPSERT", payload);
                                }
                            }
                        }
                    }

                    return rowsAffected > 0;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"切换库位状态失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 删除库位（物理删除）
        /// </summary>
        public bool DeleteLocation(string name)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(name))
                    return false;

                string locationName = name.Trim();

                using (var connection = new SQLiteConnection(connectionString))
                {
                    connection.Open();

                    string locationCode = locationName;
                    using (var getRow = new SQLiteCommand(
                        "SELECT name, code FROM locations WHERE name = @name", connection))
                    {
                        getRow.Parameters.AddWithValue("@name", locationName);
                        using (var reader = getRow.ExecuteReader())
                        {
                            if (!reader.Read())
                            {
                                Console.WriteLine($">>> [DeleteLocation] 库位不存在: {locationName}");
                                return false;
                            }
                            locationName = reader["name"]?.ToString()?.Trim() ?? locationName;
                            string code = reader["code"]?.ToString()?.Trim();
                            if (!string.IsNullOrEmpty(code))
                                locationCode = code;
                        }
                    }

                    int referenceCount = CountLocationReferences(connection, locationName);
                    if (referenceCount > 0)
                    {
                        Console.WriteLine($">>> [DeleteLocation] 库位 [{locationName}] 被 {referenceCount} 条业务记录引用，不能删除");
                        return false;
                    }

                    using (var command = new SQLiteCommand("DELETE FROM locations WHERE name = @name", connection))
                    {
                        command.Parameters.AddWithValue("@name", locationName);
                        int result = command.ExecuteNonQuery();
                        if (result > 0)
                        {
                            NotifyLocalConfigDelta("LOCATION", locationCode, "DELETE", new JObject
                            {
                                ["code"] = locationCode,
                                ["name"] = locationName
                            });
                            Console.WriteLine($">>> [DeleteLocation] 库位 [{locationName}] 删除成功");
                            return true;
                        }
                    }
                    return false;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"删除库位失败: {ex.Message}");
                return false;
            }
        }

        private static int CountLocationReferences(SQLiteConnection connection, string locationName)
        {
            using (var command = new SQLiteCommand(@"
                SELECT COUNT(*) FROM (
                    SELECT location FROM inbound_transactions WHERE location = @name
                    UNION ALL
                    SELECT location FROM sales_transactions WHERE location = @name
                    UNION ALL
                    SELECT location FROM presale_bills WHERE location = @name
                )", connection))
            {
                command.Parameters.AddWithValue("@name", locationName ?? string.Empty);
                return Convert.ToInt32(command.ExecuteScalar());
            }
        }

        /// <summary>增量同步：插入或更新库位（含 status / code），供 AutoSyncServer ApplyConfigOperation 使用。</summary>
        public bool UpsertLocationForSync(string name, string code, string description, int status)
        {
            try
            {
                string effectiveName = name?.Trim() ?? "";
                if (string.IsNullOrEmpty(effectiveName)) return false;
                string effectiveCode = string.IsNullOrWhiteSpace(code) ? effectiveName : code.Trim();

                using (var connection = new SQLiteConnection(connectionString))
                {
                    connection.Open();
                    using (var checkCmd = new SQLiteCommand(@"
                        SELECT id FROM locations
                        WHERE name = @name
                           OR (code IS NOT NULL AND code != '' AND code = @code)", connection))
                    {
                        checkCmd.Parameters.AddWithValue("@name", effectiveName);
                        checkCmd.Parameters.AddWithValue("@code", effectiveCode);
                        var idObj = checkCmd.ExecuteScalar();
                        if (idObj != null && idObj != DBNull.Value)
                        {
                            int id = Convert.ToInt32(idObj);
                            using (var up = new SQLiteCommand(@"
                                UPDATE locations
                                SET name = @name,
                                    description = @desc,
                                    status = @st,
                                    code = @code
                                WHERE id = @id", connection))
                            {
                                up.Parameters.AddWithValue("@name", effectiveName);
                                up.Parameters.AddWithValue("@desc", description ?? "");
                                up.Parameters.AddWithValue("@st", status);
                                up.Parameters.AddWithValue("@code", effectiveCode);
                                up.Parameters.AddWithValue("@id", id);
                                return up.ExecuteNonQuery() > 0;
                            }
                        }
                    }

                    using (var ins = new SQLiteCommand(@"
                        INSERT INTO locations (name, description, status, code)
                        VALUES (@name, @desc, @st, @code)", connection))
                    {
                        ins.Parameters.AddWithValue("@name", effectiveName);
                        ins.Parameters.AddWithValue("@desc", description ?? "");
                        ins.Parameters.AddWithValue("@st", status);
                        ins.Parameters.AddWithValue("@code", effectiveCode);
                        return ins.ExecuteNonQuery() > 0;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"UpsertLocationForSync 失败: {ex.Message}");
                return false;
            }
        }

        #endregion

        #region 经手人管理方法
        public DataTable GetAllHandlers()
        {
            var dataTable = new DataTable();
            try
            {
                Console.WriteLine($">>> [GetAllHandlers] 开始查询经手人");

                using (var connection = new SQLiteConnection(connectionString))
                {
                    connection.Open();

                    var command = new SQLiteCommand(@"
                SELECT name 
                FROM handlers 
                WHERE status = 1 
                ORDER BY name", connection);

                    var adapter = new SQLiteDataAdapter(command);
                    adapter.Fill(dataTable);
                    Console.WriteLine($">>> [GetAllHandlers] 查询完成: {dataTable.Rows.Count} 行");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($">>> [GetAllHandlers] 异常: {ex.Message}");
            }
            return dataTable;
        }

        public DataTable GetAllHandlersWithDetails()
{
    var dataTable = new DataTable();
    try
    {
        Console.WriteLine($">>> [GetAllHandlersWithDetails] 开始查询");

        using (var connection = new SQLiteConnection(connectionString))
        {
            connection.Open();

            // ✅ 修改：显示所有经手人，包括禁用的（status=0）
            // 按 status 降序（启用的在前面），再按 name 排序
            var command = new SQLiteCommand(@"
                SELECT id, name, status 
                FROM handlers 
                ORDER BY status DESC, name", connection);  // status=1 排在 status=0 前面

            var adapter = new SQLiteDataAdapter(command);
            adapter.Fill(dataTable);
            Console.WriteLine($">>> [GetAllHandlersWithDetails] 查询完成: {dataTable.Rows.Count} 行");
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($">>> [GetAllHandlersWithDetails] 异常: {ex.Message}");
    }
    return dataTable;
}

        public DataTable SearchHandlers(string keyword)
        {
            var dataTable = new DataTable();
            try
            {
                using (var connection = new SQLiteConnection(connectionString))
                {
                    connection.Open();

                    // ❌ 当前：只搜索启用的，但后续逻辑可能有问题
                    // var command = new SQLiteCommand(@"
                    //    SELECT name 
                    //    FROM handlers 
                    //    WHERE status = 1 
                    //    AND name LIKE @keyword 
                    //    ORDER BY name", connection);

                    // ✅ 修改后：明确只搜索启用的
                    var command = new SQLiteCommand(@"
                SELECT id, name, status 
                FROM handlers 
                WHERE status = 1 
                AND name LIKE @keyword 
                ORDER BY name", connection);

                    command.Parameters.AddWithValue("@keyword", $"%{keyword}%");
                    var adapter = new SQLiteDataAdapter(command);
                    adapter.Fill(dataTable);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"搜索经手人失败: {ex.Message}");
            }
            return dataTable;
        }

        private void NotifyOperatorSyncRow(string name)
        {
            try
            {
                using (var connection = new SQLiteConnection(connectionString))
                {
                    connection.Open();
                    using (var cmd = new SQLiteCommand(
                        "SELECT name, code, status FROM handlers WHERE name = @name", connection))
                    {
                        cmd.Parameters.AddWithValue("@name", name);
                        using (var r = cmd.ExecuteReader())
                        {
                            if (!r.Read()) return;
                            string opName = r["name"]?.ToString() ?? name;
                            string opCode = r["code"]?.ToString();
                            if (string.IsNullOrWhiteSpace(opCode)) opCode = opName;
                            bool enabled = Convert.ToInt32(r["status"]) != 0;
                            var payload = new JObject
                            {
                                ["code"] = opCode,
                                ["name"] = opName,
                                ["enabled"] = enabled
                            };
                            NotifyLocalConfigDelta("OPERATOR", opCode, "UPSERT", payload);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($">>> NotifyOperatorSyncRow 异常: {ex.Message}");
            }
        }

        public bool AddHandler(string name)
        {
            try
            {
                Console.WriteLine($">>> [AddHandler] 开始添加: '{name}'");

                using (var connection = new SQLiteConnection(connectionString))
                {
                    connection.Open();
                    Console.WriteLine($">>> [AddHandler] 数据库连接成功");

                    using (var transaction = connection.BeginTransaction())
                    {
                        try
                        {
                            var checkCmd = new SQLiteCommand(
                                "SELECT COUNT(*) FROM handlers WHERE name = @name",
                                connection);
                            checkCmd.Parameters.AddWithValue("@name", name);
                            var existingCount = Convert.ToInt32(checkCmd.ExecuteScalar());
                            Console.WriteLine($">>> [AddHandler] 同名经手人数量: {existingCount}");

                            if (existingCount > 0)
                            {
                                Console.WriteLine($">>> [AddHandler] 经手人已存在，启用它");
                                var enableCmd = new SQLiteCommand(
                                    "UPDATE handlers SET status = 1 WHERE name = @name",
                                    connection);
                                enableCmd.Parameters.AddWithValue("@name", name);
                                int enableResult = enableCmd.ExecuteNonQuery();
                                Console.WriteLine($">>> [AddHandler] 启用结果: {enableResult} 行受影响");

                                transaction.Commit();
                                if (enableResult > 0) NotifyOperatorSyncRow(name);
                                return enableResult > 0;
                            }
                            else
                            {
                                var command = new SQLiteCommand(@"
                            INSERT INTO handlers (name, status) 
                            VALUES (@name, 1)", connection);

                                command.Parameters.AddWithValue("@name", name);
                                int result = command.ExecuteNonQuery();
                                Console.WriteLine($">>> [AddHandler] 插入结果: {result} 行受影响");

                                transaction.Commit();
                                Console.WriteLine($">>> [AddHandler] 事务提交成功");

                                var vacuumCmd = new SQLiteCommand("VACUUM", connection);
                                vacuumCmd.ExecuteNonQuery();
                                Console.WriteLine($">>> [AddHandler] 数据库压缩完成");

                                if (result > 0) NotifyOperatorSyncRow(name);
                                return result > 0;
                            }
                        }
                        catch (Exception ex)
                        {
                            transaction.Rollback();
                            Console.WriteLine($">>> [AddHandler] 事务回滚，异常: {ex.ToString()}");
                            return false;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($">>> [AddHandler] 异常: {ex.ToString()}");
                return false;
            }
        }

        // 重载方法，兼容两种调用方式
        public bool UpdateHandler(string oldName, string newName)
        {
            return UpdateHandler(oldName, newName, true);
        }

        public bool UpdateHandler(string oldName, string newName, bool isActive)
        {
            try
            {
                Console.WriteLine($">>> [UpdateHandler] 开始更新: '{oldName}' -> '{newName}'");

                using (var connection = new SQLiteConnection(connectionString))
                {
                    connection.Open();

                    var command = new SQLiteCommand(@"
                UPDATE handlers 
                SET name = @newName, status = @isActive 
                WHERE name = @oldName", connection);

                    command.Parameters.AddWithValue("@newName", newName);
                    command.Parameters.AddWithValue("@oldName", oldName);
                    command.Parameters.AddWithValue("@isActive", isActive ? 1 : 0);
                    int result = command.ExecuteNonQuery();
                    Console.WriteLine($">>> [UpdateHandler] 执行结果: {result} 行受影响");

                    if (result > 0)
                    {
                        if (!string.Equals(oldName, newName, StringComparison.Ordinal))
                        {
                            NotifyLocalConfigDelta("OPERATOR", oldName, "DELETE", new JObject
                            {
                                ["code"] = oldName,
                                ["name"] = oldName
                            });
                        }
                        NotifyOperatorSyncRow(newName);
                    }

                    return result > 0;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($">>> [UpdateHandler] 异常: {ex.Message}");
                return false;
            }
        }

        // 根据ID更新经手人
        public bool UpdateHandlerById(int id, string name, string phone, bool isActive)
        {
            try
            {
                using (var connection = new SQLiteConnection(connectionString))
                {
                    connection.Open();

                    string oldName = null;
                    using (var getCmd = new SQLiteCommand("SELECT name FROM handlers WHERE id = @id", connection))
                    {
                        getCmd.Parameters.AddWithValue("@id", id);
                        oldName = getCmd.ExecuteScalar()?.ToString();
                    }

                    var command = new SQLiteCommand(@"
                UPDATE handlers 
                SET name = @name, status = @isActive 
                WHERE id = @id", connection);

                    command.Parameters.AddWithValue("@id", id);
                    command.Parameters.AddWithValue("@name", name);
                    command.Parameters.AddWithValue("@isActive", isActive ? 1 : 0);
                    int result = command.ExecuteNonQuery();

                    if (result > 0)
                    {
                        if (!string.IsNullOrWhiteSpace(oldName) &&
                            !string.Equals(oldName, name, StringComparison.Ordinal))
                        {
                            NotifyLocalConfigDelta("OPERATOR", oldName, "DELETE", new JObject
                            {
                                ["code"] = oldName,
                                ["name"] = oldName
                            });
                        }
                        NotifyOperatorSyncRow(name);
                    }

                    return result > 0;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"更新经手人失败: {ex.Message}");
                return false;
            }
        }

        public bool DeleteHandler(string name)
        {
            try
            {
                Console.WriteLine($">>> [DeleteHandler] 开始物理删除: '{name}'");

                // 第一步：检查该经手人是否被业务单据引用
                string checkSql = @"
            SELECT COUNT(*) as ref_count FROM (
                SELECT handler FROM inbound_transactions WHERE handler = @name
                UNION ALL
                SELECT handler FROM sales_transactions WHERE handler = @name
                UNION ALL
                SELECT handler FROM packaging_transactions WHERE handler = @name
                UNION ALL
                SELECT handler FROM deductions WHERE handler = @name
                UNION ALL
                SELECT handler FROM advances WHERE handler = @name
            )";

                DataTable checkResult = ExecuteQuery(checkSql, new Dictionary<string, object>
        {
            { "@name", name }
        });

                if (checkResult.Rows.Count > 0)
                {
                    int refCount = Convert.ToInt32(checkResult.Rows[0]["ref_count"]);
                    if (refCount > 0)
                    {
                        Console.WriteLine($">>> [DeleteHandler] 经手人 '{name}' 被 {refCount} 条业务记录引用，不能物理删除");
                        return false;
                    }
                }

                // 第二步：如果没有被引用，执行物理删除
                using (var connection = new SQLiteConnection(connectionString))
                {
                    connection.Open();

                    var command = new SQLiteCommand(@"
                DELETE FROM handlers                     -- ✅ 改为 DELETE
                WHERE name = @name", connection); 
        
            command.Parameters.AddWithValue("@name", name);
                    int result = command.ExecuteNonQuery();
                    Console.WriteLine($">>> [DeleteHandler] 物理删除结果: {result} 行受影响");

                    if (result > 0)
                    {
                        NotifyLocalConfigDelta("OPERATOR", name, "DELETE", new JObject
                        {
                            ["code"] = name,
                            ["name"] = name
                        });
                    }

                    return result > 0;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($">>> [DeleteHandler] 异常: {ex.Message}");
                return false;
            }
        }

        public bool ToggleHandlerStatus(string name)
        {
            try
            {
                Console.WriteLine($">>> [ToggleHandlerStatus] 开始切换状态: '{name}'");

                using (var connection = new SQLiteConnection(connectionString))
                {
                    connection.Open();

                    var getStatusCmd = new SQLiteCommand(
                        "SELECT status FROM handlers WHERE name = @name",
                        connection);
                    getStatusCmd.Parameters.AddWithValue("@name", name);
                    var currentStatus = getStatusCmd.ExecuteScalar();

                    if (currentStatus == null)
                    {
                        Console.WriteLine($">>> [ToggleHandlerStatus] 经手人不存在: '{name}'");
                        return false;
                    }

                    int newStatus = (Convert.ToInt32(currentStatus) == 1) ? 0 : 1;
                    Console.WriteLine($">>> [ToggleHandlerStatus] 当前状态: {currentStatus}, 新状态: {newStatus}");

                    var updateCmd = new SQLiteCommand(
                        "UPDATE handlers SET status = @status WHERE name = @name",
                        connection);
                    updateCmd.Parameters.AddWithValue("@status", newStatus);
                    updateCmd.Parameters.AddWithValue("@name", name);

                    int result = updateCmd.ExecuteNonQuery();
                    Console.WriteLine($">>> [ToggleHandlerStatus] 执行结果: {result} 行受影响");

                    if (result > 0) NotifyOperatorSyncRow(name);
                    return result > 0;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($">>> [ToggleHandlerStatus] 异常: {ex.Message}");
                return false;
            }
        }
        #endregion

        #region 业务记录方法
        public bool SaveInboundRecord(string orderNo, string clientCode, string clientName, string location,
                             DateTime date, string spec, int quantity, decimal unitPrice,
                             decimal totalAmount, string handler, string creator,
                             string sourceDeviceId = "", string sourceRecordId = "")
        {
            return SaveInboundRecord(orderNo, clientCode, clientName, location, date, spec, quantity, unitPrice,
                totalAmount, handler, creator, sourceDeviceId, sourceRecordId, out _);
        }

        public bool SaveInboundRecord(string orderNo, string clientCode, string clientName, string location,
                             DateTime date, string spec, int quantity, decimal unitPrice,
                             decimal totalAmount, string handler, string creator,
                             string sourceDeviceId, string sourceRecordId, out string errorMessage)
        {
            errorMessage = "";
            try
            {
                using (var connection = new SQLiteConnection(connectionString))
                {
                    connection.Open();
                    EnsureSourceColumns(connection, "inbound_transactions");

                    string storedOrderNo = (orderNo ?? "").Trim();
                    string trimmedSourceRecordId = string.IsNullOrWhiteSpace(sourceRecordId) ? "" : sourceRecordId.Trim();
                    string trimmedSourceDeviceId = string.IsNullOrWhiteSpace(sourceDeviceId) ? "" : sourceDeviceId.Trim();

                    if (!string.IsNullOrEmpty(trimmedSourceRecordId) && ExistsInboundBySourceRecordId(trimmedSourceRecordId))
                        return true;

                    if (ExistsInboundByBusinessKey(connection, storedOrderNo, spec, trimmedSourceDeviceId))
                    {
                        errorMessage = $"同一入库单已存在相同规格({spec})记录，请勿重复提交";
                        return false;
                    }

                    var command = new SQLiteCommand(@"
                INSERT OR IGNORE INTO inbound_transactions 
                (order_no, client_code, client_name, location, date, spec, quantity, unit_price, total_amount, handler, creator, source_device_id, source_record_id, created_time, updated_at)
                VALUES (@orderNo, @clientCode, @clientName, @location, @date, @spec, @quantity, @unitPrice, @totalAmount, @handler, @creator, @sourceDeviceId, @sourceRecordId, datetime('now', 'localtime'), datetime('now', 'localtime'))", connection);

                    command.Parameters.AddWithValue("@orderNo", storedOrderNo);
                    command.Parameters.AddWithValue("@clientCode", clientCode ?? "");
                    command.Parameters.AddWithValue("@clientName", clientName ?? "");
                    command.Parameters.AddWithValue("@location", location ?? "");
                    command.Parameters.AddWithValue("@date", date.ToString("yyyy-MM-dd"));
                    command.Parameters.AddWithValue("@spec", spec ?? "");
                    command.Parameters.AddWithValue("@quantity", quantity);
                    command.Parameters.AddWithValue("@unitPrice", unitPrice);
                    command.Parameters.AddWithValue("@totalAmount", totalAmount);
                    command.Parameters.AddWithValue("@handler", handler ?? "");
                    command.Parameters.AddWithValue("@creator", creator ?? "");
                    command.Parameters.AddWithValue("@sourceDeviceId", string.IsNullOrWhiteSpace(trimmedSourceDeviceId) ? (object)DBNull.Value : trimmedSourceDeviceId);
                    command.Parameters.AddWithValue("@sourceRecordId", string.IsNullOrWhiteSpace(trimmedSourceRecordId) ? (object)DBNull.Value : trimmedSourceRecordId);

                    if (command.ExecuteNonQuery() > 0)
                        return true;

                    if (!string.IsNullOrEmpty(trimmedSourceRecordId) && ExistsInboundBySourceRecordId(trimmedSourceRecordId))
                        return true;

                    if (ExistsInboundByBusinessKey(connection, storedOrderNo, spec, trimmedSourceDeviceId))
                    {
                        errorMessage = $"同一入库单已存在相同规格({spec})记录，请勿重复提交";
                        return false;
                    }

                    if (TableDdlHasUniquePair(connection, "inbound_transactions", "order_no", "spec") &&
                        TryGetOrderSpecLegacyConflict(connection, "inbound_transactions", storedOrderNo, spec, trimmedSourceDeviceId, out string existingDeviceId))
                    {
                        errorMessage =
                            $"该单号({storedOrderNo})下规格「{spec}」已存在（来源设备: {existingDeviceId}）。" +
                            "不同手持机应可并存，请重启桌面端以完成数据库升级。";
                        return false;
                    }

                    errorMessage = "入库保存失败，请检查数据完整性";
                    return false;
                }
            }
            catch (Exception ex)
            {
                errorMessage = $"保存入库记录异常: {ex.Message}";
                Console.WriteLine(errorMessage);
                return false;
            }
        }

        public bool SaveSalesRecord(string orderNo, string clientCode, string clientName, string location,
                                   DateTime date, string spec, int quantity, decimal unitPrice,
                                   decimal totalAmount, string handler, string creator,
                                   string sourceDeviceId = "", string sourceRecordId = "")
        {
            return SaveSalesRecord(orderNo, clientCode, clientName, location, date, spec, quantity, unitPrice,
                totalAmount, handler, creator, sourceDeviceId, sourceRecordId, out _);
        }

        public bool SaveSalesRecord(string orderNo, string clientCode, string clientName, string location,
                                   DateTime date, string spec, int quantity, decimal unitPrice,
                                   decimal totalAmount, string handler, string creator,
                                   string sourceDeviceId, string sourceRecordId, out string errorMessage)
        {
            errorMessage = "";
            try
            {
                using (var connection = new SQLiteConnection(connectionString))
                {
                    connection.Open();
                    EnsureSourceColumns(connection, "sales_transactions");

                    string storedOrderNo = (orderNo ?? "").Trim();
                    string trimmedSourceRecordId = string.IsNullOrWhiteSpace(sourceRecordId) ? "" : sourceRecordId.Trim();
                    string trimmedSourceDeviceId = string.IsNullOrWhiteSpace(sourceDeviceId) ? "" : sourceDeviceId.Trim();

                    if (!string.IsNullOrEmpty(trimmedSourceRecordId) && ExistsSalesBySourceRecordId(trimmedSourceRecordId))
                        return true;

                    if (ExistsSalesByBusinessKey(connection, storedOrderNo, spec, trimmedSourceDeviceId))
                    {
                        errorMessage = $"同一销售单已存在相同规格({spec})记录，请勿重复提交";
                        return false;
                    }

                    var command = new SQLiteCommand(@"
                INSERT OR IGNORE INTO sales_transactions 
                (order_no, client_code, client_name, location, date, spec, quantity, unit_price, total_amount, handler, creator, source_device_id, source_record_id, created_time, updated_at)
                VALUES (@orderNo, @clientCode, @clientName, @location, @date, @spec, @quantity, @unitPrice, @totalAmount, @handler, @creator, @sourceDeviceId, @sourceRecordId, datetime('now', 'localtime'), datetime('now', 'localtime'))", connection);

                    command.Parameters.AddWithValue("@orderNo", storedOrderNo);
                    command.Parameters.AddWithValue("@clientCode", clientCode ?? (object)DBNull.Value);
                    command.Parameters.AddWithValue("@clientName", clientName ?? (object)DBNull.Value);
                    command.Parameters.AddWithValue("@location", location ?? (object)DBNull.Value);
                    command.Parameters.AddWithValue("@date", date.ToString("yyyy-MM-dd"));
                    command.Parameters.AddWithValue("@spec", spec ?? "");
                    command.Parameters.AddWithValue("@quantity", quantity);
                    command.Parameters.AddWithValue("@unitPrice", unitPrice);
                    command.Parameters.AddWithValue("@totalAmount", totalAmount);
                    command.Parameters.AddWithValue("@handler", handler ?? (object)DBNull.Value);
                    command.Parameters.AddWithValue("@creator", creator ?? (object)DBNull.Value);
                    command.Parameters.AddWithValue("@sourceDeviceId", string.IsNullOrWhiteSpace(trimmedSourceDeviceId) ? (object)DBNull.Value : trimmedSourceDeviceId);
                    command.Parameters.AddWithValue("@sourceRecordId", string.IsNullOrWhiteSpace(trimmedSourceRecordId) ? (object)DBNull.Value : trimmedSourceRecordId);

                    if (command.ExecuteNonQuery() > 0)
                        return true;

                    if (!string.IsNullOrEmpty(trimmedSourceRecordId) && ExistsSalesBySourceRecordId(trimmedSourceRecordId))
                        return true;

                    if (ExistsSalesByBusinessKey(connection, storedOrderNo, spec, trimmedSourceDeviceId))
                    {
                        errorMessage = $"同一销售单已存在相同规格({spec})记录，请勿重复提交";
                        return false;
                    }

                    if (TableDdlHasUniquePair(connection, "sales_transactions", "order_no", "spec") &&
                        TryGetOrderSpecLegacyConflict(connection, "sales_transactions", storedOrderNo, spec, trimmedSourceDeviceId, out string existingDeviceId))
                    {
                        errorMessage =
                            $"该单号({storedOrderNo})下规格「{spec}」已存在（来源设备: {existingDeviceId}）。" +
                            "不同手持机应可并存，请重启桌面端以完成数据库升级。";
                        return false;
                    }

                    errorMessage = "销售保存失败，请检查数据完整性";
                    return false;
                }
            }
            catch (Exception ex)
            {
                errorMessage = $"保存销售记录异常: {ex.Message}";
                Console.WriteLine(errorMessage);
                return false;
            }
        }

        /// <summary>
        /// 保存包装记录（包含出包装/进包装标记）
        /// </summary>
        public bool SavePackagingRecord(string orderNo, string clientCode, string clientName, string packType,
                               string packFlag, int quantity, decimal unitPrice, decimal totalAmount,
                               string handler, string creator, string sourceDeviceId = "", string sourceRecordId = "")
        {
            return SavePackagingRecord(orderNo, clientCode, clientName, packType, packFlag, quantity, unitPrice,
                totalAmount, handler, creator, sourceDeviceId, sourceRecordId, out _, out _);
        }

        public bool SavePackagingRecord(string orderNo, string clientCode, string clientName, string packType,
                               string packFlag, int quantity, decimal unitPrice, decimal totalAmount,
                               string handler, string creator, string sourceDeviceId, string sourceRecordId,
                               out string errorMessage)
        {
            return SavePackagingRecord(orderNo, clientCode, clientName, packType, packFlag, quantity, unitPrice,
                totalAmount, handler, creator, sourceDeviceId, sourceRecordId, out errorMessage, out _);
        }

        public bool SavePackagingRecord(string orderNo, string clientCode, string clientName, string packType,
                               string packFlag, int quantity, decimal unitPrice, decimal totalAmount,
                               string handler, string creator, string sourceDeviceId, string sourceRecordId,
                               out string errorMessage, out PackagingSaveOutcome outcome)
        {
            errorMessage = "";
            outcome = PackagingSaveOutcome.OtherError;
            try
            {
                using (var connection = new SQLiteConnection(connectionString))
                {
                    connection.Open();
                    EnsureSourceColumns(connection, "packaging_transactions");
                    string storedOrderNo = (orderNo ?? "").Trim();
                    string storedPackFlag = NormalizePackFlagForStorage(packFlag);
                    string trimmedSourceRecordId = string.IsNullOrWhiteSpace(sourceRecordId) ? "" : sourceRecordId.Trim();
                    string trimmedSourceDeviceId = string.IsNullOrWhiteSpace(sourceDeviceId) ? "" : sourceDeviceId.Trim();

                    if (!string.IsNullOrEmpty(trimmedSourceRecordId) && ExistsPackagingBySourceRecordId(trimmedSourceRecordId))
                    {
                        if (IsPackagingIdempotentMatch(connection, trimmedSourceRecordId, storedOrderNo, clientCode ?? "",
                                packType, storedPackFlag, quantity, totalAmount, trimmedSourceDeviceId))
                        {
                            outcome = PackagingSaveOutcome.IdempotentMatch;
                            errorMessage = BuildPackagingIdempotentMatchMessage(
                                storedOrderNo, clientCode ?? "", packType, storedPackFlag, quantity, totalAmount, trimmedSourceDeviceId);
                            return true;
                        }

                        outcome = PackagingSaveOutcome.ContentMismatch;
                        errorMessage = DescribePackagingContentMismatch(connection, trimmedSourceRecordId, storedOrderNo,
                            clientCode ?? "", packType, storedPackFlag, quantity, totalAmount, trimmedSourceDeviceId);
                        return false;
                    }

                    if (ExistsPackagingByBusinessKey(connection, storedOrderNo, packType, storedPackFlag, trimmedSourceDeviceId))
                    {
                        outcome = PackagingSaveOutcome.BusinessKeyDuplicate;
                        errorMessage = BuildPackagingBusinessKeyDuplicateMessage(
                            storedOrderNo, packType, storedPackFlag, trimmedSourceDeviceId);
                        return false;
                    }

                    string sql = @"
                INSERT OR IGNORE INTO packaging_transactions 
                (order_no, client_code, client_name, pack_type, pack_flag, quantity, unit_price, 
                 total_amount, handler, creator, date, source_device_id, source_record_id, created_time, updated_at) 
                VALUES (@orderNo, @clientCode, @clientName, @packType, @packFlag, @quantity,
                        @unitPrice, @totalAmount, @handler, @creator, @date, @sourceDeviceId, @sourceRecordId, datetime('now', 'localtime'), datetime('now', 'localtime'))";

                    var command = new SQLiteCommand(sql, connection);

                    command.Parameters.AddWithValue("@orderNo", storedOrderNo);
                    command.Parameters.AddWithValue("@clientCode", clientCode);
                    command.Parameters.AddWithValue("@clientName", clientName);
                    command.Parameters.AddWithValue("@packType", packType);
                    command.Parameters.AddWithValue("@packFlag", storedPackFlag);
                    command.Parameters.AddWithValue("@quantity", quantity);
                    command.Parameters.AddWithValue("@unitPrice", unitPrice);
                    command.Parameters.AddWithValue("@totalAmount", totalAmount);
                    command.Parameters.AddWithValue("@handler", handler);
                    command.Parameters.AddWithValue("@creator", creator);
                    command.Parameters.AddWithValue("@date", DateTime.Now.ToString("yyyy-MM-dd"));
                    command.Parameters.AddWithValue("@sourceDeviceId", string.IsNullOrWhiteSpace(trimmedSourceDeviceId) ? (object)DBNull.Value : trimmedSourceDeviceId);
                    command.Parameters.AddWithValue("@sourceRecordId", string.IsNullOrWhiteSpace(trimmedSourceRecordId) ? (object)DBNull.Value : trimmedSourceRecordId);

                    int rows = command.ExecuteNonQuery();
                    if (rows > 0)
                    {
                        outcome = PackagingSaveOutcome.SavedNew;
                        return true;
                    }

                    if (!string.IsNullOrEmpty(trimmedSourceRecordId) && ExistsPackagingBySourceRecordId(trimmedSourceRecordId))
                    {
                        if (IsPackagingIdempotentMatch(connection, trimmedSourceRecordId, storedOrderNo, clientCode ?? "",
                                packType, storedPackFlag, quantity, totalAmount, trimmedSourceDeviceId))
                        {
                            outcome = PackagingSaveOutcome.IdempotentMatch;
                            errorMessage = BuildPackagingIdempotentMatchMessage(
                                storedOrderNo, clientCode ?? "", packType, storedPackFlag, quantity, totalAmount, trimmedSourceDeviceId);
                            return true;
                        }

                        outcome = PackagingSaveOutcome.ContentMismatch;
                        errorMessage = DescribePackagingContentMismatch(connection, trimmedSourceRecordId, storedOrderNo,
                            clientCode ?? "", packType, storedPackFlag, quantity, totalAmount, trimmedSourceDeviceId);
                        return false;
                    }

                    if (ExistsPackagingByBusinessKey(connection, storedOrderNo, packType, storedPackFlag, trimmedSourceDeviceId))
                    {
                        outcome = PackagingSaveOutcome.BusinessKeyDuplicate;
                        errorMessage = BuildPackagingBusinessKeyDuplicateMessage(
                            storedOrderNo, packType, storedPackFlag, trimmedSourceDeviceId);
                        return false;
                    }

                    if (PackagingTableHasLegacyOrderPackUnique(connection) &&
                        TryGetPackagingLegacyOrderPackConflict(connection, storedOrderNo, packType, trimmedSourceDeviceId,
                            out string existingDeviceId))
                    {
                        outcome = PackagingSaveOutcome.BusinessKeyDuplicate;
                        errorMessage =
                            $"该单号({storedOrderNo})下包装类型「{packType}」已存在（来源设备: {existingDeviceId}）。" +
                            "不同手持机应可并存，请重启桌面端以完成数据库升级。";
                        return false;
                    }

                    outcome = PackagingSaveOutcome.OtherError;
                    errorMessage = "包装保存失败，请检查数据完整性";
                    return false;
                }
            }
            catch (Exception ex)
            {
                outcome = PackagingSaveOutcome.OtherError;
                errorMessage = $"保存包装记录异常: {ex.Message}";
                Console.WriteLine(errorMessage);
                return false;
            }
        }

        private static string BuildPackagingIdempotentMatchMessage(
            string orderNo, string clientCode, string packType, string packFlag,
            int quantity, decimal totalAmount, string sourceDeviceId)
        {
            return "电脑端已有相同明细，未重复保存。判定依据：同步记录ID已存在，且单号、客户编号、包装类型、出/进标记、数量、金额、来源设备均一致。" +
                   $"（单号={orderNo}，客户={clientCode}，类型={packType}，标记={packFlag}，数量={quantity}，金额={totalAmount:F2}，设备={sourceDeviceId}）";
        }

        private static string BuildPackagingBusinessKeyDuplicateMessage(
            string orderNo, string packType, string packFlag, string sourceDeviceId)
        {
            return $"电脑端已有相同单据。判定依据：业务唯一键重复（单号={orderNo} + 包装类型={packType} + 出/进={packFlag} + 来源设备={sourceDeviceId}）";
        }

        private static string DescribePackagingContentMismatch(
            SQLiteConnection connection,
            string sourceRecordId,
            string orderNo,
            string clientCode,
            string packType,
            string packFlag,
            int quantity,
            decimal totalAmount,
            string sourceDeviceId)
        {
            const string sql = @"
SELECT order_no, client_code, pack_type, COALESCE(pack_flag, 'TAKE') AS pack_flag,
       quantity, total_amount, COALESCE(source_device_id, '') AS source_device_id
FROM packaging_transactions
WHERE source_record_id = @sourceRecordId
LIMIT 1";
            var mismatches = new List<string>();
            using (var command = new SQLiteCommand(sql, connection))
            {
                command.Parameters.AddWithValue("@sourceRecordId", sourceRecordId);
                using (var reader = command.ExecuteReader())
                {
                    if (!reader.Read())
                    {
                        return "同步记录ID已存在但无法读取电脑端原记录";
                    }

                    string existingOrderNo = reader["order_no"]?.ToString()?.Trim() ?? "";
                    string existingClientCode = reader["client_code"]?.ToString()?.Trim() ?? "";
                    string existingPackType = reader["pack_type"]?.ToString()?.Trim() ?? "";
                    string existingPackFlag = reader["pack_flag"]?.ToString()?.Trim() ?? "TAKE";
                    int existingQuantity = Convert.ToInt32(reader["quantity"] ?? 0);
                    decimal existingTotalAmount = Convert.ToDecimal(reader["total_amount"] ?? 0m);
                    string existingDeviceId = reader["source_device_id"]?.ToString()?.Trim() ?? "";
                    string normalizedPackFlag = NormalizePackFlagForStorage(packFlag);

                    if (!string.Equals(existingOrderNo, orderNo?.Trim() ?? "", StringComparison.Ordinal))
                        mismatches.Add($"单号(电脑={existingOrderNo},本次={orderNo})");
                    if (!string.Equals(existingClientCode, clientCode?.Trim() ?? "", StringComparison.Ordinal))
                        mismatches.Add($"客户编号(电脑={existingClientCode},本次={clientCode})");
                    if (!string.Equals(existingPackType, packType?.Trim() ?? "", StringComparison.Ordinal))
                        mismatches.Add($"包装类型(电脑={existingPackType},本次={packType})");
                    if (!string.Equals(existingPackFlag, normalizedPackFlag, StringComparison.Ordinal))
                        mismatches.Add($"出/进标记(电脑={existingPackFlag},本次={normalizedPackFlag})");
                    if (existingQuantity != quantity)
                        mismatches.Add($"数量(电脑={existingQuantity},本次={quantity})");
                    if (Math.Abs(existingTotalAmount - totalAmount) > 0.01m)
                        mismatches.Add($"金额(电脑={existingTotalAmount:F2},本次={totalAmount:F2})");
                    if (!string.Equals(existingDeviceId, sourceDeviceId ?? "", StringComparison.Ordinal))
                        mismatches.Add($"来源设备(电脑={existingDeviceId},本次={sourceDeviceId})");
                }
            }

            if (mismatches.Count == 0)
            {
                return "同步记录ID已存在但内容与本次上传不一致";
            }

            return "同步记录ID已存在但内容与本次上传不一致：" + string.Join("；", mismatches);
        }

        private static bool ExistsPackagingByBusinessKey(SQLiteConnection connection, string orderNo, string packType,
            string packFlag, string sourceDeviceId)
        {
            const string sql = @"
SELECT COUNT(1) FROM packaging_transactions
WHERE order_no = @orderNo
  AND pack_type = @packType
  AND COALESCE(pack_flag, 'TAKE') = @packFlag
  AND COALESCE(source_device_id, '') = @sourceDeviceId
LIMIT 1";
            using (var command = new SQLiteCommand(sql, connection))
            {
                command.Parameters.AddWithValue("@orderNo", orderNo ?? "");
                command.Parameters.AddWithValue("@packType", packType ?? "");
                command.Parameters.AddWithValue("@packFlag", packFlag ?? "TAKE");
                command.Parameters.AddWithValue("@sourceDeviceId", sourceDeviceId ?? "");
                return Convert.ToInt32(command.ExecuteScalar() ?? 0) > 0;
            }
        }

        /// <summary>
        /// 检测旧版 UNIQUE(order_no, pack_type) 冲突：同单号+包装类型已存在但来源设备不同。
        /// </summary>
        private static bool TryGetPackagingLegacyOrderPackConflict(SQLiteConnection connection, string orderNo,
            string packType, string currentDeviceId, out string existingDeviceId)
        {
            existingDeviceId = "";
            const string sql = @"
SELECT COALESCE(source_device_id, '') FROM packaging_transactions
WHERE order_no = @orderNo AND pack_type = @packType
LIMIT 1";
            using (var command = new SQLiteCommand(sql, connection))
            {
                command.Parameters.AddWithValue("@orderNo", orderNo ?? "");
                command.Parameters.AddWithValue("@packType", packType ?? "");
                var result = command.ExecuteScalar();
                if (result == null || result == DBNull.Value)
                    return false;

                existingDeviceId = result.ToString() ?? "";
                return !string.Equals(existingDeviceId, currentDeviceId ?? "", StringComparison.Ordinal);
            }
        }

        private static bool ExistsInboundByBusinessKey(SQLiteConnection connection, string orderNo, string spec, string sourceDeviceId)
        {
            const string sql = @"
SELECT COUNT(1) FROM inbound_transactions
WHERE order_no = @orderNo AND spec = @spec AND COALESCE(source_device_id, '') = @sourceDeviceId
LIMIT 1";
            using (var command = new SQLiteCommand(sql, connection))
            {
                command.Parameters.AddWithValue("@orderNo", orderNo ?? "");
                command.Parameters.AddWithValue("@spec", spec ?? "");
                command.Parameters.AddWithValue("@sourceDeviceId", sourceDeviceId ?? "");
                return Convert.ToInt32(command.ExecuteScalar() ?? 0) > 0;
            }
        }

        private static bool ExistsSalesByBusinessKey(SQLiteConnection connection, string orderNo, string spec, string sourceDeviceId)
        {
            const string sql = @"
SELECT COUNT(1) FROM sales_transactions
WHERE order_no = @orderNo AND spec = @spec AND COALESCE(source_device_id, '') = @sourceDeviceId
LIMIT 1";
            using (var command = new SQLiteCommand(sql, connection))
            {
                command.Parameters.AddWithValue("@orderNo", orderNo ?? "");
                command.Parameters.AddWithValue("@spec", spec ?? "");
                command.Parameters.AddWithValue("@sourceDeviceId", sourceDeviceId ?? "");
                return Convert.ToInt32(command.ExecuteScalar() ?? 0) > 0;
            }
        }

        private static bool TryGetOrderSpecLegacyConflict(SQLiteConnection connection, string tableName,
            string orderNo, string spec, string currentDeviceId, out string existingDeviceId)
        {
            existingDeviceId = "";
            string sql = $@"
SELECT COALESCE(source_device_id, '') FROM {tableName}
WHERE order_no = @orderNo AND spec = @spec
LIMIT 1";
            using (var command = new SQLiteCommand(sql, connection))
            {
                command.Parameters.AddWithValue("@orderNo", orderNo ?? "");
                command.Parameters.AddWithValue("@spec", spec ?? "");
                var result = command.ExecuteScalar();
                if (result == null || result == DBNull.Value)
                    return false;

                existingDeviceId = result.ToString() ?? "";
                return !string.Equals(existingDeviceId, currentDeviceId ?? "", StringComparison.Ordinal);
            }
        }

        private static bool ExistsAdvanceBySourceRecordId(SQLiteConnection connection, string sourceRecordId)
        {
            if (string.IsNullOrWhiteSpace(sourceRecordId))
                return false;
            using (var command = new SQLiteCommand(
                "SELECT COUNT(1) FROM advances WHERE source_record_id = @id LIMIT 1", connection))
            {
                command.Parameters.AddWithValue("@id", sourceRecordId.Trim());
                return Convert.ToInt32(command.ExecuteScalar() ?? 0) > 0;
            }
        }

        private static bool ExistsAdvanceByBusinessKey(SQLiteConnection connection, string clientCode,
            decimal amount, string advanceDate, string createdTime, string sourceDeviceId)
        {
            const string sql = @"
SELECT COUNT(1) FROM advances
WHERE client_code = @clientCode AND amount = @amount AND advance_date = @advanceDate
  AND created_time = @createdTime
  AND COALESCE(source_device_id, '') = @sourceDeviceId
LIMIT 1";
            using (var command = new SQLiteCommand(sql, connection))
            {
                command.Parameters.AddWithValue("@clientCode", clientCode ?? "");
                command.Parameters.AddWithValue("@amount", amount);
                command.Parameters.AddWithValue("@advanceDate", advanceDate ?? "");
                command.Parameters.AddWithValue("@createdTime", createdTime ?? "");
                command.Parameters.AddWithValue("@sourceDeviceId", sourceDeviceId ?? "");
                return Convert.ToInt32(command.ExecuteScalar() ?? 0) > 0;
            }
        }

        private static bool ExistsDeductionBySourceRecordId(SQLiteConnection connection, string sourceRecordId)
        {
            if (string.IsNullOrWhiteSpace(sourceRecordId))
                return false;
            using (var command = new SQLiteCommand(
                "SELECT COUNT(1) FROM deductions WHERE source_record_id = @id LIMIT 1", connection))
            {
                command.Parameters.AddWithValue("@id", sourceRecordId.Trim());
                return Convert.ToInt32(command.ExecuteScalar() ?? 0) > 0;
            }
        }

        private static bool ExistsDeductionByBusinessKey(SQLiteConnection connection, string clientCode,
            decimal amount, string deductDate, string createdTime, string sourceDeviceId)
        {
            const string sql = @"
SELECT COUNT(1) FROM deductions
WHERE client_code = @clientCode AND amount = @amount AND deduct_date = @deductDate
  AND created_time = @createdTime
  AND COALESCE(source_device_id, '') = @sourceDeviceId
LIMIT 1";
            using (var command = new SQLiteCommand(sql, connection))
            {
                command.Parameters.AddWithValue("@clientCode", clientCode ?? "");
                command.Parameters.AddWithValue("@amount", amount);
                command.Parameters.AddWithValue("@deductDate", deductDate ?? "");
                command.Parameters.AddWithValue("@createdTime", createdTime ?? "");
                command.Parameters.AddWithValue("@sourceDeviceId", sourceDeviceId ?? "");
                return Convert.ToInt32(command.ExecuteScalar() ?? 0) > 0;
            }
        }

        /// <summary>
        /// 将手持 created_time（毫秒时间戳或已格式化字符串）规范为 yyyy-MM-dd HH:mm:ss。
        /// </summary>
        public static string NormalizeCreatedTimeToSecond(string createdTimeRaw, string creatorFallback = null)
        {
            if (!string.IsNullOrWhiteSpace(createdTimeRaw))
            {
                string trimmed = createdTimeRaw.Trim();
                if (trimmed.All(char.IsDigit) && trimmed.Length >= 10)
                {
                    if (long.TryParse(trimmed, out long ms))
                    {
                        // 10 位视为秒，13 位视为毫秒
                        if (trimmed.Length <= 10)
                            return DateTimeOffset.FromUnixTimeSeconds(ms).LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss");
                        return DateTimeOffset.FromUnixTimeMilliseconds(ms).LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss");
                    }
                }

                if (DateTime.TryParse(trimmed, out DateTime parsed))
                    return parsed.ToString("yyyy-MM-dd HH:mm:ss");

                if (trimmed.Length >= 19)
                    return trimmed.Substring(0, 19);
            }

            if (!string.IsNullOrWhiteSpace(creatorFallback) &&
                creatorFallback.All(char.IsDigit) && creatorFallback.Length > 10 &&
                long.TryParse(creatorFallback, out long creatorMs))
            {
                return DateTimeOffset.FromUnixTimeMilliseconds(creatorMs).LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss");
            }

            return DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        }

        private static bool ExistsPresaleBillByBusinessKey(SQLiteConnection connection, string billNo, string sourceDeviceId)
        {
            const string sql = @"
SELECT COUNT(1) FROM presale_bills
WHERE bill_no = @billNo AND COALESCE(source_device_id, '') = @sourceDeviceId
LIMIT 1";
            using (var command = new SQLiteCommand(sql, connection))
            {
                command.Parameters.AddWithValue("@billNo", billNo ?? "");
                command.Parameters.AddWithValue("@sourceDeviceId", sourceDeviceId ?? "");
                return Convert.ToInt32(command.ExecuteScalar() ?? 0) > 0;
            }
        }

        /// <summary>
        /// 增量导入：入库 (order_no, spec, source_device_id) 已存在则跳过，不覆盖。
        /// 返回 true 表示新插入一行；false 且 skippedDuplicate 为 true 表示因唯一约束跳过；false 且 error 非空表示失败。
        /// </summary>
        public bool TryInsertInboundIncremental(string orderNo, string clientCode, string clientName, string location,
            DateTime date, string spec, int quantity, decimal unitPrice, decimal totalAmount, string handler, string creator,
            string sourceDeviceId, string sourceRecordId, out bool skippedDuplicate, out string error)
        {
            skippedDuplicate = false;
            error = null;
            try
            {
                using (var connection = new SQLiteConnection(connectionString))
                {
                    connection.Open();
                    EnsureSourceColumns(connection, "inbound_transactions");

                    string storedOrderNo = (orderNo ?? "").Trim();

                    var command = new SQLiteCommand(@"
                INSERT OR IGNORE INTO inbound_transactions 
                (order_no, client_code, client_name, location, date, spec, quantity, unit_price, total_amount, handler, creator, source_device_id, source_record_id, created_time, updated_at)
                VALUES (@orderNo, @clientCode, @clientName, @location, @date, @spec, @quantity, @unitPrice, @totalAmount, @handler, @creator, @sourceDeviceId, @sourceRecordId, datetime('now', 'localtime'), datetime('now', 'localtime'))", connection);

                    command.Parameters.AddWithValue("@orderNo", storedOrderNo);
                    command.Parameters.AddWithValue("@clientCode", clientCode ?? "");
                    command.Parameters.AddWithValue("@clientName", clientName ?? "");
                    command.Parameters.AddWithValue("@location", location ?? "");
                    command.Parameters.AddWithValue("@date", date.ToString("yyyy-MM-dd"));
                    command.Parameters.AddWithValue("@spec", spec ?? "");
                    command.Parameters.AddWithValue("@quantity", quantity);
                    command.Parameters.AddWithValue("@unitPrice", unitPrice);
                    command.Parameters.AddWithValue("@totalAmount", totalAmount);
                    command.Parameters.AddWithValue("@handler", handler ?? "");
                    command.Parameters.AddWithValue("@creator", creator ?? "");
                    command.Parameters.AddWithValue("@sourceDeviceId", sourceDeviceId ?? "");
                    command.Parameters.AddWithValue("@sourceRecordId", sourceRecordId ?? "");

                    int rows = command.ExecuteNonQuery();
                    if (rows == 1)
                        return true;
                    skippedDuplicate = true;
                    return false;
                }
            }
            catch (Exception ex)
            {
                error = ex.Message;
                Console.WriteLine($"TryInsertInboundIncremental 失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 增量导入：销售 (order_no, spec, source_device_id) 已存在则跳过。
        /// </summary>
        public bool TryInsertSalesIncremental(string orderNo, string clientCode, string clientName, string location,
            DateTime date, string spec, int quantity, decimal unitPrice, decimal totalAmount, string handler, string creator,
            string sourceDeviceId, string sourceRecordId, out bool skippedDuplicate, out string error)
        {
            skippedDuplicate = false;
            error = null;
            try
            {
                using (var connection = new SQLiteConnection(connectionString))
                {
                    connection.Open();
                    EnsureSourceColumns(connection, "sales_transactions");

                    string storedOrderNo = (orderNo ?? "").Trim();

                    var command = new SQLiteCommand(@"
                INSERT OR IGNORE INTO sales_transactions 
                (order_no, client_code, client_name, location, date, spec, quantity, unit_price, total_amount, handler, creator, source_device_id, source_record_id, created_time, updated_at)
                VALUES (@orderNo, @clientCode, @clientName, @location, @date, @spec, @quantity, @unitPrice, @totalAmount, @handler, @creator, @sourceDeviceId, @sourceRecordId, datetime('now', 'localtime'), datetime('now', 'localtime'))", connection);

                    command.Parameters.AddWithValue("@orderNo", storedOrderNo);
                    command.Parameters.AddWithValue("@clientCode", clientCode ?? (object)DBNull.Value);
                    command.Parameters.AddWithValue("@clientName", clientName ?? (object)DBNull.Value);
                    command.Parameters.AddWithValue("@location", location ?? (object)DBNull.Value);
                    command.Parameters.AddWithValue("@date", date.ToString("yyyy-MM-dd"));
                    command.Parameters.AddWithValue("@spec", spec ?? "");
                    command.Parameters.AddWithValue("@quantity", quantity);
                    command.Parameters.AddWithValue("@unitPrice", unitPrice);
                    command.Parameters.AddWithValue("@totalAmount", totalAmount);
                    command.Parameters.AddWithValue("@handler", handler ?? (object)DBNull.Value);
                    command.Parameters.AddWithValue("@creator", creator ?? (object)DBNull.Value);
                    command.Parameters.AddWithValue("@sourceDeviceId", string.IsNullOrEmpty(sourceDeviceId) ? (object)DBNull.Value : sourceDeviceId);
                    command.Parameters.AddWithValue("@sourceRecordId", string.IsNullOrEmpty(sourceRecordId) ? (object)DBNull.Value : sourceRecordId);

                    int rows = command.ExecuteNonQuery();
                    if (rows == 1)
                        return true;
                    skippedDuplicate = true;
                    return false;
                }
            }
            catch (Exception ex)
            {
                error = ex.Message;
                Console.WriteLine($"TryInsertSalesIncremental 失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 增量导入：包装 (order_no, pack_type, pack_flag, source_device_id) 已存在则跳过；date 为业务日期。
        /// </summary>
        public bool TryInsertPackagingIncremental(string orderNo, string clientCode, string clientName, string packType,
            string packFlag, int quantity, decimal unitPrice, decimal totalAmount, string handler, string creator,
            DateTime transactionDate, string sourceDeviceId, string sourceRecordId, out bool skippedDuplicate, out string error)
        {
            skippedDuplicate = false;
            error = null;
            try
            {
                using (var connection = new SQLiteConnection(connectionString))
                {
                    connection.Open();
                    EnsureSourceColumns(connection, "packaging_transactions");

                    string storedOrderNo = (orderNo ?? "").Trim();
                    string storedPackFlag = NormalizePackFlagForStorage(packFlag);

                    var command = new SQLiteCommand(@"
                INSERT OR IGNORE INTO packaging_transactions 
                (order_no, client_code, client_name, pack_type, pack_flag, quantity, unit_price, 
                 total_amount, handler, creator, date, source_device_id, source_record_id, created_time, updated_at) 
                VALUES (@orderNo, @clientCode, @clientName, @packType, @packFlag, @quantity,
                        @unitPrice, @totalAmount, @handler, @creator, @date, @sourceDeviceId, @sourceRecordId, datetime('now', 'localtime'), datetime('now', 'localtime'))", connection);

                    command.Parameters.AddWithValue("@orderNo", storedOrderNo);
                    command.Parameters.AddWithValue("@clientCode", clientCode ?? (object)DBNull.Value);
                    command.Parameters.AddWithValue("@clientName", clientName ?? (object)DBNull.Value);
                    command.Parameters.AddWithValue("@packType", packType ?? "");
                    command.Parameters.AddWithValue("@packFlag", storedPackFlag);
                    command.Parameters.AddWithValue("@quantity", quantity);
                    command.Parameters.AddWithValue("@unitPrice", unitPrice);
                    command.Parameters.AddWithValue("@totalAmount", totalAmount);
                    command.Parameters.AddWithValue("@handler", handler ?? (object)DBNull.Value);
                    command.Parameters.AddWithValue("@creator", creator ?? (object)DBNull.Value);
                    command.Parameters.AddWithValue("@date", transactionDate.ToString("yyyy-MM-dd"));
                    command.Parameters.AddWithValue("@sourceDeviceId", string.IsNullOrEmpty(sourceDeviceId) ? (object)DBNull.Value : sourceDeviceId);
                    command.Parameters.AddWithValue("@sourceRecordId", string.IsNullOrEmpty(sourceRecordId) ? (object)DBNull.Value : sourceRecordId);

                    int rows = command.ExecuteNonQuery();
                    if (rows == 1)
                        return true;
                    skippedDuplicate = true;
                    return false;
                }
            }
            catch (Exception ex)
            {
                error = ex.Message;
                Console.WriteLine($"TryInsertPackagingIncremental 失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 判断包装记录是否已按 source_record_id 存在（用于幂等确认）
        /// </summary>
        public bool ExistsPackagingBySourceRecordId(string sourceRecordId)
        {
            if (string.IsNullOrWhiteSpace(sourceRecordId))
            {
                return false;
            }

            try
            {
                using (var connection = new SQLiteConnection(connectionString))
                {
                    connection.Open();
                    EnsureSourceColumns(connection, "packaging_transactions");
                    const string sql = "SELECT COUNT(1) FROM packaging_transactions WHERE source_record_id = @sourceRecordId LIMIT 1";
                    using (var command = new SQLiteCommand(sql, connection))
                    {
                        command.Parameters.AddWithValue("@sourceRecordId", sourceRecordId);
                        var count = Convert.ToInt32(command.ExecuteScalar() ?? 0);
                        return count > 0;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"查询包装 source_record_id 失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 幂等命中时校验已存记录与本次上传内容是否一致，避免单号冲突导致误确认。
        /// </summary>
        private static bool IsPackagingIdempotentMatch(
            SQLiteConnection connection,
            string sourceRecordId,
            string orderNo,
            string clientCode,
            string packType,
            string packFlag,
            int quantity,
            decimal totalAmount,
            string sourceDeviceId)
        {
            const string sql = @"
SELECT order_no, client_code, pack_type, COALESCE(pack_flag, 'TAKE') AS pack_flag,
       quantity, total_amount, COALESCE(source_device_id, '') AS source_device_id
FROM packaging_transactions
WHERE source_record_id = @sourceRecordId
LIMIT 1";
            using (var command = new SQLiteCommand(sql, connection))
            {
                command.Parameters.AddWithValue("@sourceRecordId", sourceRecordId);
                using (var reader = command.ExecuteReader())
                {
                    if (!reader.Read())
                        return false;

                    string existingOrderNo = reader["order_no"]?.ToString()?.Trim() ?? "";
                    string existingClientCode = reader["client_code"]?.ToString()?.Trim() ?? "";
                    string existingPackType = reader["pack_type"]?.ToString()?.Trim() ?? "";
                    string existingPackFlag = reader["pack_flag"]?.ToString()?.Trim() ?? "TAKE";
                    int existingQuantity = Convert.ToInt32(reader["quantity"] ?? 0);
                    decimal existingTotalAmount = Convert.ToDecimal(reader["total_amount"] ?? 0m);
                    string existingDeviceId = reader["source_device_id"]?.ToString()?.Trim() ?? "";

                    string normalizedPackFlag = NormalizePackFlagForStorage(packFlag);
                    string normalizedDeviceId = sourceDeviceId ?? "";

                    if (!string.Equals(existingOrderNo, orderNo?.Trim() ?? "", StringComparison.Ordinal))
                        return false;
                    if (!string.Equals(existingClientCode, clientCode?.Trim() ?? "", StringComparison.Ordinal))
                        return false;
                    if (!string.Equals(existingPackType, packType?.Trim() ?? "", StringComparison.Ordinal))
                        return false;
                    if (!string.Equals(existingPackFlag, normalizedPackFlag, StringComparison.Ordinal))
                        return false;
                    if (existingQuantity != quantity)
                        return false;
                    if (Math.Abs(existingTotalAmount - totalAmount) > 0.01m)
                        return false;
                    if (!string.Equals(existingDeviceId, normalizedDeviceId, StringComparison.Ordinal))
                        return false;

                    return true;
                }
            }
        }

        /// <summary>
        /// 判断入库记录是否已按 source_record_id 存在（用于幂等确认）
        /// </summary>
        public bool ExistsInboundBySourceRecordId(string sourceRecordId)
        {
            if (string.IsNullOrWhiteSpace(sourceRecordId))
            {
                return false;
            }

            try
            {
                using (var connection = new SQLiteConnection(connectionString))
                {
                    connection.Open();
                    EnsureSourceColumns(connection, "inbound_transactions");
                    const string sql = "SELECT COUNT(1) FROM inbound_transactions WHERE source_record_id = @sourceRecordId LIMIT 1";
                    using (var command = new SQLiteCommand(sql, connection))
                    {
                        command.Parameters.AddWithValue("@sourceRecordId", sourceRecordId);
                        var count = Convert.ToInt32(command.ExecuteScalar() ?? 0);
                        return count > 0;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"查询入库 source_record_id 失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 判断销售记录是否已按 source_record_id 存在（用于幂等确认）
        /// </summary>
        public bool ExistsSalesBySourceRecordId(string sourceRecordId)
        {
            if (string.IsNullOrWhiteSpace(sourceRecordId))
            {
                return false;
            }

            try
            {
                using (var connection = new SQLiteConnection(connectionString))
                {
                    connection.Open();
                    EnsureSourceColumns(connection, "sales_transactions");
                    const string sql = "SELECT COUNT(1) FROM sales_transactions WHERE source_record_id = @sourceRecordId LIMIT 1";
                    using (var command = new SQLiteCommand(sql, connection))
                    {
                        command.Parameters.AddWithValue("@sourceRecordId", sourceRecordId);
                        var count = Convert.ToInt32(command.ExecuteScalar() ?? 0);
                        return count > 0;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"查询销售 source_record_id 失败: {ex.Message}");
                return false;
            }
        }

        
        #endregion

        #region 对账相关方法
        // 获取客户对账汇总数据
        public DataTable GetClientBalanceSummary(string clientCode, DateTime startDate, DateTime endDate)
        {
            try
            {
                using (var connection = new SQLiteConnection(connectionString))
                {
                    connection.Open();

                    string query = @"
         SELECT 
             c.code as ClientCode,
             c.name as ClientName,
             COALESCE(SUM(sm.total_amount), 0) as SalesTotal,
             COALESCE(SUM(pm.total_amount), 0) as PackagingTotal,
             COALESCE(SUM(d.amount), 0) as DeductionsTotal,
             COALESCE(SUM(a.amount), 0) as AdvancesTotal,
             (COALESCE(SUM(sm.total_amount), 0) + COALESCE(SUM(pm.total_amount), 0) - 
              COALESCE(SUM(d.amount), 0) - COALESCE(SUM(a.amount), 0)) as PayableAmount
         FROM clients c
         LEFT JOIN sales_transactions sm ON c.code = sm.client_code 
             AND sm.date BETWEEN @startDate AND @endDate
         LEFT JOIN packaging_transactions pm ON c.code = pm.client_code 
             AND pm.created_time BETWEEN @startDate AND @endDate
         LEFT JOIN deductions d ON c.code = d.client_code 
             AND d.deduct_date BETWEEN @startDate AND @endDate
         LEFT JOIN advances a ON c.code = a.client_code 
             AND a.advance_date BETWEEN @startDate AND @endDate
         WHERE c.code = @clientCode
         GROUP BY c.code, c.name";

                    using (var command = new SQLiteCommand(query, connection))
                    {
                        command.Parameters.AddWithValue("@clientCode", clientCode);
                        command.Parameters.AddWithValue("@startDate", startDate.ToString("yyyy-MM-dd"));
                        command.Parameters.AddWithValue("@endDate", endDate.ToString("yyyy-MM-dd"));

                        DataTable dt = new DataTable();
                        using (var adapter = new SQLiteDataAdapter(command))
                        {
                            adapter.Fill(dt);
                        }
                        return dt;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"获取客户对账汇总失败: {ex.Message}");
                return new DataTable();
            }
        }
        // 在 DatabaseManager 类中添加（如果还没有的话）：
        // 您应该已经有一个ExecuteQuery方法，添加这个带参数的重载版本
        public DataTable ExecuteQuery(string sql, Dictionary<string, object> parameters = null)
        {
            try
            {
                using (var connection = new SQLiteConnection(connectionString))
                {
                    connection.Open();
                    using (var command = new SQLiteCommand(sql, connection))
                    {
                        if (parameters != null)
                        {
                            foreach (var param in parameters)
                            {
                                command.Parameters.AddWithValue(param.Key, param.Value ?? DBNull.Value);
                            }
                        }

                        DataTable dataTable = new DataTable();
                        using (var adapter = new SQLiteDataAdapter(command))
                        {
                            adapter.Fill(dataTable);
                        }
                        return dataTable;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"执行查询失败: {ex.Message}");
                return new DataTable();
            }
        }
        // 获取客户明细对账数据
        public DataTable GetClientBalanceDetails(string clientCode, DateTime startDate, DateTime endDate)
        {
            try
            {
                using (var connection = new SQLiteConnection(connectionString))
                {
                    connection.Open();

                    string query = @"
         SELECT 
             '销售' as Type,
             order_no as OrderNo,
             date as TransactionDate,
             total_amount as Amount,
             handler as Handler,
             '' as Reason
         FROM sales_transactions 
         WHERE client_code = @clientCode AND date BETWEEN @startDate AND @endDate
         
         UNION ALL
         
         SELECT 
             '包装' as Type,
             order_no as OrderNo,
             DATE(created_time) as TransactionDate,
             total_amount as Amount,
             handler as Handler,
             '' as Reason
         FROM packaging_transactions 
         WHERE client_code = @clientCode AND created_time BETWEEN @startDate AND @endDate
         
         UNION ALL
         
         SELECT 
             '扣款' as Type,
             '' as OrderNo,
             deduct_date as TransactionDate,
             -amount as Amount,  -- 扣款为负数
             handler as Handler,
             reason as Reason
         FROM deductions 
         WHERE client_code = @clientCode AND deduct_date BETWEEN @startDate AND @endDate
         
         UNION ALL
         
         SELECT 
             '预支' as Type,
             '' as OrderNo,
             advance_date as TransactionDate,
             -amount as Amount,  -- 预支为负数
             handler as Handler,
             reason as Reason
         FROM advances 
         WHERE client_code = @clientCode AND advance_date BETWEEN @startDate AND @endDate
         
         ORDER BY TransactionDate DESC";

                    using (var command = new SQLiteCommand(query, connection))
                    {
                        command.Parameters.AddWithValue("@clientCode", clientCode);
                        command.Parameters.AddWithValue("@startDate", startDate.ToString("yyyy-MM-dd"));
                        command.Parameters.AddWithValue("@endDate", endDate.ToString("yyyy-MM-dd"));

                        DataTable dt = new DataTable();
                        using (var adapter = new SQLiteDataAdapter(command))
                        {
                            adapter.Fill(dt);
                        }
                        return dt;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"获取客户对账明细失败: {ex.Message}");
                return new DataTable();
            }
        }


        #region 统计查询方法
        public int GetTodayTotalQuantity(string spec)
        {
            try
            {
                using (var connection = new SQLiteConnection(connectionString))
                {
                    connection.Open();
                    var command = new SQLiteCommand(@"
                SELECT COALESCE(SUM(quantity), 0)
                FROM inbound_transactions 
                WHERE spec = @spec AND date = @today", connection);

                    command.Parameters.AddWithValue("@spec", spec);
                    command.Parameters.AddWithValue("@today", DateTime.Now.ToString("yyyy-MM-dd"));

                    var result = command.ExecuteScalar();
                    return Convert.ToInt32(result);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"获取今日总数失败: {ex.Message}");
                return 0;
            }
        }
        #region 多维统计查询方法
        public DataTable GetSummaryByClient(DateTime startDate, DateTime endDate)
        {
            Console.WriteLine($">>> [GetSummaryByClient] 开始获取客户统计: {startDate:yyyy-MM-dd} 至 {endDate:yyyy-MM-dd}");

            DataTable result = new DataTable();

            try
            {
                using (SQLiteConnection conn = new SQLiteConnection(ConnectionString))
                {
                    conn.Open();

                    // ✅ 修正查询：使用正确的字段名 client_code 而不是 customer_id
                    // ✅ 检查表结构，确定正确的字段名
                    string query = @"
                SELECT 
                    c.code AS '客户编号',
                    c.name AS '客户名称',
                    COUNT(t.id) AS '业务笔数',
                    COALESCE(SUM(CASE WHEN t.transaction_type = '销售' THEN t.quantity ELSE 0 END), 0) AS '销售数量',
                    COALESCE(SUM(CASE WHEN t.transaction_type = '销售' THEN t.total_amount ELSE 0 END), 0) AS '销售金额',
                    COALESCE(SUM(CASE WHEN t.transaction_type = '入库' THEN t.quantity ELSE 0 END), 0) AS '入库数量',
                    COALESCE(SUM(CASE WHEN t.transaction_type = '入库' THEN t.total_amount ELSE 0 END), 0) AS '入库金额',
                    COALESCE(c.balance, 0) AS '当前余额'
                FROM clients c
                LEFT JOIN (
                    -- ✅ 修正：使用client_code字段
                    SELECT id, client_code, '入库' as transaction_type, quantity, total_amount, date as transaction_date 
                    FROM inbound_transactions 
                    WHERE date BETWEEN @StartDate AND @EndDate
                    
                    UNION ALL
                    
                    SELECT id, client_code, '销售' as transaction_type, quantity, total_amount, date as transaction_date 
                    FROM sales_transactions 
                    WHERE date BETWEEN @StartDate AND @EndDate
                ) t ON c.code = t.client_code  -- ✅ 修正：使用code关联
                GROUP BY c.id, c.code, c.name, c.balance
                ORDER BY c.code";

                    Console.WriteLine($">>> 执行客户统计查询...");

                    using (SQLiteCommand cmd = new SQLiteCommand(query, conn))
                    {
                        cmd.Parameters.AddWithValue("@StartDate", startDate.ToString("yyyy-MM-dd"));
                        cmd.Parameters.AddWithValue("@EndDate", endDate.ToString("yyyy-MM-dd"));

                        using (SQLiteDataAdapter adapter = new SQLiteDataAdapter(cmd))
                        {
                            adapter.Fill(result);
                        }
                    }

                    Console.WriteLine($">>> [GetSummaryByClient] 查询完成，返回 {result.Rows.Count} 行数据");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($">>> [GetSummaryByClient] 查询失败: {ex.Message}");

                // 备用方案：只获取客户基本信息
                try
                {
                    result = GetBasicClientInfo();
                }
                catch (Exception ex2)
                {
                    Console.WriteLine($">>> 备用查询也失败: {ex2.Message}");
                    result = CreateEmptyClientSummaryTable();
                }
            }

            return result;
        }

        // 备用方法：只获取客户基本信息
        private DataTable GetBasicClientInfo()
        {
            DataTable result = new DataTable();

            try
            {
                using (SQLiteConnection conn = new SQLiteConnection(ConnectionString))
                {
                    conn.Open();

                    string query = @"
                SELECT 
                    code AS '客户编号',
                    name AS '客户名称',
                    0 AS '业务笔数',
                    0 AS '销售数量',
                    0.00 AS '销售金额',
                    0 AS '入库数量',
                    0.00 AS '入库金额',
                    COALESCE(balance, 0.00) AS '当前余额'
                FROM clients 
                WHERE status = 1
                ORDER BY code";

                    using (SQLiteCommand cmd = new SQLiteCommand(query, conn))
                    {
                        using (SQLiteDataAdapter adapter = new SQLiteDataAdapter(cmd))
                        {
                            adapter.Fill(result);
                        }
                    }

                    Console.WriteLine($">>> [GetBasicClientInfo] 获取到 {result.Rows.Count} 条客户记录");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($">>> [GetBasicClientInfo] 失败: {ex.Message}");
                throw;
            }

            return result;
        }

        // 按关键词搜索客户统计（最终修复版）
        public DataTable SearchClientsSummary(DateTime startDate, DateTime endDate, string keyword)
        {
            try
            {
                Console.WriteLine($">>> [SearchClientsSummary] 搜索客户统计：关键词='{keyword}'");

                // ✅ 先修复表结构
                FixClientsTable();

                // 使用安全的查询，避免不存在的字段
                string sql = @"
            SELECT 
                c.code AS 客户编号,
                c.name AS 客户名称,
                0 AS 业务笔数,
                0 AS 销售数量,
                0.0 AS 销售金额,
                0 AS 入库数量,
                0.0 AS 入库金额,
                COALESCE(c.balance, 0) AS 当前余额
            FROM clients c
            WHERE (c.name LIKE @keyword OR c.code LIKE @keyword)
            ORDER BY c.code ASC";

                DataTable result = ExecuteQuery(sql, new Dictionary<string, object>
        {
            { "@keyword", $"%{keyword}%" }
        });

                Console.WriteLine($">>> [SearchClientsSummary] 搜索完成，返回 {result.Rows.Count} 行数据");
                return result;
            }
            catch (Exception ex)
            {
                Console.WriteLine($">>> [SearchClientsSummary] 异常，尝试简化查询: {ex.Message}");

                // 如果还有问题，使用最简化的查询
                try
                {
                    string simpleSql = @"
                SELECT 
                    code AS 客户编号,
                    name AS 客户名称,
                    '0' AS 业务笔数,
                    '0' AS 销售数量,
                    '0.0' AS 销售金额,
                    '0' AS 入库数量,
                    '0.0' AS 入库金额,
                    '0.0' AS 当前余额
                FROM clients 
                WHERE (name LIKE @keyword OR code LIKE @keyword)
                ORDER BY code ASC";

                    return ExecuteQuery(simpleSql, new Dictionary<string, object>
            {
                { "@keyword", $"%{keyword}%" }
            });
                }
                catch (Exception ex2)
                {
                    Console.WriteLine($">>> [SearchClientsSummary] 简化查询也失败: {ex2.Message}");
                    return new DataTable();
                }
            }
        }
        // 1. 按经手人汇总业务量与金额
        public DataTable GetSummaryByHandler(DateTime startDate, DateTime endDate)
        {
            DataTable dt = new DataTable();
            try
            {
                using (var connection = new SQLiteConnection(connectionString))
                {
                    connection.Open();

                    string query = @"
SELECT 
    handler as 经手人,
    COUNT(*) as 业务笔数,
    SUM(CASE WHEN type = '销售' THEN quantity ELSE 0 END) as 销售数量,
    SUM(CASE WHEN type = '销售' THEN total_amount ELSE 0 END) as 销售金额,
    SUM(CASE WHEN type = '入库' THEN quantity ELSE 0 END) as 入库数量,
    SUM(CASE WHEN type = '入库' THEN total_amount ELSE 0 END) as 入库金额
FROM (
    -- 销售记录
    SELECT 
        '销售' as type,
        handler,
        quantity,
        total_amount,
        date
    FROM sales_transactions 
    WHERE date BETWEEN @startDate AND @endDate
    
    UNION ALL
    
    -- 入库记录  
    SELECT 
        '入库' as type,
        handler,
        quantity,
        total_amount,
        date
    FROM inbound_transactions 
    WHERE date BETWEEN @startDate AND @endDate
) as combined_data
WHERE handler IS NOT NULL AND handler != ''
GROUP BY handler
ORDER BY 销售金额 DESC, 入库金额 DESC";

                    using (var command = new SQLiteCommand(query, connection))
                    {
                        command.Parameters.AddWithValue("@startDate", startDate.ToString("yyyy-MM-dd"));
                        command.Parameters.AddWithValue("@endDate", endDate.ToString("yyyy-MM-dd"));

                        using (var adapter = new SQLiteDataAdapter(command))
                        {
                            adapter.Fill(dt);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"按经手人汇总失败: {ex.Message}");
            }
            return dt;
        }

        // 2. 按商品型号汇总入库、出库量与金额
        public DataTable GetSummaryByProductType(DateTime startDate, DateTime endDate)
        {
            DataTable dt = new DataTable();
            try
            {
                using (var connection = new SQLiteConnection(connectionString))
                {
                    connection.Open();

                    string query = @"
SELECT 
    spec as 商品型号,
    SUM(CASE WHEN type = '入库' THEN quantity ELSE 0 END) as 入库数量,
    SUM(CASE WHEN type = '入库' THEN total_amount ELSE 0 END) as 入库金额,
    SUM(CASE WHEN type = '销售' THEN quantity ELSE 0 END) as 销售数量,
    SUM(CASE WHEN type = '销售' THEN total_amount ELSE 0 END) as 销售金额,
    (SUM(CASE WHEN type = '入库' THEN quantity ELSE 0 END) - 
     SUM(CASE WHEN type = '销售' THEN quantity ELSE 0 END)) as 当前库存
FROM (
    -- 入库记录
    SELECT 
        '入库' as type,
        spec,
        quantity,
        total_amount
    FROM inbound_transactions 
    WHERE date BETWEEN @startDate AND @endDate
    
    UNION ALL
    
    -- 销售记录
    SELECT 
        '销售' as type,
        spec,
        quantity,
        total_amount
    FROM sales_transactions 
    WHERE date BETWEEN @startDate AND @endDate
) as combined_data
GROUP BY spec
ORDER BY 入库金额 DESC, 销售金额 DESC";

                    using (var command = new SQLiteCommand(query, connection))
                    {
                        command.Parameters.AddWithValue("@startDate", startDate.ToString("yyyy-MM-dd"));
                        command.Parameters.AddWithValue("@endDate", endDate.ToString("yyyy-MM-dd"));

                        using (var adapter = new SQLiteDataAdapter(command))
                        {
                            adapter.Fill(dt);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"按商品型号汇总失败: {ex.Message}");
            }
            return dt;
        }

        // 3. 按日期分批汇总（日报、周报、月报）
        public DataTable GetSummaryByDate(DateTime startDate, DateTime endDate, string periodType = "日")
        {
            DataTable dt = new DataTable();
            try
            {
                using (var connection = new SQLiteConnection(connectionString))
                {
                    connection.Open();

                    string dateGroup = "";
                    switch (periodType)
                    {
                        case "周":
                            dateGroup = "strftime('%Y-%W', date)";
                            break;
                        case "月":
                            dateGroup = "strftime('%Y-%m', date)";
                            break;
                        case "年":
                            dateGroup = "strftime('%Y', date)";
                            break;
                        default: // 日
                            dateGroup = "date";
                            break;
                    }

                    string query = $@"
SELECT 
    {dateGroup} as 期间,
    COUNT(*) as 业务笔数,
    SUM(CASE WHEN type = '销售' THEN quantity ELSE 0 END) as 销售数量,
    SUM(CASE WHEN type = '销售' THEN total_amount ELSE 0 END) as 销售金额,
    SUM(CASE WHEN type = '入库' THEN quantity ELSE 0 END) as 入库数量,
    SUM(CASE WHEN type = '入库' THEN total_amount ELSE 0 END) as 入库金额,
    SUM(CASE WHEN type = '包装' THEN quantity ELSE 0 END) as 包装数量,
    SUM(CASE WHEN type = '包装' THEN total_amount ELSE 0 END) as 包装金额
FROM (
    -- 销售记录
    SELECT 
        '销售' as type,
        date,
        quantity,
        total_amount
    FROM sales_transactions 
    WHERE date BETWEEN @startDate AND @endDate
    
    UNION ALL
    
    -- 入库记录
    SELECT 
        '入库' as type,
        date,
        quantity,
        total_amount
    FROM inbound_transactions 
    WHERE date BETWEEN @startDate AND @endDate
    
    UNION ALL
    
    -- 包装记录
    SELECT 
        '包装' as type,
        DATE(created_time) as date,
        quantity,
        total_amount
    FROM packaging_transactions 
    WHERE DATE(created_time) BETWEEN @startDate AND @endDate
) as combined_data
GROUP BY {dateGroup}
ORDER BY {dateGroup} DESC";

                    using (var command = new SQLiteCommand(query, connection))
                    {
                        command.Parameters.AddWithValue("@startDate", startDate.ToString("yyyy-MM-dd"));
                        command.Parameters.AddWithValue("@endDate", endDate.ToString("yyyy-MM-dd"));

                        using (var adapter = new SQLiteDataAdapter(command))
                        {
                            adapter.Fill(dt);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"按日期汇总失败: {ex.Message}");
            }
            return dt;
        }

        // 4. 按库位查询库存情况（出库/预占来自销预售单，不再使用 sales_transactions）
        public DataTable GetInventoryByLocation()
        {
            DataTable dt = new DataTable();
            try
            {
                using (var connection = new SQLiteConnection(connectionString))
                {
                    connection.Open();

                    string query = @"
WITH inbound AS (
    SELECT
        COALESCE(location, '') AS location,
        spec,
        SUM(COALESCE(quantity, 0)) AS qty,
        SUM(COALESCE(total_amount, 0)) AS amt
    FROM inbound_transactions
    WHERE COALESCE(location, '') != ''
      AND COALESCE(spec, '') != ''
    GROUP BY COALESCE(location, ''), spec
),
presale_reserved AS (
    SELECT
        COALESCE(b.location, '') AS location,
        i.spec,
        SUM(MAX(0, COALESCE(i.quantity, 0) - COALESCE(i.shipped_quantity, 0))) AS qty
    FROM presale_bills b
    INNER JOIN presale_items i ON b.bill_no = i.bill_no
    WHERE b.sale_mode = 'PRESALE'
      AND b.status IN ('PRESALE', 'SHIPPED')
      AND b.status != 'CANCELLED'
      AND COALESCE(b.location, '') != ''
      AND COALESCE(i.spec, '') != ''
    GROUP BY COALESCE(b.location, ''), i.spec
),
presale_out AS (
    SELECT
        COALESCE(b.location, '') AS location,
        i.spec,
        SUM(
            CASE
                WHEN COALESCE(i.shipped_quantity, 0) > 0 THEN COALESCE(i.shipped_quantity, 0)
                WHEN b.sale_mode = 'DIRECT_OUT' OR b.status IN ('SHIPPED', 'COMPLETED') THEN COALESCE(i.quantity, 0)
                ELSE 0
            END
        ) AS qty,
        SUM(
            CASE
                WHEN COALESCE(i.shipped_quantity, 0) > 0 THEN COALESCE(i.shipped_quantity, 0) * COALESCE(i.unit_price, 0)
                WHEN b.sale_mode = 'DIRECT_OUT' OR b.status IN ('SHIPPED', 'COMPLETED') THEN COALESCE(i.total_amount, 0)
                ELSE 0
            END
        ) AS amt
    FROM presale_bills b
    INNER JOIN presale_items i ON b.bill_no = i.bill_no
    WHERE b.status != 'CANCELLED'
      AND COALESCE(b.location, '') != ''
      AND COALESCE(i.spec, '') != ''
      AND (
            b.sale_mode = 'DIRECT_OUT'
            OR b.status IN ('SHIPPED', 'COMPLETED')
          )
    GROUP BY COALESCE(b.location, ''), i.spec
),
combined AS (
    SELECT location, spec FROM inbound
    UNION
    SELECT location, spec FROM presale_reserved
    UNION
    SELECT location, spec FROM presale_out
)
SELECT
    c.location AS 库位,
    c.spec AS 商品型号,
    COALESCE(i.qty, 0) AS 累计入库,
    COALESCE(pr.qty, 0) AS 预售数量,
    COALESCE(po.qty, 0) AS 出库数量,
    COALESCE(i.qty, 0) - COALESCE(pr.qty, 0) - COALESCE(po.qty, 0) AS 当前库存,
    COALESCE(i.amt, 0) AS 入库金额,
    COALESCE(po.amt, 0) AS 出库金额
FROM combined c
LEFT JOIN inbound i ON c.location = i.location AND c.spec = i.spec
LEFT JOIN presale_reserved pr ON c.location = pr.location AND c.spec = pr.spec
LEFT JOIN presale_out po ON c.location = po.location AND c.spec = po.spec
ORDER BY c.location, c.spec";

                    using (var adapter = new SQLiteDataAdapter(query, connection))
                    {
                        adapter.Fill(dt);
                    }

                    Console.WriteLine($">>> GetInventoryByLocation 查询成功，返回 {dt.Rows.Count} 行");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"按库位查询库存失败: {ex.Message}");
            }
            return dt;
        }

        /// <summary>
        /// 入库统计快照：按 date + 客户 + 库位 + 型号 汇总（用于下发手持端离线查询）
        /// </summary>
        public DataTable GetInboundDailyStats()
        {
            DataTable dt = new DataTable();
            try
            {
                using (var connection = new SQLiteConnection(connectionString))
                {
                    connection.Open();

                    // 说明：
                    // - date 字段本身为 yyyy-MM-dd，但这里仍做 substr 归一化以兼容潜在的带时间格式
                    // - order_no 在跨设备入库时可能被拼接 “@deviceId”，但仍可作为订单唯一标识
                    string query = @"
SELECT
    substr(date, 1, 10) AS date,
    client_code AS customer_no,
    client_name AS customer_name,
    location AS location_name,
    spec,
    SUM(COALESCE(quantity, 0)) AS quantity,
    SUM(COALESCE(total_amount, 0)) AS amount,
    COUNT(DISTINCT order_no) AS order_count
FROM inbound_transactions
WHERE date IS NOT NULL AND TRIM(date) != ''
GROUP BY substr(date, 1, 10), client_code, client_name, location, spec
ORDER BY substr(date, 1, 10) DESC, client_code, location, spec";

                    using (var command = new SQLiteCommand(query, connection))
                    using (var adapter = new SQLiteDataAdapter(command))
                    {
                        adapter.Fill(dt);
                    }

                    Console.WriteLine($">>> GetInboundDailyStats 查询成功，返回 {dt.Rows.Count} 行");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"GetInboundDailyStats 查询失败: {ex.Message}");
            }
            return dt;
        }
        public DataTable GetDashboardSummary(DateTime startDate, DateTime endDate)
        {
            DataTable result = new DataTable();

            try
            {
                using (SQLiteConnection conn = new SQLiteConnection(ConnectionString))
                {
                    conn.Open();

                    // 先检查表有哪些字段
                    bool hasCreatedTime = ColumnExists(conn, "inbound_transactions", "created_time");
                    bool hasDateField = ColumnExists(conn, "inbound_transactions", "date");

                    Console.WriteLine($">>> inbound_transactions表字段检查:");
                    Console.WriteLine($">>>   created_time字段: {hasCreatedTime}");
                    Console.WriteLine($">>>   date字段: {hasDateField}");

                    string query = BuildDashboardQuery(hasCreatedTime, hasDateField);
                    Console.WriteLine($">>> 执行的查询: {query}");

                    using (SQLiteCommand cmd = new SQLiteCommand(query, conn))
                    {
                        using (SQLiteDataAdapter adapter = new SQLiteDataAdapter(cmd))
                        {
                            adapter.Fill(result);
                        }
                    }

                    Console.WriteLine($">>> 仪表板查询成功，返回 {result.Rows.Count} 行数据");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"获取仪表板数据失败: {ex.Message}");
                result = CreateEmptyDashboardTable();
            }

            return result;
        }

        private string BuildDashboardQuery(bool hasCreatedTime, bool hasDateField)
        {
            string timeField = "date"; // 默认使用date字段

            if (hasCreatedTime)
            {
                timeField = "created_time";
            }
            else if (hasDateField)
            {
                timeField = "date";
            }
            else
            {
                // 如果没有时间字段，返回简化查询
                return @"
            SELECT '今日入库' AS 统计项, 0 AS 金额, 0 AS 笔数
            UNION ALL
            SELECT '今日销售' AS 统计项, 0 AS 金额, 0 AS 笔数
            UNION ALL
            SELECT '本月入库' AS 统计项, 0 AS 金额, 0 AS 笔数
            UNION ALL
            SELECT '本月销售' AS 统计项, 0 AS 金额, 0 AS 笔数
            UNION ALL
            SELECT '累计客户' AS 统计项, 0 AS 金额, (SELECT COUNT(*) FROM clients WHERE status = 1) AS 笔数";
            }

            return $@"
        SELECT '今日入库' AS 统计项, 
               COALESCE(SUM(total_amount), 0) AS 金额,
               COUNT(*) AS 笔数
        FROM inbound_transactions 
        WHERE DATE({timeField}) = DATE('now')
        
        UNION ALL
        
        SELECT '今日销售' AS 统计项, 
               COALESCE(SUM(total_amount), 0) AS 金额,
               COUNT(*) AS 笔数
        FROM sales_transactions 
        WHERE DATE({timeField}) = DATE('now')
        
        UNION ALL
        
        SELECT '本月入库' AS 统计项, 
               COALESCE(SUM(total_amount), 0) AS 金额,
               COUNT(*) AS 笔数
        FROM inbound_transactions 
        WHERE strftime('%Y-%m', {timeField}) = strftime('%Y-%m', 'now')
        
        UNION ALL
        
        SELECT '本月销售' AS 统计项, 
               COALESCE(SUM(total_amount), 0) AS 金额,
               COUNT(*) AS 笔数
        FROM sales_transactions 
        WHERE strftime('%Y-%m', {timeField}) = strftime('%Y-%m', 'now')
        
        UNION ALL
        
        SELECT '累计客户' AS 统计项, 
               0 AS 金额,
               COUNT(*) AS 笔数
        FROM clients 
        WHERE status = 1";
        }
        private bool ColumnExists(SQLiteConnection conn, string tableName, string columnName)
        {
            try
            {
                string query = $"PRAGMA table_info({tableName})";
                using (SQLiteCommand cmd = new SQLiteCommand(query, conn))
                {
                    using (SQLiteDataReader reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            if (reader["name"].ToString().Equals(columnName, StringComparison.OrdinalIgnoreCase))
                            {
                                return true;
                            }
                        }
                    }
                }
                return false;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"检查列存在失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 包装出/进标志入库前统一为 TAKE 或 RETURN，与唯一索引一致。
        /// </summary>
        private static string NormalizePackFlagForStorage(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return "TAKE";
            var t = raw.Trim();
            var u = t.ToUpperInvariant();
            if (u == "TAKE" || u == "RETURN")
                return u;
            if (t.IndexOf('退') >= 0)
                return "RETURN";
            return "TAKE";
        }

        private static bool IndexExistsOnConnection(SQLiteConnection conn, string indexName)
        {
            using (var cmd = new SQLiteCommand("SELECT 1 FROM sqlite_master WHERE type='index' AND name=@n LIMIT 1", conn))
            {
                cmd.Parameters.AddWithValue("@n", indexName);
                return cmd.ExecuteScalar() != null;
            }
        }

        /// <summary>
        /// 业务唯一键含来源设备；包装表另含 pack_flag。单号不再拼接 @设备。
        /// </summary>
        private void EnsureTransactionBusinessUniqueKeys(SQLiteConnection conn)
        {
            try
            {
                MigrateBusinessUniqueForTable(conn, "inbound_transactions", "uidx_inbound_business",
                    @"CREATE UNIQUE INDEX IF NOT EXISTS uidx_inbound_business ON inbound_transactions(order_no, spec, COALESCE(source_device_id,''))");

                MigrateBusinessUniqueForTable(conn, "sales_transactions", "uidx_sales_business",
                    @"CREATE UNIQUE INDEX IF NOT EXISTS uidx_sales_business ON sales_transactions(order_no, spec, COALESCE(source_device_id,''))");

                if (TableExistsOnConnection(conn, "packaging_transactions") &&
                    !IndexExistsOnConnection(conn, "uidx_packaging_business"))
                {
                    NormalizePackagingPackFlagsBeforeUnique(conn);
                    MigrateBusinessUniqueForTable(conn, "packaging_transactions", "uidx_packaging_business",
                        @"CREATE UNIQUE INDEX IF NOT EXISTS uidx_packaging_business ON packaging_transactions(order_no, pack_type, COALESCE(pack_flag,'TAKE'), COALESCE(source_device_id,''))");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($">>> EnsureTransactionBusinessUniqueKeys 失败: {ex.Message}");
            }
        }

        private static void NormalizePackagingPackFlagsBeforeUnique(SQLiteConnection conn)
        {
            const string sql = @"
UPDATE packaging_transactions SET pack_flag = CASE
  WHEN pack_flag IS NULL OR TRIM(IFNULL(pack_flag,'')) = '' THEN 'TAKE'
  WHEN UPPER(TRIM(IFNULL(pack_flag,''))) = 'RETURN' THEN 'RETURN'
  WHEN INSTR(IFNULL(pack_flag,''), '退') > 0 THEN 'RETURN'
  ELSE 'TAKE' END";
            using (var cmd = new SQLiteCommand(sql, conn))
            {
                cmd.ExecuteNonQuery();
            }
        }

        private static bool TableExistsOnConnection(SQLiteConnection conn, string tableName)
        {
            using (var cmd = new SQLiteCommand("SELECT 1 FROM sqlite_master WHERE type='table' AND name=@t LIMIT 1", conn))
            {
                cmd.Parameters.AddWithValue("@t", tableName);
                return cmd.ExecuteScalar() != null;
            }
        }

        private static void DropSqliteAutoUniqueIndexes(SQLiteConnection conn, string tableName, SQLiteTransaction tx)
        {
            string safeTable = tableName.Replace("'", "''");
            using (var listCmd = new SQLiteCommand($"PRAGMA index_list('{safeTable}')", conn, tx))
            using (var reader = listCmd.ExecuteReader())
            {
                var toDrop = new List<string>();
                while (reader.Read())
                {
                    string name = reader["name"]?.ToString() ?? "";
                    int unique = Convert.ToInt32(reader["unique"]);
                    if (unique == 1 && name.StartsWith("sqlite_autoindex_", StringComparison.Ordinal))
                        toDrop.Add(name);
                }
                foreach (var n in toDrop)
                {
                    string safeName = n.Replace("\"", "\"\"");
                    using (var drop = new SQLiteCommand($"DROP INDEX IF EXISTS \"{safeName}\"", conn, tx))
                        drop.ExecuteNonQuery();
                }
            }
        }

        private static void MigrateBusinessUniqueForTable(SQLiteConnection conn, string tableName, string newIndexName, string createIndexSql)
        {
            if (!TableExistsOnConnection(conn, tableName))
                return;
            if (IndexExistsOnConnection(conn, newIndexName))
                return;

            // sqlite_autoindex_* 与表级 UNIQUE 约束绑定，无法 DROP；直接补建业务唯一索引即可。
            using (var cmd = new SQLiteCommand(createIndexSql, conn))
                cmd.ExecuteNonQuery();
        }

        private const string MigrationPackagingDropLegacyOrderPackUniqueV1 = "packaging_drop_legacy_order_pack_unique_v1";
        private const string MigrationInboundDropLegacyOrderSpecUniqueV1 = "inbound_drop_legacy_order_spec_unique_v1";
        private const string MigrationSalesDropLegacyOrderSpecUniqueV1 = "sales_drop_legacy_order_spec_unique_v1";
        private const string MigrationAdvancesDropLegacyBusinessUniqueV1 = "advances_drop_legacy_business_unique_v1";
        private const string MigrationDeductionsDropLegacyBusinessUniqueV1 = "deductions_drop_legacy_business_unique_v1";
        private const string MigrationAdvancesDeductionsBusinessUniqueAddCreatedTimeV1 =
            "advances_deductions_business_unique_add_created_time_v1";

        private static bool TableDdlHasUniquePair(SQLiteConnection conn, string tableName, string col1, string col2)
        {
            if (!TableExistsOnConnection(conn, tableName))
                return false;

            using (var cmd = new SQLiteCommand(
                "SELECT sql FROM sqlite_master WHERE type='table' AND name=@t LIMIT 1", conn))
            {
                cmd.Parameters.AddWithValue("@t", tableName);
                var ddl = cmd.ExecuteScalar()?.ToString() ?? "";
                return ddl.IndexOf($"UNIQUE({col1}, {col2})", StringComparison.OrdinalIgnoreCase) >= 0
                    || ddl.IndexOf($"UNIQUE ({col1}, {col2})", StringComparison.OrdinalIgnoreCase) >= 0;
            }
        }

        private static void RebuildTablePreserveData(SQLiteConnection conn, SQLiteTransaction tx, string tableName,
            string createTableSql, string[] indexSqls)
        {
            string oldName = tableName + "_old";
            using (var fkOff = new SQLiteCommand("PRAGMA foreign_keys=OFF", conn, tx))
                fkOff.ExecuteNonQuery();

            using (var rename = new SQLiteCommand($"ALTER TABLE {tableName} RENAME TO {oldName}", conn, tx))
                rename.ExecuteNonQuery();

            using (var create = new SQLiteCommand(createTableSql, conn, tx))
                create.ExecuteNonQuery();

            using (var copy = new SQLiteCommand($"INSERT INTO {tableName} SELECT * FROM {oldName}", conn, tx))
                copy.ExecuteNonQuery();

            using (var drop = new SQLiteCommand($"DROP TABLE {oldName}", conn, tx))
                drop.ExecuteNonQuery();

            if (indexSqls != null)
            {
                foreach (string sql in indexSqls)
                {
                    using (var idx = new SQLiteCommand(sql, conn, tx))
                        idx.ExecuteNonQuery();
                }
            }

            using (var fkOn = new SQLiteCommand("PRAGMA foreign_keys=ON", conn, tx))
                fkOn.ExecuteNonQuery();
        }

        private static void MarkSchemaMigrationApplied(SQLiteConnection conn, string migrationName)
        {
            using (var tx = conn.BeginTransaction())
            {
                if (!HasSchemaMigration(conn, migrationName, tx))
                    RecordSchemaMigration(conn, migrationName, tx);
                tx.Commit();
            }
        }

        private static bool PackagingTableHasLegacyOrderPackUnique(SQLiteConnection conn)
        {
            if (!TableExistsOnConnection(conn, "packaging_transactions"))
                return false;

            using (var cmd = new SQLiteCommand(
                "SELECT sql FROM sqlite_master WHERE type='table' AND name='packaging_transactions' LIMIT 1", conn))
            {
                var ddl = cmd.ExecuteScalar()?.ToString() ?? "";
                return ddl.IndexOf("UNIQUE(order_no, pack_type)", StringComparison.OrdinalIgnoreCase) >= 0
                    || ddl.IndexOf("UNIQUE (order_no, pack_type)", StringComparison.OrdinalIgnoreCase) >= 0;
            }
        }

        private static bool PrecheckPackagingBusinessUniqueForMigration(SQLiteConnection conn, SQLiteTransaction tx, out string error)
        {
            error = null;
            if (!TableExistsOnConnection(conn, "packaging_transactions"))
                return true;

            var counts = new Dictionary<string, int>(StringComparer.Ordinal);
            using (var cmd = new SQLiteCommand(
                "SELECT order_no, pack_type, pack_flag, source_device_id FROM packaging_transactions", conn, tx))
            using (var reader = cmd.ExecuteReader())
            {
                while (reader.Read())
                {
                    string on = reader["order_no"]?.ToString();
                    string pt = reader["pack_type"]?.ToString();
                    string pf = reader["pack_flag"]?.ToString();
                    string sd = reader["source_device_id"]?.ToString();
                    string key = PackagingDupKey(on, pt, pf, string.IsNullOrEmpty(sd) ? "" : sd);
                    if (!counts.TryGetValue(key, out int c))
                        c = 0;
                    counts[key] = c + 1;
                    if (counts[key] > 1)
                    {
                        error = $"包装表存在重复业务键 order_no+pack_type+pack_flag+设备 (示例: {on} / {pt})，请先人工去重后再升级。";
                        return false;
                    }
                }
            }
            return true;
        }

        private static void RebuildPackagingTransactionsWithoutLegacyUnique(SQLiteConnection conn, SQLiteTransaction tx)
        {
            using (var fkOff = new SQLiteCommand("PRAGMA foreign_keys=OFF", conn, tx))
                fkOff.ExecuteNonQuery();

            using (var rename = new SQLiteCommand("ALTER TABLE packaging_transactions RENAME TO packaging_transactions_old", conn, tx))
                rename.ExecuteNonQuery();

            const string createSql = @"
CREATE TABLE packaging_transactions (
    id INTEGER PRIMARY KEY AUTOINCREMENT NOT NULL,
    order_no TEXT NOT NULL,
    pack_type TEXT NOT NULL,
    client_code TEXT NULL,
    client_name TEXT NULL,
    quantity INTEGER DEFAULT 0,
    unit_price REAL DEFAULT 0,
    total_amount REAL DEFAULT 0,
    handler TEXT NULL,
    creator TEXT NULL,
    created_time DATETIME DEFAULT CURRENT_TIMESTAMP,
    updated_at DATETIME DEFAULT CURRENT_TIMESTAMP,
    is_settled INTEGER DEFAULT 0,
    settled_time DATETIME NULL,
    settled_by TEXT NULL,
    date TEXT NULL,
    status TEXT DEFAULT '处理中',
    remarks TEXT NULL,
    pack_flag TEXT DEFAULT 'TAKE',
    source_device_id TEXT NULL,
    source_record_id TEXT NULL
)";
            using (var create = new SQLiteCommand(createSql, conn, tx))
                create.ExecuteNonQuery();

            using (var copy = new SQLiteCommand(
                "INSERT INTO packaging_transactions SELECT * FROM packaging_transactions_old", conn, tx))
                copy.ExecuteNonQuery();

            using (var drop = new SQLiteCommand("DROP TABLE packaging_transactions_old", conn, tx))
                drop.ExecuteNonQuery();

            string[] indexSql =
            {
                "CREATE INDEX IF NOT EXISTS idx_packaging_order ON packaging_transactions(order_no)",
                "CREATE INDEX IF NOT EXISTS idx_packaging_type ON packaging_transactions(pack_type)",
                "CREATE INDEX IF NOT EXISTS idx_packaging_date ON packaging_transactions(created_time)",
                "CREATE INDEX IF NOT EXISTS idx_packaging_transactions_source_record ON packaging_transactions(source_record_id)"
            };
            foreach (string sql in indexSql)
            {
                using (var idx = new SQLiteCommand(sql, conn, tx))
                    idx.ExecuteNonQuery();
            }

            using (var fkOn = new SQLiteCommand("PRAGMA foreign_keys=ON", conn, tx))
                fkOn.ExecuteNonQuery();
        }

        /// <summary>
        /// 移除 packaging_transactions 旧版 UNIQUE(order_no, pack_type)，使不同 source_device_id 可并存。
        /// </summary>
        private void MigratePackagingDropLegacyOrderPackUnique(SQLiteConnection conn)
        {
            try
            {
                EnsureSchemaMigrationsTable(conn);
                if (HasSchemaMigration(conn, MigrationPackagingDropLegacyOrderPackUniqueV1))
                    return;

                if (!TableExistsOnConnection(conn, "packaging_transactions"))
                {
                    using (var tx = conn.BeginTransaction())
                    {
                        RecordSchemaMigration(conn, MigrationPackagingDropLegacyOrderPackUniqueV1, tx);
                        tx.Commit();
                    }
                    return;
                }

                if (!PackagingTableHasLegacyOrderPackUnique(conn))
                {
                    using (var tx = conn.BeginTransaction())
                    {
                        RecordSchemaMigration(conn, MigrationPackagingDropLegacyOrderPackUniqueV1, tx);
                        tx.Commit();
                    }
                    return;
                }

                using (var tx = conn.BeginTransaction())
                {
                    if (HasSchemaMigration(conn, MigrationPackagingDropLegacyOrderPackUniqueV1, tx))
                    {
                        tx.Commit();
                        return;
                    }

                    if (!PrecheckPackagingBusinessUniqueForMigration(conn, tx, out string err))
                    {
                        tx.Rollback();
                        Console.WriteLine($">>> 包装表迁移已跳过（预检失败）: {err}");
                        try
                        {
                            MessageBox.Show(
                                "包装表结构升级未执行：预检发现重复业务键。\n\n" + err +
                                "\n\n请备份数据库后人工去重，再重启程序。",
                                "数据迁移", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        }
                        catch { }
                        return;
                    }

                    RebuildPackagingTransactionsWithoutLegacyUnique(conn, tx);
                    RecordSchemaMigration(conn, MigrationPackagingDropLegacyOrderPackUniqueV1, tx);
                    tx.Commit();
                    Console.WriteLine(">>> 包装表迁移 packaging_drop_legacy_order_pack_unique_v1 已完成（已移除 UNIQUE(order_no, pack_type)）");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($">>> MigratePackagingDropLegacyOrderPackUnique 失败: {ex.Message}");
                try
                {
                    MessageBox.Show($"包装表结构升级失败：{ex.Message}", "数据迁移",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                catch { }
            }
        }

        private static bool PrecheckOrderSpecDeviceBusinessUnique(SQLiteConnection conn, SQLiteTransaction tx,
            string tableName, out string error)
        {
            error = null;
            if (!TableExistsOnConnection(conn, tableName))
                return true;

            var counts = new Dictionary<string, int>(StringComparer.Ordinal);
            string sql = $"SELECT order_no, spec, source_device_id FROM {tableName}";
            using (var cmd = new SQLiteCommand(sql, conn, tx))
            using (var reader = cmd.ExecuteReader())
            {
                while (reader.Read())
                {
                    string on = reader["order_no"]?.ToString() ?? "";
                    string sp = reader["spec"]?.ToString() ?? "";
                    string sd = reader["source_device_id"]?.ToString() ?? "";
                    string key = on + "\u0001" + sp + "\u0001" + sd;
                    if (!counts.TryGetValue(key, out int c))
                        c = 0;
                    counts[key] = c + 1;
                    if (counts[key] > 1)
                    {
                        error = $"{tableName} 存在重复业务键 order_no+spec+设备 (示例: {on} / {sp})，请先人工去重后再升级。";
                        return false;
                    }
                }
            }
            return true;
        }

        private void MigrateInboundDropLegacyOrderSpecUnique(SQLiteConnection conn)
        {
            try
            {
                if (HasSchemaMigration(conn, MigrationInboundDropLegacyOrderSpecUniqueV1))
                    return;

                if (!TableExistsOnConnection(conn, "inbound_transactions"))
                {
                    MarkSchemaMigrationApplied(conn, MigrationInboundDropLegacyOrderSpecUniqueV1);
                    return;
                }

                if (!TableDdlHasUniquePair(conn, "inbound_transactions", "order_no", "spec"))
                {
                    MarkSchemaMigrationApplied(conn, MigrationInboundDropLegacyOrderSpecUniqueV1);
                    return;
                }

                using (var tx = conn.BeginTransaction())
                {
                    if (HasSchemaMigration(conn, MigrationInboundDropLegacyOrderSpecUniqueV1, tx))
                    {
                        tx.Commit();
                        return;
                    }

                    if (!PrecheckOrderSpecDeviceBusinessUnique(conn, tx, "inbound_transactions", out string err))
                    {
                        tx.Rollback();
                        Console.WriteLine($">>> 入库表迁移已跳过（预检失败）: {err}");
                        try
                        {
                            MessageBox.Show("入库表结构升级未执行：预检发现重复业务键。\n\n" + err, "数据迁移",
                                MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        }
                        catch { }
                        return;
                    }

                    const string createSql = @"
CREATE TABLE inbound_transactions (
    id INTEGER PRIMARY KEY AUTOINCREMENT NOT NULL,
    order_no TEXT NOT NULL,
    spec TEXT NOT NULL,
    client_code TEXT NULL,
    client_name TEXT NULL,
    location TEXT NULL,
    date TEXT NULL,
    quantity INTEGER DEFAULT 0,
    unit_price REAL DEFAULT 0,
    total_amount REAL DEFAULT 0,
    handler TEXT NULL,
    creator TEXT NULL,
    created_time DATETIME DEFAULT CURRENT_TIMESTAMP,
    updated_at DATETIME DEFAULT CURRENT_TIMESTAMP,
    source_device_id TEXT NULL,
    source_record_id TEXT NULL
)";
                    string[] indexes =
                    {
                        "CREATE INDEX IF NOT EXISTS idx_inbound_order ON inbound_transactions(order_no)",
                        "CREATE INDEX IF NOT EXISTS idx_inbound_client ON inbound_transactions(client_code)",
                        "CREATE INDEX IF NOT EXISTS idx_inbound_date ON inbound_transactions(date)",
                        "CREATE INDEX IF NOT EXISTS idx_inbound_spec ON inbound_transactions(spec)",
                        "CREATE INDEX IF NOT EXISTS idx_inbound_transactions_source_record ON inbound_transactions(source_record_id)"
                    };
                    RebuildTablePreserveData(conn, tx, "inbound_transactions", createSql, indexes);
                    RecordSchemaMigration(conn, MigrationInboundDropLegacyOrderSpecUniqueV1, tx);
                    tx.Commit();
                    Console.WriteLine(">>> 入库表迁移 inbound_drop_legacy_order_spec_unique_v1 已完成");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($">>> MigrateInboundDropLegacyOrderSpecUnique 失败: {ex.Message}");
            }
        }

        private void MigrateSalesDropLegacyOrderSpecUnique(SQLiteConnection conn)
        {
            try
            {
                if (HasSchemaMigration(conn, MigrationSalesDropLegacyOrderSpecUniqueV1))
                    return;

                if (!TableExistsOnConnection(conn, "sales_transactions"))
                {
                    MarkSchemaMigrationApplied(conn, MigrationSalesDropLegacyOrderSpecUniqueV1);
                    return;
                }

                if (!TableDdlHasUniquePair(conn, "sales_transactions", "order_no", "spec"))
                {
                    MarkSchemaMigrationApplied(conn, MigrationSalesDropLegacyOrderSpecUniqueV1);
                    return;
                }

                using (var tx = conn.BeginTransaction())
                {
                    if (HasSchemaMigration(conn, MigrationSalesDropLegacyOrderSpecUniqueV1, tx))
                    {
                        tx.Commit();
                        return;
                    }

                    if (!PrecheckOrderSpecDeviceBusinessUnique(conn, tx, "sales_transactions", out string err))
                    {
                        tx.Rollback();
                        Console.WriteLine($">>> 销售表迁移已跳过（预检失败）: {err}");
                        try
                        {
                            MessageBox.Show("销售表结构升级未执行：预检发现重复业务键。\n\n" + err, "数据迁移",
                                MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        }
                        catch { }
                        return;
                    }

                    const string createSql = @"
CREATE TABLE sales_transactions (
    id INTEGER PRIMARY KEY AUTOINCREMENT NOT NULL,
    order_no TEXT NOT NULL,
    spec TEXT NOT NULL,
    client_code TEXT NULL,
    client_name TEXT NULL,
    location TEXT NULL,
    date TEXT NULL,
    quantity INTEGER DEFAULT 0,
    unit_price REAL DEFAULT 0,
    total_amount REAL DEFAULT 0,
    handler TEXT NULL,
    creator TEXT NULL,
    created_time DATETIME DEFAULT CURRENT_TIMESTAMP,
    updated_at DATETIME DEFAULT CURRENT_TIMESTAMP,
    status TEXT DEFAULT '处理中',
    remarks TEXT NULL,
    is_settled INTEGER DEFAULT 0,
    settled_time DATETIME NULL,
    settled_by TEXT NULL,
    source_device_id TEXT NULL,
    source_record_id TEXT NULL
)";
                    string[] indexes =
                    {
                        "CREATE INDEX IF NOT EXISTS idx_sales_order ON sales_transactions(order_no)",
                        "CREATE INDEX IF NOT EXISTS idx_sales_date ON sales_transactions(date)",
                        "CREATE INDEX IF NOT EXISTS idx_sales_spec ON sales_transactions(spec)",
                        "CREATE INDEX IF NOT EXISTS idx_sales_transactions_source_record ON sales_transactions(source_record_id)"
                    };
                    RebuildTablePreserveData(conn, tx, "sales_transactions", createSql, indexes);
                    RecordSchemaMigration(conn, MigrationSalesDropLegacyOrderSpecUniqueV1, tx);
                    tx.Commit();
                    Console.WriteLine(">>> 销售表迁移 sales_drop_legacy_order_spec_unique_v1 已完成");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($">>> MigrateSalesDropLegacyOrderSpecUnique 失败: {ex.Message}");
            }
        }

        private static bool PrecheckClientAmountDateDeviceUnique(SQLiteConnection conn, SQLiteTransaction tx,
            string tableName, string dateColumn, out string error)
        {
            error = null;
            if (!TableExistsOnConnection(conn, tableName))
                return true;

            var counts = new Dictionary<string, int>(StringComparer.Ordinal);
            string sql = $"SELECT client_code, amount, {dateColumn}, source_device_id FROM {tableName}";
            using (var cmd = new SQLiteCommand(sql, conn, tx))
            using (var reader = cmd.ExecuteReader())
            {
                while (reader.Read())
                {
                    string cc = reader["client_code"]?.ToString() ?? "";
                    string amt = reader["amount"]?.ToString() ?? "";
                    string dt = reader[dateColumn]?.ToString() ?? "";
                    string sd = reader["source_device_id"]?.ToString() ?? "";
                    string key = cc + "\u0001" + amt + "\u0001" + dt + "\u0001" + sd;
                    if (!counts.TryGetValue(key, out int c))
                        c = 0;
                    counts[key] = c + 1;
                    if (counts[key] > 1)
                    {
                        error = $"{tableName} 存在重复业务键 client_code+amount+日期+设备，请先人工去重后再升级。";
                        return false;
                    }
                }
            }
            return true;
        }

        private void MigrateAdvancesDropLegacyBusinessUnique(SQLiteConnection conn)
        {
            try
            {
                if (HasSchemaMigration(conn, MigrationAdvancesDropLegacyBusinessUniqueV1))
                    return;

                if (!TableExistsOnConnection(conn, "advances"))
                {
                    MarkSchemaMigrationApplied(conn, MigrationAdvancesDropLegacyBusinessUniqueV1);
                    return;
                }

                bool hasOldIndex = IndexExistsOnConnection(conn, "idx_advances_unique");
                if (!hasOldIndex)
                {
                    MarkSchemaMigrationApplied(conn, MigrationAdvancesDropLegacyBusinessUniqueV1);
                    return;
                }

                using (var tx = conn.BeginTransaction())
                {
                    if (HasSchemaMigration(conn, MigrationAdvancesDropLegacyBusinessUniqueV1, tx))
                    {
                        tx.Commit();
                        return;
                    }

                    if (!PrecheckClientAmountDateDeviceUnique(conn, tx, "advances", "advance_date", out string err))
                    {
                        tx.Rollback();
                        Console.WriteLine($">>> 预支表迁移已跳过（预检失败）: {err}");
                        return;
                    }

                    using (var drop = new SQLiteCommand("DROP INDEX IF EXISTS idx_advances_unique", conn, tx))
                        drop.ExecuteNonQuery();

                    RecordSchemaMigration(conn, MigrationAdvancesDropLegacyBusinessUniqueV1, tx);
                    tx.Commit();
                    Console.WriteLine(">>> 预支表迁移 advances_drop_legacy_business_unique_v1 已完成");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($">>> MigrateAdvancesDropLegacyBusinessUnique 失败: {ex.Message}");
            }
        }

        private void MigrateDeductionsDropLegacyBusinessUnique(SQLiteConnection conn)
        {
            try
            {
                if (HasSchemaMigration(conn, MigrationDeductionsDropLegacyBusinessUniqueV1))
                    return;

                if (!TableExistsOnConnection(conn, "deductions"))
                {
                    MarkSchemaMigrationApplied(conn, MigrationDeductionsDropLegacyBusinessUniqueV1);
                    return;
                }

                bool hasOldIndex = IndexExistsOnConnection(conn, "idx_deductions_unique");
                if (!hasOldIndex)
                {
                    MarkSchemaMigrationApplied(conn, MigrationDeductionsDropLegacyBusinessUniqueV1);
                    return;
                }

                using (var tx = conn.BeginTransaction())
                {
                    if (HasSchemaMigration(conn, MigrationDeductionsDropLegacyBusinessUniqueV1, tx))
                    {
                        tx.Commit();
                        return;
                    }

                    if (!PrecheckClientAmountDateDeviceUnique(conn, tx, "deductions", "deduct_date", out string err))
                    {
                        tx.Rollback();
                        Console.WriteLine($">>> 扣款表迁移已跳过（预检失败）: {err}");
                        return;
                    }

                    using (var drop = new SQLiteCommand("DROP INDEX IF EXISTS idx_deductions_unique", conn, tx))
                        drop.ExecuteNonQuery();

                    RecordSchemaMigration(conn, MigrationDeductionsDropLegacyBusinessUniqueV1, tx);
                    tx.Commit();
                    Console.WriteLine(">>> 扣款表迁移 deductions_drop_legacy_business_unique_v1 已完成");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($">>> MigrateDeductionsDropLegacyBusinessUnique 失败: {ex.Message}");
            }
        }

        private void MigrateAdvancesDeductionsBusinessUniqueAddCreatedTime(SQLiteConnection conn)
        {
            try
            {
                if (HasSchemaMigration(conn, MigrationAdvancesDeductionsBusinessUniqueAddCreatedTimeV1))
                    return;

                using (var tx = conn.BeginTransaction())
                {
                    if (HasSchemaMigration(conn, MigrationAdvancesDeductionsBusinessUniqueAddCreatedTimeV1, tx))
                    {
                        tx.Commit();
                        return;
                    }

                    using (var dropAdv = new SQLiteCommand("DROP INDEX IF EXISTS uidx_advances_business", conn, tx))
                        dropAdv.ExecuteNonQuery();
                    using (var dropDed = new SQLiteCommand("DROP INDEX IF EXISTS uidx_deductions_business", conn, tx))
                        dropDed.ExecuteNonQuery();

                    if (TableExistsOnConnection(conn, "advances"))
                    {
                        using (var createAdv = new SQLiteCommand(
                            @"CREATE UNIQUE INDEX IF NOT EXISTS uidx_advances_business
ON advances(client_code, amount, advance_date, created_time, COALESCE(source_device_id,''))", conn, tx))
                            createAdv.ExecuteNonQuery();
                    }

                    if (TableExistsOnConnection(conn, "deductions"))
                    {
                        using (var createDed = new SQLiteCommand(
                            @"CREATE UNIQUE INDEX IF NOT EXISTS uidx_deductions_business
ON deductions(client_code, amount, deduct_date, created_time, COALESCE(source_device_id,''))", conn, tx))
                            createDed.ExecuteNonQuery();
                    }

                    RecordSchemaMigration(conn, MigrationAdvancesDeductionsBusinessUniqueAddCreatedTimeV1, tx);
                    tx.Commit();
                    Console.WriteLine(">>> 预支/扣款业务唯一键已加入 created_time（秒）");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($">>> MigrateAdvancesDeductionsBusinessUniqueAddCreatedTime 失败: {ex.Message}");
            }
        }

        private void EnsureAdvanceDeductionPresaleBusinessUniqueKeys(SQLiteConnection conn)
        {
            try
            {
                if (TableExistsOnConnection(conn, "advances") &&
                    !IndexExistsOnConnection(conn, "uidx_advances_business"))
                {
                    using (var cmd = new SQLiteCommand(
                        @"CREATE UNIQUE INDEX IF NOT EXISTS uidx_advances_business ON advances(client_code, amount, advance_date, created_time, COALESCE(source_device_id,''))", conn))
                        cmd.ExecuteNonQuery();
                }

                if (TableExistsOnConnection(conn, "deductions") &&
                    !IndexExistsOnConnection(conn, "uidx_deductions_business"))
                {
                    using (var cmd = new SQLiteCommand(
                        @"CREATE UNIQUE INDEX IF NOT EXISTS uidx_deductions_business ON deductions(client_code, amount, deduct_date, created_time, COALESCE(source_device_id,''))", conn))
                        cmd.ExecuteNonQuery();
                }

                if (TableExistsOnConnection(conn, "presale_bills") &&
                    !IndexExistsOnConnection(conn, "uidx_presale_bills_business"))
                {
                    using (var cmd = new SQLiteCommand(
                        @"CREATE UNIQUE INDEX IF NOT EXISTS uidx_presale_bills_business ON presale_bills(bill_no, COALESCE(source_device_id,''))", conn))
                        cmd.ExecuteNonQuery();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($">>> EnsureAdvanceDeductionPresaleBusinessUniqueKeys 失败: {ex.Message}");
            }
        }

        private const string MigrationLegacyOrderNoDeviceSplitV1 = "legacy_order_no_device_split_v1";

        private static void EnsureSchemaMigrationsTable(SQLiteConnection conn, SQLiteTransaction tx = null)
        {
            const string sql = @"CREATE TABLE IF NOT EXISTS schema_migrations (
    name TEXT PRIMARY KEY NOT NULL,
    applied_at TEXT NOT NULL
)";
            using (var cmd = new SQLiteCommand(sql, conn, tx))
                cmd.ExecuteNonQuery();
        }

        private static bool HasSchemaMigration(SQLiteConnection conn, string name, SQLiteTransaction tx = null)
        {
            EnsureSchemaMigrationsTable(conn, tx);
            using (var cmd = new SQLiteCommand("SELECT 1 FROM schema_migrations WHERE name=@n LIMIT 1", conn, tx))
            {
                cmd.Parameters.AddWithValue("@n", name);
                return cmd.ExecuteScalar() != null;
            }
        }

        private static void RecordSchemaMigration(SQLiteConnection conn, string name, SQLiteTransaction tx)
        {
            using (var cmd = new SQLiteCommand(
                "INSERT INTO schema_migrations (name, applied_at) VALUES (@n, datetime('now'))", conn, tx))
            {
                cmd.Parameters.AddWithValue("@n", name);
                cmd.ExecuteNonQuery();
            }
        }

        /// <summary>
        /// 将历史 order_no 中「业务单号@设备」拆分为纯单号 + source_device_id，并同步 *_items.order_no。
        /// 仅执行一次；冲突时整库回滚并提示。
        /// </summary>
        private void NormalizeLegacyEmbeddedDeviceInOrderNo(SQLiteConnection conn)
        {
            try
            {
                if (HasSchemaMigration(conn, MigrationLegacyOrderNoDeviceSplitV1))
                    return;

                using (var tx = conn.BeginTransaction())
                {
                    EnsureSchemaMigrationsTable(conn, tx);
                    if (HasSchemaMigration(conn, MigrationLegacyOrderNoDeviceSplitV1, tx))
                    {
                        tx.Commit();
                        return;
                    }

                    string err;
                    if (!PrecheckSalesPostSplit(conn, tx, out err) ||
                        !PrecheckInboundPostSplit(conn, tx, out err) ||
                        !PrecheckPackagingPostSplit(conn, tx, out err))
                    {
                        tx.Rollback();
                        Console.WriteLine($">>> 单号拆分迁移已跳过（预检失败）: {err}");
                        try
                        {
                            MessageBox.Show(
                                "历史单号规范化未执行：预检发现拆号后会违反唯一约束或与现有数据冲突。\n\n" + err +
                                "\n\n请备份数据库后人工处理含「@」的单号，再重启程序。",
                                "数据迁移", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        }
                        catch { }
                        return;
                    }

                    ApplySalesOrderNoSplit(conn, tx);
                    ApplyInboundOrderNoSplit(conn, tx);
                    ApplyPackagingOrderNoSplit(conn, tx);

                    SyncItemsOrderNoFromParent(conn, tx, "inbound_items", "inbound_id", "inbound_transactions");
                    SyncItemsOrderNoFromParent(conn, tx, "sales_items", "sales_id", "sales_transactions");
                    SyncItemsOrderNoFromParent(conn, tx, "packaging_items", "packaging_id", "packaging_transactions");

                    RecordSchemaMigration(conn, MigrationLegacyOrderNoDeviceSplitV1, tx);
                    tx.Commit();
                    Console.WriteLine(">>> 单号拆分迁移 legacy_order_no_device_split_v1 已完成");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($">>> NormalizeLegacyEmbeddedDeviceInOrderNo 失败: {ex.Message}");
                try
                {
                    MessageBox.Show($"历史单号规范化失败：{ex.Message}", "数据迁移",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                catch { }
            }
        }

        private static int LastAtIndex(string s)
        {
            if (string.IsNullOrEmpty(s))
                return -1;
            return s.LastIndexOf('@');
        }

        /// <summary>
        /// 计算销售/入库行在拆分后的业务唯一键分量；无法安全拆分则保持原 order_no。
        /// </summary>
        private static void ComputeOrderNoSplit(string orderNo, string sourceDeviceId,
            out string effectiveOrderNo, out string effectiveDeviceId, out bool shouldUpdateRow)
        {
            shouldUpdateRow = false;
            effectiveOrderNo = (orderNo ?? "").Trim();
            var dev = string.IsNullOrWhiteSpace(sourceDeviceId) ? "" : sourceDeviceId.Trim();
            effectiveDeviceId = dev;

            int at = LastAtIndex(effectiveOrderNo);
            if (at <= 0 || at >= effectiveOrderNo.Length - 1)
                return;

            string prefix = effectiveOrderNo.Substring(0, at).Trim();
            string suffix = effectiveOrderNo.Substring(at + 1).Trim();
            if (string.IsNullOrEmpty(prefix))
                return;

            if (string.IsNullOrEmpty(dev))
            {
                effectiveOrderNo = prefix;
                effectiveDeviceId = suffix;
                shouldUpdateRow = true;
                return;
            }

            if (string.Equals(dev, suffix, StringComparison.OrdinalIgnoreCase))
            {
                effectiveOrderNo = prefix;
                effectiveDeviceId = dev;
                shouldUpdateRow = true;
            }
        }

        private static string SalesDupKey(string orderNo, string spec, string deviceId)
        {
            return (orderNo ?? "") + "\u0001" + (spec ?? "") + "\u0001" + (deviceId ?? "");
        }

        private static bool PrecheckInboundPostSplit(SQLiteConnection conn, SQLiteTransaction tx, out string error)
        {
            error = null;
            if (!TableExistsOnConnection(conn, "inbound_transactions"))
                return true;

            var counts = new Dictionary<string, int>(StringComparer.Ordinal);
            using (var cmd = new SQLiteCommand(
                "SELECT id, order_no, spec, source_device_id FROM inbound_transactions", conn, tx))
            using (var r = cmd.ExecuteReader())
            {
                while (r.Read())
                {
                    string on = r["order_no"]?.ToString();
                    string sp = r["spec"]?.ToString();
                    string sd = r["source_device_id"]?.ToString();
                    ComputeOrderNoSplit(on, sd, out string eOn, out string eDev, out _);
                    string key = SalesDupKey(eOn, sp, string.IsNullOrEmpty(eDev) ? "" : eDev);
                    if (!counts.TryGetValue(key, out int c))
                        c = 0;
                    counts[key] = c + 1;
                    if (counts[key] > 1)
                    {
                        error = $"入库表 inbound_transactions 拆号后键重复: order_no+spec+设备 冲突 (示例键片段: {eOn} / {sp})。";
                        return false;
                    }
                }
            }
            return true;
        }

        private static bool PrecheckSalesPostSplit(SQLiteConnection conn, SQLiteTransaction tx, out string error)
        {
            error = null;
            if (!TableExistsOnConnection(conn, "sales_transactions"))
                return true;

            var counts = new Dictionary<string, int>(StringComparer.Ordinal);
            using (var cmd = new SQLiteCommand(
                "SELECT id, order_no, spec, source_device_id FROM sales_transactions", conn, tx))
            using (var r = cmd.ExecuteReader())
            {
                while (r.Read())
                {
                    string on = r["order_no"]?.ToString();
                    string sp = r["spec"]?.ToString();
                    string sd = r["source_device_id"]?.ToString();
                    ComputeOrderNoSplit(on, sd, out string eOn, out string eDev, out _);
                    string key = SalesDupKey(eOn, sp, string.IsNullOrEmpty(eDev) ? "" : eDev);
                    if (!counts.TryGetValue(key, out int c))
                        c = 0;
                    counts[key] = c + 1;
                    if (counts[key] > 1)
                    {
                        error = $"销售表 sales_transactions 拆号后键重复: order_no+spec+设备 冲突 (示例: {eOn} / {sp})。";
                        return false;
                    }
                }
            }
            return true;
        }

        private static string PackagingDupKey(string orderNo, string packType, string packFlag, string deviceId)
        {
            string pf = NormalizePackFlagForStorage(packFlag);
            return (orderNo ?? "") + "\u0001" + (packType ?? "") + "\u0001" + pf + "\u0001" + (deviceId ?? "");
        }

        private static void ComputePackagingOrderNoSplit(string orderNo, string sourceDeviceId,
            out string effectiveOrderNo, out string effectiveDeviceId, out bool shouldUpdateRow)
        {
            ComputeOrderNoSplit(orderNo, sourceDeviceId, out effectiveOrderNo, out effectiveDeviceId, out shouldUpdateRow);
        }

        private static bool PrecheckPackagingPostSplit(SQLiteConnection conn, SQLiteTransaction tx, out string error)
        {
            error = null;
            if (!TableExistsOnConnection(conn, "packaging_transactions"))
                return true;

            var counts = new Dictionary<string, int>(StringComparer.Ordinal);
            using (var cmd = new SQLiteCommand(
                "SELECT id, order_no, pack_type, pack_flag, source_device_id FROM packaging_transactions", conn, tx))
            using (var r = cmd.ExecuteReader())
            {
                while (r.Read())
                {
                    string on = r["order_no"]?.ToString();
                    string pt = r["pack_type"]?.ToString();
                    string pf = r["pack_flag"]?.ToString();
                    string sd = r["source_device_id"]?.ToString();
                    ComputePackagingOrderNoSplit(on, sd, out string eOn, out string eDev, out _);
                    string key = PackagingDupKey(eOn, pt, pf, string.IsNullOrEmpty(eDev) ? "" : eDev);
                    if (!counts.TryGetValue(key, out int c))
                        c = 0;
                    counts[key] = c + 1;
                    if (counts[key] > 1)
                    {
                        error = $"包装表 packaging_transactions 拆号后键重复: order_no+pack_type+pack_flag+设备 冲突 (示例: {eOn} / {pt})。";
                        return false;
                    }
                }
            }
            return true;
        }

        private static void ApplyInboundOrderNoSplit(SQLiteConnection conn, SQLiteTransaction tx)
        {
            if (!TableExistsOnConnection(conn, "inbound_transactions"))
                return;

            using (var sel = new SQLiteCommand(
                "SELECT id, order_no, source_device_id FROM inbound_transactions", conn, tx))
            using (var r = sel.ExecuteReader())
            {
                var updates = new List<(long id, string on, object dev)>();
                while (r.Read())
                {
                    long id = Convert.ToInt64(r["id"]);
                    string on = r["order_no"]?.ToString();
                    string sd = r["source_device_id"]?.ToString();
                    ComputeOrderNoSplit(on, sd, out string eOn, out string eDev, out bool upd);
                    if (!upd)
                        continue;
                    updates.Add((id, eOn, string.IsNullOrEmpty(eDev) ? (object)DBNull.Value : eDev));
                }
                r.Close();

                using (var upd = new SQLiteCommand(
                    "UPDATE inbound_transactions SET order_no=@o, source_device_id=@d WHERE id=@id", conn, tx))
                {
                    foreach (var u in updates)
                    {
                        upd.Parameters.Clear();
                        upd.Parameters.AddWithValue("@o", u.on);
                        upd.Parameters.AddWithValue("@d", u.dev);
                        upd.Parameters.AddWithValue("@id", u.id);
                        upd.ExecuteNonQuery();
                    }
                }
            }
        }

        private static void ApplySalesOrderNoSplit(SQLiteConnection conn, SQLiteTransaction tx)
        {
            if (!TableExistsOnConnection(conn, "sales_transactions"))
                return;

            using (var sel = new SQLiteCommand(
                "SELECT id, order_no, source_device_id FROM sales_transactions", conn, tx))
            using (var r = sel.ExecuteReader())
            {
                var updates = new List<(long id, string on, object dev)>();
                while (r.Read())
                {
                    long id = Convert.ToInt64(r["id"]);
                    string on = r["order_no"]?.ToString();
                    string sd = r["source_device_id"]?.ToString();
                    ComputeOrderNoSplit(on, sd, out string eOn, out string eDev, out bool upd);
                    if (!upd)
                        continue;
                    updates.Add((id, eOn, string.IsNullOrEmpty(eDev) ? (object)DBNull.Value : eDev));
                }
                r.Close();

                using (var upd = new SQLiteCommand(
                    "UPDATE sales_transactions SET order_no=@o, source_device_id=@d WHERE id=@id", conn, tx))
                {
                    foreach (var u in updates)
                    {
                        upd.Parameters.Clear();
                        upd.Parameters.AddWithValue("@o", u.on);
                        upd.Parameters.AddWithValue("@d", u.dev);
                        upd.Parameters.AddWithValue("@id", u.id);
                        upd.ExecuteNonQuery();
                    }
                }
            }
        }

        private static void ApplyPackagingOrderNoSplit(SQLiteConnection conn, SQLiteTransaction tx)
        {
            if (!TableExistsOnConnection(conn, "packaging_transactions"))
                return;

            using (var sel = new SQLiteCommand(
                "SELECT id, order_no, source_device_id FROM packaging_transactions", conn, tx))
            using (var r = sel.ExecuteReader())
            {
                var updates = new List<(long id, string on, object dev)>();
                while (r.Read())
                {
                    long id = Convert.ToInt64(r["id"]);
                    string on = r["order_no"]?.ToString();
                    string sd = r["source_device_id"]?.ToString();
                    ComputePackagingOrderNoSplit(on, sd, out string eOn, out string eDev, out bool upd);
                    if (!upd)
                        continue;
                    updates.Add((id, eOn, string.IsNullOrEmpty(eDev) ? (object)DBNull.Value : eDev));
                }
                r.Close();

                using (var upd = new SQLiteCommand(
                    "UPDATE packaging_transactions SET order_no=@o, source_device_id=@d WHERE id=@id", conn, tx))
                {
                    foreach (var u in updates)
                    {
                        upd.Parameters.Clear();
                        upd.Parameters.AddWithValue("@o", u.on);
                        upd.Parameters.AddWithValue("@d", u.dev);
                        upd.Parameters.AddWithValue("@id", u.id);
                        upd.ExecuteNonQuery();
                    }
                }
            }
        }

        private static void SyncItemsOrderNoFromParent(SQLiteConnection conn, SQLiteTransaction tx,
            string itemsTable, string fkColumn, string parentTable)
        {
            if (!TableExistsOnConnection(conn, itemsTable) || !TableExistsOnConnection(conn, parentTable))
                return;

            string safeItems = itemsTable.Replace("\"", "\"\"");
            string safeParent = parentTable.Replace("\"", "\"\"");
            string safeFk = fkColumn.Replace("\"", "\"\"");
            string sql = $@"UPDATE ""{safeItems}"" SET order_no = (
    SELECT p.order_no FROM ""{safeParent}"" p WHERE p.id = ""{safeItems}"".""{safeFk}""
) WHERE EXISTS (SELECT 1 FROM ""{safeParent}"" p WHERE p.id = ""{safeItems}"".""{safeFk}"")";

            using (var cmd = new SQLiteCommand(sql, conn, tx))
                cmd.ExecuteNonQuery();
        }

        private void EnsureSourceColumns(SQLiteConnection conn, string tableName, bool ensureUniqueSourceRecord = false)
        {
            if (!ColumnExists(conn, tableName, "source_device_id"))
            {
                using (var cmd = new SQLiteCommand($"ALTER TABLE {tableName} ADD COLUMN source_device_id TEXT", conn))
                {
                    cmd.ExecuteNonQuery();
                }
            }

            if (!ColumnExists(conn, tableName, "source_record_id"))
            {
                using (var cmd = new SQLiteCommand($"ALTER TABLE {tableName} ADD COLUMN source_record_id TEXT", conn))
                {
                    cmd.ExecuteNonQuery();
                }
            }

            var idxName = $"idx_{tableName}_source_record";
            using (var idxCmd = new SQLiteCommand($"CREATE INDEX IF NOT EXISTS {idxName} ON {tableName}(source_record_id)", conn))
            {
                idxCmd.ExecuteNonQuery();
            }

            if (ensureUniqueSourceRecord)
            {
                var uniqueIdxName = $"uidx_{tableName}_source_record";
                using (var uniqueCmd = new SQLiteCommand($"CREATE UNIQUE INDEX IF NOT EXISTS {uniqueIdxName} ON {tableName}(source_record_id) WHERE source_record_id IS NOT NULL AND source_record_id <> ''", conn))
                {
                    uniqueCmd.ExecuteNonQuery();
                }
            }
        }

        /// <summary>
        /// 旧库若无 code 列则 ALTER 添加，并为空编号行补全 SPxx；保证存在唯一索引。
        /// </summary>
        private void EnsureProductTypesCodeColumn(SQLiteConnection connection)
        {
            try
            {
                if (!ColumnExists(connection, "product_types", "code"))
                {
                    using (var cmd = new SQLiteCommand("ALTER TABLE product_types ADD COLUMN code TEXT", connection))
                    {
                        cmd.ExecuteNonQuery();
                    }
                    Console.WriteLine(">>> product_types 已添加 code 列");
                }

                int nextSerial = 1;
                using (var maxCmd = new SQLiteCommand(@"
            SELECT MAX(CAST(SUBSTR(code, 3) AS INTEGER)) FROM product_types 
            WHERE code IS NOT NULL AND code LIKE 'SP%' AND LENGTH(code) >= 3", connection))
                {
                    var o = maxCmd.ExecuteScalar();
                    if (o != null && o != DBNull.Value && !string.IsNullOrWhiteSpace(o.ToString()) && int.TryParse(o.ToString(), out int maxN))
                        nextSerial = maxN + 1;
                }

                using (var selCmd = new SQLiteCommand(
                    "SELECT id FROM product_types WHERE code IS NULL OR TRIM(IFNULL(code,'')) = '' ORDER BY id", connection))
                using (var reader = selCmd.ExecuteReader())
                {
                    var ids = new List<int>();
                    while (reader.Read())
                        ids.Add(Convert.ToInt32(reader["id"]));

                    foreach (int id in ids)
                    {
                        string code;
                        for (;;)
                        {
                            code = $"SP{nextSerial:D2}";
                            using (var chk = new SQLiteCommand("SELECT COUNT(*) FROM product_types WHERE code = @c", connection))
                            {
                                chk.Parameters.AddWithValue("@c", code);
                                if (Convert.ToInt32(chk.ExecuteScalar()) == 0)
                                    break;
                            }
                            nextSerial++;
                        }
                        using (var upd = new SQLiteCommand("UPDATE product_types SET code = @c WHERE id = @id", connection))
                        {
                            upd.Parameters.AddWithValue("@c", code);
                            upd.Parameters.AddWithValue("@id", id);
                            upd.ExecuteNonQuery();
                        }
                        nextSerial++;
                    }
                }

                using (var idxCmd = new SQLiteCommand(
                    "CREATE UNIQUE INDEX IF NOT EXISTS idx_product_types_code ON product_types(code)", connection))
                {
                    idxCmd.ExecuteNonQuery();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($">>> EnsureProductTypesCodeColumn 失败: {ex.Message}");
                throw;
            }
        }

        private string GetDatabasePath()
        {
            try
            {
                if (!string.IsNullOrEmpty(_explicitDbPath))
                    return _explicitDbPath;

                if (FiscalYearService.IsInitialized)
                {
                    string yearPath = FiscalYearService.GetActiveDbPath();
                    Console.WriteLine($">>> 使用年份数据库: {yearPath}");
                    return yearPath;
                }

                // ========== 1. 定义所有可能的数据库位置（旧版兼容） ==========
                List<string> possiblePaths = new List<string>();

                possiblePaths.Add(Path.Combine(SafeDataPaths.DataDirectory, "lengkubao.db"));
                possiblePaths.Insert(0, Path.Combine(SafeDataPaths.DataDirectory, $"lengkubao_{DateTime.Now.Year}.db"));

                // 应用程序启动路径（exe所在目录）
                string startupPath = Application.StartupPath;
                possiblePaths.Add(Path.Combine(startupPath, "lengkubao.db"));
                possiblePaths.Add(Path.Combine(startupPath, "data", "lengkubao.db"));

                // 应用程序域基目录
                string basePath = AppDomain.CurrentDomain.BaseDirectory;
                possiblePaths.Add(Path.Combine(basePath, "lengkubao.db"));
                possiblePaths.Add(Path.Combine(basePath, "data", "lengkubao.db"));

                // 当前工作目录
                string currentDir = Environment.CurrentDirectory;
                possiblePaths.Add(Path.Combine(currentDir, "lengkubao.db"));

                // 用户数据目录（用于存储数据文件）
                string appDataPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "LengKuBao");
                possiblePaths.Add(Path.Combine(appDataPath, "lengkubao.db"));

                // 程序所在目录的上级目录（有时放在bin/Debug下）
                string parentPath = Path.GetDirectoryName(startupPath);
                if (!string.IsNullOrEmpty(parentPath))
                {
                    possiblePaths.Add(Path.Combine(parentPath, "lengkubao.db"));
                }

                // ========== 2. 遍历查找已存在的数据库 ==========
                foreach (string path in possiblePaths)
                {
                    if (File.Exists(path))
                    {
                        Console.WriteLine($">>> 找到数据库: {path}");
                        return path;
                    }
                }

                // ========== 3. 如果没有找到，在用户数据区创建 ==========
                string createPath = Path.Combine(SafeDataPaths.DataDirectory, "lengkubao.db");
                try
                {
                    SafeDataPaths.EnsureUserDirectories();
                    Console.WriteLine($">>> 将在用户数据区创建数据库: {createPath}");
                }
                catch
                {
                    createPath = Path.Combine(Application.StartupPath, "lengkubao.db");
                    Console.WriteLine($">>> 将在exe目录创建数据库: {createPath}");
                }

                // ========== 4. 确保目录存在 ==========
                string directory = Path.GetDirectoryName(createPath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                return createPath;
            }
            catch (Exception ex)
            {
                Console.WriteLine($">>> 获取数据库路径失败: {ex.Message}");
                // 降级方案：返回简单路径
                return "lengkubao.db";
            }
        }

        public string ConnectionString
        {
            get
            {
                string dbPath = GetDatabasePath();
                // SQLite 需要指定版本，Version=3 是标准配置
                return $"Data Source={dbPath};Version=3;";
            }
        }


        public DataTable GetInboundRecords(DateTime startDate, DateTime endDate, string clientCode = "")
        {
            var dataTable = new DataTable();
            try
            {
                using (var connection = new SQLiteConnection(connectionString))
                {
                    connection.Open();

                    string query = @"
                SELECT order_no, client_code, client_name, location, date, spec, quantity, handler, creator
                FROM inbound_transactions 
                WHERE date BETWEEN @startDate AND @endDate";

                    if (!string.IsNullOrEmpty(clientCode))
                    {
                        query += " AND client_code = @clientCode";
                    }

                    query += " ORDER BY date DESC, created_time DESC";

                    using (var command = new SQLiteCommand(query, connection))
                    {
                        command.Parameters.AddWithValue("@startDate", startDate.ToString("yyyy-MM-dd"));
                        command.Parameters.AddWithValue("@endDate", endDate.ToString("yyyy-MM-dd"));

                        if (!string.IsNullOrEmpty(clientCode))
                        {
                            command.Parameters.AddWithValue("@clientCode", clientCode);
                        }

                        var adapter = new SQLiteDataAdapter(command);
                        adapter.Fill(dataTable);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"查询入库记录失败: {ex.Message}");
            }
            return dataTable;
        }

        public DataTable GetSalesRecords(DateTime startDate, DateTime endDate, string clientCode = "")
        {
            var dataTable = new DataTable();
            try
            {
                using (var connection = new SQLiteConnection(connectionString))
                {
                    connection.Open();

                    string query = @"
                SELECT order_no, client_code, client_name, location, date, spec, quantity, unit_price, total_amount, handler
                FROM sales_transactions 
                WHERE date BETWEEN @startDate AND @endDate";

                    if (!string.IsNullOrEmpty(clientCode))
                    {
                        query += " AND client_code = @clientCode";
                    }

                    query += " ORDER BY date DESC, created_time DESC";

                    using (var command = new SQLiteCommand(query, connection))
                    {
                        command.Parameters.AddWithValue("@startDate", startDate.ToString("yyyy-MM-dd"));
                        command.Parameters.AddWithValue("@endDate", endDate.ToString("yyyy-MM-dd"));

                        if (!string.IsNullOrEmpty(clientCode))
                        {
                            command.Parameters.AddWithValue("@clientCode", clientCode);
                        }

                        var adapter = new SQLiteDataAdapter(command);
                        adapter.Fill(dataTable);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"查询销售记录失败: {ex.Message}");
            }
            return dataTable;
        }
        #endregion

        #region 数据库初始化
        private void InitializeDatabase(bool skipSeedData = false)
        {
            try
            {
                bool dbExists = File.Exists(GetDatabasePath());

                using (var connection = new SQLiteConnection(connectionString))
                {
                    connection.Open();

                    // ========== 1. 客户表 ==========
                    string createClientsTable = @"
CREATE TABLE IF NOT EXISTS clients (
    id INTEGER PRIMARY KEY AUTOINCREMENT NOT NULL,
    code TEXT NOT NULL UNIQUE,
    name TEXT NOT NULL,
    contact TEXT NULL,
    phone TEXT NULL,
    address TEXT NULL,
    status INTEGER DEFAULT 1,
    balance DECIMAL DEFAULT 0,
    updated_at DATETIME NULL
)";
                    ExecuteTableCreation(connection, createClientsTable);
                    ExecuteTableCreation(connection, "CREATE INDEX IF NOT EXISTS idx_clients_code ON clients(code)");
                    ExecuteTableCreation(connection, "CREATE INDEX IF NOT EXISTS idx_clients_name ON clients(name)");
                    ExecuteTableCreation(connection, "CREATE INDEX IF NOT EXISTS idx_clients_status ON clients(status)");

                    // ========== 2. 商品型号表 ==========
                    string createProductTypesTable = @"
CREATE TABLE IF NOT EXISTS product_types (
    id INTEGER PRIMARY KEY AUTOINCREMENT NOT NULL,
    name TEXT NOT NULL UNIQUE,
    is_active INTEGER DEFAULT 1,
    created_time DATETIME DEFAULT CURRENT_TIMESTAMP,
    code TEXT NULL
)";
                    ExecuteTableCreation(connection, createProductTypesTable);
                    ExecuteTableCreation(connection, "CREATE UNIQUE INDEX IF NOT EXISTS idx_product_types_code ON product_types(code)");

                    // ========== 3. 包装类型字典表 ==========
                    string createPackTypesTable = @"
CREATE TABLE IF NOT EXISTS pack_types (
    id INTEGER PRIMARY KEY AUTOINCREMENT NOT NULL,
    name TEXT NOT NULL UNIQUE,
    is_active INTEGER DEFAULT 1,
    created_time DATETIME DEFAULT CURRENT_TIMESTAMP
)";
                    ExecuteTableCreation(connection, createPackTypesTable);

                    // ========== 4. 库位表 ==========
                    string createLocationsTable = @"
CREATE TABLE IF NOT EXISTS locations (
    id INTEGER PRIMARY KEY AUTOINCREMENT NOT NULL,
    name TEXT NOT NULL UNIQUE,
    description TEXT NULL,
    status INTEGER DEFAULT 1,
    code TEXT NULL
)";
                    ExecuteTableCreation(connection, createLocationsTable);
                    ExecuteTableCreation(connection, "CREATE UNIQUE INDEX IF NOT EXISTS idx_locations_code ON locations(code)");

                    // ========== 5. 经手人表 ==========
                    string createHandlersTable = @"
CREATE TABLE IF NOT EXISTS handlers (
    id INTEGER PRIMARY KEY AUTOINCREMENT NOT NULL,
    name TEXT NOT NULL UNIQUE,
    status INTEGER DEFAULT 1,
    code TEXT NULL
)";
                    ExecuteTableCreation(connection, createHandlersTable);
                    ExecuteTableCreation(connection, "CREATE UNIQUE INDEX IF NOT EXISTS idx_handlers_code ON handlers(code)");

                    // ========== 6. 入库主表 ==========
                    string createInboundTransactionsTable = @"
CREATE TABLE IF NOT EXISTS inbound_transactions (
    id INTEGER PRIMARY KEY AUTOINCREMENT NOT NULL,
    order_no TEXT NOT NULL,
    spec TEXT NOT NULL,
    client_code TEXT NULL,
    client_name TEXT NULL,
    location TEXT NULL,
    date TEXT NULL,
    quantity INTEGER DEFAULT 0,
    unit_price REAL DEFAULT 0,
    total_amount REAL DEFAULT 0,
    handler TEXT NULL,
    creator TEXT NULL,
    created_time DATETIME DEFAULT CURRENT_TIMESTAMP,
    updated_at DATETIME DEFAULT CURRENT_TIMESTAMP,
    source_device_id TEXT NULL,
    source_record_id TEXT NULL
)";
                    ExecuteTableCreation(connection, createInboundTransactionsTable);
                    ExecuteTableCreation(connection, "CREATE INDEX IF NOT EXISTS idx_inbound_order ON inbound_transactions(order_no)");
                    ExecuteTableCreation(connection, "CREATE INDEX IF NOT EXISTS idx_inbound_client ON inbound_transactions(client_code)");
                    ExecuteTableCreation(connection, "CREATE INDEX IF NOT EXISTS idx_inbound_date ON inbound_transactions(date)");
                    ExecuteTableCreation(connection, "CREATE INDEX IF NOT EXISTS idx_inbound_spec ON inbound_transactions(spec)");
                    ExecuteTableCreation(connection, "CREATE INDEX IF NOT EXISTS idx_inbound_transactions_source_record ON inbound_transactions(source_record_id)");

                    // ========== 7. 入库明细表 ==========
                    string createInboundItemsTable = @"
CREATE TABLE IF NOT EXISTS inbound_items (
    id INTEGER PRIMARY KEY AUTOINCREMENT NOT NULL,
    inbound_id INTEGER NOT NULL,
    order_no TEXT NOT NULL,
    spec TEXT NOT NULL,
    quantity INTEGER NOT NULL,
    unit_price REAL NOT NULL,
    total_amount REAL NOT NULL,
    created_time DATETIME DEFAULT CURRENT_TIMESTAMP,
    FOREIGN KEY (inbound_id) REFERENCES inbound_transactions(id) ON DELETE CASCADE
)";
                    ExecuteTableCreation(connection, createInboundItemsTable);
                    ExecuteTableCreation(connection, "CREATE INDEX IF NOT EXISTS idx_items_inbound_id ON inbound_items(inbound_id)");
                    ExecuteTableCreation(connection, "CREATE INDEX IF NOT EXISTS idx_items_order_no ON inbound_items(order_no)");
                    ExecuteTableCreation(connection, "CREATE INDEX IF NOT EXISTS idx_items_spec ON inbound_items(spec)");

                    // ========== 8. 销售主表 ==========
                    string createSalesTransactionsTable = @"
CREATE TABLE IF NOT EXISTS sales_transactions (
    id INTEGER PRIMARY KEY AUTOINCREMENT NOT NULL,
    order_no TEXT NOT NULL,
    spec TEXT NOT NULL,
    client_code TEXT NULL,
    client_name TEXT NULL,
    location TEXT NULL,
    date TEXT NULL,
    quantity INTEGER DEFAULT 0,
    unit_price REAL DEFAULT 0,
    total_amount REAL DEFAULT 0,
    handler TEXT NULL,
    creator TEXT NULL,
    created_time DATETIME DEFAULT CURRENT_TIMESTAMP,
    updated_at DATETIME DEFAULT CURRENT_TIMESTAMP,
    status TEXT DEFAULT '处理中',
    remarks TEXT NULL,
    is_settled INTEGER DEFAULT 0,
    settled_time DATETIME NULL,
    settled_by TEXT NULL,
    source_device_id TEXT NULL,
    source_record_id TEXT NULL
)";
                    ExecuteTableCreation(connection, createSalesTransactionsTable);
                    ExecuteTableCreation(connection, "CREATE INDEX IF NOT EXISTS idx_sales_order ON sales_transactions(order_no)");
                    ExecuteTableCreation(connection, "CREATE INDEX IF NOT EXISTS idx_sales_date ON sales_transactions(date)");
                    ExecuteTableCreation(connection, "CREATE INDEX IF NOT EXISTS idx_sales_spec ON sales_transactions(spec)");
                    ExecuteTableCreation(connection, "CREATE INDEX IF NOT EXISTS idx_sales_transactions_source_record ON sales_transactions(source_record_id)");

                    // ========== 9. 销售明细表 ==========
                    string createSalesItemsTable = @"
CREATE TABLE IF NOT EXISTS sales_items (
    id INTEGER PRIMARY KEY AUTOINCREMENT NOT NULL,
    sales_id INTEGER NOT NULL,
    order_no TEXT NOT NULL,
    spec TEXT NOT NULL,
    quantity INTEGER NOT NULL,
    unit_price REAL NOT NULL,
    total_amount REAL NOT NULL,
    created_time DATETIME DEFAULT CURRENT_TIMESTAMP,
    FOREIGN KEY (sales_id) REFERENCES sales_transactions(id) ON DELETE CASCADE
)";
                    ExecuteTableCreation(connection, createSalesItemsTable);

                    // ========== 10. 包装主表 ==========
                    string createPackagingTransactionsTable = @"
CREATE TABLE IF NOT EXISTS packaging_transactions (
    id INTEGER PRIMARY KEY AUTOINCREMENT NOT NULL,
    order_no TEXT NOT NULL,
    pack_type TEXT NOT NULL,
    client_code TEXT NULL,
    client_name TEXT NULL,
    quantity INTEGER DEFAULT 0,
    unit_price REAL DEFAULT 0,
    total_amount REAL DEFAULT 0,
    handler TEXT NULL,
    creator TEXT NULL,
    created_time DATETIME DEFAULT CURRENT_TIMESTAMP,
    updated_at DATETIME DEFAULT CURRENT_TIMESTAMP,
    is_settled INTEGER DEFAULT 0,
    settled_time DATETIME NULL,
    settled_by TEXT NULL,
    date TEXT NULL,
    status TEXT DEFAULT '处理中',
    remarks TEXT NULL,
    pack_flag TEXT DEFAULT 'TAKE',
    source_device_id TEXT NULL,
    source_record_id TEXT NULL
)";
                    ExecuteTableCreation(connection, createPackagingTransactionsTable);
                    ExecuteTableCreation(connection, "CREATE INDEX IF NOT EXISTS idx_packaging_order ON packaging_transactions(order_no)");
                    ExecuteTableCreation(connection, "CREATE INDEX IF NOT EXISTS idx_packaging_type ON packaging_transactions(pack_type)");
                    ExecuteTableCreation(connection, "CREATE INDEX IF NOT EXISTS idx_packaging_date ON packaging_transactions(created_time)");
                    ExecuteTableCreation(connection, "CREATE INDEX IF NOT EXISTS idx_packaging_transactions_source_record ON packaging_transactions(source_record_id)");

                    // ========== 11. 包装明细表 ==========
                    string createPackagingItemsTable = @"
CREATE TABLE IF NOT EXISTS packaging_items (
    id INTEGER PRIMARY KEY AUTOINCREMENT NOT NULL,
    packaging_id INTEGER NOT NULL,
    order_no TEXT NOT NULL,
    pack_type TEXT NOT NULL,
    quantity INTEGER NOT NULL,
    unit_price REAL NOT NULL,
    total_amount REAL NOT NULL,
    created_time DATETIME DEFAULT CURRENT_TIMESTAMP,
    FOREIGN KEY (packaging_id) REFERENCES packaging_transactions(id) ON DELETE CASCADE
)";
                    ExecuteTableCreation(connection, createPackagingItemsTable);

                    // ========== 12. 扣款表 ==========
                    string createDeductionsTable = @"
CREATE TABLE IF NOT EXISTS deductions (
    id INTEGER PRIMARY KEY AUTOINCREMENT NOT NULL,
    client_code TEXT NOT NULL,
    client_name TEXT NOT NULL,
    amount REAL NOT NULL,
    quantity INTEGER DEFAULT 0,
    unit_price REAL DEFAULT 0,
    deduct_date TEXT NOT NULL,
    reason TEXT NULL,
    handler TEXT NULL,
    creator TEXT NULL,
    status INTEGER DEFAULT 1,
    created_time DATETIME DEFAULT CURRENT_TIMESTAMP
)";
                    ExecuteTableCreation(connection, createDeductionsTable);
                    ExecuteTableCreation(connection, "CREATE UNIQUE INDEX IF NOT EXISTS uidx_deductions_business ON deductions(client_code, amount, deduct_date, created_time, COALESCE(source_device_id,''))");

                    // ========== 13. 预支表 ==========
                    string createAdvancesTable = @"
CREATE TABLE IF NOT EXISTS advances (
    id INTEGER PRIMARY KEY AUTOINCREMENT NOT NULL,
    client_code TEXT NOT NULL,
    client_name TEXT NOT NULL,
    amount REAL NOT NULL,
    advance_date TEXT NOT NULL,
    reason TEXT NULL,
    handler TEXT NULL,
    creator TEXT NULL,
    status INTEGER DEFAULT 1,
    created_time DATETIME DEFAULT CURRENT_TIMESTAMP
)";
                    ExecuteTableCreation(connection, createAdvancesTable);
                    ExecuteTableCreation(connection, "CREATE UNIQUE INDEX IF NOT EXISTS uidx_advances_business ON advances(client_code, amount, advance_date, created_time, COALESCE(source_device_id,''))");

                    // ========== 14. 预售单表 ==========
                    string createPresaleBillsTable = @"
CREATE TABLE IF NOT EXISTS presale_bills (
    id INTEGER PRIMARY KEY AUTOINCREMENT NOT NULL,
    bill_no TEXT NOT NULL,
    buyer_code TEXT NOT NULL,
    buyer_name TEXT NOT NULL,
    location TEXT NULL,
    sale_mode TEXT NOT NULL,
    status TEXT NOT NULL,
    total_amount REAL NOT NULL DEFAULT 0,
    paid_amount REAL NOT NULL DEFAULT 0,
    handler TEXT NULL,
    remark TEXT NULL,
    date TEXT NULL,
    source_device_id TEXT NULL,
    source_record_id TEXT NULL,
    created_time DATETIME DEFAULT CURRENT_TIMESTAMP
)";
                    ExecuteTableCreation(connection, createPresaleBillsTable);
                    ExecuteTableCreation(connection, "CREATE INDEX IF NOT EXISTS idx_presale_bills_bill_no ON presale_bills(bill_no)");
                    ExecuteTableCreation(connection, "CREATE INDEX IF NOT EXISTS idx_presale_bills_date ON presale_bills(date)");
                    ExecuteTableCreation(connection, "CREATE UNIQUE INDEX IF NOT EXISTS uidx_presale_bills_source_record ON presale_bills(source_record_id) WHERE source_record_id IS NOT NULL AND source_record_id <> ''");

                    string createPresaleItemsTable = @"
CREATE TABLE IF NOT EXISTS presale_items (
    id INTEGER PRIMARY KEY AUTOINCREMENT NOT NULL,
    bill_no TEXT NOT NULL,
    spec TEXT NOT NULL,
    quantity INTEGER NOT NULL DEFAULT 0,
    unit_price REAL NOT NULL DEFAULT 0,
    total_amount REAL NOT NULL DEFAULT 0,
    source_record_id TEXT NULL,
    created_time DATETIME DEFAULT CURRENT_TIMESTAMP
)";
                    ExecuteTableCreation(connection, createPresaleItemsTable);
                    ExecuteTableCreation(connection, "CREATE INDEX IF NOT EXISTS idx_presale_items_bill_no ON presale_items(bill_no)");

                    string createPresalePaymentsTable = @"
CREATE TABLE IF NOT EXISTS presale_payments (
    id INTEGER PRIMARY KEY AUTOINCREMENT NOT NULL,
    bill_no TEXT NOT NULL,
    buyer_code TEXT NULL,
    amount REAL NOT NULL DEFAULT 0,
    pay_method TEXT NULL,
    pay_time TEXT NULL,
    remark TEXT NULL,
    source_device_id TEXT NULL,
    source_record_id TEXT NULL,
    created_time DATETIME DEFAULT CURRENT_TIMESTAMP
)";
                    ExecuteTableCreation(connection, createPresalePaymentsTable);
                    ExecuteTableCreation(connection, "CREATE INDEX IF NOT EXISTS idx_presale_payments_bill_no ON presale_payments(bill_no)");
                    ExecuteTableCreation(connection, "CREATE UNIQUE INDEX IF NOT EXISTS uidx_presale_payments_source_record ON presale_payments(source_record_id) WHERE source_record_id IS NOT NULL AND source_record_id <> ''");

                    string createPresaleOutboundTable = @"
CREATE TABLE IF NOT EXISTS presale_outbound (
    id INTEGER PRIMARY KEY AUTOINCREMENT NOT NULL,
    bill_no TEXT NOT NULL,
    buyer_code TEXT NULL,
    ship_time TEXT NULL,
    remark TEXT NULL,
    source_device_id TEXT NULL,
    source_record_id TEXT NULL,
    created_time DATETIME DEFAULT CURRENT_TIMESTAMP
)";
                    ExecuteTableCreation(connection, createPresaleOutboundTable);
                    ExecuteTableCreation(connection, "CREATE INDEX IF NOT EXISTS idx_presale_outbound_bill_no ON presale_outbound(bill_no)");
                    ExecuteTableCreation(connection, "CREATE UNIQUE INDEX IF NOT EXISTS uidx_presale_outbound_source_record ON presale_outbound(source_record_id) WHERE source_record_id IS NOT NULL AND source_record_id <> ''");

                    string createPresaleOutboundItemsTable = @"
CREATE TABLE IF NOT EXISTS presale_outbound_items (
    id INTEGER PRIMARY KEY AUTOINCREMENT NOT NULL,
    outbound_id INTEGER NOT NULL,
    bill_no TEXT NOT NULL,
    spec TEXT NOT NULL,
    quantity INTEGER NOT NULL DEFAULT 0,
    unit TEXT NULL,
    source_record_id TEXT NULL,
    created_time DATETIME DEFAULT CURRENT_TIMESTAMP
)";
                    ExecuteTableCreation(connection, createPresaleOutboundItemsTable);
                    ExecuteTableCreation(connection, "CREATE INDEX IF NOT EXISTS idx_presale_outbound_items_bill_no ON presale_outbound_items(bill_no)");
                    ExecuteTableCreation(connection, "CREATE INDEX IF NOT EXISTS idx_presale_outbound_items_outbound_id ON presale_outbound_items(outbound_id)");

                    EnsurePresaleItemShippedQuantityColumn(connection);

                    string createLedgerCategoryTable = @"
CREATE TABLE IF NOT EXISTS ledger_category (
    id INTEGER PRIMARY KEY AUTOINCREMENT NOT NULL,
    type TEXT NOT NULL,
    name TEXT NOT NULL,
    is_system INTEGER NOT NULL DEFAULT 0,
    enabled INTEGER NOT NULL DEFAULT 1,
    sort_order INTEGER NOT NULL DEFAULT 0
)";
                    ExecuteTableCreation(connection, createLedgerCategoryTable);

                    string createLedgerEntryTable = @"
CREATE TABLE IF NOT EXISTS ledger_entry (
    id INTEGER PRIMARY KEY AUTOINCREMENT NOT NULL,
    entry_no TEXT NOT NULL,
    type TEXT NOT NULL,
    category_id INTEGER NOT NULL DEFAULT 0,
    category_name TEXT NOT NULL,
    amount REAL NOT NULL DEFAULT 0,
    entry_date TEXT NOT NULL,
    remark TEXT NULL,
    status INTEGER NOT NULL DEFAULT 1,
    source_device_id TEXT NULL,
    source_record_id TEXT NULL,
    created_time DATETIME DEFAULT CURRENT_TIMESTAMP
)";
                    ExecuteTableCreation(connection, createLedgerEntryTable);
                    ExecuteTableCreation(connection, "CREATE INDEX IF NOT EXISTS idx_ledger_entry_date ON ledger_entry(entry_date)");
                    ExecuteTableCreation(connection, "CREATE INDEX IF NOT EXISTS idx_ledger_entry_type ON ledger_entry(type)");
                    ExecuteTableCreation(connection, "CREATE UNIQUE INDEX IF NOT EXISTS uidx_ledger_entry_source_record ON ledger_entry(source_record_id) WHERE source_record_id IS NOT NULL AND source_record_id <> ''");

                    EnsureDefaultLedgerCategories(connection);
                    EnsureRefrigerationFeeUnifyMigration(connection);

                    EnsureYearEndTablesInConnection(connection);

                    // ========== 15. 确保 product_types 有 code 列并填充 ==========
                    EnsureProductTypesCodeColumn(connection);

                    // ========== 16. 插入默认数据（新建年份库时跳过，改由源库复制基础资料） ==========
                    if (!skipSeedData)
                    {
                        string insertDefaultProducts = @"
INSERT OR IGNORE INTO product_types (code, name) VALUES 
('SP01', '42型'), 
('SP02', '45型'), 
('SP03', '48型'), 
('SP04', '60型'), 
('SP05', '精品型'), 
('SP06', '次型'), 
('SP07', '筐')";
                        ExecuteTableCreation(connection, insertDefaultProducts);

                        string insertDefaultPackTypes = @"
INSERT OR IGNORE INTO pack_types (name) VALUES 
('42箱'), 
('45箱'), 
('60箱'), 
('42全套'), 
('60全套'), 
('45全套'), 
('格垫'), 
('托盘'), 
('网垫'), 
('纸片'), 
('纸'), 
('网套'), 
('保鲜膜'), 
('48箱')";
                        ExecuteTableCreation(connection, insertDefaultPackTypes);
                    }

                    MigratePackagingDropLegacyOrderPackUnique(connection);
                    MigrateInboundDropLegacyOrderSpecUnique(connection);
                    MigrateSalesDropLegacyOrderSpecUnique(connection);
                    MigrateAdvancesDropLegacyBusinessUnique(connection);
                    MigrateDeductionsDropLegacyBusinessUnique(connection);
                    MigrateAdvancesDeductionsBusinessUniqueAddCreatedTime(connection);
                    EnsureTransactionBusinessUniqueKeys(connection);
                    EnsureAdvanceDeductionPresaleBusinessUniqueKeys(connection);
                    NormalizeLegacyEmbeddedDeviceInOrderNo(connection);
                    EnsureDeductionQuantityColumns(connection);

                    FixClientsTable();

                    Console.WriteLine("数据库初始化完成，所有表结构已创建");
                }

                if (!dbExists)
                {
                    MessageBox.Show("数据库初始化成功！", "系统提示",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"数据库初始化失败：{ex.Message}\n\n{ex.StackTrace}",
                    "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                Console.WriteLine($"数据库初始化异常: {ex}");
            }
        }


        private void EnsureDefaultLedgerCategories(SQLiteConnection connection)
        {
            try
            {
                int count = 0;
                using (var cmd = new SQLiteCommand("SELECT COUNT(*) FROM ledger_category", connection))
                {
                    count = Convert.ToInt32(cmd.ExecuteScalar());
                }
                if (count > 0) return;

                string insert = @"
INSERT INTO ledger_category (type, name, is_system, enabled, sort_order) VALUES
('INCOME', '包装费', 1, 1, 1),
('INCOME', '制冷费', 1, 1, 2),
('EXPENSE', '电费', 1, 1, 1),
('EXPENSE', '人工费', 1, 1, 2),
('EXPENSE', '设备维护', 1, 1, 3)";
                ExecuteTableCreation(connection, insert);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"初始化收支类目失败: {ex.Message}");
            }
        }

        private void EnsureRefrigerationFeeUnifyMigration(SQLiteConnection connection)
        {
            try
            {
                EnsureSchemaMigrationsTable(connection);
                if (HasSchemaMigration(connection, MigrationRefrigerationFeeUnifyV1))
                    return;

                using (var tx = connection.BeginTransaction())
                {
                    if (HasSchemaMigration(connection, MigrationRefrigerationFeeUnifyV1, tx))
                    {
                        tx.Commit();
                        return;
                    }

                    using (var cmd = new SQLiteCommand(
                        "UPDATE deductions SET reason=@new WHERE reason=@old", connection, tx))
                    {
                        cmd.Parameters.AddWithValue("@new", RefrigerationFeeReason);
                        cmd.Parameters.AddWithValue("@old", LegacyStorageFeeReason);
                        cmd.ExecuteNonQuery();
                    }

                    using (var cmd = new SQLiteCommand(
                        "UPDATE ledger_category SET name=@new WHERE type='INCOME' AND name=@old", connection, tx))
                    {
                        cmd.Parameters.AddWithValue("@new", RefrigerationFeeReason);
                        cmd.Parameters.AddWithValue("@old", "仓储费");
                        cmd.ExecuteNonQuery();
                    }

                    using (var cmd = new SQLiteCommand(
                        "UPDATE ledger_entry SET category_name=@new WHERE category_name=@old", connection, tx))
                    {
                        cmd.Parameters.AddWithValue("@new", RefrigerationFeeReason);
                        cmd.Parameters.AddWithValue("@old", "仓储费");
                        cmd.ExecuteNonQuery();
                    }

                    using (var cmd = new SQLiteCommand(@"
INSERT INTO ledger_category (type, name, is_system, enabled, sort_order)
SELECT 'INCOME', @name, 1, 1, 2
WHERE NOT EXISTS (SELECT 1 FROM ledger_category WHERE type='INCOME' AND name=@name)", connection, tx))
                    {
                        cmd.Parameters.AddWithValue("@name", RefrigerationFeeReason);
                        cmd.ExecuteNonQuery();
                    }

                    RecordSchemaMigration(connection, MigrationRefrigerationFeeUnifyV1, tx);
                    tx.Commit();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($">>> EnsureRefrigerationFeeUnifyMigration 失败: {ex.Message}");
            }
        }

        private void EnsureDeductionQuantityColumns(SQLiteConnection connection)
        {
            if (!ColumnExists(connection, "deductions", "quantity"))
            {
                ExecuteTableCreation(connection, "ALTER TABLE deductions ADD COLUMN quantity INTEGER DEFAULT 0");
            }

            if (!ColumnExists(connection, "deductions", "unit_price"))
            {
                ExecuteTableCreation(connection, "ALTER TABLE deductions ADD COLUMN unit_price REAL DEFAULT 0");
            }
        }

        // 辅助方法：执行表创建
        private void ExecuteTableCreation(SQLiteConnection connection, string sql)
        {
            try
            {
                using (var command = new SQLiteCommand(sql, connection))
                {
                    command.ExecuteNonQuery();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"执行SQL失败: {sql.Substring(0, Math.Min(100, sql.Length))}...");
                Console.WriteLine($"错误: {ex.Message}");
                // 不抛出异常，让其他表继续创建
            }
        }
        
        // 调试方法：查看客户表结构
        public void DebugClientsTable()
        {
            try
            {
                Console.WriteLine("=== 客户表结构调试 ===");

                // 查看表结构
                var schema = ExecuteQuery("PRAGMA table_info(clients)");
                Console.WriteLine("客户表结构:");
                foreach (DataRow row in schema.Rows)
                {
                    Console.WriteLine($"  {row["cid"]}: {row["name"]} ({row["type"]})");
                }

                // 查看数据
                var data = ExecuteQuery("SELECT * FROM clients LIMIT 5");
                Console.WriteLine($"客户数据（前{data.Rows.Count}条）:");
                foreach (DataRow row in data.Rows)
                {
                    Console.WriteLine($"  ID: {row["id"]}, Code: {row["code"]}, Name: {row["name"]}");
                }

                Console.WriteLine("=== 调试结束 ===");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"调试失败: {ex.Message}");
            }
        }

        // 检查客户是否存在
        public bool CheckClientExists(string code)
        {
            try
            {
                var dt = ExecuteQuery("SELECT COUNT(*) as cnt FROM clients WHERE code = @code",
                    new Dictionary<string, object> { { "@code", code } });

                if (dt.Rows.Count > 0)
                {
                    return Convert.ToInt32(dt.Rows[0]["cnt"]) > 0;
                }
                return false;
            }
            catch
            {
                return false;
            }
        }


        // 在Form1.cs中添加测试按钮
        private void btnTestDatabase_Click(object sender, EventArgs e)
        {
            try
            {
                var db = new DatabaseManager();

                // 测试1：检查基础表
                Console.WriteLine("=== 数据库连接测试 ===");

                // 测试客户数据
                var clients = db.GetAllClients();
                Console.WriteLine($"✅ 客户表: {clients.Rows.Count} 条记录");

                // 测试库位数据
                var locations = db.GetAllLocations();
                Console.WriteLine($"✅ 库位表: {locations.Rows.Count} 条记录");

                // 测试经手人数据
                var handlers = db.GetAllHandlers();
                Console.WriteLine($"✅ 经手人表: {handlers.Rows.Count} 条记录");

                MessageBox.Show($"数据库连接正常！\n客户: {clients.Rows.Count}\n库位: {locations.Rows.Count}\n经手人: {handlers.Rows.Count}",
                               "测试通过");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"数据库测试失败: {ex.Message}", "错误");
            }
        }
        // 添加这个方法到 dbManager 类
        public void DiagnoseClientStatus()
        {
            try
            {
                Console.WriteLine("=== 客户状态诊断 ===");

                // 1. 查看不同的status值及其数量
                string sql = @"
            SELECT 
                COALESCE(status, 'NULL') as status_value,
                COUNT(*) as count
            FROM clients 
            GROUP BY status
            ORDER BY status";

                DataTable result = ExecuteQuery(sql);

                Console.WriteLine("客户状态分布:");
                foreach (DataRow row in result.Rows)
                {
                    Console.WriteLine($"  Status={row["status_value"]}: {row["count"]} 条记录");
                }

                // 2. 查看前几条记录的完整信息
                string sampleSql = @"
            SELECT id, code, name, status, contact, phone
            FROM clients 
            LIMIT 10";

                DataTable sample = ExecuteQuery(sampleSql);

                Console.WriteLine("\n前10条客户记录:");
                foreach (DataRow row in sample.Rows)
                {
                    Console.WriteLine($"  ID:{row["id"]}, 编号:{row["code"]}, 名称:{row["name"]}, 状态:{row["status"]}");
                }

                Console.WriteLine("=== 诊断结束 ===");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"诊断失败: {ex.Message}");
            }
        }
        // 验证客户数据的方法
        public void VerifyClientsData()
        {
            try
            {
                Console.WriteLine("=== 验证客户数据 ===");

                // 1. 检查表是否存在
                string tableCheckSql = "SELECT name FROM sqlite_master WHERE type='table' AND name='clients'";
                DataTable tableCheck = ExecuteQuery(tableCheckSql);
                Console.WriteLine($">>> 客户表存在: {tableCheck.Rows.Count > 0}");

                if (tableCheck.Rows.Count == 0)
                {
                    Console.WriteLine(">>> ❌ 客户表不存在！");
                    InitializeClientsTable();
                    Console.WriteLine(">>> ✅ 已创建客户表");
                }

                // 2. 查询所有客户
                string allClientsSql = "SELECT COUNT(*) as count FROM clients";
                DataTable countResult = ExecuteQuery(allClientsSql);
                int totalCount = countResult.Rows.Count > 0 ? Convert.ToInt32(countResult.Rows[0]["count"]) : 0;
                Console.WriteLine($">>> 客户表总记录数: {totalCount}");

                // 3. 查询具体数据
                if (totalCount > 0)
                {
                    string sampleSql = "SELECT id, code, name, phone, address, balance FROM clients LIMIT 10";
                    DataTable sample = ExecuteQuery(sampleSql);

                    Console.WriteLine(">>> 客户数据示例:");
                    foreach (DataRow row in sample.Rows)
                    {
                        Console.WriteLine($"  ID:{row["id"]}, 编号:{row["code"]}, 名称:{row["name"]}, 电话:{row["phone"]}, 余额:{row["balance"]}");
                    }
                }
                else
                {
                    Console.WriteLine(">>> 客户表为空（未自动注入测试数据，请在客户管理中维护）");
                }

                Console.WriteLine("=== 验证完成 ===");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"验证失败: {ex.Message}");
            }
        }

        [Obsolete("已停用：不再自动注入测试客户，请使用客户管理功能维护数据。")]
        private void AddTestClients()
        {
        }
        // 在DatabaseManager.cs中添加这些简单的方法
        public DataTable GetInboundRecordsSimple()
        {
            DataTable dt = new DataTable();
            try
            {
                using (var connection = new SQLiteConnection(connectionString))
                {
                    connection.Open();
                    string sql = @"SELECT * FROM inbound_transactions ORDER BY created_time DESC";

                    using (var adapter = new SQLiteDataAdapter(sql, connection))
                    {
                        adapter.Fill(dt);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($">>> GetInboundRecordsSimple失败: {ex.Message}");
            }
            return dt;
        }

        public DataTable GetSalesRecordsSimple()
        {
            DataTable dt = new DataTable();
            try
            {
                using (var connection = new SQLiteConnection(connectionString))
                {
                    connection.Open();
                    string sql = @"SELECT * FROM sales_transactions ORDER BY created_time DESC";

                    using (var adapter = new SQLiteDataAdapter(sql, connection))
                    {
                        adapter.Fill(dt);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($">>> GetSalesRecordsSimple失败: {ex.Message}");
            }
            return dt;
        }

        public DataTable GetPackagingRecordsSimple()
        {
            DataTable dt = new DataTable();
            try
            {
                using (var connection = new SQLiteConnection(connectionString))
                {
                    connection.Open();
                    string sql = @"SELECT * FROM packaging_transactions ORDER BY created_time DESC";

                    using (var adapter = new SQLiteDataAdapter(sql, connection))
                    {
                        adapter.Fill(dt);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($">>> GetPackagingRecordsSimple失败: {ex.Message}");
            }
            return dt;
        }
        #region 客户对账全景视图 - 核心功能
        // 执行非查询SQL
        public int ExecuteNonQuery(string sql, Dictionary<string, object> parameters = null)
        {
            try
            {
                using (var connection = new SQLiteConnection(connectionString))
                {
                    connection.Open();
                    using (var command = new SQLiteCommand(sql, connection))
                    {
                        if (parameters != null)
                        {
                            foreach (var param in parameters)
                            {
                                command.Parameters.AddWithValue(param.Key, param.Value ?? DBNull.Value);
                            }
                        }
                        return command.ExecuteNonQuery();
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"执行SQL失败: {ex.Message}");
                return 0;
            }
        }

        // 获取客户对账全景数据
        // 获取客户对账全景数据
        public DataTable GetClientBalanceOverview(string clientCode, DateTime startDate, DateTime endDate)
        {
            DataTable result = new DataTable();
            result.Columns.Add("SalesTotal", typeof(decimal));
            result.Columns.Add("PackagingTotal", typeof(decimal));
            result.Columns.Add("DeductionTotal", typeof(decimal));
            result.Columns.Add("AdvanceTotal", typeof(decimal));
            result.Columns.Add("PayableTotal", typeof(decimal));

            try
            {
                using (var conn = new SQLiteConnection(connectionString))
                {
                    conn.Open();

                    decimal salesTotal = 0;
                    decimal packagingTotal = 0;
                    decimal deductionTotal = 0;
                    decimal advanceTotal = 0;

                    // 1. 计算销售总额
                    string salesSql = @"
                SELECT COALESCE(SUM(total_amount), 0) as Total 
                FROM sales_transactions 
                WHERE client_code = @clientCode 
                  AND date BETWEEN @startDate AND @endDate";

                    using (var cmd = new SQLiteCommand(salesSql, conn))
                    {
                        cmd.Parameters.AddWithValue("@clientCode", clientCode);
                        cmd.Parameters.AddWithValue("@startDate", startDate.ToString("yyyy-MM-dd"));
                        cmd.Parameters.AddWithValue("@endDate", endDate.ToString("yyyy-MM-dd 23:59:59"));

                        salesTotal = Convert.ToDecimal(cmd.ExecuteScalar());
                    }

                    // 2. 计算包装总额 - 只计算未结清的记录 (is_settled = 0 或 NULL)
                    string packagingSql = @"
                SELECT COALESCE(SUM(total_amount), 0) as Total 
                FROM packaging_transactions 
                WHERE client_code = @clientCode 
                  AND date BETWEEN @startDate AND @endDate
                  AND (is_settled = 0 OR is_settled IS NULL)";  // 只计算未结清的

                    using (var cmd = new SQLiteCommand(packagingSql, conn))
                    {
                        cmd.Parameters.AddWithValue("@clientCode", clientCode);
                        cmd.Parameters.AddWithValue("@startDate", startDate.ToString("yyyy-MM-dd"));
                        cmd.Parameters.AddWithValue("@endDate", endDate.ToString("yyyy-MM-dd"));

                        packagingTotal = Convert.ToDecimal(cmd.ExecuteScalar());
                    }

                    // 3. 计算扣款总额
                    string deductionSql = @"
                SELECT COALESCE(SUM(amount), 0) as Total 
                FROM deductions 
                WHERE client_code = @clientCode 
                  AND deduct_date BETWEEN @startDate AND @endDate";

                    using (var cmd = new SQLiteCommand(deductionSql, conn))
                    {
                        cmd.Parameters.AddWithValue("@clientCode", clientCode);
                        cmd.Parameters.AddWithValue("@startDate", startDate.ToString("yyyy-MM-dd"));
                        cmd.Parameters.AddWithValue("@endDate", endDate.ToString("yyyy-MM-dd"));

                        deductionTotal = Convert.ToDecimal(cmd.ExecuteScalar());
                    }

                    // 4. 计算预支总额
                    string advanceSql = @"
                SELECT COALESCE(SUM(amount), 0) as Total 
                FROM advances 
                WHERE client_code = @clientCode 
                  AND advance_date BETWEEN @startDate AND @endDate";

                    using (var cmd = new SQLiteCommand(advanceSql, conn))
                    {
                        cmd.Parameters.AddWithValue("@clientCode", clientCode);
                        cmd.Parameters.AddWithValue("@startDate", startDate.ToString("yyyy-MM-dd"));
                        cmd.Parameters.AddWithValue("@endDate", endDate.ToString("yyyy-MM-dd"));

                        advanceTotal = Convert.ToDecimal(cmd.ExecuteScalar());
                    }

                    // 计算应付总款
                    decimal openingBalance = GetClientOpeningBalanceForQuery(clientCode, startDate);
                    decimal payableTotal = openingBalance + salesTotal - packagingTotal - deductionTotal - advanceTotal;

                    // 添加到结果
                    result.Rows.Add(salesTotal, packagingTotal, deductionTotal, advanceTotal, payableTotal);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"获取对账全景失败: {ex.Message}");
                throw;
            }

            return result;
        }
        public DataTable GetClientTransactionDetails(string clientCode, DateTime startDate, DateTime endDate)
        {
            try
            {
                DataTable result = CreateClientTransactionDetailsTable();
                using (var conn = new SQLiteConnection(connectionString))
                {
                    conn.Open();
                    AppendOpeningBalanceRowIfAny(result, conn, clientCode, startDate);
                    FillClientTransactionDetailsRows(result, conn, clientCode, startDate, endDate);
                }
                return result;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"获取交易明细失败: {ex.Message}");
                throw;
            }
        }

        private static DataTable CreateClientTransactionDetailsTable()
        {
            DataTable result = new DataTable();
            result.Columns.Add("Type", typeof(string));
            result.Columns.Add("TransactionDate", typeof(DateTime));
            result.Columns.Add("OrderNo", typeof(string));
            result.Columns.Add("ItemType", typeof(string));
            result.Columns.Add("Quantity", typeof(decimal));
            result.Columns.Add("UnitPrice", typeof(decimal));
            result.Columns.Add("Amount", typeof(decimal));
            result.Columns.Add("Handler", typeof(string));
            result.Columns.Add("Reason", typeof(string));
            result.Columns.Add("Location", typeof(string));
            result.Columns.Add("IsSettled", typeof(bool));
            result.Columns.Add("PackFlag", typeof(string));
            return result;
        }

        private void AppendOpeningBalanceRowIfAny(DataTable result, SQLiteConnection conn, string clientCode, DateTime startDate)
        {
            decimal openingBalance = GetClientOpeningBalanceForQuery(clientCode, startDate);
            if (openingBalance == 0)
                return;

            DateTime openingDate = startDate.Date;
            try
            {
                using (var obCmd = new SQLiteCommand(@"
SELECT effective_date FROM client_opening_balance
WHERE client_code=@code AND effective_date <= @startDate
ORDER BY effective_date DESC LIMIT 1", conn))
                {
                    obCmd.Parameters.AddWithValue("@code", clientCode);
                    obCmd.Parameters.AddWithValue("@startDate", startDate.ToString("yyyy-MM-dd"));
                    object eff = obCmd.ExecuteScalar();
                    if (eff != null && eff != DBNull.Value)
                        openingDate = DateTime.Parse(eff.ToString());
                }
            }
            catch { }

            result.Rows.Add(
                "期初余额",
                openingDate,
                "期初结转",
                "",
                0m,
                0m,
                openingBalance,
                "",
                "",
                "",
                false,
                "");
        }

        private static void FillClientTransactionDetailsRows(
            DataTable result,
            SQLiteConnection conn,
            string clientCode,
            DateTime startDate,
            DateTime endDate)
        {
                    // 1. 查询销售记录
                    string salesSql = @"
SELECT 
    '销售' as Type,
    date as TransactionDate,
    order_no as OrderNo,
    spec as ItemType,
    quantity as Quantity,
    unit_price as UnitPrice,
    total_amount as Amount,
    handler as Handler,
    '' as Reason,
    location as Location,
    COALESCE(is_settled, 0) as IsSettled,
    NULL as PackFlag  -- 销售没有包装标记
FROM sales_transactions 
WHERE client_code = @clientCode 
  AND date BETWEEN @startDate AND @endDate
ORDER BY date DESC";

                    using (var cmd = new SQLiteCommand(salesSql, conn))
                    {
                        cmd.Parameters.AddWithValue("@clientCode", clientCode);
                        cmd.Parameters.AddWithValue("@startDate", startDate.ToString("yyyy-MM-dd"));
                        cmd.Parameters.AddWithValue("@endDate", endDate.ToString("yyyy-MM-dd 23:59:59"));

                        using (var adapter = new SQLiteDataAdapter(cmd))
                        {
                            DataTable salesTable = new DataTable();
                            adapter.Fill(salesTable);

                            foreach (DataRow row in salesTable.Rows)
                            {
                                result.Rows.Add(
                                    row["Type"],
                                    Convert.ToDateTime(row["TransactionDate"]),
                                    row["OrderNo"],
                                    row["ItemType"],
                                    row["Quantity"],
                                    row["UnitPrice"],
                                    row["Amount"],
                                    row["Handler"],
                                    row["Reason"],
                                    row["Location"],
                                    row["IsSettled"],
                                    row["PackFlag"]  // 添加 PackFlag
                                );
                            }
                        }
                    }

                    // 2. 查询包装记录 - 【修改】添加 PackFlag 字段
                    string packagingSql = @"
SELECT 
    '包装' as Type,
    date as TransactionDate,
    order_no as OrderNo,
    pack_type as ItemType,
    quantity as Quantity,
    unit_price as UnitPrice,
    total_amount as Amount,
    handler as Handler,
    COALESCE(remarks, '') as Reason,
    NULL as Location,
    COALESCE(is_settled, 0) as IsSettled,
    COALESCE(pack_flag, 'TAKE') as PackFlag  -- 【新增】获取包装标记，默认TAKE
FROM packaging_transactions 
WHERE client_code = @clientCode 
  AND date BETWEEN @startDate AND @endDate  -- 【修复】使用正确的日期范围
ORDER BY date DESC";

                    using (var cmd = new SQLiteCommand(packagingSql, conn))
                    {
                        cmd.Parameters.AddWithValue("@clientCode", clientCode);
                        cmd.Parameters.AddWithValue("@startDate", startDate.ToString("yyyy-MM-dd"));
                        cmd.Parameters.AddWithValue("@endDate", endDate.ToString("yyyy-MM-dd 23:59:59"));  // 【修复】添加时间部分

                        using (var adapter = new SQLiteDataAdapter(cmd))
                        {
                            DataTable packagingTable = new DataTable();
                            adapter.Fill(packagingTable);
                            Console.WriteLine($"包装查询到 {packagingTable.Rows.Count} 条记录");
                            
                            foreach (DataRow row in packagingTable.Rows)
                            {
                                result.Rows.Add(
                                    row["Type"],
                                    Convert.ToDateTime(row["TransactionDate"]),
                                    row["OrderNo"],
                                    row["ItemType"],
                                    row["Quantity"],
                                    row["UnitPrice"],
                                    row["Amount"],
                                    row["Handler"],
                                    row["Reason"],
                                    row["Location"],
                                    row["IsSettled"],
                                    row["PackFlag"]  // 【新增】添加 PackFlag
                                );
                            }
                        }
                    }

                    // 3. 查询扣款记录
                    string deductionSql = @"
SELECT 
    '扣款' as Type,
    deduct_date as TransactionDate,
    '' as OrderNo,
    '' as ItemType,
    CASE WHEN COALESCE(quantity, 0) > 0 THEN quantity ELSE 1 END as Quantity,
    CASE WHEN COALESCE(unit_price, 0) > 0 THEN unit_price ELSE amount END as UnitPrice,
    amount as Amount,
    handler as Handler,
    reason as Reason,
    NULL as Location,
    0 as IsSettled,
    NULL as PackFlag
FROM deductions 
WHERE client_code = @clientCode 
  AND deduct_date BETWEEN @startDate AND @endDate
ORDER BY deduct_date DESC";

                    using (var cmd = new SQLiteCommand(deductionSql, conn))
                    {
                        cmd.Parameters.AddWithValue("@clientCode", clientCode);
                        cmd.Parameters.AddWithValue("@startDate", startDate.ToString("yyyy-MM-dd"));
                        cmd.Parameters.AddWithValue("@endDate", endDate.ToString("yyyy-MM-dd 23:59:59"));

                        using (var adapter = new SQLiteDataAdapter(cmd))
                        {
                            DataTable deductionTable = new DataTable();
                            adapter.Fill(deductionTable);

                            foreach (DataRow row in deductionTable.Rows)
                            {
                                result.Rows.Add(
                                    row["Type"],
                                    Convert.ToDateTime(row["TransactionDate"]),
                                    row["OrderNo"],
                                    row["ItemType"],
                                    row["Quantity"],
                                    row["UnitPrice"],
                                    row["Amount"],
                                    row["Handler"],
                                    row["Reason"],
                                    row["Location"],
                                    row["IsSettled"],
                                    row["PackFlag"]
                                );
                            }
                        }
                    }

                    // 4. 查询预支记录
                    string advanceSql = @"
SELECT 
    '预支' as Type,
    advance_date as TransactionDate,
    '' as OrderNo,
    '' as ItemType,
    0 as Quantity,
    0 as UnitPrice,
    amount as Amount,
    handler as Handler,
    reason as Reason,
    NULL as Location,
    0 as IsSettled,
    NULL as PackFlag
FROM advances 
WHERE client_code = @clientCode 
  AND advance_date BETWEEN @startDate AND @endDate
ORDER BY advance_date DESC";

                    using (var cmd = new SQLiteCommand(advanceSql, conn))
                    {
                        cmd.Parameters.AddWithValue("@clientCode", clientCode);
                        cmd.Parameters.AddWithValue("@startDate", startDate.ToString("yyyy-MM-dd"));
                        cmd.Parameters.AddWithValue("@endDate", endDate.ToString("yyyy-MM-dd 23:59:59"));

                        using (var adapter = new SQLiteDataAdapter(cmd))
                        {
                            DataTable advanceTable = new DataTable();
                            adapter.Fill(advanceTable);

                            foreach (DataRow row in advanceTable.Rows)
                            {
                                result.Rows.Add(
                                    row["Type"],
                                    Convert.ToDateTime(row["TransactionDate"]),
                                    row["OrderNo"],
                                    row["ItemType"],
                                    row["Quantity"],
                                    row["UnitPrice"],
                                    row["Amount"],
                                    row["Handler"],
                                    row["Reason"],
                                    row["Location"],
                                    row["IsSettled"],
                                    row["PackFlag"]
                                );
                            }
                        }
                    }

                    // 按日期排序
                    if (result.Rows.Count > 0)
                    {
                        DataView dv = result.DefaultView;
                        dv.Sort = "TransactionDate DESC";
                        DataTable sorted = dv.ToTable();
                        result.Rows.Clear();
                        foreach (DataRow sortedRow in sorted.Rows)
                            result.ImportRow(sortedRow);
                    }
        }

        public DataTable DebugAllClientData(string clientCode, DateTime startDate, DateTime endDate)
        {
            try
            {
                using (var connection = new SQLiteConnection(connectionString))
                {
                    connection.Open();

                    string query = @"
-- 直接查询所有数据，不经过转换
SELECT 
    '销售' as 类型,
    order_no as 订单号,
    client_code as 客户代码,
    client_name as 客户名称,
    date as 日期,
    total_amount as 金额,
    handler as 经手人,
    creator as 创建人,
    created_time as 创建时间
FROM sales_transactions 
WHERE client_code = @clientCode
  AND date BETWEEN @startDate AND @endDate  -- 添加日期过滤

UNION ALL

SELECT 
    '包装' as 类型,
    order_no as 订单号,
    client_code as 客户代码,
    client_name as 客户名称,
    date as 日期,
    total_amount as 金额,
    handler as 经手人,
    creator as 创建人,
    created_time as 创建时间
FROM packaging_transactions 
WHERE client_code = @clientCode
  AND date BETWEEN @startDate AND @endDate  -- 添加日期过滤

UNION ALL

SELECT 
    '扣款' as 类型,
    '' as 订单号,
    client_code as 客户代码,
    client_name as 客户名称,
    deduct_date as 日期,
    amount as 金额,
    handler as 经手人,
    creator as 创建人,
    created_time as 创建时间
FROM deductions 
WHERE client_code = @clientCode
  AND deduct_date BETWEEN @startDate AND @endDate  -- 添加日期过滤

UNION ALL

SELECT 
    '预支' as 类型,
    '' as 订单号,
    client_code as 客户代码,
    client_name as 客户名称,
    advance_date as 日期,
    amount as 金额,
    handler as 经手人,
    creator as 创建人,
    created_time as 创建时间
FROM advances 
WHERE client_code = @clientCode
  AND advance_date BETWEEN @startDate AND @endDate  -- 添加日期过滤

ORDER BY 创建时间 DESC";

                    using (var command = new SQLiteCommand(query, connection))
                    {
                        command.Parameters.AddWithValue("@clientCode", clientCode);
                        command.Parameters.AddWithValue("@startDate", startDate.ToString("yyyy-MM-dd"));
                        command.Parameters.AddWithValue("@endDate", endDate.ToString("yyyy-MM-dd"));

                        DataTable dt = new DataTable();
                        using (var adapter = new SQLiteDataAdapter(command))
                        {
                            adapter.Fill(dt);
                        }

                        Console.WriteLine($"DebugAllClientData 查询到 {dt.Rows.Count} 条数据");
                        return dt;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"调试查询失败: {ex.Message}");
                return new DataTable();
            }
        }
        public void CheckSyncDataDates(string clientCode)
        {
            try
            {
                using (var connection = new SQLiteConnection(connectionString))
                {
                    connection.Open();

                    // 检查包装表日期字段的值
                    string query = @"
SELECT 
    order_no,
    date as 包装日期,
    created_time as 创建时间,
    creator as 创建人,
    CASE 
        WHEN date IS NULL OR date = '' THEN '日期为空'
        WHEN date NOT LIKE '____-__-__' THEN '日期格式不正确'
        ELSE '日期格式正确'
    END as 日期状态
FROM packaging_transactions 
WHERE client_code = @clientCode
  AND (creator LIKE '%同步%' OR creator LIKE '%手持%' OR creator LIKE '%mobile%')
ORDER BY created_time DESC";

                    using (var command = new SQLiteCommand(query, connection))
                    {
                        command.Parameters.AddWithValue("@clientCode", clientCode);

                        using (var reader = command.ExecuteReader())
                        {
                            Console.WriteLine($"=== 检查同步的包装数据 ===");
                            while (reader.Read())
                            {
                                Console.WriteLine($"{reader["order_no"]} - 包装日期: {reader["包装日期"]} - 创建时间: {reader["创建时间"]} - 状态: {reader["日期状态"]} - 创建人: {reader["创建人"]}");
                            }
                        }
                    }

                    // 检查销售表
                    query = @"
SELECT 
    order_no,
    date as 销售日期,
    created_time as 创建时间,
    creator as 创建人,
    CASE 
        WHEN date IS NULL OR date = '' THEN '日期为空'
        WHEN date NOT LIKE '____-__-__' THEN '日期格式不正确'
        ELSE '日期格式正确'
    END as 日期状态
FROM sales_transactions 
WHERE client_code = @clientCode
  AND (creator LIKE '%同步%' OR creator LIKE '%手持%' OR creator LIKE '%mobile%')
ORDER BY created_time DESC";

                    using (var command = new SQLiteCommand(query, connection))
                    {
                        command.Parameters.AddWithValue("@clientCode", clientCode);

                        using (var reader = command.ExecuteReader())
                        {
                            Console.WriteLine($"=== 检查同步的销售数据 ===");
                            while (reader.Read())
                            {
                                Console.WriteLine($"{reader["order_no"]} - 销售日期: {reader["销售日期"]} - 创建时间: {reader["创建时间"]} - 状态: {reader["日期状态"]} - 创建人: {reader["创建人"]}");
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"检查同步数据失败: {ex.Message}");
            }
        }
        // 在 DatabaseManager 类中添加
        public bool UpdateTransaction(string tableName, string clientCode,
            Dictionary<string, object> originalData, Dictionary<string, object> newData)
        {
            try
            {
                string dateField = tableName == "deductions" ? "deduct_date" : "advance_date";

                // 构建更新SQL
                StringBuilder sql = new StringBuilder($"UPDATE {tableName} SET ");

                // 添加更新字段
                var parameters = new Dictionary<string, object>();
                int paramCount = 0;

                foreach (var kvp in newData)
                {
                    string paramName = $"@new{paramCount}";
                    sql.Append($"{GetFieldName(kvp.Key, tableName)} = {paramName}, ");
                    parameters.Add(paramName, kvp.Value);
                    paramCount++;
                }

                sql.Append("updated_time = datetime('now'), updated_by = @operator ");

                // 添加WHERE条件（使用原始数据定位）
                sql.Append($" WHERE client_code = @clientCode ");

                parameters.Add("@clientCode", clientCode);

                // 添加原始数据条件
                foreach (var kvp in originalData)
                {
                    string paramName = $"@orig{paramCount}";
                    sql.Append($" AND {GetFieldName(kvp.Key, tableName)} = {paramName}");
                    parameters.Add(paramName, kvp.Value);
                    paramCount++;
                }

                parameters.Add("@operator", Environment.UserName);

                int rows = ExecuteNonQuery(sql.ToString(), parameters);
                return rows > 0;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"更新失败: {ex.Message}", "错误");
                return false;
            }
        }

        // 辅助方法：根据字段名获取数据库列名
        private string GetFieldName(string field, string tableName)
        {
            switch (field)
            {
                case "date":
                    return tableName == "deductions" ? "deduct_date" : "advance_date";
                case "amount":
                    return "amount";
                case "handler":
                    return "handler";
                case "reason":
                    return "reason";
                default:
                    return field;
            }
        }
        public bool UpdateDeduction(string clientCode, Dictionary<string, object> originalData,
    Dictionary<string, object> newData)
        {
            try
            {
                // 构建更新SQL - 移除 updated_time 和 updated_by 字段
                StringBuilder sql = new StringBuilder("UPDATE deductions SET ");

                var parameters = new Dictionary<string, object>();
                bool hasUpdateField = false;

                // 添加新值
                if (newData.ContainsKey("date"))
                {
                    sql.Append("deduct_date = @newDate, ");
                    parameters.Add("@newDate", ((DateTime)newData["date"]).ToString("yyyy-MM-dd"));
                    hasUpdateField = true;
                }

                if (newData.ContainsKey("amount"))
                {
                    sql.Append("amount = @newAmount, ");
                    parameters.Add("@newAmount", newData["amount"]);
                    hasUpdateField = true;
                }

                if (newData.ContainsKey("quantity"))
                {
                    sql.Append("quantity = @newQuantity, ");
                    parameters.Add("@newQuantity", newData["quantity"]);
                    hasUpdateField = true;
                }

                if (newData.ContainsKey("unit_price"))
                {
                    sql.Append("unit_price = @newUnitPrice, ");
                    parameters.Add("@newUnitPrice", newData["unit_price"]);
                    hasUpdateField = true;
                }

                if (newData.ContainsKey("handler"))
                {
                    sql.Append("handler = @newHandler, ");
                    parameters.Add("@newHandler", newData["handler"]);
                    hasUpdateField = true;
                }

                if (newData.ContainsKey("reason"))
                {
                    sql.Append("reason = @newReason, ");
                    parameters.Add("@newReason", newData["reason"]);
                    hasUpdateField = true;
                }

                if (!hasUpdateField)
                {
                    MessageBox.Show("没有需要更新的字段", "提示");
                    return false;
                }

                // 移除末尾的逗号和空格
                sql.Length -= 2;

                // WHERE 条件 - 使用原始数据定位记录
                sql.Append(" WHERE client_code = @clientCode ");
                parameters.Add("@clientCode", clientCode);

                // 添加原始数据条件
                List<string> whereConditions = new List<string>();

                if (originalData.ContainsKey("date"))
                {
                    whereConditions.Add("deduct_date = @origDate");
                    parameters.Add("@origDate", ((DateTime)originalData["date"]).ToString("yyyy-MM-dd"));
                }

                if (originalData.ContainsKey("amount"))
                {
                    whereConditions.Add("amount = @origAmount");
                    parameters.Add("@origAmount", originalData["amount"]);
                }

                if (originalData.ContainsKey("handler"))
                {
                    whereConditions.Add("handler = @origHandler");
                    parameters.Add("@origHandler", originalData["handler"]);
                }

                if (originalData.ContainsKey("reason"))
                {
                    whereConditions.Add("reason = @origReason");
                    parameters.Add("@origReason", originalData["reason"]);
                }

                // 添加所有WHERE条件
                foreach (string condition in whereConditions)
                {
                    sql.Append(" AND ").Append(condition);
                }

                // 调试：输出SQL语句
                Debug.WriteLine("更新扣款SQL: " + sql.ToString());
                Debug.WriteLine("参数: " + string.Join(", ", parameters.Select(kv => $"{kv.Key}={kv.Value}")));

                int rows = ExecuteNonQuery(sql.ToString(), parameters);

                Debug.WriteLine($"影响行数: {rows}");

                return rows > 0;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"更新扣款失败: {ex.Message}", "错误");
                Debug.WriteLine($"更新扣款异常: {ex.Message}\n{ex.StackTrace}");
                return false;
            }
        }

        public bool UpdateAdvance(string clientCode, Dictionary<string, object> originalData,
            Dictionary<string, object> newData)
        {
            try
            {
                // 构建更新SQL - 移除 updated_time 和 updated_by 字段
                StringBuilder sql = new StringBuilder("UPDATE advances SET ");

                var parameters = new Dictionary<string, object>();
                bool hasUpdateField = false;

                // 添加新值
                if (newData.ContainsKey("date"))
                {
                    sql.Append("advance_date = @newDate, ");
                    parameters.Add("@newDate", ((DateTime)newData["date"]).ToString("yyyy-MM-dd"));
                    hasUpdateField = true;
                }

                if (newData.ContainsKey("amount"))
                {
                    sql.Append("amount = @newAmount, ");
                    parameters.Add("@newAmount", newData["amount"]);
                    hasUpdateField = true;
                }

                if (newData.ContainsKey("handler"))
                {
                    sql.Append("handler = @newHandler, ");
                    parameters.Add("@newHandler", newData["handler"]);
                    hasUpdateField = true;
                }

                if (newData.ContainsKey("reason"))
                {
                    sql.Append("reason = @newReason, ");
                    parameters.Add("@newReason", newData["reason"]);
                    hasUpdateField = true;
                }

                if (!hasUpdateField)
                {
                    MessageBox.Show("没有需要更新的字段", "提示");
                    return false;
                }

                // 移除末尾的逗号和空格
                sql.Length -= 2;

                // WHERE 条件
                sql.Append(" WHERE client_code = @clientCode ");
                parameters.Add("@clientCode", clientCode);

                // 添加原始数据条件
                List<string> whereConditions = new List<string>();

                if (originalData.ContainsKey("date"))
                {
                    whereConditions.Add("advance_date = @origDate");
                    parameters.Add("@origDate", ((DateTime)originalData["date"]).ToString("yyyy-MM-dd"));
                }

                if (originalData.ContainsKey("amount"))
                {
                    whereConditions.Add("amount = @origAmount");
                    parameters.Add("@origAmount", originalData["amount"]);
                }

                if (originalData.ContainsKey("handler"))
                {
                    whereConditions.Add("handler = @origHandler");
                    parameters.Add("@origHandler", originalData["handler"]);
                }

                if (originalData.ContainsKey("reason"))
                {
                    whereConditions.Add("reason = @origReason");
                    parameters.Add("@origReason", originalData["reason"]);
                }

                // 添加所有WHERE条件
                foreach (string condition in whereConditions)
                {
                    sql.Append(" AND ").Append(condition);
                }

                // 调试信息
                Debug.WriteLine("更新预支SQL: " + sql.ToString());
                Debug.WriteLine("参数: " + string.Join(", ", parameters.Select(kv => $"{kv.Key}={kv.Value}")));

                int rows = ExecuteNonQuery(sql.ToString(), parameters);

                Debug.WriteLine($"影响行数: {rows}");

                return rows > 0;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"更新预支失败: {ex.Message}", "错误");
                Debug.WriteLine($"更新预支异常: {ex.Message}\n{ex.StackTrace}");
                return false;
            }
        }

        // 添加扣款记录
        public bool AddDeduction(string clientCode, string clientName, decimal amount,
            DateTime deductDate, string reason, string handler, string creator,
            int quantity = 0, decimal unitPrice = 0)
        {
            try
            {
                string normalizedReason = IsRefrigerationFeeReason(reason)
                    ? RefrigerationFeeReason
                    : (reason ?? "").Trim();

                using (var connection = new SQLiteConnection(connectionString))
                {
                    connection.Open();
                    EnsureDeductionQuantityColumns(connection);

                    using (var tx = connection.BeginTransaction())
                    {
                        try
                        {
                            string query = @"INSERT INTO deductions 
                            (client_code, client_name, amount, quantity, unit_price, deduct_date, reason, handler, creator) 
                            VALUES (@clientCode, @clientName, @amount, @quantity, @unitPrice, @deductDate, @reason, @handler, @creator)";

                            using (var command = new SQLiteCommand(query, connection, tx))
                            {
                                command.Parameters.AddWithValue("@clientCode", clientCode);
                                command.Parameters.AddWithValue("@clientName", clientName);
                                command.Parameters.AddWithValue("@amount", amount);
                                command.Parameters.AddWithValue("@quantity", quantity);
                                command.Parameters.AddWithValue("@unitPrice", unitPrice);
                                command.Parameters.AddWithValue("@deductDate", deductDate.ToString("yyyy-MM-dd"));
                                command.Parameters.AddWithValue("@reason", normalizedReason);
                                command.Parameters.AddWithValue("@handler", handler);
                                command.Parameters.AddWithValue("@creator", creator);

                                if (command.ExecuteNonQuery() <= 0)
                                {
                                    tx.Rollback();
                                    return false;
                                }
                            }

                            long deductionId = connection.LastInsertRowId;
                            if (IsRefrigerationFeeReason(normalizedReason))
                            {
                                EnsureRefrigerationFeeLedgerForDeduction(
                                    connection, tx, deductionId, clientName, amount, deductDate, quantity, unitPrice);
                            }

                            tx.Commit();
                            return true;
                        }
                        catch (Exception ex)
                        {
                            try { tx.Rollback(); } catch { }
                            Console.WriteLine($"添加扣款记录失败: {ex.Message}");
                            return false;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"添加扣款记录失败: {ex.Message}");
                return false;
            }
        }

        public bool HasRefrigerationFeeDeductionInRange(string clientCode, DateTime dateFrom, DateTime dateTo)
        {
            try
            {
                using (var connection = new SQLiteConnection(connectionString))
                {
                    connection.Open();
                    using (var cmd = new SQLiteCommand(@"
SELECT COUNT(*) FROM deductions
WHERE client_code = @clientCode
  AND deduct_date BETWEEN @dateFrom AND @dateTo
  AND (reason = @reason1 OR reason = @reason2)", connection))
                    {
                        cmd.Parameters.AddWithValue("@clientCode", clientCode);
                        cmd.Parameters.AddWithValue("@dateFrom", dateFrom.ToString("yyyy-MM-dd"));
                        cmd.Parameters.AddWithValue("@dateTo", dateTo.ToString("yyyy-MM-dd"));
                        cmd.Parameters.AddWithValue("@reason1", RefrigerationFeeReason);
                        cmd.Parameters.AddWithValue("@reason2", LegacyStorageFeeReason);
                        return Convert.ToInt32(cmd.ExecuteScalar()) > 0;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"查询制冷费扣款失败: {ex.Message}");
                return false;
            }
        }

        public bool DeleteDeductionWithLedgerByBusinessKey(string clientCode, DateTime date, decimal amount,
            string handler, string reason)
        {
            try
            {
                using (var connection = new SQLiteConnection(connectionString))
                {
                    connection.Open();
                    using (var tx = connection.BeginTransaction())
                    {
                        long? deductionId = null;
                        string dbReason = reason;
                        using (var findCmd = new SQLiteCommand(@"
SELECT id, reason FROM deductions
WHERE client_code = @clientCode
  AND deduct_date = @date
  AND amount = @amount
  AND handler = @handler
  AND reason = @reason
LIMIT 1", connection, tx))
                        {
                            findCmd.Parameters.AddWithValue("@clientCode", clientCode);
                            findCmd.Parameters.AddWithValue("@date", date.ToString("yyyy-MM-dd"));
                            findCmd.Parameters.AddWithValue("@amount", amount);
                            findCmd.Parameters.AddWithValue("@handler", handler);
                            findCmd.Parameters.AddWithValue("@reason", reason);
                            using (var reader = findCmd.ExecuteReader())
                            {
                                if (reader.Read())
                                {
                                    deductionId = reader.GetInt64(0);
                                    dbReason = reader.IsDBNull(1) ? reason : reader.GetString(1);
                                }
                            }
                        }

                        if (!deductionId.HasValue)
                        {
                            tx.Rollback();
                            return false;
                        }

                        if (IsRefrigerationFeeReason(dbReason))
                            DeleteLedgerEntryForDeduction(connection, tx, deductionId.Value);

                        using (var delCmd = new SQLiteCommand("DELETE FROM deductions WHERE id = @id", connection, tx))
                        {
                            delCmd.Parameters.AddWithValue("@id", deductionId.Value);
                            if (delCmd.ExecuteNonQuery() <= 0)
                            {
                                tx.Rollback();
                                return false;
                            }
                        }

                        tx.Commit();
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"删除扣款记录失败: {ex.Message}");
                return false;
            }
        }

        private static void DeleteLedgerEntryForDeduction(SQLiteConnection connection, SQLiteTransaction tx, long deductionId)
        {
            string sourceRecordId = "DEDUCTION:" + deductionId;
            using (var cmd = new SQLiteCommand(
                "DELETE FROM ledger_entry WHERE source_record_id = @sourceRecordId", connection, tx))
            {
                cmd.Parameters.AddWithValue("@sourceRecordId", sourceRecordId);
                cmd.ExecuteNonQuery();
            }
        }

        private void EnsureRefrigerationFeeLedgerForDeduction(
            SQLiteConnection connection,
            SQLiteTransaction tx,
            long deductionId,
            string clientName,
            decimal amount,
            DateTime deductDate,
            int quantity,
            decimal unitPrice)
        {
            string sourceRecordId = "DEDUCTION:" + deductionId;
            using (var existsCmd = new SQLiteCommand(
                "SELECT COUNT(*) FROM ledger_entry WHERE source_record_id = @sourceRecordId", connection, tx))
            {
                existsCmd.Parameters.AddWithValue("@sourceRecordId", sourceRecordId);
                if (Convert.ToInt32(existsCmd.ExecuteScalar()) > 0)
                    return;
            }

            long categoryId = GetOrEnsureRefrigerationFeeCategoryId(connection, tx);
            string entryNo = GenerateLedgerEntryNo(connection, tx, deductDate);
            string remark = BuildRefrigerationFeeLedgerRemark(clientName, quantity, unitPrice);
            string createdTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

            using (var cmd = new SQLiteCommand(@"
INSERT INTO ledger_entry (
    entry_no, type, category_id, category_name, amount, entry_date, remark, status,
    source_device_id, source_record_id, created_time
) VALUES (
    @entryNo, 'INCOME', @categoryId, @categoryName, @amount, @entryDate, @remark, 1,
    NULL, @sourceRecordId, @createdTime
)", connection, tx))
            {
                cmd.Parameters.AddWithValue("@entryNo", entryNo);
                cmd.Parameters.AddWithValue("@categoryId", categoryId);
                cmd.Parameters.AddWithValue("@categoryName", RefrigerationFeeReason);
                cmd.Parameters.AddWithValue("@amount", amount);
                cmd.Parameters.AddWithValue("@entryDate", deductDate.ToString("yyyy-MM-dd"));
                cmd.Parameters.AddWithValue("@remark", remark);
                cmd.Parameters.AddWithValue("@sourceRecordId", sourceRecordId);
                cmd.Parameters.AddWithValue("@createdTime", createdTime);
                cmd.ExecuteNonQuery();
            }
        }

        private static string BuildRefrigerationFeeLedgerRemark(string clientName, int quantity, decimal unitPrice)
        {
            string safeClientName = string.IsNullOrWhiteSpace(clientName) ? "客户" : clientName.Trim();
            if (quantity > 0 && unitPrice > 0)
                return $"{RefrigerationFeeReason}-{safeClientName}-{quantity}件×{unitPrice:0.##}";
            return $"{RefrigerationFeeReason}-{safeClientName}";
        }

        private static long GetOrEnsureRefrigerationFeeCategoryId(SQLiteConnection connection, SQLiteTransaction tx)
        {
            using (var findCmd = new SQLiteCommand(
                "SELECT id FROM ledger_category WHERE type = 'INCOME' AND name = @name LIMIT 1", connection, tx))
            {
                findCmd.Parameters.AddWithValue("@name", RefrigerationFeeReason);
                var found = findCmd.ExecuteScalar();
                if (found != null && found != DBNull.Value)
                    return Convert.ToInt64(found);
            }

            using (var insertCmd = new SQLiteCommand(@"
INSERT INTO ledger_category (type, name, is_system, enabled, sort_order)
VALUES ('INCOME', @name, 1, 1, 2)", connection, tx))
            {
                insertCmd.Parameters.AddWithValue("@name", RefrigerationFeeReason);
                insertCmd.ExecuteNonQuery();
            }

            return connection.LastInsertRowId;
        }

        private static string GenerateLedgerEntryNo(SQLiteConnection connection, SQLiteTransaction tx, DateTime entryDate)
        {
            string datePart = entryDate.ToString("yyyyMMdd");
            string prefix = "LS-" + datePart + "-";
            using (var cmd = new SQLiteCommand(
                "SELECT COUNT(*) FROM ledger_entry WHERE entry_no LIKE @prefix || '%'", connection, tx))
            {
                cmd.Parameters.AddWithValue("@prefix", prefix);
                int count = Convert.ToInt32(cmd.ExecuteScalar());
                return prefix + (count + 1).ToString("000");
            }
        }

        // 添加预支记录
        public bool AddAdvance(string clientCode, string clientName, decimal amount,
            DateTime advanceDate, string reason, string handler, string creator)
        {
            try
            {
                using (var connection = new SQLiteConnection(connectionString))
                {
                    connection.Open();

                    string query = @"INSERT INTO advances 
                    (client_code, client_name, amount, advance_date, reason, handler, creator) 
                    VALUES (@clientCode, @clientName, @amount, @advanceDate, @reason, @handler, @creator)";

                    using (var command = new SQLiteCommand(query, connection))
                    {
                        command.Parameters.AddWithValue("@clientCode", clientCode);
                        command.Parameters.AddWithValue("@clientName", clientName);
                        command.Parameters.AddWithValue("@amount", amount);
                        command.Parameters.AddWithValue("@advanceDate", advanceDate.ToString("yyyy-MM-dd"));
                        command.Parameters.AddWithValue("@reason", reason);
                        command.Parameters.AddWithValue("@handler", handler);
                        command.Parameters.AddWithValue("@creator", creator);

                        return command.ExecuteNonQuery() > 0;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"添加预支记录失败: {ex.Message}");
                return false;
            }
        }

        // 删除扣款记录
        public bool DeleteDeduction(int deductionId)
        {
            try
            {
                using (var connection = new SQLiteConnection(connectionString))
                {
                    connection.Open();

                    string query = "DELETE FROM deductions WHERE id = @id";

                    using (var command = new SQLiteCommand(query, connection))
                    {
                        command.Parameters.AddWithValue("@id", deductionId);
                        return command.ExecuteNonQuery() > 0;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"删除扣款记录失败: {ex.Message}");
                return false;
            }
        }

        // 删除预支记录
        public bool DeleteAdvance(int advanceId)
        {
            try
            {
                using (var connection = new SQLiteConnection(connectionString))
                {
                    connection.Open();

                    string query = "DELETE FROM advances WHERE id = @id";

                    using (var command = new SQLiteCommand(query, connection))
                    {
                        command.Parameters.AddWithValue("@id", advanceId);
                        return command.ExecuteNonQuery() > 0;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"删除预支记录失败: {ex.Message}");
                return false;
            }
        }

        // 新增：入库报表查询
        public DataTable GetInboundReport(DateTime startDate, DateTime endDate)
        {
            string sql = @"
            SELECT 
                i.bill_no as 单据号,
                c.name as 客户名称,
                l.name as 库位,
                h.name as 经手人,
                i.total_amount as 金额,
                i.bill_date as 入库日期,
                i.created_time as 创建时间
            FROM inbound_transactions i
            LEFT JOIN clients c ON i.client_code = c.code
            LEFT JOIN locations l ON i.location_code = l.code
            LEFT JOIN handlers h ON i.handler_code = h.code
            WHERE i.bill_date BETWEEN @start AND @end
            ORDER BY i.bill_date DESC";

            var parameters = new Dictionary<string, object>
        {
            { "@start", startDate.ToString("yyyy-MM-dd") },
            { "@end", endDate.ToString("yyyy-MM-dd") }
        };

            return ExecuteQuery(sql, parameters);
        }
        private List<string> GetClientCodesMatchingWarehouseKeyword(string keyword)
        {
            var codes = new List<string>();
            if (string.IsNullOrWhiteSpace(keyword))
                return codes;

            string sql = "SELECT code, name FROM clients WHERE status = 1";
            DataTable clients = ExecuteQuery(sql);
            foreach (DataRow row in clients.Rows)
            {
                string code = row["code"]?.ToString()?.Trim() ?? string.Empty;
                string name = row["name"]?.ToString()?.Trim() ?? string.Empty;
                if (ClientSearchHelper.MatchesClientFields(code, name, keyword))
                    codes.Add(code);
            }

            return codes.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        private static string BuildClientCodeInSqlFilter(List<string> codes, string columnName)
        {
            if (codes == null || codes.Count == 0)
                return " AND 1=0 ";

            var literals = codes
                .Where(c => !string.IsNullOrWhiteSpace(c))
                .Select(c => "'" + c.Replace("'", "''") + "'");
            return $" AND {columnName} IN ({string.Join(",", literals)}) ";
        }

        public DataTable GetWarehouseStatistics(string keyword = "", DateTime? startDate = null, DateTime? endDate = null)
        {
            return GetSimpleWarehouseStatistics(keyword, startDate, endDate);
        }
        private DataGridView gridLocations;
        /// <summary>
        /// 获取客户库位统计（按型号汇总）- 添加日期参数版本
        /// </summary>
        public DataTable GetSimpleWarehouseStatistics(string keyword = "", DateTime? startDate = null, DateTime? endDate = null)
        {
            DataTable result = new DataTable();
            List<string> productTypes = new List<string>();
            string keywordFilter = string.Empty;
            if (!string.IsNullOrWhiteSpace(keyword))
            {
                var codes = GetClientCodesMatchingWarehouseKeyword(keyword);
                keywordFilter = BuildClientCodeInSqlFilter(codes, "bd.client_code");
            }

            try
            {
                using (SQLiteConnection conn = new SQLiteConnection(connectionString))
                {
                    conn.Open();

                    string typeSql = "SELECT name FROM product_types WHERE is_active = 1 ORDER BY name";
                    using (SQLiteCommand typeCmd = new SQLiteCommand(typeSql, conn))
                    {
                        using (SQLiteDataReader reader = typeCmd.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                productTypes.Add(reader["name"].ToString());
                            }
                        }
                    }

                    if (productTypes.Count == 0)
                    {
                        productTypes.AddRange(new[] { "42型", "45型", "60型", "48型", "框", "精品", "次型" });
                    }

                    string dateFilter = "";
                    if (startDate.HasValue && endDate.HasValue)
                    {
                        dateFilter = " WHERE date BETWEEN @startDate AND @endDate ";
                    }

                    string specColumns = string.Join(",\n", productTypes.Select(t =>
                    {
                        string escaped = t.Replace("'", "''");
                        return $"    COALESCE(SUM(CASE WHEN bd.spec = '{escaped}' THEN bd.in_qty - bd.out_qty ELSE 0 END), 0) AS '{escaped}'";
                    }));

                    string query = $@"
WITH unified AS (
    SELECT
        it.client_code,
        it.client_name,
        COALESCE(it.location, '默认库位') AS location,
        ii.spec,
        ii.quantity,
        ii.total_amount,
        it.date,
        'in' AS txn_type
    FROM inbound_items ii
    INNER JOIN inbound_transactions it ON ii.order_no = it.order_no
    WHERE ii.quantity > 0

    UNION ALL

    SELECT
        it.client_code,
        it.client_name,
        COALESCE(it.location, '默认库位') AS location,
        it.spec,
        it.quantity,
        it.total_amount,
        it.date,
        'in' AS txn_type
    FROM inbound_transactions it
    WHERE it.quantity > 0
      AND NOT EXISTS (SELECT 1 FROM inbound_items ii WHERE ii.order_no = it.order_no)

    UNION ALL

    SELECT
        st.client_code,
        st.client_name,
        COALESCE(st.location, '默认库位') AS location,
        si.spec,
        si.quantity,
        si.total_amount,
        st.date,
        'out' AS txn_type
    FROM sales_items si
    INNER JOIN sales_transactions st ON si.order_no = st.order_no
    WHERE si.quantity > 0

    UNION ALL

    SELECT
        st.client_code,
        st.client_name,
        COALESCE(st.location, '默认库位') AS location,
        st.spec,
        st.quantity,
        st.total_amount,
        st.date,
        'out' AS txn_type
    FROM sales_transactions st
    WHERE st.quantity > 0
      AND NOT EXISTS (SELECT 1 FROM sales_items si WHERE si.order_no = st.order_no)
),
filtered AS (
    SELECT * FROM unified
    {(string.IsNullOrEmpty(dateFilter) ? "" : dateFilter)}
),
by_dim AS (
    SELECT
        client_code,
        client_name,
        location,
        spec,
        SUM(CASE WHEN txn_type = 'in' THEN quantity ELSE 0 END) AS in_qty,
        SUM(CASE WHEN txn_type = 'out' THEN quantity ELSE 0 END) AS out_qty,
        SUM(CASE WHEN txn_type = 'in' THEN total_amount ELSE 0 END) AS in_amount,
        SUM(CASE WHEN txn_type = 'out' THEN total_amount ELSE 0 END) AS out_amount,
        MAX(CASE WHEN txn_type = 'in' THEN date END) AS last_in_date
    FROM filtered
    GROUP BY client_code, client_name, location, spec
)
SELECT
    bd.client_code AS '客户编号',
    bd.client_name AS '客户名称',
    bd.location AS '库位名称',
{specColumns},
    COALESCE(SUM(bd.in_qty - bd.out_qty), 0) AS '总计数量',
    COALESCE(SUM(bd.in_amount - bd.out_amount), 0.0) AS '总计金额',
    MAX(bd.last_in_date) AS '最近入库'
FROM by_dim bd
WHERE 1=1 {keywordFilter}
GROUP BY bd.client_code, bd.client_name, bd.location
HAVING COALESCE(SUM(bd.in_qty - bd.out_qty), 0) != 0
ORDER BY bd.client_code, bd.location";

                    using (SQLiteCommand cmd = new SQLiteCommand(query, conn))
                    {
                        if (startDate.HasValue && endDate.HasValue)
                        {
                            cmd.Parameters.AddWithValue("@startDate", startDate.Value.ToString("yyyy-MM-dd"));
                            cmd.Parameters.AddWithValue("@endDate", endDate.Value.ToString("yyyy-MM-dd"));
                        }

                        using (SQLiteDataAdapter adapter = new SQLiteDataAdapter(cmd))
                        {
                            adapter.Fill(result);
                        }
                    }

                    Console.WriteLine($"查询返回 {result.Rows.Count} 行，{result.Columns.Count} 列");

                    foreach (string type in productTypes)
                    {
                        if (!result.Columns.Contains(type))
                        {
                            result.Columns.Add(type, typeof(long));
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"查询失败: {ex.Message}\n{ex.StackTrace}");
                result = CreateEmptyWarehouseTable();
            }

            return result;
        }

        /// <summary>
        /// 按客户+日期区间，汇总各库位各型号的入库总数、销售总数与库存。
        /// </summary>
        public DataTable GetClientInventoryByLocationAndSpec(
            string clientCode,
            string clientName,
            DateTime startDate,
            DateTime endDate)
        {
            var result = new DataTable();
            string storedCode = (clientCode ?? string.Empty).Trim();
            string storedName = (clientName ?? string.Empty).Trim();

            if (string.IsNullOrEmpty(storedCode) && string.IsNullOrEmpty(storedName))
                return result;

            try
            {
                using (var connection = new SQLiteConnection(connectionString))
                {
                    connection.Open();
                    string clientFilter;
                    if (!string.IsNullOrEmpty(storedCode))
                        clientFilter = "client_code = @clientCode";
                    else
                        clientFilter = "client_name = @clientName";

                    string query = $@"
WITH unified AS (
    SELECT
        it.client_code,
        it.client_name,
        COALESCE(it.location, '默认库位') AS location,
        ii.spec,
        ii.quantity,
        it.date,
        'in' AS txn_type
    FROM inbound_items ii
    INNER JOIN inbound_transactions it ON ii.order_no = it.order_no
    WHERE ii.quantity > 0

    UNION ALL

    SELECT
        it.client_code,
        it.client_name,
        COALESCE(it.location, '默认库位') AS location,
        it.spec,
        it.quantity,
        it.date,
        'in' AS txn_type
    FROM inbound_transactions it
    WHERE it.quantity > 0
      AND NOT EXISTS (SELECT 1 FROM inbound_items ii WHERE ii.order_no = it.order_no)

    UNION ALL

    SELECT
        st.client_code,
        st.client_name,
        COALESCE(st.location, '默认库位') AS location,
        si.spec,
        si.quantity,
        st.date,
        'out' AS txn_type
    FROM sales_items si
    INNER JOIN sales_transactions st ON si.order_no = st.order_no
    WHERE si.quantity > 0

    UNION ALL

    SELECT
        st.client_code,
        st.client_name,
        COALESCE(st.location, '默认库位') AS location,
        st.spec,
        st.quantity,
        st.date,
        'out' AS txn_type
    FROM sales_transactions st
    WHERE st.quantity > 0
      AND NOT EXISTS (SELECT 1 FROM sales_items si WHERE si.order_no = st.order_no)
),
filtered AS (
    SELECT * FROM unified
    WHERE date BETWEEN @startDate AND @endDate
      AND {clientFilter}
),
by_dim AS (
    SELECT
        client_code,
        client_name,
        location,
        spec,
        SUM(CASE WHEN txn_type = 'in' THEN quantity ELSE 0 END) AS in_qty,
        SUM(CASE WHEN txn_type = 'out' THEN quantity ELSE 0 END) AS out_qty
    FROM filtered
    GROUP BY client_code, client_name, location, spec
)
SELECT
    client_code AS '客户编号',
    client_name AS '客户名称',
    location AS '库位',
    spec AS '商品型号',
    in_qty AS '入库总数',
    out_qty AS '销售总数',
    in_qty - out_qty AS '库存'
FROM by_dim
WHERE in_qty != 0 OR out_qty != 0 OR (in_qty - out_qty) != 0
ORDER BY location, spec";

                    using (var command = new SQLiteCommand(query, connection))
                    {
                        command.Parameters.AddWithValue("@startDate", startDate.ToString("yyyy-MM-dd"));
                        command.Parameters.AddWithValue("@endDate", endDate.ToString("yyyy-MM-dd"));
                        if (!string.IsNullOrEmpty(storedCode))
                            command.Parameters.AddWithValue("@clientCode", storedCode);
                        else
                            command.Parameters.AddWithValue("@clientName", storedName);

                        using (var adapter = new SQLiteDataAdapter(command))
                        {
                            adapter.Fill(result);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"按客户查询库位库存失败 [{storedCode}/{storedName}]: {ex.Message}");
            }

            return result;
        }

        /// <summary>
        /// 基础查询 - 确保返回的DataTable包含型号列
        /// </summary>
        public DataTable GetBasicWarehouseStatistics(string keyword = "")
        {
            DataTable result = new DataTable();
            string keywordFilter = string.Empty;
            if (!string.IsNullOrWhiteSpace(keyword))
            {
                var codes = GetClientCodesMatchingWarehouseKeyword(keyword);
                keywordFilter = BuildClientCodeInSqlFilter(codes, "c.code");
            }

            try
            {
                using (SQLiteConnection conn = new SQLiteConnection(connectionString))
                {
                    conn.Open();

                    string query = @"
                SELECT 
                    c.code AS '客户编号',
                    c.name AS '客户名称',
                    COALESCE(it.location, '默认库位') AS '库位名称',
                    COALESCE(SUM(it.quantity), 0) AS '总计数量',
                    COALESCE(SUM(it.total_amount), 0.00) AS '总计金额',
                    MAX(it.created_time) AS '最近入库'
                FROM clients c
                LEFT JOIN inbound_transactions it ON c.code = it.client_code
                WHERE c.status = 1
                    {keywordFilter}
                GROUP BY c.code, c.name, it.location
                HAVING SUM(it.quantity) > 0
                ORDER BY c.code, it.location";

                    query = query.Replace("{keywordFilter}", keywordFilter);

                    using (SQLiteCommand cmd = new SQLiteCommand(query, conn))
                    {
                        using (SQLiteDataAdapter adapter = new SQLiteDataAdapter(cmd))
                        {
                            adapter.Fill(result);
                        }
                    }

                    Console.WriteLine($"基础查询返回 {result.Rows.Count} 行");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"基础查询失败: {ex.Message}");
                result = CreateEmptyWarehouseTable();
            }

            return result;
        }

        
        /// <summary>
        /// 创建客户库位统计的空表结构
        /// </summary>
        private DataTable CreateClientWarehouseDataTableStructure()
        {
            DataTable table = new DataTable();

            try
            {
                // 添加基础列
                table.Columns.Add("客户编号", typeof(string));
                table.Columns.Add("客户名称", typeof(string));
                table.Columns.Add("库位名称", typeof(string));

                // 从数据库获取型号
                List<string> productTypes = new List<string>();
                try
                {
                    using (SQLiteConnection conn = new SQLiteConnection(connectionString))
                    {
                        conn.Open();
                        string sql = "SELECT name FROM product_types WHERE is_active = 1 ORDER BY name";
                        using (SQLiteCommand cmd = new SQLiteCommand(sql, conn))
                        {
                            using (SQLiteDataReader reader = cmd.ExecuteReader())
                            {
                                while (reader.Read())
                                {
                                    productTypes.Add(reader["name"].ToString());
                                }
                            }
                        }
                    }
                }
                catch
                {
                    // 如果获取失败，使用默认型号
                    productTypes.AddRange(new[] { "42型", "45型", "60型", "48型", "框", "精品", "次型" });
                }

                // 添加型号列
                foreach (string type in productTypes)
                {
                    table.Columns.Add(type, typeof(int));
                }

                // 添加统计列
                table.Columns.Add("总计数量", typeof(int));
                table.Columns.Add("总计金额", typeof(decimal));
                table.Columns.Add("最近入库", typeof(string));

                // 添加一行提示数据
                DataRow row = table.NewRow();
                row["客户名称"] = "暂无客户库位数据";
                table.Rows.Add(row);

                Console.WriteLine($"创建空表结构完成，共 {table.Columns.Count} 列");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"创建空表结构失败: {ex.Message}");

                // 最简单的表结构
                table = new DataTable();
                table.Columns.Add("提示", typeof(string));
                DataRow row = table.NewRow();
                row["提示"] = "创建表结构失败";
                table.Rows.Add(row);
            }

            return table;
        }

        private async Task<List<string>> GetProductTypesAsync()
        {
            List<string> types = new List<string>();

            await Task.Run(() =>
            {
                try
                {
                    using (SQLiteConnection conn = new SQLiteConnection(connectionString))
                    {
                        conn.Open();
                        string sql = "SELECT name FROM product_types WHERE is_active = 1 ORDER BY name";
                        using (SQLiteCommand cmd = new SQLiteCommand(sql, conn))
                        {
                            using (SQLiteDataReader reader = cmd.ExecuteReader())
                            {
                                while (reader.Read())
                                {
                                    types.Add(reader["name"].ToString());
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"获取型号失败: {ex.Message}");
                }
            });

            return types;
        }
        private DataTable CreateEmptyWarehouseTable()
        {
            DataTable table = new DataTable();

            // 获取当前型号
            List<string> productTypes = GetProductTypesAsync().Result;

            table.Columns.Add("客户编号", typeof(string));
            table.Columns.Add("客户名称", typeof(string));
            table.Columns.Add("库位名称", typeof(string));

            foreach (string type in productTypes)
            {
                table.Columns.Add(type, typeof(int));
            }

            table.Columns.Add("总计数量", typeof(int));
            table.Columns.Add("总计金额", typeof(decimal));
            table.Columns.Add("最近入库", typeof(string));

            // 添加提示行
            DataRow row = table.NewRow();
            row["客户名称"] = "暂无客户库位数据";
            table.Rows.Add(row);

            return table;
        }
        // 新增：销售报表查询
        public DataTable GetSalesReport(DateTime startDate, DateTime endDate)
        {
            string sql = @"
            SELECT 
                s.bill_no as 单据号,
                c.name as 客户名称,
                l.name as 库位,
                h.name as 经手人,
                s.total_amount as 金额,
                s.sale_date as 销售日期,
                s.created_time as 创建时间
            FROM sales_transactions s
            LEFT JOIN clients c ON s.client_code = c.code
            LEFT JOIN locations l ON s.location_code = l.code
            LEFT JOIN handlers h ON s.handler_code = h.code
            WHERE s.sale_date BETWEEN @start AND @end
            ORDER BY s.sale_date DESC";

            var parameters = new Dictionary<string, object>
        {
            { "@start", startDate.ToString("yyyy-MM-dd") },
            { "@end", endDate.ToString("yyyy-MM-dd") }
        };

            return ExecuteQuery(sql, parameters);
        }


        // 新增：统计报表数据
        public DataTable GetStatisticsReport(string reportType, DateTime startDate, DateTime endDate)
        {
            string sql = "";

            switch (reportType)
            {
                case "handler":
                    sql = @"
                    SELECT 
                        h.name as 经手人,
                        COUNT(DISTINCT i.bill_no) as 入库单数,
                        COUNT(DISTINCT s.bill_no) as 销售单数,
                        SUM(COALESCE(i.total_amount, 0)) as 入库金额,
                        SUM(COALESCE(s.total_amount, 0)) as 销售金额
                    FROM handlers h
                    LEFT JOIN inbound_transactions i ON h.code = i.handler_code 
                        AND i.bill_date BETWEEN @start AND @end
                    LEFT JOIN sales_transactions s ON h.code = s.handler_code 
                        AND s.sale_date BETWEEN @start AND @end
                    GROUP BY h.name
                    ORDER BY 销售金额 DESC";
                    break;

                case "product":
                    sql = @"
                    SELECT 
                        p.name as 商品名称,
                        SUM(CASE WHEN i.bill_no IS NOT NULL THEN id.quantity ELSE 0 END) as 入库数量,
                        SUM(CASE WHEN s.bill_no IS NOT NULL THEN sd.quantity ELSE 0 END) as 销售数量,
                        SUM(CASE WHEN i.bill_no IS NOT NULL THEN id.quantity * id.unit_price ELSE 0 END) as 入库金额,
                        SUM(CASE WHEN s.bill_no IS NOT NULL THEN sd.quantity * sd.unit_price ELSE 0 END) as 销售金额
                    FROM product_types p
                    LEFT JOIN inbound_details id ON p.code = id.product_type
                    LEFT JOIN inbound_transactions i ON id.bill_no = i.bill_no 
                        AND i.bill_date BETWEEN @start AND @end
                    LEFT JOIN sales_details sd ON p.code = sd.product_type
                    LEFT JOIN sales_transactions s ON sd.bill_no = s.bill_no 
                        AND s.sale_date BETWEEN @start AND @end
                    GROUP BY p.name
                    ORDER BY 销售金额 DESC";
                    break;
            }

            var parameters = new Dictionary<string, object>
        {
            { "@start", startDate.ToString("yyyy-MM-dd") },
            { "@end", endDate.ToString("yyyy-MM-dd") }
        };

            return ExecuteQuery(sql, parameters);
        }

        // 获取所有客户的汇总对账数据
        public DataTable GetAllClientsBalanceSummary(DateTime startDate, DateTime endDate)
        {
            DataTable dt = new DataTable();
            try
            {
                using (var connection = new SQLiteConnection(connectionString))
                {
                    connection.Open();

                    string query = @"
SELECT 
    c.code as ClientCode,
    c.name as ClientName,
    COALESCE(SUM(s.total_amount), 0) as SalesTotal,
    COALESCE(SUM(p.total_amount), 0) as PackagingTotal,
    COALESCE(SUM(d.amount), 0) as DeductionTotal,
    COALESCE(SUM(a.amount), 0) as AdvanceTotal,
    (COALESCE(SUM(s.total_amount), 0) + COALESCE(SUM(p.total_amount), 0) - 
     COALESCE(SUM(d.amount), 0) - COALESCE(SUM(a.amount), 0)) as PayableTotal
    
FROM clients c
    
LEFT JOIN sales_transactions s ON c.code = s.client_code 
    AND s.date BETWEEN @startDate AND @endDate
    
LEFT JOIN packaging_transactions p ON c.code = p.client_code 
    AND DATE(p.created_time) BETWEEN @startDate AND @endDate
    
LEFT JOIN deductions d ON c.code = d.client_code 
    AND d.deduct_date BETWEEN @startDate AND @endDate
    
LEFT JOIN advances a ON c.code = a.client_code 
    AND a.advance_date BETWEEN @startDate AND @endDate
    
WHERE c.status = 1
GROUP BY c.code, c.name
ORDER BY c.name";

                    using (var command = new SQLiteCommand(query, connection))
                    {
                        command.Parameters.AddWithValue("@startDate", startDate.ToString("yyyy-MM-dd"));
                        command.Parameters.AddWithValue("@endDate", endDate.ToString("yyyy-MM-dd"));

                        using (var adapter = new SQLiteDataAdapter(command))
                        {
                            adapter.Fill(dt);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"获取所有客户对账汇总失败: {ex.Message}");
            }
            return dt;
        }

        #endregion

        // 在 DatabaseManager.cs 中添加以下方法

        /// <summary>
        /// 获取所有预支款记录（简化版，用于同步）
        /// </summary>
        public DataTable GetAdvanceRecordsSimple()
        {
            string sql = @"
        SELECT client_code, client_name, amount, advance_date, reason, handler, creator, status
        FROM advances 
        WHERE status = 1 
        ORDER BY created_time DESC";

            return ExecuteQuery(sql);
        }

        /// <summary>
        /// 获取所有扣款记录（简化版，用于同步）
        /// </summary>
        public DataTable GetDeductionRecordsSimple()
        {
            string sql = @"
        SELECT client_code, client_name, amount, deduct_date, reason, handler, creator, status
        FROM deductions 
        WHERE status = 1 
        ORDER BY created_time DESC";

            return ExecuteQuery(sql);
        }

        /// <summary>
        /// 保存预支款记录
        /// </summary>
        public bool SaveAdvanceRecord(string clientCode, string clientName, decimal amount,
            string advanceDate, string reason, string handler, string creator, long status,
            string sourceDeviceId = "", string sourceRecordId = "", string createdTime = null)
        {
            return SaveAdvanceRecord(clientCode, clientName, amount, advanceDate, reason, handler, creator, status,
                sourceDeviceId, sourceRecordId, createdTime, out _);
        }

        public bool SaveAdvanceRecord(string clientCode, string clientName, decimal amount,
            string advanceDate, string reason, string handler, string creator, long status,
            string sourceDeviceId, string sourceRecordId, out string errorMessage)
        {
            return SaveAdvanceRecord(clientCode, clientName, amount, advanceDate, reason, handler, creator, status,
                sourceDeviceId, sourceRecordId, null, out errorMessage);
        }

        public bool SaveAdvanceRecord(string clientCode, string clientName, decimal amount,
            string advanceDate, string reason, string handler, string creator, long status,
            string sourceDeviceId, string sourceRecordId, string createdTime, out string errorMessage)
        {
            errorMessage = "";
            try
            {
                using (var conn = new SQLiteConnection(connectionString))
                {
                    conn.Open();
                    EnsureSourceColumns(conn, "advances", true);
                }

                string formattedAdvanceDate = advanceDate;
                if (!string.IsNullOrWhiteSpace(advanceDate) && advanceDate.Length >= 10)
                    formattedAdvanceDate = advanceDate.Substring(0, 10);

                string trimmedSourceRecordId = string.IsNullOrWhiteSpace(sourceRecordId) ? "" : sourceRecordId.Trim();
                string trimmedSourceDeviceId = string.IsNullOrWhiteSpace(sourceDeviceId) ? "" : sourceDeviceId.Trim();
                string formattedCreatedTime = NormalizeCreatedTimeToSecond(createdTime, creator);

                using (var conn = new SQLiteConnection(connectionString))
                {
                    conn.Open();

                    if (!string.IsNullOrEmpty(trimmedSourceRecordId) && ExistsAdvanceBySourceRecordId(conn, trimmedSourceRecordId))
                        return true;

                    if (ExistsAdvanceByBusinessKey(conn, clientCode, amount, formattedAdvanceDate, formattedCreatedTime, trimmedSourceDeviceId))
                    {
                        errorMessage = $"同一设备已存在相同预支记录（客户={clientName}，金额={amount}，日期={formattedAdvanceDate}，时间={formattedCreatedTime}），请勿重复提交";
                        return false;
                    }

                    string sql = @"
INSERT OR IGNORE INTO advances 
(client_code, client_name, amount, advance_date, reason, handler, creator, status, created_time, source_device_id, source_record_id)
VALUES 
(@clientCode, @clientName, @amount, @advanceDate, @reason, @handler, @creator, @status, @createdTime, @sourceDeviceId, @sourceRecordId)";

                    using (var command = new SQLiteCommand(sql, conn))
                    {
                        command.Parameters.AddWithValue("@clientCode", clientCode);
                        command.Parameters.AddWithValue("@clientName", clientName);
                        command.Parameters.AddWithValue("@amount", amount);
                        command.Parameters.AddWithValue("@advanceDate", formattedAdvanceDate);
                        command.Parameters.AddWithValue("@reason", reason ?? "");
                        command.Parameters.AddWithValue("@handler", handler ?? "");
                        command.Parameters.AddWithValue("@creator", creator ?? "");
                        command.Parameters.AddWithValue("@status", status);
                        command.Parameters.AddWithValue("@createdTime", formattedCreatedTime);
                        command.Parameters.AddWithValue("@sourceDeviceId", string.IsNullOrWhiteSpace(trimmedSourceDeviceId) ? (object)DBNull.Value : trimmedSourceDeviceId);
                        command.Parameters.AddWithValue("@sourceRecordId", string.IsNullOrWhiteSpace(trimmedSourceRecordId) ? (object)DBNull.Value : trimmedSourceRecordId);

                        if (command.ExecuteNonQuery() > 0)
                        {
                            Console.WriteLine($"✅ 预支款保存成功：{clientName}，金额={amount}，时间={formattedCreatedTime}");
                            return true;
                        }
                    }

                    if (!string.IsNullOrEmpty(trimmedSourceRecordId) && ExistsAdvanceBySourceRecordId(conn, trimmedSourceRecordId))
                        return true;

                    if (ExistsAdvanceByBusinessKey(conn, clientCode, amount, formattedAdvanceDate, formattedCreatedTime, trimmedSourceDeviceId))
                    {
                        errorMessage = $"同一设备已存在相同预支记录（客户={clientName}，金额={amount}，日期={formattedAdvanceDate}，时间={formattedCreatedTime}），请勿重复提交";
                        return false;
                    }

                    errorMessage = "预支款保存失败，请检查数据完整性";
                    return false;
                }
            }
            catch (Exception ex)
            {
                errorMessage = $"保存预支款失败：{ex.Message}";
                Console.WriteLine($"❌ {errorMessage}");
                return false;
            }
        }

        /// <summary>
        /// 保存扣款记录
        /// </summary>
        public bool SaveDeductionRecord(string clientCode, string clientName, decimal amount,
            string deductDate, string reason, string handler, string creator, long status,
            string sourceDeviceId = "", string sourceRecordId = "",
            int quantity = 0, decimal unitPrice = 0, string createdTime = null)
        {
            return SaveDeductionRecord(clientCode, clientName, amount, deductDate, reason, handler, creator, status,
                sourceDeviceId, sourceRecordId, quantity, unitPrice, createdTime, out _);
        }

        public bool SaveDeductionRecord(string clientCode, string clientName, decimal amount,
            string deductDate, string reason, string handler, string creator, long status,
            string sourceDeviceId, string sourceRecordId,
            int quantity, decimal unitPrice, out string errorMessage)
        {
            return SaveDeductionRecord(clientCode, clientName, amount, deductDate, reason, handler, creator, status,
                sourceDeviceId, sourceRecordId, quantity, unitPrice, null, out errorMessage);
        }

        public bool SaveDeductionRecord(string clientCode, string clientName, decimal amount,
            string deductDate, string reason, string handler, string creator, long status,
            string sourceDeviceId, string sourceRecordId,
            int quantity, decimal unitPrice, string createdTime, out string errorMessage)
        {
            errorMessage = "";
            try
            {
                using (var conn = new SQLiteConnection(connectionString))
                {
                    conn.Open();
                    EnsureSourceColumns(conn, "deductions", true);
                    EnsureDeductionQuantityColumns(conn);
                }

                string formattedDeductDate = deductDate;
                if (!string.IsNullOrWhiteSpace(deductDate) && deductDate.Length >= 10)
                    formattedDeductDate = deductDate.Substring(0, 10);

                string trimmedSourceRecordId = string.IsNullOrWhiteSpace(sourceRecordId) ? "" : sourceRecordId.Trim();
                string trimmedSourceDeviceId = string.IsNullOrWhiteSpace(sourceDeviceId) ? "" : sourceDeviceId.Trim();
                string formattedCreatedTime = NormalizeCreatedTimeToSecond(createdTime, creator);

                using (var conn = new SQLiteConnection(connectionString))
                {
                    conn.Open();

                    if (!string.IsNullOrEmpty(trimmedSourceRecordId) && ExistsDeductionBySourceRecordId(conn, trimmedSourceRecordId))
                        return true;

                    if (ExistsDeductionByBusinessKey(conn, clientCode, amount, formattedDeductDate, formattedCreatedTime, trimmedSourceDeviceId))
                    {
                        errorMessage = $"同一设备已存在相同扣款记录（客户={clientName}，金额={amount}，日期={formattedDeductDate}，时间={formattedCreatedTime}），请勿重复提交";
                        return false;
                    }

                    string sql = @"
INSERT OR IGNORE INTO deductions 
(client_code, client_name, amount, quantity, unit_price, deduct_date, reason, handler, creator, status, created_time, source_device_id, source_record_id)
VALUES 
(@clientCode, @clientName, @amount, @quantity, @unitPrice, @deductDate, @reason, @handler, @creator, @status, @createdTime, @sourceDeviceId, @sourceRecordId)";

                    using (var command = new SQLiteCommand(sql, conn))
                    {
                        command.Parameters.AddWithValue("@clientCode", clientCode);
                        command.Parameters.AddWithValue("@clientName", clientName);
                        command.Parameters.AddWithValue("@amount", amount);
                        command.Parameters.AddWithValue("@quantity", quantity);
                        command.Parameters.AddWithValue("@unitPrice", unitPrice);
                        command.Parameters.AddWithValue("@deductDate", formattedDeductDate);
                        command.Parameters.AddWithValue("@reason", reason ?? "");
                        command.Parameters.AddWithValue("@handler", handler ?? "");
                        command.Parameters.AddWithValue("@creator", creator ?? "");
                        command.Parameters.AddWithValue("@status", status);
                        command.Parameters.AddWithValue("@createdTime", formattedCreatedTime);
                        command.Parameters.AddWithValue("@sourceDeviceId", string.IsNullOrWhiteSpace(trimmedSourceDeviceId) ? (object)DBNull.Value : trimmedSourceDeviceId);
                        command.Parameters.AddWithValue("@sourceRecordId", string.IsNullOrWhiteSpace(trimmedSourceRecordId) ? (object)DBNull.Value : trimmedSourceRecordId);

                        if (command.ExecuteNonQuery() > 0)
                        {
                            Console.WriteLine($"✅ 扣款保存成功：{clientName}，数量={quantity}，单价={unitPrice}，金额={amount}，时间={formattedCreatedTime}");
                            return true;
                        }
                    }

                    if (!string.IsNullOrEmpty(trimmedSourceRecordId) && ExistsDeductionBySourceRecordId(conn, trimmedSourceRecordId))
                        return true;

                    if (ExistsDeductionByBusinessKey(conn, clientCode, amount, formattedDeductDate, formattedCreatedTime, trimmedSourceDeviceId))
                    {
                        errorMessage = $"同一设备已存在相同扣款记录（客户={clientName}，金额={amount}，日期={formattedDeductDate}，时间={formattedCreatedTime}），请勿重复提交";
                        return false;
                    }

                    errorMessage = "扣款保存失败，请检查数据完整性";
                    return false;
                }
            }
            catch (Exception ex)
            {
                errorMessage = $"保存扣款失败：{ex.Message}";
                Console.WriteLine($"❌ {errorMessage}");
                return false;
            }
        }

        /// <summary>PC 本地修改预售单时，供 AutoSyncServer 写入增量变更日志。</summary>
        public static event EventHandler<LocalConfigDeltaEventArgs> LocalPresaleChangedForSync;

        /// <summary>手持端同步写入 PC 预售单后通知查询界面刷新。</summary>
        public static event Action<string> PresaleBillRemoteUpdated;

        public JObject BuildPresaleBillSyncPayload(string billNo)
        {
            if (string.IsNullOrWhiteSpace(billNo)) return null;
            using (var conn = new SQLiteConnection(connectionString))
            {
                conn.Open();
                DataRow billRow = null;
                using (var cmd = new SQLiteCommand(
                    "SELECT * FROM presale_bills WHERE bill_no = @billNo LIMIT 1", conn))
                {
                    cmd.Parameters.AddWithValue("@billNo", billNo);
                    using (var adapter = new SQLiteDataAdapter(cmd))
                    {
                        var table = new DataTable();
                        adapter.Fill(table);
                        if (table.Rows.Count == 0) return null;
                        billRow = table.Rows[0];
                    }
                }

                var items = new JArray();
                using (var itemCmd = new SQLiteCommand(
                    "SELECT spec, quantity, COALESCE(shipped_quantity, 0) AS shipped_quantity, unit_price, total_amount FROM presale_items WHERE bill_no = @billNo ORDER BY id", conn))
                {
                    itemCmd.Parameters.AddWithValue("@billNo", billNo);
                    using (var reader = itemCmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            items.Add(new JObject
                            {
                                ["spec"] = reader["spec"]?.ToString() ?? "",
                                ["quantity"] = reader["quantity"]?.ToString() ?? "0",
                                ["shipped_quantity"] = reader["shipped_quantity"]?.ToString() ?? "0",
                                ["unit_price"] = reader["unit_price"]?.ToString() ?? "0",
                                ["total_amount"] = reader["total_amount"]?.ToString() ?? "0",
                            });
                        }
                    }
                }

                string sourceRecordId = billRow["source_record_id"]?.ToString() ?? "";
                if (string.IsNullOrWhiteSpace(sourceRecordId))
                    sourceRecordId = $"PC_PRESALE_{billNo}";

                string saleMode = billRow["sale_mode"]?.ToString() ?? "PRESALE";
                string billStatus = billRow["status"]?.ToString() ?? "PRESALE";
                if (string.Equals(saleMode, "DIRECT_OUT", StringComparison.OrdinalIgnoreCase)
                    && string.Equals(billStatus, "PRESALE", StringComparison.OrdinalIgnoreCase))
                {
                    billStatus = "COMPLETED";
                }

                return new JObject
                {
                    ["bill_no"] = billNo,
                    ["buyer_code"] = billRow["buyer_code"]?.ToString() ?? "",
                    ["buyer_name"] = billRow["buyer_name"]?.ToString() ?? "",
                    ["location"] = billRow["location"]?.ToString() ?? "",
                    ["sale_mode"] = saleMode,
                    ["bill_status"] = billStatus,
                    ["total_amount"] = billRow["total_amount"]?.ToString() ?? "0",
                    ["paid_amount"] = billRow["paid_amount"]?.ToString() ?? "0",
                    ["handler"] = billRow["handler"]?.ToString() ?? "",
                    ["remark"] = billRow["remark"]?.ToString() ?? "",
                    ["date"] = billRow["date"]?.ToString() ?? "",
                    ["items"] = items,
                    ["source_device_id"] = billRow["source_device_id"]?.ToString() ?? "PC_LOCAL",
                    ["source_record_id"] = sourceRecordId,
                    ["fiscal_year"] = FiscalYearService.IsInitialized ? FiscalYearService.ActiveYear : DateTime.Now.Year,
                };
            }
        }

        public JObject BuildPresalePaymentSyncPayload(string billNo, decimal amount, string payMethod, string payTime, string remark, string sourceRecordId = null)
        {
            if (string.IsNullOrWhiteSpace(billNo)) return null;
            string buyerCode = "";
            using (var conn = new SQLiteConnection(connectionString))
            {
                conn.Open();
                using (var cmd = new SQLiteCommand("SELECT buyer_code FROM presale_bills WHERE bill_no = @billNo LIMIT 1", conn))
                {
                    cmd.Parameters.AddWithValue("@billNo", billNo);
                    var result = cmd.ExecuteScalar();
                    buyerCode = result?.ToString() ?? "";
                }
            }
            if (string.IsNullOrWhiteSpace(sourceRecordId))
                sourceRecordId = $"PC_PRESALE_PAYMENT_{billNo}_{Guid.NewGuid():N}";
            return new JObject
            {
                ["bill_no"] = billNo,
                ["buyer_code"] = buyerCode,
                ["amount"] = amount.ToString(),
                ["pay_method"] = payMethod ?? "",
                ["pay_time"] = payTime ?? DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                ["remark"] = remark ?? "",
                ["source_device_id"] = "PC_LOCAL",
                ["source_record_id"] = sourceRecordId,
                ["fiscal_year"] = FiscalYearService.IsInitialized ? FiscalYearService.ActiveYear : DateTime.Now.Year,
            };
        }

        public List<JObject> GetPresaleBillsForFullSync(int recentDays = 90)
        {
            var result = new List<JObject>();
            string cutoff = DateTime.Now.AddDays(-recentDays).ToString("yyyy-MM-dd");
            using (var conn = new SQLiteConnection(connectionString))
            {
                conn.Open();
                var billNos = new List<string>();
                using (var cmd = new SQLiteCommand(@"
SELECT bill_no FROM presale_bills
WHERE date >= @cutoff OR status IN ('PRESALE', 'SHIPPED')
ORDER BY date DESC", conn))
                {
                    cmd.Parameters.AddWithValue("@cutoff", cutoff);
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            var no = reader["bill_no"]?.ToString();
                            if (!string.IsNullOrWhiteSpace(no)) billNos.Add(no);
                        }
                    }
                }
                foreach (var billNo in billNos.Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    var payload = BuildPresaleBillSyncPayload(billNo);
                    if (payload != null) result.Add(payload);
                }
            }
            return result;
        }

        public List<JObject> GetPresalePaymentsForFullSync(int recentDays = 90)
        {
            var result = new List<JObject>();
            string cutoff = DateTime.Now.AddDays(-recentDays).ToString("yyyy-MM-dd");
            using (var conn = new SQLiteConnection(connectionString))
            {
                conn.Open();
                using (var cmd = new SQLiteCommand(@"
SELECT bill_no, buyer_code, amount, pay_method, pay_time, remark, source_device_id, source_record_id
FROM presale_payments
WHERE pay_time >= @cutoff OR created_time >= @cutoff
ORDER BY pay_time DESC", conn))
                {
                    cmd.Parameters.AddWithValue("@cutoff", cutoff);
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            string sourceRecordId = reader["source_record_id"]?.ToString() ?? "";
                            if (string.IsNullOrWhiteSpace(sourceRecordId)) continue;
                            result.Add(new JObject
                            {
                                ["bill_no"] = reader["bill_no"]?.ToString() ?? "",
                                ["buyer_code"] = reader["buyer_code"]?.ToString() ?? "",
                                ["amount"] = reader["amount"]?.ToString() ?? "0",
                                ["pay_method"] = reader["pay_method"]?.ToString() ?? "",
                                ["pay_time"] = reader["pay_time"]?.ToString() ?? "",
                                ["remark"] = reader["remark"]?.ToString() ?? "",
                                ["source_device_id"] = reader["source_device_id"]?.ToString() ?? "",
                                ["source_record_id"] = sourceRecordId,
                                ["fiscal_year"] = FiscalYearService.IsInitialized ? FiscalYearService.ActiveYear : DateTime.Now.Year,
                            });
                        }
                    }
                }
            }
            return result;
        }

        public void NotifyPresaleBillChanged(string billNo)
        {
            if (SuppressLocalConfigSyncNotifications) return;
            try
            {
                var payload = BuildPresaleBillSyncPayload(billNo);
                if (payload == null) return;
                string entityKey = payload["source_record_id"]?.ToString() ?? billNo;
                LocalPresaleChangedForSync?.Invoke(null, new LocalConfigDeltaEventArgs
                {
                    EntityType = "PRESALE",
                    EntityKey = entityKey,
                    OpType = "UPSERT",
                    Payload = payload,
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️ 预售单增量通知失败: {ex.Message}");
            }
        }

        private static string ResolvePresaleSourceRecordId(string existingSourceRecordId, string incomingSourceRecordId)
        {
            if (string.IsNullOrWhiteSpace(incomingSourceRecordId))
                return existingSourceRecordId ?? "";

            if (string.IsNullOrWhiteSpace(existingSourceRecordId))
                return incomingSourceRecordId;

            if (existingSourceRecordId.StartsWith("PC_PRESALE_", StringComparison.OrdinalIgnoreCase))
                return incomingSourceRecordId;

            return existingSourceRecordId;
        }

        public bool SavePresaleBillFromSync(string jsonData)
        {
            return SavePresaleBillFromSync(jsonData, out _);
        }

        public bool SavePresaleBillFromSync(string jsonData, out string errorMessage)
        {
            errorMessage = "";
            try
            {
                var json = Newtonsoft.Json.Linq.JObject.Parse(jsonData);
                string billNo = json["bill_no"]?.ToString() ?? "";
                if (string.IsNullOrWhiteSpace(billNo))
                {
                    errorMessage = "预售单同步失败：bill_no 为空";
                    Console.WriteLine($"❌ {errorMessage}");
                    return false;
                }

                string buyerCode = json["buyer_code"]?.ToString() ?? "";
                string buyerName = json["buyer_name"]?.ToString() ?? "";
                string location = json["location"]?.ToString() ?? "";
                string saleMode = json["sale_mode"]?.ToString() ?? "PRESALE";
                string status = json["bill_status"]?.ToString() ?? json["status"]?.ToString() ?? "PRESALE";
                if (string.Equals(saleMode, PresaleHelper.SaleModeDirectOut, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(status, "PRESALE", StringComparison.OrdinalIgnoreCase))
                {
                    status = "COMPLETED";
                }
                decimal totalAmount = 0;
                decimal.TryParse(json["total_amount"]?.ToString() ?? "0", out totalAmount);
                decimal paidAmount = 0;
                decimal.TryParse(json["paid_amount"]?.ToString() ?? "0", out paidAmount);
                string handler = json["handler"]?.ToString() ?? "";
                string remark = json["remark"]?.ToString() ?? "";
                string date = json["date"]?.ToString() ?? DateTime.Now.ToString("yyyy-MM-dd");
                string sourceDeviceId = json["source_device_id"]?.ToString() ?? "";
                string sourceRecordId = json["source_record_id"]?.ToString() ?? "";
                string trimmedSourceDeviceId = string.IsNullOrWhiteSpace(sourceDeviceId) ? "" : sourceDeviceId.Trim();
                string trimmedSourceRecordId = string.IsNullOrWhiteSpace(sourceRecordId) ? "" : sourceRecordId.Trim();
                string createdTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                string resolvedSourceRecordId = trimmedSourceRecordId;

                using (var conn = new SQLiteConnection(connectionString))
                {
                    conn.Open();
                    EnsurePresaleItemShippedQuantityColumn(conn);
                    long billId = 0;
                    string existingSaleMode = null;
                    string existingSourceRecordId = null;

                    if (!string.IsNullOrWhiteSpace(trimmedSourceRecordId))
                    {
                        using (var findCmd = new SQLiteCommand(
                            "SELECT id, sale_mode, source_record_id FROM presale_bills WHERE source_record_id = @sourceRecordId LIMIT 1", conn))
                        {
                            findCmd.Parameters.AddWithValue("@sourceRecordId", trimmedSourceRecordId);
                            using (var reader = findCmd.ExecuteReader())
                            {
                                if (reader.Read())
                                {
                                    billId = reader.GetInt64(0);
                                    existingSaleMode = reader["sale_mode"]?.ToString();
                                    existingSourceRecordId = reader["source_record_id"]?.ToString();
                                }
                            }
                        }
                    }

                    if (!string.IsNullOrWhiteSpace(existingSaleMode)
                        && string.Equals(existingSaleMode, PresaleHelper.SaleModeDirectOut, StringComparison.OrdinalIgnoreCase)
                        && string.Equals(saleMode, PresaleHelper.SaleModePresale, StringComparison.OrdinalIgnoreCase))
                    {
                        saleMode = PresaleHelper.SaleModeDirectOut;
                    }

                    string storedSourceRecordId = ResolvePresaleSourceRecordId(existingSourceRecordId, trimmedSourceRecordId);
                    resolvedSourceRecordId = storedSourceRecordId ?? trimmedSourceRecordId;

                    if (billId > 0)
                    {
                        string updateSql = @"
UPDATE presale_bills SET
    bill_no = @billNo, buyer_code = @buyerCode, buyer_name = @buyerName, location = @location,
    sale_mode = @saleMode, status = @status, total_amount = @totalAmount, paid_amount = @paidAmount,
    handler = @handler, remark = @remark, date = @date, source_device_id = @sourceDeviceId,
    source_record_id = @sourceRecordId
WHERE id = @id";
                        using (var cmd = new SQLiteCommand(updateSql, conn))
                        {
                            cmd.Parameters.AddWithValue("@billNo", billNo);
                            cmd.Parameters.AddWithValue("@buyerCode", buyerCode);
                            cmd.Parameters.AddWithValue("@buyerName", buyerName);
                            cmd.Parameters.AddWithValue("@location", location);
                            cmd.Parameters.AddWithValue("@saleMode", saleMode);
                            cmd.Parameters.AddWithValue("@status", status);
                            cmd.Parameters.AddWithValue("@totalAmount", totalAmount);
                            cmd.Parameters.AddWithValue("@paidAmount", paidAmount);
                            cmd.Parameters.AddWithValue("@handler", handler);
                            cmd.Parameters.AddWithValue("@remark", remark);
                            cmd.Parameters.AddWithValue("@date", date);
                            cmd.Parameters.AddWithValue("@sourceDeviceId", sourceDeviceId);
                            cmd.Parameters.AddWithValue("@sourceRecordId", storedSourceRecordId ?? "");
                            cmd.Parameters.AddWithValue("@id", billId);
                            cmd.ExecuteNonQuery();
                        }
                    }
                    else
                    {
                        if (ExistsPresaleBillByBusinessKey(conn, billNo, trimmedSourceDeviceId))
                        {
                            errorMessage = $"同一设备已存在预售单 {billNo}，请勿重复提交";
                            return false;
                        }

                        string insertSql = @"
INSERT INTO presale_bills (
    bill_no, buyer_code, buyer_name, location, sale_mode, status, total_amount, paid_amount,
    handler, remark, date, source_device_id, source_record_id, created_time
) VALUES (
    @billNo, @buyerCode, @buyerName, @location, @saleMode, @status, @totalAmount, @paidAmount,
    @handler, @remark, @date, @sourceDeviceId, @sourceRecordId, @createdTime
)";
                        using (var cmd = new SQLiteCommand(insertSql, conn))
                        {
                            cmd.Parameters.AddWithValue("@billNo", billNo);
                            cmd.Parameters.AddWithValue("@buyerCode", buyerCode);
                            cmd.Parameters.AddWithValue("@buyerName", buyerName);
                            cmd.Parameters.AddWithValue("@location", location);
                            cmd.Parameters.AddWithValue("@saleMode", saleMode);
                            cmd.Parameters.AddWithValue("@status", status);
                            cmd.Parameters.AddWithValue("@totalAmount", totalAmount);
                            cmd.Parameters.AddWithValue("@paidAmount", paidAmount);
                            cmd.Parameters.AddWithValue("@handler", handler);
                            cmd.Parameters.AddWithValue("@remark", remark);
                            cmd.Parameters.AddWithValue("@date", date);
                            cmd.Parameters.AddWithValue("@sourceDeviceId", sourceDeviceId);
                            cmd.Parameters.AddWithValue("@sourceRecordId", storedSourceRecordId ?? trimmedSourceRecordId ?? "");
                            cmd.Parameters.AddWithValue("@createdTime", createdTime);
                            cmd.ExecuteNonQuery();
                        }
                        using (var idCmd = new SQLiteCommand("SELECT last_insert_rowid()", conn))
                        {
                            billId = Convert.ToInt64(idCmd.ExecuteScalar());
                        }
                    }

                    if (!string.IsNullOrWhiteSpace(storedSourceRecordId))
                    {
                        using (var delCmd = new SQLiteCommand(@"
DELETE FROM presale_items
WHERE source_record_id = @sourceRecordId OR source_record_id LIKE @itemPrefix", conn))
                        {
                            delCmd.Parameters.AddWithValue("@sourceRecordId", storedSourceRecordId);
                            delCmd.Parameters.AddWithValue("@itemPrefix", storedSourceRecordId + "_ITEM_%");
                            delCmd.ExecuteNonQuery();
                        }
                    }
                    else
                    {
                        using (var delCmd = new SQLiteCommand(
                            "DELETE FROM presale_items WHERE bill_no = @billNo AND (source_record_id IS NULL OR source_record_id = '')", conn))
                        {
                            delCmd.Parameters.AddWithValue("@billNo", billNo);
                            delCmd.ExecuteNonQuery();
                        }
                    }

                    var items = json["items"] as Newtonsoft.Json.Linq.JArray;
                    if (items != null)
                    {
                        int index = 0;
                        foreach (var item in items)
                        {
                            string spec = item["spec"]?.ToString() ?? "";
                            int quantity = 0;
                            int.TryParse(item["quantity"]?.ToString() ?? "0", out quantity);
                            decimal unitPrice = 0;
                            decimal.TryParse(item["unit_price"]?.ToString() ?? "0", out unitPrice);
                            decimal itemTotal = 0;
                            decimal.TryParse(item["total_amount"]?.ToString() ?? "0", out itemTotal);
                            string itemSourceId = string.IsNullOrWhiteSpace(storedSourceRecordId)
                                ? ""
                                : $"{storedSourceRecordId}_ITEM_{index}";

                            string itemSql = @"
INSERT INTO presale_items (bill_no, spec, quantity, shipped_quantity, unit_price, total_amount, source_record_id, created_time)
VALUES (@billNo, @spec, @quantity, @shippedQuantity, @unitPrice, @totalAmount, @sourceRecordId, @createdTime)";
                            using (var itemCmd = new SQLiteCommand(itemSql, conn))
                            {
                                itemCmd.Parameters.AddWithValue("@billNo", billNo);
                                itemCmd.Parameters.AddWithValue("@spec", spec);
                                itemCmd.Parameters.AddWithValue("@quantity", quantity);
                                int shippedQuantity = 0;
                                int.TryParse(item["shipped_quantity"]?.ToString() ?? "0", out shippedQuantity);
                                itemCmd.Parameters.AddWithValue("@shippedQuantity", shippedQuantity);
                                itemCmd.Parameters.AddWithValue("@unitPrice", unitPrice);
                                itemCmd.Parameters.AddWithValue("@totalAmount", itemTotal);
                                itemCmd.Parameters.AddWithValue("@sourceRecordId", itemSourceId);
                                itemCmd.Parameters.AddWithValue("@createdTime", createdTime);
                                itemCmd.ExecuteNonQuery();
                            }
                            index++;
                        }
                    }
                }

                if (!VerifyPresaleBillSaved(billNo, saleMode, status, storedSourceRecordId: resolvedSourceRecordId, sourceDeviceId: trimmedSourceDeviceId))
                {
                    errorMessage = $"预售单同步校验失败：{billNo} 期望 mode={saleMode} status={status}";
                    Console.WriteLine($"❌ {errorMessage}");
                    return false;
                }

                Console.WriteLine($"✅ 预售单同步保存成功：{billNo} mode={saleMode} status={status}");
                PresaleBillRemoteUpdated?.Invoke(billNo);
                return true;
            }
            catch (Exception ex)
            {
                errorMessage = $"保存预售单失败：{ex.Message}";
                Console.WriteLine($"❌ {errorMessage}");
                return false;
            }
        }

        private bool VerifyPresaleBillSaved(string billNo, string expectedSaleMode, string expectedStatus,
            string storedSourceRecordId = null, string sourceDeviceId = null)
        {
            if (string.IsNullOrWhiteSpace(billNo)) return false;
            using (var conn = new SQLiteConnection(connectionString))
            {
                conn.Open();
                string sql;
                SQLiteCommand cmd;
                if (!string.IsNullOrWhiteSpace(storedSourceRecordId))
                {
                    sql = "SELECT sale_mode, status FROM presale_bills WHERE source_record_id = @sourceRecordId LIMIT 1";
                    cmd = new SQLiteCommand(sql, conn);
                    cmd.Parameters.AddWithValue("@sourceRecordId", storedSourceRecordId);
                }
                else if (!string.IsNullOrWhiteSpace(sourceDeviceId))
                {
                    sql = "SELECT sale_mode, status FROM presale_bills WHERE bill_no = @billNo AND COALESCE(source_device_id,'') = @sourceDeviceId ORDER BY id DESC LIMIT 1";
                    cmd = new SQLiteCommand(sql, conn);
                    cmd.Parameters.AddWithValue("@billNo", billNo);
                    cmd.Parameters.AddWithValue("@sourceDeviceId", sourceDeviceId);
                }
                else
                {
                    sql = "SELECT sale_mode, status FROM presale_bills WHERE bill_no = @billNo ORDER BY id DESC LIMIT 1";
                    cmd = new SQLiteCommand(sql, conn);
                    cmd.Parameters.AddWithValue("@billNo", billNo);
                }

                using (cmd)
                using (var reader = cmd.ExecuteReader())
                {
                    if (!reader.Read()) return false;
                    string savedMode = reader["sale_mode"]?.ToString() ?? "";
                    string savedStatus = reader["status"]?.ToString() ?? "";
                    return string.Equals(savedMode, expectedSaleMode, StringComparison.OrdinalIgnoreCase)
                        && string.Equals(savedStatus, expectedStatus, StringComparison.OrdinalIgnoreCase);
                }
            }
        }

        public bool SavePresalePaymentFromSync(string jsonData)
        {
            try
            {
                var json = Newtonsoft.Json.Linq.JObject.Parse(jsonData);
                string billNo = json["bill_no"]?.ToString() ?? "";
                if (string.IsNullOrWhiteSpace(billNo))
                {
                    Console.WriteLine("❌ 预售收款同步失败：bill_no 为空");
                    return false;
                }

                string buyerCode = json["buyer_code"]?.ToString() ?? "";
                decimal amount = 0;
                decimal.TryParse(json["amount"]?.ToString() ?? "0", out amount);
                string payMethod = json["pay_method"]?.ToString() ?? "";
                string payTimeRaw = json["pay_time"]?.ToString() ?? "";
                string remark = json["remark"]?.ToString() ?? "";
                string sourceDeviceId = json["source_device_id"]?.ToString() ?? "";
                string sourceRecordId = json["source_record_id"]?.ToString() ?? "";
                string createdTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

                string payTime = createdTime;
                if (!string.IsNullOrWhiteSpace(payTimeRaw) && payTimeRaw.All(char.IsDigit) && payTimeRaw.Length > 10)
                {
                    long ts = long.Parse(payTimeRaw);
                    payTime = DateTimeOffset.FromUnixTimeMilliseconds(ts).LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss");
                }
                else if (!string.IsNullOrWhiteSpace(payTimeRaw))
                {
                    payTime = payTimeRaw;
                }

                using (var conn = new SQLiteConnection(connectionString))
                {
                    conn.Open();
                    string insertSql = @"
INSERT OR IGNORE INTO presale_payments (
    bill_no, buyer_code, amount, pay_method, pay_time, remark, source_device_id, source_record_id, created_time
) VALUES (
    @billNo, @buyerCode, @amount, @payMethod, @payTime, @remark, @sourceDeviceId, @sourceRecordId, @createdTime
)";
                    using (var cmd = new SQLiteCommand(insertSql, conn))
                    {
                        cmd.Parameters.AddWithValue("@billNo", billNo);
                        cmd.Parameters.AddWithValue("@buyerCode", buyerCode);
                        cmd.Parameters.AddWithValue("@amount", amount);
                        cmd.Parameters.AddWithValue("@payMethod", payMethod);
                        cmd.Parameters.AddWithValue("@payTime", payTime);
                        cmd.Parameters.AddWithValue("@remark", remark);
                        cmd.Parameters.AddWithValue("@sourceDeviceId", sourceDeviceId);
                        cmd.Parameters.AddWithValue("@sourceRecordId", sourceRecordId);
                        cmd.Parameters.AddWithValue("@createdTime", createdTime);
                        cmd.ExecuteNonQuery();
                    }

                    decimal paidSum = 0;
                    using (var sumCmd = new SQLiteCommand(
                        "SELECT COALESCE(SUM(amount), 0) FROM presale_payments WHERE bill_no = @billNo", conn))
                    {
                        sumCmd.Parameters.AddWithValue("@billNo", billNo);
                        paidSum = Convert.ToDecimal(sumCmd.ExecuteScalar());
                    }

                    using (var updCmd = new SQLiteCommand(
                        "UPDATE presale_bills SET paid_amount = @paidAmount WHERE bill_no = @billNo", conn))
                    {
                        updCmd.Parameters.AddWithValue("@paidAmount", paidSum);
                        updCmd.Parameters.AddWithValue("@billNo", billNo);
                        updCmd.ExecuteNonQuery();
                    }
                }

                Console.WriteLine($"✅ 预售收款同步保存成功：{billNo} 金额={amount}");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ 保存预售收款失败：{ex.Message}");
                return false;
            }
        }

        private void EnsurePresaleItemShippedQuantityColumn(SQLiteConnection conn)
        {
            if (!ColumnExists(conn, "presale_items", "shipped_quantity"))
            {
                ExecuteTableCreation(conn, "ALTER TABLE presale_items ADD COLUMN shipped_quantity INTEGER NOT NULL DEFAULT 0");
            }
        }

        public bool SavePresaleOutboundFromSync(string jsonData)
        {
            try
            {
                var json = Newtonsoft.Json.Linq.JObject.Parse(jsonData);
                string billNo = json["bill_no"]?.ToString() ?? "";
                if (string.IsNullOrWhiteSpace(billNo))
                {
                    Console.WriteLine("❌ 预售出库同步失败：bill_no 为空");
                    return false;
                }

                string buyerCode = json["buyer_code"]?.ToString() ?? "";
                string shipTimeRaw = json["ship_time"]?.ToString() ?? "";
                string remark = json["remark"]?.ToString() ?? "";
                string sourceDeviceId = json["source_device_id"]?.ToString() ?? "";
                string sourceRecordId = json["source_record_id"]?.ToString() ?? "";
                string billStatus = json["bill_status"]?.ToString() ?? "";
                string saleMode = json["sale_mode"]?.ToString() ?? "";
                string createdTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

                string shipTime = createdTime;
                if (!string.IsNullOrWhiteSpace(shipTimeRaw) && shipTimeRaw.All(char.IsDigit) && shipTimeRaw.Length > 10)
                {
                    long ts = long.Parse(shipTimeRaw);
                    shipTime = DateTimeOffset.FromUnixTimeMilliseconds(ts).LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss");
                }
                else if (!string.IsNullOrWhiteSpace(shipTimeRaw))
                {
                    shipTime = shipTimeRaw;
                }

                using (var conn = new SQLiteConnection(connectionString))
                {
                    conn.Open();
                    EnsurePresaleItemShippedQuantityColumn(conn);

                    if (!string.IsNullOrWhiteSpace(sourceRecordId))
                    {
                        using (var dupCmd = new SQLiteCommand(
                            "SELECT 1 FROM presale_outbound WHERE source_record_id = @sourceRecordId LIMIT 1", conn))
                        {
                            dupCmd.Parameters.AddWithValue("@sourceRecordId", sourceRecordId);
                            var dupObj = dupCmd.ExecuteScalar();
                            if (dupObj != null && dupObj != DBNull.Value)
                            {
                                Console.WriteLine($"预售出库已处理，跳过重复: {sourceRecordId}");
                                return true;
                            }
                        }
                    }

                    long outboundId = 0;
                    using (var insertCmd = new SQLiteCommand(@"
INSERT OR IGNORE INTO presale_outbound (
    bill_no, buyer_code, ship_time, remark, source_device_id, source_record_id, created_time
) VALUES (
    @billNo, @buyerCode, @shipTime, @remark, @sourceDeviceId, @sourceRecordId, @createdTime
)", conn))
                    {
                        insertCmd.Parameters.AddWithValue("@billNo", billNo);
                        insertCmd.Parameters.AddWithValue("@buyerCode", buyerCode);
                        insertCmd.Parameters.AddWithValue("@shipTime", shipTime);
                        insertCmd.Parameters.AddWithValue("@remark", remark);
                        insertCmd.Parameters.AddWithValue("@sourceDeviceId", sourceDeviceId);
                        insertCmd.Parameters.AddWithValue("@sourceRecordId", sourceRecordId);
                        insertCmd.Parameters.AddWithValue("@createdTime", createdTime);
                        insertCmd.ExecuteNonQuery();
                    }

                    using (var idCmd = new SQLiteCommand(
                        "SELECT id FROM presale_outbound WHERE source_record_id = @sourceRecordId LIMIT 1", conn))
                    {
                        idCmd.Parameters.AddWithValue("@sourceRecordId", sourceRecordId);
                        var idObj = idCmd.ExecuteScalar();
                        if (idObj != null && idObj != DBNull.Value)
                            outboundId = Convert.ToInt64(idObj);
                    }

                    if (outboundId <= 0)
                    {
                        using (var idCmd = new SQLiteCommand(
                            "SELECT id FROM presale_outbound WHERE bill_no = @billNo AND ship_time = @shipTime ORDER BY id DESC LIMIT 1", conn))
                        {
                            idCmd.Parameters.AddWithValue("@billNo", billNo);
                            idCmd.Parameters.AddWithValue("@shipTime", shipTime);
                            var idObj = idCmd.ExecuteScalar();
                            if (idObj != null && idObj != DBNull.Value)
                                outboundId = Convert.ToInt64(idObj);
                        }
                    }

                    var items = json["items"] as Newtonsoft.Json.Linq.JArray;
                    if (items != null && outboundId > 0)
                    {
                        int index = 0;
                        foreach (var item in items)
                        {
                            string spec = item["spec"]?.ToString() ?? "";
                            int quantity = 0;
                            int.TryParse(item["quantity"]?.ToString() ?? "0", out quantity);
                            string unit = item["unit"]?.ToString() ?? "箱";
                            if (quantity <= 0 || string.IsNullOrWhiteSpace(spec)) continue;

                            string itemSourceId = string.IsNullOrWhiteSpace(sourceRecordId)
                                ? ""
                                : $"{sourceRecordId}_ITEM_{index}";

                            using (var itemCmd = new SQLiteCommand(@"
INSERT OR IGNORE INTO presale_outbound_items (
    outbound_id, bill_no, spec, quantity, unit, source_record_id, created_time
) VALUES (
    @outboundId, @billNo, @spec, @quantity, @unit, @sourceRecordId, @createdTime
)", conn))
                            {
                                itemCmd.Parameters.AddWithValue("@outboundId", outboundId);
                                itemCmd.Parameters.AddWithValue("@billNo", billNo);
                                itemCmd.Parameters.AddWithValue("@spec", spec);
                                itemCmd.Parameters.AddWithValue("@quantity", quantity);
                                itemCmd.Parameters.AddWithValue("@unit", unit);
                                itemCmd.Parameters.AddWithValue("@sourceRecordId", itemSourceId);
                                itemCmd.Parameters.AddWithValue("@createdTime", createdTime);
                                int insertedRows = itemCmd.ExecuteNonQuery();
                                if (insertedRows > 0)
                                {
                                    using (var updCmd = new SQLiteCommand(@"
UPDATE presale_items
SET shipped_quantity = COALESCE(shipped_quantity, 0) + @quantity
WHERE bill_no = @billNo AND spec = @spec", conn))
                                    {
                                        updCmd.Parameters.AddWithValue("@quantity", quantity);
                                        updCmd.Parameters.AddWithValue("@billNo", billNo);
                                        updCmd.Parameters.AddWithValue("@spec", spec);
                                        updCmd.ExecuteNonQuery();
                                    }
                                }
                            }
                            index++;
                        }
                    }

                    if (!string.IsNullOrWhiteSpace(billStatus) && !string.IsNullOrWhiteSpace(saleMode))
                    {
                        using (var statusCmd = new SQLiteCommand(
                            "UPDATE presale_bills SET status = @status, sale_mode = @saleMode WHERE bill_no = @billNo", conn))
                        {
                            statusCmd.Parameters.AddWithValue("@status", billStatus);
                            statusCmd.Parameters.AddWithValue("@saleMode", saleMode);
                            statusCmd.Parameters.AddWithValue("@billNo", billNo);
                            statusCmd.ExecuteNonQuery();
                        }
                    }
                    else
                    {
                        bool fullyShipped = true;
                        using (var checkCmd = new SQLiteCommand(@"
SELECT quantity, COALESCE(shipped_quantity, 0) AS shipped_quantity
FROM presale_items WHERE bill_no = @billNo", conn))
                        {
                            checkCmd.Parameters.AddWithValue("@billNo", billNo);
                            using (var reader = checkCmd.ExecuteReader())
                            {
                                while (reader.Read())
                                {
                                    int qty = Convert.ToInt32(reader["quantity"]);
                                    int shipped = Convert.ToInt32(reader["shipped_quantity"]);
                                    if (shipped < qty) fullyShipped = false;
                                }
                            }
                        }
                        string newStatus = fullyShipped ? "COMPLETED" : "SHIPPED";
                        string newSaleMode = fullyShipped ? PresaleHelper.SaleModeDirectOut : PresaleHelper.SaleModePresale;
                        using (var statusCmd = new SQLiteCommand(
                            "UPDATE presale_bills SET status = @status, sale_mode = @saleMode WHERE bill_no = @billNo", conn))
                        {
                            statusCmd.Parameters.AddWithValue("@status", newStatus);
                            statusCmd.Parameters.AddWithValue("@saleMode", newSaleMode);
                            statusCmd.Parameters.AddWithValue("@billNo", billNo);
                            statusCmd.ExecuteNonQuery();
                        }
                    }
                }

                Console.WriteLine($"✅ 预售出库同步保存成功：{billNo}");
                PresaleBillRemoteUpdated?.Invoke(billNo);
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ 保存预售出库失败：{ex.Message}");
                return false;
            }
        }

        public bool SaveLedgerEntryFromSync(string jsonData)
        {
            try
            {
                var json = Newtonsoft.Json.Linq.JObject.Parse(jsonData);
                string entryNo = json["entry_no"]?.ToString() ?? "";
                if (string.IsNullOrWhiteSpace(entryNo))
                {
                    Console.WriteLine("❌ 收支流水同步失败：entry_no 为空");
                    return false;
                }

                string type = json["type"]?.ToString() ?? "";
                long categoryId = 0;
                long.TryParse(json["category_id"]?.ToString() ?? "0", out categoryId);
                string categoryName = json["category_name"]?.ToString() ?? "";
                decimal amount = 0;
                decimal.TryParse(json["amount"]?.ToString() ?? "0", out amount);
                string entryDate = json["entry_date"]?.ToString() ?? DateTime.Now.ToString("yyyy-MM-dd");
                string remark = json["remark"]?.ToString() ?? "";
                int status = 1;
                int.TryParse(json["status"]?.ToString() ?? "1", out status);
                string sourceDeviceId = json["source_device_id"]?.ToString() ?? "";
                string sourceRecordId = json["source_record_id"]?.ToString() ?? "";
                string createdTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

                using (var conn = new SQLiteConnection(connectionString))
                {
                    conn.Open();
                    long entryId = 0;
                    if (!string.IsNullOrWhiteSpace(sourceRecordId))
                    {
                        using (var findCmd = new SQLiteCommand(
                            "SELECT id FROM ledger_entry WHERE source_record_id = @sourceRecordId LIMIT 1", conn))
                        {
                            findCmd.Parameters.AddWithValue("@sourceRecordId", sourceRecordId);
                            var found = findCmd.ExecuteScalar();
                            if (found != null && found != DBNull.Value)
                                entryId = Convert.ToInt64(found);
                        }
                    }

                    if (entryId > 0)
                    {
                        string updateSql = @"
UPDATE ledger_entry SET
    entry_no = @entryNo, type = @type, category_id = @categoryId, category_name = @categoryName,
    amount = @amount, entry_date = @entryDate, remark = @remark, status = @status,
    source_device_id = @sourceDeviceId
WHERE id = @id";
                        using (var cmd = new SQLiteCommand(updateSql, conn))
                        {
                            cmd.Parameters.AddWithValue("@entryNo", entryNo);
                            cmd.Parameters.AddWithValue("@type", type);
                            cmd.Parameters.AddWithValue("@categoryId", categoryId);
                            cmd.Parameters.AddWithValue("@categoryName", categoryName);
                            cmd.Parameters.AddWithValue("@amount", amount);
                            cmd.Parameters.AddWithValue("@entryDate", entryDate);
                            cmd.Parameters.AddWithValue("@remark", remark);
                            cmd.Parameters.AddWithValue("@status", status);
                            cmd.Parameters.AddWithValue("@sourceDeviceId", sourceDeviceId);
                            cmd.Parameters.AddWithValue("@id", entryId);
                            cmd.ExecuteNonQuery();
                        }
                    }
                    else
                    {
                        string insertSql = @"
INSERT INTO ledger_entry (
    entry_no, type, category_id, category_name, amount, entry_date, remark, status,
    source_device_id, source_record_id, created_time
) VALUES (
    @entryNo, @type, @categoryId, @categoryName, @amount, @entryDate, @remark, @status,
    @sourceDeviceId, @sourceRecordId, @createdTime
)";
                        using (var cmd = new SQLiteCommand(insertSql, conn))
                        {
                            cmd.Parameters.AddWithValue("@entryNo", entryNo);
                            cmd.Parameters.AddWithValue("@type", type);
                            cmd.Parameters.AddWithValue("@categoryId", categoryId);
                            cmd.Parameters.AddWithValue("@categoryName", categoryName);
                            cmd.Parameters.AddWithValue("@amount", amount);
                            cmd.Parameters.AddWithValue("@entryDate", entryDate);
                            cmd.Parameters.AddWithValue("@remark", remark);
                            cmd.Parameters.AddWithValue("@status", status);
                            cmd.Parameters.AddWithValue("@sourceDeviceId", sourceDeviceId);
                            cmd.Parameters.AddWithValue("@sourceRecordId", sourceRecordId);
                            cmd.Parameters.AddWithValue("@createdTime", createdTime);
                            cmd.ExecuteNonQuery();
                        }
                    }
                }

                Console.WriteLine($"✅ 收支流水同步保存成功：{entryNo}");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ 保存收支流水失败：{ex.Message}");
                return false;
            }
        }

        public DataTable GetPresaleBillsForQuery(
            string startDate,
            string endDate,
            string billNo,
            string buyerName,
            string location = "",
            string handler = "",
            string debtStatus = "")
        {
            DataTable dt = new DataTable();
            try
            {
                using (var connection = new SQLiteConnection(connectionString))
                {
                    connection.Open();
                    string sql = @"
SELECT id, bill_no, buyer_code, buyer_name, location, sale_mode, status,
       total_amount, paid_amount, handler, remark, date, created_time
FROM presale_bills
WHERE date >= @startDate AND date <= @endDate
  AND (@billNo = '' OR bill_no = @billNo)
  AND (@buyerName = '' OR buyer_name = @buyerName)
  AND (@location = '' OR location = @location)
  AND (@handler = '' OR handler = @handler)
  AND (
        @debtStatus = ''
        OR (@debtStatus = 'OWED' AND total_amount > paid_amount)
        OR (@debtStatus = 'SETTLED' AND total_amount = paid_amount)
        OR (@debtStatus = 'CREDIT' AND paid_amount > total_amount)
      )
ORDER BY date DESC, created_time DESC";
                    using (var cmd = new SQLiteCommand(sql, connection))
                    {
                        cmd.Parameters.AddWithValue("@startDate", startDate ?? "");
                        cmd.Parameters.AddWithValue("@endDate", endDate ?? "");
                        cmd.Parameters.AddWithValue("@billNo", billNo ?? "");
                        cmd.Parameters.AddWithValue("@buyerName", buyerName ?? "");
                        cmd.Parameters.AddWithValue("@location", location ?? "");
                        cmd.Parameters.AddWithValue("@handler", handler ?? "");
                        cmd.Parameters.AddWithValue("@debtStatus", debtStatus ?? "");
                        using (var adapter = new SQLiteDataAdapter(cmd))
                        {
                            adapter.Fill(dt);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($">>> GetPresaleBillsForQuery失败: {ex.Message}");
            }
            return dt;
        }

        public void PopulatePresaleStockDisplayColumn(DataTable dt)
        {
            if (dt == null) return;
            if (!dt.Columns.Contains("stock_display"))
                dt.Columns.Add("stock_display", typeof(string));

            foreach (DataRow row in dt.Rows)
                row["stock_display"] = GetPresaleRowModeDisplay(row);
        }

        public string GetPresaleRowModeDisplay(DataRow row)
        {
            if (row == null) return string.Empty;

            string saleMode = row.Table.Columns.Contains("sale_mode")
                ? row["sale_mode"]?.ToString() ?? ""
                : "";
            string status = row.Table.Columns.Contains("status")
                ? row["status"]?.ToString() ?? ""
                : "";
            int quantity = 0;
            int shippedQty = 0;
            if (row.Table.Columns.Contains("quantity") && row["quantity"] != DBNull.Value)
                int.TryParse(row["quantity"]?.ToString(), out quantity);
            if (row.Table.Columns.Contains("shipped_quantity") && row["shipped_quantity"] != DBNull.Value)
                int.TryParse(row["shipped_quantity"]?.ToString(), out shippedQty);

            if (string.Equals(saleMode, PresaleHelper.SaleModeDirectOut, StringComparison.OrdinalIgnoreCase)
                || string.Equals(status, "COMPLETED", StringComparison.OrdinalIgnoreCase)
                || (quantity > 0 && shippedQty >= quantity))
            {
                return PresaleHelper.SaleModeSoldOutLabel;
            }

            string spec = row.Table.Columns.Contains("spec")
                ? row["spec"]?.ToString() ?? ""
                : "";
            if (string.IsNullOrWhiteSpace(spec))
                return string.Empty;

            return $"{PresaleHelper.SaleModePresaleLabel}{quantity},{PresaleHelper.SaleModeSoldLabel}{shippedQty}";
        }

        public DataTable GetPresaleRecordsForQuery(
            string startDate,
            string endDate,
            string billNo,
            string buyerName,
            string location = "",
            string handler = "",
            string debtStatus = "")
        {
            DataTable dt = new DataTable();
            try
            {
                using (var connection = new SQLiteConnection(connectionString))
                {
                    connection.Open();
                    EnsurePresaleItemShippedQuantityColumn(connection);
                    string sql = @"
SELECT
  b.id AS bill_id,
  i.id AS item_id,
  b.bill_no, b.buyer_code, b.buyer_name, b.location, b.sale_mode, b.status,
  b.total_amount, b.paid_amount, b.handler, b.remark, b.date,
  i.spec, i.quantity, COALESCE(i.shipped_quantity, 0) AS shipped_quantity,
  i.unit_price, i.total_amount AS item_amount
FROM presale_bills b
LEFT JOIN presale_items i ON b.bill_no = i.bill_no
WHERE b.date >= @startDate AND b.date <= @endDate
  AND (@billNo = '' OR b.bill_no LIKE '%' || @billNo || '%')
  AND (@buyerName = '' OR b.buyer_name LIKE '%' || @buyerName || '%' OR b.buyer_code LIKE '%' || @buyerName || '%')
  AND (@location = '' OR b.location LIKE '%' || @location || '%')
  AND (@handler = '' OR b.handler LIKE '%' || @handler || '%')
  AND (
        @debtStatus = ''
        OR (@debtStatus = 'OWED' AND b.total_amount > b.paid_amount)
        OR (@debtStatus = 'SETTLED' AND b.total_amount = b.paid_amount)
        OR (@debtStatus = 'CREDIT' AND b.paid_amount > b.total_amount)
      )
ORDER BY b.date DESC, b.bill_no, i.id";
                    using (var cmd = new SQLiteCommand(sql, connection))
                    {
                        cmd.Parameters.AddWithValue("@startDate", startDate ?? "");
                        cmd.Parameters.AddWithValue("@endDate", endDate ?? "");
                        cmd.Parameters.AddWithValue("@billNo", billNo ?? "");
                        cmd.Parameters.AddWithValue("@buyerName", buyerName ?? "");
                        cmd.Parameters.AddWithValue("@location", location ?? "");
                        cmd.Parameters.AddWithValue("@handler", handler ?? "");
                        cmd.Parameters.AddWithValue("@debtStatus", debtStatus ?? "");
                        using (var adapter = new SQLiteDataAdapter(cmd))
                        {
                            adapter.Fill(dt);
                        }
                    }
                    PopulatePresaleStockDisplayColumn(dt);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($">>> GetPresaleRecordsForQuery失败: {ex.Message}");
            }
            return dt;
        }

        /// <summary>汇总买家全部预售单的应收减已收；正数为欠款，负数为余额。</summary>
        public decimal GetPresaleBuyerBalance(string buyerCode, string buyerName)
        {
            try
            {
                using (var connection = new SQLiteConnection(connectionString))
                {
                    connection.Open();
                    string sql;
                    if (!string.IsNullOrWhiteSpace(buyerName))
                    {
                        sql = @"
SELECT COALESCE(SUM(total_amount), 0) - COALESCE(SUM(paid_amount), 0)
FROM presale_bills
WHERE buyer_name = @buyerName";
                    }
                    else if (!string.IsNullOrWhiteSpace(buyerCode))
                    {
                        sql = @"
SELECT COALESCE(SUM(total_amount), 0) - COALESCE(SUM(paid_amount), 0)
FROM presale_bills
WHERE buyer_code = @buyerCode";
                    }
                    else
                    {
                        return 0m;
                    }

                    using (var cmd = new SQLiteCommand(sql, connection))
                    {
                        if (!string.IsNullOrWhiteSpace(buyerName))
                            cmd.Parameters.AddWithValue("@buyerName", buyerName);
                        else
                            cmd.Parameters.AddWithValue("@buyerCode", buyerCode);

                        object result = cmd.ExecuteScalar();
                        if (result == null || result == DBNull.Value)
                            return 0m;
                        return Convert.ToDecimal(result);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($">>> GetPresaleBuyerBalance失败: {ex.Message}");
                return 0m;
            }
        }

        public List<string> GetPresaleDistinctValues(string columnName, string startDate, string endDate)
        {
            return GetTableDistinctValues("presale_bills", columnName, "date", startDate, endDate);
        }

        public List<string> GetLedgerDistinctValues(string columnName, string startDate, string endDate)
        {
            return GetTableDistinctValues("ledger_entry", columnName, "entry_date", startDate, endDate);
        }

        private List<string> GetTableDistinctValues(
            string tableName,
            string columnName,
            string dateColumn,
            string startDate,
            string endDate)
        {
            var values = new List<string>();
            if (string.IsNullOrWhiteSpace(tableName) || string.IsNullOrWhiteSpace(columnName))
                return values;

            try
            {
                using (var connection = new SQLiteConnection(connectionString))
                {
                    connection.Open();
                    string sql = $@"
SELECT DISTINCT {columnName}
FROM {tableName}
WHERE {columnName} IS NOT NULL AND TRIM({columnName}) != ''";
                    if (!string.IsNullOrWhiteSpace(startDate) && !string.IsNullOrWhiteSpace(endDate))
                        sql += $" AND {dateColumn} >= @startDate AND {dateColumn} <= @endDate";
                    sql += $" ORDER BY {columnName} LIMIT 500";

                    using (var cmd = new SQLiteCommand(sql, connection))
                    {
                        if (!string.IsNullOrWhiteSpace(startDate) && !string.IsNullOrWhiteSpace(endDate))
                        {
                            cmd.Parameters.AddWithValue("@startDate", startDate);
                            cmd.Parameters.AddWithValue("@endDate", endDate);
                        }

                        using (var reader = cmd.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                string value = reader[0]?.ToString()?.Trim();
                                if (!string.IsNullOrEmpty(value))
                                    values.Add(value);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($">>> GetTableDistinctValues失败({tableName}.{columnName}): {ex.Message}");
            }

            return values;
        }

        public DataTable GetPresaleItemsForBill(string billNo)
        {
            DataTable dt = new DataTable();
            try
            {
                using (var connection = new SQLiteConnection(connectionString))
                {
                    connection.Open();
                    string sql = @"
SELECT id, bill_no, spec, quantity, unit_price, total_amount, created_time
FROM presale_items
WHERE bill_no = @billNo
ORDER BY id";
                    using (var cmd = new SQLiteCommand(sql, connection))
                    {
                        cmd.Parameters.AddWithValue("@billNo", billNo ?? "");
                        using (var adapter = new SQLiteDataAdapter(cmd))
                        {
                            adapter.Fill(dt);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($">>> GetPresaleItemsForBill失败: {ex.Message}");
            }
            return dt;
        }

        /// <summary>按明细汇总更新预售单应收总额。</summary>
        public bool RecalculatePresaleBillTotal(string billNo)
        {
            if (string.IsNullOrWhiteSpace(billNo))
                return false;

            try
            {
                const string sql = @"
UPDATE presale_bills
SET total_amount = (
    SELECT COALESCE(SUM(total_amount), 0)
    FROM presale_items
    WHERE bill_no = @billNo
)
WHERE bill_no = @billNo";
                return ExecuteNonQuery(sql, new Dictionary<string, object> { { "@billNo", billNo } }) > 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine($">>> RecalculatePresaleBillTotal失败: {ex.Message}");
                return false;
            }
        }

        public DataTable GetLedgerEntriesForQuery(
            string startDate,
            string endDate,
            string typeFilter,
            string categoryName)
        {
            DataTable dt = new DataTable();
            try
            {
                using (var connection = new SQLiteConnection(connectionString))
                {
                    connection.Open();
                    string sql = @"
SELECT id, entry_no, type, category_name, amount, entry_date, remark, status, created_time
FROM ledger_entry
WHERE entry_date >= @startDate AND entry_date <= @endDate
  AND (@typeFilter = '' OR type = @typeFilter)
  AND (@categoryName = '' OR category_name LIKE '%' || @categoryName || '%')
ORDER BY entry_date DESC, created_time DESC";
                    using (var cmd = new SQLiteCommand(sql, connection))
                    {
                        cmd.Parameters.AddWithValue("@startDate", startDate ?? "");
                        cmd.Parameters.AddWithValue("@endDate", endDate ?? "");
                        cmd.Parameters.AddWithValue("@typeFilter", typeFilter ?? "");
                        cmd.Parameters.AddWithValue("@categoryName", categoryName ?? "");
                        using (var adapter = new SQLiteDataAdapter(cmd))
                        {
                            adapter.Fill(dt);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($">>> GetLedgerEntriesForQuery失败: {ex.Message}");
            }
            return dt;
        }

        public void GetLedgerSummaryForQuery(string startDate, string endDate, string typeFilter, string categoryName,
            out decimal totalIncome, out decimal totalExpense)
        {
            totalIncome = 0;
            totalExpense = 0;
            try
            {
                using (var connection = new SQLiteConnection(connectionString))
                {
                    connection.Open();
                    string sql = @"
SELECT
    COALESCE(SUM(CASE WHEN type = 'INCOME' AND status = 1 THEN amount ELSE 0 END), 0) AS income,
    COALESCE(SUM(CASE WHEN type = 'EXPENSE' AND status = 1 THEN amount ELSE 0 END), 0) AS expense
FROM ledger_entry
WHERE entry_date >= @startDate AND entry_date <= @endDate
  AND (@typeFilter = '' OR type = @typeFilter)
  AND (@categoryName = '' OR category_name LIKE '%' || @categoryName || '%')";
                    using (var cmd = new SQLiteCommand(sql, connection))
                    {
                        cmd.Parameters.AddWithValue("@startDate", startDate ?? "");
                        cmd.Parameters.AddWithValue("@endDate", endDate ?? "");
                        cmd.Parameters.AddWithValue("@typeFilter", typeFilter ?? "");
                        cmd.Parameters.AddWithValue("@categoryName", categoryName ?? "");
                        using (var reader = cmd.ExecuteReader())
                        {
                            if (reader.Read())
                            {
                                totalIncome = Convert.ToDecimal(reader["income"]);
                                totalExpense = Convert.ToDecimal(reader["expense"]);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($">>> GetLedgerSummaryForQuery失败: {ex.Message}");
            }
        }


        #endregion  // 这里闭合前面的某个#region
        // ClientInfo类
        public class ClientInfo
        {
            public string Code { get; set; }
            public string Name { get; set; }
        }
        #endregion
        #region 新版多型号查询方法

        public DataTable GetInboundRecordsWithDetails(DateTime? startDate = null, DateTime? endDate = null,
            string clientCode = "", string spec = "")
        {
            try
            {
                Console.WriteLine($">>> [GetInboundRecordsWithDetails] 查询入库记录: {startDate} 至 {endDate}, 客户: {clientCode}, 型号: {spec}");

                string sql = @"
            SELECT 
                it.id,
                it.order_no,
                it.client_code,
                it.client_name,
                it.location,
                it.date,
                it.handler,
                it.creator,
                it.item_count,
                it.total_quantity,
                it.created_time,
                (SELECT GROUP_CONCAT(ii.spec || '×' || ii.quantity, '、 ') 
                 FROM inbound_items ii 
                 WHERE ii.order_no = it.order_no) as specs_summary,
                (SELECT SUM(ii.total_amount) 
                 FROM inbound_items ii 
                 WHERE ii.order_no = it.order_no) as total_amount
            FROM inbound_transactions it
            WHERE 1=1";

                var parameters = new Dictionary<string, object>();

                if (startDate.HasValue)
                {
                    sql += " AND it.date >= @startDate";
                    parameters.Add("@startDate", startDate.Value.ToString("yyyy-MM-dd"));
                }

                if (endDate.HasValue)
                {
                    sql += " AND it.date <= @endDate";
                    parameters.Add("@endDate", endDate.Value.ToString("yyyy-MM-dd"));
                }

                if (!string.IsNullOrEmpty(clientCode))
                {
                    sql += " AND it.client_code LIKE @clientCode";
                    parameters.Add("@clientCode", $"%{clientCode}%");
                }

                if (!string.IsNullOrEmpty(spec))
                {
                    sql += " AND EXISTS (SELECT 1 FROM inbound_items ii WHERE ii.order_no = it.order_no AND ii.spec LIKE @spec)";
                    parameters.Add("@spec", $"%{spec}%");
                }

                sql += " ORDER BY it.date DESC, it.id DESC";

                DataTable result = ExecuteQuery(sql, parameters);
                Console.WriteLine($">>> [GetInboundRecordsWithDetails] 查询成功，返回 {result.Rows.Count} 条记录");

                return result;
            }
            catch (Exception ex)
            {
                Console.WriteLine($">>> [GetInboundRecordsWithDetails] 异常: {ex.Message}");
                return new DataTable();
            }
        }

        public DataTable GetSalesRecordsWithDetails(DateTime? startDate = null, DateTime? endDate = null,
            string clientCode = "", string spec = "")
        {
            try
            {
                string sql = @"
            SELECT 
                st.id,
                st.order_no,
                st.client_code,
                st.client_name,
                st.date,
                st.handler,
                st.creator,
                st.item_count,
                st.total_quantity,
                st.created_time,
                (SELECT GROUP_CONCAT(si.spec || '×' || si.quantity, '、 ') 
                 FROM sales_items si 
                 WHERE si.order_no = st.order_no) as specs_summary,
                (SELECT SUM(si.total_amount) 
                 FROM sales_items si 
                 WHERE si.order_no = st.order_no) as total_amount
            FROM sales_transactions st
            WHERE 1=1";

                var parameters = new Dictionary<string, object>();

                if (startDate.HasValue)
                {
                    sql += " AND st.date >= @startDate";
                    parameters.Add("@startDate", startDate.Value.ToString("yyyy-MM-dd"));
                }

                if (endDate.HasValue)
                {
                    sql += " AND st.date <= @endDate";
                    parameters.Add("@endDate", endDate.Value.ToString("yyyy-MM-dd"));
                }

                if (!string.IsNullOrEmpty(clientCode))
                {
                    sql += " AND st.client_code LIKE @clientCode";
                    parameters.Add("@clientCode", $"%{clientCode}%");
                }

                if (!string.IsNullOrEmpty(spec))
                {
                    sql += " AND EXISTS (SELECT 1 FROM sales_items si WHERE si.order_no = st.order_no AND si.spec LIKE @spec)";
                    parameters.Add("@spec", $"%{spec}%");
                }

                sql += " ORDER BY st.date DESC, st.id DESC";

                return ExecuteQuery(sql, parameters);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"获取销售记录（带明细）失败: {ex.Message}");
                return new DataTable();
            }
        }

        public DataTable GetPackagingRecordsWithDetails(DateTime? startDate = null, DateTime? endDate = null,
            string clientCode = "", string packType = "")
        {
            try
            {
                string sql = @"
            SELECT 
                pt.id,
                pt.order_no,
                pt.client_code,
                pt.client_name,
                pt.handler,
                pt.creator,
                pt.item_count,
                pt.total_quantity,
                DATE(pt.created_time) as date,
                pt.created_time,
                (SELECT GROUP_CONCAT(pi.pack_type || '×' || pi.quantity, '、 ') 
                 FROM packaging_items pi 
                 WHERE pi.order_no = pt.order_no) as pack_types_summary,
                (SELECT SUM(pi.total_amount) 
                 FROM packaging_items pi 
                 WHERE pi.order_no = pt.order_no) as total_amount
            FROM packaging_transactions pt
            WHERE 1=1";

                var parameters = new Dictionary<string, object>();

                if (startDate.HasValue)
                {
                    sql += " AND DATE(pt.created_time) >= @startDate";
                    parameters.Add("@startDate", startDate.Value.ToString("yyyy-MM-dd"));
                }

                if (endDate.HasValue)
                {
                    sql += " AND DATE(pt.created_time) <= @endDate";
                    parameters.Add("@endDate", endDate.Value.ToString("yyyy-MM-dd"));
                }

                if (!string.IsNullOrEmpty(clientCode))
                {
                    sql += " AND pt.client_code LIKE @clientCode";
                    parameters.Add("@clientCode", $"%{clientCode}%");
                }

                if (!string.IsNullOrEmpty(packType))
                {
                    sql += " AND EXISTS (SELECT 1 FROM packaging_items pi WHERE pi.order_no = pt.order_no AND pi.pack_type LIKE @packType)";
                    parameters.Add("@packType", $"%{packType}%");
                }

                sql += " ORDER BY pt.created_time DESC";

                return ExecuteQuery(sql, parameters);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"获取包装记录（带明细）失败: {ex.Message}");
                return new DataTable();
            }
        }

        public DataTable GetInboundItemsByOrderNo(string orderNo)
        {
            try
            {
                string sql = @"
            SELECT 
                id,
                order_no,
                spec,
                quantity,
                unit_price,
                total_amount,
                created_time
            FROM inbound_items 
            WHERE order_no = @order_no
            ORDER BY id ASC";

                return ExecuteQuery(sql, new Dictionary<string, object>
        {
            { "@order_no", orderNo }
        });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"获取入库明细失败: {ex.Message}");
                return new DataTable();
            }
        }

        public DataTable GetSalesItemsByOrderNo(string orderNo)
        {
            try
            {
                string sql = @"
            SELECT 
                id,
                order_no,
                spec,
                quantity,
                unit_price,
                total_amount,
                created_time
            FROM sales_items 
            WHERE order_no = @order_no
            ORDER BY id ASC";

                return ExecuteQuery(sql, new Dictionary<string, object>
        {
            { "@order_no", orderNo }
        });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"获取销售明细失败: {ex.Message}");
                return new DataTable();
            }
        }

        public DataTable GetPackagingItemsByOrderNo(string orderNo)
        {
            try
            {
                string sql = @"
            SELECT 
                id,
                order_no,
                pack_type,
                quantity,
                unit_price,
                total_amount,
                created_time
            FROM packaging_items 
            WHERE order_no = @order_no
            ORDER BY id ASC";

                return ExecuteQuery(sql, new Dictionary<string, object>
        {
            { "@order_no", orderNo }
        });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"获取包装明细失败: {ex.Message}");
                return new DataTable();
            }
        }

        #endregion

        #region 事务控制方法

        public bool BeginTransaction()
        {
            try
            {
                ExecuteNonQuery("BEGIN TRANSACTION");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"开始事务失败: {ex.Message}");
                return false;
            }
        }

        public bool CommitTransaction()
        {
            try
            {
                ExecuteNonQuery("COMMIT");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"提交事务失败: {ex.Message}");
                return false;
            }
        }

        public bool RollbackTransaction()
        {
            try
            {
                ExecuteNonQuery("ROLLBACK");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"回滚事务失败: {ex.Message}");
                return false;
            }
        }
        

        
        public void CheckAndFixPackagingTableDateField()
{
    try
    {
        Console.WriteLine("=== 检查并修复包装表date字段 ===");
        
        using (var connection = new SQLiteConnection(connectionString))
        {
            connection.Open();
            
            // 检查date字段是否存在
            bool hasDateField = ColumnExists(connection, "packaging_transactions", "date");
            
            if (!hasDateField)
            {
                Console.WriteLine("包装表缺少date字段，正在添加...");
                
                // 添加date字段
                string alterSql = "ALTER TABLE packaging_transactions ADD COLUMN date TEXT";
                ExecuteNonQuery(alterSql);
                
                Console.WriteLine("date字段添加成功");
                
                // 填充date字段数据
                string updateSql = @"
                    UPDATE packaging_transactions 
                    SET date = DATE(created_time) 
                    WHERE date IS NULL OR date = ''";
                
                int updated = ExecuteNonQuery(updateSql);
                Console.WriteLine($"已更新 {updated} 条记录的date字段");
            }
            else
            {
                Console.WriteLine("包装表已有date字段");
                
                // 检查是否有空值并填充
                DataTable nullCheck = ExecuteQuery(
                    "SELECT COUNT(*) as null_count FROM packaging_transactions WHERE date IS NULL OR date = ''");
                
                if (nullCheck.Rows.Count > 0 && Convert.ToInt32(nullCheck.Rows[0]["null_count"]) > 0)
                {
                    Console.WriteLine("发现空的date字段，正在填充...");
                    
                    string updateSql = @"
                        UPDATE packaging_transactions 
                        SET date = DATE(created_time) 
                        WHERE date IS NULL OR date = ''";
                    
                    int updated = ExecuteNonQuery(updateSql);
                    Console.WriteLine($"已填充 {updated} 条记录的date字段");
                }
            }
        }
        
        Console.WriteLine("=== 包装表date字段检查完成 ===");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"检查修复包装表date字段失败: {ex.Message}");
    }
}

        #endregion
        // 在 DatabaseManager 类中添加这个方法（放在 #region 新版多型号查询方法 后面）

        #region 客户对账入库统计方法

        // 修改 GetClientInboundStatistics 方法，去掉金额字段
        // 修改 GetClientInboundStatistics 方法，去掉金额字段
        public DataTable GetClientInboundStatistics(string clientCode, DateTime startDate, DateTime endDate)
        {
            try
            {
                Console.WriteLine($">>> [GetClientInboundStatistics] 开始查询: 客户={clientCode}, 日期={startDate:yyyy-MM-dd} 至 {endDate:yyyy-MM-dd}");

                string sql = @"
            SELECT 
                spec AS 型号,
                SUM(quantity) AS 入库数量,
                COUNT(DISTINCT order_no) AS 入库单数
            FROM inbound_transactions 
            WHERE client_code = @clientCode 
              AND date BETWEEN @startDate AND @endDate 
            GROUP BY spec 
            ORDER BY 入库数量 DESC";

                var parameters = new Dictionary<string, object>
        {
            { "@clientCode", clientCode },
            { "@startDate", startDate.ToString("yyyy-MM-dd") },
            { "@endDate", endDate.ToString("yyyy-MM-dd") }
        };

                DataTable result = ExecuteQuery(sql, parameters);

                Console.WriteLine($">>> [GetClientInboundStatistics] 查询完成，找到 {result.Rows.Count} 种型号");

                return result;
            }
            catch (Exception ex)
            {
                Console.WriteLine($">>> [GetClientInboundStatistics] 异常: {ex.Message}");
                return new DataTable();
            }
        }

        // 修改 GetClientInboundTotal 方法，去掉金额字段
        public DataTable GetClientInboundTotal(string clientCode, DateTime startDate, DateTime endDate)
        {
            try
            {
                string sql = @"
            SELECT 
                COUNT(DISTINCT order_no) AS 总单数,
                SUM(quantity) AS 总数量,
                COUNT(DISTINCT spec) AS 型号种数
            FROM inbound_transactions 
            WHERE client_code = @clientCode 
              AND date BETWEEN @startDate AND @endDate";

                var parameters = new Dictionary<string, object>
        {
            { "@clientCode", clientCode },
            { "@startDate", startDate.ToString("yyyy-MM-dd") },
            { "@endDate", endDate.ToString("yyyy-MM-dd") }
        };

                return ExecuteQuery(sql, parameters);
            }
            catch (Exception ex)
            {
                Console.WriteLine($">>> [GetClientInboundTotal] 异常: {ex.Message}");
                return new DataTable();
            }
        }

        // ===== 新增：按库位分组的入库统计方法 =====
        public DataTable GetClientInboundStatisticsByLocation(string clientCode, DateTime startDate, DateTime endDate)
        {
            try
            {
                Console.WriteLine($">>> [GetClientInboundStatisticsByLocation] 开始查询: 客户={clientCode}, 日期={startDate:yyyy-MM-dd} 至 {endDate:yyyy-MM-dd}");

                string sql = @"
            SELECT 
                COALESCE(location, '未指定库位') AS 库位,
                spec AS 型号,
                SUM(quantity) AS 入库数量,
                COUNT(DISTINCT order_no) AS 入库单数
            FROM inbound_transactions 
            WHERE client_code = @clientCode 
              AND date BETWEEN @startDate AND @endDate 
            GROUP BY location, spec 
            ORDER BY location, spec";

                var parameters = new Dictionary<string, object>
        {
            { "@clientCode", clientCode },
            { "@startDate", startDate.ToString("yyyy-MM-dd") },
            { "@endDate", endDate.ToString("yyyy-MM-dd") }
        };

                DataTable result = ExecuteQuery(sql, parameters);

                Console.WriteLine($">>> [GetClientInboundStatisticsByLocation] 查询完成，找到 {result.Rows.Count} 条记录");

                return result;
            }
            catch (Exception ex)
            {
                Console.WriteLine($">>> [GetClientInboundStatisticsByLocation] 异常: {ex.Message}");
                return new DataTable();
            }
        }

        #endregion

        #region 年度终结

        public const string InventoryCarryoverOpeningInbound = "OPENING_INBOUND";
        public const string InventoryCarryoverEmpty = "EMPTY";

        public static string GetDatabaseFilePath()
        {
            if (FiscalYearService.IsInitialized)
                return FiscalYearService.GetActiveDbPath();
            var dm = new DatabaseManager();
            return dm.GetDatabasePath();
        }

        /// <summary>当前活跃年份数据库的连接字符串（供查询窗体统一使用）。</summary>
        public static string GetActiveConnectionString()
        {
            return BuildConnectionString(GetDatabaseFilePath());
        }

        private void EnsureYearEndTablesInConnection(SQLiteConnection connection)
        {
            const string settlementTable = @"
CREATE TABLE IF NOT EXISTS year_end_settlement (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    fiscal_year INTEGER NOT NULL UNIQUE,
    archive_path TEXT NOT NULL,
    settled_at DATETIME DEFAULT CURRENT_TIMESTAMP,
    settled_by TEXT,
    inventory_carryover TEXT,
    opening_balance_total REAL DEFAULT 0,
    opening_inventory_rows INTEGER DEFAULT 0,
    remark TEXT
)";
            const string openingBalanceTable = @"
CREATE TABLE IF NOT EXISTS client_opening_balance (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    client_code TEXT NOT NULL,
    client_name TEXT,
    payable_amount REAL NOT NULL,
    source_year INTEGER NOT NULL,
    effective_date TEXT NOT NULL,
    remark TEXT,
    created_time DATETIME DEFAULT CURRENT_TIMESTAMP
)";
            ExecuteTableCreation(connection, settlementTable);
            ExecuteTableCreation(connection, openingBalanceTable);
            ExecuteTableCreation(connection, "CREATE INDEX IF NOT EXISTS idx_opening_balance_client ON client_opening_balance(client_code)");
        }

        public bool IsYearEndSettled(int fiscalYear)
        {
            try
            {
                using (var conn = new SQLiteConnection(connectionString))
                {
                    conn.Open();
                    using (var cmd = new SQLiteCommand(
                        "SELECT 1 FROM year_end_settlement WHERE fiscal_year=@y LIMIT 1", conn))
                    {
                        cmd.Parameters.AddWithValue("@y", fiscalYear);
                        return cmd.ExecuteScalar() != null;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($">>> IsYearEndSettled 失败: {ex.Message}");
                return false;
            }
        }

        public decimal GetClientOpeningBalanceForQuery(string clientCode, DateTime startDate)
        {
            try
            {
                using (var conn = new SQLiteConnection(connectionString))
                {
                    conn.Open();
                    using (var cmd = new SQLiteCommand(@"
SELECT COALESCE(SUM(payable_amount), 0)
FROM client_opening_balance
WHERE client_code=@code AND effective_date <= @startDate", conn))
                    {
                        cmd.Parameters.AddWithValue("@code", clientCode ?? "");
                        cmd.Parameters.AddWithValue("@startDate", startDate.ToString("yyyy-MM-dd"));
                        return Convert.ToDecimal(cmd.ExecuteScalar());
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($">>> GetClientOpeningBalanceForQuery 失败: {ex.Message}");
                return 0;
            }
        }

        public decimal CalculateClientPayableBalanceAllTime(string clientCode)
        {
            try
            {
                using (var conn = new SQLiteConnection(connectionString))
                {
                    conn.Open();

                    decimal salesTotal = ScalarDecimal(conn, @"
SELECT COALESCE(SUM(total_amount), 0) FROM sales_transactions
WHERE client_code=@code AND (is_settled=0 OR is_settled IS NULL)", clientCode);

                    decimal packagingTotal = ScalarDecimal(conn, @"
SELECT COALESCE(SUM(total_amount), 0) FROM packaging_transactions
WHERE client_code=@code AND (is_settled=0 OR is_settled IS NULL)", clientCode);

                    decimal deductionTotal = ScalarDecimal(conn, @"
SELECT COALESCE(SUM(amount), 0) FROM deductions WHERE client_code=@code", clientCode);

                    decimal advanceTotal = ScalarDecimal(conn, @"
SELECT COALESCE(SUM(amount), 0) FROM advances WHERE client_code=@code", clientCode);

                    return salesTotal - packagingTotal - deductionTotal - advanceTotal;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($">>> CalculateClientPayableBalanceAllTime 失败: {ex.Message}");
                return 0;
            }
        }

        private static decimal ScalarDecimal(SQLiteConnection conn, string sql, string clientCode)
        {
            using (var cmd = new SQLiteCommand(sql, conn))
            {
                cmd.Parameters.AddWithValue("@code", clientCode ?? "");
                return Convert.ToDecimal(cmd.ExecuteScalar());
            }
        }

        public List<ClientOpeningBalanceItem> CalculateAllClientOpeningBalances()
        {
            var list = new List<ClientOpeningBalanceItem>();
            DataTable clients = GetAllClients();
            foreach (DataRow row in clients.Rows)
            {
                string code = row["code"]?.ToString() ?? "";
                if (string.IsNullOrWhiteSpace(code))
                    continue;

                decimal payable = CalculateClientPayableBalanceAllTime(code);
                if (payable == 0)
                    continue;

                list.Add(new ClientOpeningBalanceItem
                {
                    ClientCode = code,
                    ClientName = row["name"]?.ToString() ?? "",
                    PayableAmount = payable
                });
            }
            return list;
        }

        public YearEndPreviewData GetYearEndPreview(int fiscalYear)
        {
            var preview = new YearEndPreviewData { FiscalYear = fiscalYear };
            DateTime start = new DateTime(fiscalYear, 1, 1);
            DateTime end = new DateTime(fiscalYear, 12, 31);

            preview.AlreadySettled = IsYearEndSettled(fiscalYear);
            preview.OpeningBalances = CalculateAllClientOpeningBalances();
            preview.OpeningBalanceTotal = preview.OpeningBalances.Sum(x => x.PayableAmount);

            DataTable inventory = GetInventoryByLocation();
            foreach (DataRow row in inventory.Rows)
            {
                int stock = Convert.ToInt32(row["当前库存"]);
                if (stock <= 0)
                    continue;
                preview.InventoryRows.Add(new InventoryCarryoverRow
                {
                    Location = row["库位"]?.ToString() ?? "",
                    Spec = row["商品型号"]?.ToString() ?? "",
                    Quantity = stock
                });
            }

            preview.InboundCount = CountScalar($"SELECT COUNT(*) FROM inbound_transactions WHERE date BETWEEN '{start:yyyy-MM-dd}' AND '{end:yyyy-MM-dd}'");
            preview.SalesCount = CountScalar($"SELECT COUNT(*) FROM sales_transactions WHERE date BETWEEN '{start:yyyy-MM-dd}' AND '{end:yyyy-MM-dd}'");
            preview.PackagingCount = CountScalar($"SELECT COUNT(*) FROM packaging_transactions WHERE DATE(created_time) BETWEEN '{start:yyyy-MM-dd}' AND '{end:yyyy-MM-dd}'");
            preview.LedgerCount = CountScalar($"SELECT COUNT(*) FROM ledger_entry WHERE entry_date BETWEEN '{start:yyyy-MM-dd}' AND '{end:yyyy-MM-dd}'");

            GetLedgerSummaryForQuery(start.ToString("yyyy-MM-dd"), end.ToString("yyyy-MM-dd"), "", "",
                out decimal income, out decimal expense);
            preview.LedgerIncome = income;
            preview.LedgerExpense = expense;
            preview.LedgerProfit = income - expense;

            return preview;
        }

        private int CountScalar(string sql)
        {
            try
            {
                using (var conn = new SQLiteConnection(connectionString))
                {
                    conn.Open();
                    using (var cmd = new SQLiteCommand(sql, conn))
                        return Convert.ToInt32(cmd.ExecuteScalar());
                }
            }
            catch
            {
                return 0;
            }
        }

        public DataTable GetYearEndClientBalanceReport(int fiscalYear)
        {
            var dt = new DataTable();
            dt.Columns.Add("客户编号", typeof(string));
            dt.Columns.Add("客户名称", typeof(string));
            dt.Columns.Add("销售总额", typeof(decimal));
            dt.Columns.Add("包装总额", typeof(decimal));
            dt.Columns.Add("扣款总额", typeof(decimal));
            dt.Columns.Add("预支总额", typeof(decimal));
            dt.Columns.Add("应付余额", typeof(decimal));

            DateTime start = new DateTime(fiscalYear, 1, 1);
            DateTime end = new DateTime(fiscalYear, 12, 31);
            DataTable clients = GetAllClients();

            foreach (DataRow client in clients.Rows)
            {
                string code = client["code"]?.ToString() ?? "";
                if (string.IsNullOrWhiteSpace(code))
                    continue;

                DataTable overview = GetClientBalanceOverview(code, start, end);
                if (overview.Rows.Count == 0)
                    continue;

                DataRow o = overview.Rows[0];
                decimal payable = Convert.ToDecimal(o["PayableTotal"]);
                if (payable == 0 &&
                    Convert.ToDecimal(o["SalesTotal"]) == 0 &&
                    Convert.ToDecimal(o["PackagingTotal"]) == 0 &&
                    Convert.ToDecimal(o["DeductionTotal"]) == 0 &&
                    Convert.ToDecimal(o["AdvanceTotal"]) == 0)
                    continue;

                dt.Rows.Add(
                    code,
                    client["name"]?.ToString() ?? "",
                    o["SalesTotal"],
                    o["PackagingTotal"],
                    o["DeductionTotal"],
                    o["AdvanceTotal"],
                    payable);
            }
            return dt;
        }

        public DataTable GetYearEndProfitReport(int fiscalYear)
        {
            var dt = new DataTable();
            dt.Columns.Add("项目", typeof(string));
            dt.Columns.Add("金额", typeof(decimal));

            DateTime start = new DateTime(fiscalYear, 1, 1);
            DateTime end = new DateTime(fiscalYear, 12, 31);

            GetLedgerSummaryForQuery(start.ToString("yyyy-MM-dd"), end.ToString("yyyy-MM-dd"), "", "",
                out decimal income, out decimal expense);

            dt.Rows.Add("收支流水-收入", income);
            dt.Rows.Add("收支流水-支出", expense);
            dt.Rows.Add("收支流水-利润", income - expense);

            DataTable byDate = GetSummaryByDate(start, end, "月");
            decimal salesAmount = 0, inboundAmount = 0, packagingAmount = 0;
            foreach (DataRow row in byDate.Rows)
            {
                if (row["销售金额"] != DBNull.Value) salesAmount += Convert.ToDecimal(row["销售金额"]);
                if (row["入库金额"] != DBNull.Value) inboundAmount += Convert.ToDecimal(row["入库金额"]);
                if (row["包装金额"] != DBNull.Value) packagingAmount += Convert.ToDecimal(row["包装金额"]);
            }
            dt.Rows.Add("业务-销售金额", salesAmount);
            dt.Rows.Add("业务-入库金额", inboundAmount);
            dt.Rows.Add("业务-包装金额", packagingAmount);

            return dt;
        }

        public void ExecuteYearEndSettlement(
            int fiscalYear,
            string archivePath,
            string inventoryCarryover,
            string settledBy,
            out string errorMessage)
        {
            errorMessage = null;
            int newYear = fiscalYear + 1;
            string effectiveDate = $"{newYear}-01-01";
            var openingBalances = CalculateAllClientOpeningBalances();
            var inventoryRows = new List<InventoryCarryoverRow>();
            DataTable inventory = GetInventoryByLocation();
            foreach (DataRow row in inventory.Rows)
            {
                int stock = Convert.ToInt32(row["当前库存"]);
                if (stock <= 0)
                    continue;
                inventoryRows.Add(new InventoryCarryoverRow
                {
                    Location = row["库位"]?.ToString() ?? "",
                    Spec = row["商品型号"]?.ToString() ?? "",
                    Quantity = stock
                });
            }

            bool wasSuppressed = SuppressLocalConfigSyncNotifications;
            SuppressLocalConfigSyncNotifications = true;

            try
            {
                using (var conn = new SQLiteConnection(connectionString))
                {
                    conn.Open();
                    using (var tx = conn.BeginTransaction())
                    {
                        try
                        {
                            using (var delOpening = new SQLiteCommand("DELETE FROM client_opening_balance", conn, tx))
                                delOpening.ExecuteNonQuery();

                            foreach (var item in openingBalances)
                            {
                                using (var cmd = new SQLiteCommand(@"
INSERT INTO client_opening_balance
(client_code, client_name, payable_amount, source_year, effective_date, remark)
VALUES (@code, @name, @amount, @sourceYear, @effDate, @remark)", conn, tx))
                                {
                                    cmd.Parameters.AddWithValue("@code", item.ClientCode);
                                    cmd.Parameters.AddWithValue("@name", item.ClientName);
                                    cmd.Parameters.AddWithValue("@amount", item.PayableAmount);
                                    cmd.Parameters.AddWithValue("@sourceYear", fiscalYear);
                                    cmd.Parameters.AddWithValue("@effDate", effectiveDate);
                                    cmd.Parameters.AddWithValue("@remark", $"自{fiscalYear}年度结转");
                                    cmd.ExecuteNonQuery();
                                }
                            }

                            int openingInventoryRows = 0;
                            if (inventoryCarryover == InventoryCarryoverOpeningInbound && inventoryRows.Count > 0)
                            {
                                openingInventoryRows = InsertOpeningInboundRecords(conn, tx, fiscalYear, newYear, inventoryRows);
                            }

                            ClearBusinessData(conn, tx);

                            using (var cmd = new SQLiteCommand(@"
INSERT INTO year_end_settlement
(fiscal_year, archive_path, settled_by, inventory_carryover, opening_balance_total, opening_inventory_rows, remark)
VALUES (@year, @path, @by, @carry, @balanceTotal, @invRows, @remark)", conn, tx))
                            {
                                cmd.Parameters.AddWithValue("@year", fiscalYear);
                                cmd.Parameters.AddWithValue("@path", archivePath ?? "");
                                cmd.Parameters.AddWithValue("@by", settledBy ?? "");
                                cmd.Parameters.AddWithValue("@carry", inventoryCarryover ?? InventoryCarryoverEmpty);
                                cmd.Parameters.AddWithValue("@balanceTotal", openingBalances.Sum(x => x.PayableAmount));
                                cmd.Parameters.AddWithValue("@invRows", openingInventoryRows);
                                cmd.Parameters.AddWithValue("@remark", $"开启{newYear}年度账本");
                                cmd.ExecuteNonQuery();
                            }

                            tx.Commit();
                        }
                        catch (Exception ex)
                        {
                            tx.Rollback();
                            errorMessage = ex.Message;
                        }
                    }
                }
            }
            finally
            {
                SuppressLocalConfigSyncNotifications = wasSuppressed;
            }
        }

        private static int InsertOpeningInboundRecords(
            SQLiteConnection conn,
            SQLiteTransaction tx,
            int sourceYear,
            int newYear,
            List<InventoryCarryoverRow> rows)
        {
            int seq = 1;
            foreach (var row in rows)
            {
                string orderNo = $"QC{newYear}{seq:D4}";
                using (var cmd = new SQLiteCommand(@"
INSERT INTO inbound_transactions
(order_no, spec, client_code, client_name, location, date, quantity, unit_price, total_amount, handler, creator)
VALUES (@orderNo, @spec, '', '', @location, @date, @qty, 0, 0, '系统年结', '系统年结')", conn, tx))
                {
                    cmd.Parameters.AddWithValue("@orderNo", orderNo);
                    cmd.Parameters.AddWithValue("@spec", row.Spec);
                    cmd.Parameters.AddWithValue("@location", row.Location);
                    cmd.Parameters.AddWithValue("@date", $"{newYear}-01-01");
                    cmd.Parameters.AddWithValue("@qty", row.Quantity);
                    cmd.ExecuteNonQuery();
                }
                seq++;
            }
            return rows.Count;
        }

        private static void ClearBusinessData(SQLiteConnection conn, SQLiteTransaction tx)
        {
            string[] tables =
            {
                "presale_payments", "presale_items", "presale_bills",
                "inbound_items", "inbound_transactions",
                "sales_items", "sales_transactions",
                "packaging_items", "packaging_transactions",
                "deductions", "advances", "ledger_entry"
            };
            foreach (string table in tables)
            {
                using (var cmd = new SQLiteCommand($"DELETE FROM {table}", conn, tx))
                    cmd.ExecuteNonQuery();
            }
        }

        public DataTable QueryArchiveSummaryByDate(string readOnlyConnStr, DateTime startDate, DateTime endDate, string periodType)
        {
            var dt = new DataTable();
            try
            {
                using (var connection = new SQLiteConnection(readOnlyConnStr))
                {
                    connection.Open();
                    string dateGroup = "date";
                    switch (periodType)
                    {
                        case "月": dateGroup = "strftime('%Y-%m', date)"; break;
                        case "年": dateGroup = "strftime('%Y', date)"; break;
                    }

                    string query = $@"
SELECT {dateGroup} as 期间,
    COUNT(*) as 业务笔数,
    SUM(CASE WHEN type = '销售' THEN total_amount ELSE 0 END) as 销售金额,
    SUM(CASE WHEN type = '入库' THEN total_amount ELSE 0 END) as 入库金额
FROM (
    SELECT '销售' as type, date, total_amount FROM sales_transactions
    WHERE date BETWEEN @startDate AND @endDate
    UNION ALL
    SELECT '入库' as type, date, total_amount FROM inbound_transactions
    WHERE date BETWEEN @startDate AND @endDate
) GROUP BY {dateGroup} ORDER BY {dateGroup}";

                    using (var command = new SQLiteCommand(query, connection))
                    {
                        command.Parameters.AddWithValue("@startDate", startDate.ToString("yyyy-MM-dd"));
                        command.Parameters.AddWithValue("@endDate", endDate.ToString("yyyy-MM-dd"));
                        using (var adapter = new SQLiteDataAdapter(command))
                            adapter.Fill(dt);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($">>> QueryArchiveSummaryByDate 失败: {ex.Message}");
            }
            return dt;
        }

        public DataTable GetClientTransactionDetailsFromConnection(
            string connStr,
            string clientCode,
            DateTime startDate,
            DateTime endDate)
        {
            DataTable result = CreateClientTransactionDetailsTable();
            try
            {
                using (var conn = new SQLiteConnection(connStr))
                {
                    conn.Open();
                    FillClientTransactionDetailsRows(result, conn, clientCode, startDate, endDate);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($">>> GetClientTransactionDetailsFromConnection 失败: {ex.Message}");
                throw;
            }
            return result;
        }

        public DataTable GetClientInboundStatisticsByLocationFromConnection(
            string connStr,
            string clientCode,
            DateTime startDate,
            DateTime endDate)
        {
            var dt = new DataTable();
            try
            {
                using (var conn = new SQLiteConnection(connStr))
                {
                    conn.Open();
                    const string sql = @"
SELECT 
    COALESCE(location, '未指定库位') AS 库位,
    spec AS 型号,
    SUM(quantity) AS 入库数量,
    COUNT(DISTINCT order_no) AS 入库单数
FROM inbound_transactions 
WHERE client_code = @clientCode 
  AND date BETWEEN @startDate AND @endDate 
GROUP BY location, spec 
ORDER BY location, spec";
                    using (var cmd = new SQLiteCommand(sql, conn))
                    {
                        cmd.Parameters.AddWithValue("@clientCode", clientCode);
                        cmd.Parameters.AddWithValue("@startDate", startDate.ToString("yyyy-MM-dd"));
                        cmd.Parameters.AddWithValue("@endDate", endDate.ToString("yyyy-MM-dd"));
                        using (var adapter = new SQLiteDataAdapter(cmd))
                            adapter.Fill(dt);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($">>> GetClientInboundStatisticsByLocationFromConnection 失败: {ex.Message}");
            }
            return dt;
        }

        public DataTable GetClientInboundTotalFromConnection(
            string connStr,
            string clientCode,
            DateTime startDate,
            DateTime endDate)
        {
            var dt = new DataTable();
            try
            {
                using (var conn = new SQLiteConnection(connStr))
                {
                    conn.Open();
                    const string sql = @"
SELECT 
    COUNT(DISTINCT order_no) AS 总单数,
    SUM(quantity) AS 总数量,
    COUNT(DISTINCT spec) AS 型号种数
FROM inbound_transactions 
WHERE client_code = @clientCode 
  AND date BETWEEN @startDate AND @endDate";
                    using (var cmd = new SQLiteCommand(sql, conn))
                    {
                        cmd.Parameters.AddWithValue("@clientCode", clientCode);
                        cmd.Parameters.AddWithValue("@startDate", startDate.ToString("yyyy-MM-dd"));
                        cmd.Parameters.AddWithValue("@endDate", endDate.ToString("yyyy-MM-dd"));
                        using (var adapter = new SQLiteDataAdapter(cmd))
                            adapter.Fill(dt);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($">>> GetClientInboundTotalFromConnection 失败: {ex.Message}");
            }
            return dt;
        }

        public class ClientOpeningBalanceItem
        {
            public string ClientCode { get; set; }
            public string ClientName { get; set; }
            public decimal PayableAmount { get; set; }
        }

        public class InventoryCarryoverRow
        {
            public string Location { get; set; }
            public string Spec { get; set; }
            public int Quantity { get; set; }
        }

        public class YearEndPreviewData
        {
            public int FiscalYear { get; set; }
            public bool AlreadySettled { get; set; }
            public int InboundCount { get; set; }
            public int SalesCount { get; set; }
            public int PackagingCount { get; set; }
            public int LedgerCount { get; set; }
            public decimal LedgerIncome { get; set; }
            public decimal LedgerExpense { get; set; }
            public decimal LedgerProfit { get; set; }
            public decimal OpeningBalanceTotal { get; set; }
            public List<ClientOpeningBalanceItem> OpeningBalances { get; set; } = new List<ClientOpeningBalanceItem>();
            public List<InventoryCarryoverRow> InventoryRows { get; set; } = new List<InventoryCarryoverRow>();
        }

        #endregion

    } // DatabaseManager类结束

} // 命名空间结束
#endregion