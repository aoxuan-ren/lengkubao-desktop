using System;
using System.IO;
using System.Text;
using lengkubao.desktop;

namespace Lengkubao.LicenseVerify
{
    internal static class Program
    {
        private const string DefaultPrivateKeyPath = @"D:\afilessss\lengkubao.desktop\tools\private_key.xml";

        private static int Main(string[] args)
        {
            if (args.Length == 0)
            {
                PrintUsage();
                return 2;
            }

            string command = args[0].ToLowerInvariant();

            if (command == "machineid")
            {
                Console.OutputEncoding = Encoding.UTF8;
                Console.Write(LicenseManager.GetMachineId());
                return 0;
            }

            if (command == "verify" && args.Length >= 3)
            {
                bool ok = LicenseManager.TryVerifyAndParse(args[1], args[2], out _);
                return ok ? 0 : 1;
            }

            if (command == "verifyinfo" && args.Length >= 3)
            {
                if (!LicenseManager.TryVerifyAndParse(args[1], args[2], out LicenseManager.LicenseInfo info))
                    return 1;

                string output = LicenseManager.FormatVerifyInfo(info);
                Console.OutputEncoding = Encoding.UTF8;
                Console.Write(output);

                if (args.Length >= 4)
                    File.WriteAllText(args[3], output, Encoding.UTF8);

                return 0;
            }

            if (command == "sign" && args.Length >= 2)
            {
                string keyPath = FindPrivateKeyPath(args);
                if (keyPath == null || !File.Exists(keyPath))
                {
                    Console.Error.WriteLine("private_key.xml not found");
                    return 2;
                }

                string machineId = args[1].Trim().ToUpperInvariant();
                string privateKeyXml = File.ReadAllText(keyPath);
                string payload = ResolveSignPayload(args, machineId);
                string signature = LicenseManager.SignForAdmin(payload, privateKeyXml);

                Console.OutputEncoding = Encoding.UTF8;
                Console.Write(FormatSignature(signature));
                return 0;
            }

            PrintUsage();
            return 2;
        }

        private static string ResolveSignPayload(string[] args, string machineId)
        {
            if (args.Length >= 3)
            {
                string mode = args[2].Trim().ToLowerInvariant();
                if (mode == "1y" || mode == "1year" || mode == "year1")
                    return LicenseManager.BuildSignedPayload(machineId, LicenseManager.LicenseKind.Trial, LicenseManager.TrialPlan1Year);
                if (mode == "3y" || mode == "3year" || mode == "year3")
                    return LicenseManager.BuildSignedPayload(machineId, LicenseManager.LicenseKind.Trial, LicenseManager.TrialPlan3Year);
            }

            return LicenseManager.BuildSignedPayload(machineId, LicenseManager.LicenseKind.Permanent);
        }

        private static string FindPrivateKeyPath(string[] args)
        {
            if (args.Length >= 4 && File.Exists(args[3]))
                return args[3];

            if (args.Length >= 3 && File.Exists(args[2]) && args[2].EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                return args[2];

            if (File.Exists(DefaultPrivateKeyPath))
                return DefaultPrivateKeyPath;

            return null;
        }

        private static string FormatSignature(string base64)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < base64.Length; i++)
            {
                if (i > 0 && i % 4 == 0)
                    sb.Append('-');
                sb.Append(base64[i]);
            }
            return sb.ToString();
        }

        private static void PrintUsage()
        {
            Console.WriteLine("Lengkubao.LicenseVerify");
            Console.WriteLine("  LicenseVerify.exe machineid");
            Console.WriteLine("  LicenseVerify.exe verify <machineId> <signature>");
            Console.WriteLine("  LicenseVerify.exe verifyinfo <machineId> <signature> [output.txt]");
            Console.WriteLine("  LicenseVerify.exe sign <machineId> [1y|3y] [private_key.xml path]");
            Console.WriteLine("  Default private key: " + DefaultPrivateKeyPath);
        }
    }
}
