// DatabaseHelper.cs - 创建新文件
using System;
using System.Data;
using System.Collections.Generic;

namespace lengkubao.desktop
{
    public static class DatabaseHelper
    {
        private static DatabaseManager db = new DatabaseManager();

        public static void Reset()
        {
            db = new DatabaseManager();
        }

        // 获取所有表名
        public static List<string> GetTableNames()
        {
            try
            {
                string sql = "SELECT name FROM sqlite_master WHERE type='table' ORDER BY name";
                DataTable tables = db.ExecuteQuery(sql);

                List<string> tableNames = new List<string>();
                foreach (DataRow row in tables.Rows)
                {
                    tableNames.Add(row["name"].ToString());
                }
                return tableNames;
            }
            catch (Exception)
            {
                return new List<string>();
            }
        }

        // 获取表结构
        public static DataTable GetTableSchema(string tableName)
        {
            try
            {
                string sql = $"PRAGMA table_info({tableName})";
                return db.ExecuteQuery(sql);
            }
            catch (Exception)
            {
                return new DataTable();
            }
        }

        // 查找特定类型的表
        public static string FindTableByName(string keyword)
        {
            try
            {
                List<string> tables = GetTableNames();
                foreach (string table in tables)
                {
                    if (table.ToLower().Contains(keyword.ToLower()))
                    {
                        return table;
                    }
                }
                return "";
            }
            catch (Exception)
            {
                return "";
            }
        }

        // 获取表数据
        public static DataTable GetTableData(string tableName, int limit = 200)
        {
            try
            {
                string sql = $"SELECT * FROM {tableName} ORDER BY id DESC LIMIT {limit}";
                return db.ExecuteQuery(sql);
            }
            catch (Exception)
            {
                return new DataTable();
            }
        }

        // 中文字段名映射
        public static Dictionary<string, string> GetChineseColumnNames(string tableName)
        {
            var mappings = new Dictionary<string, string>();

            // 根据表名返回对应的中文映射
            if (tableName.ToLower().Contains("inbound") || tableName.ToLower().Contains("入库"))
            {
                mappings = new Dictionary<string, string>
                {
                    { "id", "ID" },
                    { "order_no", "单据号" },
                    { "transaction_date", "日期" },
                    { "handler", "经手人" },
                    { "supplier", "供应商" },
                    { "product_type", "商品型号" },
                    { "quantity", "数量" },
                    { "unit_price", "单价" },
                    { "amount", "金额" },
                    { "location", "库位" },
                    { "notes", "备注" },
                    { "created_time", "创建时间" }
                };
            }
            else if (tableName.ToLower().Contains("sales") || tableName.ToLower().Contains("销售"))
            {
                mappings = new Dictionary<string, string>
                {
                    { "id", "ID" },
                    { "sales_no", "销售单号" },
                    { "sales_date", "销售日期" },
                    { "customer_name", "客户名称" },
                    { "customer_code", "客户编号" },
                    { "handler", "经手人" },
                    { "product_type", "商品型号" },
                    { "quantity", "数量" },
                    { "unit_price", "单价" },
                    { "amount", "金额" },
                    { "location", "库位" },
                    { "delivery_status", "发货状态" },
                    { "payment_status", "付款状态" },
                    { "notes", "备注" },
                    { "created_time", "创建时间" },
                    { "updated_time", "更新时间" }
                };
            }
            else if (tableName.ToLower().Contains("packaging") || tableName.ToLower().Contains("包装"))
            {
                mappings = new Dictionary<string, string>
                {
                    { "id", "ID" },
                    { "packaging_no", "包装单号" },
                    { "packaging_date", "包装日期" },
                    { "client_name", "客户名称" },
                    { "client_code", "客户编号" },
                    { "handler", "经手人" },
                    { "product_type", "商品型号" },
                    { "quantity", "数量" },
                    { "package_type", "包装方式" },
                    { "material_cost", "材料成本" },
                    { "labor_cost", "人工成本" },
                    { "total_cost", "总成本" },
                    { "location", "库位" },
                    { "status", "状态" },
                    { "notes", "备注" },
                    { "created_time", "创建时间" },
                    { "updated_time", "更新时间" }
                };
            }

            return mappings;
        }
    }
}