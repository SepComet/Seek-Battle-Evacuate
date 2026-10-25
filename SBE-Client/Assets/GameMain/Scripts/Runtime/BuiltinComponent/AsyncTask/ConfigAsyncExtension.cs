using Cysharp.Threading.Tasks;
using UnityGameFramework.Runtime;

namespace SepCore.AsyncTask
{
    /// <summary>
    /// Config 异步扩展方法
    /// </summary>
    public static class ConfigAsyncExtension
    {
        /// <summary>
        /// 异步加载配置
        /// </summary>
        /// <param name="configComponent">配置组件</param>
        /// <param name="configAssetName">配置资源名称</param>
        /// <param name="userData">用户自定义数据</param>
        /// <param name="timeout">超时时间（秒），0表示不超时</param>
        /// <returns>加载成功事件</returns>
        public static UniTask<LoadConfigSuccessEventArgs> LoadConfigAsync(this ConfigComponent configComponent,
            string configAssetName,
            object userData = null,
            float timeout = 30f)
        {
            UniTask<LoadConfigSuccessEventArgs> waitTask = AsyncTaskHelper.WaitSuccessOrFailureAsync<LoadConfigSuccessEventArgs, LoadConfigFailureEventArgs>(
                LoadConfigSuccessEventArgs.EventId,
                LoadConfigFailureEventArgs.EventId,
                successArgs => successArgs.ConfigAssetName == configAssetName && ReferenceEquals(successArgs.UserData, userData),
                failureArgs => failureArgs.ConfigAssetName == configAssetName && ReferenceEquals(failureArgs.UserData, userData),
                timeout
            );

            configComponent.ReadData(configAssetName, userData);
            return waitTask;
        }

        /// <summary>
        /// 异步加载配置（通过字典加载）
        /// </summary>
        /// <param name="configComponent">配置组件</param>
        /// <param name="dictionaryName">字典名称</param>
        /// <param name="userData">用户自定义数据</param>
        /// <param name="timeout">超时时间（秒），0表示不超时</param>
        /// <returns>加载成功事件</returns>
        public static UniTask<LoadConfigSuccessEventArgs> LoadConfigFromDictionaryAsync(this ConfigComponent configComponent,
            string dictionaryName,
            object userData = null,
            float timeout = 30f)
        {
            UniTask<LoadConfigSuccessEventArgs> waitTask = AsyncTaskHelper.WaitSuccessOrFailureAsync<LoadConfigSuccessEventArgs, LoadConfigFailureEventArgs>(
                LoadConfigSuccessEventArgs.EventId,
                LoadConfigFailureEventArgs.EventId,
                successArgs => successArgs.ConfigAssetName == dictionaryName && ReferenceEquals(successArgs.UserData, userData),
                failureArgs => failureArgs.ConfigAssetName == dictionaryName && ReferenceEquals(failureArgs.UserData, userData),
                timeout
            );

            configComponent.ReadData(dictionaryName, userData);
            return waitTask;
        }
    }
}
