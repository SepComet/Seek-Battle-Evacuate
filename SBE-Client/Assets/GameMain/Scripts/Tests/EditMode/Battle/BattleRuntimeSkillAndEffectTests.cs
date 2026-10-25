using System.Collections.Generic;
using NUnit.Framework;
using SepCore.Battle;
using SepCore.CustomComponent;
using SepCore.Definition;

namespace SepCore.Tests
{
    [TestFixture]
    public class BattleRuntimeSkillAndEffectTests
    {
        [Test]
        public void PlayerSkill_SingleEnemyDamage_CalculatesDamageAndDeductsMp()
        {
            TestConfigProvider config = TestConfigProvider.StandardWithSkills();
            // 本用例验证单体伤害公式：本地覆盖 101 为旧版高伤技能（真实配表 101 现为减速）
            config.AddAction(TestConfigs.SkillAction(101, BattleTargetType.SingleEnemy, -5, BattleStatType.ATK, -1500, 10));
            PlayerUnitState player = TestBattle.Player(atk: 14, currentMp: 40);
            BattleRuntime runtime = TestBattle.Create1v1(player, config);

            // 角色 1 技能 101：FlatValue = -5, SourceStat = ATK(14), Permille = -1500, MpCost = 10
            // 伤害 = -5 + 14 * (-1500) / 1000 = -5 + (-21) = -26
            // 敌人 3001 初始 50 HP -> 24 HP
            BattleStep step = runtime.SubmitCommand(TestBattle.Skill(1, 101, 2));

            Assert.Null(step.Result);
            Assert.AreEqual(1, step.Events.Count);
            BattleEvent battleEvent = step.Events[0];
            Assert.AreEqual(1, battleEvent.ActorUnitId);
            Assert.AreEqual(BattleActionType.Skill, battleEvent.CommandType);
            Assert.AreEqual(101, battleEvent.ActionConfigId);
            Assert.AreEqual(2, battleEvent.TargetUnitId);
            Assert.AreEqual(50, battleEvent.BeforeHp);
            Assert.AreEqual(24, battleEvent.AfterHp);
            Assert.AreEqual(24, runtime.GetUnit(2).CurrentHp);
            Assert.AreEqual(30, runtime.GetUnit(1).CurrentMp);
            Assert.AreEqual(2, runtime.CurrentActorUnitId);
        }

        [Test]
        public void PlayerSkill_SingleAllyHeal_HealsAllyAndClampsToMaxHp()
        {
            TestConfigProvider config = TestConfigProvider.StandardWithSkills();
            config.AddParty(TestConfigs.Party(4002, 3001));

            // 角色 1 受伤（50/120 HP）；角色 2（治疗者，MAT 5，技能 102，速度较低）
            PlayerUnitState p1 = TestBattle.Player(currentHp: 50, maxHp: 120, speed: 10, partyOrder: 1, characterId: 1001);
            PlayerUnitState p2 = TestBattle.Player(currentHp: 150, maxHp: 150, speed: 20, partyOrder: 2, characterId: 1002,
                mat: 5, skillActionId: 102);

            BattleEncounter encounter = new BattleEncounter(1, 4002, true); // 先制：玩家全先行
            BattleRuntime runtime = BattleRuntime.Create(encounter, new List<PlayerUnitState> { p1, p2 }, config, new TestRandomSource());

            // 角色 2 速度更高，先手行动（UnitId = 2）
            Assert.AreEqual(2, runtime.CurrentActorUnitId);

            // 技能 102：FlatValue = 20, SourceStat = MAT(5), Permille = 1000, MpCost = 10
            // 治疗量 = 20 + 5 * 1000 / 1000 = 25
            // 目标角色 1（UnitId = 1）：50 -> 75 HP
            BattleStep step = runtime.SubmitCommand(TestBattle.Skill(2, 102, 1));

            Assert.AreEqual(1, step.Events.Count);
            BattleEvent battleEvent = step.Events[0];
            Assert.AreEqual(2, battleEvent.ActorUnitId);
            Assert.AreEqual(1, battleEvent.TargetUnitId);
            Assert.AreEqual(50, battleEvent.BeforeHp);
            Assert.AreEqual(75, battleEvent.AfterHp);
            Assert.AreEqual(75, runtime.GetUnit(1).CurrentHp);
            Assert.AreEqual(30, runtime.GetUnit(2).CurrentMp);
        }

        [Test]
        public void PlayerSkill_SingleAllyHeal_CanTargetSelf()
        {
            TestConfigProvider config = TestConfigProvider.StandardWithSkills();
            // 角色 2 自身受伤（80/150 HP，MAT 5，技能 102）
            PlayerUnitState p2 = TestBattle.Player(currentHp: 80, maxHp: 150, speed: 10, partyOrder: 1, characterId: 1002,
                mat: 5, skillActionId: 102);

            BattleRuntime runtime = TestBattle.Create1v1(p2, config);
            Assert.AreEqual(1, runtime.CurrentActorUnitId);

            // 治疗自身（UnitId = 1）
            BattleStep step = runtime.SubmitCommand(TestBattle.Skill(1, 102, 1));

            Assert.AreEqual(1, step.Events.Count);
            Assert.AreEqual(1, step.Events[0].TargetUnitId);
            Assert.AreEqual(80, step.Events[0].BeforeHp);
            Assert.AreEqual(105, step.Events[0].AfterHp);
            Assert.AreEqual(105, runtime.GetUnit(1).CurrentHp);
            Assert.AreEqual(30, runtime.GetUnit(1).CurrentMp);
        }

        [Test]
        public void PlayerSkill_SingleAllyHeal_ClampsAtMaxHp()
        {
            TestConfigProvider config = TestConfigProvider.StandardWithSkills();
            // 角色 2 仅缺 5 点血（145/150 HP）
            PlayerUnitState p2 = TestBattle.Player(currentHp: 145, maxHp: 150, speed: 10, partyOrder: 1, characterId: 1002,
                mat: 5, skillActionId: 102);

            BattleRuntime runtime = TestBattle.Create1v1(p2, config);

            // 治疗 25，但上限为 150
            BattleStep step = runtime.SubmitCommand(TestBattle.Skill(1, 102, 1));

            Assert.AreEqual(150, step.Events[0].AfterHp);
            Assert.AreEqual(150, runtime.GetUnit(1).CurrentHp);
        }

        [Test]
        public void PlayerSkill_AllEnemiesDamage_HitsAllActiveEnemies()
        {
            TestConfigProvider config = TestConfigProvider.StandardWithSkills();
            // 敌人队伍 4002：敌人 3001（50 HP）和敌人 3002（40 HP）
            config.AddEnemy(TestConfigs.Enemy(3002, 40, 8, 201));
            config.AddParty(TestConfigs.Party(4002, 3001, 3002));

            // 角色 3（MAT 16，技能 103 全体伤害）
            PlayerUnitState p3 = TestBattle.Player(currentHp: 90, maxHp: 90, speed: 20, partyOrder: 1, characterId: 1003,
                mat: 16, skillActionId: 103);

            BattleEncounter encounter = new BattleEncounter(1, 4002, false);
            BattleRuntime runtime = BattleRuntime.Create(encounter, new List<PlayerUnitState> { p3 }, config, new TestRandomSource());

            // 技能 103：FlatValue = -2, SourceStat = MAT(16), Permille = -800, MpCost = 10
            // 伤害 = -2 + 16 * (-800) / 1000 = -2 + (-12) = -14
            // 全体目标由内核自动展开，传入空列表
            BattleStep step = runtime.SubmitCommand(TestBattle.Skill(1, 103));

            Assert.AreEqual(2, step.Events.Count);
            // 敌人 1（UnitId = 2）：50 -> 36
            Assert.AreEqual(2, step.Events[0].TargetUnitId);
            Assert.AreEqual(50, step.Events[0].BeforeHp);
            Assert.AreEqual(36, step.Events[0].AfterHp);
            Assert.AreEqual(36, runtime.GetUnit(2).CurrentHp);

            // 敌人 2（UnitId = 3）：40 -> 26
            Assert.AreEqual(3, step.Events[1].TargetUnitId);
            Assert.AreEqual(40, step.Events[1].BeforeHp);
            Assert.AreEqual(26, step.Events[1].AfterHp);
            Assert.AreEqual(26, runtime.GetUnit(3).CurrentHp);

            Assert.AreEqual(30, runtime.GetUnit(1).CurrentMp);
        }

        [Test]
        public void PlayerSkill_InsufficientMp_RejectsCommandWithoutSideEffects()
        {
            TestConfigProvider config = TestConfigProvider.StandardWithSkills();
            // 玩家当前仅剩 5 MP，技能 101 需要 10 MP
            PlayerUnitState player = TestBattle.Player(currentMp: 5);
            BattleRuntime runtime = TestBattle.Create1v1(player, config);

            BattleStep step = runtime.SubmitCommand(TestBattle.Skill(1, 101, 2));

            Assert.Null(step.Result);
            Assert.IsEmpty(step.Events);
            Assert.AreEqual(1, runtime.CurrentActorUnitId);
            Assert.AreEqual(5, runtime.GetUnit(1).CurrentMp);
            Assert.AreEqual(50, runtime.GetUnit(2).CurrentHp);
        }

        [Test]
        public void PlayerSkill_InvalidTarget_DeadTarget_IsRejected()
        {
            TestConfigProvider config = TestConfigProvider.StandardWithSkills();
            config.AddEnemy(TestConfigs.Enemy(3002, 50, 8, 201));
            config.AddParty(TestConfigs.Party(4002, 3001, 3002));

            PlayerUnitState player = TestBattle.Player(atk: 100);
            BattleEncounter encounter = new BattleEncounter(1, 4002, true);
            BattleRuntime runtime = BattleRuntime.Create(encounter, new List<PlayerUnitState> { player }, config, new TestRandomSource());

            // 普攻击杀敌人 1（UnitId = 2）
            runtime.SubmitCommand(TestBattle.Attack(1, 2));
            Assert.True(runtime.GetUnit(2).IsDefeated);

            // 尝试对已阵亡的敌人 1 释放技能
            BattleStep step = runtime.SubmitCommand(TestBattle.Skill(1, 101, 2));

            Assert.Null(step.Result);
            Assert.IsEmpty(step.Events);
            Assert.AreEqual(40, runtime.GetUnit(1).CurrentMp);
        }

        [Test]
        public void PlayerSkill_InvalidTarget_WrongFaction_IsRejected()
        {
            TestConfigProvider config = TestConfigProvider.StandardWithSkills();
            BattleRuntime runtime = TestBattle.Create1v1(TestBattle.Player(), config);

            // 伤害技能（SingleEnemy）选友方自己（UnitId = 1）
            BattleStep step1 = runtime.SubmitCommand(TestBattle.Skill(1, 101, 1));
            Assert.Null(step1.Result);
            Assert.IsEmpty(step1.Events);
            Assert.AreEqual(1, runtime.CurrentActorUnitId);

            // 治疗技能（SingleAlly）选敌方（UnitId = 2）
            PlayerUnitState healer = TestBattle.Player(skillActionId: 102);
            BattleRuntime runtime2 = TestBattle.Create1v1(healer, config);
            BattleStep step2 = runtime2.SubmitCommand(TestBattle.Skill(1, 102, 2));
            Assert.Null(step2.Result);
            Assert.IsEmpty(step2.Events);
            Assert.AreEqual(1, runtime2.CurrentActorUnitId);
        }

        [Test]
        public void PlayerSkill_InvalidTarget_WrongCount_IsRejected()
        {
            TestConfigProvider config = TestConfigProvider.StandardWithSkills();
            BattleRuntime runtime = TestBattle.Create1v1(TestBattle.Player(), config);

            // 单体技能传空目标
            BattleStep stepEmpty = runtime.SubmitCommand(TestBattle.Skill(1, 101));
            Assert.Null(stepEmpty.Result);
            Assert.IsEmpty(stepEmpty.Events);

            // 单体技能传多个目标
            BattleStep stepMultiple = runtime.SubmitCommand(TestBattle.Skill(1, 101, 2, 2));
            Assert.Null(stepMultiple.Result);
            Assert.IsEmpty(stepMultiple.Events);
        }

        [Test]
        public void PlayerSkill_SelfTarget_AlwaysTargetsSelf()
        {
            TestConfigProvider config = TestConfigProvider.StandardWithSkills();
            // 技能 105：Self 目标，治疗 30，MP 消耗 5
            config.AddAction(TestConfigs.SkillAction(105, BattleTargetType.Self, 30, BattleStatType.None, 0, 5));

            // 角色 2 自身受伤（80/150 HP，技能 105）
            PlayerUnitState p2 = TestBattle.Player(currentHp: 80, maxHp: 150, speed: 10, partyOrder: 1,
                characterId: 1002, skillActionId: 105);
            BattleRuntime runtime = TestBattle.Create1v1(p2, config);
            Assert.AreEqual(1, runtime.CurrentActorUnitId);

            // 空目标由内核展开为施法者自己
            BattleStep step = runtime.SubmitCommand(TestBattle.Skill(1, 105));

            Assert.AreEqual(1, step.Events.Count);
            Assert.AreEqual(1, step.Events[0].TargetUnitId);
            Assert.AreEqual(80, step.Events[0].BeforeHp);
            Assert.AreEqual(110, step.Events[0].AfterHp);
            Assert.AreEqual(35, runtime.GetUnit(1).CurrentMp);
        }

        [Test]
        public void PlayerSkill_SelfTarget_WrongTarget_IsRejected()
        {
            TestConfigProvider config = TestConfigProvider.StandardWithSkills();
            config.AddAction(TestConfigs.SkillAction(105, BattleTargetType.Self, 30, BattleStatType.None, 0, 5));

            PlayerUnitState p2 = TestBattle.Player(speed: 10, partyOrder: 1, characterId: 1002, skillActionId: 105);
            BattleRuntime runtime = TestBattle.Create1v1(p2, config);

            // Self 技能指定敌人（UnitId = 2）为目标
            BattleStep step = runtime.SubmitCommand(TestBattle.Skill(1, 105, 2));

            Assert.Null(step.Result);
            Assert.IsEmpty(step.Events);
            Assert.AreEqual(40, runtime.GetUnit(1).CurrentMp);
            Assert.AreEqual(1, runtime.CurrentActorUnitId);
        }

        [Test]
        public void PlayerSkill_AllAlliesHeal_HitsAllActiveAllies()
        {
            TestConfigProvider config = TestConfigProvider.StandardWithSkills();
            config.AddParty(TestConfigs.Party(4002, 3001));
            // 技能 106：AllAllies 全体友方治疗，FlatValue = 25（SourceStat None，无加成）
            config.AddAction(TestConfigs.SkillAction(106, BattleTargetType.AllAllies, 25, BattleStatType.None, 0, 10));

            PlayerUnitState p1 = TestBattle.Player(currentHp: 100, maxHp: 150, speed: 10, partyOrder: 1, characterId: 1001);
            PlayerUnitState p2 = TestBattle.Player(currentHp: 120, maxHp: 120, speed: 20, partyOrder: 2, characterId: 1002,
                mat: 5, skillActionId: 106);

            BattleEncounter encounter = new BattleEncounter(1, 4002, true); // 先制：玩家全先行
            BattleRuntime runtime = BattleRuntime.Create(encounter, new List<PlayerUnitState> { p1, p2 }, config, new TestRandomSource());

            // 角色 2（UnitId = 2）速度更高先手；空目标由内核展开为全部存活友方
            Assert.AreEqual(2, runtime.CurrentActorUnitId);
            BattleStep step = runtime.SubmitCommand(TestBattle.Skill(2, 106));

            Assert.AreEqual(2, step.Events.Count);
            // 角色 1：100 -> 125
            Assert.AreEqual(1, step.Events[0].TargetUnitId);
            Assert.AreEqual(100, step.Events[0].BeforeHp);
            Assert.AreEqual(125, step.Events[0].AfterHp);
            // 角色 2 满血：120 -> 120（钳制不超过 MaxHP）
            Assert.AreEqual(2, step.Events[1].TargetUnitId);
            Assert.AreEqual(120, step.Events[1].BeforeHp);
            Assert.AreEqual(120, step.Events[1].AfterHp);

            Assert.AreEqual(30, runtime.GetUnit(2).CurrentMp);
        }

        [Test]
        public void EnemyTurn_UsableSkill_SelectsAndExecutes()
        {
            TestConfigProvider config = TestConfigProvider.StandardWithSkills();
            // 敌人配置：MaxMp = 20, Atk = 8, Mat = 10, Actions = [201 (atk), 202 (skill)]
            EnemyConfig enemy = TestConfigFactory.Create<EnemyConfig>(
                "Id", 3001, "Name", "法师敌人", "MaxHp", 50, "MaxMp", 20, "Atk", 8, "Mat", 10,
                "Speed", 20, "ThreatLevelId", 1, "AiType", EnemyAiType.Random,
                "ActionIds", new List<int> { 201, 202 }, "DropTableId", 0);
            config.AddEnemy(enemy);

            PlayerUnitState player = TestBattle.Player(speed: 10);
            // 敌人速度 20 > 玩家 10，敌人先手
            // 随机源注入 1（在 2 个可用行动中选择索引 1，即 Action 202 技能）
            BattleRuntime runtime = BattleRuntime.Create(TestBattle.Encounter(), new List<PlayerUnitState> { player },
                config, new TestRandomSource(1));

            Assert.AreEqual(2, runtime.CurrentActorUnitId);

            // 推进敌人回合
            BattleStep step = runtime.AdvanceEnemyTurn();

            Assert.AreEqual(1, step.Events.Count);
            BattleEvent battleEvent = step.Events[0];
            Assert.AreEqual(2, battleEvent.ActorUnitId);
            Assert.AreEqual(202, battleEvent.ActionConfigId);
            Assert.AreEqual(1, battleEvent.TargetUnitId);
            // 技能 202：FlatValue = -4, SourceStat = MAT(10), Permille = -1200, MpCost = 8
            // 伤害 = -4 + 10 * (-1200) / 1000 = -4 + (-12) = -16
            // 玩家 120 HP -> 104 HP；敌人 20 MP -> 12 MP
            Assert.AreEqual(120, battleEvent.BeforeHp);
            Assert.AreEqual(104, battleEvent.AfterHp);
            Assert.AreEqual(104, runtime.GetUnit(1).CurrentHp);
            Assert.AreEqual(12, runtime.GetUnit(2).CurrentMp);
        }

        [Test]
        public void EnemyTurn_AllEnemiesSkill_HitsAllPlayersWithoutExtraRandom()
        {
            TestConfigProvider config = TestConfigProvider.StandardWithSkills();
            // 敌人全体技能 203：AllEnemies, FlatValue = -10, MpCost = 5
            config.AddAction(TestConfigs.SkillAction(203, BattleTargetType.AllEnemies, -10, BattleStatType.None, 0, 5));
            EnemyConfig enemy = TestConfigFactory.Create<EnemyConfig>(
                "Id", 3001, "Name", "BOSS", "MaxHp", 100, "MaxMp", 20, "Atk", 10, "Mat", 10,
                "Speed", 30, "ThreatLevelId", 1, "AiType", EnemyAiType.Random,
                "ActionIds", new List<int> { 203 }, "DropTableId", 0);
            config.AddEnemy(enemy);

            PlayerUnitState p1 = TestBattle.Player(currentHp: 100, maxHp: 100, speed: 10, partyOrder: 1, characterId: 1001);
            PlayerUnitState p2 = TestBattle.Player(currentHp: 100, maxHp: 100, speed: 10, partyOrder: 2, characterId: 1002);

            // 随机源只包含 1 个值（甚至不需要，因为 action 只有 1 个且目标是全体）
            BattleRuntime runtime = BattleRuntime.Create(TestBattle.Encounter(), new List<PlayerUnitState> { p1, p2 },
                config, new TestRandomSource());

            BattleStep step = runtime.AdvanceEnemyTurn();

            Assert.AreEqual(2, step.Events.Count);
            Assert.AreEqual(90, runtime.GetUnit(1).CurrentHp);
            Assert.AreEqual(90, runtime.GetUnit(2).CurrentHp);
            Assert.AreEqual(15, runtime.GetUnit(3).CurrentMp);
        }
    }
}
