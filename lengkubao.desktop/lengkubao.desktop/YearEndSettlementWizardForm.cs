using System;
using System.Drawing;
using System.Text;
using System.Windows.Forms;

namespace lengkubao.desktop
{
    public class YearEndSettlementWizardForm : Form
    {
        private readonly YearEndSettlementService _service = new YearEndSettlementService();
        private int _step = 0;
        private int _fiscalYear;
        private string _inventoryCarryover = DatabaseManager.InventoryCarryoverEmpty;
        private DatabaseManager.YearEndPreviewData _preview;

        private Panel panelSteps;
        private Label lblStepTitle;
        private TextBox txtPreview;
        private NumericUpDown numYear;
        private TextBox txtConfirmYear;
        private RadioButton rbOpeningInbound;
        private RadioButton rbEmptyInventory;
        private Button btnBack;
        private Button btnNext;
        private Button btnCancel;

        public bool SettlementCompleted { get; private set; }

        public YearEndSettlementWizardForm()
        {
            _fiscalYear = DateTime.Now.Year - 1;
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            Text = "年度终结向导";
            Size = new Size(720, 560);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;

            lblStepTitle = new Label
            {
                Location = new Point(20, 15),
                Size = new Size(660, 28),
                Font = new Font("微软雅黑", 11f, FontStyle.Bold)
            };
            Controls.Add(lblStepTitle);

            panelSteps = new Panel
            {
                Location = new Point(20, 50),
                Size = new Size(660, 400),
                BorderStyle = BorderStyle.FixedSingle
            };
            Controls.Add(panelSteps);

            btnBack = new Button { Text = "上一步", Location = new Point(400, 470), Size = new Size(90, 32) };
            btnBack.Click += BtnBack_Click;
            Controls.Add(btnBack);

            btnNext = new Button { Text = "下一步", Location = new Point(500, 470), Size = new Size(90, 32) };
            btnNext.Click += BtnNext_Click;
            Controls.Add(btnNext);

            btnCancel = new Button { Text = "取消", Location = new Point(600, 470), Size = new Size(90, 32), DialogResult = DialogResult.Cancel };
            Controls.Add(btnCancel);

            ShowStep(0);
        }

        private void ShowStep(int step)
        {
            _step = step;
            panelSteps.Controls.Clear();
            btnBack.Enabled = step > 0;
            btnNext.Text = step == 3 ? "执行年结" : "下一步";

            switch (step)
            {
                case 0:
                    BuildStepPreview();
                    lblStepTitle.Text = "步骤 1/4 — 预览与校验";
                    break;
                case 1:
                    BuildStepArchiveInfo();
                    lblStepTitle.Text = "步骤 2/4 — 数据归档";
                    break;
                case 2:
                    BuildStepCarryover();
                    lblStepTitle.Text = "步骤 3/4 — 结转确认";
                    break;
                case 3:
                    BuildStepConfirm();
                    lblStepTitle.Text = "步骤 4/4 — 执行确认";
                    break;
            }
        }

        private void BuildStepPreview()
        {
            var lbl = new Label
            {
                Text = "结算年度：",
                Location = new Point(15, 20),
                AutoSize = true
            };
            panelSteps.Controls.Add(lbl);

            int defaultYear = _fiscalYear >= 2000 ? _fiscalYear : DateTime.Now.Year - 1;
            numYear = new NumericUpDown
            {
                Location = new Point(100, 16),
                Width = 100,
                Minimum = 2000,
                Maximum = 2100,
                Value = defaultYear
            };
            _fiscalYear = defaultYear;
            numYear.ValueChanged += (s, e) => _fiscalYear = (int)numYear.Value;
            panelSteps.Controls.Add(numYear);

            var btnRefresh = new Button
            {
                Text = "刷新预览",
                Location = new Point(220, 14),
                Size = new Size(90, 28)
            };
            btnRefresh.Click += (s, e) => RefreshPreview();
            panelSteps.Controls.Add(btnRefresh);

            txtPreview = new TextBox
            {
                Location = new Point(15, 55),
                Size = new Size(625, 325),
                Multiline = true,
                ScrollBars = ScrollBars.Vertical,
                ReadOnly = true,
                Font = new Font("Consolas", 9f)
            };
            panelSteps.Controls.Add(txtPreview);

            RefreshPreview();
        }

        private void RefreshPreview()
        {
            _fiscalYear = (int)numYear.Value;
            _preview = _service.LoadPreview(_fiscalYear);

            var sb = new StringBuilder();
            sb.AppendLine($"=== {_fiscalYear} 年度终结预览 ===");
            if (_preview.AlreadySettled)
                sb.AppendLine("⚠ 该年度已执行过年终结，不可重复操作。");
            sb.AppendLine();
            sb.AppendLine($"入库记录：{_preview.InboundCount} 笔");
            sb.AppendLine($"销售记录：{_preview.SalesCount} 笔");
            sb.AppendLine($"包装记录：{_preview.PackagingCount} 笔");
            sb.AppendLine($"收支流水：{_preview.LedgerCount} 笔");
            sb.AppendLine($"收支利润：{_preview.LedgerProfit:N2} 元（收入 {_preview.LedgerIncome:N2} / 支出 {_preview.LedgerExpense:N2}）");
            sb.AppendLine();
            sb.AppendLine($"待结转客户期初应收：{_preview.OpeningBalances.Count} 户，合计 {_preview.OpeningBalanceTotal:N2} 元");
            sb.AppendLine($"期末库存行数：{_preview.InventoryRows.Count}（库存>0 的库位+规格）");
            if (_preview.InventoryRows.Count > 0)
            {
                sb.AppendLine("库存明细（前10行）：");
                for (int i = 0; i < Math.Min(10, _preview.InventoryRows.Count); i++)
                {
                    var row = _preview.InventoryRows[i];
                    sb.AppendLine($"  {row.Location} / {row.Spec}：{row.Quantity} 件");
                }
            }
            sb.AppendLine();
            sb.AppendLine("执行前将自动创建「年结前」备份。");
            txtPreview.Text = sb.ToString();
        }

        private void BuildStepArchiveInfo()
        {
            if (_preview == null)
                _preview = _service.LoadPreview(_fiscalYear);

            var lbl = new Label
            {
                Location = new Point(15, 20),
                Size = new Size(620, 340),
                Font = new Font("微软雅黑", 10f),
                Text = $"即将归档 {_fiscalYear} 年度数据，生成：\r\n\r\n" +
                       $"• 完整数据库快照 lengkubao_{_fiscalYear}.db\r\n" +
                       $"• 年度利润汇总_{_fiscalYear}.xlsx\r\n" +
                       $"• 客户对账汇总_{_fiscalYear}.xlsx\r\n" +
                       $"• 库存结转表_{_fiscalYear}.xlsx\r\n" +
                       $"• archive_meta.json\r\n\r\n" +
                       $"保存位置：\r\n{YearEndArchiveManager.GetYearArchiveDir(_fiscalYear)}\r\n\r\n" +
                       "归档完成后才会清空当前业务数据并开启新年度。"
            };
            panelSteps.Controls.Add(lbl);
        }

        private void BuildStepCarryover()
        {
            if (_preview == null)
                _preview = _service.LoadPreview(_fiscalYear);

            var lblBalance = new Label
            {
                Location = new Point(15, 15),
                Size = new Size(620, 60),
                Text = $"客户期初应收将自动结转 {_preview.OpeningBalances.Count} 户，" +
                       $"合计 {_preview.OpeningBalanceTotal:N2} 元，生效日期 {_fiscalYear + 1}-01-01。"
            };
            panelSteps.Controls.Add(lblBalance);

            var lblInv = new Label
            {
                Location = new Point(15, 85),
                Size = new Size(620, 40),
                Font = new Font("微软雅黑", 10f, FontStyle.Bold),
                Text = _preview.InventoryRows.Count > 0
                    ? $"检测到 {_preview.InventoryRows.Count} 条库存记录，请选择新年度库存处理方式："
                    : "当前无库存，新年度将从空库存开始。"
            };
            panelSteps.Controls.Add(lblInv);

            rbOpeningInbound = new RadioButton
            {
                Text = "生成期初入库记录（推荐，保留期末库存）",
                Location = new Point(30, 130),
                Size = new Size(580, 24),
                Enabled = _preview.InventoryRows.Count > 0,
                Checked = _preview.InventoryRows.Count > 0
            };
            panelSteps.Controls.Add(rbOpeningInbound);

            rbEmptyInventory = new RadioButton
            {
                Text = "从空库存开始（不写入期初入库）",
                Location = new Point(30, 165),
                Size = new Size(580, 24),
                Checked = _preview.InventoryRows.Count == 0
            };
            panelSteps.Controls.Add(rbEmptyInventory);
        }

        private void BuildStepConfirm()
        {
            var lbl = new Label
            {
                Location = new Point(15, 15),
                Size = new Size(620, 120),
                ForeColor = Color.DarkRed,
                Text = "⚠ 此操作不可撤销（除非从备份恢复）。\r\n\r\n" +
                       "将清空：入库、销售、包装、预支、扣款、预售、收支流水等业务数据。\r\n" +
                       "将保留：客户、商品、库位、经手人、包装类型等基础资料。\r\n\r\n" +
                       $"请在下方输入年度数字 {_fiscalYear} 以确认："
            };
            panelSteps.Controls.Add(lbl);

            txtConfirmYear = new TextBox
            {
                Location = new Point(15, 145),
                Width = 150
            };
            panelSteps.Controls.Add(txtConfirmYear);
        }

        private void BtnBack_Click(object sender, EventArgs e)
        {
            if (_step > 0)
                ShowStep(_step - 1);
        }

        private void BtnNext_Click(object sender, EventArgs e)
        {
            if (_step == 0)
            {
                _fiscalYear = (int)numYear.Value;
                _preview = _service.LoadPreview(_fiscalYear);
                if (_preview.AlreadySettled)
                {
                    MessageBox.Show($"{_fiscalYear} 年度已执行过年终结。", "提示",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
            }

            if (_step == 2)
            {
                _inventoryCarryover = rbOpeningInbound != null && rbOpeningInbound.Checked
                    ? DatabaseManager.InventoryCarryoverOpeningInbound
                    : DatabaseManager.InventoryCarryoverEmpty;
            }

            if (_step == 3)
            {
                if (txtConfirmYear.Text.Trim() != _fiscalYear.ToString())
                {
                    MessageBox.Show($"请输入 {_fiscalYear} 以确认执行。", "确认",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                ExecuteSettlement();
                return;
            }

            ShowStep(_step + 1);
        }

        private void ExecuteSettlement()
        {
            btnNext.Enabled = false;
            btnBack.Enabled = false;
            try
            {
                Cursor = Cursors.WaitCursor;
                try
                {
                    string preBackup = _service.CreatePreSettlementBackup();
                    Console.WriteLine($">>> 年结前备份: {preBackup}");
                }
                catch (Exception ex)
                {
                    if (MessageBox.Show($"创建年结前备份失败：{ex.Message}\n\n是否仍继续？",
                            "警告", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                        return;
                }

                var settleResult = _service.ArchiveAndSettle(
                    _fiscalYear,
                    _inventoryCarryover,
                    Environment.UserName);

                if (!settleResult.Success)
                {
                    MessageBox.Show($"年度终结失败：{settleResult.ErrorMessage}", "错误",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                SettlementCompleted = true;
                MessageBox.Show(
                    $"✅ {_fiscalYear} 年度终结完成！\n\n" +
                    $"归档文件：\n{settleResult.ArchivePath}\n\n" +
                    "建议：\n1. 重启应用程序\n2. 在手持端执行全量同步",
                    "完成",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                DialogResult = DialogResult.OK;
                Close();
            }
            finally
            {
                Cursor = Cursors.Default;
                btnNext.Enabled = true;
                btnBack.Enabled = _step > 0;
            }
        }
    }
}
