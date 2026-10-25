using System.Collections.Generic;
using NUnit.Framework;
using SepCore.Battle;
using SepCore.Definition;
using SepCore.Exploration;

namespace SepCore.Tests
{
    [TestFixture]
    public class PlayerPartyBuilderTests
    {
        [Test]
        public void Build_NoEquipment_BuildsBaseStats()
        {
            CharacterConfig char1 = TestConfigFactory.Create<CharacterConfig>(
                "Id", 1,
                "Name", "Hero",
                "MaxHp", 100,
                "MaxMp", 50,
                "Atk", 20,
                "Mat", 15,
                "Speed", 10,
                "AttackActionId", 101,
                "SkillActionId", 201
            );

            SaveData save = new SaveData
            {
                loadout = new LoadoutSave
                {
                    partyCharacterIds = new int[] { 1 }
                },
                characters = new List<CharacterSave>
                {
                    new CharacterSave(1, 0, 0)
                }
            };

            List<PlayerUnitState> result = PlayerPartyBuilder.Build(
                save,
                id => id == 1 ? char1 : null,
                id => null
            );

            Assert.AreEqual(1, result.Count);
            PlayerUnitState player = result[0];
            Assert.AreEqual(1, player.CharacterId);
            Assert.AreEqual(1, player.PartyOrder);
            Assert.AreEqual(100, player.CurrentHp);
            Assert.AreEqual(100, player.MaxHp);
            Assert.AreEqual(50, player.CurrentMp);
            Assert.AreEqual(50, player.MaxMp);
            Assert.AreEqual(20, player.Atk);
            Assert.AreEqual(15, player.Mat);
            Assert.AreEqual(10, player.Speed);
            Assert.AreEqual(101, player.AttackActionId);
            Assert.AreEqual(201, player.SkillActionId);
        }

        [Test]
        public void Build_WithWeaponAndArmor_AppliesBonusToStatsAndMaxLimits()
        {
            CharacterConfig char1 = TestConfigFactory.Create<CharacterConfig>(
                "Id", 1,
                "Name", "Hero",
                "MaxHp", 100,
                "MaxMp", 50,
                "Atk", 20,
                "Mat", 15,
                "Speed", 10,
                "AttackActionId", 101,
                "SkillActionId", 201
            );

            ItemConfig sword = TestConfigFactory.Create<ItemConfig>(
                "Id", 1001,
                "Name", "Sword",
                "MaxHpBonus", 0,
                "MaxMpBonus", 0,
                "AtkBonus", 15,
                "MatBonus", 0,
                "SpeedBonus", 2
            );

            ItemConfig shield = TestConfigFactory.Create<ItemConfig>(
                "Id", 2001,
                "Name", "Shield",
                "MaxHpBonus", 30,
                "MaxMpBonus", 10,
                "AtkBonus", 0,
                "MatBonus", 5,
                "SpeedBonus", -1
            );

            SaveData save = new SaveData
            {
                loadout = new LoadoutSave
                {
                    partyCharacterIds = new int[] { 1 }
                },
                characters = new List<CharacterSave>
                {
                    new CharacterSave(1, 1001, 2001)
                }
            };

            Dictionary<int, ItemConfig> items = new Dictionary<int, ItemConfig>
            {
                { 1001, sword },
                { 2001, shield }
            };

            List<PlayerUnitState> result = PlayerPartyBuilder.Build(
                save,
                id => id == 1 ? char1 : null,
                id => items.TryGetValue(id, out var v) ? v : null
            );

            Assert.AreEqual(1, result.Count);
            PlayerUnitState player = result[0];
            Assert.AreEqual(130, player.MaxHp);
            Assert.AreEqual(130, player.CurrentHp);
            Assert.AreEqual(60, player.MaxMp);
            Assert.AreEqual(60, player.CurrentMp);
            Assert.AreEqual(35, player.Atk);
            Assert.AreEqual(20, player.Mat);
            Assert.AreEqual(11, player.Speed);
        }

        [Test]
        public void Build_PreservesPartyOrder()
        {
            CharacterConfig c1 = TestConfigFactory.Create<CharacterConfig>("Id", 1, "MaxHp", 100, "MaxMp", 50, "Atk", 10, "Mat", 10, "Speed", 10, "AttackActionId", 1, "SkillActionId", 2);
            CharacterConfig c2 = TestConfigFactory.Create<CharacterConfig>("Id", 2, "MaxHp", 100, "MaxMp", 50, "Atk", 10, "Mat", 10, "Speed", 10, "AttackActionId", 1, "SkillActionId", 2);

            SaveData save = new SaveData
            {
                loadout = new LoadoutSave
                {
                    partyCharacterIds = new int[] { 2, 1 }
                }
            };

            List<PlayerUnitState> result = PlayerPartyBuilder.Build(
                save,
                id => id == 1 ? c1 : (id == 2 ? c2 : null),
                id => null
            );

            Assert.AreEqual(2, result.Count);
            Assert.AreEqual(2, result[0].CharacterId);
            Assert.AreEqual(1, result[0].PartyOrder);
            Assert.AreEqual(1, result[1].CharacterId);
            Assert.AreEqual(2, result[1].PartyOrder);
        }
    }
}
