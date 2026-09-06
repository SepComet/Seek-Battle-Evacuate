using System.Collections.Generic;
using NUnit.Framework;
using SepCore.Battle;
using SepCore.Definition;
using SepCore.Run;

namespace SepCore.Tests
{
    [TestFixture]
    public class RoundSessionTests
    {
        private ItemConfig CreateItem(int id, int value = 50, int stackLimit = 20)
        {
            return TestConfigFactory.Create<ItemConfig>(
                "Id", id,
                "Name", "Item_" + id,
                "Value", value,
                "StackLimit", stackLimit);
        }

        private CharacterConfig CreateChar(int id, int hp = 100, int mp = 50, int atk = 10, int mat = 5, int spd = 10)
        {
            return TestConfigFactory.Create<CharacterConfig>(
                "Id", id,
                "Name", "Char_" + id,
                "MaxHp", hp,
                "MaxMp", mp,
                "Atk", atk,
                "Mat", mat,
                "Speed", spd,
                "WeaponItemId", 0,
                "ArmorItemId", 0,
                "AttackActionId", 1,
                "SkillActionId", 101);
        }

        private GlobalConfig CreateGlobal(int backpackSlots = 16, int safeSlots = 4)
        {
            return TestConfigFactory.Create<GlobalConfig>(
                "BackpackSlotCount", backpackSlots,
                "SecureSlotCount", safeSlots,
                "ReviveHp", 1,
                "ReviveMp", 1,
                "NewGameCharacterIds", new List<int> { 1001, 1002 });
        }

        [Test]
        public void Create_WithValidInputs_InitializesContainersAndParty()
        {
            GlobalConfig global = CreateGlobal(12, 3);
            SaveData save = new SaveData
            {
                characters = new List<CharacterSave>
                {
                    new CharacterSave(1001, 0, 0),
                    new CharacterSave(1002, 0, 0)
                },
                loadout = new LoadoutSave
                {
                    carriedItems = new List<ItemStack>
                    {
                        new ItemStack(5001, 5)
                    }
                }
            };

            RoundSession session = RoundSession.Create(
                DifficultyTier.Tier1,
                12345,
                save,
                global,
                id => CreateChar(id),
                id => CreateItem(id));

            Assert.AreEqual(12, session.Backpack.MaxSlots);
            Assert.AreEqual(3, session.SafeCase.MaxSlots);
            Assert.AreEqual(2, session.Party.Count);
            Assert.AreEqual(1001, session.Party[0].CharacterId);
            Assert.AreEqual(1002, session.Party[1].CharacterId);
            Assert.AreEqual(1, session.Backpack.UsedSlotsCount);
            Assert.AreEqual(5001, session.Backpack.Slots[0].itemId);
            Assert.AreEqual(5, session.Backpack.Slots[0].count);
        }

        [Test]
        public void ToPlayerUnitStates_ExportsExpectedContractList()
        {
            GlobalConfig global = CreateGlobal();
            SaveData save = new SaveData
            {
                characters = new List<CharacterSave> { new CharacterSave(1001, 0, 0) }
            };

            RoundSession session = RoundSession.Create(
                DifficultyTier.Tier1,
                123,
                save,
                global,
                id => CreateChar(id, hp: 150, mp: 60),
                id => CreateItem(id));

            List<PlayerUnitState> playerUnits = session.ToPlayerUnitStates();

            Assert.AreEqual(1, playerUnits.Count);
            Assert.AreEqual(1001, playerUnits[0].CharacterId);
            Assert.AreEqual(150, playerUnits[0].CurrentHp);
            Assert.AreEqual(60, playerUnits[0].CurrentMp);
            Assert.AreEqual(1, playerUnits[0].AttackActionId);
            Assert.AreEqual(101, playerUnits[0].SkillActionId);
        }

        [Test]
        public void ApplyBattleResult_Victory_UpdatesHpAndRevivesDefeated()
        {
            GlobalConfig global = CreateGlobal();
            SaveData save = new SaveData
            {
                characters = new List<CharacterSave>
                {
                    new CharacterSave(1001, 0, 0),
                    new CharacterSave(1002, 0, 0)
                }
            };

            RoundSession session = RoundSession.Create(
                DifficultyTier.Tier1,
                123,
                save,
                global,
                id => CreateChar(id, hp: 100, mp: 50),
                id => CreateItem(id));

            BattleResult battleResult = new BattleResult(
                1,
                BattleOutcomeType.Victory,
                new List<BattlePlayerResult>
                {
                    new BattlePlayerResult(1001, 65, 20, false, false),
                    new BattlePlayerResult(1002, 0, 0, true, false)
                });

            session.ApplyBattleResult(battleResult, reviveHp: 1, reviveMp: 1);

            Assert.AreEqual(65, session.Party[0].CurrentHp);
            Assert.AreEqual(20, session.Party[0].CurrentMp);
            Assert.AreEqual(1, session.Party[1].CurrentHp);
            Assert.AreEqual(1, session.Party[1].CurrentMp);
        }

        [Test]
        public void ApplyBattleResult_TotalDefeat_DoesNotModifyState()
        {
            GlobalConfig global = CreateGlobal();
            SaveData save = new SaveData
            {
                characters = new List<CharacterSave>
                {
                    new CharacterSave(1001, 0, 0)
                }
            };

            RoundSession session = RoundSession.Create(
                DifficultyTier.Tier1,
                123,
                save,
                global,
                id => CreateChar(id, hp: 100, mp: 50),
                id => CreateItem(id));

            BattleResult defeatResult = new BattleResult(
                1,
                BattleOutcomeType.TotalDefeat,
                new List<BattlePlayerResult>
                {
                    new BattlePlayerResult(1001, 0, 0, true, false)
                });

            session.ApplyBattleResult(defeatResult, reviveHp: 1, reviveMp: 1);

            Assert.AreEqual(100, session.Party[0].CurrentHp);
            Assert.AreEqual(50, session.Party[0].CurrentMp);
        }

        [Test]
        public void TryMoveBetweenContainers_TransfersItemsBothDirections()
        {
            GlobalConfig global = CreateGlobal(4, 2);
            RoundItemContainer backpack = new RoundItemContainer(4, id => CreateItem(id, stackLimit: 10));
            RoundItemContainer safeCase = new RoundItemContainer(2, id => CreateItem(id, stackLimit: 10));
            List<RoundCharacterState> party = new List<RoundCharacterState>();

            RoundSession session = new RoundSession(DifficultyTier.Tier1, 1, 1000, backpack, safeCase, party);

            session.Backpack.TryAddItem(5001, 8);

            // 背包移入保险箱
            bool toSafe = session.TryMoveBetweenContainers(fromBackpackToSafe: true, 0, 5, out int movedToSafe);
            Assert.IsTrue(toSafe);
            Assert.AreEqual(5, movedToSafe);
            Assert.AreEqual(3, session.Backpack.Slots[0].count);
            Assert.AreEqual(5, session.SafeCase.Slots[0].count);

            // 保险箱移回背包
            bool toBackpack = session.TryMoveBetweenContainers(fromBackpackToSafe: false, 0, 3, out int movedToBackpack);
            Assert.IsTrue(toBackpack);
            Assert.AreEqual(3, movedToBackpack);
            Assert.AreEqual(6, session.Backpack.Slots[0].count);
            Assert.AreEqual(2, session.SafeCase.Slots[0].count);
        }

        [Test]
        public void TotalLootValue_SumsBothContainers()
        {
            GlobalConfig global = CreateGlobal(4, 2);
            RoundItemContainer backpack = new RoundItemContainer(4, id => CreateItem(id, value: 100));
            RoundItemContainer safeCase = new RoundItemContainer(2, id => CreateItem(id, value: 200));
            List<RoundCharacterState> party = new List<RoundCharacterState>();

            RoundSession session = new RoundSession(DifficultyTier.Tier1, 1, 1000, backpack, safeCase, party);

            session.Backpack.TryAddItem(5001, 3); // 3 * 100 = 300
            session.SafeCase.TryAddItem(5002, 2); // 2 * 200 = 400

            Assert.AreEqual(300, session.Backpack.TotalValue);
            Assert.AreEqual(400, session.SafeCase.TotalValue);
            Assert.AreEqual(700, session.TotalLootValue);
        }
    }
}
