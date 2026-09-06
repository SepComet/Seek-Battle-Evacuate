using GameFramework;
using GameFramework.Event;

namespace SepCore.Base
{
    /// <summary>
    /// 单局保险箱内容变更事件。
    /// 物品从背包移入/移出保险箱时触发，驱动背包界面保险箱列表与容量提示刷新。
    /// </summary>
    public sealed class RoundSafeCaseChangedEventArgs : GameEventArgs
    {
        public static int EventId => typeof(RoundSafeCaseChangedEventArgs).GetHashCode();

        public override int Id => EventId;

        public int UsedSlots { get; private set; }

        public int MaxSlots { get; private set; }

        public int TotalValue { get; private set; }

        public RoundSafeCaseChangedEventArgs()
        {
            UsedSlots = 0;
            MaxSlots = 0;
            TotalValue = 0;
        }

        public static RoundSafeCaseChangedEventArgs Create(int usedSlots, int maxSlots, int totalValue)
        {
            var args = ReferencePool.Acquire<RoundSafeCaseChangedEventArgs>();
            args.UsedSlots = usedSlots;
            args.MaxSlots = maxSlots;
            args.TotalValue = totalValue;
            return args;
        }

        public override void Clear()
        {
            UsedSlots = 0;
            MaxSlots = 0;
            TotalValue = 0;
        }
    }
}
