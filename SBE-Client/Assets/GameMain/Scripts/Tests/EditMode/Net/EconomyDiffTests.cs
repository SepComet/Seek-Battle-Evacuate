using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using SepCore.Definition;
using SepCore.Net;

namespace SepCore.Tests
{
    [TestFixture]
    public class EconomyDiffTests
    {
        private readonly Dictionary<int, ItemConfig> _items = new Dictionary<int, ItemConfig>();

        [OneTimeSetUp]
        public void SetUpItems()
        {
            _items[5001] = MakeItem(5001, 99, EquipmentSlotType.None);
            _items[5101] = MakeItem(5101, 1, EquipmentSlotType.Weapon);
            _items[5102] = MakeItem(5102, 1, EquipmentSlotType.Weapon);
            _items[5201] = MakeItem(5201, 1, EquipmentSlotType.Armor);
            _items[5301] = MakeItem(5301, 5, EquipmentSlotType.None);
        }

        private static ItemConfig MakeItem(int id, int stackLimit, EquipmentSlotType slot)
        {
            return TestConfigFactory.Create<ItemConfig>(
                "Id", id, "Name", "Item_" + id, "StackLimit", stackLimit, "EquipSlot", slot,
                "Value", 10, "Width", 1, "Height", 1);
        }

        private ItemConfig GetItem(int id) => _items.TryGetValue(id, out ItemConfig config) ? config : null;

        private static WarehouseLedger LedgerOf(params NetSnapRow[] rows)
        {
            NetSnapshot snapshot = new NetSnapshot { items = rows.ToList(), initialized = true };
            snapshot.characters.Add(new NetSnapChar { cid = 1001 });
            WarehouseLedger ledger = new WarehouseLedger();
            ledger.Rebuild(snapshot);
            return ledger;
        }

        private static NetSnapRow Row(long uid, int itemId, int count, int x, int y, int wearer = 0)
        {
            return new NetSnapRow { uid = uid, itemId = itemId, count = count, x = x, y = y, wearer = wearer };
        }

        private static SaveData SaveWith(List<GridItemStack> warehouse, int weaponItemId = 0)
        {
            return new SaveData
            {
                mainWarehouse = warehouse,
                characters = new List<CharacterSave> { new CharacterSave(1001, weaponItemId, 0) },
            };
        }

        private List<NetOp> Diff(WarehouseLedger ledger, SaveData save) => EconomyDiff.Compute(ledger, save, GetItem);

        [Test]
        public void NoChange_ProducesNoOps()
        {
            WarehouseLedger ledger = LedgerOf(Row(1, 5001, 5, 1, 1));
            SaveData save = SaveWith(new List<GridItemStack> { new GridItemStack(5001, 5, 1, 1) });
            Assert.That(Diff(ledger, save), Is.Empty);
        }

        [Test]
        public void LayoutMove_ProducesMov()
        {
            WarehouseLedger ledger = LedgerOf(Row(1, 5001, 5, 1, 1));
            SaveData save = SaveWith(new List<GridItemStack> { new GridItemStack(5001, 5, 2, 2) });
            List<NetOp> ops = Diff(ledger, save);
            Assert.That(ops, Has.Count.EqualTo(1));
            Assert.That(ops[0].k, Is.EqualTo(NetOpKind.Mov));
            Assert.That(ops[0].uid, Is.EqualTo(1));
            Assert.That(ops[0].x, Is.EqualTo(2));
            Assert.That(ops[0].y, Is.EqualTo(2));
        }

        [Test]
        public void LootAdded_ProducesAdd()
        {
            WarehouseLedger ledger = LedgerOf(Row(1, 5001, 5, 1, 1));
            SaveData save = SaveWith(new List<GridItemStack>
            {
                new GridItemStack(5001, 5, 1, 1),
                new GridItemStack(5301, 3, 0, 3),
            });
            List<NetOp> ops = Diff(ledger, save);
            Assert.That(ops, Has.Count.EqualTo(1));
            Assert.That(ops[0].k, Is.EqualTo(NetOpKind.Add));
            Assert.That(ops[0].itemId, Is.EqualTo(5301));
            Assert.That(ops[0].count, Is.EqualTo(3));
        }

        [Test]
        public void StackPartiallyConsumed_ProducesDelDelta()
        {
            WarehouseLedger ledger = LedgerOf(Row(1, 5001, 5, 1, 1));
            SaveData save = SaveWith(new List<GridItemStack> { new GridItemStack(5001, 2, 1, 1) });
            List<NetOp> ops = Diff(ledger, save);
            Assert.That(ops, Has.Count.EqualTo(1));
            Assert.That(ops[0].k, Is.EqualTo(NetOpKind.Del));
            Assert.That(ops[0].uid, Is.EqualTo(1));
            Assert.That(ops[0].count, Is.EqualTo(3));
        }

        [Test]
        public void StackRemoved_ProducesWholeDel()
        {
            WarehouseLedger ledger = LedgerOf(Row(1, 5001, 5, 1, 1));
            SaveData save = SaveWith(new List<GridItemStack>());
            List<NetOp> ops = Diff(ledger, save);
            Assert.That(ops, Has.Count.EqualTo(1));
            Assert.That(ops[0].k, Is.EqualTo(NetOpKind.Del));
            Assert.That(ops[0].count, Is.EqualTo(0));
        }

        [Test]
        public void EquipFromGrid_ProducesWearByUid()
        {
            WarehouseLedger ledger = LedgerOf(Row(1, 5101, 1, 2, 0));
            SaveData save = SaveWith(new List<GridItemStack>(), weaponItemId: 5101);
            List<NetOp> ops = Diff(ledger, save);
            Assert.That(ops, Has.Count.EqualTo(1));
            Assert.That(ops[0].k, Is.EqualTo(NetOpKind.Wear));
            Assert.That(ops[0].uid, Is.EqualTo(1));
            Assert.That(ops[0].cid, Is.EqualTo(1001));
            Assert.That(ops[0].slot, Is.EqualTo(NetSlot.Weapon));
        }

        [Test]
        public void UnequipToGrid_ProducesRel()
        {
            WarehouseLedger ledger = LedgerOf(Row(1, 5101, 1, 0, 0, wearer: 1001));
            SaveData save = SaveWith(new List<GridItemStack> { new GridItemStack(5101, 1, 4, 4) });
            List<NetOp> ops = Diff(ledger, save);
            Assert.That(ops, Has.Count.EqualTo(1));
            Assert.That(ops[0].k, Is.EqualTo(NetOpKind.Rel));
            Assert.That(ops[0].uid, Is.EqualTo(1));
            Assert.That(ops[0].x, Is.EqualTo(4));
            Assert.That(ops[0].y, Is.EqualTo(4));
        }

        [Test]
        public void SwapWeapon_ProducesRelThenWear()
        {
            WarehouseLedger ledger = LedgerOf(
                Row(1, 5101, 1, 0, 0, wearer: 1001),
                Row(2, 5102, 1, 5, 5));
            SaveData save = SaveWith(new List<GridItemStack> { new GridItemStack(5101, 1, 2, 2) }, weaponItemId: 5102);
            List<NetOp> ops = Diff(ledger, save);
            Assert.That(ops, Has.Count.EqualTo(2));
            Assert.That(ops[0].k, Is.EqualTo(NetOpKind.Rel));
            Assert.That(ops[0].uid, Is.EqualTo(1));
            Assert.That(ops[1].k, Is.EqualTo(NetOpKind.Wear));
            Assert.That(ops[1].uid, Is.EqualTo(2));
        }

        [Test]
        public void DeathDestroysWorn_ProducesDel()
        {
            WarehouseLedger ledger = LedgerOf(Row(1, 5101, 1, 0, 0, wearer: 1001));
            SaveData save = SaveWith(new List<GridItemStack>());
            List<NetOp> ops = Diff(ledger, save);
            Assert.That(ops, Has.Count.EqualTo(1));
            Assert.That(ops[0].k, Is.EqualTo(NetOpKind.Del));
            Assert.That(ops[0].uid, Is.EqualTo(1));
        }

        [Test]
        public void InRunPickupWorn_ProducesWearByItemId()
        {
            WarehouseLedger ledger = LedgerOf(Row(1, 5001, 5, 1, 1));
            SaveData save = SaveWith(new List<GridItemStack> { new GridItemStack(5001, 5, 1, 1) }, weaponItemId: 5102);
            List<NetOp> ops = Diff(ledger, save);
            Assert.That(ops, Has.Count.EqualTo(1));
            Assert.That(ops[0].k, Is.EqualTo(NetOpKind.Wear));
            Assert.That(ops[0].itemId, Is.EqualTo(5102));
            Assert.That(ops[0].uid, Is.EqualTo(0));
        }

        [Test]
        public void RecruitNewCharacter_ProducesRecruit()
        {
            WarehouseLedger ledger = LedgerOf(Row(1, 5001, 5, 1, 1));
            SaveData save = SaveWith(new List<GridItemStack> { new GridItemStack(5001, 5, 1, 1) });
            save.characters.Add(new CharacterSave(1002, 0, 0));
            List<NetOp> ops = Diff(ledger, save);
            Assert.That(ops, Has.Count.EqualTo(1));
            Assert.That(ops[0].k, Is.EqualTo(NetOpKind.Recruit));
            Assert.That(ops[0].cid, Is.EqualTo(1002));
        }

        [Test]
        public void SettlementFullBatch_OrderAndContent()
        {
            // 撤离结算综合用例：招募 + 战利品入库 + 旧武器销毁 + 新武器穿上
            WarehouseLedger ledger = LedgerOf(
                Row(1, 5101, 1, 0, 0, wearer: 1001),
                Row(2, 5001, 10, 0, 0));
            SaveData save = SaveWith(new List<GridItemStack>
            {
                new GridItemStack(5001, 10, 0, 0),
                new GridItemStack(5301, 4, 1, 0),
            }, weaponItemId: 5102);
            save.characters.Add(new CharacterSave(1003, 0, 0));

            List<NetOp> ops = Diff(ledger, save);
            Assert.That(ops.Select(o => o.k), Is.EqualTo(new[] { NetOpKind.Recruit, NetOpKind.Del, NetOpKind.Add, NetOpKind.Wear }));
            Assert.That(ops[1].uid, Is.EqualTo(1));
            Assert.That(ops[2].itemId, Is.EqualTo(5301));
            Assert.That(ops[3].itemId, Is.EqualTo(5102));
        }

        [Test]
        public void PartyChange_ProducesPartyOp()
        {
            WarehouseLedger ledger = LedgerOf();
            SaveData save = SaveWith(new List<GridItemStack>());
            save.loadout = new LoadoutSave { partyCharacterIds = new[] { 1001 } };

            List<NetOp> ops = Diff(ledger, save);
            Assert.That(ops, Has.Count.EqualTo(1));
            Assert.That(ops[0].k, Is.EqualTo(NetOpKind.Party));
            Assert.That(ops[0].party, Is.EqualTo(new[] { 1001 }));
        }

        [Test]
        public void PartyOrderMatters_ProducesPartyOp()
        {
            // 顺序即出战序：内容相同但顺序不同也必须整发
            WarehouseLedger ledger = LedgerOf();
            NetSnapshot snapshot = new NetSnapshot { initialized = true, party = new[] { 1001, 1002 } };
            snapshot.characters.Add(new NetSnapChar { cid = 1001 });
            snapshot.characters.Add(new NetSnapChar { cid = 1002 });
            ledger.Rebuild(snapshot);

            SaveData save = SaveWith(new List<GridItemStack>());
            save.loadout = new LoadoutSave { partyCharacterIds = new[] { 1002, 1001 } };

            List<NetOp> ops = Diff(ledger, save);
            Assert.That(ops, Has.Count.EqualTo(1));
            Assert.That(ops[0].k, Is.EqualTo(NetOpKind.Party));
            Assert.That(ops[0].party, Is.EqualTo(new[] { 1002, 1001 }));
        }

        [Test]
        public void PartyNullLocal_ProducesNoPartyOp()
        {
            // 本地从未配置（null）时不得整发空队误清服务器状态
            WarehouseLedger ledger = LedgerOf();
            NetSnapshot snapshot = new NetSnapshot { initialized = true, party = new[] { 1001 } };
            snapshot.characters.Add(new NetSnapChar { cid = 1001 });
            ledger.Rebuild(snapshot);

            SaveData save = SaveWith(new List<GridItemStack>());
            save.loadout = new LoadoutSave { partyCharacterIds = null };

            Assert.That(Diff(ledger, save), Is.Empty);
        }

        [Test]
        public void DifficultyChange_ProducesDifficultyOp()
        {
            WarehouseLedger ledger = LedgerOf();
            SaveData save = SaveWith(new List<GridItemStack>());
            save.loadout = new LoadoutSave { difficultyId = DifficultyTier.Tier2 };

            List<NetOp> ops = Diff(ledger, save);
            Assert.That(ops, Has.Count.EqualTo(1));
            Assert.That(ops[0].k, Is.EqualTo(NetOpKind.Difficulty));
            Assert.That(ops[0].difficulty, Is.EqualTo(2));
        }

        [Test]
        public void CarryChange_ProducesCarryOp()
        {
            WarehouseLedger ledger = LedgerOf();
            SaveData save = SaveWith(new List<GridItemStack>());
            save.loadout = new LoadoutSave
            {
                carriedItems = new List<ItemStack> { new ItemStack(5001, 3), new ItemStack(5301, 1) },
            };

            List<NetOp> ops = Diff(ledger, save);
            Assert.That(ops, Has.Count.EqualTo(1));
            Assert.That(ops[0].k, Is.EqualTo(NetOpKind.Carry));
            Assert.That(ops[0].carry, Has.Count.EqualTo(2));
            Assert.That(ops[0].carry[1].itemId, Is.EqualTo(5301));
        }

        [Test]
        public void LoadoutInSync_ProducesNoOps()
        {
            NetSnapshot snapshot = new NetSnapshot
            {
                initialized = true,
                difficulty = 3,
                party = new[] { 1001 },
                carried = new List<NetStack> { new NetStack(5001, 2) },
            };
            snapshot.characters.Add(new NetSnapChar { cid = 1001 });
            snapshot.items.Add(Row(1, 5001, 5, 1, 1));
            WarehouseLedger ledger = new WarehouseLedger();
            ledger.Rebuild(snapshot);

            SaveData save = SaveWith(new List<GridItemStack> { new GridItemStack(5001, 5, 1, 1) });
            save.loadout = new LoadoutSave
            {
                partyCharacterIds = new[] { 1001 },
                difficultyId = DifficultyTier.Tier3,
                carriedItems = new List<ItemStack> { new ItemStack(5001, 2) },
            };

            Assert.That(Diff(ledger, save), Is.Empty);
        }

        [Test]
        public void ClearConfiguredLoadout_ProducesExplicitZeroReplacements()
        {
            NetSnapshot snapshot = new NetSnapshot
            {
                initialized = true,
                party = new[] { 1001 },
                difficulty = 3,
                carried = new List<NetStack> { new NetStack(5001, 2) },
            };
            snapshot.characters.Add(new NetSnapChar { cid = 1001 });
            WarehouseLedger ledger = new WarehouseLedger();
            ledger.Rebuild(snapshot);
            SaveData save = SaveWith(new List<GridItemStack>());
            save.loadout = new LoadoutSave
            {
                partyCharacterIds = Array.Empty<int>(),
                difficultyId = DifficultyTier.None,
                carriedItems = new List<ItemStack>(),
            };

            List<NetOp> ops = Diff(ledger, save);
            Assert.That(ops.Select(o => o.k), Is.EqualTo(new[]
                { NetOpKind.Party, NetOpKind.Difficulty, NetOpKind.Carry }));
            Assert.That(ops[0].party, Is.Empty);
            Assert.That(ops[1].difficulty, Is.Zero);
            Assert.That(ops[2].carry, Is.Empty);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void CarrySequenceOrCountChange_ReplacesWholePlan(bool reorder)
        {
            NetSnapshot snapshot = new NetSnapshot
            {
                initialized = true,
                carried = new List<NetStack> { new NetStack(5001, 2), new NetStack(5301, 1) },
            };
            snapshot.characters.Add(new NetSnapChar { cid = 1001 });
            WarehouseLedger ledger = new WarehouseLedger();
            ledger.Rebuild(snapshot);
            SaveData save = SaveWith(new List<GridItemStack>());
            save.loadout = new LoadoutSave
            {
                carriedItems = reorder
                    ? new List<ItemStack> { new ItemStack(5301, 1), new ItemStack(5001, 2) }
                    : new List<ItemStack> { new ItemStack(5001, 3), new ItemStack(5301, 1) },
            };

            List<NetOp> ops = Diff(ledger, save);
            Assert.That(ops, Has.Count.EqualTo(1));
            Assert.That(ops[0].k, Is.EqualTo(NetOpKind.Carry));
            Assert.That(ops[0].carry.Select(s => s.itemId),
                Is.EqualTo(save.loadout.carriedItems.Select(s => s.itemId)));
            Assert.That(ops[0].carry.Select(s => s.count),
                Is.EqualTo(save.loadout.carriedItems.Select(s => s.count)));
        }

        [Test]
        public void CombinedAssetAndLoadoutChanges_AppendLoadoutAfterAssetOps()
        {
            WarehouseLedger ledger = LedgerOf(
                Row(1, 5101, 1, 0, 0, wearer: 1001),
                Row(2, 5001, 10, 0, 0));
            SaveData save = SaveWith(new List<GridItemStack>
            {
                new GridItemStack(5001, 10, 0, 0),
                new GridItemStack(5301, 4, 1, 0),
            }, weaponItemId: 5102);
            save.characters.Add(new CharacterSave(1002, 0, 0));
            save.loadout = new LoadoutSave
            {
                partyCharacterIds = new[] { 1002, 1001 },
                difficultyId = DifficultyTier.Tier2,
                carriedItems = new List<ItemStack> { new ItemStack(5001, 3) },
            };

            Assert.That(Diff(ledger, save).Select(o => o.k), Is.EqualTo(new[]
            {
                NetOpKind.Recruit, NetOpKind.Del, NetOpKind.Add, NetOpKind.Wear,
                NetOpKind.Party, NetOpKind.Difficulty, NetOpKind.Carry,
            }));
        }
    }
}
