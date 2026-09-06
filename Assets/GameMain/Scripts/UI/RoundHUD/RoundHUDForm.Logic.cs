using System;
using GameFramework.Event;
using SepCore.Base;
using SepCore.Definition;
using SepCore.Run;
using UnityEngine;
using UnityGameFramework.Runtime;

namespace SepCore.UI
{
    /// <summary>
    /// 局内 HUD 界面逻辑（手写 partial，与自动生成的 RoundHUDForm.cs 合并）。
    /// 负责探索主界面 HUD 上的基础交互：剩余探索时间轮询刷新、背包面板打开/收起切换、背包状态（剩余槽位/总战利品）展示，
    /// 以及撤离点标记在小地图上的逐帧定位。
    /// </summary>
    public partial class RoundHUDForm : UGuiForm
    {
        private int _lastRemainingSeconds = -1;
        private ExtractionMarkerData _extractionMarkerData;

        protected override void OnOpen(object userData)
        {
            base.OnOpen(userData);

            View.backpackButton.onClick.AddListener(OnBackpackButtonClick);
            GameEntry.Event.Subscribe(RoundBackpackChangedEventArgs.EventId, OnBackpackChanged);

            _lastRemainingSeconds = -1;
            _extractionMarkerData = null;
            UpdateRemainingTime(force: true);
            UpdateBackpackStats();
            ApplyExtractionMarker(userData);
        }

        protected override void OnClose(bool isShutdown, object userData)
        {
            View.backpackButton.onClick.RemoveListener(OnBackpackButtonClick);
            GameEntry.Event.Unsubscribe(RoundBackpackChangedEventArgs.EventId, OnBackpackChanged);

            base.OnClose(isShutdown, userData);
        }

        protected override void OnUpdate(float elapseSeconds, float realElapseSeconds)
        {
            base.OnUpdate(elapseSeconds, realElapseSeconds);

            UpdateRemainingTime(force: false);
            UpdateExtractionMarker();
        }

        private void OnBackpackButtonClick()
        {
            if (GameEntry.UI.HasUIForm(UIFormType.RoundBackpackForm))
            {
                UGuiForm backpackForm = GameEntry.UI.GetUIForm(UIFormType.RoundBackpackForm);
                if (backpackForm != null)
                {
                    GameEntry.UI.CloseUIForm(backpackForm);
                }
            }
            else
            {
                GameEntry.UI.OpenUIForm(UIFormType.RoundBackpackForm);
            }
        }

        private void UpdateRemainingTime(bool force)
        {
            GlobalConfig global = GameEntry.Luban.Global?.Data;
            if (global == null || GameEntry.TurnBattle == null)
            {
                return;
            }

            long elapsedMs = GameEntry.TurnBattle.RunElapsedMs;
            long remainingMs = Math.Max(0, global.RunTimeLimitMs - elapsedMs);
            int totalSeconds = (int)(remainingMs / 1000);

            if (!force && totalSeconds == _lastRemainingSeconds)
            {
                return;
            }

            _lastRemainingSeconds = totalSeconds;
            int minutes = totalSeconds / 60;
            int seconds = totalSeconds % 60;
            View.remainingTime.Set(minutes, seconds);
        }

        private void OnBackpackChanged(object sender, GameEventArgs e)
        {
            UpdateBackpackStats();
        }

        /// <summary>
        /// 保存主流程传入的撤离点与小地图视窗数据，标记位置由 UpdateExtractionMarker 逐帧刷新。
        /// </summary>
        private void ApplyExtractionMarker(object userData)
        {
            ExtractionMarkerData data = userData as ExtractionMarkerData;
            if (data == null)
            {
                View.extractionMarker.gameObject.SetActive(false);
                return;
            }

            if (data.MainCamera == null || data.ViewportSize.x <= 0f || data.ViewportSize.y <= 0f)
            {
                Log.Error("[RoundHUD] Extraction marker data is invalid.");
                View.extractionMarker.gameObject.SetActive(false);
                return;
            }

            _extractionMarkerData = data;
            View.extractionMarker.gameObject.SetActive(true);
            UpdateExtractionMarker();
        }

        /// <summary>
        /// 小地图纹理是跟随玩家的局部视窗：把撤离点相对视窗中心的偏移换算到标记层坐标，世界 +Y 对应层向上；
        /// 撤离点超出小地图范围时钉在层边缘，保持标记完整可见。
        /// </summary>
        private void UpdateExtractionMarker()
        {
            if (_extractionMarkerData == null)
            {
                return;
            }

            Vector3 center = _extractionMarkerData.MainCamera.transform.position;
            Vector2 delta = new Vector2(
                _extractionMarkerData.ExtractionPoint.x - center.x,
                _extractionMarkerData.ExtractionPoint.y - center.y);

            float layerWidth = View.minimapMarkers.rect.width;
            float layerHeight = View.minimapMarkers.rect.height;
            float markerWidth = View.extractionMarker.rect.width;
            float markerHeight = View.extractionMarker.rect.height;

            float centerX = (0.5f + delta.x / _extractionMarkerData.ViewportSize.x) * layerWidth;
            float centerFromTop = (0.5f - delta.y / _extractionMarkerData.ViewportSize.y) * layerHeight;

            centerX = Mathf.Clamp(centerX, markerWidth * 0.5f, layerWidth - markerWidth * 0.5f);
            centerFromTop = Mathf.Clamp(centerFromTop, markerHeight * 0.5f, layerHeight - markerHeight * 0.5f);

            // 标记 Pivot 为左上角，偏移半个标记尺寸使标记中心对准目标点
            View.extractionMarker.anchoredPosition = new Vector2(
                centerX - markerWidth * 0.5f,
                -(centerFromTop - markerHeight * 0.5f));
        }

        private void UpdateBackpackStats()
        {
            if (GameEntry.Round?.Session == null)
            {
                return;
            }

            RoundSession session = GameEntry.Round.Session;
            if (View.freeSlotsValue != null)
            {
                View.freeSlotsValue.Set(session.FreeBackpackSlots, session.Backpack.MaxSlots);
            }

            if (View.lootValueText != null)
            {
                FormatText format = GameEntry.Luban.Tables?.TbFormatText?.GetOrDefault("RoundHUDLootValue");
                int totalLoot = session.TotalLootValue;
                View.lootValueText.SetText(format != null ? string.Format(format.Format, totalLoot) : totalLoot.ToString("N0"));
            }
        }
    }
}
