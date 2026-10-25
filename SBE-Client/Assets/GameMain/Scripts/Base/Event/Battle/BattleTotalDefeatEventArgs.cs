using GameFramework;
using GameFramework.Event;

namespace SepCore.Base
{
    /// <summary>
    /// 全队战斗死亡事件。
    /// 当回合制战斗以全员阵亡（TotalDefeat）结束时触发，通知主探索流程进入失败结算。
    /// </summary>
    public sealed class BattleTotalDefeatEventArgs : GameEventArgs
    {
        public static int EventId => typeof(BattleTotalDefeatEventArgs).GetHashCode();

        public override int Id => EventId;

        public BattleTotalDefeatEventArgs()
        {
        }

        public static BattleTotalDefeatEventArgs Create()
        {
            return ReferencePool.Acquire<BattleTotalDefeatEventArgs>();
        }

        public override void Clear()
        {
        }
    }
}
