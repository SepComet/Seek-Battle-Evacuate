using NUnit.Framework;
using SepCore.Definition;
using SepCore.Exploration;
using UnityEngine;

namespace SepCore.Tests
{
    /// <summary>
    /// 交互主键（契约 §6）与摆位/生成数据的点位下标一致性。
    /// 服务端 mapgen.ResourcePointID / EnemyPointID 用同一格式（"r%d" / "e%d"），
    /// 这里钉住客户端侧格式与"下标即定义列表序"的不变量，避免实体与交互请求对不上。
    /// </summary>
    [TestFixture]
    public class PointIdsTests
    {
        [Test]
        public void ResourcePoint_MatchesContractFormat()
        {
            Assert.AreEqual("r0", PointIds.ResourcePoint(0));
            Assert.AreEqual("r2", PointIds.ResourcePoint(2));
            Assert.AreEqual("r13", PointIds.ResourcePoint(13));
        }

        [Test]
        public void EnemyPoint_MatchesContractFormat()
        {
            Assert.AreEqual("e0", PointIds.EnemyPoint(0));
            Assert.AreEqual("e3", PointIds.EnemyPoint(3));
            Assert.AreEqual("e21", PointIds.EnemyPoint(21));
        }

        /// <summary>
        /// 摆位出口的点位携带与生成器一致的下标与主键：实体从摆位结果取 pointId 时
        /// 必须与服务端按"地图定义列表序"生成的点位 ID 相同。
        /// </summary>
        [Test]
        public void PlacementPointIds_FollowDefinitionOrder()
        {
            ResourcePointPlacementData resource = new ResourcePointPlacementData(4, Vector2.one, 7, true);
            EnemyPointPlacementData enemy = new EnemyPointPlacementData(2, Vector2.zero,
                EnemyPartyThreatLevel.High, true);

            Assert.AreEqual(4, resource.Index);
            Assert.AreEqual("r4", resource.PointId);
            Assert.AreEqual(2, enemy.Index);
            Assert.AreEqual("e2", enemy.PointId);
        }

        /// <summary>
        /// 完整生成结果的点位主键同样来自定义列表下标（离线路径与联网路径共用同一规则）。
        /// </summary>
        [Test]
        public void BuildDataPointIds_FollowDefinitionOrder()
        {
            ResourcePointBuildData resource = new ResourcePointBuildData(3, Vector2.one, 5,
                new[] { 5001 });
            EnemyPointBuildData enemy = new EnemyPointBuildData(6, Vector2.zero,
                EnemyPartyThreatLevel.Low, 4001);

            Assert.AreEqual("r3", resource.PointId);
            Assert.AreEqual("e6", enemy.PointId);
        }
    }
}
