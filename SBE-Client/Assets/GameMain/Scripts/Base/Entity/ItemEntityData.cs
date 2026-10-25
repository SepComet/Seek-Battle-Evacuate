using System;
using SepCore.Definition;
using UnityEngine;

namespace SepCore.Entity
{
    /// <summary>
    /// 场景道具实体数据。
    /// 承载掉落在地图上的具体物品 ID、堆叠数量及稀有度，
    /// 并标注是否来自敌人战斗掉落（结算据此上报 battleLoot 供服务器开箱对账）。
    /// </summary>
    [Serializable]
    public sealed class ItemEntityData : EntityDataBase
    {
        [SerializeField] private int _itemId;
        [SerializeField] private int _count;
        [SerializeField] private Rarity _rarity;
        [SerializeField] private Vector3? _spawnFromPosition;
        [SerializeField] private bool _isBattleLoot;

        public ItemEntityData(
            int entityId,
            string assetName,
            Vector3 position,
            int itemId,
            int count,
            Rarity rarity,
            Quaternion? rotation = null,
            Vector3? spawnFromPosition = null,
            bool isBattleLoot = false) : base(assetName, entityId)
        {
            Position = position;
            Rotation = rotation ?? Quaternion.identity;
            _itemId = itemId;
            _count = Mathf.Max(1, count);
            _rarity = rarity;
            _spawnFromPosition = spawnFromPosition;
            _isBattleLoot = isBattleLoot;
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

        /// <summary>
        /// 是否来自敌人战斗掉落。拾取时记入本局战斗所得，结算上报后计入服务器对账额度。
        /// </summary>
        public bool IsBattleLoot => _isBattleLoot;
    }
}
