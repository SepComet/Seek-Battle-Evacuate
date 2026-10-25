using System;
using System.Globalization;
using SepCore.Net;
using UnityEngine;

namespace SepCore.UI
{
    /// <summary>跨场景网络状态层；采样由 NetComponent 驱动，UI 只读取状态。</summary>
    public partial class NetworkStatusForm : UGuiForm
    {
        private const float RefreshInterval = 0.25f;
        private float _refreshRemaining;

        protected override void OnOpen(object userData)
        {
            base.OnOpen(userData);
            _refreshRemaining = 0f;
            View.titleText.Set();
            RefreshStatus();
        }

        protected override void OnUpdate(float elapseSeconds, float realElapseSeconds)
        {
            base.OnUpdate(elapseSeconds, realElapseSeconds);
            _refreshRemaining -= realElapseSeconds;
            if (_refreshRemaining > 0f) return;
            _refreshRemaining = RefreshInterval;
            RefreshStatus();
        }

        private void RefreshStatus()
        {
            NetComponent net = GameEntry.Net;
            NetConnectionState state = net.ConnectionState;
            string stateKey;
            switch (state)
            {
                case NetConnectionState.Offline: stateKey = "NetworkStatusOffline"; break;
                case NetConnectionState.Connecting: stateKey = "NetworkStatusConnecting"; break;
                case NetConnectionState.LoggingIn: stateKey = "NetworkStatusAuthenticating"; break;
                case NetConnectionState.Online: stateKey = "NetworkStatusOnline"; break;
                case NetConnectionState.LoginFailed: stateKey = "NetworkStatusFailed"; break;
                default: throw new ArgumentOutOfRangeException(nameof(state), state, "Unknown network state.");
            }

            View.stateText.Set(Format(stateKey));
            View.stateLabel.color = state == NetConnectionState.Online
                ? new Color32(111, 212, 167, 255)
                : state == NetConnectionState.Offline || state == NetConnectionState.LoginFailed
                    ? new Color32(234, 133, 126, 255)
                    : new Color32(232, 192, 111, 255);
            View.accountText.Set(net.UserId > 0
                ? Format("NetworkStatusAccount", net.UserId.ToString(CultureInfo.InvariantCulture))
                : Format("NetworkStatusAccountPending"));

            NetworkLatencyMonitor latency = net.Latency;
            if (state != NetConnectionState.Online)
            {
                View.latencyText.Set(Format("NetworkStatusLatencyUnavailable"));
                View.averageText.Set(Format("NetworkStatusAverageUnavailable"));
                return;
            }

            View.latencyText.Set(latency.IsTimedOut
                ? Format("NetworkStatusProbeTimeout")
                : latency.HasSamples
                    ? Format("NetworkStatusLatency", Math.Round(latency.LatestMilliseconds))
                    : Format("NetworkStatusSampling"));
            View.averageText.Set(latency.HasSamples
                ? Format("NetworkStatusAverage", Math.Round(latency.AverageMilliseconds))
                : Format("NetworkStatusAverageUnavailable"));
        }

        private static string Format(string key, params object[] args)
        {
            return string.Format(CultureInfo.InvariantCulture, GameEntry.Luban.Tables.TbFormatText.Get(key).Format, args);
        }
    }
}
