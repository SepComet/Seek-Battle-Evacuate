using GameFramework;
using GameFramework.Event;

namespace SepCore.Base
{
    /// <summary>
    /// 公共页头请求关闭对应顶级页面，返回其下方的大厅。
    /// </summary>
    public sealed class LobbyPageBackRequestedEventArgs : GameEventArgs
    {
        public static int EventId => typeof(LobbyPageBackRequestedEventArgs).GetHashCode();

        public override int Id => EventId;

        public int SerialId { get; private set; }

        public static LobbyPageBackRequestedEventArgs Create(int serialId)
        {
            LobbyPageBackRequestedEventArgs args = ReferencePool.Acquire<LobbyPageBackRequestedEventArgs>();
            args.SerialId = serialId;
            return args;
        }

        public override void Clear()
        {
            SerialId = 0;
        }
    }
}
