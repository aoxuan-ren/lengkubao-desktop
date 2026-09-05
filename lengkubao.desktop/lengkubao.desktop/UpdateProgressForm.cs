using System;
using System.Drawing;
using System.Windows.Forms;

namespace lengkubao.desktop
{
    internal sealed class UpdateProgressForm : Form
    {
        private readonly Label _statusLabel;
        private readonly ProgressBar _progressBar;

        public UpdateProgressForm()
        {
            Text = "冷库宝 - 正在更新";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ControlBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(420, 120);
            Font = new Font("Microsoft YaHei UI", 9F);

            _statusLabel = new Label
            {
                Text = "正在下载更新包，请稍候…",
                AutoSize = false,
                Location = new Point(24, 20),
                Size = new Size(372, 40)
            };

            _progressBar = new ProgressBar
            {
                Location = new Point(24, 68),
                Size = new Size(372, 24),
                Style = ProgressBarStyle.Continuous,
                Minimum = 0,
                Maximum = 100
            };

            Controls.Add(_statusLabel);
            Controls.Add(_progressBar);
        }

        public void ReportProgress(int percent, string status)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action<int, string>(ReportProgress), percent, status);
                return;
            }

            _progressBar.Value = Math.Max(0, Math.Min(100, percent));
            if (!string.IsNullOrWhiteSpace(status))
                _statusLabel.Text = status;
        }
    }
}
