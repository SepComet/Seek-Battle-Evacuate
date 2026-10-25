using System.Collections.Generic;
using NUnit.Framework;
using SepCore.Battle;
using SepCore.CustomComponent;
using SepCore.Definition;

namespace SepCore.Tests
{
    [TestFixture]
    public class BattleRuntimeEnemyTurnTests
    {
        [Test]
        public void AdvanceEnemyTurn_WhenPlayerIsCurrent_ReturnsNoOp()
        {
            BattleRuntime runtime = TestBattle.Create1v1(TestBattle.Player());

            BattleStep step = runtime.AdvanceEnemyTurn();

            Assert.Null(step.Result);
            Assert.IsEmpty(step.Events);
            Assert.AreEqual(1, runtime.CurrentActorUnitId);
            Assert.AreEqual(120, runtime.GetUnit(1).CurrentHp);
            Assert.AreEqual(50, runtime.GetUnit(2).CurrentHp);
        }

        [Test]
        public void EnemyWithoutUsableActions_SkipsItsTurn()
        {
            TestConfigProvider config = TestConfigProvider.Standard1v1();
            // 敌人唯一行动改为 MP 不足（100 > 敌人 MP 0），无可用行动
            config.AddAction(TestConfigs.AttackAction(201, 0, BattleStatType.ATK, -1000, 100));
            BattleRuntime runtime = TestBattle.Create1v1(TestBattle.Player(), config);

            runtime.SubmitCommand(TestBattle.Attack(1, 2));
            BattleStep step = runtime.AdvanceEnemyTurn();

            Assert.Null(step.Result);
            Assert.IsEmpty(step.Events);
            Assert.AreEqual(1, runtime.CurrentActorUnitId);
            Assert.AreEqual(2, runtime.RoundNumber);
            Assert.AreEqual(120, runtime.GetUnit(1).CurrentHp);
        }

        [Test]
        public void EnemyWithNoActionIds_SkipsItsTurn()
        {
            TestConfigProvider config = TestConfigProvider.Standard1v1();
            config.AddEnemy(TestConfigs.Enemy(3001, 50, 8));
            BattleRuntime runtime = TestBattle.Create1v1(TestBattle.Player(), config);

            runtime.SubmitCommand(TestBattle.Attack(1, 2));
            BattleStep step = runtime.AdvanceEnemyTurn();

            Assert.Null(step.Result);
            Assert.IsEmpty(step.Events);
            Assert.AreEqual(1, runtime.CurrentActorUnitId);
            Assert.AreEqual(2, runtime.RoundNumber);
        }

        [Test]
        public void EnemyWithMultipleActions_ChoosesByRandomSequence()
        {
            TestConfigProvider config = TestConfigProvider.Standard1v1();
            config.AddAction(TestConfigs.AttackAction(201, 0, BattleStatType.ATK, -1000));
            config.AddAction(TestConfigs.AttackAction(202, -30, BattleStatType.None, 0));
            config.AddEnemy(TestConfigs.Enemy(3001, 50, 8, 201, 202));
            config.AddParty(TestConfigs.Party(4001, 3001));

            // 随机序列 [0] -> 选 201，伤害 = ATK = 8
            BattleRuntime runtimeA = TestBattle.Create1v1(TestBattle.Player(), config, new TestRandomSource(0));
            runtimeA.SubmitCommand(TestBattle.Attack(1, 2));
            BattleStep stepA = runtimeA.AdvanceEnemyTurn();
            Assert.AreEqual(112, stepA.Events[0].AfterHp);

            // 随机序列 [1] -> 选 202，固定伤害 30
            BattleRuntime runtimeB = TestBattle.Create1v1(TestBattle.Player(), config, new TestRandomSource(1));
            runtimeB.SubmitCommand(TestBattle.Attack(1, 2));
            BattleStep stepB = runtimeB.AdvanceEnemyTurn();
            Assert.AreEqual(90, stepB.Events[0].AfterHp);
        }

        [Test]
        public void SameInputsAndRandomSequence_ProduceSameRecords()
        {
            TestConfigProvider config = TestConfigProvider.Standard1v1();
            config.AddAction(TestConfigs.AttackAction(201, 0, BattleStatType.ATK, -1000));
            config.AddAction(TestConfigs.AttackAction(202, -30, BattleStatType.None, 0));
            config.AddEnemy(TestConfigs.Enemy(3001, 50, 8, 201, 202));
            config.AddParty(TestConfigs.Party(4001, 3001));

            BattleRuntime runtimeA = TestBattle.Create1v1(TestBattle.Player(), config, new TestRandomSource(0, 0, 0, 0));
            BattleRuntime runtimeB = TestBattle.Create1v1(TestBattle.Player(), config, new TestRandomSource(0, 0, 0, 0));

            List<string> recordsA = CollectRecords(runtimeA);
            List<string> recordsB = CollectRecords(runtimeB);

            CollectionAssert.AreEqual(recordsA, recordsB);
        }

        private static List<string> CollectRecords(BattleRuntime runtime)
        {
            List<string> records = new List<string>();
            int guard = 0;
            while (runtime.Result == null && guard++ < 200)
            {
                BattleUnit actor = runtime.CurrentActor;
                BattleStep step;
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

                foreach (BattleEvent battleEvent in step.Events)
                {
                    records.Add(string.Format("{0}:{1}:{2}:{3}:{4}",
                        battleEvent.ActorUnitId, battleEvent.ActionConfigId, battleEvent.TargetUnitId,
                        battleEvent.AfterHp, battleEvent.AfterMp));
                }
            }

            return records;
        }
    }
}