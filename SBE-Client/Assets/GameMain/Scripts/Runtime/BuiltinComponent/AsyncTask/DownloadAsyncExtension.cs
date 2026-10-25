using Cysharp.Threading.Tasks;
using GameFramework.Download;
using UnityGameFramework.Runtime;
using DownloadFailureEventArgs = UnityGameFramework.Runtime.DownloadFailureEventArgs;
using DownloadSuccessEventArgs = UnityGameFramework.Runtime.DownloadSuccessEventArgs;

namespace SepCore.AsyncTask
{
    /// <summary>
    /// Download 异步扩展方法
    /// </summary>
    public static class DownloadAsyncExtension
    {
        /// <summary>
        /// 异步下载文件
        /// </summary>
        /// <param name="downloadComponent">下载组件</param>
        /// <param name="downloadUri">下载地址</param>
        /// <param name="downloadPath">下载后存放路径</param>
        /// <param name="userData">用户自定义数据</param>
        /// <param name="timeout">超时时间（秒），0表示不超时</param>
        /// <returns>下载成功事件</returns>
        public static UniTask<DownloadSuccessEventArgs> DownloadFileAsync(this DownloadComponent downloadComponent,
            string downloadUri,
            string downloadPath,
            object userData = null,
            float timeout = 300f)
        {
            int serialId = 0;
            UniTask<DownloadSuccessEventArgs> waitTask =
                AsyncTaskHelper.WaitSuccessOrFailureAsync<DownloadSuccessEventArgs, DownloadFailureEventArgs>(
                    DownloadSuccessEventArgs.EventId,
                    DownloadFailureEventArgs.EventId,
                    successArgs => successArgs.SerialId == serialId,
                    failureArgs => failureArgs.SerialId == serialId,
                    timeout
                );

            serialId = downloadComponent.AddDownload(downloadPath, downloadUri, userData);
            return waitTask;
        }

        /// <summary>
        /// 异步下载文件（简化版本）
        /// </summary>
        /// <param name="downloadComponent">下载组件</param>
        /// <param name="downloadUri">下载地址</param>
        /// <param name="downloadPath">下载后存放路径</param>
        /// <param name="timeout">超时时间（秒），0表示不超时</param>
        /// <returns>下载成功事件</returns>
        public static UniTask<DownloadSuccessEventArgs> DownloadFileAsync(this DownloadComponent downloadComponent,
            string downloadUri,
            string downloadPath,
            float timeout = 300f)
        {
            return DownloadFileAsync(downloadComponent, downloadUri, downloadPath, null, timeout);
        }
    }
}