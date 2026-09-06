using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using SepCore.CustomComponent;
using SepCore.Definition;
using SepCore.Exploration;
using UnityEngine;

namespace SepCore.Tests
{
    [TestFixture]
    public class MapBuilderTests
    {
        private readonly List<MapDefinition> _definitions = new List<MapDefinition>();

        [TearDown]
        public void TearDown()
        {
            foreach (MapDefinition definition in _definitions)
            {
                UnityEngine.Object.DestroyImmediate(definition);
            }

            _definitions.Clear();
        }

        [Test]
        public void Build_UsesFixedStageOrderAndSkipsPartyRollForNoneThreat()
        {
            MapDefinition definition = CreateMapDefinition(
                new List<Vector2> { new Vector2(1f, 2f), new Vector2(2f, 3f) },
                new List<ResourcePointDefinition> { CreateResourcePoint(new Vector2(3f, 4f), 1) },
                new List<Vector2> { new Vector2(5f, 6f), new Vector2(7f, 8f) },
                new List<Vector2> { new Vector2(9f, 10f) });
            DifficultyConfig difficulty = CreateDifficulty(
                CreateThreatWeight(EnemyPartyThreatLevel.None, 1),
                CreateThreatWeight(EnemyPartyThreatLevel.Low, 1));
            ResourcePointConfig resource = CreateResourceConfig(1, 2, 2, 1, 0, 0, 0, 0);
            List<ItemConfig> items = new List<ItemConfig>
            {
                CreateItem(5001, Rarity.White),
                CreateItem(5002, Rarity.White)
            };
            List<EnemyPartyConfig> parties = new List<EnemyPartyConfig>
            {
                CreateParty(4001, EnemyPartyThreatLevel.Low)
            };
            RecordingRandomSource random = new RecordingRandomSource(2, 0, 1, 0, 0, 0, 1, 0, 0, 1);

            MapBuildResult result = MapBuilder.Build(definition, difficulty,
                new List<ResourcePointConfig> { resource }, items, parties, random);

            CollectionAssert.AreEqual(new[] { 5002, 5001 }, result.ResourcePoints[0].ItemIds);
            Assert.AreEqual(1, result.EnemyPoints.Count);
            Assert.AreEqual(new Vector2(7f, 8f), result.EnemyPoints[0].Position);
            Assert.AreEqual(EnemyPartyThreatLevel.Low, result.EnemyPoints[0].ThreatLevel);
            Assert.AreEqual(4001, result.EnemyPoints[0].EnemyPartyId);
            Assert.AreEqual(new Vector2(9f, 10f), result.ExtractionPoint);
            Assert.AreEqual(new Vector2(2f, 3f), result.PlayerSpawnPoint);
            CollectionAssert.AreEqual(new[]
            {
                new RandomRange(2, 3),
                new RandomRange(0, 1),
                new RandomRange(0, 2),
                new RandomRange(0, 1),
                new RandomRange(0, 1),
                new RandomRange(0, 2),
                new RandomRange(0, 2),
                new RandomRange(0, 1),
                new RandomRange(0, 1),
                new RandomRange(0, 2)
            }, random.Ranges);
        }

        [Test]
        public void Build_AllowsSameItemInDifferentResourcePoints()
        {
            MapDefinition definition = CreateMapDefinition(
                new List<Vector2> { Vector2.zero },
                new List<ResourcePointDefinition>
                {
                    CreateResourcePoint(new Vector2(1f, 1f), 1),
                    CreateResourcePoint(new Vector2(2f, 2f), 1)
                },
                new List<Vector2>(),
                new List<Vector2> { Vector2.zero });
            DifficultyConfig difficulty = CreateDifficulty(
                CreateThreatWeight(EnemyPartyThreatLevel.None, 1));
            ResourcePointConfig resource = CreateResourceConfig(1, 1, 1, 1, 0, 0, 0, 0);
            List<ItemConfig> items = new List<ItemConfig> { CreateItem(5001, Rarity.White) };
            RecordingRandomSource random = new RecordingRandomSource(1, 0, 0, 1, 0, 0, 0, 0);

            MapBuildResult result = MapBuilder.Build(definition, difficulty,
                new List<ResourcePointConfig> { resource }, items, new List<EnemyPartyConfig>(), random);

            CollectionAssert.AreEqual(new[] { 5001 }, result.ResourcePoints[0].ItemIds);
            CollectionAssert.AreEqual(new[] { 5001 }, result.ResourcePoints[1].ItemIds);
            Assert.AreEqual(Vector2.zero, result.PlayerSpawnPoint);
            Assert.AreEqual(8, random.Ranges.Count);
        }

        private MapDefinition CreateMapDefinition(List<Vector2> playerPoints,
            List<ResourcePointDefinition> resourcePoints, List<Vector2> enemyPoints,
            List<Vector2> extractionPoints)
        {
            MapDefinition definition = ScriptableObject.CreateInstance<MapDefinition>();
            _definitions.Add(definition);
            SetField(definition, "_playerSpawnPoints", playerPoints);
            SetField(definition, "_resourcePoints", resourcePoints);
            SetField(definition, "_enemySpawnPoints", enemyPoints);
            SetField(definition, "_extractionPoints", extractionPoints);
            return definition;
        }

        private static ResourcePointDefinition CreateResourcePoint(Vector2 position, int resourcePointId)
        {
            ResourcePointDefinition definition = new ResourcePointDefinition();
            object boxed = definition;
            SetField(boxed, "_position", position);
            SetField(boxed, "_resourcePointId", resourcePointId);
            return (ResourcePointDefinition)boxed;
        }

        private static DifficultyConfig CreateDifficulty(params WeightedEnemyParty[] weights)
        {
            return TestConfigFactory.Create<DifficultyConfig>(
                "Tier", DifficultyTier.Tier1,
                "Name", "Difficulty",
                "EnemyParties", new List<WeightedEnemyParty>(weights));
        }

        private static WeightedEnemyParty CreateThreatWeight(EnemyPartyThreatLevel threat, int weight)
        {
            return TestConfigFactory.Create<WeightedEnemyParty>("Threat", threat, "Weight", weight);
        }

        private static ResourcePointConfig CreateResourceConfig(int id, int minCount, int maxCount,
            int whiteWeight, int greenWeight, int blueWeight, int goldWeight, int redWeight)
        {
            LootGenerationConfig loot = TestConfigFactory.Create<LootGenerationConfig>(
                "Difficulty", DifficultyTier.Tier1,
                "MinCount", minCount,
                "MaxCount", maxCount,
                "WhiteWeight", whiteWeight,
                "GreenWeight", greenWeight,
                "BlueWeight", blueWeight,
                "GoldWeight", goldWeight,
                "RedWeight", redWeight);
            return TestConfigFactory.Create<ResourcePointConfig>(
                "Id", id,
                "Name", "Resource",
                "LootConfigs", new List<LootGenerationConfig> { loot });
        }

        private static ItemConfig CreateItem(int id, Rarity rarity)
        {
            return TestConfigFactory.Create<ItemConfig>("Id", id, "Name", "Item", "Rarity", rarity);
        }

        private static EnemyPartyConfig CreateParty(int id, EnemyPartyThreatLevel threat)
        {
            return TestConfigFactory.Create<EnemyPartyConfig>(
                "Id", id,
                "Name", "Party",
                "EnemyIds", new List<int> { 3001 },
                "ThreatConfig", threat);
        }

        private static void SetField(object target, string fieldName, object value)
        {
            FieldInfo field = target.GetType().GetField(fieldName,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            field.SetValue(target, value);
        }

        private readonly struct RandomRange
        {
            private readonly int _minInclusive;
            private readonly int _maxExclusive;

            public RandomRange(int minInclusive, int maxExclusive)
            {
                _minInclusive = minInclusive;
                _maxExclusive = maxExclusive;
            }

            public override bool Equals(object obj)
            {
                return obj is RandomRange other && _minInclusive == other._minInclusive &&
                       _maxExclusive == other._maxExclusive;
            }

            public override int GetHashCode()
            {
                return (_minInclusive * 397) ^ _maxExclusive;
            }
        }

        private sealed class RecordingRandomSource : IRoundRandomSource
        {
            private readonly Queue<int> _values;

            public RecordingRandomSource(params int[] values)
            {
                _values = new Queue<int>(values);
                Ranges = new List<RandomRange>();
            }

            public List<RandomRange> Ranges { get; }

            public int NextInt(int minInclusive, int maxExclusive)
            {
                Ranges.Add(new RandomRange(minInclusive, maxExclusive));
                int value = _values.Dequeue();
                Assert.That(value, Is.GreaterThanOrEqualTo(minInclusive).And.LessThan(maxExclusive));
                return value;
            }

            public bool RollPermille(int successPermille)
            {
                return NextInt(0, 1000) < successPermille;
            }
        }
    }
}
