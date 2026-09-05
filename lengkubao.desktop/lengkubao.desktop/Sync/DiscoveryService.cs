using Makaretu.Dns;

using System;

using System.Collections.Generic;

using System.Linq;

using System.Net;

using System.Net.NetworkInformation;

using System.Net.Sockets;

using System.Text;

using System.Threading;



namespace lengkubao.desktop.Sync

{

    /// <summary>LAN 服务发现：mDNS _lengkubao._tcp + UDP 广播兜底。</summary>

    public sealed class DiscoveryService : IDisposable

    {

        public const string ServiceType = "_lengkubao._tcp.local.";

        private const int BroadcastPort = 8888;



        private readonly Action<string> _log;

        private readonly object _lock = new object();

        private UdpClient _udpListener;

        private Thread _udpThread;

        private MulticastService _mdns;

        private ServiceDiscovery _serviceDiscovery;

        private ServiceProfile _serviceProfile;

        private bool _running;

        private string _pairingCode;

        private int _tcpPort;



        public DiscoveryService(Action<string> log = null)

        {

            _log = log ?? (_ => { });

        }



        public void Start(string pairingCode, int tcpPort)

        {

            lock (_lock)

            {

                _pairingCode = pairingCode ?? "";

                _tcpPort = tcpPort;

                if (_running) return;

                _running = true;

            }



            StartMdnsAdvertise();

            StartUdpListener();

            BroadcastServerReady();

            _log($"📡 发现服务已启动 mDNS + UDP:{BroadcastPort}");

        }



        public void UpdatePairingCode(string pairingCode, int tcpPort)

        {

            lock (_lock)

            {

                _pairingCode = pairingCode ?? "";

                _tcpPort = tcpPort;

            }

            RestartMdnsAdvertise();

        }



        public void Stop()

        {

            lock (_lock)

            {

                if (!_running) return;

                _running = false;

            }



            StopMdnsAdvertise();

            try { _udpListener?.Close(); } catch { }

            _udpListener = null;

            _log("📡 发现服务已停止");

        }



        public void BroadcastPairingAnnounce()

        {

            try

            {

                string message = $"PAIRING_ANNOUNCE|{Environment.MachineName}|{_pairingCode}|{_tcpPort}";

                SendUdpBroadcast(message);

                _serviceDiscovery?.Announce(_serviceProfile);

                _log($"📢 手动广播配对码: {_pairingCode}");

            }

            catch (Exception ex)

            {

                _log($"❌ 广播配对码失败: {ex.Message}");

            }

        }



        public void BroadcastServerReady()

        {

            try

            {

                string message = $"LENGKUBAO_SERVER_ANNOUNCE|{Environment.MachineName}|{_pairingCode}|{_tcpPort}";

                SendUdpBroadcast(message);

                if (_serviceProfile != null)

                    _serviceDiscovery?.Announce(_serviceProfile);

            }

            catch (Exception ex)

            {

                _log($"⚠️ 服务器广播失败: {ex.Message}");

            }

        }



        private void StartMdnsAdvertise()

        {

            try

            {

                StopMdnsAdvertise();

                _mdns = new MulticastService();

                _mdns.Start();

                _serviceDiscovery = new ServiceDiscovery(_mdns);

                _serviceProfile = BuildServiceProfile();

                _serviceDiscovery.Advertise(_serviceProfile);

                _log($"🍎 mDNS 已发布 {ServiceType} 配对码={_pairingCode} 端口={_tcpPort}");

            }

            catch (Exception ex)

            {

                _log($"⚠️ mDNS 发布失败（UDP 兜底仍可用）: {ex.Message}");

                var inner = ex.InnerException;

                while (inner != null)

                {

                    _log($"   ↳ InnerException: {inner.Message}");

                    inner = inner.InnerException;

                }

                _log($"   ↳ 详情: {ex}");

            }

        }



        private void RestartMdnsAdvertise()

        {

            if (!_running) return;

            try

            {

                if (_serviceProfile != null)

                    _serviceDiscovery?.Unadvertise(_serviceProfile);

                _serviceProfile = BuildServiceProfile();

                _serviceDiscovery?.Advertise(_serviceProfile);

                _log($"🍎 mDNS 配对信息已更新: {_pairingCode}");

            }

            catch (Exception ex)

            {

                _log($"⚠️ mDNS 更新失败: {ex.Message}");

                var inner = ex.InnerException;

                while (inner != null)

                {

                    _log($"   ↳ InnerException: {inner.Message}");

                    inner = inner.InnerException;

                }

            }

        }



        private ServiceProfile BuildServiceProfile()

        {

            var ips = GetLocalIPv4()

                .Select(IPAddress.Parse)

                .Where(a => !IPAddress.IsLoopback(a))

                .Distinct()

                .ToList();

            if (ips.Count == 0)

                ips.Add(IPAddress.Parse("127.0.0.1"));



            var profile = new ServiceProfile("_lengkubao._tcp", Environment.MachineName, (ushort)_tcpPort, ips);

            profile.AddProperty("pairingCode", _pairingCode ?? "");

            profile.AddProperty("deviceName", Environment.MachineName);

            return profile;

        }



        private void StopMdnsAdvertise()

        {

            try

            {

                if (_serviceProfile != null && _serviceDiscovery != null)

                    _serviceDiscovery.Unadvertise(_serviceProfile);

            }

            catch { }

            try { _serviceDiscovery?.Dispose(); } catch { }

            try { _mdns?.Stop(); } catch { }

            try { _mdns?.Dispose(); } catch { }

            _serviceProfile = null;

            _serviceDiscovery = null;

            _mdns = null;

        }



        private void StartUdpListener()

        {

            try

            {

                _udpListener = new UdpClient(BroadcastPort);

                _udpThread = new Thread(UdpLoop) { IsBackground = true };

                _udpThread.Start();

                _log($"📢 UDP 发现监听端口 {BroadcastPort}");

            }

            catch (Exception ex)

            {

                _log($"⚠️ UDP 发现启动失败: {ex.Message}");

            }

        }



        private void UdpLoop()

        {

            while (_running)

            {

                try

                {

                    if (_udpListener == null) break;

                    IPEndPoint remote = new IPEndPoint(IPAddress.Any, 0);

                    byte[] data = _udpListener.Receive(ref remote);

                    string message = Encoding.UTF8.GetString(data);

                    HandleUdpMessage(message, remote);

                }

                catch (SocketException)

                {

                    if (!_running) break;

                }

                catch (ObjectDisposedException)

                {

                    break;

                }

                catch (Exception ex)

                {

                    if (_running) _log($"⚠️ UDP 处理错误: {ex.Message}");

                }

            }

        }



        private void HandleUdpMessage(string message, IPEndPoint remote)

        {

            if (string.IsNullOrEmpty(message)) return;



            if (message.StartsWith("DISCOVER_LENGKUBAO"))

            {

                string response = $"LENGKUBAO_SERVER|{Environment.MachineName}|{_pairingCode}|{_tcpPort}";

                SendUdp(response, remote);

                return;

            }



            if (message.StartsWith("PAIRING_REQUEST|"))

            {

                var parts = message.Split('|');

                if (parts.Length >= 3)

                {

                    string deviceCode = parts[2];

                    string response = deviceCode == _pairingCode

                        ? $"PAIRING_ACCEPTED|{Environment.MachineName}|欢迎连接"

                        : "PAIRING_REJECTED|配对码错误";

                    SendUdp(response, remote);

                }

                return;

            }



            if (message.StartsWith("MDNS_QUERY"))

            {

                var parts = message.Split('|');

                string queryCode = parts.Length > 1 ? parts[1] : "";

                if (queryCode == _pairingCode || string.IsNullOrEmpty(queryCode))

                {

                    string ips = string.Join(",", GetLocalIPv4());

                    string response = $"MDNS_RESPONSE|{Environment.MachineName}|{ips}|{_tcpPort}|{_pairingCode}";

                    SendUdp(response, remote);

                }

            }

        }



        private void SendUdp(string message, IPEndPoint remote)

        {

            try

            {

                byte[] bytes = Encoding.UTF8.GetBytes(message);

                _udpListener?.Send(bytes, bytes.Length, remote);

            }

            catch { }

        }



        private void SendUdpBroadcast(string message)

        {

            byte[] bytes = Encoding.UTF8.GetBytes(message);

            foreach (var addr in GetBroadcastAddresses())

            {

                try

                {

                    using (var client = new UdpClient())

                    {

                        client.EnableBroadcast = true;

                        client.Send(bytes, bytes.Length, new IPEndPoint(addr, BroadcastPort));

                    }

                }

                catch { }

            }

        }



        private static IEnumerable<IPAddress> GetBroadcastAddresses()

        {

            var list = new List<IPAddress> { IPAddress.Broadcast };

            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())

            {

                if (nic.OperationalStatus != OperationalStatus.Up) continue;

                foreach (var ua in nic.GetIPProperties().UnicastAddresses)

                {

                    if (ua.Address.AddressFamily != AddressFamily.InterNetwork || ua.IPv4Mask == null)

                        continue;

                    var ipBytes = ua.Address.GetAddressBytes();

                    var maskBytes = ua.IPv4Mask.GetAddressBytes();

                    var bcBytes = new byte[4];

                    for (int i = 0; i < 4; i++)

                        bcBytes[i] = (byte)(ipBytes[i] | (~maskBytes[i] & 0xFF));

                    list.Add(new IPAddress(bcBytes));

                }

            }

            return list.Distinct();

        }



        private static IEnumerable<string> GetLocalIPv4()

        {

            return NetworkInterface.GetAllNetworkInterfaces()

                .Where(n => n.OperationalStatus == OperationalStatus.Up)

                .SelectMany(n => n.GetIPProperties().UnicastAddresses)

                .Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork)

                .Select(a => a.Address.ToString());

        }



        public void Dispose()

        {

            Stop();

        }

    }

}


