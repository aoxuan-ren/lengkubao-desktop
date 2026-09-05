using System;
using System.Collections.Generic;
using System.Data;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;

namespace lengkubao.desktop
{
    public class PackTypeBatchPriceDialog : Form
    {
        private readonly DatabaseManager db;
        private DataGridView grid;
        private DataTable sourceData;
        private Button btnSave;
        private Button btnCancel;

        public PackTypeBatchPriceDialog(DatabaseManager databaseManager)
        {
            db = databaseManager ?? new DatabaseManager();
            InitializeComponents();
            LoadData();
        }

        private void InitializeComponents()
        {
            Text = "批量修改包装单价";
            Size = new Size(480, 520);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            BackColor = Color.White;
            Font = QueryUiHelper.ChromeFont;

            var lblTitle = new Label
            {
                Text = "批量修改包装单价",
                Font = new Font("微软雅黑", 13f, FontStyle.Bold),
                ForeColor = Color.FromArgb(15, 118, 110),
                Location = new Point(20, 12),
                Size = new Size(440, 28),
                TextAlign = ContentAlignment.MiddleLeft
            };
            Controls.Add(lblTitle);

            var lblHint = new Label
            {
                Text = "仅更新「处理中」未结清记录；留空新单价表示不修改该类型。",
                ForeColor = Color.Gray,
                Location = new Point(20, 42),
                Size = new Size(440, 22)
            };
            Controls.Add(lblHint);

            grid = new DataGridView
            {
                Location = new Point(20, 70),
                Size = new Size(424, 360),
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                RowHeadersVisible = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                BackgroundColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                EditMode = DataGridViewEditMode.EditOnEnter
            };
            QueryUiHelper.ApplyQueryGrid(grid, QueryModule.Packaging);
            Controls.Add(grid);

            btnSave = QueryUiHelper.CreateButton("保存", QueryButtonRole.Primary, QueryUiHelper.DefaultButtonSize);
            btnSave.Location = new Point(260, 445);
            btnSave.Click += BtnSave_Click;
            Controls.Add(btnSave);

            btnCancel = QueryUiHelper.CreateButton("取消", QueryButtonRole.Secondary, QueryUiHelper.DefaultButtonSize);
            btnCancel.Location = new Point(360, 445);
            btnCancel.Click += (s, e) =>
            {
                DialogResult = DialogResult.Cancel;
                Close();
            };
            Controls.Add(btnCancel);

            CancelButton = btnCancel;
        }

        private void LoadData()
        {
            sourceData = db.GetPackTypePriceSummary();
            if (sourceData == null)
                sourceData = new DataTable();

            var displayTable = new DataTable();
            displayTable.Columns.Add("包装明细", typeof(string));
            displayTable.Columns.Add("当前单价", typeof(decimal));
            displayTable.Columns.Add("新单价", typeof(string));
            displayTable.Columns.Add("pending_count", typeof(int));

            foreach (DataRow row in sourceData.Rows)
            {
                displayTable.Rows.Add(
                    row["pack_type"],
                    row["current_unit_price"],
                    string.Empty,
                    row["pending_count"]);
            }

            grid.DataSource = displayTable;
            ConfigureGridColumns();
        }

        private void ConfigureGridColumns()
        {
            if (grid.Columns.Count == 0)
                return;

            grid.ReadOnly = false;
            grid.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
            grid.ColumnHeadersDefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;

            grid.Columns["包装明细"].ReadOnly = true;
            grid.Columns["当前单价"].ReadOnly = true;
            grid.Columns["当前单价"].DefaultCellStyle.Format = "N2";

            foreach (DataGridViewColumn column in grid.Columns)
            {
                if (!column.Visible)
                    continue;

                column.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
                column.HeaderCell.Style.Alignment = DataGridViewContentAlignment.MiddleCenter;
            }

            if (grid.Columns.Contains("pending_count"))
                grid.Columns["pending_count"].Visible = false;
        }

        private void BtnSave_Click(object sender, EventArgs e)
        {
            grid.EndEdit();

            var updates = new List<PriceUpdateItem>();
            foreach (DataGridViewRow row in grid.Rows)
            {
                if (row.IsNewRow)
                    continue;

                string packType = row.Cells["包装明细"].Value?.ToString()?.Trim();
                if (string.IsNullOrWhiteSpace(packType))
                    continue;

                decimal currentPrice = 0m;
                if (row.Cells["当前单价"].Value != null && row.Cells["当前单价"].Value != DBNull.Value)
                    decimal.TryParse(row.Cells["当前单价"].Value.ToString(), out currentPrice);

                string newPriceText = row.Cells["新单价"].Value?.ToString()?.Trim() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(newPriceText))
                    continue;

                if (!decimal.TryParse(newPriceText, NumberStyles.Number, CultureInfo.CurrentCulture, out decimal newPrice)
                    && !decimal.TryParse(newPriceText, NumberStyles.Number, CultureInfo.InvariantCulture, out newPrice))
                {
                    MessageBox.Show($"包装明细 [{packType}] 的新单价格式无效。", "提示",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                if (newPrice <= 0)
                {
                    MessageBox.Show($"包装明细 [{packType}] 的新单价必须大于 0。", "提示",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                if (newPrice == currentPrice)
                    continue;

                int pendingCount = 0;
                if (row.Cells["pending_count"].Value != null && row.Cells["pending_count"].Value != DBNull.Value)
                    int.TryParse(row.Cells["pending_count"].Value.ToString(), out pendingCount);

                updates.Add(new PriceUpdateItem
                {
                    PackType = packType,
                    NewPrice = newPrice,
                    PendingCount = pendingCount
                });
            }

            if (updates.Count == 0)
            {
                MessageBox.Show("没有需要更新的内容。", "提示",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            int totalRecords = 0;
            foreach (var item in updates)
                totalRecords += item.PendingCount;

            string confirmMessage = $"将更新 {updates.Count} 种包装明细";
            if (totalRecords > 0)
                confirmMessage += $"，共 {totalRecords} 条处理中记录";
            confirmMessage += "。\n\n已结清记录不会修改。确定继续吗？";

            if (MessageBox.Show(confirmMessage, "确认批量改价",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return;

            try
            {
                int totalUpdated = 0;
                foreach (var item in updates)
                    totalUpdated += db.BatchUpdatePackTypeUnitPrice(item.PackType, item.NewPrice);

                MessageBox.Show($"批量改价完成，共更新 {totalUpdated} 条记录。", "成功",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);

                DialogResult = DialogResult.OK;
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"批量改价失败: {ex.Message}", "错误",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private sealed class PriceUpdateItem
        {
            public string PackType { get; set; }
            public decimal NewPrice { get; set; }
            public int PendingCount { get; set; }
        }
    }
}
