using System;
using Cysharp.Threading.Tasks;
using GameFramework.Entity;
using UnityGameFramework.Runtime;
using HideEntityCompleteEventArgs = UnityGameFramework.Runtime.HideEntityCompleteEventArgs;
using ShowEntityFailureEventArgs = UnityGameFramework.Runtime.ShowEntityFailureEventArgs;
using ShowEntitySuccessEventArgs = UnityGameFramework.Runtime.ShowEntitySuccessEventArgs;

namespace SepCore.AsyncTask
{
    /// <summary>
    /// Entity 异步扩展方法
    /// </summary>
    public static class EntityAsyncExtension
    {
        /// <summary>
        /// 异步显示实体
        /// </summary>
        /// <param name="entityComponent">实体组件</param>
        /// <param name="entityId">实体编号</param>
        /// <param name="entityLogicType">实体逻辑类型</param>
        /// <param name="entityAssetName">实体资源名称</param>
        /// <param name="entityGroupName">实体组名称</param>
        /// <param name="userData">用户自定义数据</param>
        /// <param name="timeout">超时时间（秒），0表示不超时</param>
        /// <returns>显示的实体</returns>
        public static UniTask<UnityGameFramework.Runtime.Entity> ShowEntityAsync(this EntityComponent entityComponent,
            int entityId,
            Type entityLogicType,
            string entityAssetName,
            string entityGroupName,
            object userData = null,
            float timeout = 30f)
        {
            UniTask<UnityGameFramework.Runtime.Entity> waitTask = AsyncTaskHelper
                .WaitSuccessOrFailureAsync<ShowEntitySuccessEventArgs, ShowEntityFailureEventArgs>(
                    ShowEntitySuccessEventArgs.EventId,
                    ShowEntityFailureEventArgs.EventId,
                    successArgs => successArgs.Entity.Id == entityId && ReferenceEquals(successArgs.UserData, userData),
                    failureArgs => failureArgs.EntityId == entityId && ReferenceEquals(failureArgs.UserData, userData),
                    timeout
                ).ContinueWith(successArgs => successArgs.Entity);

            entityComponent.ShowEntity(entityId, entityLogicType, entityAssetName, entityGroupName, userData);
            return waitTask;
        }

        /// <summary>
        /// 异步显示实体（泛型版本）
        /// </summary>
        /// <typeparam name="T">实体逻辑类型</typeparam>
        /// <param name="entityComponent">实体组件</param>
        /// <param name="entityId">实体编号</param>
        /// <param name="entityAssetName">实体资源名称</param>
        /// <param name="entityGroupName">实体组名称</param>
        /// <param name="userData">用户自定义数据</param>
        /// <param name="timeout">超时时间（秒），0表示不超时</param>
        /// <returns>显示的实体</returns>
        public static UniTask<T> ShowEntityAsync<T>(this EntityComponent entityComponent,
            int entityId,
            string entityAssetName,
            string entityGroupName,
            object userData = null,
            float timeout = 30f) where T : EntityLogic
        {
            UniTask<T> waitTask = AsyncTaskHelper
                .WaitSuccessOrFailureAsync<ShowEntitySuccessEventArgs, ShowEntityFailureEventArgs>(
                    ShowEntitySuccessEventArgs.EventId,
                    ShowEntityFailureEventArgs.EventId,
                    successArgs => successArgs.Entity.Id == entityId && ReferenceEquals(successArgs.UserData, userData),
                    failureArgs => failureArgs.EntityId == entityId && ReferenceEquals(failureArgs.UserData, userData),
                    timeout
                ).ContinueWith(successArgs => (T)successArgs.Entity.Logic);

            entityComponent.ShowEntity<T>(entityId, entityAssetName, entityGroupName, userData);
            return waitTask;
        }

        /// <summary>
        /// 异步隐藏实体
        /// </summary>
        /// <param name="entityComponent">实体组件</param>
        /// <param name="entityId">实体编号</param>
        /// <param name="userData">用户自定义数据</param>
        /// <param name="timeout">超时时间（秒），0表示不超时</param>
        /// <returns>隐藏完成事件</returns>
        public static UniTask<HideEntityCompleteEventArgs> HideEntityAsync(this EntityComponent entityComponent,
            int entityId,
            object userData = null,
            float timeout = 30f)
        {
            UniTask<HideEntityCompleteEventArgs> waitTask = AsyncTaskHelper.WaitEventAsync<HideEntityCompleteEventArgs>(
                HideEntityCompleteEventArgs.EventId,
                args => args.EntityId == entityId,
                timeout
            );

            entityComponent.HideEntity(entityId, userData);
            return waitTask;
        }

        /// <summary>
        /// 异步隐藏实体（通过实体对象）
        /// </summary>
        /// <param name="entityComponent">实体组件</param>
        /// <param name="entity">要隐藏的实体</param>
        /// <param name="userData">用户自定义数据</param>
        /// <param name="timeout">超时时间（秒），0表示不超时</param>
        /// <returns>隐藏完成事件</returns>
        public static UniTask<HideEntityCompleteEventArgs> HideEntityAsync(this EntityComponent entityComponent,
            UnityGameFramework.Runtime.Entity entity,
            object userData = null,
            float timeout = 30f)
        {
            return HideEntityAsync(entityComponent, entity.Id, userData, timeout);
        }
    }
}