using System;
using System.Collections.Generic;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace lengkubao.desktop
{
    public class PackagingForm : Form
    {
        private ComboBox cmbClient;
        private ComboBox cmbHandler;
        private ComboBox cmbPackFlag;
        private DataGridView gridItems;
        private Label lblItemSummary;
        private Label lblTotalAmount;
        private Button btnSave;
        private Button btnCancel;
        private List<ClientSearchItem> _clientItems = new List<ClientSearchItem>();
        private List<ClientSearchItem> _handlerItems = new List<ClientSearchItem>();
        private List<ClientSearchItem> _packFlagItems = new List<ClientSearchItem>();
        private List<PackagingTypeRow> _packagingRows = new List<PackagingTypeRow>();
        private Label _workspaceTitleLabel;
        private Panel _workspaceHeaderPanel;
        private Panel _workspaceItemsPanel;
        private Panel _workspaceFooterPanel;
        private const int WorkspaceLabelCol = 108;
        private const int WorkspaceRowSpacing = 44;
        private const int WorkspaceFieldHeight = 32;
        private const int ItemGridRowHeight = 34;

        public PackagingForm()
        {
            InitializeForm();
        }

        private void InitializeForm()
        {
            Text = "包装记账";
            Font = new Font("微软雅黑", 12f);
            Size = new Size(720, 680);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Color.FromArgb(240, 242, 245);
            FormBorderStyle = FormBorderStyle.Sizable;
            MaximizeBox = true;
            MinimizeBox = true;
            MinimumSize = new Size(560, 520);

            _workspaceTitleLabel = new Label
            {
                Text = "包装记账",
                Font = new Font("微软雅黑", 18f, FontStyle.Bold),
                ForeColor = Color.FromArgb(0, 102, 204),
                Height = 42,
                TextAlign = ContentAlignment.MiddleCenter
            };
            Controls.Add(_workspaceTitleLabel);

            _workspaceHeaderPanel = new Panel { BackColor = Color.Transparent };
            Controls.Add(_workspaceHeaderPanel);

            int yPos = 0;
            int spacing = WorkspaceRowSpacing;

            Label lblPackFlag = CreateFormLabel("出进类型:", 10, yPos);
            cmbPackFlag = CreateComboBox(WorkspaceLabelCol, yPos, 260);
            _workspaceHeaderPanel.Controls.Add(lblPackFlag);
            _workspaceHeaderPanel.Controls.Add(cmbPackFlag);
            yPos += spacing;

            Label lblClient = CreateFormLabel("客 户:", 10, yPos);
            cmbClient = CreateComboBox(WorkspaceLabelCol, yPos, 260);
            cmbClient.DropDownHeight = 250;
            _workspaceHeaderPanel.Controls.Add(lblClient);
            _workspaceHeaderPanel.Controls.Add(cmbClient);
            yPos += spacing;

            Label lblHandler = CreateFormLabel("经手人:", 10, yPos);
            cmbHandler = CreateComboBox(WorkspaceLabelCol, yPos, 260);
            _workspaceHeaderPanel.Controls.Add(lblHandler);
            _workspaceHeaderPanel.Controls.Add(cmbHandler);

            _workspaceItemsPanel = new Panel { BackColor = Color.White };
            Controls.Add(_workspaceItemsPanel);

            var lblItemsTitle = new Label
            {
                Text = "包装明细",
                Font = new Font("微软雅黑", 13f, FontStyle.Bold),
                ForeColor = Color.FromArgb(0, 102, 204),
                Location = new Point(8, 8),
                AutoSize = true
            };
            lblItemSummary = new Label
            {
                Text = "已选: 0 种",
                Font = new Font("微软雅黑", 10f),
                ForeColor = Color.FromArgb(0, 150, 136),
                Location = new Point(8, 36),
                AutoSize = true
            };
            var lblHint = new Label
            {
                Text = "填写各包装类型的数量（单价选填，未填默认为0），可一次录入多种",
                Font = new Font("微软雅黑", 9f),
                ForeColor = Color.Gray,
                Location = new Point(8, 58),
                AutoSize = true
            };
            lblTotalAmount = new Label
            {
                Text = "包装总金额: 0.00 元",
                Font = new Font("微软雅黑", 11f, FontStyle.Bold),
                ForeColor = Color.FromArgb(0, 102, 204),
                Location = new Point(8, 80),
                AutoSize = true
            };

            gridItems = CreateItemsGrid();
            gridItems.CellValueChanged += (s, e) => UpdateItemSummary();
            gridItems.DataError += (s, e) => e.Cancel = true;
            gridItems.CurrentCellDirtyStateChanged += (s, e) =>
            {
                if (gridItems.IsCurrentCellDirty)
                    gridItems.CommitEdit(DataGridViewDataErrorContexts.Commit);
            };

            _workspaceItemsPanel.Controls.Add(lblItemsTitle);
            _workspaceItemsPanel.Controls.Add(lblItemSummary);
            _workspaceItemsPanel.Controls.Add(lblHint);
            _workspaceItemsPanel.Controls.Add(lblTotalAmount);
            _workspaceItemsPanel.Controls.Add(gridItems);

            _workspaceFooterPanel = new Panel { BackColor = Color.Transparent };
            Controls.Add(_workspaceFooterPanel);

            btnSave = new Button
            {
                Text = "保 存",
                Size = new Size(128, 42),
                Location = new Point(60, 6),
                Font = new Font("微软雅黑", 12f, FontStyle.Bold),
                BackColor = Color.FromArgb(0, 150, 136),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand
            };
            btnSave.FlatAppearance.BorderSize = 0;
            btnSave.Click += BtnSave_Click;

            btnCancel = new Button
            {
                Text = "取 消",
                Size = new Size(128, 42),
                Location = new Point(190, 6),
                Font = new Font("微软雅黑", 12f),
                BackColor = Color.FromArgb(158, 158, 158),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand
            };
            btnCancel.FlatAppearance.BorderSize = 0;
            btnCancel.Click += BtnCancel_Click;

            _workspaceFooterPanel.Controls.Add(btnSave);
            _workspaceFooterPanel.Controls.Add(btnCancel);

            Resize -= PackagingForm_WorkspaceLayout;
            Resize += PackagingForm_WorkspaceLayout;
            PackagingForm_WorkspaceLayout(null, EventArgs.Empty);

            FormClosing += PackagingForm_FormClosing;
            LoadData();
        }

        private DataGridView CreateItemsGrid()
        {
            var grid = new DataGridView
            {
                AutoGenerateColumns = false,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                RowHeadersVisible = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                BackgroundColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing,
                ColumnHeadersHeight = 32,
                RowTemplate = { Height = ItemGridRowHeight },
                EditMode = DataGridViewEditMode.EditOnEnter
            };

            grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "PackType",
                HeaderText = "包装明细",
                DataPropertyName = "PackType",
                ReadOnly = true,
                FillWeight = 40
            });
            grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "Quantity",
                HeaderText = "数量",
                DataPropertyName = "QuantityDisplay",
                ReadOnly = false,
                FillWeight = 20,
                DefaultCellStyle = new DataGridViewCellStyle { Alignment = DataGridViewContentAlignment.MiddleCenter }
            });
            grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "UnitPrice",
                HeaderText = "单价",
                DataPropertyName = "UnitPriceDisplay",
                ReadOnly = false,
                FillWeight = 20,
                DefaultCellStyle = new DataGridViewCellStyle { Alignment = DataGridViewContentAlignment.MiddleCenter }
            });
            grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "Subtotal",
                HeaderText = "小计",
                DataPropertyName = "SubtotalDisplay",
                ReadOnly = true,
                FillWeight = 20,
                DefaultCellStyle = new DataGridViewCellStyle { Alignment = DataGridViewContentAlignment.MiddleCenter }
            });

            grid.DefaultCellStyle.Font = new Font("微软雅黑", 11f);
            grid.ColumnHeadersDefaultCellStyle.Font = new Font("微软雅黑", 11f, FontStyle.Bold);
            grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(240, 242, 245);
            grid.EnableHeadersVisualStyles = false;
            return grid;
        }

        private void PackagingForm_WorkspaceLayout(object sender, EventArgs e)
        {
            if (_workspaceTitleLabel == null || _workspaceHeaderPanel == null ||
                _workspaceItemsPanel == null || _workspaceFooterPanel == null)
                return;

            int pad = 16;
            int w = ClientSize.Width;
            int h = ClientSize.Height;
            if (w < 80 || h < 80)
                return;

            _workspaceTitleLabel.SetBounds(0, pad / 2, w, 42);
            _workspaceFooterPanel.Height = 60;
            _workspaceFooterPanel.SetBounds(pad, h - _workspaceFooterPanel.Height - pad, w - 2 * pad, _workspaceFooterPanel.Height);

            int headerTop = _workspaceTitleLabel.Bottom + 8;
            _workspaceHeaderPanel.SetBounds(pad, headerTop, w - 2 * pad, WorkspaceRowSpacing * 3 + 8);

            int fieldW = Math.Max(220, _workspaceHeaderPanel.ClientSize.Width - WorkspaceLabelCol - 16);
            foreach (Control c in _workspaceHeaderPanel.Controls)
            {
                if (c is ComboBox cb)
                {
                    cb.Left = WorkspaceLabelCol;
                    cb.Width = fieldW;
                    cb.Height = WorkspaceFieldHeight;
                }
            }

            int itemsTop = _workspaceHeaderPanel.Bottom + 8;
            int itemsBottom = _workspaceFooterPanel.Top - pad;
            _workspaceItemsPanel.SetBounds(pad, itemsTop, w - 2 * pad, Math.Max(140, itemsBottom - itemsTop));

            if (gridItems != null)
            {
                gridItems.SetBounds(8, 106, _workspaceItemsPanel.ClientSize.Width - 16,
                    Math.Max(80, _workspaceItemsPanel.ClientSize.Height - 114));
            }

            int gap = 24;
            int totalBtn = btnSave.Width + btnCancel.Width + gap;
            int sx = Math.Max(8, (_workspaceFooterPanel.ClientSize.Width - totalBtn) / 2);
            btnSave.Location = new Point(sx, 8);
            btnCancel.Location = new Point(btnSave.Right + gap, 8);
        }

        private Label CreateFormLabel(string text, int x, int y)
        {
            return new Label
            {
                Text = text,
                Font = new Font("微软雅黑", 12f),
                Location = new Point(x, y + 4),
                Size = new Size(94, WorkspaceFieldHeight),
                TextAlign = ContentAlignment.MiddleRight,
                ForeColor = Color.FromArgb(51, 51, 51)
            };
        }

        private ComboBox CreateComboBox(int x, int y, int width)
        {
            var combo = new ComboBox
            {
                Location = new Point(x, y),
                Size = new Size(width, WorkspaceFieldHeight),
                Font = new Font("微软雅黑", 12f),
                BackColor = Color.White,
                FlatStyle = FlatStyle.Flat
            };
            ClientSearchHelper.ApplySearchableStyle(combo);
            return combo;
        }

        private void BindNameCombo(ComboBox combo, List<ClientSearchItem> items, Action<ClientSearchItem> onSelected = null)
        {
            ClientSearchHelper.BindSearchableCombo(combo, items, new ClientSearchHelper.SearchComboOptions
            {
                PlaceholderText = ClientSearchHelper.NamePlaceholderText,
                OnSelected = onSelected
            });
        }

        private void UpdateFlagColor(ClientSearchItem item = null)
        {
            if (item == null && !ClientSearchHelper.TryGetSelectedItem(cmbPackFlag, _packFlagItems, out item))
            {
                cmbPackFlag.BackColor = Color.White;
                cmbPackFlag.ForeColor = Color.Black;
                UpdateItemSummary();
                return;
            }

            if (item != null && item.Code == "TAKE")
            {
                cmbPackFlag.BackColor = Color.FromArgb(232, 245, 233);
                cmbPackFlag.ForeColor = Color.FromArgb(46, 125, 50);
            }
            else if (item != null && item.Code == "RETURN")
            {
                cmbPackFlag.BackColor = Color.FromArgb(255, 235, 238);
                cmbPackFlag.ForeColor = Color.FromArgb(198, 40, 40);
            }
            else
            {
                cmbPackFlag.BackColor = Color.White;
                cmbPackFlag.ForeColor = Color.Black;
            }
            UpdateItemSummary();
        }

        private bool IsReturnPackFlag()
        {
            return ClientSearchHelper.TryGetSelectedItem(cmbPackFlag, _packFlagItems, out ClientSearchItem item)
                && item.Code == "RETURN";
        }

        private void LoadData()
        {
            try
            {
                DatabaseManager db = new DatabaseManager();

                _packFlagItems = new List<ClientSearchItem>
                {
                    new ClientSearchItem
                    {
                        Code = "TAKE",
                        Name = "出包装",
                        DisplayText = "出包装 (客户领取包装，金额计入应收)",
                        Initials = PinyinHelper.GetInitials("出包装")
                    },
                    new ClientSearchItem
                    {
                        Code = "RETURN",
                        Name = "进包装",
                        DisplayText = "进包装 (客户退回包装，金额从应收扣除)",
                        Initials = PinyinHelper.GetInitials("进包装")
                    }
                };
                BindNameCombo(cmbPackFlag, _packFlagItems, UpdateFlagColor);

                _clientItems = ClientSearchHelper.BuildItems(db.GetAllClients());
                ClientSearchHelper.BindSearchableCombo(cmbClient, _clientItems);

                var handlerNames = new List<string>();
                foreach (DataRow row in db.GetAllHandlers().Rows)
                    handlerNames.Add(row["name"].ToString());
                _handlerItems = ClientSearchHelper.BuildSimpleItems(handlerNames);
                BindNameCombo(cmbHandler, _handlerItems);

                RestoreLastSelection();
                LoadPackagingRows(db);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"加载数据失败: {ex.Message}", "错误",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void LoadPackagingRows(DatabaseManager db)
        {
            var defaultPrices = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
            foreach (DataRow row in db.GetPackTypePriceSummary().Rows)
            {
                string packType = row["pack_type"]?.ToString()?.Trim();
                if (string.IsNullOrWhiteSpace(packType))
                    continue;
                decimal price = 0m;
                if (row["current_unit_price"] != DBNull.Value)
                    decimal.TryParse(row["current_unit_price"].ToString(), out price);
                defaultPrices[packType] = price;
            }

            _packagingRows.Clear();
            foreach (string name in db.GetActivePackTypeNames())
            {
                defaultPrices.TryGetValue(name, out decimal unitPrice);
                _packagingRows.Add(new PackagingTypeRow
                {
                    PackType = name,
                    UnitPrice = unitPrice
                });
            }

            gridItems.DataSource = null;
            gridItems.DataSource = _packagingRows;
            UpdateItemSummary();
        }

        private void UpdateItemSummary()
        {
            if (lblItemSummary == null || lblTotalAmount == null)
                return;

            int selectedCount = _packagingRows.Count(r => r.Quantity > 0);
            lblItemSummary.Text = $"已选: {selectedCount} 种";

            decimal rawTotal = _packagingRows.Sum(r => r.Subtotal);
            decimal displayTotal = IsReturnPackFlag() ? -rawTotal : rawTotal;
            lblTotalAmount.Text = $"包装总金额: {displayTotal:0.00} 元";
            lblTotalAmount.ForeColor = IsReturnPackFlag() && rawTotal > 0
                ? Color.FromArgb(198, 40, 40)
                : Color.FromArgb(0, 102, 204);

            gridItems?.Refresh();
        }

        private List<PackagingTypeRow> GetFilledItems()
        {
            return _packagingRows
                .Where(r => r.Quantity > 0)
                .ToList();
        }

        private bool ValidateInput()
        {
            if (!ClientSearchHelper.TryGetSelectedItem(cmbPackFlag, _packFlagItems, out _))
            {
                MessageBox.Show("请选择出包装还是进包装！", "提示",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                cmbPackFlag.Focus();
                return false;
            }

            if (!ClientSearchHelper.TryGetSelectedClient(cmbClient, _clientItems, out _))
            {
                MessageBox.Show("请选择客户！", "提示",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                cmbClient.Focus();
                return false;
            }

            if (!ClientSearchHelper.TryGetSelectedItem(cmbHandler, _handlerItems, out _))
            {
                MessageBox.Show("请选择经手人！", "提示",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                cmbHandler.Focus();
                return false;
            }

            var filled = GetFilledItems();
            if (filled.Count == 0)
            {
                MessageBox.Show("请至少输入一种包装类型的数量！", "提示",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                if (gridItems.Rows.Count > 0)
                    gridItems.CurrentCell = gridItems.Rows[0].Cells["Quantity"];
                gridItems.Focus();
                return false;
            }

            // 单价选填：未填或无效时按 0 处理
            foreach (var item in filled)
            {
                if (item.UnitPrice < 0)
                    item.UnitPrice = 0m;
            }

            return true;
        }

        private void BtnSave_Click(object sender, EventArgs e)
        {
            if (!ValidateInput())
                return;

            try
            {
                if (!ClientSearchHelper.TryGetSelectedClient(cmbClient, _clientItems, out ClientSearchItem clientItem))
                    return;
                if (!ClientSearchHelper.TryGetSelectedItem(cmbPackFlag, _packFlagItems, out ClientSearchItem flagItem))
                    return;
                if (!ClientSearchHelper.TryGetSelectedItem(cmbHandler, _handlerItems, out ClientSearchItem handlerItem))
                    return;

                var items = GetFilledItems();
                string clientName = clientItem.Name;
                string clientCode = clientItem.Code;
                string orderNo = "PZ" + DateTime.Now.ToString("yyyyMMddHHmmss") + new Random().Next(100, 999);
                string packFlag = flagItem.Code;
                string handler = handlerItem.Name;

                DatabaseManager db = new DatabaseManager();
                int savedCount = 0;
                string lastError = null;

                foreach (var item in items)
                {
                    decimal totalAmount = item.Subtotal;
                    if (packFlag == "RETURN")
                        totalAmount = -totalAmount;

                    bool success = db.SavePackagingRecord(
                        orderNo: orderNo,
                        clientCode: clientCode,
                        clientName: clientName,
                        packType: item.PackType,
                        packFlag: packFlag,
                        quantity: item.Quantity,
                        unitPrice: item.UnitPrice,
                        totalAmount: totalAmount,
                        handler: handler,
                        creator: Environment.UserName);

                    if (success)
                        savedCount++;
                    else
                        lastError = item.PackType;
                }

                if (savedCount == items.Count)
                {
                    string flagDisplay = packFlag == "TAKE" ? "出包装" : "进包装";
                    string signDisplay = packFlag == "RETURN" ? " (扣款)" : "";
                    decimal grandTotal = items.Sum(i => i.Subtotal);
                    if (packFlag == "RETURN")
                        grandTotal = -grandTotal;

                    string detail = string.Join("\n", items.Select(i =>
                        $"  · {i.PackType} × {i.Quantity} @ {i.UnitPrice:F2} = {Math.Abs(i.Subtotal):F2}"));

                    MessageBox.Show($"✅ 包装记录保存成功！\n\n" +
                                  $"单据号: {orderNo}\n" +
                                  $"包装类型: {flagDisplay}\n" +
                                  $"客户: {clientName}\n" +
                                  $"共 {savedCount} 种明细:\n{detail}\n" +
                                  $"总金额: {Math.Abs(grandTotal):F2}元{signDisplay}",
                        "成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    SaveLastSelection();
                    ClearForm();
                }
                else if (savedCount > 0)
                {
                    MessageBox.Show($"部分保存成功（{savedCount}/{items.Count}）。\n失败明细: {lastError}",
                        "警告", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
                else
                {
                    MessageBox.Show("保存失败，请重试！", "错误",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"保存失败: {ex.Message}", "错误",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void ClearForm()
        {
            // 仅清空客户与数量；出进类型、经手人、单价保留，换的时候再改
            ClientSearchHelper.ResetToPlaceholder(cmbClient);

            foreach (var row in _packagingRows)
                row.Quantity = 0;

            gridItems.Refresh();
            UpdateItemSummary();
            cmbClient.Focus();
        }

        private void RestoreLastSelection()
        {
            string packFlag = FormLastSelectionSettings.Get(FormLastSelectionSettings.Keys.PackagingPackFlag);
            if (!string.IsNullOrEmpty(packFlag))
                ClientSearchHelper.TrySelectByCode(cmbPackFlag, _packFlagItems, packFlag);

            string handler = FormLastSelectionSettings.Get(FormLastSelectionSettings.Keys.PackagingHandler);
            if (!string.IsNullOrEmpty(handler))
                ClientSearchHelper.TrySelectByName(cmbHandler, _handlerItems, handler);
        }

        private void SaveLastSelection()
        {
            var values = new Dictionary<string, string>();

            if (ClientSearchHelper.TryGetSelectedItem(cmbPackFlag, _packFlagItems, out ClientSearchItem flagItem))
                values[FormLastSelectionSettings.Keys.PackagingPackFlag] = flagItem.Code;
            if (ClientSearchHelper.TryGetSelectedItem(cmbHandler, _handlerItems, out ClientSearchItem handlerItem))
                values[FormLastSelectionSettings.Keys.PackagingHandler] = handlerItem.Name;

            if (values.Count > 0)
                FormLastSelectionSettings.SetMany(values);
        }

        private void PackagingForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            SaveLastSelection();
        }

        private void BtnCancel_Click(object sender, EventArgs e)
        {
            Close();
        }

        private sealed class PackagingTypeRow
        {
            public string PackType { get; set; }
            public int Quantity { get; set; }
            public decimal UnitPrice { get; set; }
            public string QuantityDisplay
            {
                get => Quantity > 0 ? Quantity.ToString() : "";
                set
                {
                    if (string.IsNullOrWhiteSpace(value))
                        Quantity = 0;
                    else if (int.TryParse(value.Trim(), out int parsed) && parsed >= 0)
                        Quantity = parsed;
                }
            }
            public string UnitPriceDisplay
            {
                get => UnitPrice > 0 ? UnitPrice.ToString("0.##") : "";
                set
                {
                    if (string.IsNullOrWhiteSpace(value))
                        UnitPrice = 0m;
                    else if (decimal.TryParse(value.Trim(), out decimal parsed))
                        UnitPrice = parsed < 0m ? 0m : parsed;
                }
            }
            public decimal Subtotal => Quantity > 0 ? Quantity * Math.Max(0m, UnitPrice) : 0m;
            public string SubtotalDisplay => Quantity > 0 ? Subtotal.ToString("0.00") : "";
        }
    }
}
