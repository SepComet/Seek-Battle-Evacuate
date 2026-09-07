using System;
using SepCore.Definition;
using UnityEngine;

namespace SepCore.Entity
{
    /// <summary>
    /// 场景道具实体数据。
    /// 承载掉落在地图上的具体物品 ID、堆叠数量及稀有度。
    /// </summary>
    [Serializable]
    public sealed class ItemEntityData : EntityDataBase
    {
        [SerializeField] private int _itemId;
        [SerializeField] private int _count;
        [SerializeField] private Rarity _rarity;
        [SerializeField] private Vector3? _spawnFromPosition;

        public ItemEntityData(
            int entityId,
            string assetName,
            Vector3 position,
            int itemId,
            int count,
            Rarity rarity,
            Quaternion? rotation = null,
            Vector3? spawnFromPosition = null) : base(assetName, entityId)
        {
            Position = position;
            Rotation = rotation ?? Quaternion.identity;
            _itemId = itemId;
            _count = Mathf.Max(1, count);
            _rarity = rarity;
            _spawnFromPosition = spawnFromPosition;
        }

        /// <summary>
        /// 物品配置 ID。
        /// </summary>
        public int ItemId => _itemId;

        /// <summary>
        /// 物品数量。
        /// </summary>
        public int Count => _count;

        /// <summary>
        /// 物品稀有度。
        /// </summary>
        public Rarity Rarity => _rarity;

        /// <summary>
        /// 抛物线动画起始点坐标（若为 null 则不播放掉落动画）。
        /// </summary>
        public Vector3? SpawnFromPosition => _spawnFromPosition;
    }
}
