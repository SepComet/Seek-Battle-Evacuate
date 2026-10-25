using Cysharp.Threading.Tasks;
using GameFramework.Sound;
using UnityGameFramework.Runtime;
using PlaySoundFailureEventArgs = UnityGameFramework.Runtime.PlaySoundFailureEventArgs;
using PlaySoundSuccessEventArgs = UnityGameFramework.Runtime.PlaySoundSuccessEventArgs;

namespace SepCore.AsyncTask
{
    /// <summary>
    /// Sound 异步扩展方法
    /// </summary>
    public static class SoundAsyncExtension
    {
        /// <summary>
        /// 异步播放声音
        /// </summary>
        /// <param name="soundComponent">声音组件</param>
        /// <param name="soundAssetName">声音资源名称</param>
        /// <param name="soundGroupName">声音组名称</param>
        /// <param name="playSoundParams">播放声音参数</param>
        /// <param name="userData">用户自定义数据</param>
        /// <param name="timeout">超时时间（秒），0表示不超时</param>
        /// <returns>播放成功事件</returns>
        public static UniTask<PlaySoundSuccessEventArgs> PlaySoundAsync(this SoundComponent soundComponent,
            string soundAssetName,
            string soundGroupName,
            PlaySoundParams playSoundParams = null,
            object userData = null,
            float timeout = 30f)
        {
            int serialId = 0;
            UniTask<PlaySoundSuccessEventArgs> waitTask = AsyncTaskHelper.WaitSuccessOrFailureAsync<PlaySoundSuccessEventArgs, PlaySoundFailureEventArgs>(
                PlaySoundSuccessEventArgs.EventId,
                PlaySoundFailureEventArgs.EventId,
                successArgs => successArgs.SerialId == serialId,
                failureArgs => failureArgs.SerialId == serialId,
                timeout
            );

            serialId = soundComponent.PlaySound(soundAssetName, soundGroupName, 0, playSoundParams, userData);
            return waitTask;
        }

    }
}
