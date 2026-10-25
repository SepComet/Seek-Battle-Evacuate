using System.Collections.Generic;
using SepCore.Definition;
using UnityEngine;

namespace SepCore.Exploration
{
    /// <summary>
    /// 场景可交互目标优先级选择算法（纯逻辑）。
    /// 排序规则：
    /// 1. 实体类型优先级：物资点（ResourcePoint）优先于普通道具（Item）；
    /// 2. 道具之间：先按稀有度（Rarity）从高到低排序，同稀有度再按与领队的欧氏距离从近到远排序；
    /// 3. 物资点之间：按与领队的欧氏距离从近到远排序。
    /// </summary>
    public static class InteractTargetSelector
    {
        /// <summary>
        /// 从候选列表中选出当前优先级最高且允许交互的实体。
        /// </summary>
        /// <param name="candidates">候选可交互实体集合。</param>
        /// <param name="leaderPosition">领队当前所在世界坐标。</param>
        /// <returns>最高优先级的可交互对象；若候选集为空或无合法对象，返回 null。</returns>
        public static IInteractable SelectBestTarget(
            IReadOnlyList<IInteractable> candidates,
            Vector3 leaderPosition)
        {
            if (candidates == null || candidates.Count == 0)
            {
                return null;
            }

            IInteractable best = null;
            for (int i = 0; i < candidates.Count; i++)
            {
                IInteractable candidate = candidates[i];
                if (candidate == null || !candidate.CanInteract)
                {
                    continue;
                }

                // 若关联的实体 GameObject 已被禁用，则忽略
                if (candidate.EntityGameObject != null && !candidate.EntityGameObject.activeInHierarchy)
                {
                    continue;
                }

                if (best == null)
                {
                    best = candidate;
                    continue;
                }

                if (Compare(candidate, best, leaderPosition) < 0)
                {
                    best = candidate;
                }
            }

            return best;
        }

        /// <summary>
        /// 比较两个候选目标的优先级。
        /// 返回值小于 0 表示 a 优先级高于 b（a 更优）；大于 0 表示 b 优先级高于 a；等于 0 表示优先级并列。
        /// </summary>
        public static int Compare(IInteractable a, IInteractable b, Vector3 leaderPosition)
        {
            if (ReferenceEquals(a, b))
            {
                return 0;
            }

            if (a == null)
            {
                return 1;
            }

            if (b == null)
            {
                return -1;
            }

            // 1. 优先按交互类型排序：ResourcePoint (1) < Item (2)
            if (a.InteractableType != b.InteractableType)
            {
                return a.InteractableType.CompareTo(b.InteractableType);
            }

            // 2. 同为普通道具 Item：
            if (a.InteractableType == InteractableType.Item)
            {
                // 2.1 稀有度从高到低排序 (Red(5) > Gold(4) > Blue(3) > Green(2) > White(1) > None(0))
                if (a.Rarity != b.Rarity)
                {
                    return b.Rarity.CompareTo(a.Rarity);
                }

                // 2.2 稀有度相同，按距离升序（距离近者在先）
                float distA = (a.Position - leaderPosition).sqrMagnitude;
                float distB = (b.Position - leaderPosition).sqrMagnitude;
                return distA.CompareTo(distB);
            }

            // 3. 同为物资点 ResourcePoint 或其他类型：按距离升序（距离近者在先）
            float dA = (a.Position - leaderPosition).sqrMagnitude;
            float dB = (b.Position - leaderPosition).sqrMagnitude;
            return dA.CompareTo(dB);
        }
    }
}
