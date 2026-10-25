using GameFramework;
using GameFramework.Event;

namespace SepCore.Base
{
    /// <summary>
    /// 大厅请求退出登录：断开连接、清除本地会话与缓存存档，返回登录流程。
    /// </summary>
    public sealed class LogoutRequestedEventArgs : GameEventArgs
    {
        public static int EventId => typeof(LogoutRequestedEventArgs).GetHashCode();

        public override int Id => EventId;

        public static LogoutRequestedEventArgs Create()
        {
            return ReferencePool.Acquire<LogoutRequestedEventArgs>();
        }

        public override void Clear()
        {
        }
    }
}
