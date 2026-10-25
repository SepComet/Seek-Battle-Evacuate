using System.Collections.Generic;
using System.Collections.ObjectModel;
using SepCore.Definition;
using UnityEngine;

namespace SepCore.Exploration
{
    /// <summary>
    /// 待实例化的物资点。联网流程 <see cref="ContentsKnown"/> 为 false（清单等 loot 响应下发），
    /// 离线完整生成路径为 true。
    /// </summary>
    public sealed class ResourcePointSpawnSpec
    {
        internal ResourcePointSpawnSpec(int index, Vector2 position, int resourcePointId,
            IReadOnlyList<int> itemIds, bool contentsKnown)
        {
            Index = index;
            PointId = PointIds.ResourcePoint(index);
            Position = position;
            ResourcePointId = resourcePointId;
            ItemIds = itemIds;
            ContentsKnown = contentsKnown;
        }

        /// <summary>该点在物资点定义列表中的下标。</summary>
        public int Index { get; }

        /// <summary>交互主键 "r&lt;Index&gt;"。</summary>
        public string PointId { get; }

        /// <summary>瓦片坐标（未加 0.5 中心偏移）。</summary>
        public Vector2 Position { get; }

        public int ResourcePointId { get; }

        /// <summary>开局已知的掉落清单；<see cref="ContentsKnown"/> 为 false 时为空。</summary>
        public IReadOnlyList<int> ItemIds { get; }

        /// <summary>清单是否在开局即确定。</summary>
        public bool ContentsKnown { get; }
    }

    /// <summary>
    /// 待实例化的敌点。<see cref="PartyKnown"/> 为 false 表示队伍构成等 encounter 响应下发；
    /// <see cref="EnemyPartyId"/> 此时是本地按威胁等级选出的占位队伍，只用于 prefab 选取、
    /// 警惕参数与掉落配置，开战一律用服务器响应的队伍。
    /// </summary>
    public sealed class EnemySpawnSpec
    {
        internal EnemySpawnSpec(int index, Vector2 position, EnemyPartyThreatLevel threatLevel, int enemyPartyId,
            bool partyKnown)
        {
            Index = index;
            PointId = PointIds.EnemyPoint(index);
            Position = position;
            ThreatLevel = threatLevel;
            EnemyPartyId = enemyPartyId;
            PartyKnown = partyKnown;
        }

        /// <summary>该点在地图定义敌点列表中的下标。</summary>
        public int Index { get; }

        /// <summary>交互主键 "e&lt;Index&gt;"。</summary>
        public string PointId { get; }

        /// <summary>瓦片坐标（未加 0.5 中心偏移）。</summary>
        public Vector2 Position { get; }

        public EnemyPartyThreatLevel ThreatLevel { get; }

        /// <summary>队伍配置 ID：离线为权威值，联网为同威胁等级的本地占位队伍。</summary>
        public int EnemyPartyId { get; }

        /// <summary>队伍构成是否在开局即确定（离线完整生成）。</summary>
        public bool PartyKnown { get; }
    }

    /// <summary>
    /// 地图实体装配方案：把「离线完整生成」与「联网摆位预测 + 服务器显式出生/撤离点」
    /// 两种来源归一成同一份待实例化清单，流程层的实体实例化只有一条路径。
    /// 只含纯数据，不碰 Unity 对象与实体，可在 EditMode 直接断言。
    /// </summary>
    public sealed class MapSpawnPlan
    {
        private MapSpawnPlan(Vector2 playerSpawnTile, Vector2 extractionTile,
            IList<ResourcePointSpawnSpec> resourcePoints, IList<EnemySpawnSpec> enemyPoints,
            IEnumerable<RoomDefinition> rooms)
        {
            PlayerSpawnTile = playerSpawnTile;
            ExtractionTile = extractionTile;
            ResourcePoints = new ReadOnlyCollection<ResourcePointSpawnSpec>(
                new List<ResourcePointSpawnSpec>(resourcePoints));
            EnemyPoints = new ReadOnlyCollection<EnemySpawnSpec>(new List<EnemySpawnSpec>(enemyPoints));
            Rooms = new ReadOnlyCollection<RoomDefinition>(new List<RoomDefinition>(rooms));
        }

        /// <summary>出生点瓦片坐标（联网用服务器显式值，离线用生成值）。</summary>
        public Vector2 PlayerSpawnTile { get; }

        /// <summary>撤离点瓦片坐标（联网用服务器显式值，离线用生成值）。</summary>
        public Vector2 ExtractionTile { get; }

        public IReadOnlyList<ResourcePointSpawnSpec> ResourcePoints { get; }

        /// <summary>只含本局实际生成的敌点（不存在 Threat=None 的点）。</summary>
        public IReadOnlyList<EnemySpawnSpec> EnemyPoints { get; }

        public IReadOnlyList<RoomDefinition> Rooms { get; }

        /// <summary>
        /// 离线（场景调试）路径：完整生成结果自带全部清单与队伍，出生/撤离点也取生成值。
        /// </summary>
        public static MapSpawnPlan FromBuildResult(MapBuildResult result)
        {
            List<ResourcePointSpawnSpec> resourcePoints =
                new List<ResourcePointSpawnSpec>(result.ResourcePoints.Count);
            for (int i = 0; i < result.ResourcePoints.Count; i++)
            {
                ResourcePointBuildData point = result.ResourcePoints[i];
                resourcePoints.Add(new ResourcePointSpawnSpec(point.Index, point.Position, point.ResourcePointId,
                    point.ItemIds, contentsKnown: true));
            }

            List<EnemySpawnSpec> enemyPoints = new List<EnemySpawnSpec>(result.EnemyPoints.Count);
            for (int i = 0; i < result.EnemyPoints.Count; i++)
            {
                EnemyPointBuildData point = result.EnemyPoints[i];
                enemyPoints.Add(new EnemySpawnSpec(point.Index, point.Position, point.ThreatLevel,
                    point.EnemyPartyId, partyKnown: true));
            }

            return new MapSpawnPlan(result.PlayerSpawnPoint, result.ExtractionPoint, resourcePoints, enemyPoints,
                result.Rooms);
        }

        /// <summary>
        /// 联网路径：摆位用预测结果，出生/撤离点用服务器显式值（瓦片坐标）。
        /// 物资清单留空等 loot 响应；敌队伍用同威胁等级的本地占位配置（实体需要它选 prefab 与警惕参数），
        /// 开战队伍等 encounter 响应。
        /// </summary>
        public static MapSpawnPlan FromPlacement(MapPlacement placement, Vector2 playerSpawnTile,
            Vector2 extractionTile, IReadOnlyList<EnemyPartyConfig> enemyPartyConfigs)
        {
            List<ResourcePointSpawnSpec> resourcePoints =
                new List<ResourcePointSpawnSpec>(placement.ResourcePoints.Count);
            for (int i = 0; i < placement.ResourcePoints.Count; i++)
            {
                ResourcePointPlacementData point = placement.ResourcePoints[i];
                if (!point.Present)
                {
                    continue;
                }

                resourcePoints.Add(new ResourcePointSpawnSpec(point.Index, point.Position, point.ResourcePointId,
                    System.Array.Empty<int>(), contentsKnown: false));
            }

            List<EnemySpawnSpec> enemyPoints = new List<EnemySpawnSpec>(placement.EnemyPoints.Count);
            for (int i = 0; i < placement.EnemyPoints.Count; i++)
            {
                EnemyPointPlacementData point = placement.EnemyPoints[i];
                if (!point.Present)
                {
                    continue;
                }

                int placeholderPartyId = EnemyPartyCatalog.FindPlaceholderPartyId(enemyPartyConfigs,
                    point.ThreatLevel);
                enemyPoints.Add(new EnemySpawnSpec(point.Index, point.Position, point.ThreatLevel,
                    placeholderPartyId, partyKnown: false));
            }

            return new MapSpawnPlan(playerSpawnTile, extractionTile, resourcePoints, enemyPoints, placement.Rooms);
        }
    }
}
