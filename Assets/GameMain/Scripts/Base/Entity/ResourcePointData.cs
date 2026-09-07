using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using UnityEngine;

namespace SepCore.Entity
{
    /// <summary>
    /// 资源点实体数据。
    /// 包含单局开局已确定性生成的物品 ID 序列和资源点类型配置 ID。
    /// </summary>
    [Serializable]
    public sealed class ResourcePointData : EntityDataBase
    {
        [SerializeField] private int _resourcePointId;
        [SerializeField] private List<int> _itemIds;
        [SerializeField] private float _searchProgressTime;
        [SerializeField] private int _droppedItemCount;

        public ResourcePointData(int entityId, string assetName, Vector3 position, int resourcePointId, IEnumerable<int> itemIds,
            Quaternion? rotation = null) : base(assetName, entityId)
        {
            if (itemIds == null)
            {
                throw new ArgumentNullException(nameof(itemIds));
            }

            Position = position;
            Rotation = rotation ?? Quaternion.identity;
            _resourcePointId = resourcePointId;
            _itemIds = new List<int>(itemIds);
            ItemIds = new ReadOnlyCollection<int>(_itemIds);
        }

        /// <summary>
        /// 物资点类型 ID（对应 ResourcePointConfig.Id）
        /// </summary>
        public int ResourcePointId => _resourcePointId;

        /// <summary>
        /// 开局确定性生成的物品 ID 序列（掉落顺序固定）
        /// </summary>
        public IReadOnlyList<int> ItemIds { get; }

        /// <summary>
        /// 包含的物品总数
        /// </summary>
        public int ItemCount => _itemIds.Count;

        /// <summary>
        /// 当前累计搜索时间（秒）。松开按键或被打断时保留。
        /// </summary>
        public float SearchProgressTime
        {
            get => _searchProgressTime;
            set => _searchProgressTime = Mathf.Max(0f, value);
        }

        /// <summary>
        /// 当前已从物资点掉落到地图的道具数量。
        /// </summary>
        public int DroppedItemCount
        {
            get => _droppedItemCount;
            set => _droppedItemCount = Mathf.Clamp(value, 0, _itemIds.Count);
        }

        /// <summary>
        /// 物资点内部物品是否已全部掉落完毕。
        /// 全部掉落完毕后禁止再次交互。
        /// </summary>
        public bool IsCompleted => _itemIds.Count == 0 || _droppedItemCount >= _itemIds.Count;
    }
}
