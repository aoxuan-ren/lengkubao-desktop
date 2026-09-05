using System;

using System.Collections.Concurrent;

using System.Text;

using System.Threading.Tasks;

using TouchSocket.Core;

using TouchSocket.Sockets;



namespace lengkubao.desktop.Sync

{

    /// <summary>TouchSocket TCP 传输层：按行拆包、连接会话管理。</summary>

    public sealed class SyncTransport : IDisposable

    {

        private readonly Action<string> _log;

        private TcpService _service;

        private readonly ConcurrentDictionary<string, StringBuilder> _lineBuffers = new ConcurrentDictionary<string, StringBuilder>();



        public SyncTransport(Action<string> log = null)

        {

            _log = log ?? (_ => { });

        }



        public bool IsRunning { get; private set; }



        /// <summary>clientId, remoteIp, line → optional response line (without trailing newline)</summary>

        public Func<string, string, string, Task<string>> OnLineReceived { get; set; }



        public event Action<string, string> ClientConnected;

        public event Action<string> ClientDisconnected;



        public void Start(int port)

        {

            if (IsRunning) return;



            _service = new TcpService();

            _service.Connected = (client, e) =>

            {

                string ip = client.IP;

                _lineBuffers[client.Id] = new StringBuilder();

                _log($"📱 TouchSocket 连接: {client.Id} from {ip}");

                ClientConnected?.Invoke(client.Id, ip);

                return EasyTask.CompletedTask;

            };



            _service.Closed = (client, e) =>

            {

                _lineBuffers.TryRemove(client.Id, out _);

                _log($"📴 TouchSocket 断开: {client.Id}");

                ClientDisconnected?.Invoke(client.Id);

                return EasyTask.CompletedTask;

            };



            _service.Received = async (client, e) =>

            {

                try

                {

                    string chunk = e.ByteBlock.Span.ToString(Encoding.UTF8);

                    if (string.IsNullOrEmpty(chunk)) return;



                    var buffer = _lineBuffers.GetOrAdd(client.Id, _ => new StringBuilder());

                    buffer.Append(chunk);



                    while (true)

                    {

                        string all = buffer.ToString();

                        int nl = all.IndexOf('\n');

                        if (nl < 0) break;



                        string line = all.Substring(0, nl).Trim('\r');

                        buffer.Clear();

                        buffer.Append(all.Substring(nl + 1));



                        if (string.IsNullOrEmpty(line)) continue;



                        string response = null;

                        if (OnLineReceived != null)

                            response = await OnLineReceived(client.Id, client.IP, line).ConfigureAwait(false);



                        if (!string.IsNullOrEmpty(response))

                            await _service.SendAsync(client.Id, Encoding.UTF8.GetBytes(response + "\n")).ConfigureAwait(false);

                    }

                }

                catch (Exception ex)

                {

                    _log($"⚠️ TouchSocket 接收错误: {ex.Message}");

                }

            };



            _service.Setup(new TouchSocketConfig()

                .SetListenIPHosts(new IPHost(port)));



            _service.Start();

            IsRunning = true;

            _log($"📡 TouchSocket 服务启动，端口 {port}");

        }



        public async Task SendAsync(string clientId, string line)

        {

            if (_service == null || string.IsNullOrEmpty(clientId)) return;

            await _service.SendAsync(clientId, Encoding.UTF8.GetBytes(line + "\n")).ConfigureAwait(false);

        }



        public void Stop()

        {

            if (!IsRunning) return;

            try

            {

                _service?.Stop();

                _service?.SafeDispose();

            }

            catch { }

            _service = null;

            _lineBuffers.Clear();

            IsRunning = false;

            _log("📡 TouchSocket 服务已停止");

        }



        public void Dispose()

        {

            Stop();

        }

    }

}


