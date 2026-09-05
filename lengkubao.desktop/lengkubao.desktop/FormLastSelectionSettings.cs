using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace lengkubao.desktop
{
    /// <summary>
    /// 开单页上次选择的字段（关闭前记住，下次打开还原）。
    /// </summary>
    public static class FormLastSelectionSettings
    {
        private static readonly string ConfigFile = Path.Combine(Application.StartupPath, "form_last_selection.ini");
        private static readonly object SyncRoot = new object();

        public static string Get(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
                return null;

            try
            {
                Dictionary<string, string> map = ReadAll();
                if (map.TryGetValue(key, out string value) && !string.IsNullOrWhiteSpace(value))
                    return value.Trim();
            }
            catch (Exception ex)
            {
                Console.WriteLine($">>> [FormLastSelectionSettings] 读取失败: {ex.Message}");
            }

            return null;
        }

        public static void Set(string key, string value)
        {
            if (string.IsNullOrWhiteSpace(key))
                return;

            try
            {
                lock (SyncRoot)
                {
                    Dictionary<string, string> map = ReadAll();
                    if (string.IsNullOrWhiteSpace(value))
                        map.Remove(key);
                    else
                        map[key] = value.Trim();
                    WriteAll(map);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($">>> [FormLastSelectionSettings] 保存失败: {ex.Message}");
            }
        }

        public static void SetMany(IDictionary<string, string> values)
        {
            if (values == null || values.Count == 0)
                return;

            try
            {
                lock (SyncRoot)
                {
                    Dictionary<string, string> map = ReadAll();
                    foreach (var pair in values)
                    {
                        if (string.IsNullOrWhiteSpace(pair.Key))
                            continue;
                        if (string.IsNullOrWhiteSpace(pair.Value))
                            map.Remove(pair.Key);
                        else
                            map[pair.Key] = pair.Value.Trim();
                    }
                    WriteAll(map);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($">>> [FormLastSelectionSettings] 批量保存失败: {ex.Message}");
            }
        }

        private static Dictionary<string, string> ReadAll()
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (!File.Exists(ConfigFile))
                return map;

            foreach (string line in File.ReadAllLines(ConfigFile, Encoding.UTF8))
            {
                if (string.IsNullOrWhiteSpace(line) || !line.Contains("="))
                    continue;

                int eq = line.IndexOf('=');
                string key = line.Substring(0, eq).Trim();
                string value = line.Substring(eq + 1).Trim();
                if (string.IsNullOrEmpty(key))
                    continue;
                map[key] = value;
            }

            return map;
        }

        private static void WriteAll(Dictionary<string, string> map)
        {
            var sb = new StringBuilder();
            foreach (var pair in map)
                sb.Append(pair.Key).Append('=').Append(pair.Value).AppendLine();
            File.WriteAllText(ConfigFile, sb.ToString(), Encoding.UTF8);
        }

        public static class Keys
        {
            public const string PackagingPackFlag = "Packaging.PackFlag";
            public const string PackagingHandler = "Packaging.Handler";
            public const string InboundLocation = "Inbound.Location";
            public const string InboundHandler = "Inbound.Handler";
        }
    }
}
