using System;
using System.Collections.Generic;
using GameFramework.Event;
using SepCore.Base;
using SepCore.Definition;
using UnityEngine;
using UnityGameFramework.Runtime;

namespace SepCore.UI
{
    /// <summary>
    /// 角色备战界面逻辑（手写 partial，与自动生成的 LoadoutForm.cs 合并）。
    /// 负责管理角色列表展示与选择、从主仓库筛选武器与防具、
    /// 装备穿戴/替换与卸下（采用方案 A 主仓库流转），并驱动预览界面更新。
    /// </summary>
    public partial class LoadoutForm : UGuiForm
    {
        private int _selectedCharacterIndex = 0;
        private readonly List<CharacterSlotItem> _characterSlots = new List<CharacterSlotItem>();
        private readonly List<WarehouseSlotItem> _weaponSlots = new List<WarehouseSlotItem>();
        private readonly List<WarehouseSlotItem> _armorSlots = new List<WarehouseSlotItem>();
        private readonly List<ItemStack> _weaponStacks = new List<ItemStack>();
        private readonly List<ItemStack> _armorStacks = new List<ItemStack>();

        private bool _eventBound = false;
        private bool _previewBound = false;

        /// <summary>
        /// 刷新备战界面，重建角色列表、武器列表、防具列表并更新预览。
        /// </summary>
        public void Refresh()
        {
            EnsureEventBound();
            EnsurePreviewBound();

            SaveData save = GameEntry.Save.Data;
            if (save == null || save.characters == null || save.characters.Count == 0)
            {
                Log.Warning("Save data or characters are not available for LoadoutForm.");
                return;
            }

            if (_selectedCharacterIndex < 0 || _selectedCharacterIndex >= save.characters.Count)
            {
                _selectedCharacterIndex = 0;
            }

            RebuildCharacterList(save.characters);
            RebuildWeaponList(save.mainWarehouse);
            RebuildArmorList(save.mainWarehouse);
            RefreshPreview(save.characters);
        }

        private void EnsureEventBound()
        {
            if (_eventBound)
            {
                return;
            }

            _eventBound = true;
            GameEntry.Event.Subscribe(WarehouseSlotItemClickEventArgs.EventId, OnWarehouseSlotItemClick);
        }

        public void UnbindEvent()
        {
            if (!_eventBound)
            {
                return;
            }

            _eventBound = false;
            GameEntry.Event.Unsubscribe(WarehouseSlotItemClickEventArgs.EventId, OnWarehouseSlotItemClick);
        }

        private void EnsurePreviewBound()
        {
            if (_previewBound)
            {
                return;
            }

            _previewBound = true;
            View.characterPreviewForm.OnUnequipWeaponRequested += OnUnequipWeaponRequested;
            View.characterPreviewForm.OnUnequipArmorRequested += OnUnequipArmorRequested;
        }

        private void OnDisable()
        {
            UnbindEvent();
        }

        private void OnDestroy()
        {
            UnbindEvent();
            View.characterPreviewForm.OnUnequipWeaponRequested -= OnUnequipWeaponRequested;
            View.characterPreviewForm.OnUnequipArmorRequested -= OnUnequipArmorRequested;
        }

        private void RebuildCharacterList(List<CharacterSave> characters)
        {
            LoadoutView view = View;
            CharacterSlotItem template = view.characterSlotTemplate;
            template.gameObject.SetActive(false);

            for (int i = view.characterListRoot.childCount - 1; i >= 0; i--)
            {
                Transform child = view.characterListRoot.GetChild(i);
                if (child == template.transform)
                {
                    continue;
                }

                Destroy(child.gameObject);
            }

            _characterSlots.Clear();

            for (int i = 0; i < characters.Count; i++)
            {
                CharacterSlotItem slot = Instantiate(template, view.characterListRoot);
                slot.gameObject.SetActive(true);
                slot.SetCharacter(characters[i]);
                int index = i;
                slot.SetOnClick(() => OnCharacterSelected(index));
                slot.SetSelected(i == _selectedCharacterIndex);
                _characterSlots.Add(slot);
            }
        }

        private void OnCharacterSelected(int index)
        {
            SaveData save = GameEntry.Save.Data;
            if (save == null || save.characters == null || index < 0 || index >= save.characters.Count)
            {
                return;
            }

            _selectedCharacterIndex = index;
            for (int i = 0; i < _characterSlots.Count; i++)
            {
                _characterSlots[i].SetSelected(i == _selectedCharacterIndex);
            }

            RefreshPreview(save.characters);
        }

        private void RebuildWeaponList(List<ItemStack> warehouse)
        {
            LoadoutView view = View;
            WarehouseSlotItem template = view.weaponSlotTemplate;
            template.gameObject.SetActive(false);

            for (int i = view.weaponListRoot.childCount - 1; i >= 0; i--)
            {
                Transform child = view.weaponListRoot.GetChild(i);
                if (child == template.transform)
                {
                    continue;
                }

                Destroy(child.gameObject);
            }

            _weaponSlots.Clear();
            _weaponStacks.Clear();

            if (warehouse != null)
            {
                for (int i = 0; i < warehouse.Count; i++)
                {
                    ItemStack stack = warehouse[i];
                    if (stack.count <= 0)
                    {
                        continue;
                    }

                    ItemConfig config = GameEntry.Luban.Get<ItemConfig>(stack.itemId);
                    if (config != null && config.EquipSlot == EquipmentSlotType.Weapon)
                    {
                        _weaponStacks.Add(stack);
                    }
                }
            }

            for (int i = 0; i < _weaponStacks.Count; i++)
            {
                WarehouseSlotItem slot = Instantiate(template, view.weaponListRoot);
                slot.gameObject.SetActive(true);
                slot.SetSlotId(i);
                slot.SetItem(_weaponStacks[i]);
                _weaponSlots.Add(slot);
            }
        }

        private void RebuildArmorList(List<ItemStack> warehouse)
        {
            LoadoutView view = View;
            WarehouseSlotItem template = view.armorSlotTemplate;
            template.gameObject.SetActive(false);

            for (int i = view.armorListRoot.childCount - 1; i >= 0; i--)
            {
                Transform child = view.armorListRoot.GetChild(i);
                if (child == template.transform)
                {
                    continue;
                }

                Destroy(child.gameObject);
            }

            _armorSlots.Clear();
            _armorStacks.Clear();

            if (warehouse != null)
            {
                for (int i = 0; i < warehouse.Count; i++)
                {
                    ItemStack stack = warehouse[i];
                    if (stack.count <= 0)
                    {
                        continue;
                    }

                    ItemConfig config = GameEntry.Luban.Get<ItemConfig>(stack.itemId);
                    if (config != null && config.EquipSlot == EquipmentSlotType.Armor)
                    {
                        _armorStacks.Add(stack);
                    }
                }
            }

            for (int i = 0; i < _armorStacks.Count; i++)
            {
                WarehouseSlotItem slot = Instantiate(template, view.armorListRoot);
                slot.gameObject.SetActive(true);
                slot.SetSlotId(i);
                slot.SetItem(_armorStacks[i]);
                _armorSlots.Add(slot);
            }
        }

        private void RefreshPreview(List<CharacterSave> characters)
        {
            if (characters == null || _selectedCharacterIndex < 0 || _selectedCharacterIndex >= characters.Count)
            {
                return;
            }

            View.characterPreviewForm.Refresh(characters[_selectedCharacterIndex]);
        }

        private void OnWarehouseSlotItemClick(object sender, GameEventArgs e)
        {
            if (this == null || !gameObject.activeInHierarchy)
            {
                return;
            }

            WarehouseSlotItem slot = sender as WarehouseSlotItem;
            if (slot == null)
            {
                return;
            }

            WarehouseSlotItemClickEventArgs ne = (WarehouseSlotItemClickEventArgs)e;
            int slotId = ne.SlotId;

            if (_weaponSlots.Contains(slot))
            {
                OnWeaponSlotClicked(slotId);
            }
            else if (_armorSlots.Contains(slot))
            {
                OnArmorSlotClicked(slotId);
            }
        }

        private void OnWeaponSlotClicked(int slotId)
        {
            SaveData save = GameEntry.Save.Data;
            if (save == null || save.characters == null ||
                _selectedCharacterIndex < 0 || _selectedCharacterIndex >= save.characters.Count)
            {
                return;
            }

            if (slotId < 0 || slotId >= _weaponStacks.Count)
            {
                return;
            }

            int newWeaponId = _weaponStacks[slotId].itemId;
            CharacterSave character = save.characters[_selectedCharacterIndex];
            int oldWeaponId = character.weaponItemId;

            if (oldWeaponId == newWeaponId)
            {
                return;
            }

            if (oldWeaponId > 0)
            {
                AddToWarehouse(save.mainWarehouse, oldWeaponId, 1);
            }

            if (!RemoveFromWarehouse(save.mainWarehouse, newWeaponId, 1))
            {
                Log.Warning("Failed to remove weapon '{0}' from mainWarehouse.", newWeaponId);
                return;
            }

            character.weaponItemId = newWeaponId;
            save.characters[_selectedCharacterIndex] = character;

            RebuildWeaponList(save.mainWarehouse);
            RefreshPreview(save.characters);
        }

        private void OnArmorSlotClicked(int slotId)
        {
            SaveData save = GameEntry.Save.Data;
            if (save == null || save.characters == null ||
                _selectedCharacterIndex < 0 || _selectedCharacterIndex >= save.characters.Count)
            {
                return;
            }

            if (slotId < 0 || slotId >= _armorStacks.Count)
            {
                return;
            }

            int newArmorId = _armorStacks[slotId].itemId;
            CharacterSave character = save.characters[_selectedCharacterIndex];
            int oldArmorId = character.armorItemId;

            if (oldArmorId == newArmorId)
            {
                return;
            }

            if (oldArmorId > 0)
            {
                AddToWarehouse(save.mainWarehouse, oldArmorId, 1);
            }

            if (!RemoveFromWarehouse(save.mainWarehouse, newArmorId, 1))
            {
                Log.Warning("Failed to remove armor '{0}' from mainWarehouse.", newArmorId);
                return;
            }

            character.armorItemId = newArmorId;
            save.characters[_selectedCharacterIndex] = character;

            RebuildArmorList(save.mainWarehouse);
            RefreshPreview(save.characters);
        }

        private void OnUnequipWeaponRequested()
        {
            SaveData save = GameEntry.Save.Data;
            if (save == null || save.characters == null ||
                _selectedCharacterIndex < 0 || _selectedCharacterIndex >= save.characters.Count)
            {
                return;
            }

            CharacterSave character = save.characters[_selectedCharacterIndex];
            if (character.weaponItemId <= 0)
            {
                return;
            }

            int oldWeaponId = character.weaponItemId;
            AddToWarehouse(save.mainWarehouse, oldWeaponId, 1);
            character.weaponItemId = 0;
            save.characters[_selectedCharacterIndex] = character;

            RebuildWeaponList(save.mainWarehouse);
            RefreshPreview(save.characters);
        }

        private void OnUnequipArmorRequested()
        {
            SaveData save = GameEntry.Save.Data;
            if (save == null || save.characters == null ||
                _selectedCharacterIndex < 0 || _selectedCharacterIndex >= save.characters.Count)
            {
                return;
            }

            CharacterSave character = save.characters[_selectedCharacterIndex];
            if (character.armorItemId <= 0)
            {
                return;
            }

            int oldArmorId = character.armorItemId;
            AddToWarehouse(save.mainWarehouse, oldArmorId, 1);
            character.armorItemId = 0;
            save.characters[_selectedCharacterIndex] = character;

            RebuildArmorList(save.mainWarehouse);
            RefreshPreview(save.characters);
        }

        private static void AddToWarehouse(List<ItemStack> warehouse, int itemId, int count)
        {
            if (warehouse == null || itemId <= 0 || count <= 0)
            {
                return;
            }

            ItemConfig config = GameEntry.Luban.Get<ItemConfig>(itemId);
            int stackLimit = config != null && config.StackLimit > 0 ? config.StackLimit : 1;
            int remaining = count;

            for (int i = 0; i < warehouse.Count; i++)
            {
                if (warehouse[i].itemId == itemId && warehouse[i].count < stackLimit)
                {
                    int space = stackLimit - warehouse[i].count;
                    int toAdd = Math.Min(space, remaining);
                    ItemStack s = warehouse[i];
                    s.count += toAdd;
                    warehouse[i] = s;
                    remaining -= toAdd;
                    if (remaining <= 0)
                    {
                        return;
                    }
                }
            }

            while (remaining > 0)
            {
                int toAdd = Math.Min(stackLimit, remaining);
                warehouse.Add(new ItemStack(itemId, toAdd));
                remaining -= toAdd;
            }
        }

        private static bool RemoveFromWarehouse(List<ItemStack> warehouse, int itemId, int count)
        {
            if (warehouse == null || itemId <= 0 || count <= 0)
            {
                return false;
            }

            for (int i = 0; i < warehouse.Count; i++)
            {
                if (warehouse[i].itemId == itemId)
                {
                    if (warehouse[i].count > count)
                    {
                        ItemStack s = warehouse[i];
                        s.count -= count;
                        warehouse[i] = s;
                        return true;
                    }
                    else
                    {
                        count -= warehouse[i].count;
                        warehouse.RemoveAt(i);
                        i--;
                        if (count <= 0)
                        {
                            return true;
                        }
                    }
                }
            }

            return false;
        }
    }
}
