using System.Collections.Generic;
using System.Collections.ObjectModel;
using SepCore.Definition;
using UnityEngine;

namespace SepCore.Exploration
{
    /// <summary>
    /// 物资点摆位（prediction）视图：存在性与位置，不含物品。
    /// 本阶段物资点恒存在，<see cref="Present"/> 保留给未来的箱子存在性权重。
    /// </summary>
    public sealed class ResourcePointPlacementData
    {
        internal ResourcePointPlacementData(int index, Vector2 position, int resourcePointId, bool present)
        {
            Index = index;
            PointId = PointIds.ResourcePoint(index);
            Position = position;
            ResourcePointId = resourcePointId;
            Present = present;
        }

        /// <summary>该点在物资点定义列表中的下标，用于构造交互主键（契约 §6）。</summary>
        public int Index { get; }

        /// <summary>交互主键 "r&lt;Index&gt;"。</summary>
        public string PointId { get; }

        public Vector2 Position { get; }

        public int ResourcePointId { get; }

        public bool Present { get; }
    }

    /// <summary>
    /// 敌点摆位（prediction）视图：存在性与威胁等级，不含队伍。
    /// </summary>
    public sealed class EnemyPointPlacementData
    {
        internal EnemyPointPlacementData(int index, Vector2 position, EnemyPartyThreatLevel threatLevel, bool present)
        {
            Index = index;
            PointId = PointIds.EnemyPoint(index);
            Position = position;
            ThreatLevel = threatLevel;
            Present = present;
        }

        /// <summary>该点在敌点定义列表中的下标，用于构造交互主键（契约 §6）。</summary>
        public int Index { get; }

        /// <summary>交互主键 "e&lt;Index&gt;"。</summary>
        public string PointId { get; }

        public Vector2 Position { get; }

        public EnemyPartyThreatLevel ThreatLevel { get; }

        public bool Present { get; }
    }

    /// <summary>
    /// 摆位预测结果：与完整生成共用同一套 stage 代码产出的子集。
    /// 客户端联网流程只消费本结果用于即时渲染（出生/撤离点仍用服务器显式值）。
    /// </summary>
    public sealed class MapPlacement
    {
        internal MapPlacement(DifficultyTier difficulty, Vector2 playerSpawnPoint, Vector2 extractionPoint,
            IList<ResourcePointPlacementData> resourcePoints, IList<EnemyPointPlacementData> enemyPoints,
            IEnumerable<RoomDefinition> rooms)
        {
            Difficulty = difficulty;
            PlayerSpawnPoint = playerSpawnPoint;
            ExtractionPoint = extractionPoint;
            ResourcePoints = new ReadOnlyCollection<ResourcePointPlacementData>(
                new List<ResourcePointPlacementData>(resourcePoints));
            EnemyPoints = new ReadOnlyCollection<EnemyPointPlacementData>(
                new List<EnemyPointPlacementData>(enemyPoints));
            Rooms = new ReadOnlyCollection<RoomDefinition>(new List<RoomDefinition>(rooms));
        }

        public DifficultyTier Difficulty { get; }

        public Vector2 PlayerSpawnPoint { get; }

        public Vector2 ExtractionPoint { get; }

        /// <summary>
        /// 与地图定义的物资点列表同序、同长度。
        /// </summary>
        public IReadOnlyList<ResourcePointPlacementData> ResourcePoints { get; }

        /// <summary>
        /// 与地图定义的敌点列表同序、同长度（不存在的点也在列表内，Present 为 false）。
        /// </summary>
        public IReadOnlyList<EnemyPointPlacementData> EnemyPoints { get; }

        public IReadOnlyList<RoomDefinition> Rooms { get; }
    }

    public sealed class ResourcePointBuildData
    {
        internal ResourcePointBuildData(int index, Vector2 position, int resourcePointId, IList<int> itemIds)
        {
            Index = index;
            PointId = PointIds.ResourcePoint(index);
            Position = position;
            ResourcePointId = resourcePointId;
            ItemIds = new ReadOnlyCollection<int>(new List<int>(itemIds));
        }

        /// <summary>
        /// 该点在物资点定义列表中的下标，用于构造交互主键 "r{Index}"（契约 §6）。
        /// </summary>
        public int Index { get; }

        /// <summary>交互主键 "r&lt;Index&gt;"。</summary>
        public string PointId { get; }

        public Vector2 Position { get; }

        public int ResourcePointId { get; }

        public IReadOnlyList<int> ItemIds { get; }
    }

    public sealed class EnemyPointBuildData
    {
        internal EnemyPointBuildData(int index, Vector2 position, EnemyPartyThreatLevel threatLevel,
            int enemyPartyId)
        {
            Index = index;
            PointId = PointIds.EnemyPoint(index);
            Position = position;
            ThreatLevel = threatLevel;
            EnemyPartyId = enemyPartyId;
        }

        /// <summary>
        /// 该敌点在地图定义敌点列表中的下标，用于构造交互主键 "e{Index}"（契约 §6）。
        /// </summary>
        public int Index { get; }

        /// <summary>交互主键 "e&lt;Index&gt;"。</summary>
        public string PointId { get; }

        public Vector2 Position { get; }

        public EnemyPartyThreatLevel ThreatLevel { get; }

        public int EnemyPartyId { get; }
    }

    public sealed class MapBuildResult
    {
        internal MapBuildResult(DifficultyTier difficulty, Vector2 playerSpawnPoint,
            IList<ResourcePointBuildData> resourcePoints, IList<EnemyPointBuildData> enemyPoints,
            Vector2 extractionPoint, IEnumerable<RoomDefinition> rooms)
        {
            Difficulty = difficulty;
            PlayerSpawnPoint = playerSpawnPoint;
            ResourcePoints = new ReadOnlyCollection<ResourcePointBuildData>(
                new List<ResourcePointBuildData>(resourcePoints));
            EnemyPoints = new ReadOnlyCollection<EnemyPointBuildData>(new List<EnemyPointBuildData>(enemyPoints));
            ExtractionPoint = extractionPoint;
            Rooms = new ReadOnlyCollection<RoomDefinition>(new List<RoomDefinition>(rooms));
        }

        public DifficultyTier Difficulty { get; }

        public Vector2 PlayerSpawnPoint { get; }

        public IReadOnlyList<ResourcePointBuildData> ResourcePoints { get; }

        public IReadOnlyList<EnemyPointBuildData> EnemyPoints { get; }

        public Vector2 ExtractionPoint { get; }

        public IReadOnlyList<RoomDefinition> Rooms { get; }
    }
}
