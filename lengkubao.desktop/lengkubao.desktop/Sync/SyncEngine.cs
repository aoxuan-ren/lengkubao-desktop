using Newtonsoft.Json.Linq;

using System;

using System.Collections.Concurrent;



namespace lengkubao.desktop.Sync

{

    /// <summary>统一 Outbox 消息路由与 ACK 语义。</summary>

    public sealed class SyncEngine

    {

        private readonly CrsqlEngine _crsql;

        private readonly Action<string> _log;

        private readonly ConcurrentDictionary<string, long> _peerDbVersion = new ConcurrentDictionary<string, long>();



        /// <summary>legacy 行协议处理器（由 AutoSyncServer 注入现有业务逻辑）</summary>

        public Func<string, string, string> LegacyLineHandler { get; set; }



        /// <summary>统一 PUSH 处理器：deviceId, opId, entityType, payloadJson → (success, error)</summary>

        public Func<string, string, string, string, (bool ok, string error)> PushHandler { get; set; }



        /// <summary>CONFIG_PULL：clientId, sinceDbVersion → JSON changes array</summary>

        public Func<string, long, string> ConfigPullHandler { get; set; }



        /// <summary>CONFIG_PUSH：clientId, changesJson → success</summary>

        public Func<string, string, bool> ConfigPushHandler { get; set; }



        public SyncEngine(CrsqlEngine crsql, Action<string> log = null)

        {

            _crsql = crsql;

            _log = log ?? (_ => { });

        }



        public string HandleLine(string clientId, string remoteIp, string line)

        {

            if (string.IsNullOrWhiteSpace(line)) return null;



            try

            {

                if (line.StartsWith("PUSH|", StringComparison.Ordinal))

                    return HandleUnifiedPush(clientId, line.Substring("PUSH|".Length));



                if (line.StartsWith("CONFIG_PULL|", StringComparison.Ordinal))

                    return HandleConfigPull(clientId, line.Substring("CONFIG_PULL|".Length));



                if (line.StartsWith("CONFIG_PUSH|", StringComparison.Ordinal))

                    return HandleConfigPush(clientId, line.Substring("CONFIG_PUSH|".Length));



                if (LegacyLineHandler != null)

                    return LegacyLineHandler(clientId, line);

            }

            catch (Exception ex)

            {

                _log($"⚠️ SyncEngine 处理失败: {ex.Message}");

                return $"SYNC_ERROR|{ex.Message}";

            }



            return null;

        }



        private string HandleUnifiedPush(string clientId, string json)

        {

            var root = JObject.Parse(json);

            string opId = root["op_id"]?.ToString();

            string type = root["type"]?.ToString();

            string payload = root["payload"]?.ToString() ?? root.ToString();



            if (string.IsNullOrEmpty(opId) || string.IsNullOrEmpty(type))

                return "ACK|fail||missing op_id or type";



            if (PushHandler == null)

                return "ACK|fail|" + opId + "|handler not configured";



            var result = PushHandler(clientId, opId, type, payload);

            return result.ok

                ? "ACK|ok|" + opId

                : "ACK|fail|" + opId + "|" + (result.error ?? "unknown");

        }



        private string HandleConfigPull(string clientId, string json)

        {

            var root = JObject.Parse(json);

            long since = root["since_db_version"]?.Value<long>() ?? 0;



            if (ConfigPullHandler != null)

            {

                string changes = ConfigPullHandler(clientId, since);

                long toVersion = _peerDbVersion.GetOrAdd(clientId, since);

                return $"CONFIG_PULL_RESP|{{\"available\":true,\"since\":{since},\"to\":{toVersion},\"changes\":{changes ?? "[]"}}}";

            }



            if (!_crsql.IsAvailable)

                return "CONFIG_PULL_RESP|{\"available\":false,\"changes\":[]}";



            long current = _peerDbVersion.GetOrAdd(clientId, since);

            return $"CONFIG_PULL_RESP|{{\"available\":true,\"since\":{since},\"to\":{current},\"changes\":[]}}";

        }



        private string HandleConfigPush(string clientId, string json)

        {

            var root = JObject.Parse(json);

            string changes = root["changes"]?.ToString();



            if (ConfigPushHandler != null)

            {

                bool ok = ConfigPushHandler(clientId, changes);

                return ok

                    ? "CONFIG_PUSH_ACK|{\"ok\":true}"

                    : "CONFIG_PUSH_ACK|{\"ok\":false}";

            }



            if (!_crsql.IsAvailable || string.IsNullOrEmpty(changes))

                return "CONFIG_PUSH_ACK|{\"ok\":false}";



            return "CONFIG_PUSH_ACK|{\"ok\":true}";

        }



        public void UpdatePeerVersion(string clientId, long dbVersion)

        {

            _peerDbVersion[clientId] = dbVersion;

        }

    }

}


