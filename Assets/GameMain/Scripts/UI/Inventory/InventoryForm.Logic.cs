using System.Collections.Generic;
using SepCore.Definition;
using UnityEngine;
using UnityGameFramework.Runtime;

namespace SepCore.UI
{
    /// <summary>
    /// 物品列表界面逻辑（手写 partial，与自动生成的 InventoryForm.cs 合并）。
    /// 支持按物品类型筛选（ALL / 战利品 / 装备 / 消耗品）。
    /// 注意：本表单作为嵌套表单挂在 LobbyForm/WarehouseForm 下，UGF 生命周期（OnInit/OnOpen）
    /// 不会被框架调用，事件绑定改为幂等式（EnsureListenersBound）。
    /// </summary>
    public partial class InventoryForm : UGuiForm
    {
        private bool _listenersBound = false;
        private IReadOnlyList<ItemStack> _stacks = null;
        private ItemType? _filterType = null;
        private List<ItemStack> _visibleStacks = new List<ItemStack>();
        private readonly List<WarehouseSlotItem> _slots = new List<WarehouseSlotItem>();

        private void EnsureListenersBound()
        {
            if (_listenersBound)
            {
                return;
            }

            _listenersBound = true;
            View.allToggle.onValueChanged.AddListener(OnAllToggleValueChanged);
            View.lootToggle.onValueChanged.AddListener(OnLootToggleValueChanged);
            View.equipmentToggle.onValueChanged.AddListener(OnEquipmentToggleValueChanged);
            View.consumablesToggle.onValueChanged.AddListener(OnConsumablesToggleValueChanged);
        }

        private void OnAllToggleValueChanged(bool isOn)
        {
            if (isOn)
            {
                SetFilter(null);
            }
        }

        private void OnLootToggleValueChanged(bool isOn)
        {
            if (isOn)
            {
                SetFilter(ItemType.Loot);
            }
        }

        private void OnEquipmentToggleValueChanged(bool isOn)
        {
            if (isOn)
            {
                SetFilter(ItemType.Equipment);
            }
        }

        private void OnConsumablesToggleValueChanged(bool isOn)
        {
            if (isOn)
            {
                SetFilter(ItemType.Consumable);
            }
        }

        private void SetFilter(ItemType? filterType)
        {
            _filterType = filterType;
            RebuildGrid();
        }

        /// <summary>
        /// 设置列表数据并重建网格，保留当前筛选。
        /// </summary>
        public void RefreshList(IReadOnlyList<ItemStack> stacks)
        {
            EnsureListenersBound();

            _stacks = stacks;
            RebuildGrid();
        }

        private void RebuildGrid()
        {
            InventoryView inventoryView = View;
            int slotCount = GameEntry.Luban.Global.Data.WarehouseSlotCount;
            if (slotCount <= 0)
            {
                Log.Warning("Warehouse slot count from global config is invalid: {0}.", slotCount);
                return;
            }

            _visibleStacks = CollectVisibleStacks();
            _slots.Clear();

            WarehouseSlotItem template = inventoryView.warehouseSlotTemplate;
            template.gameObject.SetActive(false);

            for (int i = inventoryView.warehouseSlotRoot.childCount - 1; i >= 0; i--)
            {
                Transform child = inventoryView.warehouseSlotRoot.GetChild(i);
                if (child == template.transform)
                {
                    continue;
                }

                Destroy(child.gameObject);
            }

            for (int i = 0; i < slotCount; i++)
            {
                WarehouseSlotItem slot = Instantiate(template, inventoryView.warehouseSlotRoot);
                slot.gameObject.SetActive(true);
                slot.SetSlotId(i);
                if (i < _visibleStacks.Count)
                {
                    slot.SetItem(_visibleStacks[i]);
                }
                else
                {
                    slot.SetEmpty();
                }

                _slots.Add(slot);
            }

            int usedSlotCount = _stacks != null ? _stacks.Count : 0;
            inventoryView.itemCountFormatText.Set(usedSlotCount, slotCount);
        }

        /// <summary>
        /// 按网格索引高亮对应格子，其余格子清除选中。
        /// </summary>
        public void SetSelectedSlot(int slotId)
        {
            for (int i = 0; i < _slots.Count; i++)
            {
                _slots[i].SetSelected(i == slotId);
            }
        }

        /// <summary>
        /// 获取指定网格索引对应的物品 ID；空格或越界返回 0。
        /// </summary>
        public int GetItemIdAtSlot(int slotId)
        {
            if (slotId < 0 || slotId >= _visibleStacks.Count)
            {
                return 0;
            }

            return _visibleStacks[slotId].itemId;
        }

        private List<ItemStack> CollectVisibleStacks()
        {
            List<ItemStack> visible = new List<ItemStack>();
            if (_stacks == null)
            {
                return visible;
            }

            for (int i = 0; i < _stacks.Count; i++)
            {
                ItemStack stack = _stacks[i];
                if (_filterType.HasValue)
                {
                    ItemConfig config = GameEntry.Luban.Get<ItemConfig>(stack.itemId);
                    if (config == null || config.ItemType != _filterType.Value)
                    {
                        continue;
                    }
                }

                visible.Add(stack);
            }

            return visible;
        }
    }
}