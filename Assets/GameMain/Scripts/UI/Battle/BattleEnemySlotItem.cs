using System;
using Cysharp.Threading.Tasks;
using DG.Tweening;
using SepCore.AsyncTask;
using SepCore.Battle;
using SepCore.Definition;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityGameFramework.Runtime;

namespace SepCore.UI
{
    [DisallowMultipleComponent]
    public sealed class BattleEnemySlotItem : MonoBehaviour
    {
        [SerializeField] private Button targetButton;
        [SerializeField] private GameObject selectedMarker;
        [SerializeField] private Image icon;
        [SerializeField] private Image hpFill;
        [SerializeField] private TextMeshProUGUI enemyName;
        [SerializeField] private TextMeshProUGUI hpText;
        [SerializeField] private TextMeshProUGUI stateText;

        private int _iconVersion;
        private int _currentUnitId;
        private Action<int> _onClick;
        private bool _hasCachedOriginalTransform;
        private Vector3 _iconOriginalLocalPos;
        private Vector3 _iconOriginalScale;
        private Sequence _enterSequence;
        private bool _hasCachedSlotLocalPos;
        private Vector3 _slotOriginalLocalPos;
        private Sequence _fadeSequence;

        public int CurrentUnitId => _currentUnitId;

        private void Awake()
        {
            EnsureCachedOriginalTransform();
            EnsureCachedSlotLocalPos();
        }

        private void OnDisable()
        {
            ResetVisualState();
        }

        private void EnsureCachedSlotLocalPos()
        {
            if (!_hasCachedSlotLocalPos)
            {
                _slotOriginalLocalPos = transform.localPosition;
                _hasCachedSlotLocalPos = true;
            }
        }

        private void EnsureCachedOriginalTransform()
        {
            if (_hasCachedOriginalTransform || icon == null)
            {
                return;
            }

            _iconOriginalLocalPos = icon.rectTransform.localPosition;
            _iconOriginalScale = icon.rectTransform.localScale;
            _hasCachedOriginalTransform = true;
        }

        public void SetOnClick(Action<int> onClick)
        {
            _onClick = onClick;
            targetButton.onClick.RemoveAllListeners();
            if (_onClick != null)
            {
                targetButton.onClick.AddListener(() => _onClick(_currentUnitId));
            }
        }

        public void SetDetailsVisible(bool visible)
        {
            enemyName.gameObject.SetActive(visible);
            hpText.gameObject.SetActive(visible);
            hpFill.gameObject.SetActive(visible);
            stateText.gameObject.SetActive(visible);
            if (!visible) selectedMarker.SetActive(false);
        }

        /// <summary>
        /// 播放敌人头像从大地图屏幕投影坐标飞入嵌合的入场动画。
        /// </summary>
        /// <param name="startScreenPos">大地图敌人小队在当前屏幕上的像素坐标。</param>
        /// <param name="delay">错峰延迟时间（秒）。</param>
        /// <param name="onComplete">单个卡片入场嵌合完成回调。</param>
        public void PlayEnterAnimation(Vector3 startScreenPos, float delay, Action onComplete = null)
        {
            if (icon == null)
            {
                onComplete?.Invoke();
                return;
            }

            EnsureCachedOriginalTransform();
            ResetVisualState();

            RectTransform parentRect = icon.rectTransform.parent as RectTransform;
            Canvas canvas = GetComponentInParent<Canvas>();
            Camera uiCamera = (canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay)
                ? canvas.worldCamera
                : null;

            if (parentRect != null && RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    parentRect, startScreenPos, uiCamera, out Vector2 startLocalPos))
            {
                icon.rectTransform.localPosition = startLocalPos;
            }
            else
            {
                icon.rectTransform.localPosition = _iconOriginalLocalPos;
            }

            icon.rectTransform.localScale = _iconOriginalScale * 0.35f;
            SetDetailsVisible(false);

            _enterSequence?.Kill();
            _enterSequence = DOTween.Sequence();

            if (delay > 0f)
            {
                _enterSequence.AppendInterval(delay);
            }

            _enterSequence.Append(icon.rectTransform.DOLocalMove(_iconOriginalLocalPos, 0.42f).SetEase(Ease.OutCubic));
            _enterSequence.Join(icon.rectTransform.DOScale(_iconOriginalScale, 0.42f).SetEase(Ease.OutBack));

            _enterSequence.AppendCallback(() =>
            {
                SetDetailsVisible(true);
                icon.rectTransform.DOPunchScale(new Vector3(0.12f, -0.12f, 0f), 0.15f);
            });

            _enterSequence.AppendInterval(0.15f);
            _enterSequence.OnComplete(() =>
            {
                _enterSequence = null;
                onComplete?.Invoke();
            });
        }

        /// <summary>
        /// 播放战斗胜利时敌人槽位微幅上浮消散淡出动画。
        /// </summary>
        /// <param name="delay">错峰延迟时间（秒）。</param>
        /// <param name="onComplete">淡出完成回调。</param>
        public void PlayVictoryFadeOut(float delay, Action onComplete = null)
        {
            EnsureCachedSlotLocalPos();

            CanvasGroup group = gameObject.GetOrAddComponent<CanvasGroup>();
            group.DOKill();
            transform.DOKill();

            _fadeSequence?.Kill();
            _fadeSequence = DOTween.Sequence();

            if (delay > 0f)
            {
                _fadeSequence.AppendInterval(delay);
            }

            // 微幅上浮 20px 并淡出 0.35s
            _fadeSequence.Append(group.DOFade(0f, 0.35f).SetEase(Ease.OutQuad));
            _fadeSequence.Join(transform.DOLocalMoveY(_slotOriginalLocalPos.y + 20f, 0.35f).SetEase(Ease.OutQuad));
            _fadeSequence.OnComplete(() =>
            {
                _fadeSequence = null;
                onComplete?.Invoke();
            });
        }

        /// <summary>
        /// 重置视觉状态，停止正在执行的入场/胜利动画并复原头像坐标、槽位位置与详情可见性。
        /// </summary>
        public void ResetVisualState()
        {
            if (_enterSequence != null)
            {
                _enterSequence.Kill();
                _enterSequence = null;
            }

            if (_fadeSequence != null)
            {
                _fadeSequence.Kill();
                _fadeSequence = null;
            }

            if (icon != null && _hasCachedOriginalTransform)
            {
                icon.rectTransform.DOKill();
                icon.rectTransform.localPosition = _iconOriginalLocalPos;
                icon.rectTransform.localScale = _iconOriginalScale;
            }

            if (_hasCachedSlotLocalPos)
            {
                transform.DOKill();
                transform.localPosition = _slotOriginalLocalPos;
            }

            CanvasGroup group = GetComponent<CanvasGroup>();
            if (group != null)
            {
                group.DOKill();
                group.alpha = 1f;
            }

            SetDetailsVisible(true);
        }

        /// <summary>
        /// 用战斗单位视图填充敌人槽：名称、HP 数值与血条、配置图标和当前行动者标记。
        /// 阵亡或已逃跑目标不可选；同一单位复用时不重复加载图标。
        /// </summary>
        public void SetEnemy(BattleUnitView unit, bool isCurrentActor, bool isSelectedTarget = false)
        {
            enemyName.text = BattleUnitViewHelper.GetDisplayName(unit);

            hpText.text = unit.CurrentHp + " / " + unit.MaxHp;

            SetBar(hpFill, unit.CurrentHp, unit.MaxHp);

            selectedMarker.SetActive(isCurrentActor || isSelectedTarget);

            targetButton.interactable = !unit.IsDefeated && !unit.IsEscaped;

            // 状态模板：阵亡常驻显示（终局状态），其余清空；眩晕等瞬时状态只在轮到时飘字
            stateText.text = unit.IsDefeated ? "阵亡" : string.Empty;

            icon.color = unit.IsDefeated || unit.IsEscaped ? Color.gray : Color.white;

            if (_currentUnitId != unit.BattleUnitId)
            {
                _currentUnitId = unit.BattleUnitId;
                _iconVersion++;
                ShowIconAsync(BattleUnitViewHelper.GetEnemyIconConfig(unit.ConfigId), _iconVersion).Forget();
            }
        }

        /// <summary>
        /// 生成一个独立飘字（伤害数字、状态名）：出现后上浮淡出，不受后续刷新影响。
        /// </summary>
        public void SpawnFloatText(string text)
        {
            FloatText.Spawn(stateText, text);
        }

        /// <summary>
        /// 异步加载敌人图标；iconVersion 用于防止复用格子时旧加载结果覆盖新内容。
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

            icon.sprite = sprite;
            icon.gameObject.SetActive(true);
        }

        private static void SetBar(Image fill, int current, int max)
        {
            float ratio = max > 0 ? Mathf.Clamp01((float)current / max) : 0f;
            fill.rectTransform.anchorMax = new Vector2(ratio, 1f);
        }
    }
}
