using Newtonsoft.Json.Linq;
using System;
using System.Data.SQLite;
using System.IO;

namespace lengkubao.desktop.Sync
{
    /// <summary>CR-SQLite 扩展加载与 CRR 表管理。扩展不可用时 IsAvailable=false，同步层走 Outbox fallback。</summary>
    public sealed class CrsqlEngine : IDisposable
    {
        private readonly Action<string> _log;
        private bool _initialized;

        public CrsqlEngine(Action<string> log = null)
        {
            _log = log ?? (_ => { });
        }

        public bool IsAvailable { get; private set; }

        public static readonly string[] ConfigTables =
        {
            "clients", "locations", "handlers", "products", "pack_types"
        };

        public void TryInitialize(SQLiteConnection connection)
        {
            if (_initialized || connection == null) return;
            _initialized = true;

            try
            {
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                string extPath = Path.Combine(baseDir, "native", "crsqlite");
                if (!File.Exists(extPath))
                    extPath = Path.Combine(baseDir, "crsqlite.dll");

                if (!File.Exists(extPath))
                {
                    _log("ℹ️ cr-sqlite 扩展未找到，配置同步使用 Outbox fallback");
                    IsAvailable = false;
                    return;
                }

                connection.EnableExtensions(true);
                connection.LoadExtension(extPath, "sqlite3_crsqlite_init");
                ExecuteNonQuery(connection, "SELECT crsql_site_id()");

                foreach (var table in ConfigTables)
                {
                    if (TableExists(connection, table))
                        ExecuteNonQuery(connection, $"SELECT crsql_as_crr('{table}')");
                }

                IsAvailable = true;
                _log("✅ cr-sqlite 已加载，配置表已标记 CRR");
            }
            catch (Exception ex)
            {
                IsAvailable = false;
                _log($"ℹ️ cr-sqlite 不可用，使用 Outbox fallback: {ex.Message}");
            }
        }

        public string PullChangesJson(SQLiteConnection connection, long sinceDbVersion, string peerSiteId)
        {
            if (!IsAvailable || connection == null) return null;

            var rows = new JArray();
            using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = @"
SELECT ""table"", ""pk"", ""cid"", ""val"", ""col_version"", ""db_version"", ""site_id"", ""cl"", ""seq""
FROM crsql_changes
WHERE db_version > @since";
                cmd.Parameters.AddWithValue("@since", sinceDbVersion);
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        rows.Add(new JObject
                        {
                            ["table"] = reader.GetString(0),
                            ["pk"] = Convert.ToBase64String((byte[])reader[1]),
                            ["cid"] = reader.IsDBNull(2) ? null : reader.GetString(2),
                            ["val"] = reader.IsDBNull(3) ? null : reader.GetValue(3)?.ToString(),
                            ["col_version"] = reader.GetInt64(4),
                            ["db_version"] = reader.GetInt64(5),
                            ["site_id"] = Convert.ToBase64String((byte[])reader[6]),
                            ["cl"] = reader.GetInt64(7),
                            ["seq"] = reader.GetInt64(8)
                        });
                    }
                }
            }

            return rows.ToString();
        }

        public void ApplyChangesJson(SQLiteConnection connection, string changesJson)
        {
            if (!IsAvailable || connection == null || string.IsNullOrWhiteSpace(changesJson)) return;

            var arr = JArray.Parse(changesJson);
            using (var tx = connection.BeginTransaction())
            {
                foreach (JObject item in arr)
                {
                    using (var cmd = connection.CreateCommand())
                    {
                        cmd.Transaction = tx;
                        cmd.CommandText = @"
INSERT INTO crsql_changes (""table"", ""pk"", ""cid"", ""val"", ""col_version"", ""db_version"", ""site_id"", ""cl"", ""seq"")
VALUES (@table, @pk, @cid, @val, @col_version, @db_version, @site_id, @cl, @seq)";
                        cmd.Parameters.AddWithValue("@table", (string)item["table"]);
                        cmd.Parameters.AddWithValue("@pk", Convert.FromBase64String((string)item["pk"]));
                        cmd.Parameters.AddWithValue("@cid", (string)item["cid"] ?? (object)DBNull.Value);
                        cmd.Parameters.AddWithValue("@val", item["val"]?.ToString() ?? (object)DBNull.Value);
                        cmd.Parameters.AddWithValue("@col_version", (long)item["col_version"]);
                        cmd.Parameters.AddWithValue("@db_version", (long)item["db_version"]);
                        cmd.Parameters.AddWithValue("@site_id", Convert.FromBase64String((string)item["site_id"]));
                        cmd.Parameters.AddWithValue("@cl", (long)item["cl"]);
                        cmd.Parameters.AddWithValue("@seq", (long)item["seq"]);
                        cmd.ExecuteNonQuery();
                    }
                }
                tx.Commit();
            }
        }

        private static bool TableExists(SQLiteConnection connection, string table)
        {
            using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = "SELECT 1 FROM sqlite_master WHERE type='table' AND name=@n LIMIT 1";
                cmd.Parameters.AddWithValue("@n", table);
                return cmd.ExecuteScalar() != null;
            }
        }

        private static void ExecuteNonQuery(SQLiteConnection connection, string sql)
        {
            using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = sql;
                cmd.ExecuteNonQuery();
            }
        }

        public void Dispose()
        {
            _initialized = false;
            IsAvailable = false;
        }
    }
}
