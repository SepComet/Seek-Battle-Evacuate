using System.Collections.Generic;
using NUnit.Framework;
using SepCore.Definition;
using SepCore.Exploration;
using UnityEngine;

namespace SepCore.Tests
{
    /// <summary>
    /// 地图实体装配方案：把「离线完整生成」与「联网摆位预测 + 服务器显式出生/撤离点」归一成同一份
    /// 待实例化清单。这里断言两条来源的关键差异：联网时清单/队伍留空（等交互响应）、
    /// 出生与撤离点取服务器值、不存在的敌点不入列。
    /// </summary>
    [TestFixture]
    public class MapSpawnPlanTests
    {
        [Test]
        public void FromBuildResult_CarriesEverythingFromLocalGeneration()
        {
            List<ResourcePointBuildData> resources = new List<ResourcePointBuildData>
            {
                new ResourcePointBuildData(0, new Vector2(1f, 2f), 11, new[] { 5001, 5003 }),
                new ResourcePointBuildData(1, new Vector2(3f, 4f), 12, new[] { 5002 }),
            };
            List<EnemyPointBuildData> enemies = new List<EnemyPointBuildData>
            {
                new EnemyPointBuildData(1, new Vector2(5f, 6f), EnemyPartyThreatLevel.Middle, 4101),
            };
            MapBuildResult buildResult = new MapBuildResult(DifficultyTier.Tier1,
                new Vector2(3f, -14f), resources, enemies, new Vector2(30f, -3f),
                new[] { new RoomDefinition(new Vector2(-20f, -10f), new Vector2(0f, 10f)) });

            MapSpawnPlan plan = MapSpawnPlan.FromBuildResult(buildResult);

            Assert.AreEqual(new Vector2(3f, -14f), plan.PlayerSpawnTile);
            Assert.AreEqual(new Vector2(30f, -3f), plan.ExtractionTile);
            Assert.AreEqual(1, plan.Rooms.Count);

            Assert.AreEqual(2, plan.ResourcePoints.Count);
            Assert.AreEqual("r0", plan.ResourcePoints[0].PointId);
            Assert.AreEqual("r1", plan.ResourcePoints[1].PointId);
            Assert.AreEqual(11, plan.ResourcePoints[0].ResourcePointId);
            Assert.AreEqual(new Vector2(1f, 2f), plan.ResourcePoints[0].Position);
            Assert.IsTrue(plan.ResourcePoints[0].ContentsKnown);
            CollectionAssert.AreEqual(new[] { 5001, 5003 }, plan.ResourcePoints[0].ItemIds);

            Assert.AreEqual(1, plan.EnemyPoints.Count);
            Assert.AreEqual("e1", plan.EnemyPoints[0].PointId);
            Assert.AreEqual(EnemyPartyThreatLevel.Middle, plan.EnemyPoints[0].ThreatLevel);
            Assert.AreEqual(4101, plan.EnemyPoints[0].EnemyPartyId);
            Assert.IsTrue(plan.EnemyPoints[0].PartyKnown);
        }

        /// <summary>
        /// 联网路径：箱子清单留空（等 loot 响应），出生与撤离点用服务器显式值，
        /// 威胁等级仍来自预测；队伍用同威胁等级的本地占位配置（实体需要它选 prefab 与警惕参数）。
        /// </summary>
        [Test]
        public void FromPlacement_UsesPredictedPositionsAndServerSpawnPoints()
        {
            List<ResourcePointPlacementData> resources = new List<ResourcePointPlacementData>
            {
                new ResourcePointPlacementData(0, new Vector2(1f, 2f), 11, true),
            };
            List<EnemyPointPlacementData> enemies = new List<EnemyPointPlacementData>
            {
                new EnemyPointPlacementData(0, new Vector2(7f, 8f), EnemyPartyThreatLevel.Low, true),
                new EnemyPointPlacementData(1, new Vector2(9f, 10f), EnemyPartyThreatLevel.None, false),
                new EnemyPointPlacementData(2, new Vector2(11f, 12f), EnemyPartyThreatLevel.High, true),
            };
            MapPlacement placement = new MapPlacement(DifficultyTier.Tier2,
                new Vector2(3f, -14f), new Vector2(30f, -3f), resources, enemies,
                new[] { new RoomDefinition(new Vector2(-20f, -10f), new Vector2(0f, 10f)) });

            MapSpawnPlan plan = MapSpawnPlan.FromPlacement(placement,
                new Vector2(1f, -16f), new Vector2(28f, -8f), CreatePartyConfigs());

            // 出生/撤离点取服务器显式值，不用预测值。
            Assert.AreEqual(new Vector2(1f, -16f), plan.PlayerSpawnTile);
            Assert.AreEqual(new Vector2(28f, -8f), plan.ExtractionTile);

            Assert.AreEqual(1, plan.ResourcePoints.Count);
            Assert.IsFalse(plan.ResourcePoints[0].ContentsKnown);
            Assert.AreEqual(0, plan.ResourcePoints[0].ItemIds.Count);
            Assert.AreEqual("r0", plan.ResourcePoints[0].PointId);

            // 不存在的敌点（Threat=None）不入列；保留下来的点位下标不重排（主键仍是定义序）。
            Assert.AreEqual(2, plan.EnemyPoints.Count);
            Assert.AreEqual("e0", plan.EnemyPoints[0].PointId);
            Assert.AreEqual("e2", plan.EnemyPoints[1].PointId);
            Assert.AreEqual(EnemyPartyThreatLevel.High, plan.EnemyPoints[1].ThreatLevel);

            // 占位队伍按配表顺序取该威胁等级的第一支；PartyKnown 为 false 表示构成以服务器为准。
            Assert.AreEqual(4001, plan.EnemyPoints[0].EnemyPartyId);
            Assert.IsFalse(plan.EnemyPoints[0].PartyKnown);
            Assert.AreEqual(4201, plan.EnemyPoints[1].EnemyPartyId);
            Assert.IsFalse(plan.EnemyPoints[1].PartyKnown);
        }

        [Test]
        public void FromPlacement_WithoutPartyForRolledThreat_Throws()
        {
            List<EnemyPointPlacementData> enemies = new List<EnemyPointPlacementData>
            {
                new EnemyPointPlacementData(0, Vector2.zero, EnemyPartyThreatLevel.High, true),
            };
            MapPlacement placement = new MapPlacement(DifficultyTier.Tier1, Vector2.zero, Vector2.zero,
                new List<ResourcePointPlacementData>(), enemies, new List<RoomDefinition>());

            Assert.Throws<System.InvalidOperationException>(() => MapSpawnPlan.FromPlacement(
                placement, Vector2.zero, Vector2.zero, CreatePartyConfigs()));
        }

        [Test]
        public void FromPlacement_SkipsAbsentResourcePoints()
        {
            List<ResourcePointPlacementData> resources = new List<ResourcePointPlacementData>
            {
                new ResourcePointPlacementData(0, new Vector2(1f, 2f), 11, false),
                new ResourcePointPlacementData(1, new Vector2(3f, 4f), 12, true),
            };
            MapPlacement placement = new MapPlacement(DifficultyTier.Tier1,
                Vector2.zero, Vector2.zero, resources, new List<EnemyPointPlacementData>(),
                new List<RoomDefinition>());

            MapSpawnPlan plan = MapSpawnPlan.FromPlacement(placement, Vector2.zero, Vector2.zero,
                CreatePartyConfigs());

            Assert.AreEqual(1, plan.ResourcePoints.Count);
            Assert.AreEqual("r1", plan.ResourcePoints[0].PointId);
        }

        /// <summary>低威胁两支（取配表序第一支）、高威胁一支；无 Middle 队伍。</summary>
        private static List<EnemyPartyConfig> CreatePartyConfigs()
        {
            return new List<EnemyPartyConfig>
            {
                TestConfigFactory.Create<EnemyPartyConfig>(
                    "Id", 4001, "Name", "LowA", "EnemyIds", new List<int> { 3001 },
                    "ThreatConfig", EnemyPartyThreatLevel.Low),
                TestConfigFactory.Create<EnemyPartyConfig>(
                    "Id", 4002, "Name", "LowB", "EnemyIds", new List<int> { 3001 },
                    "ThreatConfig", EnemyPartyThreatLevel.Low),
                TestConfigFactory.Create<EnemyPartyConfig>(
                    "Id", 4201, "Name", "High", "EnemyIds", new List<int> { 3002 },
                    "ThreatConfig", EnemyPartyThreatLevel.High),
            };
        }
    }
}
