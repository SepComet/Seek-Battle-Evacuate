using System;
using System.Collections.Generic;
using SepCore.Definition;

namespace SepCore.Run
{
    /// <summary>
    /// 单局物品容器（共享背包、保险箱等）。
    /// 固定槽位数，基于配表 ItemConfig.StackLimit 执行智能堆叠、拆分与溢出管理。
    /// </summary>
    public sealed class RoundItemContainer
    {
        private readonly int _maxSlots;
        private readonly ItemStack[] _slots;
        private readonly Func<int, ItemConfig> _itemConfigGetter;

        public int MaxSlots => _maxSlots;

        public IReadOnlyList<ItemStack> Slots => _slots;

        public RoundItemContainer(int maxSlots, Func<int, ItemConfig> itemConfigGetter)
        {
            if (maxSlots < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maxSlots), "MaxSlots cannot be negative.");
            }

            _maxSlots = maxSlots;
            _slots = new ItemStack[_maxSlots];
            _itemConfigGetter = itemConfigGetter ?? throw new ArgumentNullException(nameof(itemConfigGetter));
        }

        /// <summary>
        /// 当前已占用的有效槽位数量。
        /// </summary>
        public int UsedSlotsCount
        {
            get
            {
                int count = 0;
                for (int i = 0; i < _slots.Length; i++)
                {
                    if (_slots[i].itemId > 0 && _slots[i].count > 0)
                    {
                        count++;
                    }
                }
                return count;
            }
        }

        /// <summary>
        /// 剩余可用空槽位数量。
        /// </summary>
        public int FreeSlotsCount => _maxSlots - UsedSlotsCount;

        /// <summary>
        /// 是否已无任何空槽位。
        /// </summary>
        public bool IsFull => FreeSlotsCount <= 0;

        /// <summary>
        /// 容器内所有物品按配表价值（ItemConfig.Value）累加的总估值。
        /// </summary>
        public int TotalValue
        {
            get
            {
                int sum = 0;
                for (int i = 0; i < _slots.Length; i++)
                {
                    if (_slots[i].itemId > 0 && _slots[i].count > 0)
                    {
                        ItemConfig config = _itemConfigGetter(_slots[i].itemId);
                        if (config != null)
                        {
                            sum += config.Value * _slots[i].count;
                        }
                    }
                }
                return sum;
            }
        }

        /// <summary>
        /// 尝试将指定数量的物品加入容器。
        /// 优先寻找未满的同类物品堆叠，多余部分填入首个可用空槽位。
        /// </summary>
        /// <param name="itemId">物品配置 ID。</param>
        /// <param name="count">欲放入数量。</param>
        /// <returns>实际成功放入容器的数量。</returns>
        public int TryAddItem(int itemId, int count)
        {
            if (itemId <= 0 || count <= 0)
            {
                return 0;
            }

            ItemConfig config = _itemConfigGetter(itemId);
            int stackLimit = config != null && config.StackLimit > 0 ? config.StackLimit : 1;

            int remaining = count;

            // 阶段 1：先向已有同类未满格进行堆叠
            for (int i = 0; i < _slots.Length; i++)
            {
                if (_slots[i].itemId == itemId && _slots[i].count > 0 && _slots[i].count < stackLimit)
                {
                    int space = stackLimit - _slots[i].count;
                    int toAdd = Math.Min(space, remaining);
                    _slots[i].count += toAdd;
                    remaining -= toAdd;

                    if (remaining <= 0)
                    {
                        return count;
                    }
                }
            }

            // 阶段 2：填入空槽位
            for (int i = 0; i < _slots.Length; i++)
            {
                if (_slots[i].itemId <= 0 || _slots[i].count <= 0)
                {
                    int toAdd = Math.Min(stackLimit, remaining);
                    _slots[i] = new ItemStack(itemId, toAdd);
                    remaining -= toAdd;

                    if (remaining <= 0)
                    {
                        return count;
                    }
                }
            }

            return count - remaining;
        }

        /// <summary>
        /// 扣减或移除指定槽位中的物品。
        /// </summary>
        /// <param name="slotIndex">槽位索引（0 ~ MaxSlots - 1）。</param>
        /// <param name="count">扣减数量。</param>
        /// <param name="removedCount">实际扣减数量。</param>
        /// <returns>是否成功执行扣减。</returns>
        public bool TryRemoveItemAt(int slotIndex, int count, out int removedCount)
        {
            removedCount = 0;
            if (slotIndex < 0 || slotIndex >= _slots.Length || count <= 0)
            {
                return false;
            }

            if (_slots[slotIndex].itemId <= 0 || _slots[slotIndex].count <= 0)
            {
                return false;
            }

            int toRemove = Math.Min(count, _slots[slotIndex].count);
            _slots[slotIndex].count -= toRemove;
            removedCount = toRemove;

            if (_slots[slotIndex].count <= 0)
            {
                _slots[slotIndex] = default;
            }

            return true;
        }

        /// <summary>
        /// 交换两个槽位的内容。若两槽位为同类物品且未达上限，执行合并堆叠。
        /// </summary>
        public bool SwapSlots(int slotA, int slotB)
        {
            if (slotA < 0 || slotA >= _slots.Length || slotB < 0 || slotB >= _slots.Length)
            {
                return false;
            }

            if (slotA == slotB)
            {
                return true;
            }

            // 同类物品合并
            if (_slots[slotA].itemId > 0 && _slots[slotA].itemId == _slots[slotB].itemId)
            {
                ItemConfig config = _itemConfigGetter(_slots[slotA].itemId);
                int stackLimit = config != null && config.StackLimit > 0 ? config.StackLimit : 1;

                if (_slots[slotB].count < stackLimit)
                {
                    int space = stackLimit - _slots[slotB].count;
                    int toMerge = Math.Min(space, _slots[slotA].count);
                    _slots[slotB].count += toMerge;
                    _slots[slotA].count -= toMerge;

                    if (_slots[slotA].count <= 0)
                    {
                        _slots[slotA] = default;
                    }
                    return true;
                }
            }

            // 常规交换
            ItemStack temp = _slots[slotA];
            _slots[slotA] = _slots[slotB];
            _slots[slotB] = temp;
            return true;
        }

        /// <summary>
        /// 将指定槽位中的物品转移至目标容器。
        /// </summary>
        public bool TryMoveTo(int fromSlotIndex, RoundItemContainer targetContainer, int count, out int movedCount)
        {
            movedCount = 0;
            if (targetContainer == null || fromSlotIndex < 0 || fromSlotIndex >= _slots.Length || count <= 0)
            {
                return false;
            }

            if (_slots[fromSlotIndex].itemId <= 0 || _slots[fromSlotIndex].count <= 0)
            {
                return false;
            }

            int itemId = _slots[fromSlotIndex].itemId;
            int available = Math.Min(count, _slots[fromSlotIndex].count);

            int accepted = targetContainer.TryAddItem(itemId, available);
            if (accepted > 0)
            {
                _slots[fromSlotIndex].count -= accepted;
                movedCount = accepted;

                if (_slots[fromSlotIndex].count <= 0)
                {
                    _slots[fromSlotIndex] = default;
                }
                return true;
            }

            return false;
        }

        /// <summary>
        /// 直接设置指定槽位的数据（用于初始化或单元测试）。
        /// </summary>
        public void SetSlot(int slotIndex, ItemStack stack)
        {
            if (slotIndex >= 0 && slotIndex < _slots.Length)
            {
                _slots[slotIndex] = stack;
            }
        }

        /// <summary>
        /// 清空所有槽位。
        /// </summary>
        public void Clear()
        {
            for (int i = 0; i < _slots.Length; i++)
            {
                _slots[i] = default;
            }
        }

        /// <summary>
        /// 导出所有非空堆叠列表（供结算入库使用）。
        /// </summary>
        public List<ItemStack> ToNonEmptyList()
        {
            List<ItemStack> list = new List<ItemStack>();
            for (int i = 0; i < _slots.Length; i++)
            {
                if (_slots[i].itemId > 0 && _slots[i].count > 0)
                {
                    list.Add(_slots[i]);
                }
            }
            return list;
        }
    }
}
