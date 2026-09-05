using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace lengkubao.desktop
{
    public class CustomerEditForm : Form
    {
        private TextBox txtCode;
        private TextBox txtName;
        private TextBox txtContact;
        private TextBox txtPhone;
        private TextBox txtAddress;
        private Button btnSave;
        private Button btnCancel;

        private int? editId = null;
        private DatabaseManager dbManager;
        private bool isEditMode = false;

        // 第7步：添加新属性
        public bool SaveSuccess { get; private set; } = false;
        public string SavedCode { get; private set; } = "";
        public string SavedName { get; private set; } = "";
        public string SavedPhone { get; private set; } = "";
        public string SavedAddress { get; private set; } = "";

        public CustomerEditForm()
        {
            dbManager = new DatabaseManager();
            isEditMode = false;
            InitializeForm("新增客户");

            // 直接调用 DatabaseManager 的方法
            txtCode.Text = dbManager.GenerateClientCode();
        }

        public CustomerEditForm(int id, string code, string name, string contact, string phone, string address)
        {
            dbManager = new DatabaseManager();
            editId = id;
            isEditMode = true;
            InitializeForm("编辑客户");
            txtCode.Text = code;
            txtName.Text = name;
            txtContact.Text = contact;
            txtPhone.Text = phone;
            txtAddress.Text = address;
        }

        private void InitializeForm(string title)
        {
            this.Text = title;
            this.Size = new Size(450, 320);
            this.StartPosition = FormStartPosition.CenterParent;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;

            // 客户编号
            Label lblCode = new Label();
            lblCode.Text = "客户编号:";
            lblCode.Location = new Point(20, 20);
            lblCode.Size = new Size(80, 20);
            lblCode.Font = new Font("微软雅黑", 9);

            txtCode = new TextBox();
            txtCode.Location = new Point(100, 20);
            txtCode.Size = new Size(300, 25);
            txtCode.Font = new Font("微软雅黑", 9);
            if (!isEditMode) txtCode.ReadOnly = true;

            // 客户名称
            Label lblName = new Label();
            lblName.Text = "客户名称:";
            lblName.Location = new Point(20, 60);
            lblName.Size = new Size(80, 20);
            lblName.Font = new Font("微软雅黑", 9);

            txtName = new TextBox();
            txtName.Location = new Point(100, 60);
            txtName.Size = new Size(300, 25);
            txtName.Font = new Font("微软雅黑", 9);

            // 联系人
            Label lblContact = new Label();
            lblContact.Text = "联系人:";
            lblContact.Location = new Point(20, 100);
            lblContact.Size = new Size(80, 20);
            lblContact.Font = new Font("微软雅黑", 9);

            txtContact = new TextBox();
            txtContact.Location = new Point(100, 100);
            txtContact.Size = new Size(300, 25);
            txtContact.Font = new Font("微软雅黑", 9);

            // 电话
            Label lblPhone = new Label();
            lblPhone.Text = "电话:";
            lblPhone.Location = new Point(20, 140);
            lblPhone.Size = new Size(80, 20);
            lblPhone.Font = new Font("微软雅黑", 9);

            txtPhone = new TextBox();
            txtPhone.Location = new Point(100, 140);
            txtPhone.Size = new Size(300, 25);
            txtPhone.Font = new Font("微软雅黑", 9);

            // 地址
            Label lblAddress = new Label();
            lblAddress.Text = "地址:";
            lblAddress.Location = new Point(20, 180);
            lblAddress.Size = new Size(80, 20);
            lblAddress.Font = new Font("微软雅黑", 9);

            txtAddress = new TextBox();
            txtAddress.Location = new Point(100, 180);
            txtAddress.Size = new Size(300, 60);
            txtAddress.Multiline = true;
            txtAddress.ScrollBars = ScrollBars.Vertical;
            txtAddress.Font = new Font("微软雅黑", 9);

            // 按钮
            btnSave = new Button();
            btnSave.Text = "保存";
            btnSave.Location = new Point(240, 250);
            btnSave.Size = new Size(80, 35);
            btnSave.BackColor = Color.FromArgb(46, 204, 113);
            btnSave.ForeColor = Color.White;
            btnSave.Font = new Font("微软雅黑", 10);
            btnSave.FlatStyle = FlatStyle.Flat;

            btnCancel = new Button();
            btnCancel.Text = "取消";
            btnCancel.Location = new Point(330, 250);
            btnCancel.Size = new Size(80, 35);
            btnCancel.BackColor = Color.FromArgb(149, 165, 166);
            btnCancel.ForeColor = Color.White;
            btnCancel.Font = new Font("微软雅黑", 10);
            btnCancel.FlatStyle = FlatStyle.Flat;
            btnCancel.DialogResult = DialogResult.Cancel;

            // 添加控件
            this.Controls.AddRange(new Control[] {
                lblCode, txtCode,
                lblName, txtName,
                lblContact, txtContact,
                lblPhone, txtPhone,
                lblAddress, txtAddress,
                btnSave, btnCancel
            });

            // 绑定事件
            btnSave.Click += BtnSave_Click;

            // 设置回车和ESC键
            this.AcceptButton = btnSave;
            this.CancelButton = btnCancel;
        }

        
        // 修改 CustomerEditForm 的保存方法，添加详细日志
        private void BtnSave_Click(object sender, EventArgs e)
        {
            try
            {
                Console.WriteLine("=== 开始保存客户 ===");

                string code = txtCode.Text.Trim();
                string name = txtName.Text.Trim();
                string contact = txtContact.Text.Trim();
                string phone = txtPhone.Text.Trim();
                string address = txtAddress.Text.Trim();

                Console.WriteLine($"保存信息 - 编号: '{code}', 名称: '{name}'");
                Console.WriteLine($"联系人: '{contact}', 电话: '{phone}', 地址: '{address}'");

                // 验证
                if (string.IsNullOrEmpty(name))
                {
                    MessageBox.Show("客户名称不能为空！", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    txtName.Focus();
                    return;
                }

                if (string.IsNullOrEmpty(code))
                {
                    MessageBox.Show("客户编号不能为空！", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    txtCode.Focus();
                    return;
                }

                bool success;

                if (isEditMode)
                {
                    success = dbManager.UpdateClient(code, name, contact, phone, address);
                    Console.WriteLine($"更新结果: 成功={success}");
                }
                else
                {
                    success = dbManager.AddClient(code, name, contact, phone, address);
                    Console.WriteLine($"插入结果: 成功={success}");

                    if (!success)
                    {
                        MessageBox.Show("客户编号已存在，请使用不同的编号！", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        txtCode.Focus();
                        txtCode.SelectAll();
                        return;
                    }
                }

                if (success)
                {
                    // 保存成功，设置返回属性
                    SaveSuccess = true;
                    SavedCode = code;
                    SavedName = name;
                    SavedPhone = phone;
                    SavedAddress = address;

                    Console.WriteLine($"保存成功！设置返回属性: Code={SavedCode}, Name={SavedName}");

                    MessageBox.Show("保存成功！", "成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    this.DialogResult = DialogResult.OK;
                    this.Close();
                }
                else
                {
                    SaveSuccess = false;
                    string errorMsg = isEditMode ? "客户信息更新失败" : "客户添加失败，可能是编号重复";
                    Console.WriteLine($"保存失败: {errorMsg}");
                    MessageBox.Show($"保存失败: {errorMsg}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }

                Console.WriteLine("=== 保存结束 ===");
            }
            catch (Exception ex)
            {
                SaveSuccess = false;
                Console.WriteLine($"保存异常: {ex.Message}\n{ex.StackTrace}");
                MessageBox.Show($"保存失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }


        // 公共属性
        public string CustomerCode => txtCode.Text.Trim();
        public string CustomerName => txtName.Text.Trim();
        public string Contact => txtContact.Text.Trim();
        public string Phone => txtPhone.Text.Trim();
        public string Address => txtAddress.Text.Trim();
        public int? EditId => editId;
    }
}