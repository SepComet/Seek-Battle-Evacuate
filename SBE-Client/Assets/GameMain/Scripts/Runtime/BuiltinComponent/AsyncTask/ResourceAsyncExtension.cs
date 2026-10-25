using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using GameFramework.Resource;
using UnityGameFramework.Runtime;
using ResourceApplySuccessEventArgs = UnityGameFramework.Runtime.ResourceApplySuccessEventArgs;
using ResourceUpdateAllCompleteEventArgs = UnityGameFramework.Runtime.ResourceUpdateAllCompleteEventArgs;
using ResourceVerifySuccessEventArgs = UnityGameFramework.Runtime.ResourceVerifySuccessEventArgs;

namespace SepCore.AsyncTask
{
    /// <summary>
    /// Resource 异步扩展方法
    /// </summary>
    public static class ResourceAsyncExtension
    {
        /// <summary>
        /// 异步等待资源更新完成
        /// </summary>
        /// <param name="resourceComponent">资源组件</param>
        /// <param name="timeout">超时时间（秒），0表示不超时</param>
        /// <returns>更新完成事件</returns>
        public static UniTask<ResourceUpdateAllCompleteEventArgs> WaitForResourceUpdateCompleteAsync(this ResourceComponent resourceComponent,
            float timeout = 0f)
        {
            return AsyncTaskHelper.WaitEventAsync<ResourceUpdateAllCompleteEventArgs>(
                ResourceUpdateAllCompleteEventArgs.EventId,
                null,
                timeout
            );
        }

        /// <summary>
        /// 异步等待资源验证完成
        /// </summary>
        /// <param name="resourceComponent">资源组件</param>
        /// <param name="timeout">超时时间（秒），0表示不超时</param>
        /// <returns>验证成功事件</returns>
        public static UniTask<ResourceVerifySuccessEventArgs> WaitForResourceVerifyCompleteAsync(this ResourceComponent resourceComponent,
            float timeout = 0f)
        {
            return AsyncTaskHelper.WaitEventAsync<ResourceVerifySuccessEventArgs>(
                ResourceVerifySuccessEventArgs.EventId,
                null,
                timeout
            );
        }

        /// <summary>
        /// 异步等待资源应用完成
        /// </summary>
        /// <param name="resourceComponent">资源组件</param>
        /// <param name="timeout">超时时间（秒），0表示不超时</param>
        /// <returns>应用成功事件</returns>
        public static UniTask<ResourceApplySuccessEventArgs> WaitForResourceApplyCompleteAsync(this ResourceComponent resourceComponent,
            float timeout = 0f)
        {
            return AsyncTaskHelper.WaitEventAsync<ResourceApplySuccessEventArgs>(
                ResourceApplySuccessEventArgs.EventId,
                null,
                timeout
            );
        }

        /// <summary>
        /// 异步加载资源
        /// </summary>
        /// <typeparam name="T">资源类型</typeparam>
        /// <param name="resourceComponent">资源组件</param>
        /// <param name="assetName">资源名称</param>
        /// <param name="priority">加载优先级</param>
        /// <param name="userData">用户自定义数据</param>
        /// <param name="progress">加载进度</param>
        /// <param name="timeout">超时时间（秒），0表示不超时</param>
        /// <returns>加载的资源</returns>
        public static UniTask<T> LoadAssetAsync<T>(this ResourceComponent resourceComponent,
            string assetName,
            int priority = 0,
            object userData = null,
            IProgress<float> progress = null,
            float timeout = 0f) where T : class
        {
            var tcs = new UniTaskCompletionSource<T>();

            var callbacks = new LoadAssetCallbacks(
                (name, asset, duration, ud) => tcs.TrySetResult((T)asset),
                (name, status, errorMessage, ud) => tcs.TrySetException(new Exception($"加载资源失败: {name}, status: {status}, error: {errorMessage}")),
                (name, p, ud) => progress?.Report(p)
            );

            resourceComponent.LoadAsset(assetName, typeof(T), priority, callbacks, userData);

            if (timeout > 0f)
            {
                UniTask.Delay(TimeSpan.FromSeconds(timeout), cancellationToken: CancellationToken.None)
                    .ContinueWith(() => tcs.TrySetException(new TimeoutException($"加载资源超时: {assetName}, {timeout}秒")));
            }

            return tcs.Task;
        }
    }
}