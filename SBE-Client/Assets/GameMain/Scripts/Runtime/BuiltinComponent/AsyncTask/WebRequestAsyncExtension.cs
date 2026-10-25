using Cysharp.Threading.Tasks;
using UnityGameFramework.Runtime;
using WebRequestFailureEventArgs = UnityGameFramework.Runtime.WebRequestFailureEventArgs;
using WebRequestSuccessEventArgs = UnityGameFramework.Runtime.WebRequestSuccessEventArgs;

namespace SepCore.AsyncTask
{
    /// <summary>
    /// WebRequest 异步扩展方法
    /// </summary>
    public static class WebRequestAsyncExtension
    {
        /// <summary>
        /// 异步发送Web请求
        /// </summary>
        /// <param name="webRequestComponent">WebRequest组件</param>
        /// <param name="webRequestUri">请求地址</param>
        /// <param name="userData">用户自定义数据</param>
        /// <param name="timeout">超时时间（秒），0表示不超时</param>
        /// <returns>请求成功事件</returns>
        public static UniTask<WebRequestSuccessEventArgs> GetWebRequestAsync(this WebRequestComponent webRequestComponent,
            string webRequestUri,
            object userData = null,
            float timeout = 30f)
        {
            int serialId = 0;
            UniTask<WebRequestSuccessEventArgs> waitTask = AsyncTaskHelper.WaitSuccessOrFailureAsync<WebRequestSuccessEventArgs, WebRequestFailureEventArgs>(
                WebRequestSuccessEventArgs.EventId,
                WebRequestFailureEventArgs.EventId,
                successArgs => successArgs.SerialId == serialId,
                failureArgs => failureArgs.SerialId == serialId,
                timeout
            );

            serialId = webRequestComponent.AddWebRequest(webRequestUri, userData);
            return waitTask;
        }

        /// <summary>
        /// 异步发送Web请求并获取字节数据
        /// </summary>
        /// <param name="webRequestComponent">WebRequest组件</param>
        /// <param name="webRequestUri">请求地址</param>
        /// <param name="userData">用户自定义数据</param>
        /// <param name="timeout">超时时间（秒），0表示不超时</param>
        /// <returns>响应字节数据</returns>
        public static UniTask<byte[]> GetWebRequestBytesAsync(this WebRequestComponent webRequestComponent,
            string webRequestUri,
            object userData = null,
            float timeout = 30f)
        {
            return GetWebRequestAsync(webRequestComponent, webRequestUri, userData, timeout)
                .ContinueWith(successArgs => successArgs.GetWebResponseBytes());
        }

        /// <summary>
        /// 异步发送Web请求并获取字符串数据
        /// </summary>
        /// <param name="webRequestComponent">WebRequest组件</param>
        /// <param name="webRequestUri">请求地址</param>
        /// <param name="userData">用户自定义数据</param>
        /// <param name="timeout">超时时间（秒），0表示不超时</param>
        /// <returns>响应字符串数据</returns>
        public static UniTask<string> GetWebRequestStringAsync(this WebRequestComponent webRequestComponent,
            string webRequestUri,
            object userData = null,
            float timeout = 30f)
        {
            return GetWebRequestBytesAsync(webRequestComponent, webRequestUri, userData, timeout)
                .ContinueWith(bytes => System.Text.Encoding.UTF8.GetString(bytes));
        }

        /// <summary>
        /// 异步发送POST请求
        /// </summary>
        /// <param name="webRequestComponent">WebRequest组件</param>
        /// <param name="webRequestUri">请求地址</param>
        /// <param name="postData">POST数据</param>
        /// <param name="userData">用户自定义数据</param>
        /// <param name="timeout">超时时间（秒），0表示不超时</param>
        /// <returns>请求成功事件</returns>
        public static UniTask<WebRequestSuccessEventArgs> PostWebRequestAsync(this WebRequestComponent webRequestComponent,
            string webRequestUri,
            byte[] postData,
            object userData = null,
            float timeout = 30f)
        {
            int serialId = 0;
            UniTask<WebRequestSuccessEventArgs> waitTask = AsyncTaskHelper.WaitSuccessOrFailureAsync<WebRequestSuccessEventArgs, WebRequestFailureEventArgs>(
                WebRequestSuccessEventArgs.EventId,
                WebRequestFailureEventArgs.EventId,
                successArgs => successArgs.SerialId == serialId,
                failureArgs => failureArgs.SerialId == serialId,
                timeout
            );

            serialId = webRequestComponent.AddWebRequest(webRequestUri, postData, userData);
            return waitTask;
        }
    }
}
