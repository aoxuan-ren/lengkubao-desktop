using System;
using System.Drawing;
using System.Windows.Forms;

namespace lengkubao.desktop
{
    public sealed class LicenseActivationForm : Form
    {
        private readonly TextBox _signatureBox;
        private readonly Label _machineLabel;

        public LicenseActivationForm()
        {
            Text = "冷库宝 - 软件激活";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(480, 260);
            Font = new Font("Microsoft YaHei UI", 9F);

            var title = new Label
            {
                Text = "请输入安装授权码以激活软件",
                AutoSize = true,
                Location = new Point(24, 20)
            };

            var machineCaption = new Label
            {
                Text = "机器码：",
                AutoSize = true,
                Location = new Point(24, 56)
            };

            string machineId = LicenseManager.GetMachineId();
            _machineLabel = new Label
            {
                Text = machineId,
                AutoSize = true,
                Location = new Point(88, 56),
                Font = new Font("Consolas", 10F, FontStyle.Bold)
            };

            var copyBtn = new Button
            {
                Text = "复制机器码",
                Location = new Point(360, 52),
                Size = new Size(96, 28)
            };
            copyBtn.Click += (s, e) =>
            {
                Clipboard.SetText(machineId);
                MessageBox.Show("机器码已复制。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            };

            var sigCaption = new Label
            {
                Text = "授权码：",
                AutoSize = true,
                Location = new Point(24, 100)
            };

            _signatureBox = new TextBox
            {
                Location = new Point(24, 124),
                Size = new Size(432, 80),
                Multiline = true,
                ScrollBars = ScrollBars.Vertical
            };

            var activateBtn = new Button
            {
                Text = "激活",
                DialogResult = DialogResult.None,
                Location = new Point(280, 214),
                Size = new Size(80, 32)
            };
            activateBtn.Click += ActivateBtn_Click;

            var exitBtn = new Button
            {
                Text = "退出",
                DialogResult = DialogResult.Cancel,
                Location = new Point(376, 214),
                Size = new Size(80, 32)
            };

            Controls.AddRange(new Control[]
            {
                title, machineCaption, _machineLabel, copyBtn,
                sigCaption, _signatureBox, activateBtn, exitBtn
            });

            AcceptButton = activateBtn;
            CancelButton = exitBtn;
        }

        private void ActivateBtn_Click(object sender, EventArgs e)
        {
            string machineId = LicenseManager.GetMachineId();
            string signature = _signatureBox.Text;

            if (!LicenseManager.TryVerifyAndParse(machineId, signature, out LicenseManager.LicenseInfo info))
            {
                MessageBox.Show(
                    "授权码无效，请核对机器码后联系管理员获取正确授权码。",
                    "激活失败",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                return;
            }

            LicenseManager.SaveLicense(machineId, signature, info);

            if (info.Kind == LicenseManager.LicenseKind.Trial && info.ExpiresAt.HasValue)
            {
                MessageBox.Show(
                    "试用授权已激活。" + Environment.NewLine +
                    "到期时间：" + info.ExpiresAt.Value.ToString("yyyy-MM-dd HH:mm:ss") + Environment.NewLine +
                    "到期后需重新输入授权码。",
                    "激活成功",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }

            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
