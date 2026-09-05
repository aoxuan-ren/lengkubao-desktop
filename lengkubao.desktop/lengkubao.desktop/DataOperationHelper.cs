using System;
using System.Data;
using System.Collections.Generic;
using System.Linq;

namespace lengkubao.desktop
{
    public class DataOperationHelper
    {
        private DatabaseManager db;

        public DataOperationHelper()
        {
            db = new DatabaseManager();
        }

        // 保存入库记录（多型号支持）- 修复版本
        public bool SaveInboundRecordWithItems(string orderNo, string clientCode, string clientName,
    string location, DateTime date, List<InboundItem> items, string handler, string creator)
        {
            // 参数验证
            if (string.IsNullOrEmpty(orderNo))
            {
                Console.WriteLine("❌ 订单号不能为空");
                return false;
            }
            if (items == null || items.Count == 0)
            {
                Console.WriteLine("❌ 入库明细不能为空");
                return false;
            }

            DatabaseManager db = new DatabaseManager();
            bool transactionActive = false;

            try
            {
                // 开始事务
                db.ExecuteNonQuery("BEGIN TRANSACTION");
                transactionActive = true;
                Console.WriteLine($"🔄 开始事务: {orderNo}");

                // 1. 插入主表记录
                string mainSql = @"
            INSERT INTO inbound_transactions 
            (order_no, spec, client_code, client_name, location, date, 
             quantity, unit_price, total_amount, handler, creator, created_time, updated_at) 
            VALUES 
            (@order_no, @spec, @client_code, @client_name, @location, @date,
             @quantity, @unit_price, @total_amount, @handler, @creator, 
             datetime('now', 'localtime'), datetime('now', 'localtime'));
            SELECT last_insert_rowid();";

                // 使用第一条明细作为主表的默认值
                var firstItem = items[0];

                var mainParams = new Dictionary<string, object>
        {
            { "@order_no", orderNo },
            { "@spec", firstItem.Spec ?? "" },
            { "@client_code", clientCode ?? "" },
            { "@client_name", clientName ?? "" },
            { "@location", location ?? "" },
            { "@date", date.ToString("yyyy-MM-dd") },
            { "@quantity", firstItem.Quantity },
            { "@unit_price", firstItem.UnitPrice },
            { "@total_amount", firstItem.TotalAmount },
            { "@handler", handler ?? "" },
            { "@creator", creator ?? "" }
        };

                object result = db.ExecuteScalar(mainSql, mainParams);
                int inboundId = result != null ? Convert.ToInt32(result) : 0;

                if (inboundId == 0)
                {
                    throw new Exception("获取入库主表ID失败");
                }

                Console.WriteLine($"✅ 主表插入成功，ID: {inboundId}");

                // 2. 插入所有明细
                foreach (var item in items)
                {
                    string itemSql = @"
                INSERT INTO inbound_items 
                (inbound_id, order_no, spec, quantity, unit_price, total_amount, created_time) 
                VALUES 
                (@inbound_id, @order_no, @spec, @quantity, @unit_price, @total_amount, datetime('now', 'localtime'))";

                    var itemParams = new Dictionary<string, object>
            {
                { "@inbound_id", inboundId },
                { "@order_no", orderNo },
                { "@spec", item.Spec ?? "" },
                { "@quantity", item.Quantity },
                { "@unit_price", item.UnitPrice },
                { "@total_amount", item.TotalAmount }
            };

                    db.ExecuteNonQuery(itemSql, itemParams);
                    Console.WriteLine($"  - 明细插入: {item.Spec} x {item.Quantity}");
                }

                // 3. 提交事务
                db.ExecuteNonQuery("COMMIT");
                transactionActive = false;
                Console.WriteLine($"✅ 事务提交成功: {orderNo}，共{items.Count}个规格");

                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ 保存失败: {ex.Message}");

                // 只有事务还在活动时才回滚
                if (transactionActive)
                {
                    try
                    {
                        db.ExecuteNonQuery("ROLLBACK");
                        Console.WriteLine($"↩️ 事务已回滚");
                    }
                    catch (Exception rollbackEx)
                    {
                        Console.WriteLine($"⚠️ 回滚失败: {rollbackEx.Message}");
                    }
                }

                return false;
            }
        }

        // 辅助方法：检查入库单是否存在
        private bool CheckInboundOrderExists(DatabaseManager db, string orderNo)
        {
            string sql = "SELECT COUNT(*) FROM inbound_transactions WHERE order_no = @order_no";
            var count = Convert.ToInt32(db.ExecuteScalar(sql, new Dictionary<string, object>
    {
        { "@order_no", orderNo }
    }));
            return count > 0;
        }

        // 辅助方法：获取入库单ID
        private int GetInboundId(DatabaseManager db, string orderNo)
        {
            string sql = "SELECT id FROM inbound_transactions WHERE order_no = @order_no LIMIT 1";
            var result = db.ExecuteScalar(sql, new Dictionary<string, object>
    {
        { "@order_no", orderNo }
    });
            return result != null ? Convert.ToInt32(result) : 0;
        }

        // 辅助方法：检查明细是否存在
        private bool CheckInboundItemExists(DatabaseManager db, int inboundId, string spec)
        {
            string sql = "SELECT COUNT(*) FROM inbound_items WHERE inbound_id = @inbound_id AND spec = @spec";
            var count = Convert.ToInt32(db.ExecuteScalar(sql, new Dictionary<string, object>
    {
        { "@inbound_id", inboundId },
        { "@spec", spec ?? "" }
    }));
            return count > 0;
        }

        // 辅助方法：更新主表汇总数据
        private void UpdateInboundSummary(DatabaseManager db, int inboundId)
        {
            // 计算总数量和总金额
            string summarySql = @"
        SELECT 
            SUM(quantity) as total_qty,
            SUM(total_amount) as total_amt
        FROM inbound_items 
        WHERE inbound_id = @inbound_id";

            DataTable dt = db.ExecuteQuery(summarySql, new Dictionary<string, object>
    {
        { "@inbound_id", inboundId }
    });

            if (dt.Rows.Count > 0)
            {
                int totalQty = dt.Rows[0]["total_qty"] != DBNull.Value ?
                    Convert.ToInt32(dt.Rows[0]["total_qty"]) : 0;
                double totalAmt = dt.Rows[0]["total_amt"] != DBNull.Value ?
                    Convert.ToDouble(dt.Rows[0]["total_amt"]) : 0;

                // 更新主表（这里可以根据需要决定是否更新）
                string updateSql = @"
            UPDATE inbound_transactions 
            SET quantity = @total_qty,
                total_amount = @total_amt,
                updated_at = datetime('now', 'localtime')
            WHERE id = @inbound_id";

                db.ExecuteNonQuery(updateSql, new Dictionary<string, object>
        {
            { "@inbound_id", inboundId },
            { "@total_qty", totalQty },
            { "@total_amt", totalAmt }
        });
            }
        }

        // 保存销售记录（多型号支持）- 修复版本
        public bool SaveSalesRecordWithItems(string orderNo, string clientCode, string clientName,
            DateTime date, List<SalesItem> items, string handler, string creator)
        {
            try
            {
                // 计算总数
                int totalQuantity = items.Sum(i => i.Quantity);
                double totalAmount = items.Sum(i => i.TotalAmount);

                // 开始事务
                db.ExecuteNonQuery("BEGIN TRANSACTION");

                try
                {
                    // ✅ 修复：使用正确的表结构
                    string mainSql = @"
                        INSERT OR REPLACE INTO sales_transactions 
                        (order_no, spec, client_code, client_name, date, quantity, unit_price, total_amount, handler, creator, created_time, source_device_id) 
                        VALUES (@order_no, @spec, @client_code, @client_name, @date, @quantity, @unit_price, @total_amount, @handler, @creator, datetime('now', 'localtime'), @source_device_id)";

                    var firstItem = items.FirstOrDefault();
                    if (firstItem == null)
                        throw new Exception("销售明细不能为空");

                    db.ExecuteNonQuery(mainSql, new Dictionary<string, object>
                    {
                        { "@order_no", orderNo },
                        { "@spec", firstItem.Spec },
                        { "@client_code", clientCode },
                        { "@client_name", clientName },
                        { "@date", date.ToString("yyyy-MM-dd") },
                        { "@quantity", firstItem.Quantity },
                        { "@unit_price", firstItem.UnitPrice },
                        { "@total_amount", firstItem.TotalAmount },
                        { "@handler", handler },
                        { "@creator", creator },
                        { "@source_device_id", (object)DBNull.Value }
                    });

                    // 2. 获取主表ID（本机录入无来源设备，与唯一键 COALESCE(source_device_id,'') 一致）
                    string getIdSql = @"SELECT id FROM sales_transactions 
                        WHERE order_no = @order_no AND spec = @spec AND COALESCE(source_device_id,'') = ''";
                    DataTable result = db.ExecuteQuery(getIdSql, new Dictionary<string, object>
                    {
                        { "@order_no", orderNo },
                        { "@spec", firstItem.Spec }
                    });

                    int salesId = result.Rows.Count > 0 ? Convert.ToInt32(result.Rows[0]["id"]) : 0;

                    if (salesId == 0)
                        throw new Exception("获取销售主表ID失败");

                    // 3. 删除旧的明细数据
                    string deleteSql = "DELETE FROM sales_items WHERE order_no = @order_no";
                    db.ExecuteNonQuery(deleteSql, new Dictionary<string, object>
                    {
                        { "@order_no", orderNo }
                    });

                    // 4. 插入新的明细数据
                    foreach (var item in items)
                    {
                        string detailSql = @"
                            INSERT INTO sales_items 
                            (sales_id, order_no, spec, quantity, unit_price, total_amount, created_time) 
                            VALUES (@sales_id, @order_no, @spec, @quantity, @unit_price, @total_amount, datetime('now', 'localtime'))";

                        db.ExecuteNonQuery(detailSql, new Dictionary<string, object>
                        {
                            { "@sales_id", salesId },
                            { "@order_no", orderNo },
                            { "@spec", item.Spec },
                            { "@quantity", item.Quantity },
                            { "@unit_price", item.UnitPrice },
                            { "@total_amount", item.TotalAmount }
                        });
                    }

                    // 5. 提交事务
                    db.ExecuteNonQuery("COMMIT");

                    return true;
                }
                catch (Exception ex)
                {
                    try
                    {
                        db.ExecuteNonQuery("ROLLBACK");
                    }
                    catch { }
                    throw new Exception($"保存销售记录失败: {ex.Message}");
                }
            }
            catch (Exception ex)
            {
                throw new Exception($"保存销售记录失败: {ex.Message}");
            }
        }

        // 保存包装记录（多型号支持）- 修复版本
        public bool SavePackagingRecordWithItems(string orderNo, string clientCode, string clientName,
            List<PackagingItem> items, string handler, string creator)
        {
            try
            {
                // 计算总数
                int totalQuantity = items.Sum(i => i.Quantity);
                double totalAmount = items.Sum(i => i.TotalAmount);

                // 开始事务
                db.ExecuteNonQuery("BEGIN TRANSACTION");

                try
                {
                    // ✅ 修复：使用正确的表结构
                    string mainSql = @"
                        INSERT OR REPLACE INTO packaging_transactions 
                        (order_no, pack_type, pack_flag, client_code, client_name, quantity, unit_price, total_amount, handler, creator, date, created_time, source_device_id) 
                        VALUES (@order_no, @pack_type, @pack_flag, @client_code, @client_name, @quantity, @unit_price, @total_amount, @handler, @creator, @date, datetime('now', 'localtime'), @source_device_id)";

                    var firstItem = items.FirstOrDefault();
                    if (firstItem == null)
                        throw new Exception("包装明细不能为空");

                    db.ExecuteNonQuery(mainSql, new Dictionary<string, object>
                    {
                        { "@order_no", orderNo },
                        { "@pack_type", firstItem.PackType },
                        { "@client_code", clientCode },
                        { "@client_name", clientName },
                        { "@quantity", firstItem.Quantity },
                        { "@unit_price", firstItem.UnitPrice },
                        { "@total_amount", firstItem.TotalAmount },
                        { "@handler", handler },
                        { "@creator", creator },
                        { "@date", DateTime.Now.ToString("yyyy-MM-dd") },
                        { "@pack_flag", "TAKE" },
                        { "@source_device_id", (object)DBNull.Value }
                    });

                    // 2. 获取主表ID（本机录入：TAKE、无来源设备）
                    string getIdSql = @"SELECT id FROM packaging_transactions 
                        WHERE order_no = @order_no AND pack_type = @pack_type 
                          AND COALESCE(pack_flag,'TAKE') = 'TAKE' AND COALESCE(source_device_id,'') = ''";
                    DataTable result = db.ExecuteQuery(getIdSql, new Dictionary<string, object>
                    {
                        { "@order_no", orderNo },
                        { "@pack_type", firstItem.PackType }
                    });

                    int packagingId = result.Rows.Count > 0 ? Convert.ToInt32(result.Rows[0]["id"]) : 0;

                    if (packagingId == 0)
                        throw new Exception("获取包装主表ID失败");

                    // 3. 删除旧的明细数据
                    string deleteSql = "DELETE FROM packaging_items WHERE order_no = @order_no";
                    db.ExecuteNonQuery(deleteSql, new Dictionary<string, object>
                    {
                        { "@order_no", orderNo }
                    });

                    // 4. 插入新的明细数据
                    foreach (var item in items)
                    {
                        string detailSql = @"
                            INSERT INTO packaging_items 
                            (packaging_id, order_no, pack_type, quantity, unit_price, total_amount, created_time) 
                            VALUES (@packaging_id, @order_no, @pack_type, @quantity, @unit_price, @total_amount, datetime('now', 'localtime'))";

                        db.ExecuteNonQuery(detailSql, new Dictionary<string, object>
                        {
                            { "@packaging_id", packagingId },
                            { "@order_no", orderNo },
                            { "@pack_type", item.PackType },
                            { "@quantity", item.Quantity },
                            { "@unit_price", item.UnitPrice },
                            { "@total_amount", item.TotalAmount }
                        });
                    }

                    // 5. 提交事务
                    db.ExecuteNonQuery("COMMIT");

                    return true;
                }
                catch (Exception ex)
                {
                    try
                    {
                        db.ExecuteNonQuery("ROLLBACK");
                    }
                    catch { }
                    throw new Exception($"保存包装记录失败: {ex.Message}");
                }
            }
            catch (Exception ex)
            {
                throw new Exception($"保存包装记录失败: {ex.Message}");
            }
        }

        // 查询方法保持不变...
        public DataTable GetInboundRecordsWithDetails(DateTime? startDate = null, DateTime? endDate = null,
            string clientCode = "", string spec = "")
        {
            try
            {
                string sql = @"
                    SELECT 
                        it.id,
                        it.order_no,
                        it.client_code,
                        it.client_name,
                        it.location,
                        it.date,
                        it.spec,
                        it.quantity,
                        it.unit_price,
                        it.total_amount,
                        it.handler,
                        it.creator,
                        it.created_time,
                        (SELECT GROUP_CONCAT(ii.spec || '×' || ii.quantity, '、 ') 
                         FROM inbound_items ii 
                         WHERE ii.order_no = it.order_no) as specs_summary
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
                    sql += " AND (it.spec LIKE @spec OR EXISTS (SELECT 1 FROM inbound_items ii WHERE ii.order_no = it.order_no AND ii.spec LIKE @spec))";
                    parameters.Add("@spec", $"%{spec}%");
                }

                sql += " ORDER BY it.date DESC, it.id DESC";

                return db.ExecuteQuery(sql, parameters);
            }
            catch (Exception ex)
            {
                throw new Exception($"获取入库记录（带明细）失败: {ex.Message}");
            }
        }

        // 其他查询方法保持不变...
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
                        st.spec,
                        st.quantity,
                        st.unit_price,
                        st.total_amount,
                        st.handler,
                        st.creator,
                        st.created_time,
                        (SELECT GROUP_CONCAT(si.spec || '×' || si.quantity, '、 ') 
                         FROM sales_items si 
                         WHERE si.order_no = st.order_no) as specs_summary
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
                    sql += " AND (st.spec LIKE @spec OR EXISTS (SELECT 1 FROM sales_items si WHERE si.order_no = st.order_no AND si.spec LIKE @spec))";
                    parameters.Add("@spec", $"%{spec}%");
                }

                sql += " ORDER BY st.date DESC, st.id DESC";

                return db.ExecuteQuery(sql, parameters);
            }
            catch (Exception ex)
            {
                throw new Exception($"获取销售记录（带明细）失败: {ex.Message}");
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
                        pt.pack_type,
                        pt.quantity,
                        pt.unit_price,
                        pt.total_amount,
                        pt.handler,
                        pt.creator,
                        DATE(pt.created_time) as date,
                        pt.created_time,
                        (SELECT GROUP_CONCAT(pi.pack_type || '×' || pi.quantity, '、 ') 
                         FROM packaging_items pi 
                         WHERE pi.order_no = pt.order_no) as pack_types_summary
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
                    sql += " AND (pt.pack_type LIKE @packType OR EXISTS (SELECT 1 FROM packaging_items pi WHERE pi.order_no = pt.order_no AND pi.pack_type LIKE @packType))";
                    parameters.Add("@packType", $"%{packType}%");
                }

                sql += " ORDER BY pt.created_time DESC";

                return db.ExecuteQuery(sql, parameters);
            }
            catch (Exception ex)
            {
                throw new Exception($"获取包装记录（带明细）失败: {ex.Message}");
            }
        }

        // 获取明细的方法保持不变...
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

                return db.ExecuteQuery(sql, new Dictionary<string, object>
                {
                    { "@order_no", orderNo }
                });
            }
            catch (Exception ex)
            {
                throw new Exception($"获取入库明细失败: {ex.Message}");
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

                return db.ExecuteQuery(sql, new Dictionary<string, object>
                {
                    { "@order_no", orderNo }
                });
            }
            catch (Exception ex)
            {
                throw new Exception($"获取销售明细失败: {ex.Message}");
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

                return db.ExecuteQuery(sql, new Dictionary<string, object>
                {
                    { "@order_no", orderNo }
                });
            }
            catch (Exception ex)
            {
                throw new Exception($"获取包装明细失败: {ex.Message}");
            }
        }

        // 删除记录的方法保持不变...
        public bool DeleteInboundRecord(string orderNo)
        {
            try
            {
                db.ExecuteNonQuery("BEGIN TRANSACTION");

                try
                {
                    string deleteDetailsSql = "DELETE FROM inbound_items WHERE order_no = @order_no";
                    db.ExecuteNonQuery(deleteDetailsSql, new Dictionary<string, object> { { "@order_no", orderNo } });

                    string deleteMainSql = "DELETE FROM inbound_transactions WHERE order_no = @order_no";
                    int rowsAffected = db.ExecuteNonQuery(deleteMainSql, new Dictionary<string, object> { { "@order_no", orderNo } });

                    db.ExecuteNonQuery("COMMIT");

                    return rowsAffected > 0;
                }
                catch (Exception ex)
                {
                    try { db.ExecuteNonQuery("ROLLBACK"); } catch { }
                    throw new Exception($"删除入库记录失败: {ex.Message}");
                }
            }
            catch (Exception ex)
            {
                throw new Exception($"删除入库记录失败: {ex.Message}");
            }
        }

        public bool DeleteSalesRecord(string orderNo)
        {
            try
            {
                db.ExecuteNonQuery("BEGIN TRANSACTION");

                try
                {
                    string deleteDetailsSql = "DELETE FROM sales_items WHERE order_no = @order_no";
                    db.ExecuteNonQuery(deleteDetailsSql, new Dictionary<string, object> { { "@order_no", orderNo } });

                    string deleteMainSql = "DELETE FROM sales_transactions WHERE order_no = @order_no";
                    int rowsAffected = db.ExecuteNonQuery(deleteMainSql, new Dictionary<string, object> { { "@order_no", orderNo } });

                    db.ExecuteNonQuery("COMMIT");

                    return rowsAffected > 0;
                }
                catch (Exception ex)
                {
                    try { db.ExecuteNonQuery("ROLLBACK"); } catch { }
                    throw new Exception($"删除销售记录失败: {ex.Message}");
                }
            }
            catch (Exception ex)
            {
                throw new Exception($"删除销售记录失败: {ex.Message}");
            }
        }

        public bool DeletePackagingRecord(string orderNo)
        {
            try
            {
                db.ExecuteNonQuery("BEGIN TRANSACTION");

                try
                {
                    string deleteDetailsSql = "DELETE FROM packaging_items WHERE order_no = @order_no";
                    db.ExecuteNonQuery(deleteDetailsSql, new Dictionary<string, object> { { "@order_no", orderNo } });

                    string deleteMainSql = "DELETE FROM packaging_transactions WHERE order_no = @order_no";
                    int rowsAffected = db.ExecuteNonQuery(deleteMainSql, new Dictionary<string, object> { { "@order_no", orderNo } });

                    db.ExecuteNonQuery("COMMIT");

                    return rowsAffected > 0;
                }
                catch (Exception ex)
                {
                    try { db.ExecuteNonQuery("ROLLBACK"); } catch { }
                    throw new Exception($"删除包装记录失败: {ex.Message}");
                }
            }
            catch (Exception ex)
            {
                throw new Exception($"删除包装记录失败: {ex.Message}");
            }
        }

        // 其他辅助方法保持不变...
        public bool CheckDatabaseConnection()
        {
            try
            {
                string sql = "SELECT 1";
                object result = db.ExecuteScalar(sql);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        public bool CheckTableExists(string tableName)
        {
            try
            {
                string sql = $"SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='{tableName}'";
                object result = db.ExecuteScalar(sql);
                return Convert.ToInt32(result) > 0;
            }
            catch
            {
                return false;
            }
        }

        public int GetRecordCount(string tableName)
        {
            try
            {
                string sql = $"SELECT COUNT(*) FROM {tableName}";
                object result = db.ExecuteScalar(sql);
                return Convert.ToInt32(result);
            }
            catch
            {
                return 0;
            }
        }

        public bool DeleteRecord(string tableName, int id)
        {
            try
            {
                string sql = $"DELETE FROM {tableName} WHERE id = @id";
                return db.ExecuteNonQuery(sql, new Dictionary<string, object> { { "@id", id } }) > 0;
            }
            catch (Exception ex)
            {
                throw new Exception($"删除记录失败: {ex.Message}");
            }
        }
    }

    // 数据项类定义保持不变
    public class InboundItem
    {
        public string Spec { get; set; }
        public int Quantity { get; set; }
        public double UnitPrice { get; set; }
        public double TotalAmount { get; set; }
    }

    public class SalesItem
    {
        public string Spec { get; set; }
        public int Quantity { get; set; }
        public double UnitPrice { get; set; }
        public double TotalAmount { get; set; }
    }

    public class PackagingItem
    {
        public string PackType { get; set; }
        public int Quantity { get; set; }
        public double UnitPrice { get; set; }
        public double TotalAmount { get; set; }
    }
}