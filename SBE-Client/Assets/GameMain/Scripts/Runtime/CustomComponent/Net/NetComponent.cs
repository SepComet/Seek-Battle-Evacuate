using Cysharp.Threading.Tasks;
using SepCore.Definition;
using UnityGameFramework.Runtime;
using UnityEngine;

namespace SepCore.Net
{
    /// <summary>
    /// 网络组件：持有连接与同步服务，主线程泵送消息，断线自动重连重登。
    /// 大厅经济按钮的可用性以 <see cref="OnlineReady"/> 为准。
    /// </summary>
    public class NetComponent : GameFrameworkComponent
    {
        private const float ReconnectIntervalSeconds = 3f;

        [SerializeField] private string _serverUri = "ws://127.0.0.1:7777/ws";

        private WebSocketConnection _connection;
        private float _nextAttemptTime;
        private bool _loginInFlight;

        /// <summary>
        /// 同步服务（账本、登录、结算、快照落地）。
        /// </summary>
        public WarehouseSyncService Sync { get; private set; }

        /// <summary>
        /// 是否已连接。
        /// </summary>
        public bool IsConnected => _connection != null && _connection.IsConnected;

        /// <summary>
        /// 是否已完成登录与首次同步（大厅经济操作的前提）。
        /// </summary>
        public bool OnlineReady { get; private set; }

        /// <summary>
        /// 挂起自动重连（登录过程自己驱动登录时置 true，避免并发抢登）。
        /// </summary>
        public bool SuspendAutoLogin { get; set; }

        private void Awake()
        {
            _connection = new WebSocketConnection();
            Sync = new WarehouseSyncService(
                _connection,
                () => GameEntry.Save?.Data,
                id => GameEntry.Luban != null && GameEntry.Luban.IsReady ? GameEntry.Luban.Get<ItemConfig>(id) : null,
                () => GameEntry.Luban != null && GameEntry.Luban.IsReady ? GameEntry.Luban.Global?.Data : null,
                PersistLocalCache);

            _connection.Disconnected += OnDisconnected;
        }

        private void Update()
        {
            _connection.Pump();

            if (OnlineReady || SuspendAutoLogin || _loginInFlight || !CanAttemptLogin() || Time.unscaledTime < _nextAttemptTime)
            {
                return;
            }

            UniTask.Void(async () =>
            {
                _loginInFlight = true;
                try
                {
                    await TryLoginAsync();
                }
                finally
                {
                    _loginInFlight = false;
                }
            });
        }

        private bool CanAttemptLogin()
        {
            return GameEntry.Save != null && GameEntry.Save.IsReady && GameEntry.Luban != null && GameEntry.Luban.IsReady;
        }

        /// <summary>
        /// 尝试一次完整登录（连接+认证/注册+首登迁移+拉取）。
        /// </summary>
        public async UniTask<bool> TryLoginAsync()
        {
            DeviceIdentity.Load(out string device, out string token);
            bool ok = await Sync.LoginAsync(_serverUri, device, token);
            if (ok)
            {
                DeviceIdentity.SaveToken(Sync.Token);
                OnlineReady = true;
                Log.Info("Net online ready.");
            }
            else
            {
                OnlineReady = false;
                _nextAttemptTime = Time.unscaledTime + ReconnectIntervalSeconds;
            }

            return ok;
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

            UniTask.Void(Sync.FlushAsync);
        }

        private void OnDisconnected()
        {
            OnlineReady = false;
            _nextAttemptTime = Time.unscaledTime + ReconnectIntervalSeconds;
            Log.Warning("Net disconnected; will retry.");
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
