using Cysharp.Threading.Tasks;
using DG.Tweening;
using SepCore.AsyncTask;
using SepCore.Base;
using SepCore.Definition;
using SepCore.Run;
using UnityEngine;
using UnityGameFramework.Runtime;

namespace SepCore.Entity
{
    /// <summary>
    /// 场景道具实体逻辑。
    /// 对应预制体 ItemEntity.prefab，负责在场景中渲染掉落道具贴图、碰撞检测与背包拾取交互。
    /// 实现 IInteractable 接口，供领队探测器根据稀有度和距离进行排序选择。
    /// </summary>
    public sealed class ItemEntityLogic : EntityBase, IInteractable
    {
        private const float DropParabolaDuration = 0.45f;
        private const float DropParabolaJumpPower = 0.75f;

        private ItemEntityData _data = null;
        private ItemConfig _config = null;
        private SpriteRenderer _spriteRenderer = null;
        private Material _originalMaterial = null;
        private MaterialPropertyBlock _propertyBlock = null;
        private int _spriteVersion = 0;
        private bool _isBeingPickedUp = false;
        private bool _isHighlighted = false;
        private bool _isFlying = false;
        private Sequence _dropAnimationSequence = null;

        /// <summary>
        /// 实体数据。
        /// </summary>
        public ItemEntityData Data => _data;

        /// <summary>
        /// 静态配置。
        /// </summary>
        public ItemConfig Config => _config;

        public InteractableType InteractableType => InteractableType.Item;

        public Rarity Rarity => _data != null ? _data.Rarity : (_config != null ? _config.Rarity : Rarity.None);

        public Vector3 Position => transform.position;

        public bool CanInteract => _data != null && !_isBeingPickedUp && !_isFlying;

        public GameObject EntityGameObject => gameObject;

        public void SetHighlight(bool highlight)
        {
            _isHighlighted = highlight;
            if (_spriteRenderer == null)
            {
                _spriteRenderer = GetComponentInChildren<SpriteRenderer>();
            }

            Color outlineColor = GetRarityOutlineColor(Rarity);
            Exploration.SpriteOutlineApplier.ApplyOutline(
                _spriteRenderer,
                ref _originalMaterial,
                ref _propertyBlock,
                highlight,
                outlineColor);
        }

        private static Color GetRarityOutlineColor(Rarity rarity)
        {
            return rarity switch
            {
                Rarity.Red => new Color(1f, 0.25f, 0.25f, 1f),
                Rarity.Gold => new Color(1f, 0.85f, 0.2f, 1f),
                Rarity.Blue => new Color(0.3f, 0.7f, 1f, 1f),
                Rarity.Green => new Color(0.3f, 1f, 0.4f, 1f),
                Rarity.White => new Color(0.95f, 0.95f, 0.95f, 1f),
                _ => new Color(1f, 0.9f, 0.2f, 1f)
            };
        }

        public void OnInteract(GameObject interactor)
        {
            if (_isBeingPickedUp || _data == null)
            {
                return;
            }

            if (GameEntry.Round?.Session == null)
            {
                Log.Error("Cannot pick up item because RoundSession is null.");
                return;
            }

            RoundItemContainer backpack = GameEntry.Round.Session.Backpack;
            if (backpack.IsFull)
            {
                Log.Info("Backpack is full, cannot pick up item '{0}'.", _data.ItemId);
                return;
            }

            _isBeingPickedUp = true;
            int addedCount = backpack.TryAddItem(_data.ItemId, _data.Count);
            if (addedCount > 0)
            {
                // 派发背包变更事件，刷新 HUD 战利品与空闲槽位
                GameEntry.Event.Fire(this, RoundBackpackChangedEventArgs.Create(
                    backpack.UsedSlotsCount, backpack.MaxSlots, GameEntry.Round.Session.TotalLootValue));

                Log.Info("Item '{0}' x{1} picked up successfully.", _data.ItemId, addedCount);

                if (addedCount >= _data.Count)
                {
                    // 全部放入，回收场景实体
                    GameEntry.Entity.HideEntity(this);
                }
                else
                {
                    // 仅放入部分，解除拾取锁定
                    _isBeingPickedUp = false;
                }
            }
            else
            {
                _isBeingPickedUp = false;
                Log.Warning("Failed to add item '{0}' to backpack.", _data.ItemId);
            }
        }

        protected override void OnShow(object userData)
        {
            base.OnShow(userData);

            _isBeingPickedUp = false;
            _data = userData as ItemEntityData;
            if (_data == null)
            {
                Log.Error("Item entity data is invalid.");
                return;
            }

            _config = GameEntry.Luban.Get<ItemConfig>(_data.ItemId);
            if (_config == null)
            {
                Log.Error("Item config '{0}' is invalid.", _data.ItemId);
                return;
            }

            _spriteRenderer = GetComponentInChildren<SpriteRenderer>();
            if (_spriteRenderer != null)
            {
                _spriteRenderer.sprite = null;
            }

            _spriteVersion++;
            ShowItemSpriteAsync(_spriteVersion).Forget();

            if (_data.SpawnFromPosition.HasValue)
            {
                PlayDropParabolaAnimation(_data.SpawnFromPosition.Value, _data.Position);
            }
        }

        protected override void OnHide(bool isShutdown, object userData)
        {
            KillDropAnimation();
            SetHighlight(false);
            _isBeingPickedUp = false;
            _data = null;
            _config = null;
            _spriteRenderer = null;
            _originalMaterial = null;
            base.OnHide(isShutdown, userData);
        }

        private void PlayDropParabolaAnimation(Vector3 startPos, Vector3 targetPos)
        {
            KillDropAnimation();
            _isFlying = true;
            transform.position = startPos;
            transform.localScale = Vector3.one * 0.4f;

            _dropAnimationSequence = DOTween.Sequence();
            _dropAnimationSequence.Append(
                transform.DOJump(targetPos, DropParabolaJumpPower, 1, DropParabolaDuration)
                    .SetEase(Ease.Linear));
            _dropAnimationSequence.Join(
                transform.DOScale(Vector3.one, DropParabolaDuration)
                    .SetEase(Ease.OutQuad));
            _dropAnimationSequence.Append(
                transform.DOPunchScale(new Vector3(0.2f, -0.2f, 0f), 0.15f, 6, 0.5f));
            _dropAnimationSequence.OnComplete(() =>
            {
                _isFlying = false;
                _dropAnimationSequence = null;
            });
        }

        private void KillDropAnimation()
        {
            if (_dropAnimationSequence != null)
            {
                _dropAnimationSequence.Kill();
                _dropAnimationSequence = null;
            }

            transform.DOKill();
            transform.localScale = Vector3.one;
            _isFlying = false;
        }

        private async UniTaskVoid ShowItemSpriteAsync(int version)
        {
            if (_config == null || _config.Icon_Ref == null)
            {
                return;
            }

            Sprite sprite = await SpriteLoader.LoadSpriteAsync(_config.Icon_Ref);
            if (sprite == null || _spriteRenderer == null || _spriteVersion != version)
            {
                return;
            }

            _spriteRenderer.sprite = sprite;
            if (_isHighlighted)
            {
                SetHighlight(true);
            }
        }
    }
}
