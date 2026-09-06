using System.Collections.Generic;
using System.Collections.ObjectModel;
using SepCore.Definition;
using UnityEngine;

namespace SepCore.Exploration
{
    public sealed class ResourcePointBuildData
    {
        internal ResourcePointBuildData(Vector2 position, int resourcePointId, IList<int> itemIds)
        {
            Position = position;
            ResourcePointId = resourcePointId;
            ItemIds = new ReadOnlyCollection<int>(new List<int>(itemIds));
        }

        public Vector2 Position { get; }

        public int ResourcePointId { get; }

        public IReadOnlyList<int> ItemIds { get; }
    }

    public sealed class EnemyPointBuildData
    {
        internal EnemyPointBuildData(Vector2 position, EnemyPartyThreatLevel threatLevel, int enemyPartyId)
        {
            Position = position;
            ThreatLevel = threatLevel;
            EnemyPartyId = enemyPartyId;
        }

        public Vector2 Position { get; }

        public EnemyPartyThreatLevel ThreatLevel { get; }

        public int EnemyPartyId { get; }
    }

    public sealed class MapBuildResult
    {
        internal MapBuildResult(DifficultyTier difficulty, Vector2 playerSpawnPoint,
            IList<ResourcePointBuildData> resourcePoints, IList<EnemyPointBuildData> enemyPoints,
            Vector2 extractionPoint, IEnumerable<RoomDefinition> rooms = null)
        {
            Difficulty = difficulty;
            PlayerSpawnPoint = playerSpawnPoint;
            ResourcePoints = new ReadOnlyCollection<ResourcePointBuildData>(
                new List<ResourcePointBuildData>(resourcePoints));
            EnemyPoints = new ReadOnlyCollection<EnemyPointBuildData>(new List<EnemyPointBuildData>(enemyPoints));
            ExtractionPoint = extractionPoint;
            Rooms = new ReadOnlyCollection<RoomDefinition>(
                rooms != null ? new List<RoomDefinition>(rooms) : new List<RoomDefinition>());
        }

        public DifficultyTier Difficulty { get; }

        public Vector2 PlayerSpawnPoint { get; }

        public IReadOnlyList<ResourcePointBuildData> ResourcePoints { get; }

        public IReadOnlyList<EnemyPointBuildData> EnemyPoints { get; }

        public Vector2 ExtractionPoint { get; }

        public IReadOnlyList<RoomDefinition> Rooms { get; }
    }
}
