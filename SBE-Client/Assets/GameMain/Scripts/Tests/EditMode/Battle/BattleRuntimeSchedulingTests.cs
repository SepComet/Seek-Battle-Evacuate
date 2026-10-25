using System.Collections.Generic;
using NUnit.Framework;
using SepCore.Battle;
using SepCore.CustomComponent;
using SepCore.Definition;

namespace SepCore.Tests
{
    [TestFixture]
    public class BattleRuntimeSchedulingTests
    {
        [Test]
        public void FasterEnemy_ActsBeforeSlowerPlayer()
        {
            TestConfigProvider config = TestConfigProvider.Standard1v1();
            config.AddEnemy(TestConfigs.EnemyWithSpeed(3001, 50, 8, 20, 201));
            BattleRuntime runtime = BattleRuntime.Create(TestBattle.Encounter(),
                new List<PlayerUnitState> { TestBattle.Player(speed: 8) }, config, new TestRandomSource());

            Assert.AreEqual(2, runtime.CurrentActorUnitId);
            BattleStep step = runtime.AdvanceEnemyTurn();
            Assert.AreEqual(1, step.Events[0].TargetUnitId);
            Assert.AreEqual(1, runtime.CurrentActorUnitId);
        }

        [Test]
        public void SameSpeed_PlayerActsBeforeEnemy()
        {
            TestConfigProvider config = TestConfigProvider.Standard1v1();
            config.AddEnemy(TestConfigs.EnemyWithSpeed(3001, 50, 8, 12, 201));
            BattleRuntime runtime = BattleRuntime.Create(TestBattle.Encounter(),
                new List<PlayerUnitState> { TestBattle.Player(speed: 12) }, config, new TestRandomSource());

            Assert.AreEqual(1, runtime.CurrentActorUnitId);
            runtime.SubmitCommand(TestBattle.Attack(1, 2));
            Assert.AreEqual(2, runtime.CurrentActorUnitId);
        }

        [Test]
        public void SameFactionSameSpeed_PartyOrderDecides()
        {
            TestConfigProvider config = TestConfigProvider.Standard1v1();
            config.AddEnemy(TestConfigs.EnemyWithSpeed(3001, 500, 8, 5, 201));
            List<PlayerUnitState> players = new List<PlayerUnitState>
            {
                TestBattle.Player(speed: 10, partyOrder: 2, characterId: 1002),
                TestBattle.Player(speed: 10, partyOrder: 1, characterId: 1001)
            };

            BattleRuntime runtime = BattleRuntime.Create(TestBattle.Encounter(), players, config, new TestRandomSource());

            // 玩家同速：PartyOrder 1 的角色先行动（1001 是第二个单位）
            Assert.AreEqual(2, runtime.CurrentActorUnitId);
            Assert.AreEqual(1001, runtime.GetUnit(2).ConfigId);
        }

        [Test]
        public void EachUnitActsOncePerRound_2v2()
        {
            TestConfigProvider config = TestConfigProvider.Standard1v1();
            config.AddEnemy(TestConfigs.EnemyWithSpeed(3001, 500, 8, 20, 201));
            config.AddEnemy(TestConfigs.EnemyWithSpeed(3002, 500, 8, 5, 201));
            config.AddParty(TestConfigs.Party(4001, 3001, 3002));
            List<PlayerUnitState> players = new List<PlayerUnitState>
            {
                TestBattle.Player(speed: 15, partyOrder: 1),
                TestBattle.Player(speed: 10, partyOrder: 2, characterId: 1002)
            };

            BattleRuntime runtime = BattleRuntime.Create(TestBattle.Encounter(), players, config, new TestRandomSource());

            List<int> round1 = CollectActorsInRound(runtime);
            CollectionAssert.AreEqual(new List<int> { 3, 1, 2, 4 }, round1);
            Assert.AreEqual(2, runtime.RoundNumber);
        }

        [Test]
        public void PreemptiveFirstRound_AllPlayersActBeforeEnemies()
        {
            TestConfigProvider config = BuildTankyConfig();
            List<PlayerUnitState> players = BuildFourPlayers(speed: 8);

            BattleRuntime runtime = BattleRuntime.Create(
                new BattleEncounter(1, 4001, true), players, config, new TestRandomSource());

            List<int> round1 = CollectActorsInRound(runtime);
            CollectionAssert.AreEqual(new List<int> { 1, 2, 3, 4, 5, 6, 7, 8 }, round1);

            List<int> round2 = CollectActorsInRound(runtime);
            CollectionAssert.AreEqual(new List<int> { 5, 6, 7, 8, 1, 2, 3, 4 }, round2);
        }

        [Test]
        public void NonPreemptive_RoundOne_UsesSpeedOrder()
        {
            TestConfigProvider config = BuildTankyConfig();
            List<PlayerUnitState> players = BuildFourPlayers(speed: 8);

            BattleRuntime runtime = BattleRuntime.Create(
                new BattleEncounter(1, 4001, false), players, config, new TestRandomSource());

            List<int> round1 = CollectActorsInRound(runtime);
            // 敌人速度 20 高于玩家 8：第一轮就按速度排
            CollectionAssert.AreEqual(new List<int> { 5, 6, 7, 8, 1, 2, 3, 4 }, round1);
        }

        [Test]
        public void RepeatedEnemyConfig_HasDistinctIds_AndCanDieSeparately()
        {
            TestConfigProvider config = TestConfigProvider.Standard1v1();
            config.AddAction(TestConfigs.AttackAction(2, 0, BattleStatType.ATK, -10000));
            config.AddParty(TestConfigs.Party(4001, 3001, 3001));

            BattleRuntime runtime = BattleRuntime.Create(TestBattle.Encounter(),
                new List<PlayerUnitState> { TestBattle.Player() }, config, new TestRandomSource());

            Assert.AreEqual(3, runtime.Units.Length);
            Assert.AreEqual(3001, runtime.GetUnit(2).ConfigId);
            Assert.AreEqual(3001, runtime.GetUnit(3).ConfigId);
            Assert.AreNotEqual(2, 3);

            // 只击杀 2 号敌人：3 号保持存活，战斗继续
            runtime.SubmitCommand(TestBattle.Attack(1, 2, 2));
            Assert.True(runtime.GetUnit(2).IsDefeated);
            Assert.False(runtime.GetUnit(3).IsDefeated);
            Assert.False(runtime.IsCompleted);

            // 敌人 3 反击后轮到玩家
            runtime.AdvanceEnemyTurn();
            Assert.AreEqual(1, runtime.CurrentActorUnitId);

            // 击杀 3 号敌人：胜利
            runtime.SubmitCommand(TestBattle.Attack(1, 3, 2));
            Assert.True(runtime.GetUnit(3).IsDefeated);
            Assert.AreEqual(BattleOutcomeType.Victory, runtime.Result.Outcome);
        }

        [Test]
        public void Create_With4Players4Enemies_Succeeds()
        {
            TestConfigProvider config = TestConfigProvider.Standard1v1();
            config.AddParty(TestConfigs.Party(4005, 3001, 3001, 3001, 3001));

            BattleRuntime runtime = BattleRuntime.Create(new BattleEncounter(1, 4005, false),
                BuildFourPlayers(speed: 12), config, new TestRandomSource());

            Assert.NotNull(runtime);
            Assert.AreEqual(8, runtime.Units.Length);
            HashSet<int> unitIds = new HashSet<int>();
            for (int i = 1; i <= 8; i++)
            {
                Assert.AreEqual(i, runtime.GetUnit(i).UnitId);
                Assert.True(unitIds.Add(i));
            }

            Assert.AreEqual(1, runtime.CurrentActorUnitId);
        }

        [Test]
        public void UnitThatDiedBeforeItsTurn_IsSkipped()
        {
            TestConfigProvider config = TestConfigProvider.Standard1v1();
            config.AddAction(TestConfigs.AttackAction(2, 0, BattleStatType.ATK, -10000));
            config.AddEnemy(TestConfigs.EnemyWithSpeed(3001, 50, 8, 20, 201));
            config.AddEnemy(TestConfigs.EnemyWithSpeed(3002, 50, 8, 1, 201));
            config.AddParty(TestConfigs.Party(4001, 3001, 3002));

            BattleRuntime runtime = BattleRuntime.Create(TestBattle.Encounter(),
                new List<PlayerUnitState> { TestBattle.Player(speed: 10) }, config, new TestRandomSource());

            // 第 1 轮：敌人 3001（速度 20）先行动
            Assert.AreEqual(2, runtime.CurrentActorUnitId);
            runtime.AdvanceEnemyTurn();

            // 玩家（速度 10）直接击杀 3002（速度 1，尚未行动）
            Assert.AreEqual(1, runtime.CurrentActorUnitId);
            runtime.SubmitCommand(TestBattle.Attack(1, 3, 2));

            // 3002 阵亡被移出候选，第 2 轮由 3001 开始
            Assert.AreEqual(2, runtime.RoundNumber);
            Assert.AreEqual(2, runtime.CurrentActorUnitId);
            Assert.False(runtime.ActedUnitIds.Contains(3));
        }

        [Test]
        public void SameInputsAndRandom_4v4_ProduceSameRecords()
        {
            TestConfigProvider config = TestConfigProvider.Standard1v1();
            config.AddAction(TestConfigs.AttackAction(202, -30, BattleStatType.None, 0));
            config.AddEnemy(TestConfigs.EnemyWithSpeed(3001, 500, 8, 20, 201, 202));
            config.AddEnemy(TestConfigs.EnemyWithSpeed(3002, 500, 8, 20, 201, 202));
            config.AddParty(TestConfigs.Party(4001, 3001, 3002));

            List<PlayerUnitState> players = new List<PlayerUnitState>
            {
                TestBattle.Player(speed: 15, partyOrder: 1),
                TestBattle.Player(speed: 10, partyOrder: 2, characterId: 1002)
            };
            TestRandomSource randomA = new TestRandomSource(0, 0, 0, 0, 0, 0, 0, 0);
            TestRandomSource randomB = new TestRandomSource(0, 0, 0, 0, 0, 0, 0, 0);

            BattleRuntime runtimeA = BattleRuntime.Create(TestBattle.Encounter(), players, config, randomA);
            BattleRuntime runtimeB = BattleRuntime.Create(TestBattle.Encounter(), players, config, randomB);

            CollectionAssert.AreEqual(CollectRecords(runtimeA), CollectRecords(runtimeB));
        }

        /// <summary>
        /// 防御性敌人配置：500 HP 保证两轮内无人阵亡，用于纯顺序断言。
        /// </summary>
        private static TestConfigProvider BuildTankyConfig()
        {
            TestConfigProvider config = TestConfigProvider.Standard1v1();
            config.AddEnemy(TestConfigs.EnemyWithSpeed(3001, 500, 8, 20, 201));
            config.AddEnemy(TestConfigs.EnemyWithSpeed(3002, 500, 8, 20, 201));
            config.AddEnemy(TestConfigs.EnemyWithSpeed(3003, 500, 8, 20, 201));
            config.AddEnemy(TestConfigs.EnemyWithSpeed(3004, 500, 8, 20, 201));
            config.AddParty(TestConfigs.Party(4001, 3001, 3002, 3003, 3004));
            return config;
        }

        private static List<PlayerUnitState> BuildFourPlayers(int speed)
        {
            return new List<PlayerUnitState>
            {
                TestBattle.Player(speed: speed, partyOrder: 1, characterId: 1001),
                TestBattle.Player(speed: speed, partyOrder: 2, characterId: 1002),
                TestBattle.Player(speed: speed, partyOrder: 3, characterId: 1003),
                TestBattle.Player(speed: speed, partyOrder: 4, characterId: 1004)
            };
        }

        /// <summary>
        /// 驱动当前一轮全部行动，返回行动者 ID 序列；战斗提前结束则立即返回。
        /// </summary>
        private static List<int> CollectActorsInRound(BattleRuntime runtime)
        {
            List<int> actors = new List<int>();
            int startRound = runtime.RoundNumber;
            while (runtime.Result == null && runtime.RoundNumber == startRound)
            {
                BattleUnit actor = runtime.CurrentActor;
                actors.Add(actor.UnitId);
                if (actor.Faction == BattleFactionType.Player)
                {
                    int targetUnitId = FirstActiveEnemy(runtime);
                    runtime.SubmitCommand(TestBattle.Attack(actor.UnitId, targetUnitId));
                }
                else
                {
                    runtime.AdvanceEnemyTurn();
                }
            }

            return actors;
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
                    step = runtime.SubmitCommand(TestBattle.Attack(actor.UnitId, FirstActiveEnemy(runtime)));
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

        private static int FirstActiveEnemy(BattleRuntime runtime)
        {
            foreach (BattleUnit unit in runtime.Units)
            {
                if (unit.Faction == BattleFactionType.Enemy && BattleRuntime.IsActive(unit))
                {
                    return unit.UnitId;
                }
            }

            return 0;
        }
    }
}