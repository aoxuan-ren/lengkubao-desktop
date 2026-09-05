using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;

namespace lengkubao.desktop
{
    /// <summary>
    /// 客户对账导出前：对单价为 0 的包装类型提示补价或忽略。
    /// </summary>
    public sealed class PackagingZeroPricePromptForm : Form
    {
        public enum PromptAction
        {
            Cancelled,
            Ignored,
            Confirmed
        }

        public PromptAction UserAction { get; private set; } = PromptAction.Cancelled;

        /// <summary>用户确认填写的包装类型单价（仅含有效单价）。</summary>
        public IReadOnlyList<PackTypePriceInput> ConfirmedPrices { get; private set; }
            = Array.Empty<PackTypePriceInput>();

        private readonly DataGridView _grid;
        private readonly List<PackTypePriceInput> _items;

        public PackagingZeroPricePromptForm(IEnumerable<PackTypePriceInput> zeroPriceItems)
        {
            _items = new List<PackTypePriceInput>();
            if (zeroPriceItems != null)
            {
                foreach (var item in zeroPriceItems)
                {
                    if (item == null || string.IsNullOrWhiteSpace(item.PackType))
                        continue;
                    _items.Add(new PackTypePriceInput
                    {
                        PackType = item.PackType.Trim(),
                        TotalQuantity = item.TotalQuantity,
                        UnitPrice = 0m
                    });
                }
            }

            Text = "包装单价提醒";
            Size = new Size(520, 420);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            BackColor = Color.White;

            var lblMessage = new Label
            {
                Text = "以下包装类型单价为 0，是否添加单价？\n添加后按包装记账规则重算金额（数量×单价，进包装为负）。",
                Location = new Point(20, 16),
                Size = new Size(460, 44),
                Font = new Font("微软雅黑", 10f, FontStyle.Bold),
                ForeColor = Color.FromArgb(192, 57, 43)
            };
            Controls.Add(lblMessage);

            _grid = new DataGridView
            {
                Location = new Point(20, 70),
                Size = new Size(460, 240),
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                RowHeadersVisible = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                BackgroundColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                EditMode = DataGridViewEditMode.EditOnEnter,
                MultiSelect = false
            };
            Controls.Add(_grid);
            BindGrid();

            var btnConfirm = new Button
            {
                Text = "确认添加",
                Location = new Point(170, 330),
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
                Location = new Point(280, 330),
                Size = new Size(100, 34),
                BackColor = Color.FromArgb(52, 152, 219),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat
            };
            btnIgnore.FlatAppearance.BorderSize = 0;
            btnIgnore.Click += BtnIgnore_Click;
            Controls.Add(btnIgnore);

            var btnCancel = new Button
            {
                Text = "取消导出",
                Location = new Point(390, 330),
                Size = new Size(90, 34),
                BackColor = Color.FromArgb(149, 165, 166),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat
            };
            btnCancel.FlatAppearance.BorderSize = 0;
            btnCancel.Click += (s, e) =>
            {
                UserAction = PromptAction.Cancelled;
                DialogResult = DialogResult.Cancel;
                Close();
            };
            Controls.Add(btnCancel);

            AcceptButton = btnConfirm;
            CancelButton = btnCancel;
        }

        private void BindGrid()
        {
            var table = new System.Data.DataTable();
            table.Columns.Add("包装类型", typeof(string));
            table.Columns.Add("数量合计", typeof(decimal));
            table.Columns.Add("单价", typeof(string));

            foreach (var item in _items)
            {
                table.Rows.Add(item.PackType, item.TotalQuantity, string.Empty);
            }

            _grid.DataSource = table;
            _grid.Columns["包装类型"].ReadOnly = true;
            _grid.Columns["数量合计"].ReadOnly = true;
            _grid.Columns["数量合计"].DefaultCellStyle.Format = "N0";
            _grid.Columns["数量合计"].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
            _grid.Columns["单价"].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
            _grid.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
            _grid.ColumnHeadersDefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
        }

        private void BtnConfirm_Click(object sender, EventArgs e)
        {
            _grid.EndEdit();

            var prices = new List<PackTypePriceInput>();
            foreach (DataGridViewRow row in _grid.Rows)
            {
                if (row.IsNewRow) continue;

                string packType = row.Cells["包装类型"].Value?.ToString()?.Trim() ?? "";
                if (string.IsNullOrEmpty(packType)) continue;

                string priceText = row.Cells["单价"].Value?.ToString()?.Trim() ?? "";
                if (string.IsNullOrWhiteSpace(priceText))
                    continue;

                if (!decimal.TryParse(priceText, NumberStyles.Number, CultureInfo.CurrentCulture, out decimal price)
                    && !decimal.TryParse(priceText, NumberStyles.Number, CultureInfo.InvariantCulture, out price))
                {
                    MessageBox.Show($"包装类型 [{packType}] 的单价格式无效。", "提示",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                if (price <= 0)
                {
                    MessageBox.Show($"包装类型 [{packType}] 的单价必须大于 0。", "提示",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                decimal qty = 0m;
                if (row.Cells["数量合计"].Value != null && row.Cells["数量合计"].Value != DBNull.Value)
                    decimal.TryParse(row.Cells["数量合计"].Value.ToString(), out qty);

                prices.Add(new PackTypePriceInput
                {
                    PackType = packType,
                    TotalQuantity = qty,
                    UnitPrice = price
                });
            }

            if (prices.Count == 0)
            {
                MessageBox.Show("请至少填写一种包装类型的单价，或点击「忽略」。", "提示",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            ConfirmedPrices = prices;
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

        public sealed class PackTypePriceInput
        {
            public string PackType { get; set; }
            public decimal TotalQuantity { get; set; }
            public decimal UnitPrice { get; set; }
        }
    }
}
