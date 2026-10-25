using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using NUnit.Framework;
using SepCore.Definition;
using SepCore.Exploration;
using UnityEngine;

namespace SepCore.Tests
{
    /// <summary>
    /// 生成器种子向量测试（exploration-online-v1 契约 §11）。
    /// 期望输出文本与 notes/vectors.md 逐字一致；ticket 03 把同一批常量复制到 Go 测试做双端对齐。
    /// 向量输入的完整纯数据（masterSeed、点位坐标、权重、物品与敌队伍表）都写在本文件里，不依赖真实 Luban 表。
    /// </summary>
    [TestFixture]
    public class MapBuilderTests
    {
        private const string VectorA = "A";
        private const string VectorB = "B";
        private const string VectorC = "C";
        private const string VectorD = "D";

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

        public static IEnumerable<TestCaseData> SeedVectorCases()
        {
            yield return new TestCaseData(VectorA);
            yield return new TestCaseData(VectorB);
            yield return new TestCaseData(VectorC);
            yield return new TestCaseData(VectorD);
        }

        [TestCaseSource(nameof(SeedVectorCases))]
        public void Build_MatchesSeedVector(string vectorName)
        {
            VectorInput input = CreateVector(vectorName);
            MapDefinition definition = CreateMapDefinition(input);

            MapBuildResult result = MapBuilder.Build(definition, CreateDifficulty(input),
                CreateResourceConfigs(input), CreateItems(input), CreateParties(input), input.MasterSeed);

            CollectionAssert.AreEqual(input.ExpectedFull, FormatFull(result, input.EnemyPoints.Count));
        }

        [TestCaseSource(nameof(SeedVectorCases))]
        public void BuildPlacement_MatchesFullBuildStages(string vectorName)
        {
            VectorInput input = CreateVector(vectorName);
            MapDefinition definition = CreateMapDefinition(input);

            MapBuildResult full = MapBuilder.Build(definition, CreateDifficulty(input),
                CreateResourceConfigs(input), CreateItems(input), CreateParties(input), input.MasterSeed);
            MapPlacement placement = MapBuilder.BuildPlacement(definition, CreateDifficulty(input), input.MasterSeed);

            List<string> expectedPlacement = ProjectPlacement(FormatFull(full, input.EnemyPoints.Count));
            CollectionAssert.AreEqual(expectedPlacement, FormatPlacement(placement));
            CollectionAssert.AreEqual(input.ExpectedPlacement, FormatPlacement(placement));

            AssertPlacementMatchesBuildStages(full, placement, input.EnemyPoints.Count);
        }

        [TestCaseSource(nameof(SeedVectorCases))]
        public void Build_WithSameMasterSeed_IsBitIdentical(string vectorName)
        {
            VectorInput input = CreateVector(vectorName);
            MapDefinition definition = CreateMapDefinition(input);
            DifficultyConfig difficulty = CreateDifficulty(input);

            MapBuildResult first = MapBuilder.Build(definition, difficulty,
                CreateResourceConfigs(input), CreateItems(input), CreateParties(input), input.MasterSeed);
            MapBuildResult second = MapBuilder.Build(definition, difficulty,
                CreateResourceConfigs(input), CreateItems(input), CreateParties(input), input.MasterSeed);

            CollectionAssert.AreEqual(FormatFull(first, input.EnemyPoints.Count),
                FormatFull(second, input.EnemyPoints.Count));

            // 摆位出口同样必须是纯函数：同一 (masterSeed, 配置) 两次调用逐字一致。
            CollectionAssert.AreEqual(FormatPlacement(MapBuilder.BuildPlacement(definition, difficulty,
                    input.MasterSeed)),
                FormatPlacement(MapBuilder.BuildPlacement(definition, difficulty, input.MasterSeed)));
        }

        [Test]
        public void Build_WithDifferentMasterSeed_ProducesDifferentMap()
        {
            VectorInput input = CreateVector(VectorA);
            MapDefinition definition = CreateMapDefinition(input);

            MapBuildResult baseline = MapBuilder.Build(definition, CreateDifficulty(input),
                CreateResourceConfigs(input), CreateItems(input), CreateParties(input), input.MasterSeed);
            // +3 是参考脚本验证过的偏移：产出与基线逐字不同且不会耗尽稀有度桶。
            MapBuildResult other = MapBuilder.Build(definition, CreateDifficulty(input),
                CreateResourceConfigs(input), CreateItems(input), CreateParties(input), input.MasterSeed + 3);

            CollectionAssert.AreNotEqual(FormatFull(baseline, input.EnemyPoints.Count),
                FormatFull(other, input.EnemyPoints.Count));
        }

        [Test]
        public void Build_ChangingOnlyResourceConfig_LeavesEnemyPlacementUntouched()
        {
            VectorInput baseline = CreateVector(VectorA);
            VectorInput changed = CreateVector(VectorD);
            Assert.AreEqual(baseline.MasterSeed, changed.MasterSeed);
            Assert.AreEqual(baseline.EnemyPoints.Count, changed.EnemyPoints.Count);

            MapBuildResult baselineResult = BuildFromVector(baseline);
            MapBuildResult changedResult = BuildFromVector(changed);

            List<string> baselineResources = ResourceLines(FormatFull(baselineResult, baseline.EnemyPoints.Count));
            List<string> changedResources = ResourceLines(FormatFull(changedResult, changed.EnemyPoints.Count));
            CollectionAssert.AreNotEqual(baselineResources, changedResources);

            List<string> baselineEnemies = EnemyLines(FormatFull(baselineResult, baseline.EnemyPoints.Count));
            List<string> changedEnemies = EnemyLines(FormatFull(changedResult, changed.EnemyPoints.Count));
            CollectionAssert.AreEqual(baselineEnemies, changedEnemies);
        }

        /// <summary>
        /// 子种子隔离也必须体现在摆位出口上（契约 §2：每条目的 PRNG 只服务该条目）：
        /// 只改物资配置时，摆位的 E 行与 spawn/extraction 逐字不变。
        /// </summary>
        [Test]
        public void BuildPlacement_ChangingOnlyResourceConfig_LeavesEnemyAndSpawnUntouched()
        {
            VectorInput baseline = CreateVector(VectorA);
            VectorInput changed = CreateVector(VectorD);
            MapDefinition baselineDefinition = CreateMapDefinition(baseline);
            MapDefinition changedDefinition = CreateMapDefinition(changed);

            List<string> baselineLines = FormatPlacement(MapBuilder.BuildPlacement(
                baselineDefinition, CreateDifficulty(baseline), baseline.MasterSeed));
            List<string> changedLines = FormatPlacement(MapBuilder.BuildPlacement(
                changedDefinition, CreateDifficulty(changed), changed.MasterSeed));

            // 物资配置不参与摆位掷点（喂料量只影响 items），所以摆位文本理应完全相同。
            CollectionAssert.AreEqual(baselineLines, changedLines);
        }

        [Test]
        public void Build_AllowsSameItemInDifferentResourcePoints()
        {
            VectorInput input = CreateVector(VectorA);
            MapBuildResult result = BuildFromVector(input);

            // R0 与 R1 各自从独立子种子与独立物品桶起步，因此都能抽到 5004：
            // 同一点内不重复，跨点允许重复。
            CollectionAssert.AreEqual(new[] { 5004, 5002, 5003 }, result.ResourcePoints[0].ItemIds);
            CollectionAssert.AreEqual(new[] { 5004, 5005 }, result.ResourcePoints[1].ItemIds);
        }

        [Test]
        public void Build_ThrowsWhenResourceConfigIsMissing()
        {
            VectorInput input = CreateVector(VectorA);
            MapDefinition definition = CreateMapDefinition(input);

            Assert.Throws<System.InvalidOperationException>(() => MapBuilder.Build(definition,
                CreateDifficulty(input), new List<ResourcePointConfig>(), CreateItems(input),
                CreateParties(input), input.MasterSeed));
        }

        [Test]
        public void Build_ThrowsWhenRarityWeightsAreAllZero()
        {
            VectorInput input = CreateVector(VectorA);
            input.LootConfigs[0].WhiteWeight = 0;
            input.LootConfigs[0].GreenWeight = 0;
            input.LootConfigs[0].BlueWeight = 0;
            MapDefinition definition = CreateMapDefinition(input);

            Assert.Throws<System.InvalidOperationException>(() => MapBuilder.Build(definition,
                CreateDifficulty(input), CreateResourceConfigs(input), CreateItems(input),
                CreateParties(input), input.MasterSeed));
        }

        [Test]
        public void Build_ThrowsWhenThreatWeightsAreAllZero()
        {
            VectorInput input = CreateVector(VectorA);
            foreach (ThreatWeightSpec weight in input.ThreatWeights)
            {
                weight.Weight = 0;
            }

            MapDefinition definition = CreateMapDefinition(input);

            Assert.Throws<System.InvalidOperationException>(() => MapBuilder.Build(definition,
                CreateDifficulty(input), CreateResourceConfigs(input), CreateItems(input),
                CreateParties(input), input.MasterSeed));
        }

        [Test]
        public void Build_ThrowsWhenPartyPoolIsEmptyForRolledThreat()
        {
            VectorInput input = CreateVector(VectorA);
            input.Parties.Clear();
            MapDefinition definition = CreateMapDefinition(input);

            Assert.Throws<System.InvalidOperationException>(() => MapBuilder.Build(definition,
                CreateDifficulty(input), CreateResourceConfigs(input), CreateItems(input),
                CreateParties(input), input.MasterSeed));
        }

        [Test]
        public void Build_ThrowsWhenSpawnCandidatesAreEmpty()
        {
            VectorInput input = CreateVector(VectorA);
            input.SpawnPoints.Clear();
            MapDefinition definition = CreateMapDefinition(input);

            Assert.Throws<System.InvalidOperationException>(() => MapBuilder.Build(definition,
                CreateDifficulty(input), CreateResourceConfigs(input), CreateItems(input),
                CreateParties(input), input.MasterSeed));
        }

        [Test]
        public void Build_ThrowsWhenExtractionCandidatesAreEmpty()
        {
            VectorInput input = CreateVector(VectorA);
            input.ExtractionPoints.Clear();
            MapDefinition definition = CreateMapDefinition(input);

            Assert.Throws<System.InvalidOperationException>(() => MapBuilder.Build(definition,
                CreateDifficulty(input), CreateResourceConfigs(input), CreateItems(input),
                CreateParties(input), input.MasterSeed));
        }

        [Test]
        public void RoomDefinition_Contains_CorrectlyIdentifiesPoints()
        {
            RoomDefinition room1 = new RoomDefinition(new Vector2(-10f, -5f), new Vector2(10f, 15f));
            RoomDefinition room2 = new RoomDefinition(new Vector2(10f, -5f), new Vector2(-10f, 15f));

            Assert.AreEqual(new Vector2(-10f, -5f), room1.Min);
            Assert.AreEqual(new Vector2(10f, 15f), room1.Max);
            Assert.AreEqual(new Vector2(20f, 20f), room1.Size);
            Assert.AreEqual(new Vector2(0f, 5f), room1.Center);

            Assert.AreEqual(room1.Min, room2.Min);
            Assert.AreEqual(room1.Max, room2.Max);

            // Inside
            Assert.IsTrue(room1.Contains(new Vector2(0f, 0f)));
            Assert.IsTrue(room1.Contains(new Vector3(5f, 5f, 0f)));

            // 房间覆盖瓦片 [Min, Max] 闭区间，瓦片占据 [x, x+1)，
            // 因此连续足迹为 [Min, Max+1)：Min 侧含边界，Max 侧边界瓦片整体算房间内
            Assert.IsTrue(room1.Contains(new Vector2(-10f, 0f)));
            Assert.IsTrue(room1.Contains(new Vector2(0f, -5f)));
            Assert.IsTrue(room1.Contains(new Vector2(10f, 15f)));
            Assert.IsTrue(room1.Contains(new Vector2(10.5f, 0f)));
            Assert.IsTrue(room1.Contains(new Vector2(0f, 15.5f)));

            // Outside（Max+1 起为半开区间上界之外）
            Assert.IsFalse(room1.Contains(new Vector2(-10.1f, 0f)));
            Assert.IsFalse(room1.Contains(new Vector2(0f, -5.1f)));
            Assert.IsFalse(room1.Contains(new Vector2(11f, 0f)));
            Assert.IsFalse(room1.Contains(new Vector2(0f, 16f)));
        }

        [Test]
        public void RoomDefinition_WithRoomType_SetsTypeAndIsCorridor()
        {
            RoomDefinition normal = new RoomDefinition(Vector2.zero, Vector2.one);
            RoomDefinition corridor = new RoomDefinition(Vector2.zero, Vector2.one, RoomType.Corridor);

            Assert.AreEqual(RoomType.Normal, normal.RoomType);
            Assert.IsFalse(normal.IsCorridor);
            Assert.AreEqual(RoomType.Corridor, corridor.RoomType);
            Assert.IsTrue(corridor.IsCorridor);
        }

        [Test]
        public void Build_PassesRoomsToMapBuildResult()
        {
            List<RoomDefinition> rooms = new List<RoomDefinition>
            {
                new RoomDefinition(new Vector2(-20f, -10f), new Vector2(0f, 10f)),
                new RoomDefinition(new Vector2(5f, 5f), new Vector2(25f, 25f))
            };

            MapDefinition definition = CreateMapDefinition(
                new List<Vector2> { Vector2.zero },
                new List<ResourcePointDefinition>(),
                new List<Vector2>(),
                new List<Vector2> { Vector2.zero },
                rooms);
            DifficultyConfig difficulty = CreateDifficulty(new List<ThreatWeightSpec>
            {
                new ThreatWeightSpec(EnemyPartyThreatLevel.None, 1)
            });

            MapBuildResult result = MapBuilder.Build(definition, difficulty,
                new List<ResourcePointConfig>(), new List<ItemConfig>(), new List<EnemyPartyConfig>(), 1L);

            Assert.AreEqual(2, result.Rooms.Count);
            Assert.AreEqual(new Vector2(-20f, -10f), result.Rooms[0].Min);
            Assert.AreEqual(new Vector2(0f, 10f), result.Rooms[0].Max);
            Assert.AreEqual(new Vector2(5f, 5f), result.Rooms[1].Min);
            Assert.AreEqual(new Vector2(25f, 25f), result.Rooms[1].Max);
        }

        [Test]
        public void BuildPlacement_PassesRoomsToPlacement()
        {
            List<RoomDefinition> rooms = new List<RoomDefinition>
            {
                new RoomDefinition(new Vector2(-20f, -10f), new Vector2(0f, 10f))
            };

            MapDefinition definition = CreateMapDefinition(
                new List<Vector2> { Vector2.zero },
                new List<ResourcePointDefinition>(),
                new List<Vector2>(),
                new List<Vector2> { Vector2.zero },
                rooms);
            DifficultyConfig difficulty = CreateDifficulty(new List<ThreatWeightSpec>
            {
                new ThreatWeightSpec(EnemyPartyThreatLevel.None, 1)
            });

            MapPlacement placement = MapBuilder.BuildPlacement(definition, difficulty, 1L);

            Assert.AreEqual(1, placement.Rooms.Count);
            Assert.AreEqual(new Vector2(-20f, -10f), placement.Rooms[0].Min);
            Assert.AreEqual(Vector2.zero, placement.PlayerSpawnPoint);
            Assert.AreEqual(Vector2.zero, placement.ExtractionPoint);
        }

        private MapBuildResult BuildFromVector(VectorInput input)
        {
            MapDefinition definition = CreateMapDefinition(input);
            return MapBuilder.Build(definition, CreateDifficulty(input), CreateResourceConfigs(input),
                CreateItems(input), CreateParties(input), input.MasterSeed);
        }

        // ------------------------------------------------------------ 向量数据

        private static VectorInput CreateVector(string vectorName)
        {
            switch (vectorName)
            {
                case VectorA:
                    return CreateVectorA();
                case VectorB:
                    return CreateVectorB();
                case VectorC:
                    return CreateVectorC();
                case VectorD:
                    return CreateVectorD();
                default:
                    throw new System.ArgumentOutOfRangeException(nameof(vectorName), vectorName, null);
            }
        }

        /// <summary>
        /// 向量 A：masterSeed=123456789，三物资点 + 四敌点，威胁权重含 None。
        /// </summary>
        private static VectorInput CreateVectorA()
        {
            return new VectorInput
            {
                MasterSeed = 123456789L,
                SpawnPoints = new List<Vector2>
                {
                    new Vector2(1f, -16f), new Vector2(5f, -16f), new Vector2(9f, -20f)
                },
                ExtractionPoints = new List<Vector2> { new Vector2(30f, -3f), new Vector2(28f, -8f) },
                ResourcePoints = new List<ResourcePointSpec>
                {
                    new ResourcePointSpec(new Vector2(-4f, 5f), 1),
                    new ResourcePointSpec(new Vector2(2f, 7f), 2),
                    new ResourcePointSpec(new Vector2(6f, -3f), 1)
                },
                EnemyPoints = new List<Vector2>
                {
                    new Vector2(-19f, 1f), new Vector2(-14f, 4f), new Vector2(3f, 9f), new Vector2(12f, -2f)
                },
                ThreatWeights = new List<ThreatWeightSpec>
                {
                    new ThreatWeightSpec(EnemyPartyThreatLevel.None, 3),
                    new ThreatWeightSpec(EnemyPartyThreatLevel.Low, 4),
                    new ThreatWeightSpec(EnemyPartyThreatLevel.Middle, 2),
                    new ThreatWeightSpec(EnemyPartyThreatLevel.High, 1)
                },
                LootConfigs = new List<LootSpec>
                {
                    new LootSpec(1, 1, 3, 60, 30, 10, 0, 0),
                    new LootSpec(2, 2, 2, 0, 0, 50, 50, 0)
                },
                Items = new List<ItemSpec>
                {
                    new ItemSpec(5001, Rarity.White),
                    new ItemSpec(5002, Rarity.White),
                    new ItemSpec(5003, Rarity.Green),
                    new ItemSpec(5004, Rarity.Blue),
                    new ItemSpec(5005, Rarity.Gold),
                    new ItemSpec(5006, Rarity.Red)
                },
                Parties = new List<PartySpec>
                {
                    new PartySpec(4001, EnemyPartyThreatLevel.Low),
                    new PartySpec(4002, EnemyPartyThreatLevel.Low),
                    new PartySpec(4101, EnemyPartyThreatLevel.Middle),
                    new PartySpec(4201, EnemyPartyThreatLevel.High)
                },
                ExpectedFull = new List<string>
                {
                    "spawn=1,-16",
                    "extraction=30,-3",
                    "R0 present=true items=5004,5002,5003",
                    "R1 present=true items=5004,5005",
                    "R2 present=true items=5001",
                    "E0 present=true threat=1 party=4001",
                    "E1 present=true threat=3 party=4201",
                    "E2 present=true threat=1 party=4001",
                    "E3 present=true threat=3 party=4201"
                },
                ExpectedPlacement = new List<string>
                {
                    "spawn=1,-16",
                    "extraction=30,-3",
                    "R0 present=true",
                    "R1 present=true",
                    "R2 present=true",
                    "E0 present=true threat=1",
                    "E1 present=true threat=3",
                    "E2 present=true threat=1",
                    "E3 present=true threat=3"
                }
            };
        }

        /// <summary>
        /// 向量 B：masterSeed=42，含多点 threat=None 与多稀有度掉落。
        /// </summary>
        private static VectorInput CreateVectorB()
        {
            return new VectorInput
            {
                MasterSeed = 42L,
                SpawnPoints = new List<Vector2> { new Vector2(1f, -16f), new Vector2(5f, -16f) },
                ExtractionPoints = new List<Vector2>
                {
                    new Vector2(30f, -3f), new Vector2(28f, -8f), new Vector2(26f, -12f)
                },
                ResourcePoints = new List<ResourcePointSpec>
                {
                    new ResourcePointSpec(new Vector2(10f, 2f), 3),
                    new ResourcePointSpec(new Vector2(-7f, -4f), 4)
                },
                EnemyPoints = new List<Vector2>
                {
                    new Vector2(-19f, 1f), new Vector2(-14f, 4f), new Vector2(3f, 9f), new Vector2(12f, -2f),
                    new Vector2(7f, 7f)
                },
                ThreatWeights = new List<ThreatWeightSpec>
                {
                    new ThreatWeightSpec(EnemyPartyThreatLevel.None, 3),
                    new ThreatWeightSpec(EnemyPartyThreatLevel.Low, 3),
                    new ThreatWeightSpec(EnemyPartyThreatLevel.Middle, 4)
                },
                LootConfigs = new List<LootSpec>
                {
                    new LootSpec(3, 1, 4, 20, 20, 20, 20, 20),
                    new LootSpec(4, 3, 3, 0, 0, 0, 0, 100)
                },
                Items = new List<ItemSpec>
                {
                    new ItemSpec(5001, Rarity.White),
                    new ItemSpec(5002, Rarity.Green),
                    new ItemSpec(5003, Rarity.Blue),
                    new ItemSpec(5004, Rarity.Gold),
                    new ItemSpec(5005, Rarity.Red),
                    new ItemSpec(5006, Rarity.Red),
                    new ItemSpec(5007, Rarity.Red)
                },
                Parties = new List<PartySpec>
                {
                    new PartySpec(4001, EnemyPartyThreatLevel.Low),
                    new PartySpec(4101, EnemyPartyThreatLevel.Middle),
                    new PartySpec(4201, EnemyPartyThreatLevel.Middle),
                    new PartySpec(4301, EnemyPartyThreatLevel.High)
                },
                ExpectedFull = new List<string>
                {
                    "spawn=5,-16",
                    "extraction=30,-3",
                    "R0 present=true items=5002",
                    "R1 present=true items=5006,5007,5005",
                    "E0 present=true threat=1 party=4001",
                    "E1 present=false",
                    "E2 present=true threat=2 party=4201",
                    "E3 present=true threat=1 party=4001",
                    "E4 present=false"
                },
                ExpectedPlacement = new List<string>
                {
                    "spawn=5,-16",
                    "extraction=30,-3",
                    "R0 present=true",
                    "R1 present=true",
                    "E0 present=true threat=1",
                    "E1 present=false",
                    "E2 present=true threat=2",
                    "E3 present=true threat=1",
                    "E4 present=false"
                }
            };
        }

        /// <summary>
        /// 向量 C：masterSeed=2147483647（契约 §5 取值上界），单候选出生/撤离。
        /// </summary>
        private static VectorInput CreateVectorC()
        {
            return new VectorInput
            {
                MasterSeed = 2147483647L,
                SpawnPoints = new List<Vector2> { new Vector2(3f, -14f) },
                ExtractionPoints = new List<Vector2> { new Vector2(30f, -3f) },
                ResourcePoints = new List<ResourcePointSpec>
                {
                    new ResourcePointSpec(new Vector2(0f, 0f), 5)
                },
                EnemyPoints = new List<Vector2>
                {
                    new Vector2(-19f, 1f), new Vector2(-14f, 4f), new Vector2(3f, 9f)
                },
                ThreatWeights = new List<ThreatWeightSpec>
                {
                    new ThreatWeightSpec(EnemyPartyThreatLevel.None, 1),
                    new ThreatWeightSpec(EnemyPartyThreatLevel.Low, 1),
                    new ThreatWeightSpec(EnemyPartyThreatLevel.High, 2)
                },
                LootConfigs = new List<LootSpec>
                {
                    new LootSpec(5, 2, 2, 100, 0, 0, 0, 0)
                },
                Items = new List<ItemSpec>
                {
                    new ItemSpec(5101, Rarity.White),
                    new ItemSpec(5102, Rarity.White),
                    new ItemSpec(5103, Rarity.White)
                },
                Parties = new List<PartySpec>
                {
                    new PartySpec(4001, EnemyPartyThreatLevel.Low),
                    new PartySpec(4201, EnemyPartyThreatLevel.High),
                    new PartySpec(4301, EnemyPartyThreatLevel.High)
                },
                ExpectedFull = new List<string>
                {
                    "spawn=3,-14",
                    "extraction=30,-3",
                    "R0 present=true items=5101,5102",
                    "E0 present=false",
                    "E1 present=true threat=1 party=4001",
                    "E2 present=true threat=3 party=4201"
                },
                ExpectedPlacement = new List<string>
                {
                    "spawn=3,-14",
                    "extraction=30,-3",
                    "R0 present=true",
                    "E0 present=false",
                    "E1 present=true threat=1",
                    "E2 present=true threat=3"
                }
            };
        }

        /// <summary>
        /// 向量 D：与 A 只差物资配置（权重与数量），用于子种子隔离断言。
        /// </summary>
        private static VectorInput CreateVectorD()
        {
            VectorInput input = CreateVectorA();
            input.LootConfigs = new List<LootSpec>
            {
                new LootSpec(1, 1, 2, 100, 0, 0, 0, 0),
                new LootSpec(2, 1, 1, 0, 100, 0, 0, 0)
            };
            input.ExpectedFull = new List<string>
            {
                "spawn=1,-16",
                "extraction=30,-3",
                "R0 present=true items=5001,5002",
                "R1 present=true items=5003",
                "R2 present=true items=5001,5002",
                "E0 present=true threat=1 party=4001",
                "E1 present=true threat=3 party=4201",
                "E2 present=true threat=1 party=4001",
                "E3 present=true threat=3 party=4201"
            };
            input.ExpectedPlacement = new List<string>
            {
                "spawn=1,-16",
                "extraction=30,-3",
                "R0 present=true",
                "R1 present=true",
                "R2 present=true",
                "E0 present=true threat=1",
                "E1 present=true threat=3",
                "E2 present=true threat=1",
                "E3 present=true threat=3"
            };
            return input;
        }

        /// <summary>
        /// 摆位与完整生成的结构级一致性：位置逐位相同、敌点列表同序同长（不存在的点也在摆位列内）。
        /// 文本比对之外再钉一次字段，防止两条 stage 路径在文本看不出差异的地方漂移。
        /// </summary>
        private static void AssertPlacementMatchesBuildStages(MapBuildResult full, MapPlacement placement,
            int enemyDefinitionCount)
        {
            Assert.AreEqual(full.Difficulty, placement.Difficulty);
            Assert.AreEqual(full.PlayerSpawnPoint, placement.PlayerSpawnPoint);
            Assert.AreEqual(full.ExtractionPoint, placement.ExtractionPoint);
            Assert.AreEqual(full.ResourcePoints.Count, placement.ResourcePoints.Count);
            for (int i = 0; i < full.ResourcePoints.Count; i++)
            {
                Assert.AreEqual(full.ResourcePoints[i].Position, placement.ResourcePoints[i].Position);
                Assert.AreEqual(full.ResourcePoints[i].ResourcePointId,
                    placement.ResourcePoints[i].ResourcePointId);
                Assert.IsTrue(placement.ResourcePoints[i].Present);

                // 交互主键（契约 §6）：两条来源必须给出同一个 "r<i>"，否则实体发出的 loot 点不到服务器记录的点。
                Assert.AreEqual(i, full.ResourcePoints[i].Index);
                Assert.AreEqual(PointIds.ResourcePoint(i), full.ResourcePoints[i].PointId);
                Assert.AreEqual(full.ResourcePoints[i].PointId, placement.ResourcePoints[i].PointId);
            }

            Assert.AreEqual(enemyDefinitionCount, placement.EnemyPoints.Count);
            Dictionary<int, EnemyPointBuildData> presentEnemies = new Dictionary<int, EnemyPointBuildData>();
            foreach (EnemyPointBuildData enemyPoint in full.EnemyPoints)
            {
                presentEnemies.Add(enemyPoint.Index, enemyPoint);
            }

            for (int i = 0; i < placement.EnemyPoints.Count; i++)
            {
                EnemyPointPlacementData placed = placement.EnemyPoints[i];
                EnemyPointBuildData built;
                if (!presentEnemies.TryGetValue(i, out built))
                {
                    Assert.IsFalse(placed.Present, "Enemy point " + FormatInt(i) + " should be absent.");
                    continue;
                }

                Assert.IsTrue(placed.Present, "Enemy point " + FormatInt(i) + " should be present.");
                Assert.AreEqual(built.Position, placed.Position);
                Assert.AreEqual(built.ThreatLevel, placed.ThreatLevel);

                // 交互主键（契约 §6）：完整生成与摆位出口必须给出同一个 "e<i>"。
                Assert.AreEqual(built.Index, placed.Index);
                Assert.AreEqual(PointIds.EnemyPoint(i), placed.PointId);
                Assert.AreEqual(built.PointId, placed.PointId);
            }
        }

        // ------------------------------------------------------------ 文本格式化

        private static List<string> FormatFull(MapBuildResult result, int enemyDefinitionCount)
        {
            List<string> lines = new List<string>
            {
                "spawn=" + FormatPoint(result.PlayerSpawnPoint),
                "extraction=" + FormatPoint(result.ExtractionPoint)
            };

            for (int i = 0; i < result.ResourcePoints.Count; i++)
            {
                ResourcePointBuildData point = result.ResourcePoints[i];
                lines.Add("R" + FormatInt(i) + " present=true items=" + FormatItemIds(point.ItemIds));
            }

            Dictionary<int, EnemyPointBuildData> enemyByIndex = new Dictionary<int, EnemyPointBuildData>();
            foreach (EnemyPointBuildData enemyPoint in result.EnemyPoints)
            {
                enemyByIndex.Add(enemyPoint.Index, enemyPoint);
            }

            for (int i = 0; i < enemyDefinitionCount; i++)
            {
                EnemyPointBuildData enemyPoint;
                if (!enemyByIndex.TryGetValue(i, out enemyPoint))
                {
                    lines.Add("E" + FormatInt(i) + " present=false");
                    continue;
                }

                lines.Add("E" + FormatInt(i) + " present=true threat=" + FormatInt((int)enemyPoint.ThreatLevel) +
                          " party=" + FormatInt(enemyPoint.EnemyPartyId));
            }

            return lines;
        }

        /// <summary>
        /// 摆位文本：敌点行数直接取摆位列表长度（契约 §4 规定与地图定义同序同长，
        /// 该不变量由 <see cref="AssertPlacementMatchesBuildStages"/> 另行断言）。
        /// </summary>
        private static List<string> FormatPlacement(MapPlacement placement)
        {
            List<string> lines = new List<string>
            {
                "spawn=" + FormatPoint(placement.PlayerSpawnPoint),
                "extraction=" + FormatPoint(placement.ExtractionPoint)
            };

            for (int i = 0; i < placement.ResourcePoints.Count; i++)
            {
                lines.Add("R" + FormatInt(i) + " present=" + FormatBool(placement.ResourcePoints[i].Present));
            }

            for (int i = 0; i < placement.EnemyPoints.Count; i++)
            {
                EnemyPointPlacementData enemyPoint = placement.EnemyPoints[i];
                lines.Add(enemyPoint.Present
                    ? "E" + FormatInt(i) + " present=true threat=" + FormatInt((int)enemyPoint.ThreatLevel)
                    : "E" + FormatInt(i) + " present=false");
            }

            return lines;
        }

        /// <summary>
        /// 把完整生成的文本投影成摆位文本：截掉 items / party 两段，其余逐字保留。
        /// </summary>
        private static List<string> ProjectPlacement(List<string> fullLines)
        {
            List<string> lines = new List<string>(fullLines.Count);
            foreach (string line in fullLines)
            {
                int itemsIndex = line.IndexOf(" items=", System.StringComparison.Ordinal);
                if (itemsIndex >= 0)
                {
                    lines.Add(line.Substring(0, itemsIndex));
                    continue;
                }

                int partyIndex = line.IndexOf(" party=", System.StringComparison.Ordinal);
                lines.Add(partyIndex >= 0 ? line.Substring(0, partyIndex) : line);
            }

            return lines;
        }

        private static List<string> ResourceLines(List<string> lines)
        {
            List<string> result = new List<string>();
            foreach (string line in lines)
            {
                if (line.StartsWith("R", System.StringComparison.Ordinal))
                {
                    result.Add(line);
                }
            }

            return result;
        }

        private static List<string> EnemyLines(List<string> lines)
        {
            List<string> result = new List<string>();
            foreach (string line in lines)
            {
                if (line.StartsWith("E", System.StringComparison.Ordinal))
                {
                    result.Add(line);
                }
            }

            return result;
        }

        private static string FormatPoint(Vector2 point)
        {
            return point.x.ToString("R", CultureInfo.InvariantCulture) + "," +
                   point.y.ToString("R", CultureInfo.InvariantCulture);
        }

        private static string FormatItemIds(IReadOnlyList<int> itemIds)
        {
            System.Text.StringBuilder builder = new System.Text.StringBuilder();
            for (int i = 0; i < itemIds.Count; i++)
            {
                if (i > 0)
                {
                    builder.Append(',');
                }

                builder.Append(FormatInt(itemIds[i]));
            }

            return builder.ToString();
        }

        private static string FormatInt(int value)
        {
            return value.ToString(CultureInfo.InvariantCulture);
        }

        private static string FormatBool(bool value)
        {
            return value ? "true" : "false";
        }

        // ------------------------------------------------------------ 配置构造

        private static DifficultyConfig CreateDifficulty(VectorInput input)
        {
            List<WeightedEnemyParty> weights = new List<WeightedEnemyParty>(input.ThreatWeights.Count);
            foreach (ThreatWeightSpec spec in input.ThreatWeights)
            {
                weights.Add(TestConfigFactory.Create<WeightedEnemyParty>(
                    "Threat", spec.Threat, "Weight", spec.Weight));
            }

            return TestConfigFactory.Create<DifficultyConfig>(
                "Tier", DifficultyTier.Tier1,
                "Name", "Difficulty",
                "EnemyParties", weights);
        }

        private static DifficultyConfig CreateDifficulty(List<ThreatWeightSpec> threatWeights)
        {
            return CreateDifficulty(new VectorInput { ThreatWeights = threatWeights });
        }

        private static List<ResourcePointConfig> CreateResourceConfigs(VectorInput input)
        {
            List<int> resourceIds = new List<int>();
            foreach (LootSpec spec in input.LootConfigs)
            {
                if (!resourceIds.Contains(spec.ResourcePointId))
                {
                    resourceIds.Add(spec.ResourcePointId);
                }
            }

            List<ResourcePointConfig> configs = new List<ResourcePointConfig>(resourceIds.Count);
            foreach (int resourceId in resourceIds)
            {
                List<LootGenerationConfig> lootConfigs = new List<LootGenerationConfig>();
                foreach (LootSpec spec in input.LootConfigs)
                {
                    if (spec.ResourcePointId != resourceId)
                    {
                        continue;
                    }

                    lootConfigs.Add(TestConfigFactory.Create<LootGenerationConfig>(
                        "Difficulty", DifficultyTier.Tier1,
                        "MinCount", spec.MinCount,
                        "MaxCount", spec.MaxCount,
                        "WhiteWeight", spec.WhiteWeight,
                        "GreenWeight", spec.GreenWeight,
                        "BlueWeight", spec.BlueWeight,
                        "GoldWeight", spec.GoldWeight,
                        "RedWeight", spec.RedWeight));
                }

                configs.Add(TestConfigFactory.Create<ResourcePointConfig>(
                    "Id", resourceId,
                    "Name", "Resource",
                    "LootConfigs", lootConfigs));
            }

            return configs;
        }

        private static List<ItemConfig> CreateItems(VectorInput input)
        {
            List<ItemConfig> items = new List<ItemConfig>(input.Items.Count);
            foreach (ItemSpec spec in input.Items)
            {
                items.Add(TestConfigFactory.Create<ItemConfig>(
                    "Id", spec.Id, "Name", "Item", "Rarity", spec.Rarity));
            }

            return items;
        }

        private static List<EnemyPartyConfig> CreateParties(VectorInput input)
        {
            List<EnemyPartyConfig> parties = new List<EnemyPartyConfig>(input.Parties.Count);
            foreach (PartySpec spec in input.Parties)
            {
                parties.Add(TestConfigFactory.Create<EnemyPartyConfig>(
                    "Id", spec.Id,
                    "Name", "Party",
                    "EnemyIds", new List<int> { 3001 },
                    "ThreatConfig", spec.Threat));
            }

            return parties;
        }

        private MapDefinition CreateMapDefinition(VectorInput input)
        {
            List<ResourcePointDefinition> resourcePoints = new List<ResourcePointDefinition>(input.ResourcePoints.Count);
            foreach (ResourcePointSpec spec in input.ResourcePoints)
            {
                resourcePoints.Add(CreateResourcePoint(spec.Position, spec.ResourcePointId));
            }

            return CreateMapDefinition(input.SpawnPoints, resourcePoints, input.EnemyPoints,
                input.ExtractionPoints);
        }

        private MapDefinition CreateMapDefinition(List<Vector2> playerPoints,
            List<ResourcePointDefinition> resourcePoints, List<Vector2> enemyPoints,
            List<Vector2> extractionPoints, List<RoomDefinition> rooms = null)
        {
            MapDefinition definition = ScriptableObject.CreateInstance<MapDefinition>();
            _definitions.Add(definition);
            SetField(definition, "_playerSpawnPoints", playerPoints);
            SetField(definition, "_resourcePoints", resourcePoints);
            SetField(definition, "_enemySpawnPoints", enemyPoints);
            SetField(definition, "_extractionPoints", extractionPoints);
            SetField(definition, "_rooms", rooms ?? new List<RoomDefinition>());
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

        private static void SetField(object target, string fieldName, object value)
        {
            FieldInfo field = target.GetType().GetField(fieldName,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            field.SetValue(target, value);
        }

        // ------------------------------------------------------------ 纯数据向量

        private sealed class VectorInput
        {
            public long MasterSeed;
            public List<Vector2> SpawnPoints;
            public List<Vector2> ExtractionPoints;
            public List<ResourcePointSpec> ResourcePoints;
            public List<Vector2> EnemyPoints;
            public List<ThreatWeightSpec> ThreatWeights;
            public List<LootSpec> LootConfigs;
            public List<ItemSpec> Items;
            public List<PartySpec> Parties;
            public List<string> ExpectedFull;
            public List<string> ExpectedPlacement;
        }

        private sealed class ResourcePointSpec
        {
            public ResourcePointSpec(Vector2 position, int resourcePointId)
            {
                Position = position;
                ResourcePointId = resourcePointId;
            }

            public Vector2 Position { get; }

            public int ResourcePointId { get; }
        }

        private sealed class ThreatWeightSpec
        {
            public ThreatWeightSpec(EnemyPartyThreatLevel threat, int weight)
            {
                Threat = threat;
                Weight = weight;
            }

            public EnemyPartyThreatLevel Threat { get; }

            public int Weight { get; set; }
        }

        private sealed class LootSpec
        {
            public LootSpec(int resourcePointId, int minCount, int maxCount, int whiteWeight, int greenWeight,
                int blueWeight, int goldWeight, int redWeight)
            {
                ResourcePointId = resourcePointId;
                MinCount = minCount;
                MaxCount = maxCount;
                WhiteWeight = whiteWeight;
                GreenWeight = greenWeight;
                BlueWeight = blueWeight;
                GoldWeight = goldWeight;
                RedWeight = redWeight;
            }

            public int ResourcePointId { get; }

            public int MinCount { get; }

            public int MaxCount { get; }

            public int WhiteWeight { get; set; }

            public int GreenWeight { get; set; }

            public int BlueWeight { get; set; }

            public int GoldWeight { get; set; }

            public int RedWeight { get; set; }
        }

        private sealed class ItemSpec
        {
            public ItemSpec(int id, Rarity rarity)
            {
                Id = id;
                Rarity = rarity;
            }

            public int Id { get; }

            public Rarity Rarity { get; }
        }

        private sealed class PartySpec
        {
            public PartySpec(int id, EnemyPartyThreatLevel threat)
            {
                Id = id;
                Threat = threat;
            }

            public int Id { get; }

            public EnemyPartyThreatLevel Threat { get; }
        }
    }
}
