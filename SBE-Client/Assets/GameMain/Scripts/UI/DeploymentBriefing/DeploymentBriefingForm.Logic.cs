using System;
using DG.Tweening;
using SepCore.Definition;
using UnityEngine;
using UnityGameFramework.Runtime;

namespace SepCore.UI
{
    /// <summary>
    /// 战术部署与战局横幅数据载体。
    /// </summary>
    public sealed class DeploymentBannerData
    {
        public string MapName { get; }
        public DifficultyTier ThreatTier { get; }
        public string SubInfo { get; }
        public float HoldDuration { get; }
        public Action OnTransitionComplete { get; }

        public DeploymentBannerData(
            string mapName = "SECTOR 04 — ABANDONED DEPOT",
            DifficultyTier threatTier = DifficultyTier.Tier1,
            string subInfo = "TACTICAL SCAN: DENSE FOG DETECTED  •  OBJECTIVE: SCAVENGE & EVACUATE",
            float holdDuration = 1.0f,
            Action onTransitionComplete = null)
        {
            MapName = mapName;
            ThreatTier = threatTier;
            SubInfo = subInfo;
            HoldDuration = holdDuration;
            OnTransitionComplete = onTransitionComplete;
        }
    }

    /// <summary>
    /// 战术部署与战局横幅表单逻辑（手写 partial，与自动生成的 DeploymentBriefingForm.cs 合并）。
    /// 作为轻量级过场横幅，展示地图与难度核心信息，播放平滑入场与淡出动效后立即移交玩家控制权。
    /// </summary>
    public partial class DeploymentBriefingForm : UGuiForm
    {
        private Sequence _bannerSequence;
        private Action _onCompleteCallback;
        private bool _isCompleted;

        protected override void OnOpen(object userData)
        {
            base.OnOpen(userData);

            _isCompleted = false;

            // 根节点不阻挡操作，允许在横幅播放期间或淡出时玩家即可开始行动
            if (View.canvasGroup != null)
            {
                View.canvasGroup.alpha = 1f;
                View.canvasGroup.interactable = false;
                View.canvasGroup.blocksRaycasts = false;
            }

            DeploymentBannerData data = userData as DeploymentBannerData ?? new DeploymentBannerData();
            Refresh(data);
            PlayBannerSequence(data.HoldDuration, data.OnTransitionComplete);
        }

        protected override void OnUpdate(float elapseSeconds, float realElapseSeconds)
        {
            base.OnUpdate(elapseSeconds, realElapseSeconds);

            // 任意点击或键盘按键可立即快速淡出跳过
            if (!_isCompleted && (Input.anyKeyDown || Input.GetMouseButtonDown(0)))
            {
                FastDismiss();
            }
        }

        protected override void OnClose(bool isShutdown, object userData)
        {
            KillSequence();
            _onCompleteCallback = null;
            base.OnClose(isShutdown, userData);
        }

        /// <summary>
        /// 刷新横幅展示文案。
        /// </summary>
        public void Refresh(DeploymentBannerData data)
        {
            if (data == null)
            {
                return;
            }

            if (View.mapNameText != null)
            {
                View.mapNameText.text = data.MapName;
            }

            if (View.threatTierText != null)
            {
                View.threatTierText.text = $"// INFILTRATION PROTOCOL  •  THREAT LEVEL: {GetThreatTierDisplayName(data.ThreatTier)}";
            }

            if (View.subInfoText != null && !string.IsNullOrEmpty(data.SubInfo))
            {
                View.subInfoText.text = data.SubInfo;
            }
        }

        /// <summary>
        /// 播放轻量过场横幅动画序列（淡入 -> 停留 -> 淡出并交权）。
        /// </summary>
        public void PlayBannerSequence(float holdDuration = 1.0f, Action onComplete = null)
        {
            KillSequence();
            _onCompleteCallback = onComplete;

            if (View.bannerCanvasGroup == null)
            {
                CompleteSequence();
                return;
            }

            View.bannerCanvasGroup.alpha = 0f;

            _bannerSequence = DOTween.Sequence();
            // 0.25 秒淡入横幅
            _bannerSequence.Append(View.bannerCanvasGroup.DOFade(1f, 0.25f).SetEase(Ease.OutCubic));
            // 停留片刻展示关键信息
            _bannerSequence.AppendInterval(holdDuration);
            // 0.35 秒平滑淡出
            _bannerSequence.Append(View.bannerCanvasGroup.DOFade(0f, 0.35f).SetEase(Ease.InCubic));
            _bannerSequence.OnComplete(CompleteSequence);
        }

        /// <summary>
        /// 玩家交互时快速淡出横幅。
        /// </summary>
        public void FastDismiss()
        {
            if (_isCompleted)
            {
                return;
            }

            KillSequence();

            if (View.bannerCanvasGroup != null && View.bannerCanvasGroup.alpha > 0f)
            {
                View.bannerCanvasGroup.DOFade(0f, 0.15f).OnComplete(CompleteSequence);
            }
            else
            {
                CompleteSequence();
            }
        }

        private void CompleteSequence()
        {
            if (_isCompleted)
            {
                return;
            }

            _isCompleted = true;
            Action callback = _onCompleteCallback;
            _onCompleteCallback = null;

            callback?.Invoke();
            Close(true);
        }

        private void KillSequence()
        {
            if (_bannerSequence != null && _bannerSequence.IsActive())
            {
                _bannerSequence.Kill();
                _bannerSequence = null;
            }
        }

        private static string GetThreatTierDisplayName(DifficultyTier tier)
        {
            switch (tier)
            {
                case DifficultyTier.Tier1:
                    return "TIER I";
                case DifficultyTier.Tier2:
                    return "TIER II";
                case DifficultyTier.Tier3:
                    return "TIER III";
                default:
                    return tier.ToString().ToUpperInvariant();
            }
        }
    }
}
