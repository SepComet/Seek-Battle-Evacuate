using System;
using DG.Tweening;
using UnityEngine;
using UnityGameFramework.Runtime;

namespace SepCore.UI
{
    /// <summary>
    /// 顶级表单的进出场表现。UGF 生命周期驱动播放，布局原位只捕获一次。
    /// 可只配置局部目标，使大厅模型展示区不参与移动或渐隐。
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(RectTransform), typeof(CanvasGroup))]
    public sealed class UIFormTransition : MonoBehaviour
    {
        [Serializable]
        public sealed class Target
        {
            [SerializeField] private RectTransform _rect = null;
            [SerializeField] private CanvasGroup _group = null;
            [SerializeField] private Vector2 _offset = Vector2.zero;
            [SerializeField, Min(0f)] private float _delay = 0f;

            public RectTransform Rect => _rect;
            public CanvasGroup Group => _group;
            public Vector2 Offset => _offset;
            public float Delay => _delay;
        }

        [SerializeField] private bool _fadeRoot = true;
        [SerializeField] private bool _blockInputDuringTransition = true;
        [SerializeField] private Vector2 _rootOffset = new Vector2(0f, -32f);
        [SerializeField, Min(0.01f)] private float _enterDuration = 0.32f;
        [SerializeField, Min(0.01f)] private float _exitDuration = 0.20f;
        [SerializeField] private Target[] _targets = Array.Empty<Target>();

        private RectTransform _root;
        private CanvasGroup _rootGroup;
        private Vector2 _rootPosition;
        private float _rootAlpha;
        private bool _rootInteractable;
        private bool _rootBlocksRaycasts;
        private Vector2[] _targetPositions;
        private float[] _targetAlphas;
        private Sequence _sequence;
        private bool _initialized;

        public bool IsPlaying => _sequence != null && _sequence.IsActive();

        public void Initialize()
        {
            if (_initialized) return;
            if (_enterDuration <= 0f || _exitDuration <= 0f)
            {
                Log.Error("UI transition durations must be positive on '{0}'.", name);
                throw new InvalidOperationException("UI transition duration is invalid.");
            }

            _root = GetComponent<RectTransform>();
            _rootGroup = GetComponent<CanvasGroup>();
            _rootPosition = _root.anchoredPosition;
            _rootAlpha = _rootGroup.alpha;
            _rootInteractable = _rootGroup.interactable;
            _rootBlocksRaycasts = _rootGroup.blocksRaycasts;
            _targetPositions = new Vector2[_targets.Length];
            _targetAlphas = new float[_targets.Length];
            for (int i = 0; i < _targets.Length; i++)
            {
                Target target = _targets[i];
                if (target.Delay < 0f || target.Rect == _root)
                {
                    Log.Error("UI transition target {0} is invalid on '{1}'.", i, name);
                    throw new InvalidOperationException("UI transition targets require child transforms and nonnegative delays.");
                }

                _targetPositions[i] = target.Rect.anchoredPosition;
                _targetAlphas[i] = target.Group.alpha;
            }

            _initialized = true;
        }

        public void PlayIn(Action onComplete = null)
        {
            Initialize();
            ResetState();
            _root.anchoredPosition = _rootPosition + _rootOffset;
            if (_fadeRoot) _rootGroup.alpha = 0f;
            for (int i = 0; i < _targets.Length; i++)
            {
                _targets[i].Rect.anchoredPosition = _targetPositions[i] + _targets[i].Offset;
                _targets[i].Group.alpha = 0f;
            }

            Play(true, onComplete);
        }

        public void PlayOut(Action onComplete = null)
        {
            Initialize();
            // 中断进场时从当前表现继续退场，不先跳回原位。
            KillSequence();
            Play(false, onComplete);
        }

        public void ResetState()
        {
            KillSequence();
            if (!_initialized) return;
            _root.anchoredPosition = _rootPosition;
            _rootGroup.alpha = _rootAlpha;
            _rootGroup.interactable = _rootInteractable;
            _rootGroup.blocksRaycasts = _rootBlocksRaycasts;
            for (int i = 0; i < _targets.Length; i++)
            {
                _targets[i].Rect.anchoredPosition = _targetPositions[i];
                _targets[i].Group.alpha = _targetAlphas[i];
            }
        }

        private void Play(bool entering, Action onComplete)
        {
            _rootGroup.interactable = _blockInputDuringTransition ? false : _rootInteractable;
            _rootGroup.blocksRaycasts = _blockInputDuringTransition || _rootBlocksRaycasts;
            if (!_fadeRoot && _rootOffset == Vector2.zero && _targets.Length == 0)
            {
                Complete(entering, onComplete);
                return;
            }

            float duration = entering ? _enterDuration : _exitDuration;
            Ease ease = entering ? Ease.OutCubic : Ease.InCubic;
            Sequence sequence = DOTween.Sequence();
            _sequence = sequence;
            sequence.SetUpdate(true);
            if (_rootOffset != Vector2.zero)
            {
                Vector2 destination = entering ? _rootPosition : _rootPosition + _rootOffset;
                sequence.Insert(0f, _root.DOAnchorPos(destination, duration).SetEase(ease));
            }

            if (_fadeRoot)
            {
                sequence.Insert(0f, _rootGroup.DOFade(entering ? _rootAlpha : 0f, duration).SetEase(ease));
            }

            for (int i = 0; i < _targets.Length; i++)
            {
                Target target = _targets[i];
                Vector2 destination = entering ? _targetPositions[i] : _targetPositions[i] + target.Offset;
                float delay = entering ? target.Delay : 0f;
                sequence.Insert(delay, target.Rect.DOAnchorPos(destination, duration).SetEase(ease));
                sequence.Insert(delay, target.Group.DOFade(entering ? _targetAlphas[i] : 0f, duration).SetEase(ease));
            }

            sequence.OnComplete(() =>
            {
                if (_sequence != sequence) return;
                _sequence = null;
                Complete(entering, onComplete);
            });
        }

        private void Complete(bool entering, Action onComplete)
        {
            if (entering)
            {
                _rootGroup.interactable = _rootInteractable;
                _rootGroup.blocksRaycasts = _rootBlocksRaycasts;
            }

            onComplete?.Invoke();
        }

        private void KillSequence()
        {
            Sequence sequence = _sequence;
            _sequence = null;
            if (sequence != null) sequence.Kill();
        }

        private void OnDisable()
        {
            ResetState();
        }

        private void OnDestroy()
        {
            ResetState();
        }
    }
}
