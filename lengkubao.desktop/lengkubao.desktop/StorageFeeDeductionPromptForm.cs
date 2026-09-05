using System;
using System.Drawing;
using System.Windows.Forms;

namespace lengkubao.desktop
{
    public sealed class StorageFeeDeductionPromptForm : Form
    {
        public enum PromptAction
        {
            Cancelled,
            Ignored,
            Confirmed
        }

        public PromptAction UserAction { get; private set; } = PromptAction.Cancelled;
        public decimal UnitPrice { get; private set; }

        private readonly int _totalQuantity;
        private readonly TextBox _txtUnitPrice;
        private readonly Label _lblAmountPreview;

        public StorageFeeDeductionPromptForm(int totalQuantity)
        {
            _totalQuantity = totalQuantity;

            Text = "制冷费扣款提醒";
            Size = new Size(420, 280);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            BackColor = Color.White;

            var lblMessage = new Label
            {
                Text = "请添加制冷费扣款，已扣款请忽略！",
                Location = new Point(24, 20),
                Size = new Size(360, 24),
                Font = new Font("微软雅黑", 11f, FontStyle.Bold),
                ForeColor = Color.FromArgb(192, 57, 43)
            };
            Controls.Add(lblMessage);

            AddFieldLabel("总件数:", 24, 62);
            var txtTotalQuantity = new TextBox
            {
                Text = totalQuantity.ToString("N0"),
                Location = new Point(120, 58),
                Size = new Size(260, 25),
                ReadOnly = true,
                BackColor = Color.WhiteSmoke,
                TextAlign = HorizontalAlignment.Right
            };
            Controls.Add(txtTotalQuantity);

            AddFieldLabel("制冷费单价:", 24, 102);
            _txtUnitPrice = new TextBox
            {
                Location = new Point(120, 98),
                Size = new Size(260, 25),
                TextAlign = HorizontalAlignment.Right
            };
            NumericFieldHelper.BindUnitPriceField(_txtUnitPrice);
            _txtUnitPrice.TextChanged += (s, e) => UpdateAmountPreview();
            Controls.Add(_txtUnitPrice);

            _lblAmountPreview = new Label
            {
                Text = "扣款金额：0.00 元",
                Location = new Point(120, 132),
                Size = new Size(260, 22),
                Font = new Font("微软雅黑", 9f, FontStyle.Bold),
                ForeColor = Color.FromArgb(0, 102, 102)
            };
            Controls.Add(_lblAmountPreview);

            var btnConfirm = new Button
            {
                Text = "确认",
                Location = new Point(120, 178),
                Size = new Size(100, 34),
                BackColor = Color.FromArgb(46, 204, 113),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat
            };
            btnConfirm.FlatAppearance.BorderSize = 0;
            btnConfirm.Click += BtnConfirm_Click;
            Controls.Add(btnConfirm);

            var btnIgnore = new Button
            {
                Text = "忽略",
                Location = new Point(230, 178),
                Size = new Size(100, 34),
                BackColor = Color.FromArgb(52, 152, 219),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat
            };
            btnIgnore.FlatAppearance.BorderSize = 0;
            btnIgnore.Click += BtnIgnore_Click;
            Controls.Add(btnIgnore);

            CancelButton = btnIgnore;
            AcceptButton = btnConfirm;
        }

        private void AddFieldLabel(string text, int x, int y)
        {
            Controls.Add(new Label
            {
                Text = text,
                Location = new Point(x, y + 4),
                Size = new Size(88, 20),
                Font = new Font("微软雅黑", 9f)
            });
        }

        private void UpdateAmountPreview()
        {
            if (!NumericFieldHelper.TryGetDecimal(_txtUnitPrice, out decimal unitPrice))
            {
                _lblAmountPreview.Text = "扣款金额：0.00 元";
                return;
            }

            decimal amount = _totalQuantity * unitPrice;
            _lblAmountPreview.Text = $"扣款金额：{amount:N2} 元";
        }

        private void BtnConfirm_Click(object sender, EventArgs e)
        {
            if (_totalQuantity <= 0)
            {
                MessageBox.Show("当前总件数为 0，无法添加制冷费扣款。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (!NumericFieldHelper.TryGetDecimal(_txtUnitPrice, out decimal unitPrice) || unitPrice <= 0)
            {
                MessageBox.Show("请输入有效的制冷费单价。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                _txtUnitPrice.Focus();
                return;
            }

            UnitPrice = unitPrice;
            UserAction = PromptAction.Confirmed;
            DialogResult = DialogResult.OK;
            Close();
        }

        private void BtnIgnore_Click(object sender, EventArgs e)
        {
            UserAction = PromptAction.Ignored;
            DialogResult = DialogResult.OK;
            Close();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (UserAction == PromptAction.Cancelled && DialogResult != DialogResult.OK)
            {
                UserAction = PromptAction.Cancelled;
            }

            base.OnFormClosing(e);
        }
    }
}
