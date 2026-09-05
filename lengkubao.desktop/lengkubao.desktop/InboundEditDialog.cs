using lengkubao.desktop;
using System;
using System.Collections.Generic;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace lengkubao.desktop
{
    public class InboundEditDialog : Form
    {
        private TextBox txtOrderNo;
        private DateTimePicker dtpDate;
        private TextBox txtHandler;
        private TextBox txtProductType;
        private NumericUpDown nudQuantity;
        private NumericUpDown nudUnitPrice;
        private TextBox txtLocation;
        private TextBox txtNotes;
        private Button btnSave;
        private Button btnCancel;

        private int recordId;
        private DatabaseManager db;

        public InboundEditDialog(int id)
        {
            recordId = id;
            db = new DatabaseManager();
            InitializeComponents();
            LoadRecordData();
        }

        private void InitializeComponents()
        {
            this.Text = "编辑入库记录";
            this.Size = new Size(400, 420);
            this.StartPosition = FormStartPosition.CenterParent;
            this.BackColor = Color.FromArgb(248, 248, 248);
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;

            // 标题
            Label lblTitle = new Label();
            lblTitle.Text = "编辑入库记录";
            lblTitle.Font = new Font("微软雅黑", 14, FontStyle.Bold);
            lblTitle.ForeColor = Color.FromArgb(52, 73, 94);
            lblTitle.Location = new Point(120, 15);
            lblTitle.Size = new Size(160, 30);
            lblTitle.TextAlign = ContentAlignment.MiddleCenter;
            this.Controls.Add(lblTitle);

            // 表单面板
            Panel panelForm = new Panel();
            panelForm.Location = new Point(20, 50);
            panelForm.Size = new Size(350, 290);
            panelForm.BackColor = Color.White;
            panelForm.BorderStyle = BorderStyle.FixedSingle;
            this.Controls.Add(panelForm);

            int yPos = 20;
            int labelWidth = 70;
            int controlWidth = 230;

            // 单据号
            AddLabel("单据号:", 20, yPos, labelWidth, panelForm);
            txtOrderNo = AddTextBox(20 + labelWidth, yPos, controlWidth, panelForm);
            txtOrderNo.ReadOnly = true;
            txtOrderNo.BackColor = Color.FromArgb(240, 240, 240);
            yPos += 32;

            // 日期
            AddLabel("日期:", 20, yPos, labelWidth, panelForm);
            dtpDate = new DateTimePicker();
            dtpDate.Location = new Point(20 + labelWidth, yPos);
            dtpDate.Size = new Size(controlWidth, 25);
            dtpDate.Font = new Font("微软雅黑", 9);
            panelForm.Controls.Add(dtpDate);
            yPos += 32;

            // 经手人
            AddLabel("经手人:", 20, yPos, labelWidth, panelForm);
            txtHandler = AddTextBox(20 + labelWidth, yPos, controlWidth, panelForm);
            txtHandler.Font = new Font("微软雅黑", 9);
            yPos += 32;

            // 商品型号
            AddLabel("商品型号:", 20, yPos, labelWidth, panelForm);
            txtProductType = AddTextBox(20 + labelWidth, yPos, controlWidth, panelForm);
            txtProductType.Font = new Font("微软雅黑", 9);
            yPos += 32;

            // 数量
            AddLabel("数量:", 20, yPos, labelWidth, panelForm);
            nudQuantity = AddNumericUpDown(20 + labelWidth, yPos, controlWidth, panelForm);
            yPos += 32;

            // 单价
            AddLabel("单价:", 20, yPos, labelWidth, panelForm);
            nudUnitPrice = AddNumericUpDown(20 + labelWidth, yPos, controlWidth, panelForm);
            nudUnitPrice.DecimalPlaces = 2;
            nudUnitPrice.Increment = 0.01m;
            yPos += 32;

            // 库位
            AddLabel("库位:", 20, yPos, labelWidth, panelForm);
            txtLocation = AddTextBox(20 + labelWidth, yPos, controlWidth, panelForm);
            txtLocation.Font = new Font("微软雅黑", 9);
            yPos += 32;

            // 备注
            AddLabel("备注:", 20, yPos, labelWidth, panelForm);
            txtNotes = AddTextBox(20 + labelWidth, yPos, controlWidth, panelForm);
            txtNotes.Font = new Font("微软雅黑", 9);
            yPos += 40;

            // 按钮面板
            Panel panelButtons = new Panel();
            panelButtons.Location = new Point(20, 340);
            panelButtons.Size = new Size(350, 50);
            panelButtons.BackColor = Color.Transparent;
            this.Controls.Add(panelButtons);

            // 保存按钮
            btnSave = new Button();
            btnSave.Text = "保存";
            btnSave.Location = new Point(90, 10);
            btnSave.Size = new Size(80, 35);
            btnSave.Font = new Font("微软雅黑", 10);
            btnSave.BackColor = Color.FromArgb(46, 204, 113);
            btnSave.ForeColor = Color.White;
            btnSave.FlatStyle = FlatStyle.Flat;
            btnSave.FlatAppearance.BorderSize = 0;
            btnSave.Cursor = Cursors.Hand;
            btnSave.Click += BtnSave_Click;
            panelButtons.Controls.Add(btnSave);

            // 取消按钮
            btnCancel = new Button();
            btnCancel.Text = "取消";
            btnCancel.Location = new Point(180, 10);
            btnCancel.Size = new Size(80, 35);
            btnCancel.Font = new Font("微软雅黑", 10);
            btnCancel.BackColor = Color.FromArgb(149, 165, 166);
            btnCancel.ForeColor = Color.White;
            btnCancel.FlatStyle = FlatStyle.Flat;
            btnCancel.FlatAppearance.BorderSize = 0;
            btnCancel.Cursor = Cursors.Hand;
            btnCancel.Click += (s, e) => this.DialogResult = DialogResult.Cancel;
            panelButtons.Controls.Add(btnCancel);
        }

        private void LoadRecordData()
        {
            try
            {
                string sql = "SELECT * FROM inbound_transactions WHERE id = @id";
                DataTable dt = db.ExecuteQuery(sql, new Dictionary<string, object> { { "@id", recordId } });

                if (dt.Rows.Count > 0)
                {
                    DataRow row = dt.Rows[0];
                    txtOrderNo.Text = row["order_no"].ToString();
                    dtpDate.Value = Convert.ToDateTime(row["date"]);
                    txtHandler.Text = row["handler"].ToString();
                    txtProductType.Text = row["spec"].ToString();
                    nudQuantity.Value = Convert.ToDecimal(row["quantity"]);
                    nudUnitPrice.Value = Convert.ToDecimal(row["unit_price"]);
                    txtLocation.Text = row["location"].ToString();
                    
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"加载记录失败：{ex.Message}", "错误",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void BtnSave_Click(object sender, EventArgs e)
        {
            // 验证输入
            if (string.IsNullOrWhiteSpace(txtHandler.Text))
            {
                MessageBox.Show("请输入经手人！", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                txtHandler.Focus();
                return;
            }

            if (string.IsNullOrWhiteSpace(txtProductType.Text))
            {
                MessageBox.Show("请输入商品型号！", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                txtProductType.Focus();
                return;
            }

            if (nudQuantity.Value <= 0)
            {
                MessageBox.Show("请输入有效的数量！", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                nudQuantity.Focus();
                return;
            }

            try
            {
                // 计算金额
                decimal quantity = nudQuantity.Value;
                decimal unitPrice = nudUnitPrice.Value;
                decimal amount = quantity * unitPrice;

                string sql = @"UPDATE inbound_transactions SET 
                    transaction_date = @transaction_date,
                    handler = @handler,
                    product_type = @product_type,
                    quantity = @quantity,
                    unit_price = @unit_price,
                    location = @location,
                    notes = @notes,
                    amount = @amount
                    WHERE id = @id";

                db.ExecuteNonQuery(sql, new Dictionary<string, object>
                {
                    { "@transaction_date", dtpDate.Value },
                    { "@handler", txtHandler.Text.Trim() },
                    { "@product_type", txtProductType.Text.Trim() },
                    { "@quantity", quantity },
                    { "@unit_price", unitPrice },
                    { "@location", txtLocation.Text.Trim() },
                    { "@notes", txtNotes.Text.Trim() },
                    { "@amount", amount },
                    { "@id", recordId }
                });

                MessageBox.Show("记录更新成功！", "成功",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                this.DialogResult = DialogResult.OK;
                this.Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"保存失败：{ex.Message}", "错误",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private Label AddLabel(string text, int x, int y, int width, Panel parent)
        {
            Label label = new Label();
            label.Text = text;
            label.Location = new Point(x, y + 3);
            label.Size = new Size(width, 25);
            label.TextAlign = ContentAlignment.MiddleRight;
            label.Font = new Font("微软雅黑", 10);
            parent.Controls.Add(label);
            return label;
        }

        private TextBox AddTextBox(int x, int y, int width, Panel parent)
        {
            TextBox textBox = new TextBox();
            textBox.Location = new Point(x, y);
            textBox.Size = new Size(width, 25);
            textBox.Font = new Font("微软雅黑", 9);
            parent.Controls.Add(textBox);
            return textBox;
        }

        private NumericUpDown AddNumericUpDown(int x, int y, int width, Panel parent)
        {
            NumericUpDown nud = new NumericUpDown();
            nud.Location = new Point(x, y);
            nud.Size = new Size(width, 25);
            nud.Minimum = 0;
            nud.Maximum = 999999;
            nud.Font = new Font("微软雅黑", 9);
            parent.Controls.Add(nud);
            return nud;
        }
    }
}