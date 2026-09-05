using System;
using System.Globalization;
using System.IO;
using System.Windows.Forms;

namespace lengkubao.desktop
{
    /// <summary>
    /// 全局默认查询日期范围，持久化到 date_range.ini。
    /// 默认结束日为当天；开始日可从配置文件记忆。搜索时仍可保存完整区间供兼容。
    /// </summary>
    public static class DateRangeSettings
    {
        private static readonly string ConfigFile = Path.Combine(Application.StartupPath, "date_range.ini");
        private const string DateFormat = "yyyy-MM-dd";

        public static void GetDefault(out DateTime start, out DateTime end)
        {
            start = DateTime.Today.AddDays(-30);
            end = DateTime.Today;

            try
            {
                if (!File.Exists(ConfigFile))
                    return;

                DateTime? savedStart = null;

                foreach (string line in File.ReadAllLines(ConfigFile))
                {
                    if (string.IsNullOrWhiteSpace(line) || !line.Contains("="))
                        continue;

                    int eq = line.IndexOf('=');
                    string key = line.Substring(0, eq).Trim();
                    string value = line.Substring(eq + 1).Trim();

                    if (key.Equals("StartDate", StringComparison.OrdinalIgnoreCase) &&
                        DateTime.TryParseExact(value, DateFormat, CultureInfo.InvariantCulture,
                            DateTimeStyles.None, out DateTime parsedStart))
                    {
                        savedStart = parsedStart.Date;
                    }
                }

                if (savedStart.HasValue)
                    start = savedStart.Value;
            }
            catch (Exception ex)
            {
                Console.WriteLine($">>> [DateRangeSettings] 加载失败: {ex.Message}");
            }

            end = DateTime.Today;
            if (start.Date > end.Date)
                start = end.Date.AddDays(-30);
        }

        public static void GetFactoryDefault(out DateTime start, out DateTime end)
        {
            start = DateTime.Today.AddDays(-30);
            end = DateTime.Today;
        }

        public static void ApplyTo(DateTimePicker startPicker, DateTimePicker endPicker)
        {
            if (startPicker == null || endPicker == null)
                return;

            GetDefault(out DateTime start, out DateTime end);
            startPicker.Value = start;
            endPicker.Value = end;
        }

        public static void ApplyFactoryDefaultTo(DateTimePicker startPicker, DateTimePicker endPicker)
        {
            if (startPicker == null || endPicker == null)
                return;

            GetFactoryDefault(out DateTime start, out DateTime end);
            startPicker.Value = start;
            endPicker.Value = end;
        }

        public static bool SaveDefault(DateTime start, DateTime end)
        {
            if (start.Date > end.Date)
                return false;

            try
            {
                string content =
                    $"StartDate={start.Date.ToString(DateFormat, CultureInfo.InvariantCulture)}{Environment.NewLine}" +
                    $"EndDate={end.Date.ToString(DateFormat, CultureInfo.InvariantCulture)}";
                File.WriteAllText(ConfigFile, content);
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($">>> [DateRangeSettings] 保存失败: {ex.Message}");
                return false;
            }
        }
    }
}
