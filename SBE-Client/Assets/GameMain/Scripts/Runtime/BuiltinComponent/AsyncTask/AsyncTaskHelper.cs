using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using GameFramework.Event;

namespace SepCore.AsyncTask
{
    /// <summary>
    /// 异步任务辅助类，用于将事件回调转换为 UniTask
    /// </summary>
    public static class AsyncTaskHelper
    {
        /// <summary>
        /// 等待指定事件触发
        /// </summary>
        /// <typeparam name="T">事件参数类型</typeparam>
        /// <param name="eventId">事件ID</param>
        /// <param name="predicate">事件过滤条件</param>
        /// <param name="timeout">超时时间（秒），0表示不超时</param>
        /// <returns>事件参数</returns>
        public static UniTask<T> WaitEventAsync<T>(int eventId, Func<T, bool> predicate = null, float timeout = 0f) where T : GameEventArgs
        {
            var tcs = new UniTaskCompletionSource<T>();
            var eventComponent = global::GameEntry.Event;

            EventHandler<GameEventArgs> handler = null;
            handler = (_, e) =>
            {
                var args = e as T;
                if (args == null) return;

                if (predicate != null && !predicate(args)) return;

                eventComponent.Unsubscribe(eventId, handler);
                tcs.TrySetResult(args);
            };

            eventComponent.Subscribe(eventId, handler);

            // 超时处理
            if (timeout > 0f)
            {
                UniTask.Delay(TimeSpan.FromSeconds(timeout), cancellationToken: CancellationToken.None)
                    .ContinueWith(() =>
                    {
                        if (tcs.TrySetException(new TimeoutException($"等待事件超时: {timeout}秒")))
                        {
                            eventComponent.Unsubscribe(eventId, handler);
                        }
                    });
            }

            return tcs.Task;
        }

        /// <summary>
        /// 等待成功或失败事件
        /// </summary>
        /// <typeparam name="TSuccess">成功事件参数类型</typeparam>
        /// <typeparam name="TFailure">失败事件参数类型</typeparam>
        /// <param name="successEventId">成功事件ID</param>
        /// <param name="failureEventId">失败事件ID</param>
        /// <param name="successPredicate">成功事件过滤条件</param>
        /// <param name="failurePredicate">失败事件过滤条件</param>
        /// <param name="timeout">超时时间（秒），0表示不超时</param>
        /// <returns>成功事件参数</returns>
        public static UniTask<TSuccess> WaitSuccessOrFailureAsync<TSuccess, TFailure>(
            int successEventId,
            int failureEventId,
            Func<TSuccess, bool> successPredicate = null,
            Func<TFailure, bool> failurePredicate = null,
            float timeout = 0f)
            where TSuccess : GameEventArgs
            where TFailure : GameEventArgs
        {
            var tcs = new UniTaskCompletionSource<TSuccess>();
            var eventComponent = global::GameEntry.Event;

            // 先声明两个 handler，再赋值 lambda，避免前向引用
            EventHandler<GameEventArgs> successHandler = null;
            EventHandler<GameEventArgs> failureHandler = null;

            successHandler = (_, e) =>
            {
                var args = e as TSuccess;
                if (args == null) return;

                if (successPredicate != null && !successPredicate(args)) return;

                eventComponent.Unsubscribe(successEventId, successHandler);
                eventComponent.Unsubscribe(failureEventId, failureHandler);
                tcs.TrySetResult(args);
            };

            failureHandler = (_, e) =>
            {
                var args = e as TFailure;
                if (args == null) return;

                if (failurePredicate != null && !failurePredicate(args)) return;

                eventComponent.Unsubscribe(successEventId, successHandler);
                eventComponent.Unsubscribe(failureEventId, failureHandler);
                tcs.TrySetException(new Exception($"操作失败: {args}"));
            };

            eventComponent.Subscribe(successEventId, successHandler);
            eventComponent.Subscribe(failureEventId, failureHandler);

            // 超时处理
            if (timeout > 0f)
            {
                UniTask.Delay(TimeSpan.FromSeconds(timeout), cancellationToken: CancellationToken.None)
                    .ContinueWith(() =>
                    {
                        if (tcs.TrySetException(new TimeoutException($"等待事件超时: {timeout}秒")))
                        {
                            eventComponent.Unsubscribe(successEventId, successHandler);
                            eventComponent.Unsubscribe(failureEventId, failureHandler);
                        }
                    });
            }

            return tcs.Task;
        }
    }
}