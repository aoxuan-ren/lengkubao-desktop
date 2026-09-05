
using System;
using System.Drawing;
using System.Windows.Forms;

namespace lengkubao.desktop
{
    public class InputDialog : Form
    {
        private TextBox textBox;
        private Button btnOk;
        private Button btnCancel;

        public string InputText => textBox.Text;

        public InputDialog(string title, string prompt)
        {
            InitializeForm(title, prompt);
        }

        private void InitializeForm(string title, string prompt)
        {
            this.Text = title;
            this.Size = new Size(300, 150);
            this.StartPosition = FormStartPosition.CenterParent;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;

            // 提示标签
            Label lblPrompt = new Label();
            lblPrompt.Text = prompt;
            lblPrompt.Location = new Point(10, 20);
            lblPrompt.Size = new Size(280, 20);
            lblPrompt.Font = new Font("微软雅黑", 9);

            // 输入框
            textBox = new TextBox();
            textBox.Location = new Point(10, 50);
            textBox.Size = new Size(280, 20);
            textBox.Font = new Font("微软雅黑", 9);

            // 确定按钮
            btnOk = new Button();
            btnOk.Text = "确定";
            btnOk.Location = new Point(120, 80);
            btnOk.Size = new Size(80, 30);
            btnOk.DialogResult = DialogResult.OK;
            btnOk.BackColor = Color.FromArgb(0, 123, 255);
            btnOk.ForeColor = Color.White;

            // 取消按钮
            btnCancel = new Button();
            btnCancel.Text = "取消";
            btnCancel.Location = new Point(210, 80);
            btnCancel.Size = new Size(80, 30);
            btnCancel.DialogResult = DialogResult.Cancel;
            btnCancel.BackColor = Color.FromArgb(108, 117, 125);
            btnCancel.ForeColor = Color.White;

            // 添加控件
            this.Controls.Add(lblPrompt);
            this.Controls.Add(textBox);
            this.Controls.Add(btnOk);
            this.Controls.Add(btnCancel);

            // 设置回车和ESC键
            this.AcceptButton = btnOk;
            this.CancelButton = btnCancel;

            // 让输入框获得焦点
            textBox.Focus();
        }
    }
}