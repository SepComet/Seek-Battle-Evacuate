using GameFramework;
using GameFramework.Event;
using SepCore.Definition;

namespace SepCore.Base
{
    /// <summary>
    /// 领队当前可交互目标变更事件。
    /// 当领队探测器检测到有效交互目标变更、出现新最高优先级目标或脱离可交互状态时触发。
    /// 驱动 UI 交互按钮的启用/置灰以及交互目标信息更新。
    /// </summary>
    public sealed class LeaderInteractTargetChangedEventArgs : GameEventArgs
    {
        public static int EventId => typeof(LeaderInteractTargetChangedEventArgs).GetHashCode();

        public override int Id => EventId;

        /// <summary>
        /// 当前是否存在可交互目标（是否处于可交互状态）。
        /// </summary>
        public bool HasTarget { get; private set; }

        /// <summary>
        /// 当前最高优先级的目标交互类型。
        /// </summary>
        public InteractableType TargetType { get; private set; }

        /// <summary>
        /// 当前最高优先级的可交互目标引用（无目标时为 null）。
        /// </summary>
        public IInteractable Target { get; private set; }

        public LeaderInteractTargetChangedEventArgs()
        {
            HasTarget = false;
            TargetType = InteractableType.None;
            Target = null;
        }

        public static LeaderInteractTargetChangedEventArgs Create(IInteractable target)
        {
            var args = ReferencePool.Acquire<LeaderInteractTargetChangedEventArgs>();
            args.Target = target;
            args.HasTarget = target != null && target.CanInteract;
            args.TargetType = args.HasTarget ? target.InteractableType : InteractableType.None;
            return args;
        }

        public override void Clear()
        {
            HasTarget = false;
            TargetType = InteractableType.None;
            Target = null;
        }
    }
}
