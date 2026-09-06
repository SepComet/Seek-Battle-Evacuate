using System;
using System.Collections.Generic;
using NUnit.Framework;
using SepCore.Definition;
using SepCore.Run;

namespace SepCore.Tests
{
    [TestFixture]
    public class RoundItemContainerTests
    {
        private ItemConfig CreateItem(int id, int value = 10, int stackLimit = 20)
        {
            return TestConfigFactory.Create<ItemConfig>(
                "Id", id,
                "Name", "Item_" + id,
                "Value", value,
                "StackLimit", stackLimit);
        }

        [Test]
        public void Constructor_NegativeMaxSlots_ThrowsException()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new RoundItemContainer(-1, _ => null));
        }

        [Test]
        public void Constructor_NullGetter_ThrowsException()
        {
            Assert.Throws<ArgumentNullException>(() => new RoundItemContainer(10, null));
        }

        [Test]
        public void Constructor_ValidInputs_InitializesEmpty()
        {
            RoundItemContainer container = new RoundItemContainer(16, _ => null);

            Assert.AreEqual(16, container.MaxSlots);
            Assert.AreEqual(0, container.UsedSlotsCount);
            Assert.AreEqual(16, container.FreeSlotsCount);
            Assert.IsFalse(container.IsFull);
            Assert.AreEqual(0, container.TotalValue);
        }

        [Test]
        public void TryAddItem_InvalidInput_ReturnsZero()
        {
            RoundItemContainer container = new RoundItemContainer(5, _ => null);

            Assert.AreEqual(0, container.TryAddItem(0, 10));
            Assert.AreEqual(0, container.TryAddItem(-1, 10));
            Assert.AreEqual(0, container.TryAddItem(5001, 0));
            Assert.AreEqual(0, container.TryAddItem(5001, -5));
        }

        [Test]
        public void TryAddItem_UnderStackLimit_FillsSingleSlot()
        {
            RoundItemContainer container = new RoundItemContainer(4, id => CreateItem(id, value: 50, stackLimit: 20));

            int added = container.TryAddItem(5001, 5);

            Assert.AreEqual(5, added);
            Assert.AreEqual(1, container.UsedSlotsCount);
            Assert.AreEqual(3, container.FreeSlotsCount);
            Assert.AreEqual(5001, container.Slots[0].itemId);
            Assert.AreEqual(5, container.Slots[0].count);
            Assert.AreEqual(250, container.TotalValue);
        }

        [Test]
        public void TryAddItem_StacksExistingSlotFirst_ThenCreatesNewSlot()
        {
            RoundItemContainer container = new RoundItemContainer(3, id => CreateItem(id, stackLimit: 10));

            container.TryAddItem(5001, 7);
            int addedSecond = container.TryAddItem(5001, 8);

            Assert.AreEqual(8, addedSecond);
            Assert.AreEqual(2, container.UsedSlotsCount);
            Assert.AreEqual(10, container.Slots[0].count);
            Assert.AreEqual(5, container.Slots[1].count);
        }

        [Test]
        public void TryAddItem_WhenContainerFull_ReturnsOnlyAcceptedCount()
        {
            RoundItemContainer container = new RoundItemContainer(2, id => CreateItem(id, stackLimit: 5));

            int added = container.TryAddItem(5001, 15);

            Assert.AreEqual(10, added);
            Assert.IsTrue(container.IsFull);
            Assert.AreEqual(0, container.FreeSlotsCount);
            Assert.AreEqual(5, container.Slots[0].count);
            Assert.AreEqual(5, container.Slots[1].count);
        }

        [Test]
        public void TryRemoveItemAt_InvalidSlotOrEmpty_ReturnsFalse()
        {
            RoundItemContainer container = new RoundItemContainer(2, _ => null);

            Assert.IsFalse(container.TryRemoveItemAt(-1, 1, out _));
            Assert.IsFalse(container.TryRemoveItemAt(2, 1, out _));
            Assert.IsFalse(container.TryRemoveItemAt(0, 0, out _));
            Assert.IsFalse(container.TryRemoveItemAt(0, 1, out _));
        }

        [Test]
        public void TryRemoveItemAt_PartialRemoval_DecrementsCount()
        {
            RoundItemContainer container = new RoundItemContainer(2, id => CreateItem(id, stackLimit: 20));
            container.TryAddItem(5001, 10);

            bool success = container.TryRemoveItemAt(0, 4, out int removed);

            Assert.IsTrue(success);
            Assert.AreEqual(4, removed);
            Assert.AreEqual(6, container.Slots[0].count);
            Assert.AreEqual(1, container.UsedSlotsCount);
        }

        [Test]
        public void TryRemoveItemAt_CompleteRemoval_ClearsSlot()
        {
            RoundItemContainer container = new RoundItemContainer(2, id => CreateItem(id, stackLimit: 20));
            container.TryAddItem(5001, 5);

            bool success = container.TryRemoveItemAt(0, 10, out int removed);

            Assert.IsTrue(success);
            Assert.AreEqual(5, removed);
            Assert.AreEqual(0, container.Slots[0].count);
            Assert.AreEqual(0, container.Slots[0].itemId);
            Assert.AreEqual(0, container.UsedSlotsCount);
        }

        [Test]
        public void SwapSlots_SameSlot_ReturnsTrue()
        {
            RoundItemContainer container = new RoundItemContainer(2, _ => null);
            Assert.IsTrue(container.SwapSlots(0, 0));
        }

        [Test]
        public void SwapSlots_OutOfBounds_ReturnsFalse()
        {
            RoundItemContainer container = new RoundItemContainer(2, _ => null);
            Assert.IsFalse(container.SwapSlots(-1, 0));
            Assert.IsFalse(container.SwapSlots(0, 2));
        }

        [Test]
        public void SwapSlots_DifferentItems_SwapsContents()
        {
            RoundItemContainer container = new RoundItemContainer(2, id => CreateItem(id));
            container.TryAddItem(5001, 3);
            container.TryAddItem(5002, 7);

            bool swapped = container.SwapSlots(0, 1);

            Assert.IsTrue(swapped);
            Assert.AreEqual(5002, container.Slots[0].itemId);
            Assert.AreEqual(7, container.Slots[0].count);
            Assert.AreEqual(5001, container.Slots[1].itemId);
            Assert.AreEqual(3, container.Slots[1].count);
        }

        [Test]
        public void SwapSlots_SameItem_MergesUpToStackLimit()
        {
            RoundItemContainer container = new RoundItemContainer(3, id => CreateItem(id, stackLimit: 10));
            container.SetSlot(0, new ItemStack(5001, 6));
            container.SetSlot(1, new ItemStack(5001, 7));

            bool merged = container.SwapSlots(0, 1);

            Assert.IsTrue(merged);
            Assert.AreEqual(3, container.Slots[0].count);
            Assert.AreEqual(10, container.Slots[1].count);
        }

        [Test]
        public void TryMoveTo_ValidMove_MovesItemsBetweenContainers()
        {
            RoundItemContainer source = new RoundItemContainer(2, id => CreateItem(id, stackLimit: 10));
            RoundItemContainer target = new RoundItemContainer(2, id => CreateItem(id, stackLimit: 10));

            source.TryAddItem(5001, 8);

            bool moved = source.TryMoveTo(0, target, 5, out int movedCount);

            Assert.IsTrue(moved);
            Assert.AreEqual(5, movedCount);
            Assert.AreEqual(3, source.Slots[0].count);
            Assert.AreEqual(5, target.Slots[0].count);
            Assert.AreEqual(5001, target.Slots[0].itemId);
        }

        [Test]
        public void TryMoveTo_TargetFull_DoesNotMoveExcess()
        {
            RoundItemContainer source = new RoundItemContainer(1, id => CreateItem(id, stackLimit: 10));
            RoundItemContainer target = new RoundItemContainer(1, id => CreateItem(id, stackLimit: 5));

            source.TryAddItem(5001, 8);

            bool moved = source.TryMoveTo(0, target, 8, out int movedCount);

            Assert.IsTrue(moved);
            Assert.AreEqual(5, movedCount);
            Assert.AreEqual(3, source.Slots[0].count);
            Assert.AreEqual(5, target.Slots[0].count);
        }

        [Test]
        public void ToNonEmptyList_ReturnsOnlyOccupiedSlots()
        {
            RoundItemContainer container = new RoundItemContainer(4, id => CreateItem(id));
            container.SetSlot(0, new ItemStack(5001, 2));
            container.SetSlot(2, new ItemStack(5002, 5));

            List<ItemStack> list = container.ToNonEmptyList();

            Assert.AreEqual(2, list.Count);
            Assert.AreEqual(5001, list[0].itemId);
            Assert.AreEqual(2, list[0].count);
            Assert.AreEqual(5002, list[1].itemId);
            Assert.AreEqual(5, list[1].count);
        }
    }
}
