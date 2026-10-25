using UnityEngine;

namespace SepCore.Exploration
{
    /// <summary>
    /// 角色输入抽象接口。
    /// 统一规范移动向量采样与交互按键状态（支持点按与长按）。
    /// </summary>
    public interface ICharacterInput
    {
        /// <summary>
        /// 2D 移动输入向量，模长钳制在 [0, 1] 之间。
        /// </summary>
        Vector2 MoveVector { get; }

        /// <summary>
        /// 交互按键当前是否处于按住状态（用于持续长按搜索物资等交互）。
        /// </summary>
        bool IsInteracting { get; }

        /// <summary>
        /// 本帧是否点按触发了交互按键（用于单次交互、拾取等）。
        /// </summary>
        bool InteractTriggered { get; }

        /// <summary>
        /// 本帧是否松开了交互按键（用于中断或完成长按）。
        /// </summary>
        bool InteractReleased { get; }

        /// <summary>
        /// 当前是否有任何有效输入（存在移动偏移或交互触发）。
        /// </summary>
        bool HasInput { get; }
    }
}
