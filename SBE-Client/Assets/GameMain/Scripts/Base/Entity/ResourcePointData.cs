using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using UnityEngine;

namespace SepCore.Entity
{
    /// <summary>
    /// 资源点实体数据。
    /// 包含资源点类型配置 ID、交互主键与掉落顺序固定的物品 ID 序列。
    /// 联网流程开局不带物品清单（<see cref="ContentsReady"/> 为 false），
    /// loot 响应到达后经 <see cref="ApplyItems"/> 一次性灌入，再按稀有度阈值逐件掉落。
    /// </summary>
    [Serializable]
    public sealed class ResourcePointData : EntityDataBase
    {
        [SerializeField] private int _resourcePointId;
        [SerializeField] private string _pointId;
        [SerializeField] private List<int> _itemIds;
        [SerializeField] private bool _contentsReady;
        [SerializeField] private float _searchProgressTime;
        [SerializeField] private int _droppedItemCount;

        /// <summary>
        /// 构造开局即持有完整掉落清单的资源点（离线完整生成路径）。
        /// </summary>
        public ResourcePointData(int entityId, string assetName, Vector3 position, int resourcePointId,
            IEnumerable<int> itemIds, Quaternion? rotation = null, string pointId = null)
            : this(entityId, assetName, position, resourcePointId, itemIds, rotation, pointId, contentsReady: true)
        {
        }

        private ResourcePointData(int entityId, string assetName, Vector3 position, int resourcePointId,
            IEnumerable<int> itemIds, Quaternion? rotation, string pointId, bool contentsReady)
            : base(assetName, entityId)
        {
            if (itemIds == null)
            {
                throw new ArgumentNullException(nameof(itemIds));
            }

            Position = position;
            Rotation = rotation ?? Quaternion.identity;
            _resourcePointId = resourcePointId;
            _pointId = pointId;
            _itemIds = new List<int>(itemIds);
            _contentsReady = contentsReady;
            ItemIds = new ReadOnlyCollection<int>(_itemIds);
        }

        /// <summary>
        /// 构造内容待定的资源点（联网流程：箱子清单等 loot 响应下发）。
        /// 内容未就绪时仍可交互，交互开始即发起 loot 请求。
        /// </summary>
        public static ResourcePointData CreatePendingContents(int entityId, string assetName, Vector3 position,
            int resourcePointId, string pointId, Quaternion? rotation = null)
        {
            return new ResourcePointData(entityId, assetName, position, resourcePointId, Array.Empty<int>(),
                rotation, pointId, contentsReady: false);
        }

        /// <summary>
        /// 物资点类型 ID（对应 ResourcePointConfig.Id）
        /// </summary>
        public int ResourcePointId => _resourcePointId;

        /// <summary>
        /// 交互主键 "r&lt;下标&gt;"（契约 §6），loot 请求以它为准；离线数据同样携带。
        /// </summary>
        public string PointId => _pointId;

        /// <summary>
        /// 物品清单是否已就绪（联网流程在 loot 响应到达后置为 true）。
        /// 未就绪时 <see cref="IsCompleted"/> 恒为 false，保证箱子仍可交互以发起请求。
        /// </summary>
        public bool ContentsReady => _contentsReady;

        /// <summary>
        /// 掉落顺序固定的物品 ID 序列；内容未就绪时为空。
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
        /// 物资点内部物品是否已全部掉落完毕。全部掉落完毕后禁止再次交互。
        /// 内容未就绪（等待 loot 响应）时不算完成。
        /// </summary>
        public bool IsCompleted => _contentsReady && _droppedItemCount >= _itemIds.Count;

        /// <summary>
        /// 灌入服务器下发的掉落清单并重置搜索状态。仅允许在内容未就绪时调用一次，
        /// 重复调用视为状态错乱直接抛错（不做静默覆盖）。
        /// </summary>
        public void ApplyItems(IEnumerable<int> itemIds)
        {
            if (itemIds == null)
            {
                throw new ArgumentNullException(nameof(itemIds));
            }

            if (_contentsReady)
            {
                throw new InvalidOperationException("Resource point items are already applied.");
            }

            _itemIds.Clear();
            _itemIds.AddRange(itemIds);
            _searchProgressTime = 0f;
            _droppedItemCount = 0;
            _contentsReady = true;
        }
    }
}
