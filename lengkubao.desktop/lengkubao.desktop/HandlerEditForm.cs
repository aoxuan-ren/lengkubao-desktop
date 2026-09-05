using System;
using System.Drawing;
using System.Windows.Forms;

namespace lengkubao.desktop
{
    public class HandlerEditForm : Form
    {
        private TextBox txtName;
        private TextBox txtPhone;
        private Button btnSave;
        private Button btnCancel;

        private int? editId = null;
        private DatabaseManager dbManager;

        public HandlerEditForm()
        {
            dbManager = new DatabaseManager();
            InitializeForm("新增经手人");
        }

        public HandlerEditForm(int id, string name, string phone)
        {
            dbManager = new DatabaseManager();
            editId = id;
            InitializeForm("编辑经手人");
            txtName.Text = name;
            txtPhone.Text = phone;
        }

        private void InitializeForm(string title)
        {
            this.Text = title;
            this.Size = new Size(400, 200);
            this.StartPosition = FormStartPosition.CenterParent;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;

            // 姓名标签和文本框
            Label lblName = new Label();
            lblName.Text = "姓名:";
            lblName.Location = new Point(20, 20);
            lblName.Size = new Size(60, 20);
            lblName.Font = new Font("微软雅黑", 9);

            txtName = new TextBox();
            txtName.Location = new Point(80, 20);
            txtName.Size = new Size(280, 20);
            txtName.MaxLength = 50;
            txtName.Font = new Font("微软雅黑", 9);

            // 电话标签和文本框
            Label lblPhone = new Label();
            lblPhone.Text = "电话:";
            lblPhone.Location = new Point(20, 60);
            lblPhone.Size = new Size(60, 20);
            lblPhone.Font = new Font("微软雅黑", 9);

            txtPhone = new TextBox();
            txtPhone.Location = new Point(80, 60);
            txtPhone.Size = new Size(280, 20);
            txtPhone.MaxLength = 20;
            txtPhone.Font = new Font("微软雅黑", 9);

            // 按钮
            btnSave = new Button();
            btnSave.Text = "保存";
            btnSave.Location = new Point(200, 100);
            btnSave.Size = new Size(80, 30);
            btnSave.BackColor = Color.FromArgb(0, 123, 255);
            btnSave.ForeColor = Color.White;
            btnSave.FlatStyle = FlatStyle.Flat;
            btnSave.Font = new Font("微软雅黑", 9);

            btnCancel = new Button();
            btnCancel.Text = "取消";
            btnCancel.Location = new Point(290, 100);
            btnCancel.Size = new Size(80, 30);
            btnCancel.BackColor = Color.FromArgb(108, 117, 125);
            btnCancel.ForeColor = Color.White;
            btnCancel.FlatStyle = FlatStyle.Flat;
            btnCancel.Font = new Font("微软雅黑", 9);
            btnCancel.DialogResult = DialogResult.Cancel;

            // 添加控件
            this.Controls.AddRange(new Control[] {
                lblName, txtName, lblPhone, txtPhone, btnSave, btnCancel
            });

            // 绑定事件
            btnSave.Click += BtnSave_Click;

            // 设置回车键默认按钮
            this.AcceptButton = btnSave;
            this.CancelButton = btnCancel;
        }

        private void BtnSave_Click(object sender, EventArgs e)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(txtName.Text))
                {
                    MessageBox.Show("请输入经手人姓名！", "提示",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    txtName.Focus();
                    return;
                }

                string name = txtName.Text.Trim();
                string phone = txtPhone.Text.Trim();

                bool success;
                DatabaseManager dbManager = new DatabaseManager();

                // 根据是新增还是编辑模式执行不同操作
                if (editId.HasValue)
                {
                    // 编辑模式 - 使用基于ID的更新
                    success = dbManager.UpdateHandlerById(editId.Value, name, phone, true);
                    if (success)
                    {
                        MessageBox.Show("经手人更新成功！", "成功",
                            MessageBoxButtons.OK, MessageBoxIcon.Information);
                        this.DialogResult = DialogResult.OK;
                        this.Close();
                    }
                    else
                    {
                        MessageBox.Show("经手人更新失败！", "错误",
                            MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }
                else
                {
                    // 新增模式
                    success = dbManager.AddHandler(name);
                    if (success)
                    {
                        MessageBox.Show("经手人添加成功！", "成功",
                            MessageBoxButtons.OK, MessageBoxIcon.Information);
                        this.DialogResult = DialogResult.OK;
                        this.Close();
                    }
                    else
                    {
                        MessageBox.Show("经手人添加失败！", "错误",
                            MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }
            }
            catch (Exception ex)  // 添加 catch 块
            {
                MessageBox.Show($"操作失败: {ex.Message}", "错误",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}