using System.Collections.Generic;
using NUnit.Framework;
using SepCore.Battle;
using SepCore.CustomComponent;
using SepCore.Definition;

namespace SepCore.Tests
{
    [TestFixture]
    public class BattleRuntimeViewAndLifecycleTests
    {
        [Test]
        public void BuildViewState_ReflectsUnitsAndTurnOrder()
        {
            BattleRuntime runtime = TestBattle.Create1v1(TestBattle.Player());

            BattleViewState view = runtime.BuildViewState();

            Assert.AreEqual(1, view.RoundNumber);
            Assert.AreEqual(1, view.CurrentActorUnitId);
            Assert.AreEqual(2, view.Units.Count);
            CollectionAssert.AreEqual(new List<int> { 1, 2 }, view.RemainingTurnOrder);
            CollectionAssert.AreEqual(new List<int> { 1, 101 }, view.AvailableActionIds);

            BattleUnitView playerView = view.Units[0];
            Assert.AreEqual(BattleFactionType.Player, playerView.Faction);
            Assert.AreEqual(1001, playerView.ConfigId);
            Assert.AreEqual(1, playerView.PartyOrder);
            Assert.AreEqual(120, playerView.CurrentHp);
            Assert.AreEqual(40, playerView.CurrentMp);
            Assert.AreEqual(12, playerView.Speed);
            Assert.False(playerView.IsDefeated);
            Assert.False(playerView.IsEscaped);
            Assert.IsEmpty(playerView.States);

            BattleUnitView enemyView = view.Units[1];
            Assert.AreEqual(BattleFactionType.Enemy, enemyView.Faction);
            Assert.AreEqual(3001, enemyView.ConfigId);
            Assert.AreEqual(50, enemyView.CurrentHp);
            Assert.False(enemyView.IsDefeated);
        }

        [Test]
        public void BuildViewState_AfterPlayerAction_HighlightsEnemyAndKeepsAllUnits()
        {
            BattleRuntime runtime = TestBattle.Create1v1(TestBattle.Player());
            runtime.SubmitCommand(TestBattle.Attack(1, 2));

            BattleViewState view = runtime.BuildViewState();

            Assert.AreEqual(2, view.CurrentActorUnitId);
            // 同轮内列表仍包含全部单位（已行动玩家不被隐藏）
            CollectionAssert.AreEqual(new List<int> { 2 }, view.RemainingTurnOrder);
            Assert.AreEqual(2, view.Units.Count);
            Assert.AreEqual(36, view.Units[1].CurrentHp);
        }

        [Test]
        public void BuildViewState_AfterCompletion_HasNoActor()
        {
            BattleRuntime runtime = TestBattle.Create1v1(TestBattle.Player());
            int guard = 0;
            while (runtime.Result == null && guard++ < 200)
            {
                BattleUnit actor = runtime.CurrentActor;
                if (actor != null && actor.Faction == BattleFactionType.Player)
                {
                    int targetUnitId = 0;
                    foreach (BattleUnit unit in runtime.Units)
                    {
                        if (unit.Faction == BattleFactionType.Enemy && BattleRuntime.IsActive(unit))
                        {
                            targetUnitId = unit.UnitId;
                            break;
                        }
                    }

                    runtime.SubmitCommand(TestBattle.Attack(actor.UnitId, targetUnitId));
                }
                else
                {
                    runtime.AdvanceEnemyTurn();
                }
            }

            BattleViewState view = runtime.BuildViewState();

            Assert.AreEqual(0, view.CurrentActorUnitId);
            Assert.IsEmpty(view.RemainingTurnOrder);
            Assert.IsEmpty(view.AvailableActionIds);
        }

        [Test]
        public void BuildViewState_DisplayOrder_PreemptiveFirstRoundShowsAllUnits()
        {
            TestConfigProvider config = TestConfigProvider.Standard1v1();
            config.AddParty(TestConfigs.Party(4005, 3001, 3001, 3001, 3001));

            List<PlayerUnitState> players = new List<PlayerUnitState>
            {
                TestBattle.Player(speed: 30, partyOrder: 1, characterId: 1001),
                TestBattle.Player(speed: 20, partyOrder: 2, characterId: 1002),
                TestBattle.Player(speed: 15, partyOrder: 3, characterId: 1003),
                TestBattle.Player(speed: 12, partyOrder: 4, characterId: 1004),
            };
            BattleEncounter encounter = new BattleEncounter(1, 4005, true);
            BattleRuntime runtime = BattleRuntime.Create(encounter, players, config, new TestRandomSource());

            BattleViewState view = runtime.BuildViewState();

            // 先制第一轮：剩余候选只有玩家，但行动栏显示全部 8 个，敌人排在玩家之后
            CollectionAssert.AreEqual(new List<int> { 1, 2, 3, 4 }, view.RemainingTurnOrder);
            CollectionAssert.AreEqual(new List<int> { 1, 2, 3, 4, 5, 6, 7, 8 }, view.DisplayOrder);
        }

        [Test]
        public void BuildViewState_DisplayOrder_KeepsActedPrefixAndReordersPending()
        {
            TestConfigProvider config = TestConfigProvider.StandardWithSkills();
            config.AddParty(TestConfigs.Party(4002, 3001));
            config.AddAction(TestConfigs.SkillAction(107, BattleTargetType.SingleEnemy, -15,
                BattleStatType.None, 0, 10, BattleStatType.Speed));
            config.AddEnemy(TestConfigFactory.Create<EnemyConfig>(
                "Id", 3001, "Name", "敌人3001", "MaxHp", 50, "MaxMp", 0, "Atk", 8, "Mat", 0,
                "Speed", 20, "ThreatLevelId", 1, "AiType", EnemyAiType.Random,
                "ActionIds", new List<int> { 201 }, "DropTableId", 0));

            PlayerUnitState pA = TestBattle.Player(speed: 30, partyOrder: 1, characterId: 1001, skillActionId: 107);
            PlayerUnitState pB = TestBattle.Player(currentHp: 120, maxHp: 120, speed: 10, partyOrder: 2, characterId: 1002);
            BattleEncounter encounter = new BattleEncounter(1, 4002, false);
            BattleRuntime runtime = BattleRuntime.Create(encounter, new List<PlayerUnitState> { pA, pB }, config, new TestRandomSource());

            // 开战：A(30)、敌人(20)、B(10)
            CollectionAssert.AreEqual(new List<int> { 1, 3, 2 }, runtime.BuildViewState().DisplayOrder);

            // A 减速敌人 20 -> 5：已行动 A 留在栏首，B 与敌人按新速度重排
            runtime.SubmitCommand(TestBattle.Skill(1, 107, 3));
            BattleViewState view = runtime.BuildViewState();
            CollectionAssert.AreEqual(new List<int> { 1, 2, 3 }, view.DisplayOrder);
            Assert.AreEqual(2, view.CurrentActorUnitId);
        }

        [Test]
        public void SequentialBattles_DoNotInheritState()
        {
            // 第一场打到玩家残血
            BattleRuntime first = TestBattle.Create1v1(TestBattle.Player());
            first.SubmitCommand(TestBattle.Attack(1, 2));
            first.AdvanceEnemyTurn();
            Assert.AreEqual(112, first.GetUnit(1).CurrentHp);

            // 第二场用同一份输入重新创建：应从满值开局
            BattleRuntime second = TestBattle.Create1v1(TestBattle.Player());
            Assert.AreEqual(120, second.GetUnit(1).CurrentHp);
            Assert.AreEqual(50, second.GetUnit(2).CurrentHp);
            Assert.AreEqual(1, second.RoundNumber);
            Assert.AreEqual(1, second.CurrentActorUnitId);
            Assert.False(second.IsCompleted);
        }

        [Test]
        public void SequentialBattles_UseIndependentActedSets()
        {
            BattleRuntime first = TestBattle.Create1v1(TestBattle.Player());
            first.SubmitCommand(TestBattle.Attack(1, 2));
            Assert.IsTrue(first.ActedUnitIds.Contains(1));

            BattleRuntime second = TestBattle.Create1v1(TestBattle.Player());
            Assert.IsEmpty(second.ActedUnitIds);
            Assert.AreEqual(1, second.CurrentActorUnitId);
        }
    }
}