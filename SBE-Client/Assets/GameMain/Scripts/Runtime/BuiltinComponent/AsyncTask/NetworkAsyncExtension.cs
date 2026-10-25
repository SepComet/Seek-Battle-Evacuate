using Cysharp.Threading.Tasks;
using UnityGameFramework.Runtime;
using NetworkClosedEventArgs = UnityGameFramework.Runtime.NetworkClosedEventArgs;
using NetworkConnectedEventArgs = UnityGameFramework.Runtime.NetworkConnectedEventArgs;

namespace SepCore.AsyncTask
{
    /// <summary>
    /// Network 异步扩展方法
    /// </summary>
    public static class NetworkAsyncExtension
    {
        /// <summary>
        /// 异步连接网络
        /// </summary>
        /// <param name="networkComponent">网络组件</param>
        /// <param name="networkChannelName">网络频道名称</param>
        /// <param name="address">连接地址</param>
        /// <param name="userData">用户自定义数据</param>
        /// <param name="timeout">超时时间（秒），0表示不超时</param>
        /// <returns>连接成功事件</returns>
        public static UniTask<NetworkConnectedEventArgs> ConnectAsync(this NetworkComponent networkComponent,
            string networkChannelName,
            string address,
            object userData = null,
            float timeout = 30f)
        {
            return AsyncTaskHelper.WaitEventAsync<NetworkConnectedEventArgs>(
                NetworkConnectedEventArgs.EventId,
                args => args.NetworkChannel.Name == networkChannelName,
                timeout
            );
        }

        /// <summary>
        /// 异步连接网络（带端口）
        /// </summary>
        /// <param name="networkComponent">网络组件</param>
        /// <param name="networkChannelName">网络频道名称</param>
        /// <param name="address">连接地址</param>
        /// <param name="port">端口号</param>
        /// <param name="userData">用户自定义数据</param>
        /// <param name="timeout">超时时间（秒），0表示不超时</param>
        /// <returns>连接成功事件</returns>
        public static UniTask<NetworkConnectedEventArgs> ConnectAsync(this NetworkComponent networkComponent,
            string networkChannelName,
            string address,
            int port,
            object userData = null,
            float timeout = 30f)
        {
            return ConnectAsync(networkComponent, networkChannelName, $"{address}:{port}", userData, timeout);
        }

        /// <summary>
        /// 异步断开网络连接
        /// </summary>
        /// <param name="networkComponent">网络组件</param>
        /// <param name="networkChannelName">网络频道名称</param>
        /// <param name="userData">用户自定义数据</param>
        /// <param name="timeout">超时时间（秒），0表示不超时</param>
        /// <returns>关闭事件</returns>
        public static UniTask<NetworkClosedEventArgs> DisconnectAsync(this NetworkComponent networkComponent,
            string networkChannelName,
            object userData = null,
            float timeout = 30f)
        {
            return AsyncTaskHelper.WaitEventAsync<NetworkClosedEventArgs>(
                NetworkClosedEventArgs.EventId,
                args => args.NetworkChannel.Name == networkChannelName,
                timeout
            );
        }
    }
}