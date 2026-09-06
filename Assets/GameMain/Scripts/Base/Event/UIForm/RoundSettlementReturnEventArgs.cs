using GameFramework;
using GameFramework.Event;

namespace SepCore.Base
{
    /// <summary>
    /// 结算界面返回大厅请求事件。
    /// 由结算界面（RoundSettlementForm）点击返回大厅按钮触发，通知主流程返回主菜单或完成结算后续流转。
    /// </summary>
    public sealed class RoundSettlementReturnEventArgs : GameEventArgs
    {
        public static int EventId => typeof(RoundSettlementReturnEventArgs).GetHashCode();

        public override int Id => EventId;

        public static RoundSettlementReturnEventArgs Create()
        {
            return ReferencePool.Acquire<RoundSettlementReturnEventArgs>();
        }

        public override void Clear()
        {
        }
    }
}
