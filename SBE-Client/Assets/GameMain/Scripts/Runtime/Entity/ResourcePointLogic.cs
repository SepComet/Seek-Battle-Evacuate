using System;
using Cysharp.Threading.Tasks;
using SepCore.CustomComponent;
using SepCore.Definition;
using SepCore.Exploration;
using SepCore.Net;
using UnityEngine;
using UnityEngine.UI;
using UnityGameFramework.Runtime;

namespace SepCore.Entity
{
    /// <summary>
    /// 资源点实体逻辑。
    /// 实现 IHoldInteractable 接口，支持被领队探测器长按交互、按稀有度累计耗时逐件向外掉落道具。
    /// 联网流程开局不带物品清单：长按开始即发起 loot 请求，响应到达后才推进搜索进度并逐件掉落；
    /// 请求失败（超时/断线/拒绝）结束本次交互、恢复移动并提示，箱子保持可再次交互。
    /// </summary>
    public sealed class ResourcePointLogic : EntityBase, IHoldInteractable
    {
        private const float DefaultItemSearchTimeSeconds = 0.2f;
        private const float DefaultLootRangeMinRadius = 0.3f;
        private const float DefaultLootRangeMaxRadius = 1.5f;
        private const int MaxDropPositionAttempts = 15;

        private ResourcePointData _data;
        private ResourcePointConfig _config;
        private SpriteRenderer _spriteRenderer;
        private Material _originalMaterial;
        private MaterialPropertyBlock _propertyBlock;
        private Slider _slider;
        private float[] _itemDropThresholds;
        private float _totalSearchTime;
        private PlayerCharacterController _interactingController;
        private bool _lootRequestInFlight;
        private bool _searching;

        /// <summary>
        /// 当前绑定的资源点实体数据。
        /// </summary>
        public ResourcePointData Data => _data;

        /// <summary>
        /// 当前资源点对应的 Luban 静态配置。
        /// </summary>
        public ResourcePointConfig Config => _config;

        public InteractableType InteractableType => InteractableType.ResourcePoint;

        public Rarity Rarity => Rarity.None;

        public Vector3 Position => transform.position;

        public bool CanInteract => _data != null && !_data.IsCompleted;

        public GameObject EntityGameObject => gameObject;

        /// <summary>
        /// 物资点总搜索耗时（秒）。
        /// </summary>
        public float TotalSearchTime => _totalSearchTime;

        public void OnInteract(GameObject interactor)
        {
            Log.Info("Interacting with ResourcePoint '{0}' (ConfigId: {1}, PointId: {2}).",
                Entity.Id, _data != null ? _data.ResourcePointId : 0, _data != null ? _data.PointId : null);
        }

        public void OnInteractStart(GameObject interactor)
        {
            if (!CanInteract)
            {
                return;
            }

            // 内容未到货（联网流程）：长按开始只发起 loot 请求，不锁定移动、不推进进度；
            // 请求在后台进行，本帧立即返回。到货后玩家继续按着即由 OnInteractHold 进入搜索。
            if (!_data.ContentsReady)
            {
                if (!_lootRequestInFlight)
                {
                    RequestLootAsync(_data).Forget();
                }

                return;
            }

            BeginSearching(interactor);
        }

        public void OnInteractHold(GameObject interactor, float deltaTime)
        {
            if (!CanInteract || _data == null || !_data.ContentsReady || _lootRequestInFlight)
            {
                return;
            }

            if (!_searching)
            {
                // 联网流程：清单在长按期间才到货，此时补上搜索起点（锁定移动、显示进度）。
                BeginSearching(interactor);
            }

            _data.SearchProgressTime = Mathf.Min(_totalSearchTime, _data.SearchProgressTime + Mathf.Max(0f, deltaTime));

            if (_itemDropThresholds != null)
            {
                while (_data.DroppedItemCount < _itemDropThresholds.Length &&
                       _data.SearchProgressTime >= _itemDropThresholds[_data.DroppedItemCount])
                {
                    DropItem(_data.DroppedItemCount);
                    _data.DroppedItemCount++;
                }
            }

            UpdateSlider();

            if (_data.IsCompleted)
            {
                OnInteractEnd(interactor);
                SetHighlight(false);
            }
        }

        public void OnInteractEnd(GameObject interactor)
        {
            ReleaseInteractingController();

            if (_slider != null)
            {
                _slider.gameObject.SetActive(false);
            }
        }

        public void SetHighlight(bool highlight)
        {
            if (!CanInteract && highlight)
            {
                return;
            }

            if (_spriteRenderer == null)
            {
                _spriteRenderer = GetComponentInChildren<SpriteRenderer>();
            }

            Exploration.SpriteOutlineApplier.ApplyOutline(
                _spriteRenderer,
                ref _originalMaterial,
                ref _propertyBlock,
                highlight,
                new Color(1f, 0.9f, 0.2f, 1f));
        }

        protected override void OnShow(object userData)
        {
            base.OnShow(userData);

            _spriteRenderer = GetComponentInChildren<SpriteRenderer>();
            _originalMaterial = null;
            _data = userData as ResourcePointData;
            if (_data == null)
            {
                Log.Error("Resource point entity data is invalid.");
                return;
            }

            _config = GameEntry.Luban.Get<ResourcePointConfig>(_data.ResourcePointId);
            if (_config == null)
            {
                Log.Error("Resource point config '{0}' is invalid.", _data.ResourcePointId);
            }

            _slider = GetComponentInChildren<Slider>(true);

            InitializeDropThresholds();

            if (_slider != null)
            {
                _slider.minValue = 0f;
                _slider.maxValue = 1f;
                _slider.value = _totalSearchTime > 0f ? Mathf.Clamp01(_data.SearchProgressTime / _totalSearchTime) : 0f;
                _slider.gameObject.SetActive(false);
            }
        }

        protected override void OnHide(bool isShutdown, object userData)
        {
            ReleaseInteractingController();
            SetHighlight(false);

            if (_slider != null)
            {
                _slider.gameObject.SetActive(false);
            }

            _data = null;
            _config = null;
            _spriteRenderer = null;
            _originalMaterial = null;
            _propertyBlock = null;
            _slider = null;
            _itemDropThresholds = null;
            _totalSearchTime = 0f;
            _lootRequestInFlight = false;
            _searching = false;

            base.OnHide(isShutdown, userData);
        }

        /// <summary>
        /// 向服务器请求本箱清单。请求在后台进行，本方法返回后主循环不受影响；
        /// 成功则灌入清单、重算搜索阈值并显示进度，失败则结束交互、恢复移动并提示。
        /// </summary>
        private async UniTaskVoid RequestLootAsync(ResourcePointData data)
        {
            if (string.IsNullOrEmpty(data.PointId))
            {
                // 联网流程的箱子必须携带交互主键（契约 §6），缺失即是装配缺陷，中止本次交互。
                Log.Error("Resource point '{0}' has no pointId; cannot request loot.", data.ResourcePointId);
                OnInteractEnd(null);
                return;
            }

            if (GameEntry.Net == null || !GameEntry.Net.OnlineReady)
            {
                // 联网物品清单只可能出现在联网流程里；走到这里说明状态错乱，直接中止本次交互。
                Log.Error("Resource point '{0}' has no contents and the network is not ready.", data.PointId);
                OnInteractEnd(null);
                NetworkNotice.ShowInteractionRejected();
                return;
            }

            _lootRequestInFlight = true;
            try
            {
                LootResult result = await GameEntry.Net.Run.LootAsync(data.PointId);
                if (result.Success)
                {
                    // 实体可能已被隐藏并池化复用给另一个物资点：只有仍是同一个数据实例才灌入，
                    // 否则会把 A 箱的清单写进 B 箱。
                    if (!Available || !ReferenceEquals(_data, data))
                    {
                        return;
                    }

                    data.ApplyItems(result.ItemIds);
                    InitializeDropThresholds();

                    // 只刷新进度值，不主动显示进度条：玩家在等待期间已松手时不应弹条，
                    // 继续按着则由下一次 OnInteractHold 的 BeginSearching 显示。
                    if (_slider != null)
                    {
                        _slider.maxValue = 1f;
                        UpdateSlider();
                    }

                    Log.Info("Resource point '{0}' contents received: {1} items.", data.PointId, data.ItemCount);
                    return;
                }

                Log.Warning("Resource point '{0}' loot request failed: {1}.", data.PointId, result.Outcome);
                if (Available && ReferenceEquals(_data, data))
                {
                    OnInteractEnd(null);
                }

                NetworkNotice.ShowInteractionRejected();
            }
            finally
            {
                if (ReferenceEquals(_data, data))
                {
                    _lootRequestInFlight = false;
                }
            }
        }

        /// <summary>
        /// 进入搜索流程：锁定交互者移动并显示搜索进度。
        /// 离线路径在长按开始时直接进入；联网路径在 loot 响应到达后由 OnInteractHold 补进入。
        /// </summary>
        private void BeginSearching(GameObject interactor)
        {
            if (interactor != null)
            {
                _interactingController = interactor.GetComponent<PlayerCharacterController>()
                    ?? interactor.GetComponentInParent<PlayerCharacterController>();
            }

            if (_interactingController != null)
            {
                _interactingController.CanMove = false;
            }

            _searching = true;

            if (_slider != null)
            {
                _slider.gameObject.SetActive(true);
                UpdateSlider();
            }
        }

        private void InitializeDropThresholds()
        {
            int itemCount = _data != null ? _data.ItemCount : 0;
            _itemDropThresholds = new float[itemCount];
            float accumulated = 0f;

            for (int i = 0; i < itemCount; i++)
            {
                int itemId = _data.ItemIds[i];
                ItemConfig itemConfig = GameEntry.Luban.Get<ItemConfig>(itemId);
                float duration = DefaultItemSearchTimeSeconds;

                if (itemConfig != null)
                {
                    RarityConfig rarityConfig = GameEntry.Luban.Get<RarityConfig>((int)itemConfig.Rarity);
                    if (rarityConfig != null && rarityConfig.SearchTimeMs > 0)
                    {
                        duration = rarityConfig.SearchTimeMs / 1000f;
                    }
                }

                accumulated += duration;
                _itemDropThresholds[i] = accumulated;
            }

            _totalSearchTime = accumulated;
        }

        private void UpdateSlider()
        {
            if (_slider != null)
            {
                _slider.value = _totalSearchTime > 0f ? Mathf.Clamp01(_data.SearchProgressTime / _totalSearchTime) : 1f;
            }
        }

        private void ReleaseInteractingController()
        {
            if (_interactingController != null)
            {
                _interactingController.CanMove = true;
                _interactingController = null;
            }

            _searching = false;
        }

        private void DropItem(int index)
        {
            if (_data == null || index < 0 || index >= _data.ItemCount)
            {
                return;
            }

            int itemId = _data.ItemIds[index];
            ItemConfig itemConfig = GameEntry.Luban.Get<ItemConfig>(itemId);
            Rarity rarity = itemConfig != null ? itemConfig.Rarity : Rarity.White;

            float minRadius = GameEntry.Luban.Global.LootRangeMinRadius / 1000f;
            float maxRadius = GameEntry.Luban.Global.LootRangeMaxRadius / 1000f;

            if (minRadius <= 0f)
            {
                minRadius = DefaultLootRangeMinRadius;
            }

            if (maxRadius <= minRadius)
            {
                maxRadius = Mathf.Max(minRadius + 0.1f, DefaultLootRangeMaxRadius);
            }

            Vector3 dropPosition = CalculateDropPosition(transform.position, minRadius, maxRadius);

            string itemEntityAsset = GameEntry.Luban.Global.ItemEntity;
            if (string.IsNullOrEmpty(itemEntityAsset))
            {
                Log.Error("GlobalConfig.ItemEntity is not configured.");
                return;
            }

            EnsureItemEntityGroup();

            int serialId = GameEntry.Entity.SerialId();
            ItemEntityData itemData = new ItemEntityData(
                serialId,
                itemEntityAsset,
                dropPosition,
                itemId,
                1,
                rarity,
                rotation: Quaternion.identity,
                spawnFromPosition: transform.position);

            GameEntry.Entity.ShowEntity<ItemEntityLogic>(itemData, "Item", Constant.AssetPriority.SceneAsset);
            Log.Info("ResourcePoint '{0}' dropped item '{1}' (Rarity: {2}) at position {3}.",
                Entity.Id, itemId, rarity, dropPosition);
        }

        private Vector3 CalculateDropPosition(Vector3 center, float minRadius, float maxRadius)
        {
            int lootCheckLayerId = LayerMask.NameToLayer(Constant.Layer.LootCheckLayerName);
            int layerMask = lootCheckLayerId >= 0 ? (1 << lootCheckLayerId) : 0;
            IRoundRandomSource random = GameEntry.Random?.Random;

            if (layerMask != 0)
            {
                for (int i = 0; i < MaxDropPositionAttempts; i++)
                {
                    float angle = random != null
                        ? random.NextInt(0, 360) * Mathf.Deg2Rad
                        : UnityEngine.Random.Range(0f, Mathf.PI * 2f);

                    float distanceRatio = random != null
                        ? (random.NextInt(0, 1001) / 1000f)
                        : UnityEngine.Random.value;
                    float distance = Mathf.Lerp(minRadius, maxRadius, distanceRatio);

                    Vector2 candidate = (Vector2)center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * distance;
                    Collider2D hit = Physics2D.OverlapPoint(candidate, layerMask);
                    if (hit != null)
                    {
                        return new Vector3(candidate.x, candidate.y, center.z);
                    }
                }

                Collider2D centerHit = Physics2D.OverlapPoint(center, layerMask);
                if (centerHit != null)
                {
                    return center;
                }
            }

            return center;
        }

        private static void EnsureItemEntityGroup()
        {
            if (GameEntry.Entity != null && !GameEntry.Entity.HasEntityGroup("Item"))
            {
                GameEntry.Entity.AddEntityGroup("Item", 60f, 32, 60f, 0);
            }
        }
    }
}
