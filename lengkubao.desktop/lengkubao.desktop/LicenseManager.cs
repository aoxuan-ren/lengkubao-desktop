using System;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32;

namespace lengkubao.desktop
{
    /// <summary>
    /// RSA 授权：公钥验签，私钥仅管理员持有（D:\afilessss\lengkubao.desktop\tools\private_key.xml）。
    /// 永久授权签名 MachineId；限期授权签名 MachineId|TRIAL|计划码（1Y / 3Y）。
    /// </summary>
    public static class LicenseManager
    {
        public enum LicenseKind
        {
            None,
            Permanent,
            Trial
        }

        public sealed class LicenseInfo
        {
            public LicenseKind Kind { get; set; }
            /// <summary>限期计划：1Y、3Y。</summary>
            public string TrialPlan { get; set; }
            public DateTime? ExpiresAt { get; set; }
            public string SignedPayload { get; set; }
            public string Signature { get; set; }
            public string MachineId { get; set; }
        }

        /// <summary>一年期授权（激活时刻起算）。</summary>
        public const string TrialPlan1Year = "1Y";

        /// <summary>三年期授权（激活时刻起算）。</summary>
        public const string TrialPlan3Year = "3Y";

        private const string PublicKeyXml =
            "<RSAKeyValue><Modulus>2B1Y9k9P2NvkJXHzgydtwoND8MVYqZIHiTRG8TvuZ6cpovnVRPGbVlrPir4/4V6r20teC2WbZqM6gc9GVjIZ01ZkQ3fKeQxyUqnmUOWQ0Rq/LuSMmtpVZTqTTRWc00CCJjJRjPR2M8S1pberrB7ivwG0fH/BV1WriDeJP3BYI3dAwGk9iaUnHL2AILbgre/S0nEm1BbEFX+QvrYcz3a3mMigBpXPT+zfXHK1bXrM5WSYGXbzQPmROLWjfubbHL5FaqLgO2fiyGXJzS3ou4V35G404jgdYzs4/q+p2+U09r9XX0rgn+ZtZ0um65964kcReLNsZBQjxHlLROyNR/glfQ==</Modulus><Exponent>AQAB</Exponent></RSAKeyValue>";

        private const string LicenseFolder = "Lengkubao";
        private const string LicenseFileName = "license.lic";

        public static string GetLicenseFilePath()
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                LicenseFolder,
                LicenseFileName);
        }

        public static string GetMachineId()
        {
            string computerName = Environment.GetEnvironmentVariable("COMPUTERNAME") ?? ("PC" + new Random().Next(10000));
            string userName = Environment.GetEnvironmentVariable("USERNAME") ?? "USER";
            string productId = GetRegistryString(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion", "ProductId") ?? "00000";
            string machineGuid = GetRegistryString(@"SOFTWARE\Microsoft\Cryptography", "MachineGuid") ?? "00000000";

            uint hashValue = SimpleHash(computerName + userName + productId + machineGuid);

            string prefix = computerName.Length >= 4 ? computerName.Substring(0, 4) : computerName;
            return (prefix + "-" + hashValue).ToUpperInvariant();
        }

        public static string NormalizeSignature(string signature)
        {
            if (string.IsNullOrWhiteSpace(signature))
                return string.Empty;

            var sb = new StringBuilder(signature.Length);
            foreach (char c in signature)
            {
                if (char.IsLetterOrDigit(c) || c == '+' || c == '/' || c == '=')
                    sb.Append(c);
            }
            return sb.ToString();
        }

        public static string BuildSignedPayload(string machineId, LicenseKind kind, string trialPlan = null)
        {
            string id = machineId.Trim().ToUpperInvariant();
            if (kind == LicenseKind.Permanent)
                return id;

            if (kind == LicenseKind.Trial && !string.IsNullOrWhiteSpace(trialPlan))
                return id + "|TRIAL|" + trialPlan.Trim().ToUpperInvariant();

            throw new ArgumentException("无效的授权类型或试用计划。");
        }

        public static bool VerifyPayload(string payload, string signatureBase64)
        {
            if (string.IsNullOrWhiteSpace(payload) || string.IsNullOrWhiteSpace(signatureBase64))
                return false;

            string normalized = NormalizeSignature(signatureBase64);
            byte[] signatureBytes;
            try
            {
                signatureBytes = Convert.FromBase64String(normalized);
            }
            catch (FormatException)
            {
                return false;
            }

            byte[] data = Encoding.UTF8.GetBytes(payload.Trim().ToUpperInvariant());

            try
            {
                using (var rsa = new RSACryptoServiceProvider())
                {
                    rsa.FromXmlString(PublicKeyXml);
                    return rsa.VerifyData(data, signatureBytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
                }
            }
            catch (CryptographicException)
            {
                return false;
            }
        }

        /// <summary>兼容旧接口：仅验永久授权。</summary>
        public static bool Verify(string machineId, string signatureBase64)
        {
            return TryVerifyAndParse(machineId, signatureBase64, out _);
        }

        public static bool TryVerifyAndParse(string machineId, string signatureBase64, out LicenseInfo info)
        {
            info = null;
            if (string.IsNullOrWhiteSpace(machineId) || string.IsNullOrWhiteSpace(signatureBase64))
                return false;

            string id = machineId.Trim().ToUpperInvariant();

            string permanentPayload = BuildSignedPayload(id, LicenseKind.Permanent);
            if (VerifyPayload(permanentPayload, signatureBase64))
            {
                info = new LicenseInfo
                {
                    Kind = LicenseKind.Permanent,
                    SignedPayload = permanentPayload
                };
                return true;
            }

            string oneYearPayload = BuildSignedPayload(id, LicenseKind.Trial, TrialPlan1Year);
            if (VerifyPayload(oneYearPayload, signatureBase64))
            {
                info = new LicenseInfo
                {
                    Kind = LicenseKind.Trial,
                    TrialPlan = TrialPlan1Year,
                    ExpiresAt = DateTime.Now.AddYears(1),
                    SignedPayload = oneYearPayload
                };
                return true;
            }

            string threeYearPayload = BuildSignedPayload(id, LicenseKind.Trial, TrialPlan3Year);
            if (VerifyPayload(threeYearPayload, signatureBase64))
            {
                info = new LicenseInfo
                {
                    Kind = LicenseKind.Trial,
                    TrialPlan = TrialPlan3Year,
                    ExpiresAt = DateTime.Now.AddYears(3),
                    SignedPayload = threeYearPayload
                };
                return true;
            }

            return false;
        }

        public static DateTime? ResolveTrialExpiry(string trialPlan, DateTime? storedExpiresAt)
        {
            return storedExpiresAt;
        }

        public static bool IsLicensed()
        {
            if (!TryLoadLicense(out LicenseInfo loaded))
                return false;

            string current = GetMachineId();
            if (!string.Equals(loaded.MachineId, current, StringComparison.OrdinalIgnoreCase))
                return false;

            if (string.IsNullOrWhiteSpace(loaded.SignedPayload))
                loaded.SignedPayload = BuildSignedPayload(current, loaded.Kind, loaded.TrialPlan);

            if (!VerifyPayload(loaded.SignedPayload, loaded.Signature))
                return false;

            if (loaded.Kind == LicenseKind.Trial)
            {
                DateTime? expiresAt = ResolveTrialExpiry(loaded.TrialPlan, loaded.ExpiresAt);
                if (!expiresAt.HasValue || DateTime.Now > expiresAt.Value)
                    return false;
            }

            return true;
        }

        /// <summary>当前是否为有效的永久授权（仅永久版可使用自动更新）。</summary>
        public static bool IsPermanentLicensed()
        {
            if (!IsLicensed())
                return false;

            if (!TryLoadLicense(out LicenseInfo info))
                return false;

            return info.Kind == LicenseKind.Permanent;
        }

        public static bool TryLoadLicense(out LicenseInfo info)
        {
            info = null;
            string path = GetLicenseFilePath();
            if (!File.Exists(path))
                return false;

            string machineId = null;
            string signature = null;
            string licenseType = null;
            string trialPlan = null;
            string expiresAtText = null;
            string signedPayload = null;

            foreach (string line in File.ReadAllLines(path))
            {
                if (line.StartsWith("MachineId=", StringComparison.OrdinalIgnoreCase))
                    machineId = line.Substring("MachineId=".Length).Trim();
                else if (line.StartsWith("Signature=", StringComparison.OrdinalIgnoreCase))
                    signature = line.Substring("Signature=".Length).Trim();
                else if (line.StartsWith("LicenseType=", StringComparison.OrdinalIgnoreCase))
                    licenseType = line.Substring("LicenseType=".Length).Trim();
                else if (line.StartsWith("TrialPlan=", StringComparison.OrdinalIgnoreCase))
                    trialPlan = line.Substring("TrialPlan=".Length).Trim();
                else if (line.StartsWith("ExpiresAt=", StringComparison.OrdinalIgnoreCase))
                    expiresAtText = line.Substring("ExpiresAt=".Length).Trim();
                else if (line.StartsWith("SignedPayload=", StringComparison.OrdinalIgnoreCase))
                    signedPayload = line.Substring("SignedPayload=".Length).Trim();
            }

            if (string.IsNullOrWhiteSpace(machineId) || string.IsNullOrWhiteSpace(signature))
                return false;

            var result = new LicenseInfo
            {
                MachineId = machineId.Trim().ToUpperInvariant(),
                Signature = signature
            };

            if (!string.IsNullOrWhiteSpace(signedPayload))
            {
                result.SignedPayload = signedPayload.Trim().ToUpperInvariant();
                if (result.SignedPayload.IndexOf("|TRIAL|", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    result.Kind = LicenseKind.Trial;
                    int idx = result.SignedPayload.LastIndexOf('|');
                    if (idx >= 0 && idx < result.SignedPayload.Length - 1)
                        result.TrialPlan = result.SignedPayload.Substring(idx + 1);
                }
                else
                {
                    result.Kind = LicenseKind.Permanent;
                }
            }
            else if (string.Equals(licenseType, "Trial", StringComparison.OrdinalIgnoreCase))
            {
                result.Kind = LicenseKind.Trial;
                result.TrialPlan = trialPlan;
                result.SignedPayload = BuildSignedPayload(machineId, LicenseKind.Trial, trialPlan);
            }
            else
            {
                result.Kind = LicenseKind.Permanent;
                result.SignedPayload = BuildSignedPayload(machineId, LicenseKind.Permanent);
            }

            if (!string.IsNullOrWhiteSpace(expiresAtText) &&
                DateTime.TryParse(expiresAtText, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime expiresAt))
            {
                result.ExpiresAt = expiresAt;
            }

            info = result;
            return true;
        }

        public static void SaveLicense(string machineId, string signature, LicenseInfo info)
        {
            if (info == null)
                throw new ArgumentNullException(nameof(info));

            string path = GetLicenseFilePath();
            Directory.CreateDirectory(Path.GetDirectoryName(path));

            string id = machineId.Trim().ToUpperInvariant();
            string payload = info.SignedPayload;
            if (string.IsNullOrWhiteSpace(payload))
                payload = BuildSignedPayload(id, info.Kind, info.TrialPlan);

            var content = new StringBuilder();
            content.AppendLine("MachineId=" + id);
            content.AppendLine("Signature=" + NormalizeSignature(signature));
            content.AppendLine("LicenseType=" + (info.Kind == LicenseKind.Trial ? "Trial" : "Permanent"));
            content.AppendLine("SignedPayload=" + payload);

            if (info.Kind == LicenseKind.Trial)
            {
                content.AppendLine("TrialPlan=" + (info.TrialPlan ?? string.Empty));
                if (info.ExpiresAt.HasValue)
                    content.AppendLine("ExpiresAt=" + info.ExpiresAt.Value.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
            }
            else
            {
                content.AppendLine("TrialPlan=");
                content.AppendLine("ExpiresAt=");
            }

            File.WriteAllText(path, content.ToString(), Encoding.UTF8);
        }

        public static string SignForAdmin(string payload, string privateKeyXml)
        {
            byte[] data = Encoding.UTF8.GetBytes(payload.Trim().ToUpperInvariant());
            using (var rsa = new RSACryptoServiceProvider())
            {
                rsa.FromXmlString(privateKeyXml);
                byte[] sig = rsa.SignData(data, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
                return Convert.ToBase64String(sig);
            }
        }

        public static string FormatVerifyInfo(LicenseInfo info)
        {
            if (info == null)
                return "INVALID";

            if (info.Kind == LicenseKind.Permanent)
                return "PERM";

            if (info.Kind == LicenseKind.Trial)
            {
                string expires = info.ExpiresAt.HasValue
                    ? info.ExpiresAt.Value.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)
                    : string.Empty;
                return "TRIAL|" + (info.TrialPlan ?? string.Empty) + "|" + expires;
            }

            return "INVALID";
        }

        private static string GetRegistryString(string subKey, string name)
        {
            try
            {
                using (var key = Registry.LocalMachine.OpenSubKey(subKey))
                {
                    return key?.GetValue(name) as string;
                }
            }
            catch
            {
                return null;
            }
        }

        private static uint SimpleHash(string input)
        {
            uint result = 0;
            foreach (char c in input)
            {
                result = unchecked(result + c);
                result = unchecked(result * 7);
            }
            return result % 100000000;
        }
    }
}
