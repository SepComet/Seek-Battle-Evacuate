using System;
using Cysharp.Threading.Tasks;
using DG.Tweening;
using SepCore.AsyncTask;
using SepCore.Battle;
using SepCore.Definition;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SepCore.UI
{
    [DisallowMultipleComponent]
    public sealed class BattleActorCardItem : MonoBehaviour
    {
        [SerializeField] private Button _button;
        [SerializeField] private GameObject _activeMarker;
        [SerializeField] private Image _icon;
        [SerializeField] private Image _hpFill;
        [SerializeField] private Image _mpFill;
        [SerializeField] private TextMeshProUGUI _characterName;
        [SerializeField] private FormatTextUI _hpText;
        [SerializeField] private FormatTextUI _mpText;
        [SerializeField] private TextMeshProUGUI _stateText;

        private int _iconVersion;
        private int _currentUnitId;
        private Action<int> _onClick;
        private bool _hasCachedOriginalTransform;
        private Vector3 _iconOriginalLocalPos;
        private Vector3 _iconOriginalScale;
        private Sequence _enterSequence;
        private bool _isDefeated;
        private bool _isEscaped;
        private bool _hasCachedCardLocalPos;
        private Vector3 _cardOriginalLocalPos;
        private Sequence _celebrateSequence;

        public int CurrentUnitId => _currentUnitId;

        private void Awake()
        {
            _button.onClick.RemoveListener(OnCardButtonClick);
            _button.onClick.AddListener(OnCardButtonClick);
            EnsureCachedOriginalTransform();
            EnsureCachedCardLocalPos();
        }

        private void OnDisable()
        {
            ResetVisualState();
        }

        private void EnsureCachedCardLocalPos()
        {
            if (!_hasCachedCardLocalPos)
            {
                _cardOriginalLocalPos = transform.localPosition;
                _hasCachedCardLocalPos = true;
            }
        }

        private void EnsureCachedOriginalTransform()
        {
            if (_hasCachedOriginalTransform)
            {
                return;
            }

            _iconOriginalLocalPos = _icon.rectTransform.localPosition;
            _iconOriginalScale = _icon.rectTransform.localScale;
            _hasCachedOriginalTransform = true;
        }

        private void OnCardButtonClick()
        {
            _onClick?.Invoke(_currentUnitId);
        }

        public void SetOnClick(Action<int> onClick)
        {
            _onClick = onClick;
            _button.onClick.RemoveListener(OnCardButtonClick);
            _button.onClick.AddListener(OnCardButtonClick);
        }

        public void SetDetailsVisible(bool visible)
        {
            _characterName.gameObject.SetActive(visible);
            _hpText.gameObject.SetActive(visible);
            _mpText.gameObject.SetActive(visible);
            _hpFill.gameObject.SetActive(visible);
            _mpFill.gameObject.SetActive(visible);
            if (!visible) _activeMarker.SetActive(false);
        }

        /// <summary>
        /// 播放卡片头像从大地图屏幕投影坐标飞入嵌合的入场动画。
        /// </summary>
        /// <param name="startScreenPos">大地图实体在当前屏幕上的像素坐标。</param>
        /// <param name="delay">错峰延迟时间（秒）。</param>
        /// <param name="onComplete">单个卡片入场嵌合完成回调。</param>
        public void PlayEnterAnimation(Vector3 startScreenPos, float delay, Action onComplete = null)
        {
            EnsureCachedOriginalTransform();
            ResetVisualState();

            RectTransform parentRect = _icon.rectTransform.parent as RectTransform;
            Canvas canvas = GetComponentInParent<Canvas>();
            Camera uiCamera = (canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay)
                ? canvas.worldCamera
                : null;

            if (parentRect != null && RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    parentRect, startScreenPos, uiCamera, out Vector2 startLocalPos))
            {
                _icon.rectTransform.localPosition = startLocalPos;
            }
            else
            {
                _icon.rectTransform.localPosition = _iconOriginalLocalPos;
            }

            GlobalConfig global = GameEntry.Luban.Global.Data;
            _icon.rectTransform.localScale = _iconOriginalScale * (global.BattleInScale / 1000f);
            SetDetailsVisible(false);

            _enterSequence?.Kill();
            _enterSequence = DOTween.Sequence();

            if (delay > 0f)
            {
                _enterSequence.AppendInterval(delay);
            }

            float inDuration = global.BattleInDurationMs / 1000f;
            _enterSequence.Append(_icon.rectTransform.DOLocalMove(_iconOriginalLocalPos, inDuration).SetEase(Ease.OutCubic));
            _enterSequence.Join(_icon.rectTransform.DOScale(_iconOriginalScale, inDuration).SetEase(Ease.OutBack));

            _enterSequence.AppendCallback(() =>
            {
                SetDetailsVisible(true);
                float punchScale = global.BattleInPunchScale / 1000f;
                _icon.rectTransform.DOPunchScale(new Vector3(punchScale, -punchScale, 0f), 0.15f);
            });

            _enterSequence.AppendInterval(0.15f);
            _enterSequence.OnComplete(() =>
            {
                _enterSequence = null;
                onComplete?.Invoke();
            });
        }

        /// <summary>
        /// 播放战斗胜利时存活角色的轻弹跳跃庆祝动画。
        /// </summary>
        /// <param name="delay">错峰延迟时间（秒）。</param>
        public void PlayVictoryCelebrate(float delay)
        {
            if (_isDefeated || _isEscaped)
            {
                return;
            }

            EnsureCachedCardLocalPos();
            transform.DOKill();
            transform.localPosition = _cardOriginalLocalPos;

            _celebrateSequence?.Kill();
            _celebrateSequence = DOTween.Sequence();

            if (delay > 0f)
            {
                _celebrateSequence.AppendInterval(delay);
            }

            GlobalConfig global = GameEntry.Luban.Global.Data;
            float outDuration = global.BattleOutDurationMs / 1000f;
            float jumpPixels = global.BattleActorCardOutPixels;
            _celebrateSequence.Append(transform.DOPunchPosition(new Vector3(0f, jumpPixels, 0f), outDuration, 5, 0.5f));
            _celebrateSequence.OnComplete(() =>
            {
                _celebrateSequence = null;
            });
        }

        /// <summary>
        /// 重置视觉状态，停止正在执行的入场/庆祝动画并复原卡片坐标、头像坐标与详情可见性。
        /// </summary>
        public void ResetVisualState()
        {
            if (_enterSequence != null)
            {
                _enterSequence.Kill();
                _enterSequence = null;
            }

            if (_celebrateSequence != null)
            {
                _celebrateSequence.Kill();
                _celebrateSequence = null;
            }

            if (_hasCachedCardLocalPos)
            {
                transform.DOKill();
                transform.localPosition = _cardOriginalLocalPos;
            }

            if (_hasCachedOriginalTransform)
            {
                _icon.rectTransform.DOKill();
                _icon.rectTransform.localPosition = _iconOriginalLocalPos;
                _icon.rectTransform.localScale = _iconOriginalScale;
            }

            SetDetailsVisible(true);
        }

        /// <summary>
        /// 用战斗单位视图填充我方卡片：名称、HP/MP 数值与血条、当前行动者标记和配置图标。
        /// 同一单位复用时不重复加载图标。
        /// </summary>
        public void SetUnit(BattleUnitView unit, bool isCurrentActor, bool isSelectedTarget = false)
        {
            _isDefeated = unit.IsDefeated;
            _isEscaped = unit.IsEscaped;
            _button.interactable = !unit.IsDefeated && !unit.IsEscaped;

            _characterName.text = BattleUnitViewHelper.GetDisplayName(unit);

            _hpText.Set(unit.CurrentHp, unit.MaxHp);

            _mpText.Set(unit.CurrentMp, unit.MaxMp);

            SetBar(_hpFill, unit.CurrentHp, unit.MaxHp);
            SetBar(_mpFill, unit.CurrentMp, unit.MaxMp);

            _activeMarker.SetActive(isCurrentActor || isSelectedTarget);

            // 状态模板：阵亡/逃跑常驻显示（终局状态），其余清空；眩晕等瞬时状态只在轮到时飘字
            if (unit.IsDefeated)
            {
                _stateText.text = "阵亡";
            }
            else if (unit.IsEscaped)
            {
                _stateText.text = "逃跑";
            }
            else
            {
                _stateText.text = string.Empty;
            }

            _icon.color = unit.IsDefeated || unit.IsEscaped ? Color.gray : Color.white;

            if (_currentUnitId != unit.BattleUnitId)
            {
                _currentUnitId = unit.BattleUnitId;
                _iconVersion++;
                ShowIconAsync(BattleUnitViewHelper.GetPlayerIconConfig(unit.ConfigId), _iconVersion).Forget();
            }
        }

        /// <summary>
        /// 生成一个独立飘字（伤害数字、状态名）：出现后上浮淡出，不受后续刷新影响。
        /// </summary>
        public void SpawnFloatText(string text)
        {
            FloatText.Spawn(_stateText, text);
        }

        /// <summary>
        /// 异步加载角色图标；iconVersion 用于防止复用格子时旧加载结果覆盖新内容。
        /// </summary>
        private async UniTaskVoid ShowIconAsync(SpriteConfig iconConfig, int iconVersion)
        {
            if (iconConfig == null)
            {
                return;
            }

            Sprite sprite = await SpriteLoader.LoadSpriteAsync(iconConfig);
            if (sprite == null || _iconVersion != iconVersion)
            {
                return;
            }

            _icon.sprite = sprite;
            _icon.gameObject.SetActive(true);
        }

        private static void SetBar(Image fill, int current, int max)
        {
            float ratio = max > 0 ? Mathf.Clamp01((float)current / max) : 0f;
            fill.rectTransform.anchorMax = new Vector2(ratio, 1f);
        }
    }
}