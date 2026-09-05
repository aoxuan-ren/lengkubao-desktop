using System;
using System.Data;
using System.Drawing;
using System.Windows.Forms;
using System.Collections.Generic;

namespace lengkubao.desktop
{
    public class SalesEditDialog : Form
    {
        private string tableName;
        private string recordId;
        private DataTable recordData;
        private DatabaseManager db;

        // 控件声明
        private TextBox txtOrderNo;
        private TextBox txtClientName;
        private TextBox txtClientCode;
        private TextBox txtSpec;
        private NumericUpDown numQuantity;
        private TextBox txtUnitPrice;
        private TextBox txtTotalAmount;
        private TextBox txtLocation;
        private TextBox txtHandler;
        private DateTimePicker dtpSalesDate;
        private TextBox txtRemarks;

        private Button btnSave;
        private Button btnCancel;

        public SalesEditDialog(string tableName, string recordId)
        {
            this.tableName = tableName;
            this.recordId = recordId;
            InitializeForm();
            LoadRecordData();
        }

        private void InitializeForm()
        {
            // 窗体设置
            this.Text = "编辑销售记录";
            this.Size = new Size(500, 500);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.BackColor = Color.FromArgb(240, 242, 245);
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;

            // 标题
            Label lblTitle = new Label();
            lblTitle.Text = "编辑销售记录";
            lblTitle.Font = new Font("微软雅黑", 16, FontStyle.Bold);
            lblTitle.ForeColor = Color.FromArgb(0, 102, 204);
            lblTitle.Size = new Size(200, 30);
            lblTitle.Location = new Point(150, 20);
            lblTitle.TextAlign = ContentAlignment.MiddleCenter;
            this.Controls.Add(lblTitle);

            // 创建表单面板
            Panel formPanel = new Panel();
            formPanel.Location = new Point(30, 60);
            formPanel.Size = new Size(440, 350);
            formPanel.BackColor = Color.White;
            formPanel.BorderStyle = BorderStyle.FixedSingle;
            this.Controls.Add(formPanel);

            int yPos = 20;
            int spacing = 35;
            int labelWidth = 100;
            int controlX = 120;

            // 1. 销售单号
            AddFormField(formPanel, "销售单号:", ref yPos, labelWidth, controlX, out txtOrderNo);
            txtOrderNo.ReadOnly = true;
            yPos += spacing;

            // 2. 客户名称
            AddFormField(formPanel, "客户名称:", ref yPos, labelWidth, controlX, out txtClientName);
            yPos += spacing;

            // 3. 客户编码
            AddFormField(formPanel, "客户编码:", ref yPos, labelWidth, controlX, out txtClientCode);
            yPos += spacing;

            // 4. 规格
            AddFormField(formPanel, "商品规格:", ref yPos, labelWidth, controlX, out txtSpec);
            yPos += spacing;

            // 5. 数量
            AddLabel(formPanel, "数量:", yPos, labelWidth);
            numQuantity = new NumericUpDown();
            numQuantity.Location = new Point(controlX, yPos);
            numQuantity.Size = new Size(280, 28);
            numQuantity.Font = new Font("微软雅黑", 10);
            numQuantity.Minimum = 1;
            numQuantity.Maximum = 99999;
            numQuantity.Value = 1;
            numQuantity.ValueChanged += (s, e) => CalculateTotal();
            formPanel.Controls.Add(numQuantity);
            yPos += spacing;

            // 6. 单价
            AddFormField(formPanel, "单价:", ref yPos, labelWidth, controlX, out txtUnitPrice);
            txtUnitPrice.TextChanged += (s, e) => CalculateTotal();
            yPos += spacing;

            // 7. 总金额
            AddFormField(formPanel, "总金额:", ref yPos, labelWidth, controlX, out txtTotalAmount);
            txtTotalAmount.ReadOnly = true;
            txtTotalAmount.BackColor = Color.WhiteSmoke;
            yPos += spacing;

            // 8. 库位
            AddFormField(formPanel, "库位:", ref yPos, labelWidth, controlX, out txtLocation);
            yPos += spacing;

            // 9. 经手人
            AddFormField(formPanel, "经手人:", ref yPos, labelWidth, controlX, out txtHandler);
            yPos += spacing;

            // 10. 销售日期
            AddLabel(formPanel, "销售日期:", yPos, labelWidth);
            dtpSalesDate = new DateTimePicker();
            dtpSalesDate.Location = new Point(controlX, yPos);
            dtpSalesDate.Size = new Size(280, 28);
            dtpSalesDate.Font = new Font("微软雅黑", 10);
            dtpSalesDate.Format = DateTimePickerFormat.Short;
            formPanel.Controls.Add(dtpSalesDate);
            yPos += spacing;

            // 11. 备注
            AddLabel(formPanel, "备注:", yPos, labelWidth);
            txtRemarks = new TextBox();
            txtRemarks.Location = new Point(controlX, yPos);
            txtRemarks.Size = new Size(280, 60);
            txtRemarks.Font = new Font("微软雅黑", 10);
            txtRemarks.Multiline = true;
            txtRemarks.ScrollBars = ScrollBars.Vertical;
            formPanel.Controls.Add(txtRemarks);

            // 按钮区域
            Panel buttonPanel = new Panel();
            buttonPanel.Location = new Point(30, 420);
            buttonPanel.Size = new Size(440, 50);
            buttonPanel.BackColor = Color.Transparent;
            this.Controls.Add(buttonPanel);

            // 保存按钮
            btnSave = new Button();
            btnSave.Text = "保 存";
            btnSave.Size = new Size(100, 35);
            btnSave.Location = new Point(120, 8);
            btnSave.Font = new Font("微软雅黑", 10, FontStyle.Bold);
            btnSave.BackColor = Color.FromArgb(0, 150, 136);
            btnSave.ForeColor = Color.White;
            btnSave.FlatStyle = FlatStyle.Flat;
            btnSave.FlatAppearance.BorderSize = 0;
            btnSave.Cursor = Cursors.Hand;
            btnSave.Click += BtnSave_Click;

            // 取消按钮
            btnCancel = new Button();
            btnCancel.Text = "取 消";
            btnCancel.Size = new Size(100, 35);
            btnCancel.Location = new Point(240, 8);
            btnCancel.Font = new Font("微软雅黑", 10);
            btnCancel.BackColor = Color.FromArgb(158, 158, 158);
            btnCancel.ForeColor = Color.White;
            btnCancel.FlatStyle = FlatStyle.Flat;
            btnCancel.FlatAppearance.BorderSize = 0;
            btnCancel.Cursor = Cursors.Hand;
            btnCancel.Click += (s, e) =>
            {
                this.DialogResult = DialogResult.Cancel;
                this.Close();
            };

            buttonPanel.Controls.Add(btnSave);
            buttonPanel.Controls.Add(btnCancel);
        }

        private void AddLabel(Panel panel, string text, int yPos, int labelWidth)
        {
            Label label = new Label();
            label.Text = text;
            label.Location = new Point(20, yPos + 5);
            label.Size = new Size(labelWidth, 25);
            label.Font = new Font("微软雅黑", 10);
            label.TextAlign = ContentAlignment.MiddleRight;
            label.ForeColor = Color.FromArgb(51, 51, 51);
            panel.Controls.Add(label);
        }

        private void AddFormField(Panel panel, string labelText, ref int yPos, int labelWidth, int controlX, out TextBox textBox)
        {
            AddLabel(panel, labelText, yPos, labelWidth);

            textBox = new TextBox();
            textBox.Location = new Point(controlX, yPos);
            textBox.Size = new Size(280, 28);
            textBox.Font = new Font("微软雅黑", 10);
            panel.Controls.Add(textBox);
        }

        private void CalculateTotal()
        {
            try
            {
                int quantity = (int)numQuantity.Value;
                if (decimal.TryParse(txtUnitPrice.Text, out decimal unitPrice))
                {
                    decimal total = quantity * unitPrice;
                    txtTotalAmount.Text = total.ToString("0.00");
                }
                else
                {
                    txtTotalAmount.Text = "0.00";
                }
            }
            catch
            {
                txtTotalAmount.Text = "0.00";
            }
        }

        private void LoadRecordData()
        {
            try
            {
                db = new DatabaseManager();

                // 查询记录数据
                string sql = $"SELECT * FROM {tableName} WHERE id = @id";
                var parameters = new Dictionary<string, object> { { "@id", recordId } };
                recordData = db.ExecuteQuery(sql, parameters);

                if (recordData == null || recordData.Rows.Count == 0)
                {
                    MessageBox.Show("未找到要编辑的记录！", "错误",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                    this.Close();
                    return;
                }

                DataRow row = recordData.Rows[0];

                // 填充数据到控件
                foreach (DataColumn column in recordData.Columns)
                {
                    string columnName = column.ColumnName.ToLower();
                    object value = row[column];

                    if (value == DBNull.Value || value == null)
                        continue;

                    string stringValue = value.ToString();

                    // 根据字段名设置对应的控件
                    if (columnName.Contains("order_no") || columnName.Contains("销售单号"))
                        txtOrderNo.Text = stringValue;
                    else if (columnName.Contains("client_name") || columnName.Contains("客户名称"))
                        txtClientName.Text = stringValue;
                    else if (columnName.Contains("client_code") || columnName.Contains("客户编码"))
                        txtClientCode.Text = stringValue;
                    else if (columnName.Contains("spec") || columnName.Contains("规格"))
                        txtSpec.Text = stringValue;
                    else if (columnName.Contains("quantity") || columnName.Contains("数量"))
                        numQuantity.Value = Convert.ToInt32(value);
                    else if (columnName.Contains("unit_price") || columnName.Contains("单价"))
                        txtUnitPrice.Text = Convert.ToDecimal(value).ToString("0.00");
                    else if (columnName.Contains("total_amount") || columnName.Contains("总金额"))
                        txtTotalAmount.Text = Convert.ToDecimal(value).ToString("0.00");
                    else if (columnName.Contains("location") || columnName.Contains("库位"))
                        txtLocation.Text = stringValue;
                    else if (columnName.Contains("handler") || columnName.Contains("经手人"))
                        txtHandler.Text = stringValue;
                    else if (columnName.Contains("date") || columnName.Contains("销售日期"))
                    {
                        try
                        {
                            dtpSalesDate.Value = Convert.ToDateTime(value);
                        }
                        catch
                        {
                            // 如果日期转换失败，使用当前日期
                        }
                    }
                    else if (columnName.Contains("remarks") || columnName.Contains("备注"))
                        txtRemarks.Text = stringValue;
                }

                // 如果没有总金额，计算一个
                if (string.IsNullOrEmpty(txtTotalAmount.Text) || txtTotalAmount.Text == "0.00")
                {
                    CalculateTotal();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"加载记录数据失败：{ex.Message}", "错误",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                this.Close();
            }
        }

        private void BtnSave_Click(object sender, EventArgs e)
        {
            if (!ValidateInput())
                return;

            try
            {
                // 构建更新SQL
                var updates = new List<string>();
                var parameters = new Dictionary<string, object>();

                // 收集要更新的字段
                AddUpdateField(updates, parameters, "client_name", txtClientName.Text);
                AddUpdateField(updates, parameters, "client_code", txtClientCode.Text);
                AddUpdateField(updates, parameters, "spec", txtSpec.Text);
                AddUpdateField(updates, parameters, "quantity", (int)numQuantity.Value);
                AddUpdateField(updates, parameters, "unit_price", decimal.Parse(txtUnitPrice.Text));
                AddUpdateField(updates, parameters, "total_amount", decimal.Parse(txtTotalAmount.Text));
                AddUpdateField(updates, parameters, "location", txtLocation.Text);
                AddUpdateField(updates, parameters, "handler", txtHandler.Text);
                AddUpdateField(updates, parameters, "sales_date", dtpSalesDate.Value.ToString("yyyy-MM-dd"));
                AddUpdateField(updates, parameters, "remarks", txtRemarks.Text);
                AddUpdateField(updates, parameters, "update_time", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));

                if (updates.Count == 0)
                {
                    MessageBox.Show("没有需要更新的字段！", "提示",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                // 构建完整SQL
                string setClause = string.Join(", ", updates);
                string sql = $"UPDATE {tableName} SET {setClause} WHERE id = @id";
                parameters.Add("@id", recordId);

                // 执行更新
                int affectedRows = db.ExecuteNonQuery(sql, parameters);

                if (affectedRows > 0)
                {
                    MessageBox.Show("销售记录更新成功！", "成功",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                    this.DialogResult = DialogResult.OK;
                    this.Close();
                }
                else
                {
                    MessageBox.Show("更新失败，请重试！", "错误",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"保存失败：{ex.Message}", "错误",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void AddUpdateField(List<string> updates, Dictionary<string, object> parameters,
                                   string fieldName, object value)
        {
            if (value != null)
            {
                string paramName = $"@p{updates.Count}";
                updates.Add($"{fieldName} = {paramName}");
                parameters.Add(paramName, value);
            }
        }

        private bool ValidateInput()
        {
            if (string.IsNullOrWhiteSpace(txtClientName.Text))
            {
                MessageBox.Show("请输入客户名称！", "提示",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                txtClientName.Focus();
                return false;
            }

            if (string.IsNullOrWhiteSpace(txtSpec.Text))
            {
                MessageBox.Show("请输入商品规格！", "提示",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                txtSpec.Focus();
                return false;
            }

            if (numQuantity.Value <= 0)
            {
                MessageBox.Show("请输入有效数量！", "提示",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                numQuantity.Focus();
                return false;
            }

            if (!decimal.TryParse(txtUnitPrice.Text, out decimal unitPrice) || unitPrice <= 0)
            {
                MessageBox.Show("请输入有效单价！", "提示",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                txtUnitPrice.Focus();
                txtUnitPrice.SelectAll();
                return false;
            }

            if (string.IsNullOrWhiteSpace(txtHandler.Text))
            {
                MessageBox.Show("请输入经手人！", "提示",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                txtHandler.Focus();
                return false;
            }

            return true;
        }
    }
}