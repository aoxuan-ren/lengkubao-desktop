using System;
using System.Collections.Generic;
using System.Data;
using System.Drawing;
using System.Windows.Forms;

namespace lengkubao.desktop
{
    public class SalesForm : Form
    {
        // 左侧 · 单笔报账
        private ComboBox cmbClient;
        private ComboBox cmbSpec;
        private TextBox txtQuantity;
        private TextBox txtUnitPrice;
        private TextBox txtTotalAmount;
        private ComboBox cmbLocation;
        private ComboBox cmbHandler;
        private Button btnSave;
        private Panel _singlePanel;

        // 右侧 · 批量报账（仅定价，不动库存）
        private ComboBox cmbBatchLocation;
        private ComboBox cmbBatchSpec;
        private TextBox txtBatchUnitPrice;
        private ComboBox cmbBatchHandler;
        private Label _lblBatchLocation;
        private Label _lblBatchSpec;
        private Label _lblBatchPrice;
        private Label _lblBatchHandler;
        private DataGridView gridBatch;
        private Label lblBatchSummary;
        private Label lblBatchActualStock;
        private Label lblBatchConfirmHint;
        private Button btnBatchConfirm;
        private ToolTip _batchToolTip;
        private Panel _batchPanel;
        private Panel _batchFilterPanel;
        private Panel _batchBottomPanel;

        private Button btnClose;
        private List<ClientSearchItem> _clientItems = new List<ClientSearchItem>();
        private List<ClientSearchItem> _specItems = new List<ClientSearchItem>();
        private List<ClientSearchItem> _locationItems = new List<ClientSearchItem>();
        private List<ClientSearchItem> _handlerItems = new List<ClientSearchItem>();
        private Label _workspaceTitleLabel;
        private SplitContainer _contentSplit;
        private Panel _workspaceFooterPanel;

        private int _batchRecordCount;
        private int _batchQuotableQty;
        private int _batchAvailableStock;

        private const int WorkspaceLabelCol = 88;
        private const int SingleRowSpacing = 44;
        private const int WorkspaceFieldHeight = 32;
        private const int BatchFilterPanelHeight = 148;
        private const int BatchBottomPanelHeight = 118;
        private const int BatchGridHeaderHeight = 28;
        private const int BatchGridRowHeight = 28;

        public SalesForm()
        {
            InitializeForm();
        }

        private void InitializeForm()
        {
            Text = "客户报账";
            Font = new Font("微软雅黑", 12f);
            Size = new Size(1080, 620);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Color.FromArgb(240, 242, 245);
            FormBorderStyle = FormBorderStyle.Sizable;
            MaximizeBox = true;
            MinimizeBox = true;
            MinimumSize = new Size(900, 520);

            _workspaceTitleLabel = new Label
            {
                Text = "客户报账",
                Font = new Font("微软雅黑", 18f, FontStyle.Bold),
                ForeColor = Color.FromArgb(0, 102, 204),
                Height = 42,
                TextAlign = ContentAlignment.MiddleCenter
            };
            Controls.Add(_workspaceTitleLabel);

            _contentSplit = new SplitContainer
            {
                Orientation = Orientation.Vertical,
                SplitterDistance = 50,
                BackColor = Color.FromArgb(220, 224, 228),
                BorderStyle = BorderStyle.FixedSingle
            };
            Controls.Add(_contentSplit);

            BuildSinglePanel();
            BuildBatchPanel();
            _contentSplit.Panel1.Controls.Add(_singlePanel);
            _contentSplit.Panel2.Controls.Add(_batchPanel);

            _workspaceFooterPanel = new Panel { BackColor = Color.Transparent };
            Controls.Add(_workspaceFooterPanel);

            btnClose = new Button
            {
                Text = "关 闭",
                Size = new Size(118, 40),
                Font = new Font("微软雅黑", 12f),
                BackColor = Color.FromArgb(158, 158, 158),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand
            };
            btnClose.FlatAppearance.BorderSize = 0;
            btnClose.Click += (s, e) => Close();
            _workspaceFooterPanel.Controls.Add(btnClose);

            Resize -= SalesForm_WorkspaceLayout;
            Resize += SalesForm_WorkspaceLayout;
            _singlePanel.Resize += (s, e) => LayoutSingleFields();
            _batchPanel.Resize += (s, e) => LayoutBatchFields();
            _batchFilterPanel.Resize += (s, e) => LayoutBatchFields();
            SalesForm_WorkspaceLayout(null, EventArgs.Empty);

            LoadData();
        }

        private void BuildSinglePanel()
        {
            _singlePanel = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.White,
                Padding = new Padding(12, 8, 12, 8)
            };

            var lblTitle = new Label
            {
                Text = "单笔报账",
                Font = new Font("微软雅黑", 14f, FontStyle.Bold),
                ForeColor = Color.FromArgb(0, 150, 136),
                Location = new Point(12, 8),
                AutoSize = true
            };
            var lblSubtitle = new Label
            {
                Text = "手动录入一条报账记录（会扣减库存）",
                Font = new Font("微软雅黑", 10f),
                ForeColor = Color.Gray,
                Location = new Point(12, 34),
                AutoSize = true
            };
            _singlePanel.Controls.Add(lblTitle);
            _singlePanel.Controls.Add(lblSubtitle);

            int yPos = 62;
            int spacing = SingleRowSpacing;

            Label lblClient = CreateFormLabel("客 户:", 4, yPos);
            cmbClient = CreateComboBox(WorkspaceLabelCol, yPos, 240);
            cmbClient.DropDownHeight = 250;
            _singlePanel.Controls.Add(lblClient);
            _singlePanel.Controls.Add(cmbClient);
            yPos += spacing;

            Label lblSpec = CreateFormLabel("规 格:", 4, yPos);
            cmbSpec = CreateComboBox(WorkspaceLabelCol, yPos, 240);
            _singlePanel.Controls.Add(lblSpec);
            _singlePanel.Controls.Add(cmbSpec);
            yPos += spacing;

            Label lblQuantity = CreateFormLabel("数 量:", 4, yPos);
            txtQuantity = CreateTextBox(WorkspaceLabelCol, yPos, 240);
            NumericFieldHelper.BindQuantityField(txtQuantity);
            txtQuantity.TextChanged += (s, e) => CalculateTotal();
            _singlePanel.Controls.Add(lblQuantity);
            _singlePanel.Controls.Add(txtQuantity);
            yPos += spacing;

            Label lblUnitPrice = CreateFormLabel("单 价:", 4, yPos);
            txtUnitPrice = CreateTextBox(WorkspaceLabelCol, yPos, 240);
            NumericFieldHelper.BindUnitPriceField(txtUnitPrice);
            txtUnitPrice.TextChanged += (s, e) => CalculateTotal();
            _singlePanel.Controls.Add(lblUnitPrice);
            _singlePanel.Controls.Add(txtUnitPrice);
            yPos += spacing;

            Label lblTotalAmount = CreateFormLabel("金 额:", 4, yPos);
            txtTotalAmount = CreateTextBox(WorkspaceLabelCol, yPos, 240);
            txtTotalAmount.ReadOnly = true;
            txtTotalAmount.BackColor = Color.WhiteSmoke;
            _singlePanel.Controls.Add(lblTotalAmount);
            _singlePanel.Controls.Add(txtTotalAmount);
            yPos += spacing;

            Label lblLocation = CreateFormLabel("库 位:", 4, yPos);
            cmbLocation = CreateComboBox(WorkspaceLabelCol, yPos, 240);
            _singlePanel.Controls.Add(lblLocation);
            _singlePanel.Controls.Add(cmbLocation);
            yPos += spacing;

            Label lblHandler = CreateFormLabel("经手人:", 4, yPos);
            cmbHandler = CreateComboBox(WorkspaceLabelCol, yPos, 240);
            _singlePanel.Controls.Add(lblHandler);
            _singlePanel.Controls.Add(cmbHandler);

            btnSave = new Button
            {
                Text = "保 存",
                Size = new Size(140, 40),
                Font = new Font("微软雅黑", 12f, FontStyle.Bold),
                BackColor = Color.FromArgb(0, 150, 136),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand,
                Anchor = AnchorStyles.Bottom | AnchorStyles.Left
            };
            btnSave.FlatAppearance.BorderSize = 0;
            btnSave.Click += BtnSave_Click;
            _singlePanel.Controls.Add(btnSave);
        }

        private void BuildBatchPanel()
        {
            _batchPanel = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.White,
                Padding = new Padding(8, 4, 8, 4)
            };

            _batchFilterPanel = new Panel
            {
                Dock = DockStyle.Top,
                Height = BatchFilterPanelHeight,
                BackColor = Color.White
            };

            _batchBottomPanel = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = BatchBottomPanelHeight,
                BackColor = Color.White
            };

            var lblTitle = new Label
            {
                Text = "批量报账",
                Font = new Font("微软雅黑", 14f, FontStyle.Bold),
                ForeColor = Color.FromArgb(0, 102, 204),
                Location = new Point(4, 4),
                AutoSize = true
            };
            var lblSubtitle = new Label
            {
                Text = "按库位+型号，对尚未报账的客户批量生成报价单（数量+单价），不影响实际库存",
                Font = new Font("微软雅黑", 9.5f),
                ForeColor = Color.Gray,
                Location = new Point(4, 28),
                Size = new Size(480, 28),
                AutoSize = false
            };
            _batchFilterPanel.Controls.Add(lblTitle);
            _batchFilterPanel.Controls.Add(lblSubtitle);

            int filterY = 58;
            int col1 = 4;
            int col2 = 248;

            _lblBatchLocation = new Label
            {
                Text = "库 位:",
                Location = new Point(col1, filterY + 4),
                Size = new Size(52, 24),
                TextAlign = ContentAlignment.MiddleRight,
                Font = new Font("微软雅黑", 11f)
            };
            cmbBatchLocation = CreateComboBox(col1 + 56, filterY, 160);
            cmbBatchLocation.SelectedIndexChanged += (s, e) => RefreshBatchGrid();
            cmbBatchLocation.TextChanged += (s, e) => RefreshBatchGrid();

            _lblBatchSpec = new Label
            {
                Text = "规 格:",
                Location = new Point(col2, filterY + 4),
                Size = new Size(52, 24),
                TextAlign = ContentAlignment.MiddleRight,
                Font = new Font("微软雅黑", 11f)
            };
            cmbBatchSpec = CreateComboBox(col2 + 56, filterY, 160);
            cmbBatchSpec.SelectedIndexChanged += (s, e) => RefreshBatchGrid();
            cmbBatchSpec.TextChanged += (s, e) => RefreshBatchGrid();

            filterY += 38;
            _lblBatchPrice = new Label
            {
                Text = "单 价:",
                Location = new Point(col1, filterY + 4),
                Size = new Size(52, 24),
                TextAlign = ContentAlignment.MiddleRight,
                Font = new Font("微软雅黑", 11f)
            };
            txtBatchUnitPrice = CreateTextBox(col1 + 56, filterY, 160);
            NumericFieldHelper.BindUnitPriceField(txtBatchUnitPrice);
            txtBatchUnitPrice.TextChanged += (s, e) => RefreshBatchGrid();

            _lblBatchHandler = new Label
            {
                Text = "经手人:",
                Location = new Point(col2, filterY + 4),
                Size = new Size(52, 24),
                TextAlign = ContentAlignment.MiddleRight,
                Font = new Font("微软雅黑", 11f)
            };
            cmbBatchHandler = CreateComboBox(col2 + 56, filterY, 160);

            _batchFilterPanel.Controls.AddRange(new Control[]
            {
                _lblBatchLocation, cmbBatchLocation,
                _lblBatchSpec, cmbBatchSpec,
                _lblBatchPrice, txtBatchUnitPrice,
                _lblBatchHandler, cmbBatchHandler
            });

            gridBatch = new DataGridView
            {
                Dock = DockStyle.Fill,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                ReadOnly = true,
                RowHeadersVisible = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                BackgroundColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                Margin = new Padding(4, 6, 4, 4)
            };
            QueryUiHelper.ApplyQueryGrid(gridBatch, QueryModule.Sales);
            ApplyBatchGridCompactStyle();

            lblBatchActualStock = new Label
            {
                Text = "实际库存：—",
                Font = new Font("微软雅黑", 11f, FontStyle.Bold),
                ForeColor = Color.FromArgb(22, 101, 52),
                Dock = DockStyle.Top,
                Height = 28,
                TextAlign = ContentAlignment.MiddleLeft
            };

            lblBatchSummary = new Label
            {
                Text = "请选择库位和规格以查看客户入库与定价预览",
                Font = new Font("微软雅黑", 10f, FontStyle.Bold),
                ForeColor = Color.FromArgb(0, 102, 204),
                Dock = DockStyle.Top,
                Height = 26,
                TextAlign = ContentAlignment.MiddleLeft
            };

            lblBatchConfirmHint = new Label
            {
                Text = string.Empty,
                Font = new Font("微软雅黑", 9f),
                ForeColor = Color.FromArgb(120, 120, 120),
                Dock = DockStyle.Top,
                Height = 22,
                TextAlign = ContentAlignment.MiddleLeft,
                AutoEllipsis = true
            };

            btnBatchConfirm = new Button
            {
                Text = "确认批量报价",
                Size = new Size(160, 38),
                Font = new Font("微软雅黑", 11f, FontStyle.Bold),
                BackColor = Color.FromArgb(0, 102, 204),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand,
                Enabled = false,
                Anchor = AnchorStyles.Top | AnchorStyles.Right
            };
            btnBatchConfirm.FlatAppearance.BorderSize = 0;
            btnBatchConfirm.Click += BtnBatchConfirm_Click;

            _batchToolTip = new ToolTip { AutoPopDelay = 8000, InitialDelay = 400, ReshowDelay = 200 };

            _batchBottomPanel.Controls.Add(lblBatchConfirmHint);
            _batchBottomPanel.Controls.Add(lblBatchSummary);
            _batchBottomPanel.Controls.Add(lblBatchActualStock);
            _batchBottomPanel.Controls.Add(btnBatchConfirm);

            _batchPanel.Controls.Add(gridBatch);
            _batchPanel.Controls.Add(_batchBottomPanel);
            _batchPanel.Controls.Add(_batchFilterPanel);
        }

        private void ApplyBatchGridCompactStyle()
        {
            if (gridBatch == null)
                return;

            var compactFont = new Font("微软雅黑", 10f);
            var compactHeaderFont = new Font("微软雅黑", 10f, FontStyle.Bold);
            gridBatch.ColumnHeadersHeight = BatchGridHeaderHeight;
            gridBatch.RowTemplate.Height = BatchGridRowHeight;
            gridBatch.ColumnHeadersDefaultCellStyle.Font = compactHeaderFont;
            gridBatch.ColumnHeadersDefaultCellStyle.Padding = new Padding(4, 0, 4, 0);
            gridBatch.DefaultCellStyle.Font = compactFont;
            gridBatch.RowsDefaultCellStyle.Font = compactFont;
        }

        private void SalesForm_WorkspaceLayout(object sender, EventArgs e)
        {
            if (_workspaceTitleLabel == null || _contentSplit == null || _workspaceFooterPanel == null)
                return;

            int pad = 16;
            int w = ClientSize.Width;
            int h = ClientSize.Height;
            if (w < 80 || h < 80)
                return;

            _workspaceTitleLabel.SetBounds(0, pad / 2, w, 42);
            _workspaceFooterPanel.Height = 58;
            _workspaceFooterPanel.SetBounds(pad, h - _workspaceFooterPanel.Height - pad, w - 2 * pad, _workspaceFooterPanel.Height);

            int splitTop = _workspaceTitleLabel.Bottom + 8;
            _contentSplit.SetBounds(pad, splitTop, w - 2 * pad, Math.Max(120, h - splitTop - _workspaceFooterPanel.Height - pad * 2));

            int splitInner = Math.Max(200, _contentSplit.ClientSize.Width - _contentSplit.SplitterWidth);
            _contentSplit.SplitterDistance = splitInner / 2;

            btnClose.Location = new Point(Math.Max(8, (_workspaceFooterPanel.ClientSize.Width - btnClose.Width) / 2), 9);

            LayoutSingleFields();
            LayoutBatchFields();
        }

        private void LayoutSingleFields()
        {
            if (_singlePanel == null || cmbClient == null)
                return;

            int fieldW = Math.Max(160, _singlePanel.ClientSize.Width - WorkspaceLabelCol - 28);
            foreach (Control c in _singlePanel.Controls)
            {
                if (c is ComboBox cb)
                {
                    cb.Width = fieldW;
                    cb.Height = WorkspaceFieldHeight;
                }
                else if (c is TextBox tb)
                {
                    tb.Width = fieldW;
                    tb.Height = WorkspaceFieldHeight;
                }
            }

            if (btnSave != null)
                btnSave.Location = new Point(WorkspaceLabelCol, Math.Max(420, _singlePanel.ClientSize.Height - 52));
        }

        private void LayoutBatchFields()
        {
            if (_batchPanel == null || _batchFilterPanel == null || _batchBottomPanel == null)
                return;

            int w = _batchFilterPanel.ClientSize.Width;
            if (w < 100)
                return;

            int col1LabelX = 4;
            int col1FieldX = 60;
            int col2LabelX = w / 2;
            int col2FieldX = col2LabelX + 56;
            int fieldW = Math.Max(100, w / 2 - col1FieldX - 8);
            int col2FieldW = Math.Max(100, w - col2FieldX - 8);

            _lblBatchLocation?.SetBounds(col1LabelX, 62, 52, 24);
            cmbBatchLocation?.SetBounds(col1FieldX, 58, fieldW, WorkspaceFieldHeight);
            _lblBatchSpec?.SetBounds(col2LabelX, 62, 52, 24);
            cmbBatchSpec?.SetBounds(col2FieldX, 58, col2FieldW, WorkspaceFieldHeight);
            _lblBatchPrice?.SetBounds(col1LabelX, 100, 52, 24);
            txtBatchUnitPrice?.SetBounds(col1FieldX, 96, fieldW, WorkspaceFieldHeight);
            _lblBatchHandler?.SetBounds(col2LabelX, 100, 52, 24);
            cmbBatchHandler?.SetBounds(col2FieldX, 96, col2FieldW, WorkspaceFieldHeight);

            if (btnBatchConfirm != null)
                btnBatchConfirm.Location = new Point(Math.Max(4, _batchBottomPanel.ClientSize.Width - btnBatchConfirm.Width - 4), 72);
        }

        private void UpdateBatchConfirmButton(bool enabled, string disableReason)
        {
            if (btnBatchConfirm == null)
                return;

            btnBatchConfirm.Enabled = enabled;
            btnBatchConfirm.BackColor = enabled
                ? Color.FromArgb(0, 102, 204)
                : Color.FromArgb(148, 163, 184);
            btnBatchConfirm.Cursor = enabled ? Cursors.Hand : Cursors.Default;

            if (lblBatchConfirmHint != null)
            {
                lblBatchConfirmHint.Text = enabled ? string.Empty : (disableReason ?? string.Empty);
                lblBatchConfirmHint.ForeColor = disableReason != null && disableReason.Contains("不一致")
                    ? Color.FromArgb(198, 40, 40)
                    : Color.FromArgb(120, 120, 120);
            }

            if (_batchToolTip != null)
            {
                string tip = enabled
                    ? "按客户待报数量批量生成报价单，不影响实际库存"
                    : (string.IsNullOrWhiteSpace(disableReason) ? "当前不可批量报价" : disableReason);
                _batchToolTip.SetToolTip(btnBatchConfirm, tip);
            }
        }

        private static void UpdateBatchActualStockLabel(Label label, int stock)
        {
            if (label == null)
                return;

            label.Text = $"实际库存：{stock} 件";
            if (stock <= 0)
            {
                label.ForeColor = Color.FromArgb(198, 40, 40);
            }
            else
            {
                label.ForeColor = Color.FromArgb(22, 101, 52);
            }
        }

        private Label CreateFormLabel(string text, int x, int y)
        {
            return new Label
            {
                Text = text,
                Font = new Font("微软雅黑", 12f),
                Location = new Point(x, y + 4),
                Size = new Size(80, WorkspaceFieldHeight),
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
                Font = new Font("微软雅黑", 11f),
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

        private TextBox CreateTextBox(int x, int y, int width)
        {
            return new TextBox
            {
                Location = new Point(x, y),
                Size = new Size(width, WorkspaceFieldHeight),
                Font = new Font("微软雅黑", 11f),
                BackColor = Color.White
            };
        }

        private void CalculateTotal()
        {
            try
            {
                if (NumericFieldHelper.TryGetInt(txtQuantity, out int quantity)
                    && NumericFieldHelper.TryGetDecimal(txtUnitPrice, out decimal unitPrice))
                {
                    txtTotalAmount.Text = (quantity * unitPrice).ToString("0.00");
                }
                else
                {
                    txtTotalAmount.Text = string.Empty;
                }
            }
            catch
            {
                txtTotalAmount.Text = string.Empty;
            }
        }

        private decimal GetBatchUnitPrice()
        {
            if (NumericFieldHelper.TryGetDecimal(txtBatchUnitPrice, out decimal parsedPrice) && parsedPrice > 0)
                return parsedPrice;
            return 0m;
        }

        private void LoadData()
        {
            try
            {
                DatabaseManager db = new DatabaseManager();

                _clientItems = ClientSearchHelper.BuildItems(db.GetAllClients());
                ClientSearchHelper.BindSearchableCombo(cmbClient, _clientItems);

                var specNames = new List<string>();
                DataTable products = db.GetAllProductTypes();
                bool hasProducts = false;
                foreach (DataRow row in products.Rows)
                {
                    if (row["is_active"].ToString() == "1")
                    {
                        specNames.Add(row["name"].ToString());
                        hasProducts = true;
                    }
                }
                if (!hasProducts)
                    specNames.AddRange(new[] { "42型", "45型", "60型", "精品型", "次型", "筐", "48型" });
                _specItems = ClientSearchHelper.BuildSimpleItems(specNames);
                BindNameCombo(cmbSpec, _specItems);
                BindNameCombo(cmbBatchSpec, _specItems);

                var locationNames = new List<string>();
                DataTable locations = db.GetActiveLocations();
                foreach (DataRow row in locations.Rows)
                    locationNames.Add(row["name"].ToString());

                if (locationNames.Count == 0)
                {
                    cmbLocation.Enabled = false;
                    cmbBatchLocation.Enabled = false;
                    _locationItems = new List<ClientSearchItem>();
                }
                else
                {
                    cmbLocation.Enabled = true;
                    cmbBatchLocation.Enabled = true;
                    _locationItems = ClientSearchHelper.BuildSimpleItems(locationNames);
                }
                BindNameCombo(cmbLocation, _locationItems);
                BindNameCombo(cmbBatchLocation, _locationItems);

                var handlerNames = new List<string>();
                foreach (DataRow row in db.GetAllHandlers().Rows)
                    handlerNames.Add(row["name"].ToString());
                _handlerItems = ClientSearchHelper.BuildSimpleItems(handlerNames);
                BindNameCombo(cmbHandler, _handlerItems);
                BindNameCombo(cmbBatchHandler, _handlerItems);

                RefreshBatchGrid();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"加载数据失败: {ex.Message}", "错误",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void RefreshBatchGrid()
        {
            if (gridBatch == null || lblBatchSummary == null || btnBatchConfirm == null)
                return;

            ClientSearchHelper.CommitComboSearchInput(cmbBatchLocation);
            ClientSearchHelper.CommitComboSearchInput(cmbBatchSpec);

            _batchRecordCount = 0;
            _batchQuotableQty = 0;
            _batchAvailableStock = 0;
            decimal totalAmount = 0m;
            decimal unitPrice = GetBatchUnitPrice();

            if (!ClientSearchHelper.TryGetSelectedItem(cmbBatchLocation, _locationItems, out ClientSearchItem locationItem)
                || !ClientSearchHelper.TryGetSelectedItem(cmbBatchSpec, _specItems, out ClientSearchItem specItem))
            {
                gridBatch.DataSource = null;
                lblBatchSummary.Text = "请选择库位和规格以查看待报价客户";
                lblBatchSummary.ForeColor = Color.FromArgb(0, 102, 204);
                UpdateBatchActualStockLabel(lblBatchActualStock, 0);
                UpdateBatchConfirmButton(false, "请从下拉列表选择库位和规格（仅输入文字无效）");
                return;
            }

            try
            {
                DatabaseManager db = new DatabaseManager();
                string location = locationItem.Name;
                string spec = specItem.Name;

                _batchAvailableStock = db.GetCurrentStockByLocationAndSpec(location, spec);
                UpdateBatchActualStockLabel(lblBatchActualStock, _batchAvailableStock);

                BatchSaleAllocationInfo allocationInfo = db.GetBatchSaleAllocationInfo(location, spec);
                DataTable sourceData = allocationInfo.Allocations;

                var displayTable = new DataTable();
                displayTable.Columns.Add("客户", typeof(string));
                displayTable.Columns.Add("入库合计", typeof(int));
                displayTable.Columns.Add("已报数量", typeof(int));
                displayTable.Columns.Add("待报价数量", typeof(int));
                displayTable.Columns.Add("预计金额", typeof(decimal));

                foreach (DataRow row in sourceData.Rows)
                {
                    int sellableQty = row["sellable_qty"] != DBNull.Value ? Convert.ToInt32(row["sellable_qty"]) : 0;
                    if (sellableQty <= 0)
                        continue;

                    string clientName = row["client_name"]?.ToString()?.Trim();
                    if (string.IsNullOrEmpty(clientName))
                        clientName = row["client_code"]?.ToString()?.Trim() ?? string.Empty;

                    int inboundQty = row["inbound_qty"] != DBNull.Value ? Convert.ToInt32(row["inbound_qty"]) : 0;
                    int soldQty = row["sold_qty"] != DBNull.Value ? Convert.ToInt32(row["sold_qty"]) : 0;
                    decimal amount = sellableQty * unitPrice;
                    displayTable.Rows.Add(clientName, inboundQty, soldQty, sellableQty, amount);

                    _batchRecordCount++;
                    _batchQuotableQty += sellableQty;
                    totalAmount += amount;
                }

                gridBatch.DataSource = displayTable;
                ConfigureBatchGridColumns();
                ApplyBatchGridCompactStyle();

                string priceHint = unitPrice > 0 ? $"{totalAmount:F2} 元" : "（请填写单价）";
                lblBatchSummary.Text =
                    $"待报价 {_batchRecordCount} 个客户 · {_batchQuotableQty} 件 · 预计金额 {priceHint}";

                if (_batchQuotableQty <= 0)
                {
                    lblBatchSummary.ForeColor = Color.FromArgb(120, 120, 120);
                    UpdateBatchConfirmButton(false, "当前库位+规格下没有待报价客户");
                }
                else if (unitPrice <= 0)
                {
                    lblBatchSummary.ForeColor = Color.FromArgb(0, 102, 204);
                    UpdateBatchConfirmButton(false, "请填写大于 0 的单价");
                }
                else
                {
                    lblBatchSummary.ForeColor = Color.FromArgb(0, 120, 80);
                    UpdateBatchConfirmButton(true, null);
                }
            }
            catch
            {
                gridBatch.DataSource = null;
                lblBatchSummary.Text = "预览加载失败，请重试";
                lblBatchSummary.ForeColor = Color.FromArgb(198, 40, 40);
                UpdateBatchActualStockLabel(lblBatchActualStock, 0);
                UpdateBatchConfirmButton(false, "预览加载失败，请重试");
            }
        }

        private void ConfigureBatchGridColumns()
        {
            if (gridBatch.Columns.Count == 0)
                return;

            gridBatch.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
            gridBatch.ColumnHeadersDefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;

            if (gridBatch.Columns.Contains("入库合计"))
                gridBatch.Columns["入库合计"].DefaultCellStyle.Format = "N0";
            if (gridBatch.Columns.Contains("已报数量"))
                gridBatch.Columns["已报数量"].DefaultCellStyle.Format = "N0";
            if (gridBatch.Columns.Contains("待报价数量"))
                gridBatch.Columns["待报价数量"].DefaultCellStyle.Format = "N0";
            if (gridBatch.Columns.Contains("预计金额"))
                gridBatch.Columns["预计金额"].DefaultCellStyle.Format = "N2";

            foreach (DataGridViewColumn column in gridBatch.Columns)
            {
                column.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
                column.HeaderCell.Style.Alignment = DataGridViewContentAlignment.MiddleCenter;
            }
        }

        private bool ValidateBatchInput(out string location, out string spec, out decimal unitPrice, out string handler)
        {
            location = string.Empty;
            spec = string.Empty;
            unitPrice = 0m;
            handler = string.Empty;

            if (!ClientSearchHelper.TryGetSelectedItem(cmbBatchSpec, _specItems, out ClientSearchItem specItem))
            {
                MessageBox.Show("请选择规格！", "提示",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                cmbBatchSpec.Focus();
                return false;
            }

            if (!ClientSearchHelper.TryGetSelectedItem(cmbBatchLocation, _locationItems, out ClientSearchItem locationItem))
            {
                MessageBox.Show("请选择库位！", "提示",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                cmbBatchLocation.Focus();
                return false;
            }

            if (!ClientSearchHelper.TryGetSelectedItem(cmbBatchHandler, _handlerItems, out ClientSearchItem handlerItem))
            {
                MessageBox.Show("请选择经手人！", "提示",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                cmbBatchHandler.Focus();
                return false;
            }

            if (NumericFieldHelper.TryGetDecimal(txtBatchUnitPrice, out decimal parsedPrice))
            {
                if (parsedPrice < 0)
                {
                    MessageBox.Show("单价不能为负数！", "提示",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    txtBatchUnitPrice.Focus();
                    return false;
                }

                if (parsedPrice > 0)
                    unitPrice = parsedPrice;
            }

            if (unitPrice <= 0)
            {
                MessageBox.Show("请填写大于 0 的单价！", "提示",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                txtBatchUnitPrice.Focus();
                return false;
            }

            location = locationItem.Name;
            spec = specItem.Name;
            handler = handlerItem.Name;
            return true;
        }

        private bool ValidateLocationActive(string location)
        {
            DatabaseManager db = new DatabaseManager();
            DataTable locationCheck = db.ExecuteQuery(
                "SELECT status FROM locations WHERE name = @name",
                new Dictionary<string, object> { { "@name", location } });

            if (locationCheck.Rows.Count > 0)
            {
                int status = Convert.ToInt32(locationCheck.Rows[0]["status"]);
                if (status != 1)
                {
                    MessageBox.Show($"所选库位 [{location}] 已被禁用，请重新选择！",
                        "库位不可用", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    LoadData();
                    return false;
                }
            }

            return true;
        }

        private void BtnBatchConfirm_Click(object sender, EventArgs e)
        {
            if (!ValidateBatchInput(out string location, out string spec, out decimal unitPrice, out string handler))
                return;

            if (!ValidateLocationActive(location))
                return;

            RefreshBatchGrid();

            if (_batchQuotableQty <= 0 || _batchRecordCount == 0)
            {
                MessageBox.Show("当前没有待报价客户。", "提示",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            string confirmMessage =
                $"将为 {_batchRecordCount} 个客户各生成一条报价单，合计 {_batchQuotableQty} 件，统一单价 {unitPrice:F2} 元。\n\n此操作不影响实际库存。确定继续吗？";
            if (MessageBox.Show(confirmMessage, "确认批量报价",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return;

            try
            {
                DatabaseManager db = new DatabaseManager();
                BatchSalesResult result = db.BatchCreateSalesFromInbound(location, spec, unitPrice, handler, Environment.UserName);
                if (!result.Success)
                {
                    MessageBox.Show(result.ErrorMessage ?? "批量报价失败。", "错误",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                MessageBox.Show($"批量报价完成！\n\n共生成 {result.SuccessCount} 条报价单\n合计数量：{result.TotalQuantity} 件\n合计金额：{result.TotalAmount:F2} 元",
                    "成功", MessageBoxButtons.OK, MessageBoxIcon.Information);

                RefreshBatchGrid();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"批量报价失败: {ex.Message}", "错误",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void BtnSave_Click(object sender, EventArgs e)
        {
            if (!ValidateInput())
                return;

            try
            {
                if (!ClientSearchHelper.TryGetSelectedClient(cmbClient, _clientItems, out ClientSearchItem clientItem))
                    return;
                if (!ClientSearchHelper.TryGetSelectedItem(cmbSpec, _specItems, out ClientSearchItem specItem))
                    return;
                if (!ClientSearchHelper.TryGetSelectedItem(cmbLocation, _locationItems, out ClientSearchItem locationItem))
                    return;
                if (!ClientSearchHelper.TryGetSelectedItem(cmbHandler, _handlerItems, out ClientSearchItem handlerItem))
                    return;

                string clientName = clientItem.Name;
                string clientCode = clientItem.Code;
                string orderNo = "XS" + DateTime.Now.ToString("yyyyMMddHHmmss") + new Random().Next(100, 999);
                string spec = specItem.Name;
                if (!NumericFieldHelper.TryGetInt(txtQuantity, out int quantity))
                    return;

                decimal unitPrice = 0m;
                if (NumericFieldHelper.TryGetDecimal(txtUnitPrice, out decimal parsedPrice) && parsedPrice > 0)
                    unitPrice = parsedPrice;

                decimal totalAmount = quantity * unitPrice;
                string location = locationItem.Name;
                string handler = handlerItem.Name;

                DatabaseManager db = new DatabaseManager();

                if (!ValidateLocationActive(location))
                    return;

                bool success = db.SaveSalesRecord(
                    orderNo: orderNo,
                    clientCode: clientCode,
                    clientName: clientName,
                    location: location,
                    date: DateTime.Now,
                    spec: spec,
                    quantity: quantity,
                    unitPrice: unitPrice,
                    totalAmount: totalAmount,
                    handler: handler,
                    creator: Environment.UserName
                );

                if (success)
                {
                    string priceInfo = unitPrice > 0 ? $"{unitPrice:F2}元" : "（未填写，待后续改价）";
                    MessageBox.Show($"报账记录保存成功！\n\n单据号: {orderNo}\n客户: {clientName}\n规格: {spec}\n数量: {quantity}\n单价: {priceInfo}\n总金额: {totalAmount:F2}元",
                        "成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    ClearSingleForm();
                    RefreshBatchGrid();
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

            if (!ClientSearchHelper.TryGetSelectedItem(cmbSpec, _specItems, out _))
            {
                MessageBox.Show("请选择规格！", "提示",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                cmbSpec.Focus();
                return false;
            }

            if (!NumericFieldHelper.TryGetInt(txtQuantity, out int quantity) || quantity <= 0)
            {
                MessageBox.Show("请输入有效数量！", "提示",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                txtQuantity.Focus();
                return false;
            }

            if (NumericFieldHelper.TryGetDecimal(txtUnitPrice, out decimal unitPrice) && unitPrice < 0)
            {
                MessageBox.Show("单价不能为负数！", "提示",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                txtUnitPrice.Focus();
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

            return true;
        }

        private void ClearSingleForm()
        {
            ClientSearchHelper.ResetToPlaceholder(cmbClient);
            ClientSearchHelper.ResetToPlaceholder(cmbSpec);
            ClientSearchHelper.ResetToPlaceholder(cmbLocation);
            ClientSearchHelper.ResetToPlaceholder(cmbHandler);
            NumericFieldHelper.ResetToPlaceholder(txtQuantity);
            NumericFieldHelper.ResetToPlaceholder(txtUnitPrice);
            txtTotalAmount.Text = string.Empty;
            cmbClient.Focus();
        }
    }
}
