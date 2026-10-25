using GameFramework;
using GameFramework.Event;

namespace SepCore.Base
{
    /// <summary>
    /// 撤离完成事件。
    /// 领队在撤离点触发器内完成撤离进度时触发，由主流程订阅并进入成功撤离结算。
    /// </summary>
    public sealed class EvacuationCompletedEventArgs : GameEventArgs
    {
        public static int EventId => typeof(EvacuationCompletedEventArgs).GetHashCode();

        public override int Id => EventId;

        public EvacuationCompletedEventArgs()
        {
        }

        public static EvacuationCompletedEventArgs Create()
        {
            return ReferencePool.Acquire<EvacuationCompletedEventArgs>();
        }

        public override void Clear()
        {
        }
    }
}