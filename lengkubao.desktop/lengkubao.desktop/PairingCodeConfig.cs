using System;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace lengkubao.desktop
{
    /// <summary>
    /// 配对码配置管理
    /// </summary>
    public static class PairingCodeConfig
    {
        private static readonly string ConfigFile = Path.Combine(Application.StartupPath, "pairing_code.ini");

        /// <summary>
        /// 保存配对码
        /// </summary>
        public static void Save(string pairingCode)
        {
            try
            {
                File.WriteAllText(ConfigFile, pairingCode);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"保存配对码失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 加载配对码
        /// </summary>
        public static string Load()
        {
            try
            {
                if (File.Exists(ConfigFile))
                {
                    return File.ReadAllText(ConfigFile).Trim();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"加载配对码失败: {ex.Message}");
            }
            return "ABC-123"; // 默认配对码
        }

        /// <summary>
        /// 生成随机配对码
        /// </summary>
        public static string GenerateRandom()
        {
            Random random = new Random();
            string letters = "ABCDEFGHJKLMNPQRSTUVWXYZ";
            string numbers = "123456789";

            string part1 = new string(Enumerable.Range(0, 3).Select(_ => letters[random.Next(letters.Length)]).ToArray());
            string part2 = new string(Enumerable.Range(0, 3).Select(_ => numbers[random.Next(numbers.Length)]).ToArray());

            return $"{part1}-{part2}";
        }
    }
}