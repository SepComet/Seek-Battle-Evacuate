using System.Collections;
using System.Collections.Generic;
using System;
using SepCore.Sound;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityGameFramework.Runtime;

namespace SepCore.UI
{
    public abstract class UGuiForm : UIFormLogic
    {
        public const int DepthFactor = 100;
        private const float FadeTime = 0.3f;

        private static Font _mainFont = null;
        private static TMP_FontAsset _mainTMPFont = null;
        private Canvas _cachedCanvas = null;
        private CanvasGroup _canvasGroup = null;
        private UIFormTransition _transition;
        private bool _isExiting;
        private bool _exitPending;
        private Action _pendingExit;
        private Coroutine _transitionRoutine;
        private List<Canvas> _cachedCanvasContainer = new List<Canvas>();

        public int OriginalDepth { get; private set; }

        public int Depth => _cachedCanvas.sortingOrder;

        public bool IsTransitioning => _isExiting || (_transition != null && _transition.IsPlaying);

        public void Close()
        {
            Close(false);
        }

        public void Close(bool ignoreFade)
        {
            if (ignoreFade)
            {
                ResetTransition();
                GameEntry.UI.CloseUIForm(this);
            }
            else
            {
                PlayExitTransition(() => GameEntry.UI.CloseUIForm(this));
            }
        }

        public void Close(Action onClosed)
        {
            PlayExitTransition(() =>
            {
                GameEntry.UI.CloseUIForm(this);
                onClosed?.Invoke();
            });
        }

        /// <summary>流程切换先等待视觉退场，再执行关闭或换场景。</summary>
        public void PlayExitTransition(Action onComplete)
        {
            if (_isExiting) return;
            _isExiting = true;
            _exitPending = true;
            _pendingExit = onComplete;
            BeginExitVisuals();
        }

        private void BeginExitVisuals()
        {
            StopTransitionRoutine();
            if (_transition != null)
            {
                _transition.PlayOut(CompleteExit);
            }
            else
            {
                _transitionRoutine = StartCoroutine(ExitCo(CompleteExit));
            }
        }

        private void CompleteExit()
        {
            Action callback = _pendingExit;
            _pendingExit = null;
            _exitPending = false;
            callback?.Invoke();
        }

        protected void PlayEnterTransition()
        {
            StopTransitionRoutine();
            _isExiting = false;
            _exitPending = false;
            _pendingExit = null;
            if (_transition != null)
            {
                _canvasGroup.alpha = 1f;
                _transition.PlayIn();
            }
            else
            {
                _canvasGroup.alpha = 0f;
                _transitionRoutine = StartCoroutine(EnterCo());
            }
        }

        private void ResetTransition()
        {
            StopTransitionRoutine();
            _isExiting = false;
            _exitPending = false;
            _pendingExit = null;
            if (_transition != null) _transition.ResetState();
        }

        private void StopTransitionRoutine()
        {
            if (_transitionRoutine == null) return;
            StopCoroutine(_transitionRoutine);
            _transitionRoutine = null;
        }

        public void PlayUISound(int uiSoundId)
        {
            GameEntry.Sound.PlayUISound(uiSoundId);
        }

        public static void SetMainFont(Font mainFont)
        {
            if (mainFont == null)
            {
                Log.Error("Main font is invalid.");
                return;
            }

            _mainFont = mainFont;
        }

        public static void SetMainTMPFont(TMP_FontAsset mainTMPFont)
        {
            if (mainTMPFont == null)
            {
                Log.Error("Main font is invalid.");
                return;
            }

            _mainTMPFont = mainTMPFont;
        }

#if UNITY_2017_3_OR_NEWER
        protected override void OnInit(object userData)
#else
        protected internal override void OnInit(object userData)
#endif
        {
            base.OnInit(userData);

            _cachedCanvas = gameObject.GetOrAddComponent<Canvas>();
            _cachedCanvas.overrideSorting = true;
            OriginalDepth = _cachedCanvas.sortingOrder;

            _canvasGroup = gameObject.GetOrAddComponent<CanvasGroup>();
            _transition = GetComponent<UIFormTransition>();

            RectTransform rect = GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = Vector2.zero;

            if (_transition != null) _transition.Initialize();

            gameObject.GetOrAddComponent<GraphicRaycaster>();

            if (_mainTMPFont == null)
            {
                Log.Warning("Main TMP font isn't set.");
            }
            else
            {
                var tmp_texts = GetComponentsInChildren<TMP_Text>(true);
                foreach (var text in tmp_texts)
                {
                    text.font = _mainTMPFont;
                }
            }

            if (_mainFont == null)
            {
                Log.Warning("Main font isn't set.");
            }
            else
            {
                var texts = GetComponentsInChildren<Text>(true);
                foreach (var text in texts)
                {
                    text.font = _mainFont;
                    if (!string.IsNullOrEmpty(text.text))
                    {
                        text.text = GameEntry.Localization.GetString(text.text);
                    }
                }
            }
        }

#if UNITY_2017_3_OR_NEWER
        protected override void OnRecycle()
#else
        protected internal override void OnRecycle()
#endif
        {
            ResetTransition();
            base.OnRecycle();
        }

#if UNITY_2017_3_OR_NEWER
        protected override void OnOpen(object userData)
#else
        protected internal override void OnOpen(object userData)
#endif
        {
            base.OnOpen(userData);

            PlayEnterTransition();
        }

#if UNITY_2017_3_OR_NEWER
        protected override void OnClose(bool isShutdown, object userData)
#else
        protected internal override void OnClose(bool isShutdown, object userData)
#endif
        {
            ResetTransition();
            base.OnClose(isShutdown, userData);
        }

#if UNITY_2017_3_OR_NEWER
        protected override void OnPause()
#else
        protected internal override void OnPause()
#endif
        {
            StopTransitionRoutine();
            if (_transition != null) _transition.ResetState();
            if (!_exitPending) _isExiting = false;
            base.OnPause();
        }

#if UNITY_2017_3_OR_NEWER
        protected override void OnResume()
#else
        protected internal override void OnResume()
#endif
        {
            base.OnResume();
            if (_exitPending) BeginExitVisuals();
            else PlayEnterTransition();
        }

#if UNITY_2017_3_OR_NEWER
        protected override void OnCover()
#else
        protected internal override void OnCover()
#endif
        {
            base.OnCover();
        }

#if UNITY_2017_3_OR_NEWER
        protected override void OnReveal()
#else
        protected internal override void OnReveal()
#endif
        {
            base.OnReveal();
        }

#if UNITY_2017_3_OR_NEWER
        protected override void OnRefocus(object userData)
#else
        protected internal override void OnRefocus(object userData)
#endif
        {
            base.OnRefocus(userData);
        }

#if UNITY_2017_3_OR_NEWER
        protected override void OnUpdate(float elapseSeconds, float realElapseSeconds)
#else
        protected internal override void OnUpdate(float elapseSeconds, float realElapseSeconds)
#endif
        {
            base.OnUpdate(elapseSeconds, realElapseSeconds);
        }

#if UNITY_2017_3_OR_NEWER
        protected override void OnDepthChanged(int uiGroupDepth, int depthInUIGroup)
#else
        protected internal override void OnDepthChanged(int uiGroupDepth, int depthInUIGroup)
#endif
        {
            int oldDepth = Depth;
            base.OnDepthChanged(uiGroupDepth, depthInUIGroup);
            int deltaDepth = UGuiGroupHelper.DepthFactor * uiGroupDepth + DepthFactor * depthInUIGroup - oldDepth +
                             OriginalDepth;
            GetComponentsInChildren(true, _cachedCanvasContainer);
            for (int i = 0; i < _cachedCanvasContainer.Count; i++)
            {
                _cachedCanvasContainer[i].sortingOrder += deltaDepth;
            }

            _cachedCanvasContainer.Clear();
        }

        private IEnumerator ExitCo(Action onComplete)
        {
            float startAlpha = _canvasGroup.alpha;
            float elapsed = 0f;
            while (elapsed < FadeTime)
            {
                elapsed += Time.unscaledDeltaTime;
                _canvasGroup.alpha = Mathf.Lerp(startAlpha, 0f, elapsed / FadeTime);
                yield return null;
            }
            _transitionRoutine = null;
            onComplete?.Invoke();
        }

        private IEnumerator EnterCo()
        {
            float elapsed = 0f;
            while (elapsed < FadeTime)
            {
                elapsed += Time.unscaledDeltaTime;
                _canvasGroup.alpha = Mathf.Clamp01(elapsed / FadeTime);
                yield return null;
            }
            _transitionRoutine = null;
        }
    }
}
