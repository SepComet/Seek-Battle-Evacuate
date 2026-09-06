using System;
using System.Collections.Generic;
using SepCore.CustomComponent;
using SepCore.Definition;

namespace SepCore.Exploration
{
    internal sealed class ResourcePointGenerator
    {
        private static readonly Rarity[] RarityOrder =
        {
            Rarity.White,
            Rarity.Green,
            Rarity.Blue,
            Rarity.Gold,
            Rarity.Red
        };

        private readonly IReadOnlyList<ResourcePointConfig> _resourceConfigs;
        private readonly IReadOnlyList<ItemConfig> _itemConfigs;
        private readonly IRoundRandomSource _random;

        public ResourcePointGenerator(IReadOnlyList<ResourcePointConfig> resourceConfigs,
            IReadOnlyList<ItemConfig> itemConfigs, IRoundRandomSource random)
        {
            _resourceConfigs = resourceConfigs;
            _itemConfigs = itemConfigs;
            _random = random;
        }

        public List<ResourcePointBuildData> Generate(IReadOnlyList<ResourcePointDefinition> definitions,
            DifficultyTier difficulty)
        {
            List<ResourcePointBuildData> results = new List<ResourcePointBuildData>(definitions.Count);
            foreach (ResourcePointDefinition definition in definitions)
            {
                ResourcePointConfig resourceConfig = FindResourceConfig(definition.ResourcePointId);
                LootGenerationConfig lootConfig = FindLootConfig(resourceConfig, difficulty);
                int itemCount = _random.NextInt(lootConfig.MinCount, lootConfig.MaxCount + 1);
                Dictionary<Rarity, List<ItemConfig>> availableItems = BuildItemPools();
                List<int> itemIds = new List<int>(itemCount);
                for (int i = 0; i < itemCount; i++)
                {
                    Rarity rarity = SelectRarity(lootConfig);
                    List<ItemConfig> rarityItems = availableItems[rarity];
                    int itemIndex = _random.NextInt(0, rarityItems.Count);
                    itemIds.Add(rarityItems[itemIndex].Id);
                    rarityItems.RemoveAt(itemIndex);
                }

                results.Add(new ResourcePointBuildData(definition.Position, definition.ResourcePointId, itemIds));
            }

            return results;
        }

        private ResourcePointConfig FindResourceConfig(int id)
        {
            foreach (ResourcePointConfig config in _resourceConfigs)
            {
                if (config.Id == id)
                {
                    return config;
                }
            }

            throw new InvalidOperationException($"Resource point config '{id}' does not exist.");
        }

        private static LootGenerationConfig FindLootConfig(ResourcePointConfig resourceConfig,
            DifficultyTier difficulty)
        {
            foreach (LootGenerationConfig config in resourceConfig.LootConfigs)
            {
                if (config.Difficulty == difficulty)
                {
                    return config;
                }
            }

            throw new InvalidOperationException(
                $"Resource point '{resourceConfig.Id}' has no loot config for difficulty '{difficulty}'.");
        }

        private Dictionary<Rarity, List<ItemConfig>> BuildItemPools()
        {
            Dictionary<Rarity, List<ItemConfig>> pools = new Dictionary<Rarity, List<ItemConfig>>();
            foreach (Rarity rarity in RarityOrder)
            {
                pools.Add(rarity, new List<ItemConfig>());
            }

            foreach (ItemConfig itemConfig in _itemConfigs)
            {
                pools[itemConfig.Rarity].Add(itemConfig);
            }

            return pools;
        }

        private Rarity SelectRarity(LootGenerationConfig config)
        {
            int totalWeight = 0;
            foreach (Rarity rarity in RarityOrder)
            {
                totalWeight += GetRarityWeight(config, rarity);
            }

            int value = _random.NextInt(0, totalWeight);
            foreach (Rarity rarity in RarityOrder)
            {
                int weight = GetRarityWeight(config, rarity);
                if (value < weight)
                {
                    return rarity;
                }

                value -= weight;
            }

            throw new InvalidOperationException("Resource loot rarity selection failed.");
        }

        private static int GetRarityWeight(LootGenerationConfig config, Rarity rarity)
        {
            switch (rarity)
            {
                case Rarity.White:
                    return config.WhiteWeight;
                case Rarity.Green:
                    return config.GreenWeight;
                case Rarity.Blue:
                    return config.BlueWeight;
                case Rarity.Gold:
                    return config.GoldWeight;
                case Rarity.Red:
                    return config.RedWeight;
                default:
                    throw new ArgumentOutOfRangeException(nameof(rarity), rarity, null);
            }
        }
    }
}
