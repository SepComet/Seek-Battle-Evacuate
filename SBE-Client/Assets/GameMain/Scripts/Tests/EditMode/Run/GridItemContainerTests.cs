using System;
using NUnit.Framework;
using SepCore.Definition;
using SepCore.Run;

namespace SepCore.Tests
{
    [TestFixture]
    public class GridItemContainerTests
    {
        private ItemConfig CreateItem(int id, int width = 1, int height = 1, int stackLimit = 1)
        {
            return TestConfigFactory.Create<ItemConfig>(
                "Id", id,
                "Name", "Item_" + id,
                "Width", width,
                "Height", height,
                "StackLimit", stackLimit);
        }

        [Test]
        public void Constructor_InvalidParameters_ThrowsException()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new GridItemContainer(0, 10, _ => null));
            Assert.Throws<ArgumentOutOfRangeException>(() => new GridItemContainer(12, 0, _ => null));
            Assert.Throws<ArgumentNullException>(() => new GridItemContainer(12, 10, null));
        }

        [Test]
        public void Constructor_ValidParameters_InitializesEmpty()
        {
            GridItemContainer container = new GridItemContainer(12, 10, _ => null);

            Assert.AreEqual(12, container.Columns);
            Assert.AreEqual(10, container.Rows);
            Assert.AreEqual(120, container.TotalSlotsCount);
            Assert.AreEqual(0, container.UsedSlotsCount);
            Assert.AreEqual(120, container.FreeSlotsCount);
            Assert.AreEqual(0, container.Items.Count);
        }

        [Test]
        public void CanPlaceAt_OutOfBounds_ReturnsFalse()
        {
            GridItemContainer container = new GridItemContainer(10, 10, id => CreateItem(id, width: 2, height: 3));

            // 负数坐标
            Assert.IsFalse(container.CanPlaceAt(1001, -1, 0, isRotated: false));
            Assert.IsFalse(container.CanPlaceAt(1001, 0, -1, isRotated: false));

            // 超出右边界 (x=9, w=2 -> 9+2=11 > 10)
            Assert.IsFalse(container.CanPlaceAt(1001, 9, 0, isRotated: false));

            // 超出下边界 (y=8, h=3 -> 8+3=11 > 10)
            Assert.IsFalse(container.CanPlaceAt(1001, 0, 8, isRotated: false));

            // 紧贴边界合法 (x=8, w=2 -> 8+2=10; y=7, h=3 -> 7+3=10)
            Assert.IsTrue(container.CanPlaceAt(1001, 8, 7, isRotated: false));
        }

        [Test]
        public void TryPlaceItem_MultiCell_OccupiesAllCells()
        {
            GridItemContainer container = new GridItemContainer(6, 6, id => CreateItem(id, width: 2, height: 2));

            GridItemInstance item = new GridItemInstance(1, 1001, 1);
            bool placed = container.TryPlaceItem(item, 1, 1, isRotated: false);

            Assert.IsTrue(placed);
            Assert.AreEqual(4, container.UsedSlotsCount);
            Assert.AreEqual(32, container.FreeSlotsCount);

            // 验证 2x2 范围内全部被该 item 占用
            Assert.AreSame(item, container.GetItemAt(1, 1));
            Assert.AreSame(item, container.GetItemAt(2, 1));
            Assert.AreSame(item, container.GetItemAt(1, 2));
            Assert.AreSame(item, container.GetItemAt(2, 2));

            // 外围仍为空
            Assert.IsNull(container.GetItemAt(0, 0));
            Assert.IsNull(container.GetItemAt(3, 3));
        }

        [Test]
        public void CanPlaceAt_OverlapCollision_ReturnsFalse()
        {
            GridItemContainer container = new GridItemContainer(6, 6, id => CreateItem(id, width: 2, height: 2));

            GridItemInstance firstItem = new GridItemInstance(1, 1001, 1);
            container.TryPlaceItem(firstItem, 1, 1, isRotated: false);

            // 与已有物品部分重叠 (2, 2 重叠)
            Assert.IsFalse(container.CanPlaceAt(1002, 2, 2, isRotated: false));

            // 完全不重叠的位置合法
            Assert.IsTrue(container.CanPlaceAt(1002, 3, 1, isRotated: false));
            Assert.IsTrue(container.CanPlaceAt(1002, 1, 3, isRotated: false));
        }

        [Test]
        public void CanPlaceAt_RotatedItem_CalculatesDimensionsCorrectly()
        {
            // 宽 1，高 3 的长条物品
            GridItemContainer container = new GridItemContainer(5, 5, id => CreateItem(id, width: 1, height: 3));

            // 在 x = 4（最后一列）：不旋转宽度为 1，刚好能放下
            Assert.IsTrue(container.CanPlaceAt(1001, 4, 0, isRotated: false));

            // 顺时针旋转 90 度后，宽度变为 3，高度变为 1。在 x = 4 处 4+3=7 > 5 越界
            Assert.IsFalse(container.CanPlaceAt(1001, 4, 0, isRotated: true));

            // 在 x = 2 处旋转后 2+3=5 <= 5 合法
            Assert.IsTrue(container.CanPlaceAt(1001, 2, 0, isRotated: true));
        }

        [Test]
        public void TryAutoInsert_StackingAndScanning_PlacesCorrectly()
        {
            ItemConfig itemConfig = CreateItem(1001, width: 1, height: 1, stackLimit: 5);
            GridItemContainer container = new GridItemContainer(2, 2, _ => itemConfig);

            // 第一次加入 3 个
            bool success1 = container.TryAutoInsert(1001, 3, out GridItemInstance placed1);
            Assert.IsTrue(success1);
            Assert.AreEqual(1, container.Items.Count);
            Assert.AreEqual(3, placed1.Count);
            Assert.AreEqual(0, placed1.AnchorX);
            Assert.AreEqual(0, placed1.AnchorY);

            // 第二次加入 2 个，应该堆叠进第一个槽位而不新增槽位
            bool success2 = container.TryAutoInsert(1001, 2, out GridItemInstance placed2);
            Assert.IsTrue(success2);
            Assert.AreSame(placed1, placed2);
            Assert.AreEqual(5, placed1.Count);
            Assert.AreEqual(1, container.Items.Count);

            // 第三次加入 1 个，堆叠已满，自动寻找下一空格 (1, 0)
            bool success3 = container.TryAutoInsert(1001, 1, out GridItemInstance placed3);
            Assert.IsTrue(success3);
            Assert.AreNotSame(placed1, placed3);
            Assert.AreEqual(1, placed3.Count);
            Assert.AreEqual(1, placed3.AnchorX);
            Assert.AreEqual(0, placed3.AnchorY);
            Assert.AreEqual(2, container.Items.Count);
        }

        [Test]
        public void RemoveItem_FreesOccupiedSlots()
        {
            GridItemContainer container = new GridItemContainer(4, 4, id => CreateItem(id, width: 2, height: 2));

            GridItemInstance item = new GridItemInstance(10, 1001, 1);
            container.TryPlaceItem(item, 0, 0, isRotated: false);

            Assert.AreEqual(4, container.UsedSlotsCount);

            bool removed = container.RemoveItem(10, out GridItemInstance outItem);
            Assert.IsTrue(removed);
            Assert.AreSame(item, outItem);
            Assert.AreEqual(0, container.UsedSlotsCount);
            Assert.AreEqual(16, container.FreeSlotsCount);
            Assert.IsNull(container.GetItemAt(0, 0));

            // 原位置可以重新放置
            Assert.IsTrue(container.CanPlaceAt(1002, 0, 0, isRotated: false));
        }

        [Test]
        public void TryMoveItem_ValidAndInvalidTargets_HandlesCorrectly()
        {
            GridItemContainer container = new GridItemContainer(6, 6, id => CreateItem(id, width: 2, height: 1));

            // 放置第一个物品 2x1 在 (0, 0)
            GridItemInstance item1 = new GridItemInstance(1, 1001, 1);
            container.TryPlaceItem(item1, 0, 0, isRotated: false);

            // 放置障碍物 2x1 在 (3, 0)
            GridItemInstance obstacle = new GridItemInstance(2, 1002, 1);
            container.TryPlaceItem(obstacle, 3, 0, isRotated: false);

            // 1. 尝试移动到与障碍物重叠的位置 (2, 0) -> 失败，原位置保持不变
            bool moveFailed = container.TryMoveItem(1, 2, 0, isRotated: false);
            Assert.IsFalse(moveFailed);
            Assert.AreEqual(0, item1.AnchorX);
            Assert.AreEqual(0, item1.AnchorY);
            Assert.AreSame(item1, container.GetItemAt(0, 0));
            Assert.AreSame(item1, container.GetItemAt(1, 0));

            // 2. 尝试旋转为 1x2 并移动到 (0, 2) -> 成功
            bool moveRotated = container.TryMoveItem(1, 0, 2, isRotated: true);
            Assert.IsTrue(moveRotated);
            Assert.AreEqual(0, item1.AnchorX);
            Assert.AreEqual(2, item1.AnchorY);
            Assert.IsTrue(item1.IsRotated);
            Assert.IsNull(container.GetItemAt(0, 0));
            Assert.IsNull(container.GetItemAt(1, 0));
            Assert.AreSame(item1, container.GetItemAt(0, 2));
            Assert.AreSame(item1, container.GetItemAt(0, 3));
        }

        [Test]
        public void ToSaveData_And_LoadFromSaveData_PreservesCoordinatesAndRotation()
        {
            GridItemContainer container = new GridItemContainer(6, 6, id => CreateItem(id, width: 2, height: 1));

            // 放置两个物品，一个未旋转，一个顺时针旋转
            GridItemInstance item1 = new GridItemInstance(1, 1001, 3);
            container.TryPlaceItem(item1, 0, 0, isRotated: false);

            GridItemInstance item2 = new GridItemInstance(2, 1002, 1);
            container.TryPlaceItem(item2, 3, 2, isRotated: true);

            // 导出存档数据
            var savedData = container.ToSaveData();
            Assert.AreEqual(2, savedData.Count);

            Assert.AreEqual(1001, savedData[0].itemId);
            Assert.AreEqual(3, savedData[0].count);
            Assert.AreEqual(0, savedData[0].x);
            Assert.AreEqual(0, savedData[0].y);
            Assert.IsFalse(savedData[0].isRotated);

            Assert.AreEqual(1002, savedData[1].itemId);
            Assert.AreEqual(1, savedData[1].count);
            Assert.AreEqual(3, savedData[1].x);
            Assert.AreEqual(2, savedData[1].y);
            Assert.IsTrue(savedData[1].isRotated);

            // 载入新容器中，验证原位完全恢复
            GridItemContainer newContainer = new GridItemContainer(6, 6, id => CreateItem(id, width: 2, height: 1));
            newContainer.LoadFromSaveData(savedData);

            Assert.AreEqual(2, newContainer.Items.Count);
            GridItemInstance loaded1 = newContainer.GetItemAt(0, 0);
            Assert.IsNotNull(loaded1);
            Assert.AreEqual(1001, loaded1.ItemId);
            Assert.AreEqual(3, loaded1.Count);
            Assert.AreEqual(0, loaded1.AnchorX);
            Assert.AreEqual(0, loaded1.AnchorY);
            Assert.IsFalse(loaded1.IsRotated);

            GridItemInstance loaded2 = newContainer.GetItemAt(3, 2);
            Assert.IsNotNull(loaded2);
            Assert.AreEqual(1002, loaded2.ItemId);
            Assert.AreEqual(1, loaded2.Count);
            Assert.AreEqual(3, loaded2.AnchorX);
            Assert.AreEqual(2, loaded2.AnchorY);
            Assert.IsTrue(loaded2.IsRotated);
        }

        [Test]
        public void LoadFromSaveData_WithConflictsOrMissingCoords_AutoInsertsWithoutOverlap()
        {
            GridItemContainer container = new GridItemContainer(4, 4, id => CreateItem(id, width: 1, height: 1));

            // 模拟老存档或未赋坐标的新物品（全部为 x=0, y=0）
            var corruptedOrLegacySave = new System.Collections.Generic.List<GridItemStack>
            {
                new GridItemStack(1001, 1, 0, 0, false),
                new GridItemStack(1002, 1, 0, 0, false), // 坐标与第一个冲突
                new GridItemStack(1003, 1, 10, 10, false), // 越界坐标
            };

            container.LoadFromSaveData(corruptedOrLegacySave);

            // 3 个物品应该全部被成功安放，互不重叠
            Assert.AreEqual(3, container.Items.Count);
            Assert.AreEqual(3, container.UsedSlotsCount);

            // 检验第一个物品在 (0, 0)，其余两个自动寻位到空格
            Assert.IsNotNull(container.GetItemAt(0, 0));
            Assert.AreEqual(1001, container.GetItemAt(0, 0).ItemId);

            // 验证每个物品的格子没有重叠
            System.Collections.Generic.HashSet<string> occupiedCoords = new System.Collections.Generic.HashSet<string>();
            foreach (var item in container.Items)
            {
                string key = $"{item.AnchorX},{item.AnchorY}";
                Assert.IsTrue(occupiedCoords.Add(key), $"Duplicate coordinate found: {key}");
                Assert.IsTrue(item.AnchorX >= 0 && item.AnchorX < container.Columns);
                Assert.IsTrue(item.AnchorY >= 0 && item.AnchorY < container.Rows);
            }
        }

        [Test]
        public void ExportModularSave_And_LoadFromModularSave_RestoresStateExact()
        {
            GridItemContainer container = new GridItemContainer(6, 6, id => CreateItem(id, width: 2, height: 1));

            // 放置一件横向和一件竖向旋转物品
            GridItemInstance item1 = new GridItemInstance(101, 1001, 5);
            container.TryPlaceItem(item1, 1, 1, isRotated: false);

            GridItemInstance item2 = new GridItemInstance(102, 1002, 2);
            container.TryPlaceItem(item2, 4, 3, isRotated: true);

            // 导出模块化拆分数据
            var (whData, whLayout) = container.ExportModularSave();
            Assert.AreEqual(2, whData.items.Count);
            Assert.AreEqual(2, whLayout.placements.Count);

            // 验证数据文件只包含资产
            Assert.AreEqual(101, whData.items[0].instanceId);
            Assert.AreEqual(1001, whData.items[0].itemId);
            Assert.AreEqual(5, whData.items[0].count);

            // 验证布局文件只包含排布
            Assert.AreEqual(101, whLayout.placements[0].instanceId);
            Assert.AreEqual(1, whLayout.placements[0].x);
            Assert.AreEqual(1, whLayout.placements[0].y);
            Assert.IsFalse(whLayout.placements[0].isRotated);

            Assert.AreEqual(102, whLayout.placements[1].instanceId);
            Assert.AreEqual(4, whLayout.placements[1].x);
            Assert.AreEqual(3, whLayout.placements[1].y);
            Assert.IsTrue(whLayout.placements[1].isRotated);

            // 载入新容器中，验证原位完全恢复
            GridItemContainer newContainer = new GridItemContainer(6, 6, id => CreateItem(id, width: 2, height: 1));
            newContainer.LoadFromModularSave(whData, whLayout);

            Assert.AreEqual(2, newContainer.Items.Count);
            GridItemInstance loaded1 = newContainer.GetItemAt(1, 1);
            Assert.IsNotNull(loaded1);
            Assert.AreEqual(101, loaded1.InstanceId);
            Assert.AreEqual(1001, loaded1.ItemId);
            Assert.AreEqual(5, loaded1.Count);
            Assert.IsFalse(loaded1.IsRotated);

            GridItemInstance loaded2 = newContainer.GetItemAt(4, 3);
            Assert.IsNotNull(loaded2);
            Assert.AreEqual(102, loaded2.InstanceId);
            Assert.AreEqual(1002, loaded2.ItemId);
            Assert.AreEqual(2, loaded2.Count);
            Assert.IsTrue(loaded2.IsRotated);
        }

        [Test]
        public void LoadFromModularSave_WhenLayoutMissingOrCorrupted_AutoInsertsWithoutDataLoss()
        {
            GridItemContainer container = new GridItemContainer(4, 4, id => CreateItem(id, width: 1, height: 1));

            WarehouseDataSave data = new WarehouseDataSave
            {
                items = new System.Collections.Generic.List<WarehouseItemData>
                {
                    new WarehouseItemData(1, 1001, 1),
                    new WarehouseItemData(2, 1002, 2),
                    new WarehouseItemData(3, 1003, 3)
                }
            };

            // 模拟 layout 损坏为 null 或丢失
            container.LoadFromModularSave(data, layout: null);

            // 3 个物品全部安全放入网格，物资 0 损失
            Assert.AreEqual(3, container.Items.Count);
            Assert.AreEqual(3, container.UsedSlotsCount);
            Assert.IsNotNull(container.GetItemAt(0, 0));
            Assert.IsNotNull(container.GetItemAt(1, 0));
            Assert.IsNotNull(container.GetItemAt(2, 0));
        }
    }
}
