using System;
using System.Collections.Generic;
using SepCore.CustomComponent;
using SepCore.Definition;
using UnityEngine;

namespace SepCore.Exploration
{
    internal sealed class EnemyPointGenerator
    {
        private readonly IReadOnlyList<EnemyPartyConfig> _enemyPartyConfigs;
        private readonly IRoundRandomSource _random;

        public EnemyPointGenerator(IReadOnlyList<EnemyPartyConfig> enemyPartyConfigs, IRoundRandomSource random)
        {
            _enemyPartyConfigs = enemyPartyConfigs;
            _random = random;
        }

        public List<EnemyPointBuildData> Generate(IReadOnlyList<Vector2> definitions,
            IReadOnlyList<WeightedEnemyParty> threatWeights)
        {
            List<EnemyPointBuildData> results = new List<EnemyPointBuildData>();
            foreach (Vector2 position in definitions)
            {
                EnemyPartyThreatLevel threatLevel = SelectThreatLevel(threatWeights);
                if (threatLevel == EnemyPartyThreatLevel.None)
                {
                    continue;
                }

                List<EnemyPartyConfig> partyPool = BuildPartyPool(threatLevel);
                int partyIndex = _random.NextInt(0, partyPool.Count);
                results.Add(new EnemyPointBuildData(position, threatLevel, partyPool[partyIndex].Id));
            }

            return results;
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

        private EnemyPartyThreatLevel SelectThreatLevel(IReadOnlyList<WeightedEnemyParty> configs)
        {
            int totalWeight = 0;
            foreach (WeightedEnemyParty config in configs)
            {
                totalWeight += config.Weight;
            }

            int value = _random.NextInt(0, totalWeight);
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
