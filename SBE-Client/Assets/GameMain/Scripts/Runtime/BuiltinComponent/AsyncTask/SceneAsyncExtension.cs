using Cysharp.Threading.Tasks;
using GameFramework.Scene;
using UnityGameFramework.Runtime;
using LoadSceneFailureEventArgs = UnityGameFramework.Runtime.LoadSceneFailureEventArgs;
using LoadSceneSuccessEventArgs = UnityGameFramework.Runtime.LoadSceneSuccessEventArgs;
using UnloadSceneFailureEventArgs = UnityGameFramework.Runtime.UnloadSceneFailureEventArgs;
using UnloadSceneSuccessEventArgs = UnityGameFramework.Runtime.UnloadSceneSuccessEventArgs;

namespace SepCore.AsyncTask
{
    /// <summary>
    /// Scene 异步扩展方法
    /// </summary>
    public static class SceneAsyncExtension
    {
        /// <summary>
        /// 异步加载场景
        /// </summary>
        /// <param name="sceneComponent">场景组件</param>
        /// <param name="sceneAssetName">场景资源名称</param>
        /// <param name="userData">用户自定义数据</param>
        /// <param name="timeout">超时时间（秒），0表示不超时</param>
        /// <returns>加载成功事件</returns>
        public static UniTask<LoadSceneSuccessEventArgs> LoadSceneAsync(this SceneComponent sceneComponent,
            string sceneAssetName,
            object userData = null,
            float timeout = 60f)
        {
            UniTask<LoadSceneSuccessEventArgs> waitTask = AsyncTaskHelper.WaitSuccessOrFailureAsync<LoadSceneSuccessEventArgs, LoadSceneFailureEventArgs>(
                LoadSceneSuccessEventArgs.EventId,
                LoadSceneFailureEventArgs.EventId,
                successArgs => successArgs.SceneAssetName == sceneAssetName && ReferenceEquals(successArgs.UserData, userData),
                failureArgs => failureArgs.SceneAssetName == sceneAssetName && ReferenceEquals(failureArgs.UserData, userData),
                timeout
            );

            sceneComponent.LoadScene(sceneAssetName, userData);
            return waitTask;
        }

        /// <summary>
        /// 异步卸载场景
        /// </summary>
        /// <param name="sceneComponent">场景组件</param>
        /// <param name="sceneAssetName">场景资源名称</param>
        /// <param name="userData">用户自定义数据</param>
        /// <param name="timeout">超时时间（秒），0表示不超时</param>
        /// <returns>卸载成功事件</returns>
        public static UniTask<UnloadSceneSuccessEventArgs> UnloadSceneAsync(this SceneComponent sceneComponent,
            string sceneAssetName,
            object userData = null,
            float timeout = 60f)
        {
            UniTask<UnloadSceneSuccessEventArgs> waitTask = AsyncTaskHelper.WaitSuccessOrFailureAsync<UnloadSceneSuccessEventArgs, UnloadSceneFailureEventArgs>(
                UnloadSceneSuccessEventArgs.EventId,
                UnloadSceneFailureEventArgs.EventId,
                successArgs => successArgs.SceneAssetName == sceneAssetName && ReferenceEquals(successArgs.UserData, userData),
                failureArgs => failureArgs.SceneAssetName == sceneAssetName && ReferenceEquals(failureArgs.UserData, userData),
                timeout
            );

            sceneComponent.UnloadScene(sceneAssetName, userData);
            return waitTask;
        }
    }
}
