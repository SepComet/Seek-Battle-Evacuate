using System.Collections.Generic;
using SepCore.Definition;
using SepCore.Run;
using UnityEngine;

namespace SepCore.UI
{
    /// <summary>
    /// 背包与保险箱容器面板逻辑（手写 partial，与自动生成的 BackpackForm.cs 合并）。
    /// 作为嵌套表单被顶级表单（RoundBackpackForm）编排，管理共享背包与保险箱网格槽位渲染及容量提示。
    /// </summary>
    public partial class BackpackForm : UGuiForm
    {
        private readonly List<WarehouseSlotItem> _backpackSlots = new List<WarehouseSlotItem>();
        private readonly List<WarehouseSlotItem> _safeCaseSlots = new List<WarehouseSlotItem>();

        private bool _selectedIsBackpack = true;
        private int _selectedSlotIndex = -1;

        /// <summary>
        /// 刷新背包与保险箱网格数据及容量。
        /// </summary>
        public void Refresh(RoundItemContainer backpack, RoundItemContainer safeCase)
        {
            if (backpack == null || safeCase == null)
            {
                return;
            }

            EnsureBackpackSlotsCreated(backpack.MaxSlots);
            for (int i = 0; i < backpack.MaxSlots; i++)
            {
                WarehouseSlotItem slot = _backpackSlots[i];
                ItemStack stack = backpack.Slots[i];
                if (stack.itemId > 0 && stack.count > 0)
                {
                    slot.SetItem(stack);
                }
                else
                {
                    slot.SetEmpty();
                }

                slot.SetSelected(_selectedIsBackpack && _selectedSlotIndex == i);
            }

            EnsureSafeCaseSlotsCreated(safeCase.MaxSlots);
            for (int i = 0; i < safeCase.MaxSlots; i++)
            {
                WarehouseSlotItem slot = _safeCaseSlots[i];
                ItemStack stack = safeCase.Slots[i];
                if (stack.itemId > 0 && stack.count > 0)
                {
                    slot.SetItem(stack);
                }
                else
                {
                    slot.SetEmpty();
                }

                slot.SetSelected(!_selectedIsBackpack && _selectedSlotIndex == i);
            }

            View.safeCapacityFormatText.Set(safeCase.UsedSlotsCount, safeCase.MaxSlots);
        }

        /// <summary>
        /// 设置当前选中的槽位高亮。
        /// </summary>
        public void SetSelectedSlot(bool isBackpack, int slotIndex)
        {
            _selectedIsBackpack = isBackpack;
            _selectedSlotIndex = slotIndex;

            for (int i = 0; i < _backpackSlots.Count; i++)
            {
                _backpackSlots[i].SetSelected(isBackpack && i == slotIndex);
            }

            for (int i = 0; i < _safeCaseSlots.Count; i++)
            {
                _safeCaseSlots[i].SetSelected(!isBackpack && i == slotIndex);
            }
        }

        /// <summary>
        /// 清除所有槽位选中高亮。
        /// </summary>
        public void ClearSelection()
        {
            _selectedSlotIndex = -1;

            for (int i = 0; i < _backpackSlots.Count; i++)
            {
                _backpackSlots[i].SetSelected(false);
            }

            for (int i = 0; i < _safeCaseSlots.Count; i++)
            {
                _safeCaseSlots[i].SetSelected(false);
            }
        }

        /// <summary>
        /// 判断指定格子是否属于本面板，并输出其容器类型与下标。
        /// </summary>
        public bool TryGetSlotItem(WarehouseSlotItem item, out bool isBackpack, out int slotIndex)
        {
            isBackpack = false;
            slotIndex = -1;

            if (item == null)
            {
                return false;
            }

            int bpIndex = _backpackSlots.IndexOf(item);
            if (bpIndex >= 0)
            {
                isBackpack = true;
                slotIndex = bpIndex;
                return true;
            }

            int scIndex = _safeCaseSlots.IndexOf(item);
            if (scIndex >= 0)
            {
                isBackpack = false;
                slotIndex = scIndex;
                return true;
            }

            return false;
        }

        private void EnsureBackpackSlotsCreated(int maxSlots)
        {
            if (_backpackSlots.Count >= maxSlots)
            {
                return;
            }

            WarehouseSlotItem template = View.backpackSlotTemplate;
            template.gameObject.SetActive(false);

            while (_backpackSlots.Count < maxSlots)
            {
                int index = _backpackSlots.Count;
                WarehouseSlotItem slot = Instantiate(template, View.backpackSlotRoot);
                slot.gameObject.SetActive(true);
                slot.SetSlotId(index);
                _backpackSlots.Add(slot);
            }
        }

        private void EnsureSafeCaseSlotsCreated(int maxSlots)
        {
            if (_safeCaseSlots.Count >= maxSlots)
            {
                return;
            }

            WarehouseSlotItem template = View.safeCaseSlotTemplate;
            Transform root = template.transform.parent;
            template.gameObject.SetActive(false);

            while (_safeCaseSlots.Count < maxSlots)
            {
                int index = _safeCaseSlots.Count;
                WarehouseSlotItem slot = Instantiate(template, root);
                slot.gameObject.SetActive(true);
                slot.SetSlotId(1000 + index);
                _safeCaseSlots.Add(slot);
            }
        }
    }
}
