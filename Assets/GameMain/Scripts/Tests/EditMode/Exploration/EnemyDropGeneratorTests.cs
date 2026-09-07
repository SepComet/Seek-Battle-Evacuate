using System.Collections.Generic;
using NUnit.Framework;
using SepCore.CustomComponent;
using SepCore.Definition;
using SepCore.Exploration;

namespace SepCore.Tests
{
    [TestFixture]
    public class EnemyDropGeneratorTests
    {
        private sealed class MockRandomSource : IRoundRandomSource
        {
            private readonly Queue<int> _values;

            public MockRandomSource(params int[] values)
            {
                _values = new Queue<int>(values);
            }

            public int NextInt(int minInclusive, int maxExclusive)
            {
                if (_values.Count > 0)
                {
                    int val = _values.Dequeue();
                    return val;
                }

                return minInclusive;
            }

            public bool RollPermille(int successPermille)
            {
                return NextInt(0, 1000) < successPermille;
            }
        }

        private static ItemConfig CreateItem(int id, Rarity rarity)
        {
            return TestConfigFactory.Create<ItemConfig>(
                "Id", id,
                "Name", "Item" + id,
                "Rarity", rarity);
        }

        private static LootGenerationConfig CreateLootConfig(DifficultyTier difficulty, int minCount, int maxCount,
            int whiteWeight, int greenWeight, int blueWeight, int goldWeight, int redWeight)
        {
            return TestConfigFactory.Create<LootGenerationConfig>(
                "Difficulty", difficulty,
                "MinCount", minCount,
                "MaxCount", maxCount,
                "WhiteWeight", whiteWeight,
                "GreenWeight", greenWeight,
                "BlueWeight", blueWeight,
                "GoldWeight", goldWeight,
                "RedWeight", redWeight);
        }

        private static EnemyDropConfig CreateDropConfig(EnemyPartyThreatLevel threat, params LootGenerationConfig[] loots)
        {
            return TestConfigFactory.Create<EnemyDropConfig>(
                "Id", threat,
                "LootConfigs", new List<LootGenerationConfig>(loots));
        }

        [Test]
        public void GenerateDrops_NullOrEmptyConfig_ReturnsEmptyList()
        {
            List<ItemConfig> items = new List<ItemConfig> { CreateItem(5001, Rarity.White) };
            MockRandomSource random = new MockRandomSource(0);

            Assert.IsEmpty(EnemyDropGenerator.GenerateDrops(null, DifficultyTier.Tier1, items, random));

            EnemyDropConfig emptyLoots = CreateDropConfig(EnemyPartyThreatLevel.Low);
            Assert.IsEmpty(EnemyDropGenerator.GenerateDrops(emptyLoots, DifficultyTier.Tier1, items, random));

            EnemyDropConfig validDrop = CreateDropConfig(EnemyPartyThreatLevel.Low,
                CreateLootConfig(DifficultyTier.Tier1, 1, 1, 100, 0, 0, 0, 0));
            Assert.IsEmpty(EnemyDropGenerator.GenerateDrops(validDrop, DifficultyTier.Tier1, null, random));
            Assert.IsEmpty(EnemyDropGenerator.GenerateDrops(validDrop, DifficultyTier.Tier1, new List<ItemConfig>(), random));
        }

        [Test]
        public void GenerateDrops_SelectsConfigMatchingDifficulty()
        {
            EnemyDropConfig dropConfig = CreateDropConfig(
                EnemyPartyThreatLevel.Low,
                CreateLootConfig(DifficultyTier.Tier1, 1, 1, 100, 0, 0, 0, 0),
                CreateLootConfig(DifficultyTier.Tier2, 3, 3, 100, 0, 0, 0, 0));

            List<ItemConfig> items = new List<ItemConfig>
            {
                CreateItem(5001, Rarity.White),
                CreateItem(5002, Rarity.White),
                CreateItem(5003, Rarity.White)
            };

            // Tier1: count 1, rarity roll 0, item roll 0
            MockRandomSource randomTier1 = new MockRandomSource(1, 0, 0);
            List<ItemConfig> resultTier1 = EnemyDropGenerator.GenerateDrops(dropConfig, DifficultyTier.Tier1, items, randomTier1);
            Assert.AreEqual(1, resultTier1.Count);

            // Tier2: count 3, rarity rolls 0, 0, 0, item rolls 0, 0, 0
            MockRandomSource randomTier2 = new MockRandomSource(3, 0, 0, 0, 0, 0, 0);
            List<ItemConfig> resultTier2 = EnemyDropGenerator.GenerateDrops(dropConfig, DifficultyTier.Tier2, items, randomTier2);
            Assert.AreEqual(3, resultTier2.Count);
        }

        [Test]
        public void GenerateDrops_WeightedRaritySelection_RollsCorrectRarity()
        {
            // 70% White, 30% Green
            EnemyDropConfig dropConfig = CreateDropConfig(
                EnemyPartyThreatLevel.Middle,
                CreateLootConfig(DifficultyTier.Tier1, 1, 1, 70, 30, 0, 0, 0));

            List<ItemConfig> items = new List<ItemConfig>
            {
                CreateItem(5001, Rarity.White),
                CreateItem(5002, Rarity.Green)
            };

            // Roll 69 (< 70) -> White
            MockRandomSource randomWhite = new MockRandomSource(1, 69, 0);
            List<ItemConfig> dropsWhite = EnemyDropGenerator.GenerateDrops(dropConfig, DifficultyTier.Tier1, items, randomWhite);
            Assert.AreEqual(1, dropsWhite.Count);
            Assert.AreEqual(5001, dropsWhite[0].Id);
            Assert.AreEqual(Rarity.White, dropsWhite[0].Rarity);

            // Roll 70 (>= 70) -> Green
            MockRandomSource randomGreen = new MockRandomSource(1, 70, 0);
            List<ItemConfig> dropsGreen = EnemyDropGenerator.GenerateDrops(dropConfig, DifficultyTier.Tier1, items, randomGreen);
            Assert.AreEqual(1, dropsGreen.Count);
            Assert.AreEqual(5002, dropsGreen[0].Id);
            Assert.AreEqual(Rarity.Green, dropsGreen[0].Rarity);
        }

        [Test]
        public void GenerateDrops_DoesNotDuplicateItemsWhenMultipleAvailable()
        {
            EnemyDropConfig dropConfig = CreateDropConfig(
                EnemyPartyThreatLevel.Low,
                CreateLootConfig(DifficultyTier.Tier1, 2, 2, 100, 0, 0, 0, 0));

            List<ItemConfig> items = new List<ItemConfig>
            {
                CreateItem(5001, Rarity.White),
                CreateItem(5002, Rarity.White)
            };

            // count=2. First item: rarity 0, index 0 (item 5001). Second item: rarity 0, index 0 (item 5002, since 5001 removed).
            MockRandomSource random = new MockRandomSource(2, 0, 0, 0, 0);
            List<ItemConfig> drops = EnemyDropGenerator.GenerateDrops(dropConfig, DifficultyTier.Tier1, items, random);

            Assert.AreEqual(2, drops.Count);
            Assert.AreEqual(5001, drops[0].Id);
            Assert.AreEqual(5002, drops[1].Id);
        }

        [Test]
        public void GenerateDrops_ZeroCount_ReturnsEmptyList()
        {
            EnemyDropConfig dropConfig = CreateDropConfig(
                EnemyPartyThreatLevel.Low,
                CreateLootConfig(DifficultyTier.Tier1, 0, 0, 100, 0, 0, 0, 0));

            List<ItemConfig> items = new List<ItemConfig>
            {
                CreateItem(5001, Rarity.White)
            };

            MockRandomSource random = new MockRandomSource(0);
            List<ItemConfig> drops = EnemyDropGenerator.GenerateDrops(dropConfig, DifficultyTier.Tier1, items, random);
            Assert.IsEmpty(drops);
        }

        [Test]
        public void GenerateDrops_FallbackToAvailableRarityWhenTargetPoolEmpty()
        {
            // 100% Gold weight, but only White items available
            EnemyDropConfig dropConfig = CreateDropConfig(
                EnemyPartyThreatLevel.High,
                CreateLootConfig(DifficultyTier.Tier1, 1, 1, 0, 0, 0, 100, 0));

            List<ItemConfig> items = new List<ItemConfig>
            {
                CreateItem(5001, Rarity.White)
            };

            MockRandomSource random = new MockRandomSource(1, 0, 0);
            List<ItemConfig> drops = EnemyDropGenerator.GenerateDrops(dropConfig, DifficultyTier.Tier1, items, random);

            Assert.AreEqual(1, drops.Count);
            Assert.AreEqual(5001, drops[0].Id);
            Assert.AreEqual(Rarity.White, drops[0].Rarity);
        }
    }
}
