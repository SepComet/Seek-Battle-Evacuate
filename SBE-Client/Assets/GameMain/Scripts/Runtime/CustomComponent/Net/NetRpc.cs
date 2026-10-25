using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityGameFramework.Runtime;

namespace SepCore.Net
{
    /// <summary>
    /// 通用 RPC 层：rid 分配与请求关联、信封收发、断线时放行等待项、ping 延迟探测。
    /// 只依赖连接抽象，不含任何业务语义；各域服务共享同一 rid 空间与消息分发，
    /// 订阅 <see cref="Disconnected"/> / <see cref="Unauthorized"/> 感知会话级变化。
    /// 事件与回调全部在主线程触发（由宿主组件在 Update 中泵送连接）。
    /// </summary>
    public sealed class NetRpc
    {
        private readonly IServerConnection _connection;
        private readonly Func<double> _clock;
        private readonly Dictionary<long, UniTaskCompletionSource<NetEnvelope>> _pending =
            new Dictionary<long, UniTaskCompletionSource<NetEnvelope>>();
        private long _nextRid;

        public NetRpc(IServerConnection connection, Func<double> clock = null)
        {
            _connection = connection;
            _clock = clock ?? (() => (double)System.Diagnostics.Stopwatch.GetTimestamp() / System.Diagnostics.Stopwatch.Frequency);
            _connection.MessageReceived += OnMessageReceived;
            _connection.Disconnected += OnDisconnected;
        }

        /// <summary>
        /// 连接是否存活。
        /// </summary>
        public bool IsConnected => _connection.IsConnected;

        /// <summary>
        /// 连接服务器。返回是否成功。
        /// </summary>
        public UniTask<bool> ConnectAsync(string uri)
        {
            return _connection.ConnectAsync(uri);
        }

        public NetworkLatencyMonitor Latency { get; } = new NetworkLatencyMonitor();

        /// <summary>
        /// 连接断开（主线程）。本层已先完成等待项放行与延迟重置，域服务据此清理登录态。
        /// </summary>
        public event Action Disconnected;

        /// <summary>
        /// 延迟探测收到 unauthorized 应答（令牌已失效）。仅探测路径触发；
        /// 域服务应清除当前登录态，由宿主驱动重登。
        /// </summary>
        public event Action Unauthorized;

        /// <summary>
        /// 主线程定时探测，不进入任何业务互斥，也不向 pending 请求表添加等待项。
        /// active 表示业务侧已处于可探测状态（如完成登录），否则只重置监控。
        /// </summary>
        public void UpdateLatency(bool active)
        {
            double now = _clock();
            if (!active || !_connection.IsConnected)
            {
                Latency.Reset(now);
                return;
            }

            if (!Latency.ShouldProbe(now)) return;
            NetEnvelope ping = NetEnvelope.Create(NetMsg.Ping);
            ping.rid = ++_nextRid;
            Latency.BeginProbe(ping.rid, now);
            if (!_connection.TrySend(GameFramework.Utility.Json.ToJson(ping)))
            {
                Latency.TryComplete(ping.rid, false, _clock());
            }
        }

        /// <summary>
        /// 重置延迟监控（登录开始、断线等会话边界由域服务/宿主调用）。
        /// </summary>
        public void ResetLatency()
        {
            Latency.Reset(_clock());
        }

        /// <summary>
        /// 发送一条信封并等待同 rid 的响应。发送失败或等待期间断线返回 null，
        /// 调用方按"连接丢失"语义处理（重试或放弃）。
        /// </summary>
        public async UniTask<NetEnvelope> SendAsync(NetEnvelope env, Action<NetEnvelope> mutate = null)
        {
            env.rid = ++_nextRid;
            mutate?.Invoke(env);
            UniTaskCompletionSource<NetEnvelope> tcs = new UniTaskCompletionSource<NetEnvelope>();
            _pending[env.rid] = tcs;
            string json = GameFramework.Utility.Json.ToJson(env);
            if (!_connection.TrySend(json))
            {
                _pending.Remove(env.rid);
                return null;
            }

            return await tcs.Task;
        }

        private void OnMessageReceived(string message)
        {
            NetEnvelope resp = GameFramework.Utility.Json.ToObject<NetEnvelope>(message);
            if (resp == null)
            {
                Log.Error("Malformed server message.");
                return;
            }

            if (Latency.TryComplete(resp.rid, resp.IsOk, _clock()))
            {
                if (!resp.IsOk && resp.code == NetErrCode.Unauthorized) Unauthorized?.Invoke();
                return;
            }

            if (resp.rid != 0 && _pending.TryGetValue(resp.rid, out UniTaskCompletionSource<NetEnvelope> tcs))
            {
                _pending.Remove(resp.rid);
                tcs.TrySetResult(resp);
            }
        }

        private void OnDisconnected()
        {
            Latency.Reset(_clock());
            // 完成等待可能同步恢复协程并发出重试；先移出旧表，避免枚举期间被修改。
            List<UniTaskCompletionSource<NetEnvelope>> pending = new List<UniTaskCompletionSource<NetEnvelope>>(_pending.Values);
            _pending.Clear();
            foreach (UniTaskCompletionSource<NetEnvelope> request in pending)
            {
                request.TrySetResult(null);
            }

            Disconnected?.Invoke();
        }
    }
}
