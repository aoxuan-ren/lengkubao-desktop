using System;
using System.Data;
using System.Drawing;
using System.Text;
using System.Windows.Forms;

namespace lengkubao.desktop
{
    public class ClientBalanceSummaryResult
    {
        public decimal PayableTotal { get; set; }
        public string SummaryText { get; set; }
        public Color TextColor { get; set; } = Color.Black;
        public FontStyle TextStyle { get; set; } = FontStyle.Regular;
    }

    public static class ClientBalanceSummaryHelper
    {
        public static ClientBalanceSummaryResult Calculate(DataTable detailsData)
        {
            decimal salesTotal = 0;
            decimal packagingTotal = 0;
            decimal deductionTotal = 0;
            decimal advanceTotal = 0;

            if (detailsData != null)
            {
                foreach (DataRow row in detailsData.Rows)
                {
                    string type = row["Type"]?.ToString() ?? "";
                    decimal amount = row["Amount"] != DBNull.Value ? Convert.ToDecimal(row["Amount"]) : 0;

                    bool isSettled = row.Table.Columns.Contains("IsSettled") &&
                                     Convert.ToBoolean(row["IsSettled"]);

                    switch (type)
                    {
                        case "销售":
                            if (!isSettled)
                                salesTotal += Math.Abs(amount);
                            break;
                        case "包装":
                            if (!isSettled)
                                packagingTotal += amount;
                            break;
                        case "扣款":
                            deductionTotal += Math.Abs(amount);
                            break;
                        case "预支":
                            advanceTotal += Math.Abs(amount);
                            break;
                    }
                }
            }

            decimal openingFromDetails = 0;
            if (detailsData != null)
            {
                foreach (DataRow row in detailsData.Rows)
                {
                    if (row["Type"]?.ToString() == "期初余额" && row["Amount"] != DBNull.Value)
                        openingFromDetails += Convert.ToDecimal(row["Amount"]);
                }
            }

            decimal payableTotal = openingFromDetails + salesTotal - packagingTotal - advanceTotal - deductionTotal;

            var overviewText = new StringBuilder();
            overviewText.AppendLine("【计算过程】");
            if (openingFromDetails != 0)
                overviewText.AppendLine($"期初余额：{openingFromDetails,12:N2} 元");
            overviewText.AppendLine($"销售总款：{salesTotal,12:N2} 元");

            if (packagingTotal > 0)
                overviewText.AppendLine($"出包装总款：-{packagingTotal,12:N2} 元");
            else if (packagingTotal < 0)
                overviewText.AppendLine($"进包装总款：+{Math.Abs(packagingTotal),12:N2} 元");

            if (advanceTotal > 0)
                overviewText.AppendLine($"预支款项：-{advanceTotal,12:N2} 元");
            if (deductionTotal > 0)
                overviewText.AppendLine($"其他扣款：-{deductionTotal,12:N2} 元");

            overviewText.AppendLine(new string('─', 30));

            decimal totalDeductions = packagingTotal + advanceTotal + deductionTotal;
            overviewText.AppendLine($"扣款合计：{totalDeductions,12:N2} 元");
            overviewText.AppendLine($"应付总款：{payableTotal,12:N2} 元");
            overviewText.AppendLine();

            string paymentDirection;
            if (payableTotal > 0)
                paymentDirection = $"※ 公司应向客户支付：{payableTotal:N2} 元";
            else if (payableTotal < 0)
                paymentDirection = $"※ 客户应向公司支付：{Math.Abs(payableTotal):N2} 元";
            else
                paymentDirection = "※ 账目已结清";

            overviewText.AppendLine(paymentDirection);

            var result = new ClientBalanceSummaryResult
            {
                PayableTotal = payableTotal,
                SummaryText = overviewText.ToString()
            };

            if (payableTotal > 0)
            {
                result.TextColor = Color.FromArgb(0, 128, 0);
                result.TextStyle = FontStyle.Bold;
            }
            else if (payableTotal < 0)
            {
                result.TextColor = Color.FromArgb(192, 0, 0);
                result.TextStyle = FontStyle.Bold;
            }

            return result;
        }

        public static void ApplyToRichTextBox(RichTextBox textBox, ClientBalanceSummaryResult summary)
        {
            if (textBox == null || summary == null)
                return;

            textBox.Text = summary.SummaryText;
            textBox.Font = new Font("微软雅黑", 10, FontStyle.Regular);
            textBox.WordWrap = true;

            if (textBox.TextLength > 0)
            {
                textBox.SelectAll();
                textBox.SelectionColor = summary.TextColor;
                textBox.SelectionFont = new Font(textBox.Font.FontFamily, textBox.Font.Size, summary.TextStyle);
                textBox.DeselectAll();
            }
        }
    }
}
