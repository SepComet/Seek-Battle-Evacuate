using System.Collections.Generic;
using NUnit.Framework;
using SepCore.Battle;
using SepCore.CustomComponent;
using SepCore.Definition;

namespace SepCore.Tests
{
    [TestFixture]
    public class BattleRuntimeAttackLoopTests
    {
        [Test]
        public void PlayerAttack_DealsAtkDamageAndHandsTurnToEnemy()
        {
            BattleRuntime runtime = TestBattle.Create1v1(TestBattle.Player());

            BattleStep step = runtime.SubmitCommand(TestBattle.Attack(1, 2));

            Assert.Null(step.Result);
            Assert.AreEqual(1, step.Events.Count);
            BattleEvent battleEvent = step.Events[0];
            Assert.AreEqual(1, battleEvent.ActorUnitId);
            Assert.AreEqual(BattleActionType.Attack, battleEvent.CommandType);
            Assert.AreEqual(1, battleEvent.ActionConfigId);
            Assert.AreEqual(2, battleEvent.TargetUnitId);
            Assert.AreEqual(50, battleEvent.BeforeHp);
            Assert.AreEqual(36, battleEvent.AfterHp);
            Assert.AreEqual(36, runtime.GetUnit(2).CurrentHp);
            Assert.AreEqual(120, runtime.GetUnit(1).CurrentHp);
            Assert.AreEqual(2, runtime.CurrentActorUnitId);
            Assert.AreEqual(1, runtime.RoundNumber);
        }

        [Test]
        public void EnemyCounterAttack_DamagesPlayerAndStartsNextRound()
        {
            BattleRuntime runtime = TestBattle.Create1v1(TestBattle.Player());
            runtime.SubmitCommand(TestBattle.Attack(1, 2));

            BattleStep step = runtime.AdvanceEnemyTurn();

            Assert.Null(step.Result);
            Assert.AreEqual(1, step.Events.Count);
            BattleEvent battleEvent = step.Events[0];
            Assert.AreEqual(2, battleEvent.ActorUnitId);
            Assert.AreEqual(1, battleEvent.TargetUnitId);
            Assert.AreEqual(120, battleEvent.BeforeHp);
            Assert.AreEqual(112, battleEvent.AfterHp);
            Assert.AreEqual(112, runtime.GetUnit(1).CurrentHp);
            Assert.AreEqual(1, runtime.CurrentActorUnitId);
            Assert.AreEqual(2, runtime.RoundNumber);
        }

        [Test]
        public void AttackLoop_EndsWithVictory()
        {
            BattleRuntime runtime = TestBattle.Create1v1(TestBattle.Player());

            BattleStep final = PlayToCompletion(runtime);

            Assert.NotNull(final.Result);
            Assert.AreEqual(BattleOutcomeType.Victory, final.Result.Outcome);
            Assert.AreEqual(1, final.Result.EncounterId);
            Assert.AreEqual(1, final.Result.Players.Count);
            Assert.AreEqual(1001, final.Result.Players[0].CharacterId);
            Assert.False(final.Result.Players[0].WasDefeated);
            Assert.False(final.Result.Players[0].Escaped);
            Assert.Greater(final.Result.Players[0].CurrentHp, 0);
            Assert.True(runtime.IsCompleted);
            Assert.AreEqual(0, runtime.CurrentActorUnitId);
        }

        [Test]
        public void AttackLoop_EndsWithTotalDefeat_WhenPlayerDies()
        {
            BattleRuntime runtime = TestBattle.Create1v1(TestBattle.Player(currentHp: 5, maxHp: 5));

            BattleStep final = PlayToCompletion(runtime);

            Assert.NotNull(final.Result);
            Assert.AreEqual(BattleOutcomeType.TotalDefeat, final.Result.Outcome);
            Assert.AreEqual(0, final.Result.Players[0].CurrentHp);
            Assert.True(final.Result.Players[0].WasDefeated);
            Assert.True(runtime.IsCompleted);
        }

        [Test]
        public void PlayerAttack_WithHealEffect_ClampsHpToMax()
        {
            TestConfigProvider config = TestConfigProvider.Standard1v1();
            config.AddAction(TestConfigs.AttackAction(2, 200, BattleStatType.None, 0));
            BattleRuntime runtime = TestBattle.Create1v1(TestBattle.Player(), config);

            BattleStep step = runtime.SubmitCommand(TestBattle.Attack(1, 2, 2));

            Assert.AreEqual(1, step.Events.Count);
            Assert.AreEqual(50, step.Events[0].AfterHp);
            Assert.AreEqual(50, runtime.GetUnit(2).CurrentHp);
        }

        [Test]
        public void InvalidCommand_WrongActor_IsRejectedWithoutConsumption()
        {
            BattleRuntime runtime = TestBattle.Create1v1(TestBattle.Player());

            BattleStep step = runtime.SubmitCommand(TestBattle.Attack(999, 2));

            Assert.Null(step.Result);
            Assert.IsEmpty(step.Events);
            Assert.AreEqual(1, runtime.CurrentActorUnitId);
            Assert.AreEqual(120, runtime.GetUnit(1).CurrentHp);
            Assert.AreEqual(50, runtime.GetUnit(2).CurrentHp);
        }

        [Test]
        public void InvalidCommand_ItemCommand_IsRejected()
        {
            BattleRuntime runtime = TestBattle.Create1v1(TestBattle.Player());

            BattleStep step = runtime.SubmitCommand(
                new BattleCommand(1, BattleActionType.Item, 0, new List<int> { 2 }));

            Assert.Null(step.Result);
            Assert.IsEmpty(step.Events);
            Assert.AreEqual(1, runtime.CurrentActorUnitId);
        }

        [Test]
        public void InvalidCommand_UnknownAction_IsRejected()
        {
            BattleRuntime runtime = TestBattle.Create1v1(TestBattle.Player());

            // 行动 999 不在配置中
            BattleStep step = runtime.SubmitCommand(
                new BattleCommand(1, BattleActionType.Attack, 999, new List<int> { 2 }));

            Assert.Null(step.Result);
            Assert.IsEmpty(step.Events);
            Assert.AreEqual(1, runtime.CurrentActorUnitId);
        }

        [Test]
        public void InvalidCommand_ActionConfigTypeMismatch_IsRejected()
        {
            TestConfigProvider config = TestConfigProvider.Standard1v1();
            BattleRuntime runtime = TestBattle.Create1v1(TestBattle.Player(), config);

            // 行动 1 是攻击，却用 Skill 类型提交
            BattleStep step = runtime.SubmitCommand(
                new BattleCommand(1, BattleActionType.Skill, 1, new List<int> { 2 }));

            Assert.Null(step.Result);
            Assert.IsEmpty(step.Events);
            Assert.AreEqual(1, runtime.CurrentActorUnitId);
        }

        [Test]
        public void InvalidCommand_AllyTarget_IsRejected()
        {
            BattleRuntime runtime = TestBattle.Create1v1(TestBattle.Player());

            BattleStep step = runtime.SubmitCommand(TestBattle.Attack(1, 1));

            Assert.Null(step.Result);
            Assert.IsEmpty(step.Events);
            Assert.AreEqual(1, runtime.CurrentActorUnitId);
        }

        [Test]
        public void InvalidCommand_NoTarget_IsRejected()
        {
            BattleRuntime runtime = TestBattle.Create1v1(TestBattle.Player());

            BattleStep step = runtime.SubmitCommand(
                new BattleCommand(1, BattleActionType.Attack, 1, new List<int>()));

            Assert.Null(step.Result);
            Assert.IsEmpty(step.Events);
            Assert.AreEqual(1, runtime.CurrentActorUnitId);
        }

        [Test]
        public void CompletedBattle_RejectsFurtherCommands()
        {
            BattleRuntime runtime = TestBattle.Create1v1(TestBattle.Player());
            PlayToCompletion(runtime);
            Assert.True(runtime.IsCompleted);

            BattleStep step = runtime.SubmitCommand(TestBattle.Attack(1, 2));

            Assert.Null(step.Result);
            Assert.IsEmpty(step.Events);
            Assert.AreEqual(0, runtime.CurrentActorUnitId);
        }

        private static BattleStep PlayToCompletion(BattleRuntime runtime)
        {
            BattleStep step = null;
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

                    step = runtime.SubmitCommand(TestBattle.Attack(actor.UnitId, targetUnitId));
                }
                else
                {
                    step = runtime.AdvanceEnemyTurn();
                }
            }

            return step;
        }
    }
}