using UnityEngine;

namespace SepCore.Definition
{
    /// <summary>
    /// 可交互实体类型枚举。
    /// 数值越小，在同等规则下默认优先级越高。
    /// </summary>
    public enum InteractableType
    {
        None = 0,

        /// <summary>
        /// 物资点（最高优先级）。
        /// </summary>
        ResourcePoint = 1,

        /// <summary>
        /// 场景普通道具。
        /// </summary>
        Item = 2,

        /// <summary>
        /// 撤离点（预留）。
        /// </summary>
        EvacuatePoint = 3,
    }

    /// <summary>
    /// 场景可交互实体抽象接口。
    /// 统一规范交互类型、稀有度、空间坐标、交互门禁及执行入口。
    /// </summary>
    public interface IInteractable
    {
        /// <summary>
        /// 交互实体类型。
        /// </summary>
        InteractableType InteractableType { get; }

        /// <summary>
        /// 稀有度（道具按自身配表 Rarity，物资点固定为 Rarity.None）。
        /// </summary>
        Rarity Rarity { get; }

        /// <summary>
        /// 实体世界空间坐标（用于计算与交互发起者的相对距离）。
        /// </summary>
        Vector3 Position { get; }

        /// <summary>
        /// 当前是否允许交互（例如物资点已搜空、道具已被标记拾取中等状态）。
        /// </summary>
        bool CanInteract { get; }

        /// <summary>
        /// 实体所属的 Unity GameObject 引用，用于生命周期存活与激活判定。
        /// </summary>
        GameObject EntityGameObject { get; }

        /// <summary>
        /// 执行交互操作。
        /// </summary>
        /// <param name="interactor">交互发起者 GameObject（通常为领队物体）。</param>
        void OnInteract(GameObject interactor);

        /// <summary>
        /// 设置当前交互目标的选中高亮（描边）状态。
        /// </summary>
        /// <param name="highlight">true 为开启高亮，false 为关闭高亮。</param>
        void SetHighlight(bool highlight);
    }

    /// <summary>
    /// 支持长按交互的场景实体接口。
    /// 在基础交互能力之上，扩展长按开始、每帧推进与松开/打断回调。
    /// </summary>
    public interface IHoldInteractable : IInteractable
    {
        /// <summary>
        /// 开始长按交互。
        /// </summary>
        /// <param name="interactor">交互发起者 GameObject（通常为领队物体）。</param>
        void OnInteractStart(GameObject interactor);

        /// <summary>
        /// 持续长按交互。
        /// </summary>
        /// <param name="interactor">交互发起者 GameObject（通常为领队物体）。</param>
        /// <param name="deltaTime">自上一帧经过的时间（秒）。</param>
        void OnInteractHold(GameObject interactor, float deltaTime);

        /// <summary>
        /// 结束或打断长按交互。
        /// </summary>
        /// <param name="interactor">交互发起者 GameObject（通常为领队物体）。</param>
        void OnInteractEnd(GameObject interactor);
    }
}

