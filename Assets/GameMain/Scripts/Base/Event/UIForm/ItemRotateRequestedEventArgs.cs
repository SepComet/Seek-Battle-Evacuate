using GameFramework;
using GameFramework.Event;

namespace SepCore.Base
{
    /// <summary>
    /// 道具旋转请求事件。
    /// 由顶层拖拽遮罩表单（ItemDragOverlayForm）的旋转按钮触发，通知当前处于拖拽状态的背包网格执行 90° 旋转。
    /// </summary>
    public sealed class ItemRotateRequestedEventArgs : GameEventArgs
    {
        public static int EventId => typeof(ItemRotateRequestedEventArgs).GetHashCode();

        public override int Id => EventId;

        public static ItemRotateRequestedEventArgs Create()
        {
            return ReferencePool.Acquire<ItemRotateRequestedEventArgs>();
        }

        public override void Clear()
        {
        }
    }
}
