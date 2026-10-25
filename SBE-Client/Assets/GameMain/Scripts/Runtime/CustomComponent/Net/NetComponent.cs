using System;
using Cysharp.Threading.Tasks;
using SepCore.Definition;
using UnityGameFramework.Runtime;
using UnityEngine;

namespace SepCore.Net
{
    public enum NetConnectionState
    {
        Offline,
        Connecting,
        LoggingIn,
        Online,
        LoginFailed,
    }

    /// <summary>
    /// 网络组件：持有连接、通用 RPC 层与仓库同步服务，主线程泵送消息，断线自动重连重登。
    /// 大厅经济按钮的可用性以 <see cref="OnlineReady"/> 为准。
    /// </summary>
    public class NetComponent : GameFrameworkComponent
    {
        private const float ReconnectIntervalSeconds = 3f;

        [SerializeField] private string _serverUri = "ws://127.0.0.1:7777/ws";

        private WebSocketConnection _connection;
        private NetRpc _rpc;
        private float _nextAttemptTime;
        private bool _loginInFlight;
        private bool _backOnlinePending;

        /// <summary>
        /// 仓库同步服务（账本、登录、结算、快照落地）。后续业务域按同样形状挂为独立服务。
        /// </summary>
        public WarehouseSyncService Warehouse { get; private set; }

        /// <summary>
        /// 探索局域服务（start_run / encounter / loot 的请求、超时与拒绝归一）。
        /// </summary>
        public RunService Run { get; private set; }

        /// <summary>
        /// 是否已连接。
        /// </summary>
        public bool IsConnected => _connection != null && _connection.IsConnected;

        /// <summary>
        /// 是否已完成登录与首次同步（大厅经济操作的前提）。
        /// </summary>
        public bool OnlineReady { get; private set; }

        /// <summary>
        /// 重新上线事件：仅在 <see cref="OnlineReady"/> 从 false 变为 true 的迁移时触发
        /// （登录成功路径），断线期间不触发。断线重连续局（ticket 07）以此作为恢复入口；
        /// 事件不携带数据，订阅方经 <see cref="TryConsumeBackOnline"/> 取走一次性标记。
        /// </summary>
        public event Action BackOnline;

        public long UserId => OnlineReady ? Warehouse.UserId : 0;
        public NetworkLatencyMonitor Latency => _rpc.Latency;

        public NetConnectionState ConnectionState => OnlineReady
            ? NetConnectionState.Online
            : _loginInFlight
                ? (IsConnected ? NetConnectionState.LoggingIn : NetConnectionState.Connecting)
                : (IsConnected ? NetConnectionState.LoginFailed : NetConnectionState.Offline);

        /// <summary>
        /// 挂起自动重连（登录过程自己驱动登录时置 true，避免并发抢登）。
        /// </summary>
        public bool SuspendAutoLogin { get; set; }

        /// <summary>
        /// 取走"重新上线"一次性标记：自上次取走后发生过 false→true 迁移则返回 true 并清除，
        /// 否则返回 false。供无法长期持订阅的轮询方（如单局流程）使用；事件订阅方直接用 <see cref="BackOnline"/>。
        /// </summary>
        public bool TryConsumeBackOnline()
        {
            if (!_backOnlinePending)
            {
                return false;
            }

            _backOnlinePending = false;
            return true;
        }

        private void Awake()
        {
            _connection = new WebSocketConnection();
            _rpc = new NetRpc(_connection);
            Warehouse = new WarehouseSyncService(
                _rpc,
                () => GameEntry.Save?.Data,
                id => GameEntry.Luban != null && GameEntry.Luban.IsReady ? GameEntry.Luban.Get<ItemConfig>(id) : null,
                () => GameEntry.Luban != null && GameEntry.Luban.IsReady ? GameEntry.Luban.Global?.Data : null,
                PersistLocalCache);
            Run = new RunService(_rpc);

            _connection.Disconnected += OnDisconnected;
        }

        private void Update()
        {
            _connection.Pump();
            if (OnlineReady && (!IsConnected || Warehouse.UserId <= 0)) OnDisconnected();
            _rpc.UpdateLatency(OnlineReady);

            if (OnlineReady || SuspendAutoLogin || _loginInFlight || !CanAttemptLogin() || Time.unscaledTime < _nextAttemptTime)
            {
                return;
            }

            TryLoginAsync().Forget();
        }

        private bool CanAttemptLogin()
        {
            return GameEntry.Save != null && GameEntry.Save.IsReady && GameEntry.Luban != null && GameEntry.Luban.IsReady;
        }

        /// <summary>
        /// 用缓存 token 尝试一次静默续登（连接+认证+首登迁移+拉取）。
        /// token 被拒时自动清除缓存并返回 AuthRejected，由调用方转入账密登录。
        /// </summary>
        public async UniTask<NetLoginOutcome> TryLoginAsync()
        {
            if (_loginInFlight) return NetLoginOutcome.Failed;
            _loginInFlight = true;
            OnlineReady = false;
            try
            {
                LoginResult result = await Warehouse.TryTokenLoginAsync(_serverUri);
                if (result.Success)
                {
                    AccountSession.SaveToken(Warehouse.Token);
                    OnlineReady = true;
                    OnBackOnline();
                    Log.Info("Net online ready. UserId: {0}.", Warehouse.UserId);
                }
                else if (result.Outcome == NetLoginOutcome.AuthRejected)
                {
                    AccountSession.ClearToken();
                    Log.Warning("Token login rejected; interactive login required.");
                }
                else
                {
                    _nextAttemptTime = Time.unscaledTime + ReconnectIntervalSeconds;
                }

                return result.Outcome;
            }
            finally
            {
                _loginInFlight = false;
            }
        }

        /// <summary>
        /// 用账号密码注册（register=true）或登录。成功后持久化会话（uid/token/username）。
        /// 失败结果带服务器错误码，供登录界面映射提示文案。
        /// </summary>
        public async UniTask<LoginResult> TryCredentialLoginAsync(string username, string password, bool register)
        {
            if (_loginInFlight) return new LoginResult(NetLoginOutcome.Failed);
            _loginInFlight = true;
            OnlineReady = false;
            try
            {
                LoginResult result = await Warehouse.TryCredentialLoginAsync(_serverUri, username, password, register);
                if (result.Success)
                {
                    AccountSession.Save(Warehouse.UserId, Warehouse.Token, username);
                    OnlineReady = true;
                    OnBackOnline();
                    Log.Info("Net online ready. UserId: {0}.", Warehouse.UserId);
                }
                else
                {
                    _nextAttemptTime = Time.unscaledTime + ReconnectIntervalSeconds;
                }

                return result;
            }
            finally
            {
                _loginInFlight = false;
            }
        }

        /// <summary>
        /// 大厅经济数据发生变化（穿卸/布局/整理）后的上报入口。
        /// </summary>
        public void NotifyEconomyChanged()
        {
            if (!OnlineReady)
            {
                return;
            }

            Warehouse.FlushAsync().Forget();
        }

        /// <summary>
        /// 主动断开连接（退出登录用）：连接关闭事件会复位在线与登录状态。
        /// </summary>
        public void Disconnect()
        {
            _connection.Close();
        }

        private void OnDisconnected()
        {
            OnlineReady = false;
            _nextAttemptTime = Time.unscaledTime + ReconnectIntervalSeconds;
            Log.Warning("Net disconnected; will retry.");
        }

        /// <summary>
        /// 登录成功后的重新上线迁移：两条登录路径成功前都先把 OnlineReady 置 false，
        /// 因此这里的 OnlineReady=true 必然是 false→true 迁移（首登或断线重连），置一次性标记并通知订阅方。
        /// </summary>
        private void OnBackOnline()
        {
            _backOnlinePending = true;
            BackOnline?.Invoke();
        }

        private void OnDestroy()
        {
            if (_connection != null)
            {
                _connection.Dispose();
                _connection = null;
            }
        }

        private static void PersistLocalCache()
        {
            if (GameEntry.Save != null && GameEntry.Save.IsReady)
            {
                GameEntry.Save.Save();
            }
        }
    }
}
