using SepCore.Net;
using SepCore.UI;
using UnityEngine;
using UnityGameFramework.Runtime;

namespace SepCore.Exploration
{
    /// <summary>
    /// 联网交互失败的玩家可见提示出口。
    /// 走既有 DialogForm（PauseGame=false，不暂停游戏），并做全局节流：
    /// 玩家贴着敌人或反复长按箱子时，请求失败可能密集出现，只提示一次即可，
    /// 不叠一屏对话框。本类不改变任何游戏状态。
    /// </summary>
    public static class NetworkNotice
    {
        /// <summary>两次提示之间的最短间隔（秒，真实时间）。</summary>
        public const float ThrottleSeconds = 3f;

        private static float _nextAllowedTime;

        /// <summary>
        /// 交互被拒绝（超时/断线/服务器拒绝）时调用：节流内直接返回，否则弹出统一文案。
        /// </summary>
        public static void ShowInteractionRejected()
        {
            Show(RunService.RejectionMessage);
        }

        /// <summary>
        /// 以单按钮对话框提示一条消息（节流同 <see cref="ShowInteractionRejected"/>）。
        /// </summary>
        public static void Show(string message)
        {
            float now = Time.unscaledTime;
            if (now < _nextAllowedTime)
            {
                return;
            }

            _nextAllowedTime = now + ThrottleSeconds;
            Log.Warning("Network interaction notice: {0}", message);

            if (GameEntry.UI == null)
            {
                return;
            }

            GameEntry.UI.OpenDialog(new DialogParams
            {
                Mode = 1,
                Title = "网络异常",
                Message = message,
                PauseGame = false,
            });
        }

        /// <summary>重置节流（单局开始时调用，避免上局末尾的提示压制本局第一条）。</summary>
        public static void ResetThrottle()
        {
            _nextAllowedTime = 0f;
        }
    }
}
