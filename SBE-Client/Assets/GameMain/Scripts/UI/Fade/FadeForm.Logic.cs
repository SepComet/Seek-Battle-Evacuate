using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using DG.Tweening;
using UnityEngine;
using UnityGameFramework.Runtime;

namespace SepCore.UI
{
    /// <summary>
    /// 转场渐变遮罩界面逻辑（手写 partial，与自动生成的 FadeForm.cs 合并）。
    /// 提供平滑淡入（黑屏遮盖）与淡出（显现画面）能力，在场景切换期间阻断玩家射线交互，避免场景卸载/加载过程中的画面闪烁。
    /// </summary>
    public partial class FadeForm : UGuiForm
    {
        public const float DefaultFadeDuration = 0.35f;

        private Tweener _fadeTweener = null;

#if UNITY_2017_3_OR_NEWER
        protected override void OnInit(object userData)
#else
        protected internal override void OnInit(object userData)
#endif
        {
            base.OnInit(userData);

            // 根节点 CanvasGroup 保持常开且不阻挡，具体 alpha 和 raycast 交由 View.maskCanvasGroup 精确控制
            CanvasGroup rootCanvasGroup = GetComponent<CanvasGroup>();
            if (rootCanvasGroup != null)
            {
                rootCanvasGroup.alpha = 1f;
                rootCanvasGroup.blocksRaycasts = true;
                rootCanvasGroup.interactable = true;
            }

            ResetMaskState();
        }

#if UNITY_2017_3_OR_NEWER
        protected override void OnOpen(object userData)
#else
        protected internal override void OnOpen(object userData)
#endif
        {
            base.OnOpen(userData);

            // 停止 UGuiForm 默认在根节点上执行的 FadeToAlpha 协程，避免与 maskCanvasGroup 冲突
            StopAllCoroutines();

            CanvasGroup rootCanvasGroup = GetComponent<CanvasGroup>();
            if (rootCanvasGroup != null)
            {
                rootCanvasGroup.alpha = 1f;
                rootCanvasGroup.blocksRaycasts = true;
                rootCanvasGroup.interactable = true;
            }

            ResetMaskState();
        }

#if UNITY_2017_3_OR_NEWER
        protected override void OnClose(bool isShutdown, object userData)
#else
        protected internal override void OnClose(bool isShutdown, object userData)
#endif
        {
            KillTween();
            base.OnClose(isShutdown, userData);
        }

#if UNITY_2017_3_OR_NEWER
        protected override void OnRecycle()
#else
        protected internal override void OnRecycle()
#endif
        {
            KillTween();
            ResetMaskState();
            base.OnRecycle();
        }

        /// <summary>
        /// 执行淡入（画面渐黑遮盖屏幕，并立即开始阻挡射线交互）。
        /// </summary>
        /// <param name="duration">渐变持续时间（秒）</param>
        /// <param name="onComplete">淡入完成回调</param>
        public void FadeIn(float duration = DefaultFadeDuration, Action onComplete = null)
        {
            KillTween();

            if (View == null || View.maskCanvasGroup == null)
            {
                Log.Error("FadeForm View or maskCanvasGroup is invalid.");
                onComplete?.Invoke();
                return;
            }

            View.maskCanvasGroup.blocksRaycasts = true;
            View.maskCanvasGroup.interactable = true;

            if (duration <= 0f)
            {
                View.maskCanvasGroup.alpha = 1f;
                onComplete?.Invoke();
                return;
            }

            _fadeTweener = View.maskCanvasGroup.DOFade(1f, duration)
                .SetUpdate(true)
                .OnComplete(() =>
                {
                    _fadeTweener = null;
                    onComplete?.Invoke();
                });
        }

        /// <summary>
        /// 执行淡出（画面渐显还原场景，淡出完成时解除射线阻挡）。
        /// </summary>
        /// <param name="duration">渐变持续时间（秒）</param>
        /// <param name="onComplete">淡出完成回调</param>
        public void FadeOut(float duration = DefaultFadeDuration, Action onComplete = null)
        {
            KillTween();

            if (View == null || View.maskCanvasGroup == null)
            {
                Log.Error("FadeForm View or maskCanvasGroup is invalid.");
                onComplete?.Invoke();
                return;
            }

            if (duration <= 0f)
            {
                View.maskCanvasGroup.alpha = 0f;
                View.maskCanvasGroup.blocksRaycasts = false;
                View.maskCanvasGroup.interactable = false;
                onComplete?.Invoke();
                return;
            }

            _fadeTweener = View.maskCanvasGroup.DOFade(0f, duration)
                .SetUpdate(true)
                .OnComplete(() =>
                {
                    _fadeTweener = null;
                    if (View != null && View.maskCanvasGroup != null)
                    {
                        View.maskCanvasGroup.blocksRaycasts = false;
                        View.maskCanvasGroup.interactable = false;
                    }
                    onComplete?.Invoke();
                });
        }

        /// <summary>
        /// 异步执行淡入（画面渐黑遮盖屏幕）。
        /// </summary>
        /// <param name="duration">渐变持续时间（秒）</param>
        /// <param name="cancellationToken">取消标记</param>
        public async UniTask FadeInAsync(float duration = DefaultFadeDuration, CancellationToken cancellationToken = default)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            var tcs = new UniTaskCompletionSource();
            FadeIn(duration, () => tcs.TrySetResult());

            try
            {
                await tcs.Task.AttachExternalCancellation(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                KillTween();
                throw;
            }
        }

        /// <summary>
        /// 异步执行淡出（画面渐显还原场景）。
        /// </summary>
        /// <param name="duration">渐变持续时间（秒）</param>
        /// <param name="cancellationToken">取消标记</param>
        public async UniTask FadeOutAsync(float duration = DefaultFadeDuration, CancellationToken cancellationToken = default)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            var tcs = new UniTaskCompletionSource();
            FadeOut(duration, () => tcs.TrySetResult());

            try
            {
                await tcs.Task.AttachExternalCancellation(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                KillTween();
                throw;
            }
        }

        /// <summary>
        /// 重置遮罩状态为完全透明且不阻断射线。
        /// </summary>
        public void ResetMaskState()
        {
            KillTween();

            if (View != null && View.maskCanvasGroup != null)
            {
                View.maskCanvasGroup.alpha = 0f;
                View.maskCanvasGroup.blocksRaycasts = false;
                View.maskCanvasGroup.interactable = false;
            }
        }

        /// <summary>
        /// 立即设置遮罩的透明度与射线阻断状态。
        /// </summary>
        public void SetMask(float alpha, bool blocksRaycasts)
        {
            KillTween();

            if (View != null && View.maskCanvasGroup != null)
            {
                View.maskCanvasGroup.alpha = alpha;
                View.maskCanvasGroup.blocksRaycasts = blocksRaycasts;
                View.maskCanvasGroup.interactable = blocksRaycasts;
            }
        }

        private void KillTween()
        {
            if (_fadeTweener != null)
            {
                if (_fadeTweener.IsActive())
                {
                    _fadeTweener.Kill();
                }
                _fadeTweener = null;
            }
        }
    }
}
