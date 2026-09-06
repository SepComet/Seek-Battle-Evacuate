using System;
using GameFramework.Event;
using SepCore.Base;
using SepCore.Definition;
using SepCore.Run;
using UnityGameFramework.Runtime;

namespace SepCore.UI
{
    /// <summary>
    /// 局内 HUD 界面逻辑（手写 partial，与自动生成的 RoundHUDForm.cs 合并）。
    /// 负责探索主界面 HUD 上的基础交互：剩余探索时间轮询刷新、背包面板打开/收起切换与背包状态（剩余槽位/总战利品）展示。
    /// </summary>
    public partial class RoundHUDForm : UGuiForm
    {
        private int _lastRemainingSeconds = -1;

        protected override void OnOpen(object userData)
        {
            base.OnOpen(userData);

            View.backpackButton.onClick.AddListener(OnBackpackButtonClick);
            GameEntry.Event.Subscribe(RoundBackpackChangedEventArgs.EventId, OnBackpackChanged);

            _lastRemainingSeconds = -1;
            UpdateRemainingTime(force: true);
            UpdateBackpackStats();
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
