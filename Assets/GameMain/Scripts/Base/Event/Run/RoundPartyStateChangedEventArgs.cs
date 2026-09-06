using GameFramework;
using GameFramework.Event;

namespace SepCore.Base
{
    /// <summary>
    /// 单局出战队伍角色状态变更事件。
    /// 战后 HP/MP 回写、角色复活等触发，驱动背包界面角色卡片或 HUD 状态刷新。
    /// </summary>
    public sealed class RoundPartyStateChangedEventArgs : GameEventArgs
    {
        public static int EventId => typeof(RoundPartyStateChangedEventArgs).GetHashCode();

        public override int Id => EventId;

        public RoundPartyStateChangedEventArgs()
        {
        }

        public static RoundPartyStateChangedEventArgs Create()
        {
            return ReferencePool.Acquire<RoundPartyStateChangedEventArgs>();
        }

        public override void Clear()
        {
        }
    }
}
