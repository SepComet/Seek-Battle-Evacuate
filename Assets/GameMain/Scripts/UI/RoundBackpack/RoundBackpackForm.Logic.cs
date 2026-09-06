using System;
using GameFramework.Event;
using SepCore.Base;
using SepCore.Definition;
using SepCore.Exploration;
using SepCore.Run;
using UnityGameFramework.Runtime;

namespace SepCore.UI
{
    /// <summary>
    /// 局内背包顶级界面逻辑（手写 partial，与自动生成的 RoundBackpackForm.cs 合并）。
    /// 作为顶级表单负责生命周期调度、探索输入禁用、倒计时同步，并编排子表单 BackpackForm 与 CharacterForm。
    /// </summary>
    public partial class RoundBackpackForm : UGuiForm
    {
        private bool _selectedIsBackpack = true;
        private int _selectedSlotIndex = -1;
        private int _lastRemainingSeconds = -1;

        protected override void OnOpen(object userData)
        {
            base.OnOpen(userData);

            CharacterInputBridge.DisableInput(InputDisableReason.Backpack);

            View.closeButton.onClick.AddListener(OnCloseButtonClick);
            View.characterForm.SetOnMoveToSafeClicked(OnMoveToSafeClicked);

            GameEntry.Event.Subscribe(RoundBackpackChangedEventArgs.EventId, OnBackpackChanged);
            GameEntry.Event.Subscribe(RoundSafeCaseChangedEventArgs.EventId, OnSafeCaseChanged);
            GameEntry.Event.Subscribe(RoundPartyStateChangedEventArgs.EventId, OnPartyStateChanged);
            GameEntry.Event.Subscribe(WarehouseSlotItemClickEventArgs.EventId, OnSlotItemClick);

            _selectedSlotIndex = -1;
            _lastRemainingSeconds = -1;

            RefreshAll();
            UpdateRemainingTime(force: true);
        }

        protected override void OnClose(bool isShutdown, object userData)
        {
            View.closeButton.onClick.RemoveListener(OnCloseButtonClick);

            GameEntry.Event.Unsubscribe(RoundBackpackChangedEventArgs.EventId, OnBackpackChanged);
            GameEntry.Event.Unsubscribe(RoundSafeCaseChangedEventArgs.EventId, OnSafeCaseChanged);
            GameEntry.Event.Unsubscribe(RoundPartyStateChangedEventArgs.EventId, OnPartyStateChanged);
            GameEntry.Event.Unsubscribe(WarehouseSlotItemClickEventArgs.EventId, OnSlotItemClick);

            CharacterInputBridge.EnableInput(InputDisableReason.Backpack);

            base.OnClose(isShutdown, userData);
        }

        protected override void OnUpdate(float elapseSeconds, float realElapseSeconds)
        {
            base.OnUpdate(elapseSeconds, realElapseSeconds);

            UpdateRemainingTime(force: false);
        }

        private void OnCloseButtonClick()
        {
            Close();
        }

        private void RefreshAll()
        {
            if (GameEntry.Round?.Session == null)
            {
                return;
            }

            RoundSession session = GameEntry.Round.Session;
            View.backpackForm.Refresh(session.Backpack, session.SafeCase);
            View.characterForm.RefreshParty(session.Party);
            UpdateSelectedItemDetail();
        }

        private void OnBackpackChanged(object sender, GameEventArgs e)
        {
            if (GameEntry.Round?.Session == null)
            {
                return;
            }

            RoundSession session = GameEntry.Round.Session;
            View.backpackForm.Refresh(session.Backpack, session.SafeCase);
            UpdateSelectedItemDetail();
        }

        private void OnSafeCaseChanged(object sender, GameEventArgs e)
        {
            if (GameEntry.Round?.Session == null)
            {
                return;
            }

            RoundSession session = GameEntry.Round.Session;
            View.backpackForm.Refresh(session.Backpack, session.SafeCase);
            UpdateSelectedItemDetail();
        }

        private void OnPartyStateChanged(object sender, GameEventArgs e)
        {
            if (GameEntry.Round?.Session == null)
            {
                return;
            }

            View.characterForm.RefreshParty(GameEntry.Round.Session.Party);
        }

        private void OnSlotItemClick(object sender, GameEventArgs e)
        {
            WarehouseSlotItem clickedSlot = sender as WarehouseSlotItem;
            if (clickedSlot == null)
            {
                return;
            }

            if (View.backpackForm.TryGetSlotItem(clickedSlot, out bool isBackpack, out int slotIndex))
            {
                _selectedIsBackpack = isBackpack;
                _selectedSlotIndex = slotIndex;
                View.backpackForm.SetSelectedSlot(isBackpack, slotIndex);
                UpdateSelectedItemDetail();
            }
        }

        private void OnMoveToSafeClicked()
        {
            if (!_selectedIsBackpack || _selectedSlotIndex < 0 || GameEntry.Round?.Session == null)
            {
                return;
            }

            ItemStack stack = GetSelectedStack();
            if (stack.itemId <= 0 || stack.count <= 0)
            {
                return;
            }

            GameEntry.Round.MoveBetweenBackpackAndSafe(fromBackpackToSafe: true, _selectedSlotIndex, stack.count, out _);
            UpdateSelectedItemDetail();
        }

        private ItemStack GetSelectedStack()
        {
            if (GameEntry.Round?.Session == null || _selectedSlotIndex < 0)
            {
                return default;
            }

            RoundItemContainer container = _selectedIsBackpack
                ? GameEntry.Round.Session.Backpack
                : GameEntry.Round.Session.SafeCase;

            if (_selectedSlotIndex >= container.Slots.Count)
            {
                return default;
            }

            return container.Slots[_selectedSlotIndex];
        }

        private void UpdateSelectedItemDetail()
        {
            if (_selectedSlotIndex < 0 || GameEntry.Round?.Session == null)
            {
                View.characterForm.ClearSelectedItem();
                return;
            }

            ItemStack stack = GetSelectedStack();
            if (stack.itemId <= 0 || stack.count <= 0)
            {
                View.characterForm.ClearSelectedItem();
                return;
            }

            bool canMoveToSafe = _selectedIsBackpack && !GameEntry.Round.Session.SafeCase.IsFull;
            View.characterForm.RefreshSelectedItem(stack, _selectedIsBackpack, canMoveToSafe);
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
            View.timerText.SetText(string.Format("{0:00}:{1:00}", minutes, seconds));
        }
    }
}
