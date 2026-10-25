using System;
using System.Collections.Generic;
using SepCore.Definition;

namespace SepCore.Exploration
{
    /// <summary>
    /// 物资点生成（exploration-online-v1 契约 §3 第 1 段）。
    /// 每个物资点从 SubSeed(master, KindResource, i) 派生独立 PRNG，
    /// 消费序固定为：数量 →（稀有度 → 桶内下标）× 数量。
    /// </summary>
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

        public ResourcePointGenerator(IReadOnlyList<ResourcePointConfig> resourceConfigs,
            IReadOnlyList<ItemConfig> itemConfigs)
        {
            _resourceConfigs = resourceConfigs;
            _itemConfigs = itemConfigs;
        }

        /// <summary>
        /// 摆位阶段：物资点存在性（本阶段恒为 true）与位置，不消费随机。
        /// </summary>
        public static ResourcePointPlacementData PlacePoint(ResourcePointDefinition definition)
        {
            return new ResourcePointPlacementData(definition.Position, definition.ResourcePointId, true);
        }

        /// <summary>
        /// 完整生成：先取同一摆位结果（位置与存在性的唯一来源），再按本点独立 PRNG 掷出掉落物品。
        /// </summary>
        public ResourcePointBuildData BuildPoint(ResourcePointDefinition definition, int index,
            DifficultyTier difficulty, long masterSeed)
        {
            ResourcePointPlacementData placement = PlacePoint(definition);
            ResourcePointConfig resourceConfig = FindResourceConfig(definition.ResourcePointId);
            LootGenerationConfig lootConfig = FindLootConfig(resourceConfig, difficulty);

            SplitMix64 pointRng = new SplitMix64(
                ExplorationSeed.SubSeed(masterSeed, ExplorationSeed.KindResource, index));
            int itemCount = pointRng.NextInt(lootConfig.MinCount, lootConfig.MaxCount + 1);
            Dictionary<Rarity, List<ItemConfig>> availableItems = BuildItemPools();
            List<int> itemIds = new List<int>(itemCount);
            for (int i = 0; i < itemCount; i++)
            {
                Rarity rarity = SelectRarity(pointRng, lootConfig);
                List<ItemConfig> rarityItems = availableItems[rarity];
                if (rarityItems.Count == 0)
                {
                    throw new InvalidOperationException(
                        $"Resource point '{definition.ResourcePointId}' has no item of rarity '{rarity}' to draw.");
                }

                int itemIndex = pointRng.NextInt(0, rarityItems.Count);
                itemIds.Add(rarityItems[itemIndex].Id);
                rarityItems.RemoveAt(itemIndex);
            }

            return new ResourcePointBuildData(placement.Position, placement.ResourcePointId, itemIds);
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
                if (!pools.ContainsKey(itemConfig.Rarity))
                {
                    throw new InvalidOperationException(
                        $"Item '{itemConfig.Id}' has rarity '{itemConfig.Rarity}' which is not in the loot rarity order.");
                }

                pools[itemConfig.Rarity].Add(itemConfig);
            }

            return pools;
        }

        private static Rarity SelectRarity(SplitMix64 pointRng, LootGenerationConfig config)
        {
            int totalWeight = 0;
            foreach (Rarity rarity in RarityOrder)
            {
                totalWeight += GetRarityWeight(config, rarity);
            }

            if (totalWeight <= 0)
            {
                throw new InvalidOperationException(
                    $"Resource loot rarity weights are all zero for difficulty '{config.Difficulty}'.");
            }

            int value = pointRng.NextInt(0, totalWeight);
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
