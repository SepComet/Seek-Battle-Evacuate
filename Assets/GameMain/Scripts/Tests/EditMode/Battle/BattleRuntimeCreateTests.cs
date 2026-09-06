using System.Collections.Generic;
using NUnit.Framework;
using SepCore.Battle;
using SepCore.CustomComponent;
using SepCore.Definition;

namespace SepCore.Tests
{
    [TestFixture]
    public class BattleRuntimeCreateTests
    {
        [Test]
        public void Create_WithStandard1v1Inputs_BuildsExpectedUnits()
        {
            BattleRuntime runtime = TestBattle.Create1v1(TestBattle.Player());

            Assert.NotNull(runtime);
            Assert.False(runtime.IsCompleted);
            Assert.AreEqual(1, runtime.RoundNumber);
            Assert.AreEqual(2, runtime.Units.Length);

            BattleUnit player = runtime.Units[0];
            Assert.AreEqual(1, player.UnitId);
            Assert.AreEqual(BattleFactionType.Player, player.Faction);
            Assert.AreEqual(1001, player.ConfigId);
            Assert.AreEqual(1, player.PartyOrder);
            Assert.AreEqual(120, player.CurrentHp);
            Assert.AreEqual(120, player.MaxHp);
            Assert.AreEqual(40, player.CurrentMp);
            Assert.AreEqual(14, player.Atk);
            Assert.AreEqual(6, player.Mat);
            Assert.AreEqual(12, player.Speed);
            CollectionAssert.AreEqual(new List<int> { 1, 101 }, player.ActionIds);

            BattleUnit enemy = runtime.Units[1];
            Assert.AreEqual(2, enemy.UnitId);
            Assert.AreEqual(BattleFactionType.Enemy, enemy.Faction);
            Assert.AreEqual(3001, enemy.ConfigId);
            Assert.AreEqual(50, enemy.CurrentHp);
            Assert.AreEqual(8, enemy.Atk);
            CollectionAssert.AreEqual(new List<int> { 201 }, enemy.ActionIds);

            Assert.AreEqual(1, runtime.CurrentActorUnitId);
        }

        [Test]
        public void Create_WithNullInputs_ReturnsNull()
        {
            PlayerUnitState playerUnit = TestBattle.Player();
            BattleEncounter encounter = TestBattle.Encounter();
            TestConfigProvider config = TestConfigProvider.Standard1v1();
            IRoundRandomSource random = new TestRandomSource();

            Assert.Null(BattleRuntime.Create(null, new List<PlayerUnitState> { playerUnit }, config, random));
            Assert.Null(BattleRuntime.Create(encounter, null, config, random));
            Assert.Null(BattleRuntime.Create(encounter, new List<PlayerUnitState>(), config, random));
            Assert.Null(BattleRuntime.Create(encounter, new List<PlayerUnitState> { playerUnit }, null, random));
            Assert.Null(BattleRuntime.Create(encounter, new List<PlayerUnitState> { playerUnit }, config, null));
        }

        [Test]
        public void Create_WithMissingEnemyParty_ReturnsNull()
        {
            TestConfigProvider config = TestConfigProvider.Standard1v1();

            Assert.Null(BattleRuntime.Create(new BattleEncounter(1, 999, false),
                new List<PlayerUnitState> { TestBattle.Player() }, config, new TestRandomSource()));
        }

        [Test]
        public void Create_WithMissingEnemyConfig_ReturnsNull()
        {
            TestConfigProvider config = TestConfigProvider.Standard1v1();
            config.AddParty(TestConfigs.Party(5001, 9999));

            Assert.Null(BattleRuntime.Create(new BattleEncounter(1, 5001, false),
                new List<PlayerUnitState> { TestBattle.Player() }, config, new TestRandomSource()));
        }
    }
}