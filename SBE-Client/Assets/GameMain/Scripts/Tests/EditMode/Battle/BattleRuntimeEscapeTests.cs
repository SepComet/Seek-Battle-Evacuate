using System.Collections.Generic;
using NUnit.Framework;
using SepCore.Battle;
using SepCore.CustomComponent;
using SepCore.Definition;

namespace SepCore.Tests
{
    [TestFixture]
    public class BattleRuntimeEscapeTests
    {
        [Test]
        public void Escape_Success_MarksEscapedAndConsumesTurn()
        {
            BattleRuntime runtime = TestBattle.Create1v1(TestBattle.Player(), EscapeConfig(1000));
            Assert.AreEqual(1, runtime.CurrentActorUnitId);

            BattleStep step = runtime.SubmitCommand(TestBattle.Escape(1));

            // 1v1 单人逃跑成功即全体逃跑，战斗结束
            Assert.NotNull(step.Result);
            Assert.AreEqual(BattleOutcomeType.AllEscaped, step.Result.Outcome);
            Assert.AreEqual(1, step.Events.Count);
            BattleEvent battleEvent = step.Events[0];
            Assert.AreEqual(BattleActionType.Escape, battleEvent.CommandType);
            Assert.AreEqual(0, battleEvent.ActionConfigId);
            Assert.AreEqual(1, battleEvent.ActorUnitId);
            Assert.AreEqual(1, battleEvent.TargetUnitId);
            Assert.AreEqual(120, battleEvent.BeforeHp);
            Assert.AreEqual(120, battleEvent.AfterHp);

            Assert.True(runtime.GetUnit(1).IsEscaped);
            Assert.False(runtime.GetUnit(1).IsDefeated);
            Assert.AreEqual(40, runtime.GetUnit(1).CurrentMp);
            Assert.AreEqual(0, runtime.CurrentActorUnitId);
        }

        [Test]
        public void Escape_Fail_StaysInBattleAndConsumesTurn()
        {
            BattleRuntime runtime = TestBattle.Create1v1(TestBattle.Player(), EscapeConfig(0));

            BattleStep step = runtime.SubmitCommand(TestBattle.Escape(1));

            Assert.Null(step.Result);
            Assert.AreEqual(1, step.Events.Count);
            Assert.AreEqual(BattleActionType.Escape, step.Events[0].CommandType);
            Assert.False(runtime.GetUnit(1).IsEscaped);
            Assert.AreEqual(120, runtime.GetUnit(1).CurrentHp);
            Assert.AreEqual(40, runtime.GetUnit(1).CurrentMp);

            // 失败同样消耗本轮行动机会
            Assert.AreEqual(2, runtime.CurrentActorUnitId);
        }

        [Test]
        public void Escape_SingleEscapeDoesNotEndBattle()
        {
            TestConfigProvider config = EscapeConfig(1000);
            config.AddParty(TestConfigs.Party(4002, 3001));

            PlayerUnitState pA = TestBattle.Player(speed: 30, partyOrder: 1, characterId: 1001);
            PlayerUnitState pB = TestBattle.Player(currentHp: 120, maxHp: 120, speed: 10, partyOrder: 2, characterId: 1002);
            BattleRuntime runtime = BattleRuntime.Create(new BattleEncounter(1, 4002, false),
                new List<PlayerUnitState> { pA, pB }, config, new TestRandomSource());

            BattleStep step = runtime.SubmitCommand(TestBattle.Escape(1));

            // 队友仍在战斗，战斗继续；B（10速）快于敌人（8速），轮到 B
            Assert.Null(step.Result);
            Assert.Null(runtime.Result);
            Assert.True(runtime.GetUnit(1).IsEscaped);
            Assert.AreEqual(2, runtime.CurrentActorUnitId);
        }

        [Test]
        public void Escape_AllEscaped_WhenLastPlayerEscapes()
        {
            BattleRuntime runtime = TestBattle.Create1v1(TestBattle.Player(), EscapeConfig(1000));

            BattleStep step = runtime.SubmitCommand(TestBattle.Escape(1));

            Assert.NotNull(step.Result);
            Assert.AreEqual(BattleOutcomeType.AllEscaped, step.Result.Outcome);
            Assert.AreEqual(1, step.Result.EncounterId);
            Assert.AreEqual(0, runtime.CurrentActorUnitId);
            Assert.AreEqual(1, step.Result.Players.Count);
            Assert.True(step.Result.Players[0].Escaped);
            Assert.False(step.Result.Players[0].WasDefeated);
            Assert.AreEqual(120, step.Result.Players[0].CurrentHp);
        }

        [Test]
        public void Escape_PartialEscapeDefeat_WhenEscapersAndDeadRemain()
        {
            TestConfigProvider config = EscapeConfig(1000);
            config.AddParty(TestConfigs.Party(4002, 3001));

            PlayerUnitState pA = TestBattle.Player(speed: 30, partyOrder: 1, characterId: 1001);
            PlayerUnitState pB = TestBattle.Player(currentHp: 5, maxHp: 120, speed: 10, partyOrder: 2, characterId: 1002);
            BattleRuntime runtime = BattleRuntime.Create(new BattleEncounter(1, 4002, false),
                new List<PlayerUnitState> { pA, pB }, config, new TestRandomSource());

            // A 逃跑成功，轮到 B；B 攻击未杀死敌人后轮到敌人
            runtime.SubmitCommand(TestBattle.Escape(1));
            Assert.AreEqual(2, runtime.CurrentActorUnitId);
            runtime.SubmitCommand(TestBattle.Attack(2, 3));
            Assert.AreEqual(3, runtime.CurrentActorUnitId);

            // 敌人只能选中仍在场的 B 并击杀：逃跑者存活、其余全灭
            BattleStep enemyStep = runtime.AdvanceEnemyTurn();

            Assert.AreEqual(2, enemyStep.Events[0].TargetUnitId);
            Assert.NotNull(enemyStep.Result);
            Assert.AreEqual(BattleOutcomeType.PartialEscapeDefeat, enemyStep.Result.Outcome);
            Assert.True(runtime.GetUnit(1).IsEscaped);
            Assert.True(runtime.GetUnit(2).IsDefeated);
        }

        [Test]
        public void Escape_VictoryKeepsEscapedTeammate()
        {
            TestConfigProvider config = EscapeConfig(1000);
            config.AddParty(TestConfigs.Party(4002, 3001));

            PlayerUnitState pA = TestBattle.Player(speed: 30, partyOrder: 1, characterId: 1001);
            PlayerUnitState pB = TestBattle.Player(currentHp: 120, maxHp: 120, atk: 100, speed: 10, partyOrder: 2, characterId: 1002);
            BattleRuntime runtime = BattleRuntime.Create(new BattleEncounter(1, 4002, false),
                new List<PlayerUnitState> { pA, pB }, config, new TestRandomSource());

            runtime.SubmitCommand(TestBattle.Escape(1));
            Assert.AreEqual(2, runtime.CurrentActorUnitId);
            BattleStep killingBlow = runtime.SubmitCommand(TestBattle.Attack(2, 3));

            Assert.NotNull(killingBlow.Result);
            Assert.AreEqual(BattleOutcomeType.Victory, killingBlow.Result.Outcome);
            Assert.AreEqual(2, killingBlow.Result.Players.Count);
            Assert.True(killingBlow.Result.Players[0].Escaped);
            Assert.AreEqual(120, killingBlow.Result.Players[0].CurrentHp);
            Assert.False(killingBlow.Result.Players[1].Escaped);
        }

        [Test]
        public void Escape_InvalidCommand_IsRejected()
        {
            BattleRuntime runtime = TestBattle.Create1v1(TestBattle.Player(), EscapeConfig(1000));

            // 非当前行动者逃跑
            BattleStep wrongActor = runtime.SubmitCommand(
                new BattleCommand(2, BattleActionType.Escape, 0, new List<int>()));
            Assert.Null(wrongActor.Result);
            Assert.IsEmpty(wrongActor.Events);

            // 逃跑带行动 ID
            BattleStep withActionId = runtime.SubmitCommand(
                new BattleCommand(1, BattleActionType.Escape, 101, new List<int>()));
            Assert.Null(withActionId.Result);
            Assert.IsEmpty(withActionId.Events);

            // 逃跑带目标
            BattleStep withTargets = runtime.SubmitCommand(
                new BattleCommand(1, BattleActionType.Escape, 0, new List<int> { 2 }));
            Assert.Null(withTargets.Result);
            Assert.IsEmpty(withTargets.Events);

            Assert.AreEqual(1, runtime.CurrentActorUnitId);
            Assert.AreEqual(1, runtime.RoundNumber);
            Assert.False(runtime.GetUnit(1).IsEscaped);
        }

        [Test]
        public void Escape_StunnedActor_IsRejected()
        {
            TestConfigProvider config = TestConfigProvider.StandardWithSkills();
            config.SetGlobal(TestConfigs.BattleGlobal(1000));
            config.AddAction(TestConfigs.SkillAction(205, BattleTargetType.SingleEnemy, 0,
                BattleStatType.None, 0, 10, BattleStatType.None, BattleStateType.Stun, 1));
            config.AddEnemy(TestConfigFactory.Create<EnemyConfig>(
                "Id", 3001, "Name", "眩晕敌", "MaxHp", 50, "MaxMp", 20, "Atk", 8, "Mat", 0,
                "Speed", 20, "ThreatLevelId", 1, "AiType", EnemyAiType.Random,
                "ActionIds", new List<int> { 205 }, "DropTableId", 0));

            PlayerUnitState player = TestBattle.Player(speed: 10);
            BattleRuntime runtime = TestBattle.Create1v1(player, config);
            runtime.AdvanceEnemyTurn();
            Assert.AreEqual(1, runtime.CurrentActorUnitId);

            BattleStep step = runtime.SubmitCommand(TestBattle.Escape(1));

            Assert.Null(step.Result);
            Assert.IsEmpty(step.Events);
            Assert.False(runtime.GetUnit(1).IsEscaped);
            Assert.AreEqual(1, runtime.CurrentActorUnitId);
        }

        private static TestConfigProvider EscapeConfig(int escapeSuccessPermille)
        {
            TestConfigProvider config = TestConfigProvider.Standard1v1();
            config.SetGlobal(TestConfigs.BattleGlobal(escapeSuccessPermille));
            return config;
        }
    }
}
