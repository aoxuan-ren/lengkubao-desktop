using System;

namespace lengkubao.desktop
{
    internal static class PresaleHelper
    {
        public const string SaleModePresale = "PRESALE";
        public const string SaleModeDirectOut = "DIRECT_OUT";
        public const string SaleModePresaleLabel = "预售";
        public const string SaleModeSoldLabel = "已售";
        public const string SaleModeSoldOutLabel = "已售完";

        public static string FormatSaleModeDisplay(object value)
        {
            string raw = value?.ToString()?.Trim() ?? string.Empty;
            if (string.IsNullOrEmpty(raw))
                return string.Empty;

            switch (raw.ToUpperInvariant())
            {
                case SaleModePresale:
                case "预售":
                    return SaleModePresaleLabel;
                case SaleModeDirectOut:
                case "已售":
                case "出库销售":
                    return SaleModeSoldLabel;
                default:
                    return raw;
            }
        }

        public static string ToDbSaleMode(object value)
        {
            string raw = value?.ToString()?.Trim() ?? string.Empty;
            if (string.IsNullOrEmpty(raw))
                return string.Empty;

            switch (raw)
            {
                case SaleModePresaleLabel:
                case SaleModePresale:
                    return SaleModePresale;
                case SaleModeSoldLabel:
                case SaleModeDirectOut:
                case "出库销售":
                    return SaleModeDirectOut;
                default:
                    return raw;
            }
        }
    }
}
