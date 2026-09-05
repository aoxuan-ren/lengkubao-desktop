using Newtonsoft.Json;

using QRCoder;

using System;

using System.Collections.Generic;

using System.Drawing;

using System.Linq;

using System.Net;

using System.Net.NetworkInformation;

using System.Net.Sockets;



namespace lengkubao.desktop.Sync

{

    /// <summary>生成手持端扫码配对用的 JSON 二维码。</summary>

    public static class PairingQrService

    {

        public const string PairType = "lengkubao_pair";

        public const int SchemaVersion = 1;



        public class PairingPayload

        {

            [JsonProperty("v")]

            public int Version { get; set; } = SchemaVersion;



            [JsonProperty("type")]

            public string Type { get; set; } = PairType;



            [JsonProperty("ip")]

            public string Ip { get; set; }



            [JsonProperty("port")]

            public int Port { get; set; }



            [JsonProperty("code")]

            public string Code { get; set; }



            [JsonProperty("name")]

            public string Name { get; set; }

        }



        public static string GetPrimaryLanIp()

        {

            try

            {

                var candidates = new List<(int priority, string ip)>();



                foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())

                {

                    if (nic.OperationalStatus != OperationalStatus.Up)

                        continue;

                    if (IsVirtualNic(nic))

                        continue;



                    foreach (var ua in nic.GetIPProperties().UnicastAddresses)

                    {

                        if (ua.Address.AddressFamily != AddressFamily.InterNetwork)

                            continue;

                        if (IPAddress.IsLoopback(ua.Address))

                            continue;



                        string ip = ua.Address.ToString();

                        if (ip.StartsWith("169.254."))

                            continue;

                        if (!IsPrivateLanIp(ip))

                            continue;



                        int priority = GetNicPriority(nic, ip);

                        candidates.Add((priority, ip));

                    }

                }



                if (candidates.Count > 0)

                    return candidates.OrderByDescending(c => c.priority).First().ip;



                var host = Dns.GetHostEntry(Dns.GetHostName());

                var fallback = host.AddressList.FirstOrDefault(a =>

                    a.AddressFamily == AddressFamily.InterNetwork &&

                    !IPAddress.IsLoopback(a) &&

                    IsPrivateLanIp(a.ToString()));

                return fallback?.ToString() ?? "127.0.0.1";

            }

            catch

            {

                return "127.0.0.1";

            }

        }



        private static bool IsVirtualNic(NetworkInterface nic)

        {

            if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback ||

                nic.NetworkInterfaceType == NetworkInterfaceType.Tunnel ||

                nic.NetworkInterfaceType == NetworkInterfaceType.Ppp)

                return true;



            string desc = (nic.Description + " " + nic.Name).ToLowerInvariant();

            return desc.Contains("hyper-v") ||

                   desc.Contains("virtual") ||

                   desc.Contains("vmware") ||

                   desc.Contains("wsl") ||

                   desc.Contains("vethernet") ||

                   desc.Contains("docker") ||

                   desc.Contains("tap") ||

                   desc.Contains("tun");

        }



        private static bool IsPrivateLanIp(string ip)

        {

            if (!IPAddress.TryParse(ip, out var addr))

                return false;

            var bytes = addr.GetAddressBytes();

            if (bytes[0] == 10)

                return true;

            if (bytes[0] == 192 && bytes[1] == 168)

                return true;

            if (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31)

                return true;

            return false;

        }



        private static int GetNicPriority(NetworkInterface nic, string ip)

        {

            int score = 0;

            if (nic.NetworkInterfaceType == NetworkInterfaceType.Wireless80211)

                score += 100;

            else if (nic.NetworkInterfaceType == NetworkInterfaceType.Ethernet)

                score += 80;



            if (ip.StartsWith("192.168."))

                score += 20;

            else if (ip.StartsWith("10."))

                score += 10;



            return score;

        }



        public static string BuildPayloadJson(string pairingCode, int tcpPort, string machineName = null)

        {

            var payload = new PairingPayload

            {

                Ip = GetPrimaryLanIp(),

                Port = tcpPort,

                Code = pairingCode ?? "",

                Name = machineName ?? Environment.MachineName

            };

            return JsonConvert.SerializeObject(payload);

        }



        public static Bitmap CreateQrBitmap(string pairingCode, int tcpPort, int pixelsPerModule = 8)

        {

            string json = BuildPayloadJson(pairingCode, tcpPort);

            using (var generator = new QRCodeGenerator())

            using (var data = generator.CreateQrCode(json, QRCodeGenerator.ECCLevel.M))

            using (var qr = new QRCode(data))

            {

                return qr.GetGraphic(pixelsPerModule);

            }

        }



        private static Dictionary<string, string> ParseQueryString(string query)

        {

            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            if (string.IsNullOrEmpty(query)) return dict;

            string q = query.TrimStart('?');

            foreach (var part in q.Split('&'))

            {

                if (string.IsNullOrEmpty(part)) continue;

                int eq = part.IndexOf('=');

                if (eq <= 0) continue;

                dict[Uri.UnescapeDataString(part.Substring(0, eq))] =

                    Uri.UnescapeDataString(part.Substring(eq + 1));

            }

            return dict;

        }



        public static bool TryParsePayload(string raw, out PairingPayload payload)

        {

            payload = null;

            if (string.IsNullOrWhiteSpace(raw))

                return false;



            raw = raw.Trim();



            // 兼容旧版 lengkubao://pair?code=...&name=...&port=...

            if (raw.StartsWith("lengkubao://pair", StringComparison.OrdinalIgnoreCase))

            {

                try

                {

                    var uri = new Uri(raw);

                    var query = ParseQueryString(uri.Query);

                    query.TryGetValue("code", out string code);

                    query.TryGetValue("name", out string name);

                    query.TryGetValue("port", out string portStr);

                    payload = new PairingPayload

                    {

                        Version = SchemaVersion,

                        Type = PairType,

                        Code = code ?? "",

                        Name = name ?? "",

                        Port = int.TryParse(portStr, out int p) ? p : 8080,

                        Ip = GetPrimaryLanIp()

                    };

                    return !string.IsNullOrEmpty(payload.Code);

                }

                catch

                {

                    return false;

                }

            }



            try

            {

                payload = JsonConvert.DeserializeObject<PairingPayload>(raw);

                return payload != null &&

                       string.Equals(payload.Type, PairType, StringComparison.OrdinalIgnoreCase) &&

                       !string.IsNullOrWhiteSpace(payload.Code) &&

                       !string.IsNullOrWhiteSpace(payload.Ip) &&

                       payload.Port > 0;

            }

            catch

            {

                return false;

            }

        }

    }

}


