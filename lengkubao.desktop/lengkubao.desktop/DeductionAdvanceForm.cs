using Microsoft.VisualBasic;
using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace lengkubao.desktop
{
    public partial class DeductionAdvanceForm : Form
    {
        private string transactionType; // "扣款" 或 "预支"
        private string clientCode;
        private string clientName;
        private bool isEditMode = false;
        private Dictionary<string, object> originalData = null;

        private TextBox txtQuantity;
        private TextBox txtUnitPrice;
        private TextBox txtAmount;
        private DateTimePicker datePicker;
        private ComboBox comboHandler;
        private List<ClientSearchItem> _handlerItems = new List<ClientSearchItem>();
        private TextBox txtReason;
        private Button btnSave;
        private Button btnCancel;

        private bool IsDeduction => transactionType == "扣款";

        public DeductionAdvanceForm(string type, string code, string name)
        {
            transactionType = type;
            clientCode = code;
            clientName = name;
            isEditMode = false;

            InitializeForm();
            LoadHandlers();
        }

        public DeductionAdvanceForm(string type, string code, string name, bool editMode = false)
        {
            transactionType = type;
            clientCode = code;
            clientName = name;
            isEditMode = editMode;

            InitializeForm();
            LoadHandlers();

            if (isEditMode)
            {
                Text = $"编辑{type}记录";
                btnSave.Text = "保存";
            }
        }

        public void SetEditMode(DateTime date, decimal amount, string handler, string reason,
            int quantity = 0, decimal unitPrice = 0)
        {
            isEditMode = true;
            Text = $"编辑{transactionType}记录";
            btnSave.Text = "更新";

            originalData = new Dictionary<string, object>
            {
                { "date", date },
                { "amount", amount },
                { "handler", handler },
                { "reason", reason }
            };

            datePicker.Value = date;
            ApplyDeductionAmountFields(amount, quantity, unitPrice);
            comboHandler.ForeColor = Color.Black;
            comboHandler.Text = handler;
            txtReason.Text = reason;
            txtReason.ForeColor = Color.Black;
        }

        private void InitializeForm()
        {
            string title = transactionType == "扣款" ? "添加扣款记录" : "添加预支记录";

            Text = title;
            Size = new Size(400, IsDeduction ? 420 : 300);
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            BackColor = Color.White;

            int yPos = 20;
            int labelWidth = 80;
            int controlWidth = 250;

            AddLabel("客户:", 20, yPos, labelWidth);
            var txtClient = new TextBox
            {
                Text = $"{clientName} ({clientCode})",
                Location = new Point(110, yPos),
                Size = new Size(controlWidth, 25),
                ReadOnly = true,
                BackColor = Color.WhiteSmoke
            };
            Controls.Add(txtClient);
            yPos += 40;

            if (IsDeduction)
            {
                AddLabel("数量:", 20, yPos, labelWidth);
                txtQuantity = new TextBox
                {
                    Location = new Point(110, yPos),
                    Size = new Size(controlWidth, 25)
                };
                NumericFieldHelper.BindQuantityField(txtQuantity);
                txtQuantity.TextChanged += (s, e) => CalculateAmount();
                Controls.Add(txtQuantity);
                yPos += 40;

                AddLabel("单价:", 20, yPos, labelWidth);
                txtUnitPrice = new TextBox
                {
                    Location = new Point(110, yPos),
                    Size = new Size(controlWidth, 25)
                };
                NumericFieldHelper.BindUnitPriceField(txtUnitPrice);
                txtUnitPrice.TextChanged += (s, e) => CalculateAmount();
                Controls.Add(txtUnitPrice);
                yPos += 40;
            }

            AddLabel("金额:", 20, yPos, labelWidth);
            txtAmount = new TextBox
            {
                Location = new Point(110, yPos),
                Size = new Size(controlWidth, 25),
                Text = IsDeduction ? string.Empty : "0.00",
                ReadOnly = IsDeduction,
                BackColor = IsDeduction ? Color.WhiteSmoke : Color.White
            };
            Controls.Add(txtAmount);
            yPos += 40;

            AddLabel(transactionType == "扣款" ? "扣款日期:" : "预支日期:", 20, yPos, labelWidth);
            datePicker = new DateTimePicker
            {
                Location = new Point(110, yPos),
                Size = new Size(controlWidth, 25),
                Format = DateTimePickerFormat.Short,
                Value = DateTime.Now
            };
            Controls.Add(datePicker);
            yPos += 40;

            AddLabel("经手人:", 20, yPos, labelWidth);
            comboHandler = new ComboBox
            {
                Location = new Point(110, yPos),
                Size = new Size(controlWidth, 25)
            };
            ClientSearchHelper.ApplySearchableStyle(comboHandler);
            Controls.Add(comboHandler);
            yPos += 40;

            AddLabel("原因:", 20, yPos, labelWidth);
            txtReason = new TextBox
            {
                Location = new Point(110, yPos),
                Size = new Size(controlWidth, 25),
                ForeColor = Color.Gray,
                Text = "请输入扣款/预支原因"
            };
            txtReason.Enter += (s, e) =>
            {
                if (txtReason.Text == "请输入扣款/预支原因")
                {
                    txtReason.Text = "";
                    txtReason.ForeColor = Color.Black;
                }
            };
            txtReason.Leave += (s, e) =>
            {
                if (string.IsNullOrWhiteSpace(txtReason.Text))
                {
                    txtReason.Text = "请输入扣款/预支原因";
                    txtReason.ForeColor = Color.Gray;
                }
            };
            Controls.Add(txtReason);

            if (IsDeduction)
            {
                var btnFillRefrigerationFee = new Button
                {
                    Text = "填入：制冷费",
                    Location = new Point(110, yPos + 32),
                    Size = new Size(120, 28),
                    FlatStyle = FlatStyle.Flat,
                    BackColor = Color.FromArgb(236, 240, 241),
                    ForeColor = Color.FromArgb(44, 62, 80)
                };
                btnFillRefrigerationFee.FlatAppearance.BorderColor = Color.FromArgb(189, 195, 199);
                btnFillRefrigerationFee.Click += (s, e) =>
                {
                    txtReason.Text = DatabaseManager.RefrigerationFeeReason;
                    txtReason.ForeColor = Color.Black;
                };
                Controls.Add(btnFillRefrigerationFee);
            }

            yPos += IsDeduction ? 80 : 50;

            btnSave = new Button
            {
                Text = "保存",
                Location = new Point(110, yPos),
                Size = new Size(100, 30),
                BackColor = transactionType == "扣款"
                    ? Color.FromArgb(192, 57, 43) : Color.FromArgb(155, 89, 182),
                ForeColor = Color.White
            };
            btnSave.Click += BtnSave_Click;
            Controls.Add(btnSave);

            btnCancel = new Button
            {
                Text = "取消",
                Location = new Point(220, yPos),
                Size = new Size(100, 30),
                BackColor = Color.FromArgb(240, 240, 240)
            };
            btnCancel.Click += (s, e) => DialogResult = DialogResult.Cancel;
            Controls.Add(btnCancel);
        }

        private void AddLabel(string text, int x, int y, int width)
        {
            var label = new Label
            {
                Text = text,
                Location = new Point(x, y),
                Size = new Size(width, 25),
                TextAlign = ContentAlignment.MiddleRight
            };
            Controls.Add(label);
        }

        public void SetEditValues(string orderNo, DateTime date, decimal amount, string handler, string reason)
        {
            datePicker.Value = date;
            ApplyDeductionAmountFields(amount);
            comboHandler.ForeColor = Color.Black;
            comboHandler.Text = handler;
            txtReason.Text = reason;
            txtReason.ForeColor = Color.Black;
        }

        private void ApplyDeductionAmountFields(decimal amount, int quantity = 0, decimal unitPrice = 0)
        {
            if (IsDeduction)
            {
                int displayQty = quantity > 0 ? quantity : 1;
                decimal displayPrice = unitPrice > 0 ? unitPrice : amount;

                txtQuantity.ForeColor = Color.Black;
                txtQuantity.Text = displayQty.ToString();
                txtUnitPrice.ForeColor = Color.Black;
                txtUnitPrice.Text = displayPrice.ToString("0.00");
                CalculateAmount();
            }
            else
            {
                txtAmount.Text = amount.ToString("0.00");
            }
        }

        private void CalculateAmount()
        {
            if (!IsDeduction)
                return;

            if (NumericFieldHelper.TryGetInt(txtQuantity, out int quantity)
                && NumericFieldHelper.TryGetDecimal(txtUnitPrice, out decimal unitPrice))
            {
                txtAmount.Text = (quantity * unitPrice).ToString("0.00");
                txtAmount.ForeColor = Color.Black;
            }
            else
            {
                txtAmount.Text = string.Empty;
            }
        }

        private void LoadHandlers()
        {
            try
            {
                DatabaseManager db = new DatabaseManager();
                DataTable handlers = db.GetAllHandlers();

                var handlerNames = new List<string>();
                foreach (DataRow row in handlers.Rows)
                    handlerNames.Add(row["name"].ToString());

                _handlerItems = ClientSearchHelper.BuildSimpleItems(handlerNames);
                ClientSearchHelper.BindSearchableCombo(comboHandler, _handlerItems, new ClientSearchHelper.SearchComboOptions
                {
                    PlaceholderText = ClientSearchHelper.NamePlaceholderText
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"加载经手人失败: {ex.Message}", "错误");
            }
        }

        private bool TryGetDeductionFields(out int quantity, out decimal unitPrice, out decimal amount)
        {
            quantity = 0;
            unitPrice = 0m;
            amount = 0m;

            if (!NumericFieldHelper.TryGetInt(txtQuantity, out quantity) || quantity <= 0)
            {
                MessageBox.Show("请输入有效的数量（必须大于0）！", "验证错误");
                txtQuantity.Focus();
                return false;
            }

            if (!NumericFieldHelper.TryGetDecimal(txtUnitPrice, out unitPrice) || unitPrice <= 0)
            {
                MessageBox.Show("请输入有效的单价（必须大于0）！", "验证错误");
                txtUnitPrice.Focus();
                return false;
            }

            amount = quantity * unitPrice;
            txtAmount.Text = amount.ToString("0.00");
            return true;
        }

        private bool TryGetAmount(out decimal amount, out int quantity, out decimal unitPrice)
        {
            quantity = 0;
            unitPrice = 0m;
            amount = 0m;

            if (IsDeduction)
                return TryGetDeductionFields(out quantity, out unitPrice, out amount);

            if (!decimal.TryParse(txtAmount.Text, out amount) || amount <= 0)
            {
                MessageBox.Show("请输入有效的金额（必须大于0）！", "验证错误");
                txtAmount.Focus();
                txtAmount.SelectAll();
                return false;
            }

            return true;
        }

        private void BtnSave_Click(object sender, EventArgs e)
        {
            try
            {
                if (!TryGetAmount(out decimal amount, out int quantity, out decimal unitPrice))
                    return;

                if (!ClientSearchHelper.TryGetSelectedItem(comboHandler, _handlerItems, out ClientSearchItem handlerItem))
                {
                    MessageBox.Show("请选择经手人！", "验证错误");
                    comboHandler.Focus();
                    return;
                }

                string reason = txtReason.Text.Trim();
                if (string.IsNullOrWhiteSpace(reason) || reason == "请输入扣款/预支原因")
                {
                    MessageBox.Show("请输入扣款/预支原因！", "验证错误");
                    txtReason.Focus();
                    return;
                }

                string handler = handlerItem.Name;
                DateTime transDate = datePicker.Value;

                DatabaseManager db = new DatabaseManager();
                bool success = false;

                if (isEditMode && originalData != null)
                {
                    Debug.WriteLine($"=== 编辑模式 ===");
                    Debug.WriteLine($"交易类型: {transactionType}");
                    Debug.WriteLine($"客户代码: {clientCode}");
                    Debug.WriteLine($"原始数据: {string.Join(", ", originalData.Select(kv => $"{kv.Key}={kv.Value}"))}");
                    Debug.WriteLine($"新数据: 日期={transDate}, 金额={amount}, 经手人={handler}, 原因={reason}");

                    if (transactionType == "扣款")
                    {
                        success = db.UpdateDeduction(
                            clientCode,
                            originalData,
                            new Dictionary<string, object>
                            {
                                { "date", transDate },
                                { "amount", amount },
                                { "quantity", quantity },
                                { "unit_price", unitPrice },
                                { "handler", handler },
                                { "reason", reason }
                            });
                    }
                    else
                    {
                        success = db.UpdateAdvance(
                            clientCode,
                            originalData,
                            new Dictionary<string, object>
                            {
                                { "date", transDate },
                                { "amount", amount },
                                { "handler", handler },
                                { "reason", reason }
                            });
                    }

                    Debug.WriteLine($"更新结果: {success}");
                }
                else
                {
                    Debug.WriteLine($"=== 添加模式 ===");

                    if (transactionType == "扣款")
                    {
                        success = db.AddDeduction(clientCode, clientName, amount,
                            transDate, reason, handler, Environment.UserName, quantity, unitPrice);
                    }
                    else
                    {
                        success = db.AddAdvance(clientCode, clientName, amount,
                            transDate, reason, handler, Environment.UserName);
                    }
                }

                if (success)
                {
                    MessageBox.Show($"{transactionType}记录{(isEditMode ? "更新" : "保存")}成功！", "成功");
                    DialogResult = DialogResult.OK;
                }
                else
                {
                    MessageBox.Show($"{transactionType}记录{(isEditMode ? "更新" : "保存")}失败！", "错误");
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"操作失败: {ex.Message}", "错误");
                Debug.WriteLine($"保存异常: {ex.Message}\n{ex.StackTrace}");
            }
        }
    }
}
