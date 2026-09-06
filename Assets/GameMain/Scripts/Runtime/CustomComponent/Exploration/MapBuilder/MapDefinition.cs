using System;
using System.Collections.Generic;
using SepCore.Definition;
using UnityEngine;

namespace SepCore.Exploration
{
    [Serializable]
    public struct ResourcePointDefinition
    {
        [SerializeField] private Vector2 _position;
        [SerializeField] private int _resourcePointId;

        public Vector2 Position => _position;

        public int ResourcePointId => _resourcePointId;
    }

    [Serializable]
    public struct RoomDefinition
    {
        [SerializeField] private RoomType _roomType;
        [SerializeField] private Vector2 _pointA;
        [SerializeField] private Vector2 _pointB;

        public RoomDefinition(Vector2 pointA, Vector2 pointB, RoomType roomType = RoomType.Normal)
        {
            _roomType = roomType;
            _pointA = pointA;
            _pointB = pointB;
        }

        public RoomType RoomType => _roomType;

        public bool IsCorridor => _roomType == RoomType.Corridor;

        public Vector2 PointA => _pointA;

        public Vector2 PointB => _pointB;

        /// <summary>
        /// 矩形包围盒左下角坐标（X、Y 取较小值）。
        /// </summary>
        public Vector2 Min => new Vector2(Mathf.Min(_pointA.x, _pointB.x), Mathf.Min(_pointA.y, _pointB.y));

        /// <summary>
        /// 矩形包围盒右上角坐标（X、Y 取较大值）。
        /// </summary>
        public Vector2 Max => new Vector2(Mathf.Max(_pointA.x, _pointB.x), Mathf.Max(_pointA.y, _pointB.y));

        /// <summary>
        /// 矩形包围盒宽高。
        /// </summary>
        public Vector2 Size => Max - Min;

        /// <summary>
        /// 矩形中心坐标。
        /// </summary>
        public Vector2 Center => (Min + Max) * 0.5f;

        /// <summary>
        /// 判定指定二维坐标点是否在房间覆盖的瓦片范围内。
        /// 房间覆盖瓦片为闭区间 [floor(Min), ceil(Max)]，瓦片 (x, y) 占据 [x, x+1) x [y, y+1)，
        /// 因此位置判定使用半开区间 [Min.x, Max.x + 1)，与迷雾揭示的瓦片范围保持一致，
        /// 保证从任意方向进入房间都在第一格触发。
        /// </summary>
        public bool Contains(Vector2 point)
        {
            Vector2 min = Min;
            Vector2 max = Max;
            return point.x >= min.x && point.x < max.x + 1f &&
                   point.y >= min.y && point.y < max.y + 1f;
        }

        /// <summary>
        /// 判定指定三维坐标点的 X、Y 平面投影是否在房间覆盖的瓦片范围内。
        /// </summary>
        public bool Contains(Vector3 point)
        {
            return Contains(new Vector2(point.x, point.y));
        }
    }

    [CreateAssetMenu(fileName = "MapDefinition", menuName = "SBE/Map Definition")]
    public sealed class MapDefinition : ScriptableObject
    {
        [SerializeField] private List<Vector2> _playerSpawnPoints = new List<Vector2>();
        [SerializeField] private List<ResourcePointDefinition> _resourcePoints = new List<ResourcePointDefinition>();
        [SerializeField] private List<Vector2> _enemySpawnPoints = new List<Vector2>();
        [SerializeField] private List<Vector2> _extractionPoints = new List<Vector2>();
        [SerializeField] private List<RoomDefinition> _rooms = new List<RoomDefinition>();

        public IReadOnlyList<Vector2> PlayerSpawnPoints => _playerSpawnPoints;

        public IReadOnlyList<ResourcePointDefinition> ResourcePoints => _resourcePoints;

        public IReadOnlyList<Vector2> EnemySpawnPoints => _enemySpawnPoints;

        public IReadOnlyList<Vector2> ExtractionPoints => _extractionPoints;

        public IReadOnlyList<RoomDefinition> Rooms => _rooms;
    }
}
