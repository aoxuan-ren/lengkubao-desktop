using System;
using System.Text;

namespace lengkubao.desktop
{
    /// <summary>
    /// 中文拼音首字母工具（GB2312 区间查表，离线可用）。
    /// </summary>
    public static class PinyinHelper
    {
        // GB2312 区位码边界（23 组 + 结束边界）
        private static readonly int[] AreaBounds =
        {
            45217, 45253, 45761, 46318, 46826, 47010, 47297, 47614,
            48119, 49062, 49324, 49896, 50371, 50614, 50622, 50906,
            51387, 51446, 52218, 52698, 52980, 53689, 54481, 55290
        };

        private static readonly char[] FirstLetters =
        {
            'a', 'b', 'c', 'd', 'e', 'f', 'g', 'h', 'j', 'k', 'l', 'm',
            'n', 'o', 'p', 'q', 'r', 's', 't', 'w', 'x', 'y', 'z'
        };

        /// <summary>
        /// 获取文本的拼音首字母串（小写），英文数字保留。
        /// </summary>
        public static string GetInitials(string text)
        {
            if (string.IsNullOrEmpty(text))
                return string.Empty;

            var sb = new StringBuilder(text.Length);
            foreach (char c in text)
            {
                if ((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9'))
                {
                    sb.Append(c);
                }
                else if (c >= 'A' && c <= 'Z')
                {
                    sb.Append(char.ToLower(c));
                }
                else if (c >= 0x4e00 && c <= 0x9fa5)
                {
                    char letter = GetFirstLetter(c);
                    if (letter != '#')
                        sb.Append(letter);
                }
            }

            return sb.ToString();
        }

        private static char GetFirstLetter(char chinese)
        {
            try
            {
                byte[] bytes = Encoding.GetEncoding("GB2312").GetBytes(new[] { chinese });
                if (bytes.Length < 2)
                    return '#';

                int b0 = bytes[0] < 0 ? bytes[0] + 256 : bytes[0];
                int b1 = bytes[1] < 0 ? bytes[1] + 256 : bytes[1];
                int code = b0 * 256 + b1;

                for (int i = 0; i < FirstLetters.Length; i++)
                {
                    if (code >= AreaBounds[i] && code < AreaBounds[i + 1])
                        return FirstLetters[i];
                }
            }
            catch
            {
                // 编码失败时跳过
            }

            return '#';
        }
    }
}
