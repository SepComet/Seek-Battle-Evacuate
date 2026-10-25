using Cysharp.Threading.Tasks;
using UnityGameFramework.Runtime;

namespace SepCore.AsyncTask
{
    /// <summary>
    /// Localization 异步扩展方法
    /// </summary>
    public static class LocalizationAsyncExtension
    {
        /// <summary>
        /// 异步加载字典
        /// </summary>
        /// <param name="localizationComponent">本地化组件</param>
        /// <param name="dictionaryAssetName">字典资源名称</param>
        /// <param name="userData">用户自定义数据</param>
        /// <param name="timeout">超时时间（秒），0表示不超时</param>
        /// <returns>加载成功事件</returns>
        public static UniTask<LoadDictionarySuccessEventArgs> LoadDictionaryAsync(this LocalizationComponent localizationComponent,
            string dictionaryAssetName,
            object userData = null,
            float timeout = 30f)
        {
            UniTask<LoadDictionarySuccessEventArgs> waitTask = AsyncTaskHelper.WaitSuccessOrFailureAsync<LoadDictionarySuccessEventArgs, LoadDictionaryFailureEventArgs>(
                LoadDictionarySuccessEventArgs.EventId,
                LoadDictionaryFailureEventArgs.EventId,
                successArgs => successArgs.DictionaryAssetName == dictionaryAssetName && ReferenceEquals(successArgs.UserData, userData),
                failureArgs => failureArgs.DictionaryAssetName == dictionaryAssetName && ReferenceEquals(failureArgs.UserData, userData),
                timeout
            );

            localizationComponent.ReadData(dictionaryAssetName, userData);
            return waitTask;
        }
    }
}
