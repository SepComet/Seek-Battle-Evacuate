using GameFramework;
using GameFramework.Event;

namespace SepCore.Base
{
    public class WarehouseSlotItemClickEventArgs : GameEventArgs
    {
        public static int EventId => typeof(WarehouseSlotItemClickEventArgs).GetHashCode();
        
        public override int Id => EventId;
        
        /// <summary>
        /// 被点击格子在整个固定网格中的索引，唯一对应一个格子。
        /// </summary>
        public int SlotId { get; private set; }

        public WarehouseSlotItemClickEventArgs()
        {
            SlotId = 0;
        }
        
        public static WarehouseSlotItemClickEventArgs Create(int slotId)
        {
            var args = ReferencePool.Acquire<WarehouseSlotItemClickEventArgs>();
            args.SlotId = slotId;
            return args;
        }
        
        public override void Clear()
        {
            SlotId = 0;
        }
    }
}