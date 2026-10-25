using GameFramework;
using GameFramework.Event;

namespace SepCore.Base
{
    /// <summary>
    /// 单局共享背包内容变更事件。
    /// 物品拾取、丢弃、整理、跨容器移动时触发，驱动 HUD 剩余槽位与战利品总价值刷新，以及背包界面重绘。
    /// </summary>
    public sealed class RoundBackpackChangedEventArgs : GameEventArgs
    {
        public static int EventId => typeof(RoundBackpackChangedEventArgs).GetHashCode();

        public override int Id => EventId;

        public int UsedSlots { get; private set; }

        public int MaxSlots { get; private set; }

        public int TotalValue { get; private set; }

        public RoundBackpackChangedEventArgs()
        {
            UsedSlots = 0;
            MaxSlots = 0;
            TotalValue = 0;
        }

        public static RoundBackpackChangedEventArgs Create(int usedSlots, int maxSlots, int totalValue)
        {
            var args = ReferencePool.Acquire<RoundBackpackChangedEventArgs>();
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
