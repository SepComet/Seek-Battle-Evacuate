using System;
using System.Collections.Generic;
using SepCore.CustomComponent;
using SepCore.Definition;
using UnityEngine;

namespace SepCore.Exploration
{
    /// <summary>
    /// 敌人掉落纯逻辑生成器。
    /// 根据敌人掉落配置、当前单局难度、物品全表与随机源，确定性生成掉落物品列表。
    /// </summary>
    public static class EnemyDropGenerator
    {
        private static readonly Rarity[] RarityOrder =
        {
            Rarity.White,
            Rarity.Green,
            Rarity.Blue,
            Rarity.Gold,
            Rarity.Red
        };

        /// <summary>
        /// 生成敌人掉落物品列表。
        /// </summary>
        /// <param name="dropConfig">敌人掉落配置（包含各难度对应的 LootGenerationConfig 列表）。</param>
        /// <param name="difficulty">当前单局探索难度。</param>
        /// <param name="allItems">物品全表列表。</param>
        /// <param name="random">本局共享随机源。</param>
        /// <returns>待掉落的物品配置列表。</returns>
        public static List<ItemConfig> GenerateDrops(
            EnemyDropConfig dropConfig,
            DifficultyTier difficulty,
            IReadOnlyList<ItemConfig> allItems,
            IRoundRandomSource random)
        {
            if (dropConfig == null || dropConfig.LootConfigs == null || dropConfig.LootConfigs.Count == 0)
            {
                return new List<ItemConfig>();
            }

            if (allItems == null || allItems.Count == 0)
            {
                return new List<ItemConfig>();
            }

            LootGenerationConfig lootConfig = FindLootConfig(dropConfig, difficulty);
            if (lootConfig == null)
            {
                return new List<ItemConfig>();
            }

            int minCount = Mathf.Max(0, lootConfig.MinCount);
            int maxCount = Mathf.Max(minCount, lootConfig.MaxCount);
            int itemCount = random != null
                ? random.NextInt(minCount, maxCount + 1)
                : UnityEngine.Random.Range(minCount, maxCount + 1);

            if (itemCount <= 0)
            {
                return new List<ItemConfig>();
            }

            Dictionary<Rarity, List<ItemConfig>> availableItems = BuildItemPools(allItems);
            List<ItemConfig> results = new List<ItemConfig>(itemCount);

            for (int i = 0; i < itemCount; i++)
            {
                Rarity rarity = SelectRarity(lootConfig, random);
                List<ItemConfig> rarityItems = GetAvailablePool(availableItems, rarity);
                if (rarityItems == null || rarityItems.Count == 0)
                {
                    break;
                }

                int itemIndex = random != null
                    ? random.NextInt(0, rarityItems.Count)
                    : UnityEngine.Random.Range(0, rarityItems.Count);

                results.Add(rarityItems[itemIndex]);

                // 若该稀有度池有多件物品，移除已选物品避免单次掉落出现重复；若仅剩 1 件则保留供后续抽取
                if (rarityItems.Count > 1)
                {
                    rarityItems.RemoveAt(itemIndex);
                }
            }

            return results;
        }

        private static LootGenerationConfig FindLootConfig(EnemyDropConfig dropConfig, DifficultyTier difficulty)
        {
            for (int i = 0; i < dropConfig.LootConfigs.Count; i++)
            {
                if (dropConfig.LootConfigs[i].Difficulty == difficulty)
                {
                    return dropConfig.LootConfigs[i];
                }
            }

            // 若找不到完全匹配的难度，返回第一个作为兜底
            return dropConfig.LootConfigs.Count > 0 ? dropConfig.LootConfigs[0] : null;
        }

        private static Dictionary<Rarity, List<ItemConfig>> BuildItemPools(IReadOnlyList<ItemConfig> allItems)
        {
            Dictionary<Rarity, List<ItemConfig>> pools = new Dictionary<Rarity, List<ItemConfig>>();
            for (int i = 0; i < RarityOrder.Length; i++)
            {
                pools.Add(RarityOrder[i], new List<ItemConfig>());
            }

            for (int i = 0; i < allItems.Count; i++)
            {
                ItemConfig itemConfig = allItems[i];
                if (itemConfig != null && pools.TryGetValue(itemConfig.Rarity, out List<ItemConfig> list))
                {
                    list.Add(itemConfig);
                }
            }

            return pools;
        }

        private static List<ItemConfig> GetAvailablePool(Dictionary<Rarity, List<ItemConfig>> availableItems, Rarity targetRarity)
        {
            if (availableItems.TryGetValue(targetRarity, out List<ItemConfig> list) && list.Count > 0)
            {
                return list;
            }

            // 降级：从所有非空稀有度池中找一个
            for (int i = 0; i < RarityOrder.Length; i++)
            {
                if (availableItems.TryGetValue(RarityOrder[i], out List<ItemConfig> fallback) && fallback.Count > 0)
                {
                    return fallback;
                }
            }

            return null;
        }

        private static Rarity SelectRarity(LootGenerationConfig config, IRoundRandomSource random)
        {
            int totalWeight = 0;
            for (int i = 0; i < RarityOrder.Length; i++)
            {
                totalWeight += GetRarityWeight(config, RarityOrder[i]);
            }

            if (totalWeight <= 0)
            {
                return Rarity.White;
            }

            int value = random != null
                ? random.NextInt(0, totalWeight)
                : UnityEngine.Random.Range(0, totalWeight);

            for (int i = 0; i < RarityOrder.Length; i++)
            {
                int weight = GetRarityWeight(config, RarityOrder[i]);
                if (value < weight)
                {
                    return RarityOrder[i];
                }

                value -= weight;
            }

            return Rarity.White;
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
                    return 0;
            }
        }
    }
}
