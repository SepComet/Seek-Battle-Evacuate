using System.Collections.Generic;
using NUnit.Framework;
using SepCore.Battle;
using SepCore.Definition;

namespace SepCore.Tests
{
    [TestFixture]
    public class RoundPlayerStateWritebackTests
    {
        [Test]
        public void Apply_Victory_KeepsSurvivorsAndRevivesDefeated()
        {
            List<PlayerUnitState> players = new List<PlayerUnitState>
            {
                TestBattle.Player(currentHp: 80, maxHp: 120, partyOrder: 1, characterId: 1001),
                TestBattle.Player(currentHp: 0, maxHp: 150, partyOrder: 2, characterId: 1002),
            };
            BattleResult result = new BattleResult(1, BattleOutcomeType.Victory,
                new List<BattlePlayerResult>
                {
                    new BattlePlayerResult(1001, 80, 25, false, false),
                    new BattlePlayerResult(1002, 0, 0, true, false),
                });

            RoundPlayerStateWriteback.Apply(players, result, 1, 1);

            Assert.AreEqual(80, players[0].CurrentHp);
            Assert.AreEqual(25, players[0].CurrentMp);
            Assert.AreEqual(1, players[1].CurrentHp);
            Assert.AreEqual(1, players[1].CurrentMp);
        }

        [Test]
        public void Apply_AllEscaped_KeepsEscapeTimeValues()
        {
            List<PlayerUnitState> players = new List<PlayerUnitState>
            {
                TestBattle.Player(currentHp: 60, maxHp: 120, currentMp: 10, partyOrder: 1, characterId: 1001),
            };
            BattleResult result = new BattleResult(1, BattleOutcomeType.AllEscaped,
                new List<BattlePlayerResult>
                {
                    new BattlePlayerResult(1001, 60, 10, false, true),
                });

            RoundPlayerStateWriteback.Apply(players, result, 1, 1);

            Assert.AreEqual(60, players[0].CurrentHp);
            Assert.AreEqual(10, players[0].CurrentMp);
        }

        [Test]
        public void Apply_PartialEscapeDefeat_KeepsEscapersAndRevivesDefeated()
        {
            List<PlayerUnitState> players = new List<PlayerUnitState>
            {
                TestBattle.Player(currentHp: 90, maxHp: 120, currentMp: 30, partyOrder: 1, characterId: 1001),
                TestBattle.Player(currentHp: 0, maxHp: 150, partyOrder: 2, characterId: 1002),
            };
            BattleResult result = new BattleResult(1, BattleOutcomeType.PartialEscapeDefeat,
                new List<BattlePlayerResult>
                {
                    new BattlePlayerResult(1001, 90, 30, false, true),
                    new BattlePlayerResult(1002, 0, 0, true, false),
                });

            RoundPlayerStateWriteback.Apply(players, result, 1, 1);

            Assert.AreEqual(90, players[0].CurrentHp);
            Assert.AreEqual(30, players[0].CurrentMp);
            Assert.AreEqual(1, players[1].CurrentHp);
            Assert.AreEqual(1, players[1].CurrentMp);
        }

        [Test]
        public void Apply_TotalDefeat_DoesNotWriteBack()
        {
            List<PlayerUnitState> players = new List<PlayerUnitState>
            {
                TestBattle.Player(currentHp: 50, maxHp: 120, currentMp: 20, partyOrder: 1, characterId: 1001),
            };
            BattleResult result = new BattleResult(1, BattleOutcomeType.TotalDefeat,
                new List<BattlePlayerResult>
                {
                    new BattlePlayerResult(1001, 0, 0, true, false),
                });

            RoundPlayerStateWriteback.Apply(players, result, 1, 1);

            Assert.AreEqual(50, players[0].CurrentHp);
            Assert.AreEqual(20, players[0].CurrentMp);
        }

        [Test]
        public void Apply_UnknownCharacterId_IsIgnored()
        {
            List<PlayerUnitState> players = new List<PlayerUnitState>
            {
                TestBattle.Player(characterId: 1001),
            };
            BattleResult result = new BattleResult(1, BattleOutcomeType.Victory,
                new List<BattlePlayerResult>
                {
                    new BattlePlayerResult(9999, 10, 10, false, false),
                });

            Assert.DoesNotThrow(() => RoundPlayerStateWriteback.Apply(players, result, 1, 1));
            Assert.AreEqual(120, players[0].CurrentHp);
        }

        [Test]
        public void Apply_NullInputs_DoNotThrow()
        {
            List<PlayerUnitState> players = new List<PlayerUnitState>
            {
                TestBattle.Player(characterId: 1001),
            };
            BattleResult result = new BattleResult(1, BattleOutcomeType.Victory,
                new List<BattlePlayerResult>());

            Assert.DoesNotThrow(() => RoundPlayerStateWriteback.Apply(null, result, 1, 1));
            Assert.DoesNotThrow(() => RoundPlayerStateWriteback.Apply(players, null, 1, 1));
        }
    }
}
