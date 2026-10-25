using Cysharp.Threading.Tasks;
using SepCore.UI;
using UnityGameFramework.Runtime;
using SepCore.Definition;

namespace SepCore.AsyncTask
{
    /// <summary>
    /// UI 异步扩展方法
    /// </summary>
    public static class UIAsyncExtension
    {
        /// <summary>
        /// 等待界面打开完成
        /// </summary>
        /// <param name="uiComponent">UI组件</param>
        /// <param name="serialId">界面序列编号</param>
        /// <param name="timeout">超时时间（秒），0表示不超时</param>
        /// <returns>打开的界面</returns>
        public static UniTask<UIForm> WaitOpenUIFormAsync(this UIComponent uiComponent,
            int serialId,
            float timeout = 30f)
        {
            return AsyncTaskHelper.WaitSuccessOrFailureAsync<OpenUIFormSuccessEventArgs, OpenUIFormFailureEventArgs>(
                OpenUIFormSuccessEventArgs.EventId,
                OpenUIFormFailureEventArgs.EventId,
                successArgs => successArgs.UIForm.SerialId == serialId,
                failureArgs => failureArgs.SerialId == serialId,
                timeout
            ).ContinueWith(successArgs => successArgs.UIForm);
        }

        /// <summary>
        /// 异步打开界面
        /// </summary>
        /// <param name="uiComponent">UI组件</param>
        /// <param name="uiFormAssetName">界面资源名称</param>
        /// <param name="uiGroupName">界面组名称</param>
        /// <param name="pauseCoveredUIForm">是否暂停被覆盖的界面</param>
        /// <param name="userData">用户自定义数据</param>
        /// <param name="timeout">超时时间（秒），0表示不超时</param>
        /// <returns>打开的界面</returns>
        public static UniTask<UIForm> OpenUIFormAsync(this UIComponent uiComponent,
            string uiFormAssetName,
            string uiGroupName,
            bool pauseCoveredUIForm = true,
            object userData = null,
            float timeout = 30f)
        {
            int serialId = 0;
            UniTask<UIForm> waitTask = AsyncTaskHelper.WaitSuccessOrFailureAsync<OpenUIFormSuccessEventArgs, OpenUIFormFailureEventArgs>(
                OpenUIFormSuccessEventArgs.EventId,
                OpenUIFormFailureEventArgs.EventId,
                successArgs => successArgs.UIForm.SerialId == serialId,
                failureArgs => failureArgs.SerialId == serialId,
                timeout
            ).ContinueWith(successArgs => successArgs.UIForm);

            serialId = uiComponent.OpenUIForm(uiFormAssetName, uiGroupName, pauseCoveredUIForm, userData);
            return waitTask;
        }

        /// <summary>
        /// 异步打开界面（通过界面编号）
        /// </summary>
        /// <param name="uiComponent">UI组件</param>
        /// <param name="uiType">界面编号</param>
        /// <param name="userData">用户自定义数据</param>
        /// <param name="timeout">超时时间（秒），0表示不超时</param>
        /// <returns>打开的界面</returns>
        public static UniTask<UIForm> OpenUIFormAsync(this UIComponent uiComponent,
            UIFormType uiType,
            object userData = null,
            float timeout = 30f)
        {
            int? serialId = uiComponent.OpenUIForm(uiType, userData);
            if (!serialId.HasValue)
            {
                return UniTask.FromResult<UIForm>(null);
            }

            return uiComponent.WaitOpenUIFormAsync(serialId.Value, timeout);
        }

        /// <summary>
        /// 异步关闭界面
        /// </summary>
        /// <param name="uiComponent">UI组件</param>
        /// <param name="serialId">界面序列编号</param>
        /// <param name="userData">用户自定义数据</param>
        /// <param name="timeout">超时时间（秒），0表示不超时</param>
        /// <returns>关闭完成事件</returns>
        public static UniTask<CloseUIFormCompleteEventArgs> CloseUIFormAsync(this UIComponent uiComponent,
            int serialId,
            object userData = null,
            float timeout = 30f)
        {
            UniTask<CloseUIFormCompleteEventArgs> waitTask = AsyncTaskHelper.WaitEventAsync<CloseUIFormCompleteEventArgs>(
                CloseUIFormCompleteEventArgs.EventId,
                args => args.SerialId == serialId,
                timeout
            );

            uiComponent.CloseUIForm(serialId, userData);
            return waitTask;
        }

        /// <summary>
        /// 异步关闭界面（通过界面对象）
        /// </summary>
        /// <param name="uiComponent">UI组件</param>
        /// <param name="uiForm">要关闭的界面</param>
        /// <param name="userData">用户自定义数据</param>
        /// <param name="timeout">超时时间（秒），0表示不超时</param>
        /// <returns>关闭完成事件</returns>
        public static UniTask<CloseUIFormCompleteEventArgs> CloseUIFormAsync(this UIComponent uiComponent,
            UIForm uiForm,
            object userData = null,
            float timeout = 30f)
        {
            return CloseUIFormAsync(uiComponent, uiForm.SerialId, userData, timeout);
        }
    }
}
