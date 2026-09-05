using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SQLite;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace lengkubao.desktop
{
    public static class SalesQueryHelper
    {
        // 数据库连接字符串
        private static string ConnectionString => $"Data Source={System.IO.Path.Combine(Application.StartupPath, "lengkubao.db")};Version=3;";

        public static void ShowSalesQuery()
        {
            // 创建窗体
            Form form = new Form();
            form.Text = "销售记录管理系统";
            form.Size = new Size(1200, 600); // 增大窗体
            form.StartPosition = FormStartPosition.CenterScreen;
            form.FormBorderStyle = FormBorderStyle.FixedDialog;
            form.MaximizeBox = false;
            form.BackColor = Color.White;

            // 创建控件
            DataGridView dataGrid = new DataGridView();
            TextBox txtSearch = new TextBox();
            ComboBox cmbSearchType = new ComboBox();
            Button btnSearch = new Button();
            Button btnRefresh = new Button();
            Button btnAdd = new Button();
            Button btnEdit = new Button();
            Button btnDelete = new Button();
            Button btnBatchDelete = new Button();
            Button btnExport = new Button();
            Button btnClose = new Button();
            CheckBox chkSelectAll = new CheckBox();
            Label lblCount = new Label();

            int yPos = 20;
            int xPos = 20;

            // ========== 搜索区域 ==========
            // 搜索类型
            Label lblSearchType = new Label
            {
                Text = "搜索类型:",
                Location = new Point(xPos, yPos + 5),
                Size = new Size(70, 25),
                Font = new Font("微软雅黑", 9)
            };
            form.Controls.Add(lblSearchType);

            cmbSearchType.Location = new Point(xPos + 75, yPos);
            cmbSearchType.Size = new Size(100, 25);
            cmbSearchType.Items.AddRange(new object[] { "销售单号", "客户名称", "商品规格", "经手人" });
            cmbSearchType.SelectedIndex = 0;
            form.Controls.Add(cmbSearchType);

            xPos += 185;

            // 搜索框
            Label lblSearch = new Label
            {
                Text = "关键词:",
                Location = new Point(xPos, yPos + 5),
                Size = new Size(60, 25),
                Font = new Font("微软雅黑", 9)
            };
            form.Controls.Add(lblSearch);

            txtSearch.Location = new Point(xPos + 65, yPos);
            txtSearch.Size = new Size(200, 25);
            txtSearch.Font = new Font("微软雅黑", 9);
            txtSearch.Text = "输入搜索关键词...";
            txtSearch.ForeColor = Color.Gray;

            // 水印效果
            txtSearch.GotFocus += (s, e) =>
            {
                if (txtSearch.Text == "输入搜索关键词...")
                {
                    txtSearch.Text = "";
                    txtSearch.ForeColor = Color.Black;
                }
            };

            txtSearch.LostFocus += (s, e) =>
            {
                if (string.IsNullOrWhiteSpace(txtSearch.Text))
                {
                    txtSearch.Text = "输入搜索关键词...";
                    txtSearch.ForeColor = Color.Gray;
                }
            };

            txtSearch.KeyPress += (s, e) =>
            {
                if (e.KeyChar == (char)13) // 回车键搜索
                {
                    SearchData(dataGrid, lblCount, cmbSearchType.SelectedItem.ToString(), txtSearch.Text);
                    e.Handled = true;
                }
            };
            form.Controls.Add(txtSearch);

            xPos += 275;

            // 搜索按钮
            btnSearch.Location = new Point(xPos, yPos);
            btnSearch.Size = new Size(80, 25);
            btnSearch.Text = "搜索";
            btnSearch.BackColor = Color.FromArgb(0, 122, 204);
            btnSearch.ForeColor = Color.White;
            btnSearch.Click += (s, e) =>
            {
                SearchData(dataGrid, lblCount, cmbSearchType.SelectedItem.ToString(), txtSearch.Text);
            };
            form.Controls.Add(btnSearch);

            xPos += 90;

            // 刷新按钮
            btnRefresh.Location = new Point(xPos, yPos);
            btnRefresh.Size = new Size(80, 25);
            btnRefresh.Text = "刷新";
            btnRefresh.BackColor = Color.FromArgb(108, 117, 125);
            btnRefresh.ForeColor = Color.White;
            btnRefresh.Click += (s, e) =>
            {
                txtSearch.Text = "输入搜索关键词...";
                txtSearch.ForeColor = Color.Gray;
                cmbSearchType.SelectedIndex = 0;
                LoadData(dataGrid, lblCount);
            };
            form.Controls.Add(btnRefresh);

            // ========== 按钮区域 ==========
            yPos = 60;
            xPos = 20;

            // 新增按钮
            btnAdd.Location = new Point(xPos, yPos);
            btnAdd.Size = new Size(80, 30);
            btnAdd.Text = "新增";
            btnAdd.BackColor = Color.FromArgb(40, 167, 69); // 绿色
            btnAdd.ForeColor = Color.White;
            btnAdd.Font = new Font("微软雅黑", 9);
            btnAdd.Click += (s, e) =>
            {
                AddNewRecord(dataGrid, lblCount);
            };
            form.Controls.Add(btnAdd);

            xPos += 90;

            // 编辑按钮
            btnEdit.Location = new Point(xPos, yPos);
            btnEdit.Size = new Size(80, 30);
            btnEdit.Text = "编辑";
            btnEdit.BackColor = Color.FromArgb(0, 150, 136); // 青色
            btnEdit.ForeColor = Color.White;
            btnEdit.Font = new Font("微软雅黑", 9);
            btnEdit.Click += (s, e) =>
            {
                EditSelectedRecord(dataGrid, lblCount);
            };
            form.Controls.Add(btnEdit);

            xPos += 90;

            // 删除按钮
            btnDelete.Location = new Point(xPos, yPos);
            btnDelete.Size = new Size(80, 30);
            btnDelete.Text = "删除";
            btnDelete.BackColor = Color.FromArgb(220, 53, 69); // 红色
            btnDelete.ForeColor = Color.White;
            btnDelete.Font = new Font("微软雅黑", 9);
            btnDelete.Click += (s, e) =>
            {
                DeleteSelectedRecord(dataGrid, lblCount, false);
            };
            form.Controls.Add(btnDelete);

            xPos += 90;

            // 批量删除按钮
            btnBatchDelete.Location = new Point(xPos, yPos);
            btnBatchDelete.Size = new Size(100, 30);
            btnBatchDelete.Text = "批量删除";
            btnBatchDelete.BackColor = Color.FromArgb(192, 57, 43); // 深红色
            btnBatchDelete.ForeColor = Color.White;
            btnBatchDelete.Font = new Font("微软雅黑", 9);
            btnBatchDelete.Click += (s, e) =>
            {
                DeleteSelectedRecord(dataGrid, lblCount, true);
            };
            form.Controls.Add(btnBatchDelete);

            xPos += 110;

            // 导出按钮
            btnExport.Location = new Point(xPos, yPos);
            btnExport.Size = new Size(100, 30);
            btnExport.Text = "导出Excel";
            btnExport.BackColor = Color.FromArgb(108, 117, 125); // 灰色
            btnExport.ForeColor = Color.White;
            btnExport.Font = new Font("微软雅黑", 9);
            btnExport.Click += (s, e) =>
            {
                ExportToExcel(dataGrid);
            };
            form.Controls.Add(btnExport);

            xPos += 110;

            // 关闭按钮
            btnClose.Location = new Point(xPos, yPos);
            btnClose.Size = new Size(80, 30);
            btnClose.Text = "关闭";
            btnClose.BackColor = Color.FromArgb(52, 152, 219); // 蓝色
            btnClose.ForeColor = Color.White;
            btnClose.Font = new Font("微软雅黑", 9);
            btnClose.Click += (s, e) => form.Close();
            form.Controls.Add(btnClose);

            // ========== 全选复选框 ==========
            chkSelectAll.Location = new Point(20, 95);
            chkSelectAll.Size = new Size(80, 20);
            chkSelectAll.Text = "全选";
            chkSelectAll.Font = new Font("微软雅黑", 9);
            chkSelectAll.CheckedChanged += (s, e) =>
            {
                SelectAllRecords(dataGrid, chkSelectAll.Checked);
            };
            form.Controls.Add(chkSelectAll);

            // ========== 记录数量标签 ==========
            lblCount.Location = new Point(110, 98);
            lblCount.Size = new Size(200, 20);
            lblCount.Font = new Font("微软雅黑", 9);
            lblCount.Text = "共 0 条记录";
            form.Controls.Add(lblCount);

            // ========== DataGridView ==========
            dataGrid.Location = new Point(20, 120);
            dataGrid.Size = new Size(1150, 420);
            dataGrid.BackgroundColor = Color.White;
            dataGrid.RowHeadersVisible = false;
            dataGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            dataGrid.MultiSelect = true; // 允许多选
            dataGrid.AllowUserToAddRows = false;
            dataGrid.AllowUserToResizeRows = false;
            dataGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.AllCells;

            // 添加选择列
            DataGridViewCheckBoxColumn checkBoxColumn = new DataGridViewCheckBoxColumn();
            checkBoxColumn.HeaderText = "选择";
            checkBoxColumn.Name = "Select";
            checkBoxColumn.Width = 50;
            dataGrid.Columns.Add(checkBoxColumn);

            // 设置列头样式
            dataGrid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(0, 122, 204);
            dataGrid.ColumnHeadersDefaultCellStyle.ForeColor = Color.White;
            dataGrid.ColumnHeadersDefaultCellStyle.Font = new Font("微软雅黑", 10, FontStyle.Bold);
            dataGrid.ColumnHeadersHeight = 40;

            // 设置行样式
            dataGrid.RowTemplate.Height = 35;
            dataGrid.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(240, 240, 240);

            form.Controls.Add(dataGrid);

            // 加载初始数据
            LoadData(dataGrid, lblCount);

            // 显示窗体
            form.ShowDialog();
        }

        // ========== 数据加载方法 ==========
        private static void LoadData(DataGridView dataGrid, Label lblCount, string searchType = "", string keyword = "")
        {
            try
            {
                DataTable table = new DataTable();

                // 添加列（包括隐藏的ID列）
                table.Columns.Add("ID", typeof(int));
                table.Columns.Add("销售单号", typeof(string));
                table.Columns.Add("客户名称", typeof(string));
                table.Columns.Add("客户编码", typeof(string));
                table.Columns.Add("商品规格", typeof(string));
                table.Columns.Add("销售数量", typeof(int));
                table.Columns.Add("销售单价", typeof(decimal));
                table.Columns.Add("金额合计", typeof(decimal));
                table.Columns.Add("销售日期", typeof(DateTime));
                table.Columns.Add("经手人", typeof(string));
                table.Columns.Add("库位", typeof(string));
                table.Columns.Add("备注", typeof(string));
                table.Columns.Add("创建时间", typeof(DateTime));

                using (var conn = new SQLiteConnection(ConnectionString))
                {
                    conn.Open();

                    // 构建查询SQL
                    string sql = @"SELECT id, order_no, client_name, client_code, spec, quantity, 
                                          unit_price, total_amount, date, handler, location, 
                                          remarks, created_time 
                                   FROM sales_transactions 
                                   WHERE 1=1";

                    // 添加搜索条件
                    if (!string.IsNullOrWhiteSpace(keyword) && keyword != "输入搜索关键词...")
                    {
                        switch (searchType)
                        {
                            case "销售单号":
                                sql += " AND order_no LIKE @keyword";
                                break;
                            case "客户名称":
                                sql += " AND client_name LIKE @keyword";
                                break;
                            case "商品规格":
                                sql += " AND spec LIKE @keyword";
                                break;
                            case "经手人":
                                sql += " AND handler LIKE @keyword";
                                break;
                        }
                    }

                    sql += " ORDER BY created_time DESC";

                    using (var cmd = new SQLiteCommand(sql, conn))
                    {
                        if (!string.IsNullOrWhiteSpace(keyword) && keyword != "输入搜索关键词...")
                        {
                            cmd.Parameters.AddWithValue("@keyword", "%" + keyword + "%");
                        }

                        using (var reader = cmd.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                table.Rows.Add(
                                    Convert.ToInt32(reader["id"]),
                                    reader["order_no"].ToString(),
                                    reader["client_name"].ToString(),
                                    reader["client_code"].ToString(),
                                    reader["spec"].ToString(),
                                    Convert.ToInt32(reader["quantity"]),
                                    Convert.ToDecimal(reader["unit_price"]),
                                    Convert.ToDecimal(reader["total_amount"]),
                                    Convert.ToDateTime(reader["date"]),
                                    reader["handler"].ToString(),
                                    reader["location"].ToString(),
                                    reader["remarks"].ToString(),
                                    Convert.ToDateTime(reader["created_time"])
                                );
                            }
                        }
                    }
                }

                // 绑定数据
                dataGrid.DataSource = table;

                // 隐藏ID列
                if (dataGrid.Columns.Contains("ID"))
                {
                    dataGrid.Columns["ID"].Visible = false;
                }

                // 设置列格式
                dataGrid.Columns["销售单价"].DefaultCellStyle.Format = "N2";
                dataGrid.Columns["金额合计"].DefaultCellStyle.Format = "N2";
                dataGrid.Columns["销售日期"].DefaultCellStyle.Format = "yyyy-MM-dd";
                dataGrid.Columns["创建时间"].DefaultCellStyle.Format = "yyyy-MM-dd HH:mm";

                // 数字列右对齐
                dataGrid.Columns["销售数量"].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
                dataGrid.Columns["销售单价"].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
                dataGrid.Columns["金额合计"].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;

                lblCount.Text = $"共 {table.Rows.Count} 条记录";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"加载销售记录失败: {ex.Message}", "错误",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // ========== 搜索方法 ==========
        private static void SearchData(DataGridView dataGrid, Label lblCount, string searchType, string keyword)
        {
            if (string.IsNullOrWhiteSpace(keyword) || keyword == "输入搜索关键词...")
            {
                MessageBox.Show("请输入搜索关键词！", "提示",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            LoadData(dataGrid, lblCount, searchType, keyword);
        }

        // ========== 全选/全不选方法 ==========
        private static void SelectAllRecords(DataGridView dataGrid, bool isChecked)
        {
            foreach (DataGridViewRow row in dataGrid.Rows)
            {
                if (row.Cells["Select"] is DataGridViewCheckBoxCell checkBoxCell)
                {
                    checkBoxCell.Value = isChecked;
                }
            }
        }

        // ========== 新增记录方法 ==========
        private static void AddNewRecord(DataGridView dataGrid, Label lblCount)
        {
            try
            {
                // 这里可以打开一个新的销售录入窗体
                // 暂时先用消息框提示
                MessageBox.Show("新增销售记录功能需要打开销售录入窗体。\n这个功能需要您已有的销售录入窗体配合。",
                    "功能说明", MessageBoxButtons.OK, MessageBoxIcon.Information);

                // 如果您有 SalesForm.cs 或类似的销售录入窗体，可以这样调用：
                // SalesForm salesForm = new SalesForm();
                // if (salesForm.ShowDialog() == DialogResult.OK)
                // {
                //     LoadData(dataGrid, lblCount); // 重新加载数据
                // }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"打开新增窗体失败: {ex.Message}", "错误",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // ========== 编辑记录方法 ==========
        private static void EditSelectedRecord(DataGridView dataGrid, Label lblCount)
        {
            if (dataGrid.SelectedRows.Count == 0)
            {
                MessageBox.Show("请先选择要编辑的记录！", "提示",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            try
            {
                DataGridViewRow selectedRow = dataGrid.SelectedRows[0];

                // 获取记录ID
                int recordId = 0;
                if (dataGrid.Columns.Contains("ID") && selectedRow.Cells["ID"].Value != null)
                {
                    recordId = Convert.ToInt32(selectedRow.Cells["ID"].Value);
                }

                if (recordId == 0)
                {
                    MessageBox.Show("无法获取记录ID！", "错误",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                // 获取记录详情
                string orderNo = selectedRow.Cells["销售单号"].Value?.ToString() ?? "";
                string clientName = selectedRow.Cells["客户名称"].Value?.ToString() ?? "";
                string spec = selectedRow.Cells["商品规格"].Value?.ToString() ?? "";

                // 这里可以打开编辑窗体
                MessageBox.Show($"将要编辑记录：\n销售单号：{orderNo}\n客户：{clientName}\n规格：{spec}\n\n编辑功能需要您已有的编辑窗体配合。",
                    "编辑记录", MessageBoxButtons.OK, MessageBoxIcon.Information);

                // 如果您有 SalesEditForm.cs 或类似的编辑窗体，可以这样调用：
                // SalesEditForm editForm = new SalesEditForm(recordId);
                // if (editForm.ShowDialog() == DialogResult.OK)
                // {
                //     LoadData(dataGrid, lblCount); // 重新加载数据
                // }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"编辑失败: {ex.Message}", "错误",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // ========== 删除记录方法 ==========
        private static void DeleteSelectedRecord(DataGridView dataGrid, Label lblCount, bool isBatchDelete)
        {
            try
            {
                List<int> recordIds = new List<int>();
                List<string> recordInfos = new List<string>();

                if (isBatchDelete)
                {
                    // 批量删除：获取所有选中的行
                    foreach (DataGridViewRow row in dataGrid.Rows)
                    {
                        if (row.Cells["Select"] is DataGridViewCheckBoxCell checkBoxCell &&
                            checkBoxCell.Value != null && Convert.ToBoolean(checkBoxCell.Value))
                        {
                            if (row.Cells["ID"].Value != null)
                            {
                                int id = Convert.ToInt32(row.Cells["ID"].Value);
                                string orderNo = row.Cells["销售单号"].Value?.ToString() ?? "";
                                string clientName = row.Cells["客户名称"].Value?.ToString() ?? "";

                                recordIds.Add(id);
                                recordInfos.Add($"销售单号：{orderNo}，客户：{clientName}");
                            }
                        }
                    }

                    if (recordIds.Count == 0)
                    {
                        MessageBox.Show("请先勾选要删除的记录！", "提示",
                            MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return;
                    }
                }
                else
                {
                    // 单条删除：获取选中的行
                    if (dataGrid.SelectedRows.Count == 0)
                    {
                        MessageBox.Show("请先选择要删除的记录！", "提示",
                            MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return;
                    }

                    DataGridViewRow selectedRow = dataGrid.SelectedRows[0];

                    if (selectedRow.Cells["ID"].Value != null)
                    {
                        int id = Convert.ToInt32(selectedRow.Cells["ID"].Value);
                        string orderNo = selectedRow.Cells["销售单号"].Value?.ToString() ?? "";
                        string clientName = selectedRow.Cells["客户名称"].Value?.ToString() ?? "";
                        string spec = selectedRow.Cells["商品规格"].Value?.ToString() ?? "";

                        recordIds.Add(id);
                        recordInfos.Add($"销售单号：{orderNo}，客户：{clientName}，规格：{spec}");
                    }
                }

                if (recordIds.Count == 0)
                {
                    MessageBox.Show("没有找到要删除的记录！", "提示",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                // 确认删除对话框
                string confirmMessage = $"确定要删除以下 {recordIds.Count} 条销售记录吗？\n\n";
                foreach (string info in recordInfos)
                {
                    confirmMessage += info + "\n";
                }
                confirmMessage += "\n此操作不可恢复！";

                DialogResult result = MessageBox.Show(confirmMessage, "确认删除",
                    MessageBoxButtons.OKCancel, MessageBoxIcon.Warning);

                if (result == DialogResult.OK)
                {
                    using (var conn = new SQLiteConnection(ConnectionString))
                    {
                        conn.Open();

                        // 使用事务确保数据一致性
                        using (var transaction = conn.BeginTransaction())
                        {
                            try
                            {
                                string deleteSql = "DELETE FROM sales_transactions WHERE id = @id";

                                foreach (int id in recordIds)
                                {
                                    using (var cmd = new SQLiteCommand(deleteSql, conn, transaction))
                                    {
                                        cmd.Parameters.AddWithValue("@id", id);
                                        cmd.ExecuteNonQuery();
                                    }
                                }

                                transaction.Commit();

                                MessageBox.Show($"成功删除 {recordIds.Count} 条记录！", "成功",
                                    MessageBoxButtons.OK, MessageBoxIcon.Information);

                                // 重新加载数据
                                LoadData(dataGrid, lblCount);
                            }
                            catch
                            {
                                transaction.Rollback();
                                throw;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"删除失败：{ex.Message}", "错误",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // ========== 导出Excel方法 ==========
        private static void ExportToExcel(DataGridView dataGrid)
        {
            if (dataGrid.Rows.Count == 0)
            {
                MessageBox.Show("没有数据可以导出！", "提示",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            SaveFileDialog saveDialog = new SaveFileDialog();
            saveDialog.Filter = "CSV文件 (*.csv)|*.csv|Excel文件 (*.xlsx)|*.xlsx|文本文件 (*.txt)|*.txt";
            saveDialog.FileName = $"销售记录_{DateTime.Now:yyyyMMdd_HHmmss}.csv";
            saveDialog.Title = "导出销售记录";

            if (saveDialog.ShowDialog() == DialogResult.OK)
            {
                try
                {
                    string filePath = saveDialog.FileName;
                    string extension = Path.GetExtension(filePath).ToLower();

                    switch (extension)
                    {
                        case ".csv":
                            ExportToCsv(dataGrid, filePath);
                            break;
                        case ".xlsx":
                            // 这里可以调用Excel导出组件
                            MessageBox.Show("Excel导出功能需要安装Excel组件或使用第三方库。\n建议先导出为CSV格式，然后用Excel打开。",
                                "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                            ExportToCsv(dataGrid, filePath.Replace(".xlsx", ".csv"));
                            break;
                        case ".txt":
                            ExportToText(dataGrid, filePath);
                            break;
                        default:
                            ExportToCsv(dataGrid, filePath);
                            break;
                    }

                    MessageBox.Show($"导出成功！\n文件已保存到：{filePath}", "成功",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"导出失败：{ex.Message}", "错误",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        // 导出为CSV
        private static void ExportToCsv(DataGridView dataGrid, string filePath)
        {
            using (StreamWriter writer = new StreamWriter(filePath, false, Encoding.UTF8))
            {
                // 写入列标题（排除选择列和ID列）
                List<string> headers = new List<string>();
                foreach (DataGridViewColumn column in dataGrid.Columns)
                {
                    if (column.Name != "Select" && column.Name != "ID" && column.Visible)
                    {
                        headers.Add(column.HeaderText);
                    }
                }
                writer.WriteLine(string.Join(",", headers));

                // 写入数据行
                foreach (DataGridViewRow row in dataGrid.Rows)
                {
                    if (row.IsNewRow) continue;

                    List<string> rowData = new List<string>();
                    foreach (DataGridViewColumn column in dataGrid.Columns)
                    {
                        if (column.Name != "Select" && column.Name != "ID" && column.Visible)
                        {
                            object value = row.Cells[column.Name].Value;
                            string cellText = FormatCellValueForCsv(value);
                            rowData.Add(cellText);
                        }
                    }
                    writer.WriteLine(string.Join(",", rowData));
                }
            }
        }

        // 导出为文本
        private static void ExportToText(DataGridView dataGrid, string filePath)
        {
            using (StreamWriter writer = new StreamWriter(filePath, false, Encoding.UTF8))
            {
                writer.WriteLine("销售记录报表");
                writer.WriteLine($"导出时间：{DateTime.Now:yyyy年MM月dd日 HH:mm:ss}");
                writer.WriteLine(new string('=', 80));
                writer.WriteLine();

                // 写入列标题
                StringBuilder header = new StringBuilder();
                foreach (DataGridViewColumn column in dataGrid.Columns)
                {
                    if (column.Name != "Select" && column.Name != "ID" && column.Visible)
                    {
                        header.Append(column.HeaderText.PadRight(15));
                    }
                }
                writer.WriteLine(header.ToString());
                writer.WriteLine(new string('-', header.Length));

                // 写入数据行
                foreach (DataGridViewRow row in dataGrid.Rows)
                {
                    if (row.IsNewRow) continue;

                    StringBuilder rowData = new StringBuilder();
                    foreach (DataGridViewColumn column in dataGrid.Columns)
                    {
                        if (column.Name != "Select" && column.Name != "ID" && column.Visible)
                        {
                            object value = row.Cells[column.Name].Value;
                            string cellText = FormatCellValueForText(value);
                            rowData.Append(cellText.PadRight(15));
                        }
                    }
                    writer.WriteLine(rowData.ToString());
                }
            }
        }

        // 格式化CSV单元格值
        private static string FormatCellValueForCsv(object value)
        {
            if (value == null || value == DBNull.Value)
                return "";

            string text = value.ToString();

            // 处理包含逗号、引号或换行符的文本
            if (text.Contains(",") || text.Contains("\"") || text.Contains("\n") || text.Contains("\r"))
            {
                text = "\"" + text.Replace("\"", "\"\"") + "\"";
            }

            return text;
        }

        // 格式化文本单元格值
        private static string FormatCellValueForText(object value)
        {
            if (value == null || value == DBNull.Value)
                return "";

            string text = value.ToString();

            // 截断长文本
            if (text.Length > 14)
            {
                text = text.Substring(0, 11) + "...";
            }

            return text;
        }
    }
}