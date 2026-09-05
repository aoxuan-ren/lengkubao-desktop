using System;
using System.Collections.Generic;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace lengkubao.desktop
{
    public partial class InboundForm : Form
    {
        private ComboBox cmbClient;
        private ComboBox cmbLocation;
        private ComboBox cmbHandler;
        private DataGridView gridItems;
        private Label lblItemSummary;
        private Button btnSave;
        private Button btnCancel;
        private List<ClientSearchItem> _clientItems = new List<ClientSearchItem>();
        private List<ClientSearchItem> _locationItems = new List<ClientSearchItem>();
        private List<ClientSearchItem> _handlerItems = new List<ClientSearchItem>();
        private List<InboundProductRow> _productRows = new List<InboundProductRow>();
        private Label _workspaceTitleLabel;
        private Panel _workspaceHeaderPanel;
        private Panel _workspaceItemsPanel;
        private Panel _workspaceFooterPanel;
        private const int WorkspaceLabelCol = 100;
        private const int WorkspaceRowSpacing = 46;
        private const int WorkspaceFieldHeight = 32;
        private const int ItemGridRowHeight = 34;

        public InboundForm()
        {
            InitializeComponent();
            InitializeForm();
        }

        private void InitializeForm()
        {
            Text = "新建入库单";
            Font = new Font("微软雅黑", 12f);
            Size = new Size(620, 640);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Color.FromArgb(240, 242, 245);
            FormBorderStyle = FormBorderStyle.Sizable;
            MaximizeBox = true;
            MinimizeBox = true;
            MinimumSize = new Size(520, 480);

            ClearDuplicateControls();

            _workspaceTitleLabel = new Label
            {
                Text = "新建入库单",
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

            Label lblClient = CreateFormLabel("客 户:", 10, yPos);
            cmbClient = CreateComboBox(WorkspaceLabelCol, yPos, 240);
            cmbClient.DropDownHeight = 250;
            _workspaceHeaderPanel.Controls.Add(lblClient);
            _workspaceHeaderPanel.Controls.Add(cmbClient);
            yPos += spacing;

            Label lblLocation = CreateFormLabel("库 位:", 10, yPos);
            cmbLocation = CreateComboBox(WorkspaceLabelCol, yPos, 240);
            _workspaceHeaderPanel.Controls.Add(lblLocation);
            _workspaceHeaderPanel.Controls.Add(cmbLocation);
            yPos += spacing;

            Label lblHandler = CreateFormLabel("经手人:", 10, yPos);
            cmbHandler = CreateComboBox(WorkspaceLabelCol, yPos, 240);
            _workspaceHeaderPanel.Controls.Add(lblHandler);
            _workspaceHeaderPanel.Controls.Add(cmbHandler);

            _workspaceItemsPanel = new Panel { BackColor = Color.White };
            Controls.Add(_workspaceItemsPanel);

            var lblItemsTitle = new Label
            {
                Text = "规格明细",
                Font = new Font("微软雅黑", 13f, FontStyle.Bold),
                ForeColor = Color.FromArgb(0, 102, 204),
                Location = new Point(8, 8),
                AutoSize = true
            };
            lblItemSummary = new Label
            {
                Text = "已选: 0 种  |  总计数量: 0",
                Font = new Font("微软雅黑", 10f),
                ForeColor = Color.FromArgb(0, 150, 136),
                Location = new Point(8, 36),
                AutoSize = true
            };
            var lblHint = new Label
            {
                Text = "在数量列填写各规格入库数量，可一次录入多种规格",
                Font = new Font("微软雅黑", 9f),
                ForeColor = Color.Gray,
                Location = new Point(8, 58),
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
            _workspaceItemsPanel.Controls.Add(gridItems);

            _workspaceFooterPanel = new Panel { BackColor = Color.Transparent };
            Controls.Add(_workspaceFooterPanel);

            btnSave = new Button
            {
                Text = "保 存",
                Size = new Size(118, 40),
                Location = new Point(70, 8),
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
                Size = new Size(118, 40),
                Location = new Point(180, 8),
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

            Resize -= InboundForm_WorkspaceLayout;
            Resize += InboundForm_WorkspaceLayout;
            InboundForm_WorkspaceLayout(null, EventArgs.Empty);

            FormClosing += InboundForm_FormClosing;
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
                Name = "Spec",
                HeaderText = "规格",
                DataPropertyName = "Spec",
                ReadOnly = true,
                FillWeight = 55
            });
            grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "Quantity",
                HeaderText = "数量",
                DataPropertyName = "QuantityDisplay",
                ReadOnly = false,
                FillWeight = 25,
                DefaultCellStyle = new DataGridViewCellStyle { Alignment = DataGridViewContentAlignment.MiddleCenter }
            });

            grid.DefaultCellStyle.Font = new Font("微软雅黑", 11f);
            grid.ColumnHeadersDefaultCellStyle.Font = new Font("微软雅黑", 11f, FontStyle.Bold);
            grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(240, 242, 245);
            grid.EnableHeadersVisualStyles = false;
            return grid;
        }

        private void InboundForm_WorkspaceLayout(object sender, EventArgs e)
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
            _workspaceFooterPanel.Height = 58;
            _workspaceFooterPanel.SetBounds(pad, h - _workspaceFooterPanel.Height - pad, w - 2 * pad, _workspaceFooterPanel.Height);

            int headerTop = _workspaceTitleLabel.Bottom + 8;
            _workspaceHeaderPanel.SetBounds(pad, headerTop, w - 2 * pad, WorkspaceRowSpacing * 3 + 8);

            int fieldW = Math.Max(200, _workspaceHeaderPanel.ClientSize.Width - WorkspaceLabelCol - 16);
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
            _workspaceItemsPanel.SetBounds(pad, itemsTop, w - 2 * pad, Math.Max(120, itemsBottom - itemsTop));

            if (gridItems != null)
            {
                gridItems.SetBounds(8, 84, _workspaceItemsPanel.ClientSize.Width - 16,
                    Math.Max(80, _workspaceItemsPanel.ClientSize.Height - 92));
            }

            int gap = 20;
            int totalBtn = btnSave.Width + btnCancel.Width + gap;
            int sx = Math.Max(8, (_workspaceFooterPanel.ClientSize.Width - totalBtn) / 2);
            btnSave.Location = new Point(sx, 9);
            btnCancel.Location = new Point(btnSave.Right + gap, 9);
        }

        private void ClearDuplicateControls()
        {
            Control[] controlsToRemove = new Control[Controls.Count];
            Controls.CopyTo(controlsToRemove, 0);

            foreach (Control control in controlsToRemove)
            {
                if (control is Label || control is TextBox || control is ComboBox ||
                    control is NumericUpDown || control is Button)
                {
                    if (!control.Name.StartsWith("$"))
                    {
                        Controls.Remove(control);
                        control.Dispose();
                    }
                }
            }
        }

        private Label CreateFormLabel(string text, int x, int y)
        {
            return new Label
            {
                Text = text,
                Font = new Font("微软雅黑", 12f),
                Location = new Point(x, y + 4),
                Size = new Size(86, WorkspaceFieldHeight),
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

        private void BindNameCombo(ComboBox combo, List<ClientSearchItem> items)
        {
            ClientSearchHelper.BindSearchableCombo(combo, items, new ClientSearchHelper.SearchComboOptions
            {
                PlaceholderText = ClientSearchHelper.NamePlaceholderText
            });
        }

        private void LoadData()
        {
            try
            {
                DatabaseManager db = new DatabaseManager();

                _clientItems = ClientSearchHelper.BuildItems(db.GetAllClients());
                BindNameCombo(cmbClient, _clientItems);

                var locationNames = new List<string>();
                DataTable locations = db.GetActiveLocations();
                foreach (DataRow row in locations.Rows)
                    locationNames.Add(row["name"].ToString());

                if (locationNames.Count == 0)
                {
                    cmbLocation.Enabled = false;
                    _locationItems = new List<ClientSearchItem>();
                }
                else
                {
                    cmbLocation.Enabled = true;
                    _locationItems = ClientSearchHelper.BuildSimpleItems(locationNames);
                }
                BindNameCombo(cmbLocation, _locationItems);

                var handlerNames = new List<string>();
                foreach (DataRow row in db.GetAllHandlers().Rows)
                    handlerNames.Add(row["name"].ToString());
                _handlerItems = ClientSearchHelper.BuildSimpleItems(handlerNames);
                BindNameCombo(cmbHandler, _handlerItems);

                RestoreLastSelection();
                LoadProductRows(db);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"加载数据失败: {ex.Message}", "错误",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void LoadProductRows(DatabaseManager db)
        {
            _productRows.Clear();
            DataTable products = db.GetAllProductTypes();
            bool hasProducts = false;
            foreach (DataRow row in products.Rows)
            {
                if (row["is_active"].ToString() == "1")
                {
                    _productRows.Add(new InboundProductRow
                    {
                        Spec = row["name"].ToString()
                    });
                    hasProducts = true;
                }
            }

            if (!hasProducts)
            {
                foreach (string spec in new[] { "42型", "45型", "60型", "精品型", "次型", "筐", "48型" })
                    _productRows.Add(new InboundProductRow { Spec = spec });
            }

            gridItems.DataSource = null;
            gridItems.DataSource = _productRows;
            UpdateItemSummary();
        }

        private void UpdateItemSummary()
        {
            if (lblItemSummary == null)
                return;

            int selectedCount = _productRows.Count(r => r.Quantity > 0);
            int totalQty = _productRows.Sum(r => r.Quantity);
            lblItemSummary.Text = $"已选: {selectedCount} 种  |  总计数量: {totalQty}";
        }

        private List<InboundProductRow> GetFilledItems()
        {
            return _productRows.Where(r => r.Quantity > 0).ToList();
        }

        private void BtnSave_Click(object sender, EventArgs e)
        {
            if (!ValidateInput())
                return;

            try
            {
                if (!ClientSearchHelper.TryGetSelectedClient(cmbClient, _clientItems, out ClientSearchItem clientItem))
                    return;
                if (!ClientSearchHelper.TryGetSelectedItem(cmbLocation, _locationItems, out ClientSearchItem locationItem))
                    return;
                if (!ClientSearchHelper.TryGetSelectedItem(cmbHandler, _handlerItems, out ClientSearchItem handlerItem))
                    return;

                var items = GetFilledItems();
                string clientName = clientItem.Name;
                string clientCode = clientItem.Code;
                string orderNo = "RK" + DateTime.Now.ToString("yyyyMMddHHmmss") + new Random().Next(100, 999);
                string location = locationItem.Name;
                string handler = handlerItem.Name;

                DatabaseManager db = new DatabaseManager();
                int savedCount = 0;
                string lastError = null;

                foreach (var item in items)
                {
                    bool success = db.SaveInboundRecord(
                        orderNo,
                        clientCode,
                        clientName,
                        location,
                        DateTime.Now,
                        item.Spec,
                        item.Quantity,
                        0.0m,
                        0.0m,
                        handler,
                        "系统");

                    if (success)
                        savedCount++;
                    else
                        lastError = item.Spec;
                }

                if (savedCount == items.Count)
                {
                    string detail = string.Join("\n", items.Select(i => $"  · {i.Spec} × {i.Quantity}"));
                    MessageBox.Show($"✅ 入库记录保存成功！\n\n单据号: {orderNo}\n共 {savedCount} 种规格:\n{detail}",
                        "成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    SaveLastSelection();
                    ClearForm();
                }
                else if (savedCount > 0)
                {
                    MessageBox.Show($"部分保存成功（{savedCount}/{items.Count}）。\n失败规格: {lastError}",
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

        private bool ValidateInput()
        {
            if (!ClientSearchHelper.TryGetSelectedClient(cmbClient, _clientItems, out _))
            {
                MessageBox.Show("请选择客户！", "提示",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                cmbClient.Focus();
                return false;
            }

            if (!ClientSearchHelper.TryGetSelectedItem(cmbLocation, _locationItems, out _))
            {
                MessageBox.Show("请选择库位！", "提示",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                cmbLocation.Focus();
                return false;
            }

            if (!ClientSearchHelper.TryGetSelectedItem(cmbHandler, _handlerItems, out _))
            {
                MessageBox.Show("请选择经手人！", "提示",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                cmbHandler.Focus();
                return false;
            }

            if (GetFilledItems().Count == 0)
            {
                MessageBox.Show("请至少输入一种规格的数量！", "提示",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                if (gridItems.Rows.Count > 0)
                    gridItems.CurrentCell = gridItems.Rows[0].Cells["Quantity"];
                gridItems.Focus();
                return false;
            }

            return true;
        }

        private void ClearForm()
        {
            // 仅清空客户与数量；库位、经手人保留，换的时候再改
            ClientSearchHelper.ResetToPlaceholder(cmbClient);

            foreach (var row in _productRows)
                row.Quantity = 0;

            gridItems.Refresh();
            UpdateItemSummary();
            cmbClient.Focus();
        }

        private void RestoreLastSelection()
        {
            string location = FormLastSelectionSettings.Get(FormLastSelectionSettings.Keys.InboundLocation);
            if (!string.IsNullOrEmpty(location))
                ClientSearchHelper.TrySelectByName(cmbLocation, _locationItems, location);

            string handler = FormLastSelectionSettings.Get(FormLastSelectionSettings.Keys.InboundHandler);
            if (!string.IsNullOrEmpty(handler))
                ClientSearchHelper.TrySelectByName(cmbHandler, _handlerItems, handler);
        }

        private void SaveLastSelection()
        {
            var values = new Dictionary<string, string>();

            if (ClientSearchHelper.TryGetSelectedItem(cmbLocation, _locationItems, out ClientSearchItem locationItem))
                values[FormLastSelectionSettings.Keys.InboundLocation] = locationItem.Name;
            if (ClientSearchHelper.TryGetSelectedItem(cmbHandler, _handlerItems, out ClientSearchItem handlerItem))
                values[FormLastSelectionSettings.Keys.InboundHandler] = handlerItem.Name;

            if (values.Count > 0)
                FormLastSelectionSettings.SetMany(values);
        }

        private void InboundForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            SaveLastSelection();
        }

        private void BtnCancel_Click(object sender, EventArgs e)
        {
            Close();
        }

        private void InboundForm_Load(object sender, EventArgs e) { }
        private void label4_Click_1(object sender, EventArgs e) { }
        private void btnsave_Click_1(object sender, EventArgs e) { }

        private sealed class InboundProductRow
        {
            public string Spec { get; set; }
            public int Quantity { get; set; }
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
        }
    }
}
