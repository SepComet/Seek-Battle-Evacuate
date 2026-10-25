using System;
using System.Collections.Generic;
using SepCore.Definition;
using UnityEngine;

namespace SepCore.Exploration
{
    /// <summary>
    /// 敌点生成（exploration-online-v1 契约 §3 第 2 段）。
    /// 每个敌点从 SubSeed(master, KindEnemy, i) 派生独立 PRNG，
    /// 消费序固定为：威胁 → 队伍池下标（threat=None 时不再消费）。
    /// 摆位出口走同一条威胁选择路径，只是不继续消费队伍池随机。
    /// </summary>
    internal sealed class EnemyPointGenerator
    {
        private readonly IReadOnlyList<EnemyPartyConfig> _enemyPartyConfigs;

        public EnemyPointGenerator(IReadOnlyList<EnemyPartyConfig> enemyPartyConfigs)
        {
            _enemyPartyConfigs = enemyPartyConfigs;
        }

        /// <summary>
        /// 摆位阶段：敌点存在性与威胁等级（威胁为 None 时视为不存在），不消费队伍池随机。
        /// </summary>
        public static EnemyPointPlacementData PlacePoint(Vector2 position, int index, long masterSeed,
            IReadOnlyList<WeightedEnemyParty> threatWeights)
        {
            return RollPlacement(position, index, masterSeed, threatWeights, out _);
        }

        /// <summary>
        /// 完整生成：把 <see cref="PlacePoint"/> 的同一次掷点结果继续用于队伍池下标；
        /// threat=None 时返回 null（该点不生成）。
        /// </summary>
        public EnemyPointBuildData BuildPoint(Vector2 position, int index, long masterSeed,
            IReadOnlyList<WeightedEnemyParty> threatWeights)
        {
            EnemyPointPlacementData placement = RollPlacement(position, index, masterSeed, threatWeights,
                out SplitMix64 pointRng);
            if (!placement.Present)
            {
                return null;
            }

            List<EnemyPartyConfig> partyPool = BuildPartyPool(placement.ThreatLevel);
            if (partyPool.Count == 0)
            {
                throw new InvalidOperationException(
                    $"No enemy party config exists for threat level '{placement.ThreatLevel}'.");
            }

            int partyIndex = pointRng.NextInt(0, partyPool.Count);
            return new EnemyPointBuildData(index, placement.Position, placement.ThreatLevel,
                partyPool[partyIndex].Id);
        }

        /// <summary>
        /// 摆位结果的唯一来源：新建本点 PRNG 并掷威胁等级，PRNG 交回调用方以便继续消费。
        /// 完整生成与摆位出口都从这里取威胁，不存在第二套掷点逻辑。
        /// </summary>
        private static EnemyPointPlacementData RollPlacement(Vector2 position, int index, long masterSeed,
            IReadOnlyList<WeightedEnemyParty> threatWeights, out SplitMix64 pointRng)
        {
            pointRng = CreatePointRng(masterSeed, index);
            EnemyPartyThreatLevel threatLevel = SelectThreatLevel(pointRng, threatWeights);
            return new EnemyPointPlacementData(index, position, threatLevel,
                threatLevel != EnemyPartyThreatLevel.None);
        }

        private static SplitMix64 CreatePointRng(long masterSeed, int index)
        {
            return new SplitMix64(ExplorationSeed.SubSeed(masterSeed, ExplorationSeed.KindEnemy, index));
        }

        private List<EnemyPartyConfig> BuildPartyPool(EnemyPartyThreatLevel threatLevel)
        {
            List<EnemyPartyConfig> partyPool = new List<EnemyPartyConfig>();
            foreach (EnemyPartyConfig partyConfig in _enemyPartyConfigs)
            {
                if (partyConfig.ThreatConfig == threatLevel)
                {
                    partyPool.Add(partyConfig);
                }
            }

            return partyPool;
        }

        private static EnemyPartyThreatLevel SelectThreatLevel(SplitMix64 pointRng,
            IReadOnlyList<WeightedEnemyParty> configs)
        {
            int totalWeight = 0;
            foreach (WeightedEnemyParty config in configs)
            {
                totalWeight += config.Weight;
            }

            if (totalWeight <= 0)
            {
                throw new InvalidOperationException("Enemy threat weights are all zero.");
            }

            int value = pointRng.NextInt(0, totalWeight);
            foreach (WeightedEnemyParty config in configs)
            {
                if (value < config.Weight)
                {
                    return config.Threat;
                }

                value -= config.Weight;
            }

            throw new InvalidOperationException("Enemy threat selection failed.");
        }
    }
}
