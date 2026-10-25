using System;
using Cysharp.Threading.Tasks;

namespace SepCore.Net
{
    /// <summary>
    /// 服务器连接接口。同步服务的唯一传输依赖；测试用假连接，正式用 WebSocket 实现。
    /// 事件与回调全部在主线程触发（由宿主组件在 Update 中泵送）。
    /// </summary>
    public interface IServerConnection
    {
        /// <summary>
        /// 是否处于已连接状态。
        /// </summary>
        bool IsConnected { get; }

        /// <summary>
        /// 连接服务器。返回是否成功。
        /// </summary>
        UniTask<bool> ConnectAsync(string uri);

        /// <summary>
        /// 主动断开。
        /// </summary>
        void Close();

        /// <summary>
        /// 发送一条 JSON 文本。返回是否已入队（未连接返回 false）。
        /// </summary>
        bool TrySend(string json);

        /// <summary>
        /// 收到服务器消息（主线程）。
        /// </summary>
        event Action<string> MessageReceived;

        /// <summary>
        /// 连接断开（主线程）。
        /// </summary>
        event Action Disconnected;
    }
}
