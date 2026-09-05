using System;
using System.Collections.Generic;
using System.Data;
using System.Drawing;
using System.Windows.Forms;

namespace lengkubao.desktop
{
    public partial class ClientEditForm : Form
    {
        private TextBox txtCode;
        private TextBox txtName;
        private TextBox txtPhone;
        private TextBox txtAddress;
        private TextBox txtContact;
        private bool isEditMode = false;
        private string originalCode = "";

        public ClientEditForm(string clientCode = null)
        {
            isEditMode = !string.IsNullOrEmpty(clientCode);
            originalCode = clientCode ?? "";

            InitializeCustomComponents();

            if (isEditMode)
            {
                LoadClientData();
            }
            else
            {
                GenerateClientCode();
            }
        }

        private void InitializeCustomComponents()
        {
            this.Text = isEditMode ? "编辑客户" : "新增客户";
            this.Size = new Size(500, 400);
            this.StartPosition = FormStartPosition.CenterParent;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;

            // 标题
            Label lblTitle = new Label();
            lblTitle.Text = this.Text;
            lblTitle.Font = new Font("微软雅黑", 14, FontStyle.Bold);
            lblTitle.ForeColor = Color.FromArgb(52, 152, 219);
            lblTitle.Size = new Size(200, 30);
            lblTitle.Location = new Point(20, 10);
            this.Controls.Add(lblTitle);

            // 客户编号
            Label lblCode = new Label();
            lblCode.Text = "客户编号:";
            lblCode.Font = new Font("微软雅黑", 10);
            lblCode.Location = new Point(30, 60);
            lblCode.Size = new Size(80, 25);
            this.Controls.Add(lblCode);

            txtCode = new TextBox();
            txtCode.Font = new Font("微软雅黑", 10);
            txtCode.Location = new Point(120, 57);
            txtCode.Size = new Size(150, 25);
            txtCode.ReadOnly = true; // 编号自动生成，不可修改
            txtCode.BackColor = Color.FromArgb(240, 240, 240);
            this.Controls.Add(txtCode);

            // 客户名称
            Label lblName = new Label();
            lblName.Text = "客户名称:";
            lblName.Font = new Font("微软雅黑", 10);
            lblName.Location = new Point(30, 100);
            lblName.Size = new Size(80, 25);
            this.Controls.Add(lblName);

            txtName = new TextBox();
            txtName.Font = new Font("微软雅黑", 10);
            txtName.Location = new Point(120, 97);
            txtName.Size = new Size(250, 25);
            txtName.MaxLength = 50;
            this.Controls.Add(txtName);

            // 联系人
            Label lblContact = new Label();
            lblContact.Text = "联系人:";
            lblContact.Font = new Font("微软雅黑", 10);
            lblContact.Location = new Point(30, 140);
            lblContact.Size = new Size(80, 25);
            this.Controls.Add(lblContact);

            txtContact = new TextBox();
            txtContact.Font = new Font("微软雅黑", 10);
            txtContact.Location = new Point(120, 137);
            txtContact.Size = new Size(150, 25);
            this.Controls.Add(txtContact);

            // 联系电话
            Label lblPhone = new Label();
            lblPhone.Text = "联系电话:";
            lblPhone.Font = new Font("微软雅黑", 10);
            lblPhone.Location = new Point(30, 180);
            lblPhone.Size = new Size(80, 25);
            this.Controls.Add(lblPhone);

            txtPhone = new TextBox();
            txtPhone.Font = new Font("微软雅黑", 10);
            txtPhone.Location = new Point(120, 177);
            txtPhone.Size = new Size(150, 25);
            txtPhone.MaxLength = 20;
            this.Controls.Add(txtPhone);

            // 地址
            Label lblAddress = new Label();
            lblAddress.Text = "地址:";
            lblAddress.Font = new Font("微软雅黑", 10);
            lblAddress.Location = new Point(30, 220);
            lblAddress.Size = new Size(80, 25);
            this.Controls.Add(lblAddress);

            txtAddress = new TextBox();
            txtAddress.Font = new Font("微软雅黑", 10);
            txtAddress.Location = new Point(120, 217);
            txtAddress.Size = new Size(250, 25);
            txtAddress.MaxLength = 100;
            this.Controls.Add(txtAddress);

            // 保存按钮
            Button btnSave = new Button();
            btnSave.Text = "💾 保存";
            btnSave.Font = new Font("微软雅黑", 10, FontStyle.Bold);
            btnSave.BackColor = Color.FromArgb(46, 204, 113);
            btnSave.ForeColor = Color.White;
            btnSave.FlatStyle = FlatStyle.Flat;
            btnSave.Size = new Size(120, 40);
            btnSave.Location = new Point(150, 270);
            btnSave.Click += BtnSave_Click;
            this.Controls.Add(btnSave);

            // 取消按钮
            Button btnCancel = new Button();
            btnCancel.Text = "取消";
            btnCancel.Font = new Font("微软雅黑", 10);
            btnCancel.BackColor = Color.FromArgb(149, 165, 166);
            btnCancel.ForeColor = Color.White;
            btnCancel.FlatStyle = FlatStyle.Flat;
            btnCancel.Size = new Size(100, 40);
            btnCancel.Location = new Point(280, 270);
            btnCancel.Click += (s, e) => this.DialogResult = DialogResult.Cancel;
            this.Controls.Add(btnCancel);

            // 设置Tab顺序
            txtName.TabIndex = 1;
            txtContact.TabIndex = 2;
            txtPhone.TabIndex = 3;
            txtAddress.TabIndex = 4;
            btnSave.TabIndex = 5;
            btnCancel.TabIndex = 6;
        }

        private void GenerateClientCode()
        {
            try
            {
                DatabaseManager db = new DatabaseManager();
                string newCode = db.GenerateClientCode();
                txtCode.Text = newCode;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"生成客户编号失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void LoadClientData()
        {
            try
            {
                if (string.IsNullOrEmpty(originalCode))
                    return;

                DatabaseManager db = new DatabaseManager();
                string sql = "SELECT code, name, contact, phone, address FROM clients WHERE code = @code";
                var dt = db.ExecuteQuery(sql, new Dictionary<string, object>
                {
                    { "@code", originalCode }
                });

                if (dt.Rows.Count > 0)
                {
                    DataRow row = dt.Rows[0];
                    txtCode.Text = row["code"].ToString();
                    txtName.Text = row["name"].ToString();
                    txtContact.Text = row["contact"]?.ToString() ?? "";
                    txtPhone.Text = row["phone"]?.ToString() ?? "";
                    txtAddress.Text = row["address"]?.ToString() ?? "";
                }
                else
                {
                    MessageBox.Show("未找到客户信息！", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    this.DialogResult = DialogResult.Cancel;
                    this.Close();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"加载客户信息失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void BtnSave_Click(object sender, EventArgs e)
        {
            try
            {
                string code = txtCode.Text.Trim();
                string name = txtName.Text.Trim();

                if (string.IsNullOrEmpty(name))
                {
                    MessageBox.Show("客户名称不能为空！", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    txtName.Focus();
                    return;
                }

                if (name.Length > 50)
                {
                    MessageBox.Show("客户名称不能超过50个字符！", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    txtName.Focus();
                    txtName.SelectAll();
                    return;
                }

                DatabaseManager db = new DatabaseManager();
                bool success;

                if (isEditMode)
                {
                    success = db.UpdateClient(
                        code,
                        name,
                        txtContact.Text.Trim(),
                        txtPhone.Text.Trim(),
                        txtAddress.Text.Trim());
                }
                else
                {
                    success = db.AddClient(
                        code,
                        name,
                        txtContact.Text.Trim(),
                        txtPhone.Text.Trim(),
                        txtAddress.Text.Trim());
                }

                if (success)
                {
                    this.DialogResult = DialogResult.OK;
                    this.Close();
                }
                else
                {
                    MessageBox.Show("保存失败，请重试！", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"保存失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}