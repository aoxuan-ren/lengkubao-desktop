using System;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;

namespace lengkubao.desktop
{
    /// <summary>
    /// 数量、单价等数字输入框：初始显示占位提示，获得焦点时清空便于输入。
    /// </summary>
    public static class NumericFieldHelper
    {
        public const string QuantityPlaceholder = "请输入数量";
        public const string UnitPricePlaceholder = "请输入单价";

        public static void BindQuantityField(TextBox textBox)
        {
            BindField(textBox, QuantityPlaceholder, integerOnly: true);
        }

        public static void BindUnitPriceField(TextBox textBox)
        {
            BindField(textBox, UnitPricePlaceholder, integerOnly: false);
        }

        public static void ResetToPlaceholder(TextBox textBox)
        {
            if (textBox == null)
                return;

            var state = GetState(textBox);
            if (state == null)
                return;

            ShowPlaceholder(textBox, state);
        }

        public static bool TryGetInt(TextBox textBox, out int value)
        {
            value = 0;
            string text = GetEffectiveText(textBox);
            if (string.IsNullOrWhiteSpace(text))
                return false;

            return int.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value)
                || int.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.CurrentCulture, out value);
        }

        public static bool TryGetDecimal(TextBox textBox, out decimal value)
        {
            value = 0m;
            string text = GetEffectiveText(textBox);
            if (string.IsNullOrWhiteSpace(text))
                return false;

            return decimal.TryParse(text.Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out value)
                || decimal.TryParse(text.Trim(), NumberStyles.Number, CultureInfo.CurrentCulture, out value);
        }

        private static void BindField(TextBox textBox, string placeholder, bool integerOnly)
        {
            if (textBox == null)
                return;

            textBox.Tag = new FieldState
            {
                Placeholder = placeholder,
                IntegerOnly = integerOnly
            };

            textBox.GotFocus += (s, e) => OnGotFocus(textBox);
            textBox.Leave += (s, e) => OnLeave(textBox);
            textBox.KeyPress += (s, e) => OnKeyPress(textBox, e);

            ShowPlaceholder(textBox, GetState(textBox));
        }

        private static void OnGotFocus(TextBox textBox)
        {
            var state = GetState(textBox);
            if (state == null)
                return;

            if (state.InternalUpdate)
                return;

            string text = textBox.Text ?? string.Empty;
            if (text == state.Placeholder || string.IsNullOrWhiteSpace(text))
            {
                state.InternalUpdate = true;
                try
                {
                    textBox.ForeColor = Color.Black;
                    textBox.Text = string.Empty;
                }
                finally
                {
                    state.InternalUpdate = false;
                }
                return;
            }

            textBox.BeginInvoke(new Action(() =>
            {
                if (!textBox.IsDisposed && textBox.Focused)
                    textBox.SelectAll();
            }));
        }

        private static void OnLeave(TextBox textBox)
        {
            var state = GetState(textBox);
            if (state == null || state.InternalUpdate)
                return;

            if (string.IsNullOrWhiteSpace(GetEffectiveText(textBox)))
                ShowPlaceholder(textBox, state);
        }

        private static void OnKeyPress(TextBox textBox, KeyPressEventArgs e)
        {
            var state = GetState(textBox);
            if (state == null || !state.IntegerOnly)
                return;

            if (char.IsControl(e.KeyChar))
                return;

            if (!char.IsDigit(e.KeyChar))
                e.Handled = true;
        }

        private static void ShowPlaceholder(TextBox textBox, FieldState state)
        {
            state.InternalUpdate = true;
            try
            {
                textBox.ForeColor = Color.Gray;
                textBox.Text = state.Placeholder;
            }
            finally
            {
                state.InternalUpdate = false;
            }
        }

        private static string GetEffectiveText(TextBox textBox)
        {
            if (textBox == null)
                return string.Empty;

            string text = textBox.Text?.Trim() ?? string.Empty;
            var state = GetState(textBox);
            if (state != null && text == state.Placeholder)
                return string.Empty;

            return text;
        }

        private static FieldState GetState(TextBox textBox)
        {
            return textBox?.Tag as FieldState;
        }

        private sealed class FieldState
        {
            public string Placeholder { get; set; }
            public bool IntegerOnly { get; set; }
            public bool InternalUpdate { get; set; }
        }
    }
}
