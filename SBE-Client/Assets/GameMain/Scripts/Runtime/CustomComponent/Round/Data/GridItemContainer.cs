using System;
using System.Collections.Generic;
using SepCore.Definition;

namespace SepCore.Run
{
    /// <summary>
    /// 脱离 MonoBehaviour 的纯逻辑网格物品容器（俄罗斯方块式二维网格）。
    /// 管理网格占用矩阵、碰撞与越界检测、旋转判定与自动寻位装箱算法。
    /// </summary>
    public sealed class GridItemContainer
    {
        private readonly int _columns;
        private readonly int _rows;
        private readonly int[,] _occupied;
        private readonly List<GridItemInstance> _items = new List<GridItemInstance>();
        private readonly Func<int, ItemConfig> _itemConfigGetter;
        private int _nextInstanceId = 1;

        public int Columns => _columns;
        public int Rows => _rows;
        public int TotalSlotsCount => _columns * _rows;
        public IReadOnlyList<GridItemInstance> Items => _items;

        public GridItemContainer(int columns, int rows, Func<int, ItemConfig> itemConfigGetter)
        {
            if (columns <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(columns), "Columns must be greater than zero.");
            }

            if (rows <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(rows), "Rows must be greater than zero.");
            }

            _columns = columns;
            _rows = rows;
            _occupied = new int[columns, rows];
            _itemConfigGetter = itemConfigGetter ?? throw new ArgumentNullException(nameof(itemConfigGetter));
        }

        /// <summary>
        /// 当前被占用的网格单元格总数。
        /// </summary>
        public int UsedSlotsCount
        {
            get
            {
                int count = 0;
                for (int x = 0; x < _columns; x++)
                {
                    for (int y = 0; y < _rows; y++)
                    {
                        if (_occupied[x, y] != 0)
                        {
                            count++;
                        }
                    }
                }
                return count;
            }
        }

        /// <summary>
        /// 剩余未占用的空格子总数。
        /// </summary>
        public int FreeSlotsCount => TotalSlotsCount - UsedSlotsCount;

        /// <summary>
        /// 检查指定尺寸的物品能否放置在目标坐标 (targetX, targetY)。
        /// </summary>
        public bool CanPlaceAt(int itemId, int targetX, int targetY, bool isRotated, int ignoreInstanceId = 0)
        {
            if (itemId <= 0)
            {
                return false;
            }

            ItemConfig config = _itemConfigGetter(itemId);
            if (config == null)
            {
                return false;
            }

            int rawW = Math.Max(1, config.Width);
            int rawH = Math.Max(1, config.Height);
            int w = isRotated ? rawH : rawW;
            int h = isRotated ? rawW : rawH;

            // 1. 边界检测
            if (targetX < 0 || targetY < 0 || targetX + w > _columns || targetY + h > _rows)
            {
                return false;
            }

            // 2. 逐格占用检测
            for (int x = targetX; x < targetX + w; x++)
            {
                for (int y = targetY; y < targetY + h; y++)
                {
                    int occ = _occupied[x, y];
                    if (occ != 0 && occ != ignoreInstanceId)
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        /// <summary>
        /// 检查已有物品实例能否放置在目标坐标 (targetX, targetY)。
        /// </summary>
        public bool CanPlaceAt(GridItemInstance item, int targetX, int targetY, bool isRotated)
        {
            if (item == null)
            {
                return false;
            }

            return CanPlaceAt(item.ItemId, targetX, targetY, isRotated, item.InstanceId);
        }

        /// <summary>
        /// 将物品放入网格指定坐标。若位置合法则占用网格并记录物品。
        /// </summary>
        public bool TryPlaceItem(GridItemInstance item, int targetX, int targetY, bool isRotated)
        {
            if (item == null)
            {
                return false;
            }

            if (!CanPlaceAt(item, targetX, targetY, isRotated))
            {
                return false;
            }

            // 若物品已经在容器中，先擦除原位置占用
            if (_items.Contains(item))
            {
                MarkOccupied(item, 0);
            }
            else
            {
                if (item.InstanceId <= 0)
                {
                    item.InstanceId = _nextInstanceId++;
                }
                else if (item.InstanceId >= _nextInstanceId)
                {
                    _nextInstanceId = item.InstanceId + 1;
                }

                _items.Add(item);
            }

            item.AnchorX = targetX;
            item.AnchorY = targetY;
            item.IsRotated = isRotated;

            MarkOccupied(item, item.InstanceId);
            return true;
        }

        /// <summary>
        /// 尝试将指定物品从当前位置移动到目标位置与旋转角度。
        /// 若目标位置合法，则原子化更新占用矩阵并更新物品坐标；若非法则保持原位置不变。
        /// </summary>
        public bool TryMoveItem(int instanceId, int targetX, int targetY, bool isRotated)
        {
            GridItemInstance item = GetItemByInstanceId(instanceId);
            if (item == null)
            {
                return false;
            }

            // 临时擦除当前物品在原位置的网格占用
            MarkOccupied(item, 0);

            // 检查目标位置是否合法（忽略自身实例）
            if (CanPlaceAt(item.ItemId, targetX, targetY, isRotated, instanceId))
            {
                item.AnchorX = targetX;
                item.AnchorY = targetY;
                item.IsRotated = isRotated;
                MarkOccupied(item, item.InstanceId);
                return true;
            }

            // 非法则恢复原位置占用
            MarkOccupied(item, item.InstanceId);
            return false;
        }

        /// <summary>
        /// 尝试将物品加入容器：优先寻找未满的同类物品堆叠；多余部分通过空间扫描自动放入空位。
        /// </summary>
        /// <param name="itemId">物品配置 ID。</param>
        /// <param name="count">放入数量。</param>
        /// <param name="placedItem">最终放入或合并到的物品实例。</param>
        /// <returns>是否全部成功放入。</returns>
        public bool TryAutoInsert(int itemId, int count, out GridItemInstance placedItem)
        {
            placedItem = null;
            if (itemId <= 0 || count <= 0)
            {
                return false;
            }

            ItemConfig config = _itemConfigGetter(itemId);
            if (config == null)
            {
                return false;
            }

            int stackLimit = config.StackLimit > 0 ? config.StackLimit : 1;
            int remaining = count;

            // 阶段 1：先向已有同类未满堆叠进行合并
            if (stackLimit > 1)
            {
                for (int i = 0; i < _items.Count; i++)
                {
                    GridItemInstance existing = _items[i];
                    if (existing.ItemId == itemId && existing.Count < stackLimit)
                    {
                        int space = stackLimit - existing.Count;
                        int toAdd = Math.Min(space, remaining);
                        existing.Count += toAdd;
                        remaining -= toAdd;
                        placedItem = existing;

                        if (remaining <= 0)
                        {
                            return true;
                        }
                    }
                }
            }

            // 阶段 2：寻找网格空位放入剩余数量
            if (TryFindFreePosition(itemId, out int foundX, out int foundY, out bool foundRotated))
            {
                GridItemInstance newItem = new GridItemInstance(_nextInstanceId++, itemId, remaining, foundX, foundY, foundRotated);
                _items.Add(newItem);
                MarkOccupied(newItem, newItem.InstanceId);
                placedItem = newItem;
                return true;
            }

            return false;
        }

        /// <summary>
        /// 按从左到右、从上到下的顺序扫描首个能够容纳该物品的网格位置。
        /// 优先尝试不旋转；若放不下且为非对称长宽，尝试旋转 90 度。
        /// </summary>
        public bool TryFindFreePosition(int itemId, out int foundX, out int foundY, out bool isRotated)
        {
            foundX = -1;
            foundY = -1;
            isRotated = false;

            ItemConfig config = _itemConfigGetter(itemId);
            if (config == null)
            {
                return false;
            }

            int rawW = Math.Max(1, config.Width);
            int rawH = Math.Max(1, config.Height);
            bool canRotate = rawW != rawH;

            // 1. 优先尝试不旋转
            for (int y = 0; y < _rows; y++)
            {
                for (int x = 0; x < _columns; x++)
                {
                    if (CanPlaceAt(itemId, x, y, isRotated: false))
                    {
                        foundX = x;
                        foundY = y;
                        isRotated = false;
                        return true;
                    }
                }
            }

            // 2. 放不下且长宽不同，尝试旋转 90 度
            if (canRotate)
            {
                for (int y = 0; y < _rows; y++)
                {
                    for (int x = 0; x < _columns; x++)
                    {
                        if (CanPlaceAt(itemId, x, y, isRotated: true))
                        {
                            foundX = x;
                            foundY = y;
                            isRotated = true;
                            return true;
                        }
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// 移除指定实例 ID 的物品并释放其占用的网格。
        /// </summary>
        public bool RemoveItem(int instanceId, out GridItemInstance removedItem)
        {
            removedItem = null;
            if (instanceId <= 0)
            {
                return false;
            }

            for (int i = 0; i < _items.Count; i++)
            {
                if (_items[i].InstanceId == instanceId)
                {
                    removedItem = _items[i];
                    _items.RemoveAt(i);
                    MarkOccupied(removedItem, 0);
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 获取指定坐标 (x, y) 处的物品实例；若为空格或越界返回 null。
        /// </summary>
        public GridItemInstance GetItemAt(int x, int y)
        {
            if (x < 0 || x >= _columns || y < 0 || y >= _rows)
            {
                return null;
            }

            int instanceId = _occupied[x, y];
            if (instanceId <= 0)
            {
                return null;
            }

            return GetItemByInstanceId(instanceId);
        }

        /// <summary>
        /// 根据实例 ID 获取对应的物品实例。
        /// </summary>
        public GridItemInstance GetItemByInstanceId(int instanceId)
        {
            for (int i = 0; i < _items.Count; i++)
            {
                if (_items[i].InstanceId == instanceId)
                {
                    return _items[i];
                }
            }

            return null;
        }

        /// <summary>
        /// 清空容器所有物品与占用矩阵。
        /// </summary>
        public void Clear()
        {
            Array.Clear(_occupied, 0, _occupied.Length);
            _items.Clear();
        }

        /// <summary>
        /// 将当前网格容器中的所有物品及绝对坐标导出为存档数据列表。
        /// </summary>
        public List<GridItemStack> ToSaveData()
        {
            List<GridItemStack> result = new List<GridItemStack>(_items.Count);
            for (int i = 0; i < _items.Count; i++)
            {
                GridItemInstance item = _items[i];
                result.Add(new GridItemStack(item.ItemId, item.Count, item.AnchorX, item.AnchorY, item.IsRotated));
            }

            return result;
        }

        /// <summary>
        /// 从存档数据列表加载物品。
        /// 若坐标合法且无重叠冲突则精确原位放置；若坐标越界或冲突（或外部新增未带坐标），则调用 TryAutoInsert 自动装箱寻位。
        /// </summary>
        public void LoadFromSaveData(IReadOnlyList<GridItemStack> savedItems)
        {
            Clear();
            if (savedItems == null || savedItems.Count == 0)
            {
                return;
            }

            // 优先尝试原位放置合法物品
            List<GridItemStack> failedItems = null;

            for (int i = 0; i < savedItems.Count; i++)
            {
                GridItemStack stack = savedItems[i];
                if (stack.itemId <= 0 || stack.count <= 0)
                {
                    continue;
                }

                if (CanPlaceAt(stack.itemId, stack.x, stack.y, stack.isRotated))
                {
                    GridItemInstance item = new GridItemInstance(_nextInstanceId++, stack.itemId, stack.count, stack.x, stack.y, stack.isRotated);
                    _items.Add(item);
                    MarkOccupied(item, item.InstanceId);
                }
                else
                {
                    if (failedItems == null)
                    {
                        failedItems = new List<GridItemStack>();
                    }

                    failedItems.Add(stack);
                }
            }

            // 对于无法原位放置的物品（如老存档全部 (0,0) 或冲突重叠），自动寻位落入空格
            if (failedItems != null)
            {
                for (int i = 0; i < failedItems.Count; i++)
                {
                    GridItemStack stack = failedItems[i];
                    TryAutoInsert(stack.itemId, stack.count, out _);
                }
            }
        }

        /// <summary>
        /// 将容器数据拆分为物资资产数据 (WarehouseDataSave) 与网格空间布局 (WarehouseLayoutSave) 两个独立结构。
        /// </summary>
        public (WarehouseDataSave data, WarehouseLayoutSave layout) ExportModularSave()
        {
            WarehouseDataSave data = new WarehouseDataSave();
            WarehouseLayoutSave layout = new WarehouseLayoutSave();

            for (int i = 0; i < _items.Count; i++)
            {
                GridItemInstance item = _items[i];
                data.items.Add(new WarehouseItemData(item.InstanceId, item.ItemId, item.Count));
                layout.placements.Add(new WarehouseSlotPlacement(item.InstanceId, item.AnchorX, item.AnchorY, item.IsRotated));
            }

            return (data, layout);
        }

        /// <summary>
        /// 从模块化存储结构中恢复网格。
        /// 根据 instanceId 匹配物品与其网格摆放；
        /// 若 layout 为空、损坏或某些物品在 layout 中无对应记录（外部新增或冲突），
        /// 则自动调用 TryAutoInsert 进行智能寻位装箱，确保物资 100% 完整保留。
        /// </summary>
        public void LoadFromModularSave(WarehouseDataSave data, WarehouseLayoutSave layout)
        {
            Clear();
            if (data == null || data.items == null || data.items.Count == 0)
            {
                return;
            }

            // 建立 instanceId -> placement 映射表
            Dictionary<int, WarehouseSlotPlacement> placementMap = new Dictionary<int, WarehouseSlotPlacement>();
            if (layout != null && layout.placements != null)
            {
                for (int i = 0; i < layout.placements.Count; i++)
                {
                    WarehouseSlotPlacement p = layout.placements[i];
                    if (p != null && !placementMap.ContainsKey(p.instanceId))
                    {
                        placementMap.Add(p.instanceId, p);
                    }
                }
            }

            List<WarehouseItemData> unplacedItems = null;

            // 阶段 1：尝试根据 placement 原位放置
            for (int i = 0; i < data.items.Count; i++)
            {
                WarehouseItemData itemData = data.items[i];
                if (itemData == null || itemData.itemId <= 0 || itemData.count <= 0)
                {
                    continue;
                }

                if (placementMap.TryGetValue(itemData.instanceId, out WarehouseSlotPlacement p) &&
                    CanPlaceAt(itemData.itemId, p.x, p.y, p.isRotated))
                {
                    int instId = itemData.instanceId > 0 ? itemData.instanceId : _nextInstanceId++;
                    if (instId >= _nextInstanceId)
                    {
                        _nextInstanceId = instId + 1;
                    }

                    GridItemInstance item = new GridItemInstance(instId, itemData.itemId, itemData.count, p.x, p.y, p.isRotated);
                    _items.Add(item);
                    MarkOccupied(item, item.InstanceId);
                }
                else
                {
                    if (unplacedItems == null)
                    {
                        unplacedItems = new List<WarehouseItemData>();
                    }

                    unplacedItems.Add(itemData);
                }
            }

            // 阶段 2：对于没有合法 placement 或坐标冲突的物品，自动寻位装箱
            if (unplacedItems != null)
            {
                for (int i = 0; i < unplacedItems.Count; i++)
                {
                    WarehouseItemData itemData = unplacedItems[i];
                    TryAutoInsert(itemData.itemId, itemData.count, out _);
                }
            }
        }

        private void MarkOccupied(GridItemInstance item, int value)
        {
            int w = item.GetWidth(_itemConfigGetter);
            int h = item.GetHeight(_itemConfigGetter);
            for (int x = item.AnchorX; x < item.AnchorX + w; x++)
            {
                for (int y = item.AnchorY; y < item.AnchorY + h; y++)
                {
                    if (x >= 0 && x < _columns && y >= 0 && y < _rows)
                    {
                        _occupied[x, y] = value;
                    }
                }
            }
        }
    }
}
