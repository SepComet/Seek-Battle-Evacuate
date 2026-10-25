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
        private int _overrideDetailItemId = 0;
        private int _lastRemainingSeconds = -1;

        protected override void OnOpen(object userData)
        {
            base.OnOpen(userData);

            CharacterInputBridge.DisableInput(InputDisableReason.Backpack);

            View.closeButton.onClick.AddListener(OnCloseButtonClick);
            View.characterForm.OnMoveClicked += OnMoveClicked;
            View.characterForm.OnThrowClicked += OnThrowClicked;
            View.characterForm.OnUnequipWeaponRequested += OnUnequipWeaponRequested;
            View.characterForm.OnUnequipArmorRequested += OnUnequipArmorRequested;

            GameEntry.Event.Subscribe(RoundBackpackChangedEventArgs.EventId, OnBackpackChanged);
            GameEntry.Event.Subscribe(RoundSafeCaseChangedEventArgs.EventId, OnSafeCaseChanged);
            GameEntry.Event.Subscribe(RoundPartyStateChangedEventArgs.EventId, OnPartyStateChanged);
            GameEntry.Event.Subscribe(WarehouseSlotItemClickEventArgs.EventId, OnSlotItemClick);

            _selectedSlotIndex = -1;
            _overrideDetailItemId = 0;
            _lastRemainingSeconds = -1;

            RefreshAll();
            UpdateRemainingTime(force: true);
        }

        protected override void OnClose(bool isShutdown, object userData)
        {
            View.closeButton.onClick.RemoveListener(OnCloseButtonClick);
            View.characterForm.OnMoveClicked -= OnMoveClicked;
            View.characterForm.OnThrowClicked -= OnThrowClicked;
            View.characterForm.OnUnequipWeaponRequested -= OnUnequipWeaponRequested;
            View.characterForm.OnUnequipArmorRequested -= OnUnequipArmorRequested;

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
            if (clickedSlot == null || GameEntry.Round?.Session == null)
            {
                return;
            }

            if (!View.backpackForm.TryGetSlotItem(clickedSlot, out bool isBackpack, out int slotIndex))
            {
                return;
            }

            RoundSession session = GameEntry.Round.Session;
            RoundItemContainer container = isBackpack ? session.Backpack : session.SafeCase;
            if (slotIndex < 0 || slotIndex >= container.Slots.Count)
            {
                return;
            }

            ItemStack stack = container.Slots[slotIndex];
            if (stack.itemId <= 0 || stack.count <= 0)
            {
                _overrideDetailItemId = 0;
                _selectedIsBackpack = isBackpack;
                _selectedSlotIndex = slotIndex;
                View.backpackForm.SetSelectedSlot(isBackpack, slotIndex);
                UpdateSelectedItemDetail();
                return;
            }

            ItemConfig itemConfig = GameEntry.Luban.Get<ItemConfig>(stack.itemId);
            int charIndex = View.characterForm.SelectedCharacterIndex;
            RoundCharacterState character = (session.Party != null && charIndex >= 0 && charIndex < session.Party.Count)
                ? session.Party[charIndex]
                : null;

            bool isEquipment = itemConfig != null &&
                               (itemConfig.EquipSlot == EquipmentSlotType.Weapon || itemConfig.EquipSlot == EquipmentSlotType.Armor);

            bool canEquip = false;
            if (isEquipment && character != null)
            {
                if (itemConfig.EquipSlot == EquipmentSlotType.Weapon && character.WeaponItemId == 0)
                {
                    canEquip = true;
                }
                else if (itemConfig.EquipSlot == EquipmentSlotType.Armor && character.ArmorItemId == 0)
                {
                    canEquip = true;
                }
            }

            if (canEquip)
            {
                int equippedItemId = stack.itemId;
                if (GameEntry.Round.TryEquipFromContainer(charIndex, isBackpack, slotIndex))
                {
                    View.backpackForm.Refresh(session.Backpack, session.SafeCase);
                    View.characterForm.RefreshParty(session.Party);

                    ItemStack remainingStack = container.Slots[slotIndex];
                    if (remainingStack.itemId > 0 && remainingStack.count > 0)
                    {
                        _overrideDetailItemId = 0;
                        _selectedIsBackpack = isBackpack;
                        _selectedSlotIndex = slotIndex;
                        View.backpackForm.SetSelectedSlot(isBackpack, slotIndex);
                        UpdateSelectedItemDetail();
                    }
                    else
                    {
                        _selectedSlotIndex = -1;
                        View.backpackForm.ClearSelection();
                        _overrideDetailItemId = equippedItemId;
                        UpdateSelectedItemDetail();
                    }
                    return;
                }
            }

            // 非装备或对应槽位已有装备（不替换），仅选中该格子并在右侧展示详情
            _overrideDetailItemId = 0;
            _selectedIsBackpack = isBackpack;
            _selectedSlotIndex = slotIndex;
            View.backpackForm.SetSelectedSlot(isBackpack, slotIndex);
            UpdateSelectedItemDetail();
        }

        private void OnUnequipWeaponRequested()
        {
            HandleUnequip(EquipmentSlotType.Weapon);
        }

        private void OnUnequipArmorRequested()
        {
            HandleUnequip(EquipmentSlotType.Armor);
        }

        private void HandleUnequip(EquipmentSlotType slotType)
        {
            if (GameEntry.Round?.Session == null)
            {
                return;
            }

            int charIndex = View.characterForm.SelectedCharacterIndex;
            if (GameEntry.Round.TryUnequipToContainer(charIndex, slotType, out bool toBackpack, out int targetSlotIndex))
            {
                _overrideDetailItemId = 0;
                _selectedIsBackpack = toBackpack;
                _selectedSlotIndex = targetSlotIndex;

                RoundSession session = GameEntry.Round.Session;
                View.backpackForm.Refresh(session.Backpack, session.SafeCase);
                View.characterForm.RefreshParty(session.Party);
                View.backpackForm.SetSelectedSlot(toBackpack, targetSlotIndex);
                UpdateSelectedItemDetail();
            }
        }

        private void OnMoveClicked()
        {
            if (_selectedSlotIndex < 0 || GameEntry.Round?.Session == null)
            {
                return;
            }

            ItemStack stack = GetSelectedStack();
            if (stack.itemId <= 0 || stack.count <= 0)
            {
                return;
            }

            bool fromBackpackToSafe = _selectedIsBackpack;
            if (GameEntry.Round.MoveBetweenBackpackAndSafe(fromBackpackToSafe, _selectedSlotIndex, stack.count, out int movedCount, out int targetSlotIndex))
            {
                RoundSession session = GameEntry.Round.Session;
                View.backpackForm.Refresh(session.Backpack, session.SafeCase);

                // 检查原槽位是否还有剩余
                ItemStack remainingSourceStack = GetSelectedStack();
                if (remainingSourceStack.itemId > 0 && remainingSourceStack.count > 0)
                {
                    View.backpackForm.SetSelectedSlot(_selectedIsBackpack, _selectedSlotIndex);
                }
                else if (targetSlotIndex >= 0)
                {
                    // 原格已空，焦点切至目标容器对应格子
                    _selectedIsBackpack = !fromBackpackToSafe;
                    _selectedSlotIndex = targetSlotIndex;
                    View.backpackForm.SetSelectedSlot(_selectedIsBackpack, targetSlotIndex);
                }
                else
                {
                    _selectedSlotIndex = -1;
                    View.backpackForm.ClearSelection();
                }

                _overrideDetailItemId = 0;
                UpdateSelectedItemDetail();
            }
        }

        private void OnThrowClicked()
        {
            if (_selectedSlotIndex < 0 || GameEntry.Round?.Session == null)
            {
                return;
            }

            if (GameEntry.Round.DiscardItemFromContainer(_selectedIsBackpack, _selectedSlotIndex))
            {
                RoundSession session = GameEntry.Round.Session;
                View.backpackForm.Refresh(session.Backpack, session.SafeCase);
                _selectedSlotIndex = -1;
                _overrideDetailItemId = 0;
                View.backpackForm.ClearSelection();
                UpdateSelectedItemDetail();
            }
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
            if (_selectedSlotIndex >= 0 && GameEntry.Round?.Session != null)
            {
                _overrideDetailItemId = 0;
                ItemStack stack = GetSelectedStack();
                if (stack.itemId > 0 && stack.count > 0)
                {
                    RoundItemContainer targetContainer = _selectedIsBackpack
                        ? GameEntry.Round.Session.SafeCase
                        : GameEntry.Round.Session.Backpack;

                    bool canMove = targetContainer != null && targetContainer.CanAcceptItem(stack.itemId, 1);
                    bool canThrow = true;

                    View.characterForm.RefreshSelectedItem(stack, canMove, canThrow);
                    return;
                }
            }

            if (_overrideDetailItemId > 0)
            {
                View.characterForm.RefreshSelectedItem(new ItemStack(_overrideDetailItemId, 1), canMove: false, canThrow: false);
                return;
            }

            View.characterForm.ClearSelectedItem();
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
            View.timerText.SetText($"{minutes:00}:{seconds:00}");
        }
    }
}
