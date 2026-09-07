using System;
using SepCore.Definition;
using UnityEngine;

namespace SepCore.Entity
{
    /// <summary>
    /// 敌人队伍实体数据。
    /// 包含敌人队伍配置 ID、威胁等级以及所属房间索引与矩形边界。
    /// </summary>
    [Serializable]
    public sealed class EnemyPartyData : EntityDataBase
    {
        [SerializeField] private int _enemyPartyId;
        [SerializeField] private EnemyPartyThreatLevel _threatLevel;
        [SerializeField] private int _roomIndex;
        [SerializeField] private Vector2 _roomMin;
        [SerializeField] private Vector2 _roomMax;

        public EnemyPartyData(
            int entityId,
            string assetName,
            Vector3 position,
            int enemyPartyId,
            EnemyPartyThreatLevel threatLevel,
            int roomIndex = -1,
            Vector2 roomMin = default,
            Vector2 roomMax = default,
            Quaternion? rotation = null)
            : base(assetName, entityId)
        {
            Position = position;
            Rotation = rotation ?? Quaternion.identity;
            _enemyPartyId = enemyPartyId;
            _threatLevel = threatLevel;
            _roomIndex = roomIndex;
            _roomMin = roomMin;
            _roomMax = roomMax;
        }

        /// <summary>
        /// 敌人队伍配置 ID（对应 EnemyPartyConfig.Id）
        /// </summary>
        public int EnemyPartyId => _enemyPartyId;

        /// <summary>
        /// 敌人队伍威胁等级
        /// </summary>
        public EnemyPartyThreatLevel ThreatLevel => _threatLevel;

        /// <summary>
        /// 所属房间索引。未分配或走廊时为 -1。
        /// </summary>
        public int RoomIndex => _roomIndex;

        /// <summary>
        /// 所属房间包围盒左下角。
        /// </summary>
        public Vector2 RoomMin => _roomMin;

        /// <summary>
        /// 所属房间包围盒右上角。
        /// </summary>
        public Vector2 RoomMax => _roomMax;

        /// <summary>
        /// 判定指定坐标是否在敌人所属房间内。
        /// </summary>
        public bool IsInRoom(Vector2 point)
        {
            return _roomIndex >= 0 &&
                   point.x >= _roomMin.x && point.x <= _roomMax.x &&
                   point.y >= _roomMin.y && point.y <= _roomMax.y;
        }
    }
}
