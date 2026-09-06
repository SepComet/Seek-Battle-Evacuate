using System.Collections.Generic;
using SepCore.CustomComponent;
using SepCore.Definition;

namespace SepCore.Battle
{
    /// <summary>
    /// 当前战斗的事实唯一来源，仅战斗期间存在。
    /// 调度、指令校验、效果、状态和敌人决策共享同一实例，不复制单位、轮次和状态集合；
    /// 内部不为函数调用建立请求/响应 DTO，玩家与敌人的行动都走同一条执行管线并产生同一组 BattleEvent。
    /// 支持 1 至 4 人普通攻击与技能行动闭环。
    /// </summary>
    internal sealed class BattleRuntime
    {
        /// <summary>
        /// 触发本场战斗的遭遇 ID，用于结果回写关联地图敌人。
        /// </summary>
        public readonly int EncounterId;

        /// <summary>
        /// 本场全部单位，创建顺序即稳定显示顺序（玩家在前）。
        /// </summary>
        public readonly BattleUnit[] Units;

        /// <summary>
        /// 是否为本场先制战斗（只影响第一轮；M1 未启用）。
        /// </summary>
        public readonly bool IsPreemptive;

        /// <summary>
        /// 当前轮次，从 1 开始。
        /// </summary>
        public int RoundNumber = 1;

        /// <summary>
        /// 当前行动者；无行动者或战斗完成时为 0。
        /// </summary>
        public int CurrentActorUnitId;

        /// <summary>
        /// 本轮已获得行动机会的单位 ID。
        /// </summary>
        public readonly HashSet<int> ActedUnitIds = new HashSet<int>();

        /// <summary>
        /// 本轮已行动单位 ID（按行动先后，用于行动栏显示顺序）。
        /// </summary>
        private readonly List<int> _actedOrder = new List<int>();

        /// <summary>
        /// 本次推进产生的待展示行动记录，推进结束时被 BattleStep 取走。
        /// </summary>
        public readonly List<BattleEvent> PendingEvents = new List<BattleEvent>();

        /// <summary>
        /// 战斗结束后的结果；非 null 即表示战斗已完成。
        /// </summary>
        public BattleResult Result;

        private readonly IRoundRandomSource _random;
        private readonly IBattleConfigProvider _config;

        /// <summary>
        /// 是否已完成。
        /// </summary>
        public bool IsCompleted => Result != null;

        /// <summary>
        /// 当前行动者单位；无行动者时返回 null。
        /// </summary>
        public BattleUnit CurrentActor => GetUnit(CurrentActorUnitId);

        private BattleRuntime(int encounterId, BattleUnit[] units, bool isPreemptive,
            IRoundRandomSource random, IBattleConfigProvider config)
        {
            EncounterId = encounterId;
            Units = units;
            IsPreemptive = isPreemptive;
            _random = random;
            _config = config;
        }

        /// <summary>
        /// 从遭遇、单局玩家状态、配置和本局随机源创建唯一战斗运行时。
        /// 任一必要输入缺失或配置缺失时返回 null，表示启动失败且不产生任何副作用。
        /// </summary>
        public static BattleRuntime Create(BattleEncounter encounter, IReadOnlyList<PlayerUnitState> players,
            IBattleConfigProvider config, IRoundRandomSource random)
        {
            if (encounter == null || players == null || players.Count == 0 || config == null || random == null)
            {
                return null;
            }

            EnemyPartyConfig party = config.GetEnemyParty(encounter.EnemyPartyConfigId);
            if (party == null || party.EnemyIds == null || party.EnemyIds.Count == 0)
            {
                return null;
            }

            List<BattleUnit> units = new List<BattleUnit>(players.Count + party.EnemyIds.Count);
            int nextId = 1;

            foreach (PlayerUnitState state in players)
            {
                if (state == null)
                {
                    return null;
                }

                BattleUnit unit = new BattleUnit(nextId++, BattleFactionType.Player, state.CharacterId, state.PartyOrder,
                    state.CurrentHp, state.MaxHp, state.CurrentMp, state.MaxMp,
                    state.Atk, state.Mat, state.Speed);
                if (state.AttackActionId != 0)
                {
                    unit.ActionIds.Add(state.AttackActionId);
                }

                if (state.SkillActionId != 0)
                {
                    unit.ActionIds.Add(state.SkillActionId);
                }

                units.Add(unit);
            }

            int enemyOrder = 1;
            foreach (int enemyId in party.EnemyIds)
            {
                EnemyConfig enemy = config.GetEnemy(enemyId);
                if (enemy == null)
                {
                    return null;
                }

                BattleUnit unit = new BattleUnit(nextId++, BattleFactionType.Enemy, enemy.Id, enemyOrder++,
                    enemy.MaxHp, enemy.MaxHp, enemy.MaxMp, enemy.MaxMp, enemy.Atk, enemy.Mat, enemy.Speed);
                if (enemy.ActionIds != null)
                {
                    unit.ActionIds.AddRange(enemy.ActionIds);
                }

                units.Add(unit);
            }

            BattleRuntime runtime = new BattleRuntime(encounter.EncounterId, units.ToArray(),
                encounter.IsPreemptive, random, config);
            runtime.SelectNextActor();
            return runtime;
        }

        /// <summary>
        /// 按运行时 ID 获取单位；不存在时返回 null。
        /// </summary>
        public BattleUnit GetUnit(int unitId)
        {
            foreach (BattleUnit unit in Units)
            {
                if (unit.UnitId == unitId)
                {
                    return unit;
                }
            }

            return null;
        }

        /// <summary>
        /// 单位是否仍在战斗中（未阵亡且未逃跑）。
        /// </summary>
        public static bool IsActive(BattleUnit unit)
        {
            return unit != null && !unit.IsDefeated && !unit.IsEscaped;
        }

        /// <summary>
        /// 校验并执行当前玩家指令（单次行动）。
        /// 非法指令不消耗 MP、行动机会或随机数；玩家行动后仅选择下一个行动者，
        /// 不自动推进敌人回合——间歇节奏由组件在外部编排。
        /// </summary>
        public BattleStep SubmitCommand(BattleCommand command)
        {
            List<int> resolvedTargets;
            if (!ValidatePlayerCommand(command, out resolvedTargets))
            {
                return new BattleStep(new BattleEvent[0], BuildViewState(), null);
            }

            PendingEvents.Clear();

            BattleUnit actor = GetUnit(command.ActorUnitId);
            if (command.CommandType == BattleActionType.Escape)
            {
                ExecuteEscape(actor);
            }
            else
            {
                ExecuteAction(actor, command.CommandType, command.ActionConfigId, resolvedTargets);
            }

            MarkActed(command.ActorUnitId);
            CheckBattleEnd();

            if (!IsCompleted)
            {
                SelectNextActor();
            }

            return new BattleStep(DrainEvents(), BuildViewState(), Result);
        }

        /// <summary>
        /// 推进当前敌人行动者的自动回合（单次行动）。
        /// 当前行动者不是敌人、没有行动者或战斗已完成时返回无变化步骤；
        /// 眩晕敌人不执行决策，直接消费一次跳过并记为独立一拍；
        /// 与玩家指令共用同一执行管线并产生同一组 BattleEvent。
        /// </summary>
        public BattleStep AdvanceEnemyTurn()
        {
            BattleUnit actor = CurrentActor;
            if (actor == null || actor.Faction != BattleFactionType.Enemy || IsCompleted)
            {
                return new BattleStep(new BattleEvent[0], BuildViewState(), null);
            }

            PendingEvents.Clear();

            if (IsStunned(actor))
            {
                TryConsumeStunSkip(actor);
            }
            else
            {
                ExecuteEnemyTurn(actor);
                MarkActed(actor.UnitId);
            }

            CheckBattleEnd();

            if (!IsCompleted)
            {
                SelectNextActor();
            }

            return new BattleStep(DrainEvents(), BuildViewState(), Result);
        }

        /// <summary>
        /// 推进当前眩晕行动者的跳过（单次行动，独立一拍）。
        /// 当前行动者未被眩晕、没有行动者或战斗已完成时返回无变化步骤；
        /// 组件用它按延迟自动推过眩晕玩家，UI 据此展示跳过行为。
        /// </summary>
        public BattleStep AdvanceStunSkip()
        {
            BattleUnit actor = CurrentActor;
            if (actor == null || IsCompleted || !IsStunned(actor))
            {
                return new BattleStep(new BattleEvent[0], BuildViewState(), null);
            }

            PendingEvents.Clear();

            TryConsumeStunSkip(actor);
            CheckBattleEnd();

            if (!IsCompleted)
            {
                SelectNextActor();
            }

            return new BattleStep(DrainEvents(), BuildViewState(), Result);
        }

        /// <summary>
        /// 指定单位是否处于眩晕（剩余行动机会次数大于 0）。
        /// </summary>
        public static bool IsStunned(BattleUnit unit)
        {
            if (unit == null)
            {
                return false;
            }

            BattleState stun = FindStatus(unit, BattleStateType.Stun);
            return stun != null && stun.RemainingRounds > 0;
        }

        /// <summary>
        /// 构建当前只读视图。
        /// </summary>
        public BattleViewState BuildViewState()
        {
            List<BattleUnitView> unitViews = new List<BattleUnitView>(Units.Length);
            foreach (BattleUnit unit in Units)
            {
                List<BattleStateView> statusViews = new List<BattleStateView>(unit.Statuses.Count);
                foreach (BattleState status in unit.Statuses)
                {
                    statusViews.Add(new BattleStateView(status.Type, status.RemainingRounds));
                }

                unitViews.Add(new BattleUnitView(unit.UnitId, unit.Faction, unit.ConfigId, unit.PartyOrder,
                    unit.CurrentHp, unit.MaxHp, unit.CurrentMp, unit.MaxMp, unit.Speed,
                    unit.IsDefeated, unit.IsEscaped, statusViews));
            }

            List<int> remainingOrder = new List<int>();
            if (!IsCompleted)
            {
                foreach (BattleUnit unit in GetRemainingCandidates())
                {
                    remainingOrder.Add(unit.UnitId);
                }
            }

            List<int> displayOrder = new List<int>();
            if (!IsCompleted)
            {
                displayOrder = GetDisplayOrder();
            }

            List<int> availableActions = new List<int>();
            BattleUnit actor = CurrentActor;
            if (actor != null && actor.Faction == BattleFactionType.Player)
            {
                availableActions.AddRange(actor.ActionIds);
            }

            return new BattleViewState(RoundNumber, CurrentActorUnitId, unitViews, remainingOrder, availableActions, displayOrder);
        }

        private bool ValidatePlayerCommand(BattleCommand command, out List<int> resolvedTargets)
        {
            resolvedTargets = null;
            if (command == null || IsCompleted)
            {
                return false;
            }

            if (command.ActorUnitId != CurrentActorUnitId)
            {
                return false;
            }

            BattleUnit actor = GetUnit(command.ActorUnitId);
            if (actor == null || actor.Faction != BattleFactionType.Player || !IsActive(actor))
            {
                return false;
            }

            // 眩晕行动者不能提交指令，其跳过由 AdvanceStunSkip 按延迟自动推进
            if (IsStunned(actor))
            {
                return false;
            }

            if (command.CommandType == BattleActionType.Escape)
            {
                // 逃跑无配置无目标：行动 ID 为 0 且目标为空；成功率判定在执行时消费随机数
                if (command.ActionConfigId != 0)
                {
                    return false;
                }

                if (command.TargetUnitIds != null && command.TargetUnitIds.Count > 0)
                {
                    return false;
                }

                resolvedTargets = new List<int>();
                return true;
            }

            if (command.CommandType != BattleActionType.Attack && command.CommandType != BattleActionType.Skill)
            {
                return false;
            }

            BattleActionConfig action = _config.GetAction(command.ActionConfigId);
            if (action == null || action.ActionType != command.CommandType)
            {
                return false;
            }

            if (actor.CurrentMp < action.MpCost)
            {
                return false;
            }

            return TryResolvePlayerTargets(command, actor, action, out resolvedTargets);
        }

        private bool TryResolvePlayerTargets(BattleCommand command, BattleUnit actor, BattleActionConfig action,
            out List<int> resolvedTargets)
        {
            resolvedTargets = null;
            switch (action.TargetType)
            {
                case BattleTargetType.SingleEnemy:
                {
                    if (command.TargetUnitIds == null || command.TargetUnitIds.Count != 1)
                    {
                        return false;
                    }

                    BattleUnit target = GetUnit(command.TargetUnitIds[0]);
                    if (target == null || target.Faction == actor.Faction || !IsActive(target))
                    {
                        return false;
                    }

                    resolvedTargets = new List<int> { target.UnitId };
                    return true;
                }
                case BattleTargetType.SingleAlly:
                {
                    if (command.TargetUnitIds == null || command.TargetUnitIds.Count != 1)
                    {
                        return false;
                    }

                    BattleUnit target = GetUnit(command.TargetUnitIds[0]);
                    // SingleAlly 包含施法者自己
                    if (target == null || target.Faction != actor.Faction || !IsActive(target))
                    {
                        return false;
                    }

                    resolvedTargets = new List<int> { target.UnitId };
                    return true;
                }
                case BattleTargetType.Self:
                {
                    if (command.TargetUnitIds != null && command.TargetUnitIds.Count > 0)
                    {
                        if (command.TargetUnitIds.Count != 1 || command.TargetUnitIds[0] != actor.UnitId)
                        {
                            return false;
                        }
                    }

                    resolvedTargets = new List<int> { actor.UnitId };
                    return true;
                }
                case BattleTargetType.AllEnemies:
                {
                    // 全体目标由战斗内核展开，UI 不自行拼装
                    resolvedTargets = new List<int>();
                    foreach (BattleUnit unit in Units)
                    {
                        if (unit.Faction != actor.Faction && IsActive(unit))
                        {
                            resolvedTargets.Add(unit.UnitId);
                        }
                    }

                    return resolvedTargets.Count > 0;
                }
                case BattleTargetType.AllAllies:
                {
                    // 全体友方由战斗内核展开
                    resolvedTargets = new List<int>();
                    foreach (BattleUnit unit in Units)
                    {
                        if (unit.Faction == actor.Faction && IsActive(unit))
                        {
                            resolvedTargets.Add(unit.UnitId);
                        }
                    }

                    return resolvedTargets.Count > 0;
                }
                default:
                    return false;
            }
        }

        /// <summary>
        /// 执行逃跑：用全局逃跑成功率与本局随机源独立判定；成功与失败都消耗本轮行动机会，
        /// 成功者离开候选集与合法目标集合（IsActive 已隔离），不视为阵亡。
        /// </summary>
        private void ExecuteEscape(BattleUnit actor)
        {
            GlobalConfig global = _config.GetGlobal();
            if (_random.RollPermille(global.EscapeSuccessPermille))
            {
                actor.IsEscaped = true;
            }

            PendingEvents.Add(new BattleEvent(actor.UnitId, BattleActionType.Escape, 0, actor.UnitId,
                actor.CurrentHp, actor.CurrentHp, actor.CurrentMp, actor.CurrentMp,
                BattleStateType.None, 0));
        }

        private void ExecuteAction(BattleUnit actor, BattleActionType commandType, int actionConfigId,
            IReadOnlyList<int> targetUnitIds)
        {
            BattleActionConfig action = _config.GetAction(actionConfigId);
            if (action == null)
            {
                return;
            }

            if (action.MpCost > 0 && actor.CurrentMp >= action.MpCost)
            {
                actor.CurrentMp -= action.MpCost;
            }

            if (action.Effects == null)
            {
                return;
            }

            foreach (int targetUnitId in targetUnitIds)
            {
                BattleUnit target = GetUnit(targetUnitId);
                if (target == null)
                {
                    continue;
                }

                foreach (BattleEffect effect in action.Effects)
                {
                    ApplyEffect(actor, target, commandType, actionConfigId, effect);
                }
            }
        }

        private void ApplyEffect(BattleUnit actor, BattleUnit target, BattleActionType commandType,
            int actionConfigId, BattleEffect effect)
        {
            int sourceValue = GetStatValue(actor, effect.SourceStat);
            int change = effect.FlatValue + sourceValue * effect.SourceScalePermille / 1000;

            int beforeHp = target.CurrentHp;
            int beforeMp = target.CurrentMp;

            switch (effect.TargetStat)
            {
                case BattleStatType.HP:
                    target.CurrentHp = Clamp(target.CurrentHp + change, 0, target.MaxHp);
                    if (target.CurrentHp <= 0)
                    {
                        target.CurrentHp = 0;
                        target.IsDefeated = true;
                    }

                    break;
                case BattleStatType.MP:
                    target.CurrentMp = Clamp(target.CurrentMp + change, 0, target.MaxMp);
                    break;
                case BattleStatType.Speed:
                    // M4：速度修改即时生效，下限为 1；调度与视图每次按当前速度重算剩余顺序
                    target.Speed = target.Speed + change < 1 ? 1 : target.Speed + change;
                    break;
                default:
                    // 上限等目标属性修改暂不处理
                    break;
            }

            BattleStateType appliedStatus = BattleStateType.None;
            int statusRemaining = 0;
            if (effect.Status != BattleStateType.None && effect.DurationRounds > 0)
            {
                statusRemaining = ApplyStatus(target, effect.Status, effect.DurationRounds);
                appliedStatus = effect.Status;
            }

            PendingEvents.Add(new BattleEvent(actor.UnitId, commandType, actionConfigId, target.UnitId,
                beforeHp, target.CurrentHp, beforeMp, target.CurrentMp,
                appliedStatus, statusRemaining));
        }

        /// <summary>
        /// 施加战斗状态；同类状态不叠层，保留较长的剩余行动机会次数。返回施加后的剩余次数。
        /// </summary>
        private static int ApplyStatus(BattleUnit target, BattleStateType type, int durationRounds)
        {
            foreach (BattleState status in target.Statuses)
            {
                if (status.Type == type)
                {
                    if (durationRounds > status.RemainingRounds)
                    {
                        status.RemainingRounds = durationRounds;
                    }

                    return status.RemainingRounds;
                }
            }

            target.Statuses.Add(new BattleState(type, durationRounds));
            return durationRounds;
        }

        private static int GetStatValue(BattleUnit unit, BattleStatType stat)
        {
            switch (stat)
            {
                case BattleStatType.HP:
                    return unit.CurrentHp;
                case BattleStatType.MP:
                    return unit.CurrentMp;
                case BattleStatType.MaxHP:
                    return unit.MaxHp;
                case BattleStatType.MaxMP:
                    return unit.MaxMp;
                case BattleStatType.ATK:
                    return unit.Atk;
                case BattleStatType.MAT:
                    return unit.Mat;
                case BattleStatType.Speed:
                    return unit.Speed;
                default:
                    return 0;
            }
        }

        private static int Clamp(int value, int min, int max)
        {
            return value < min ? min : (value > max ? max : value);
        }

        private void MarkActed(int unitId)
        {
            ActedUnitIds.Add(unitId);
            _actedOrder.Add(unitId);
        }

        /// <summary>
        /// 选择本轮下一个未行动的活跃单位；候选集为空时开启下一轮。
        /// 调度规则（文档 6.1）：
        /// 1. 先制第一轮仍有玩家未行动时只在玩家中选择；
        /// 2. 当前速度高者优先；
        /// 3. 同速时玩家优先于敌人；
        /// 4. 同阵营同速时 PartyOrder 小者优先；
        /// 5. 已行动单位不因速度变化再次行动；
        /// 6. 候选集为空时开启下一轮，先制限制只适用于第一轮。
        /// </summary>
        private void SelectNextActor()
        {
            if (IsCompleted)
            {
                CurrentActorUnitId = 0;
                return;
            }

            BattleUnit next = FindNextActor();
            if (next == null)
            {
                RoundNumber++;
                ActedUnitIds.Clear();
                _actedOrder.Clear();
                next = FindNextActor();
            }

            // 眩晕单位仍获得行动机会并停住成为当前行动者，其跳过由 AdvanceEnemyTurn/AdvanceStunSkip
            // 消费为独立一拍（走组件延迟），UI 据此展示该单位行为；此处不直接跳过
            CurrentActorUnitId = next != null ? next.UnitId : 0;
        }

        /// <summary>
        /// 消耗一次眩晕跳过：剩余次数减 1，归零移除；标记已行动并记录跳过事件。返回是否发生跳过。
        /// </summary>
        private bool TryConsumeStunSkip(BattleUnit unit)
        {
            BattleState stun = FindStatus(unit, BattleStateType.Stun);
            if (stun == null || stun.RemainingRounds <= 0)
            {
                return false;
            }

            stun.RemainingRounds--;
            int remaining = stun.RemainingRounds;
            if (remaining <= 0)
            {
                unit.Statuses.Remove(stun);
            }

            MarkActed(unit.UnitId);
            PendingEvents.Add(new BattleEvent(unit.UnitId, BattleActionType.None, 0, unit.UnitId,
                unit.CurrentHp, unit.CurrentHp, unit.CurrentMp, unit.CurrentMp,
                BattleStateType.Stun, remaining));
            return true;
        }

        private static BattleState FindStatus(BattleUnit unit, BattleStateType type)
        {
            foreach (BattleState status in unit.Statuses)
            {
                if (status.Type == type)
                {
                    return status;
                }
            }

            return null;
        }

        /// <summary>
        /// 按调度规则选本轮优先级最高的候选；没有候选时返回 null。
        /// </summary>
        private BattleUnit FindNextActor()
        {
            BattleUnit best = null;
            foreach (BattleUnit unit in Units)
            {
                if (!IsActive(unit) || ActedUnitIds.Contains(unit.UnitId))
                {
                    continue;
                }

                if (IsPreemptiveRound && unit.Faction == BattleFactionType.Enemy && HasUnactedPlayer())
                {
                    continue;
                }

                if (best == null || CompareActors(unit, best) < 0)
                {
                    best = unit;
                }
            }

            return best;
        }

        /// <summary>
        /// 先制限制只适用于第一轮。
        /// </summary>
        private bool IsPreemptiveRound => IsPreemptive && RoundNumber == 1;

        private bool HasUnactedPlayer()
        {
            foreach (BattleUnit unit in Units)
            {
                if (unit.Faction == BattleFactionType.Player && IsActive(unit) && !ActedUnitIds.Contains(unit.UnitId))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 速度高优先；同速玩家优先；同阵营同速 PartyOrder 小优先。返回负值表示 a 优先于 b。
        /// </summary>
        private static int CompareActors(BattleUnit a, BattleUnit b)
        {
            if (a.Speed != b.Speed)
            {
                return b.Speed - a.Speed;
            }

            if (a.Faction != b.Faction)
            {
                return a.Faction == BattleFactionType.Player ? -1 : 1;
            }

            return a.PartyOrder - b.PartyOrder;
        }

        /// <summary>
        /// 本轮尚未行动的候选单位，按调度优先级排序；当前行动者是首个。
        /// 先制第一轮只返回玩家候选（敌人等玩家全部行动后进入候选集）。
        /// </summary>
        private List<BattleUnit> GetRemainingCandidates()
        {
            List<BattleUnit> candidates = new List<BattleUnit>();
            foreach (BattleUnit unit in Units)
            {
                if (!IsActive(unit) || ActedUnitIds.Contains(unit.UnitId))
                {
                    continue;
                }

                if (IsPreemptiveRound && unit.Faction == BattleFactionType.Enemy && HasUnactedPlayer())
                {
                    continue;
                }

                candidates.Add(unit);
            }

            candidates.Sort(CompareActors);
            return candidates;
        }

        /// <summary>
        /// 本轮行动栏显示顺序：已行动单位按行动先后排前，未行动单位按当前调度优先级随后；
        /// 先制第一轮敌人排在全部未行动玩家之后，但仍显示。速度变化后重算。
        /// </summary>
        private List<int> GetDisplayOrder()
        {
            List<int> order = new List<int>(_actedOrder);

            List<BattleUnit> pending = new List<BattleUnit>();
            foreach (BattleUnit unit in Units)
            {
                if (!IsActive(unit) || ActedUnitIds.Contains(unit.UnitId))
                {
                    continue;
                }

                pending.Add(unit);
            }

            pending.Sort(CompareDisplayPriority);
            foreach (BattleUnit unit in pending)
            {
                order.Add(unit.UnitId);
            }

            return order;
        }

        private int CompareDisplayPriority(BattleUnit a, BattleUnit b)
        {
            if (IsPreemptiveRound && HasUnactedPlayer())
            {
                bool aWaiting = a.Faction == BattleFactionType.Enemy;
                bool bWaiting = b.Faction == BattleFactionType.Enemy;
                if (aWaiting != bWaiting)
                {
                    return aWaiting ? 1 : -1;
                }
            }

            return CompareActors(a, b);
        }

        /// <summary>
        /// 敌人随机决策并执行：从 MP 足够的行动中等概率选择，单体行动从合法目标中等概率选择；
        /// 没有可执行行动或合法目标时跳过本次机会。
        /// </summary>
        private void ExecuteEnemyTurn(BattleUnit enemy)
        {
            List<int> usableActions = new List<int>();
            foreach (int actionId in enemy.ActionIds)
            {
                BattleActionConfig action = _config.GetAction(actionId);
                if (action != null && enemy.CurrentMp >= action.MpCost)
                {
                    usableActions.Add(actionId);
                }
            }

            if (usableActions.Count == 0)
            {
                return;
            }

            int chosenActionId = PickOne(usableActions);
            BattleActionConfig chosen = _config.GetAction(chosenActionId);
            if (chosen == null)
            {
                return;
            }

            List<int> targets = ResolveEnemyTargets(enemy, chosen);
            if (targets == null || targets.Count == 0)
            {
                return;
            }

            ExecuteAction(enemy, chosen.ActionType, chosenActionId, targets);
        }

        private List<int> ResolveEnemyTargets(BattleUnit enemy, BattleActionConfig action)
        {
            switch (action.TargetType)
            {
                case BattleTargetType.SingleEnemy:
                {
                    List<int> candidates = new List<int>();
                    foreach (BattleUnit unit in Units)
                    {
                        if (unit.Faction != enemy.Faction && IsActive(unit))
                        {
                            candidates.Add(unit.UnitId);
                        }
                    }

                    if (candidates.Count == 0)
                    {
                        return null;
                    }

                    return new List<int> { PickOne(candidates) };
                }
                case BattleTargetType.AllEnemies:
                {
                    List<int> candidates = new List<int>();
                    foreach (BattleUnit unit in Units)
                    {
                        if (unit.Faction != enemy.Faction && IsActive(unit))
                        {
                            candidates.Add(unit.UnitId);
                        }
                    }

                    return candidates.Count > 0 ? candidates : null;
                }
                case BattleTargetType.SingleAlly:
                {
                    List<int> candidates = new List<int>();
                    foreach (BattleUnit unit in Units)
                    {
                        if (unit.Faction == enemy.Faction && IsActive(unit))
                        {
                            candidates.Add(unit.UnitId);
                        }
                    }

                    if (candidates.Count == 0)
                    {
                        return null;
                    }

                    return new List<int> { PickOne(candidates) };
                }
                case BattleTargetType.AllAllies:
                {
                    List<int> candidates = new List<int>();
                    foreach (BattleUnit unit in Units)
                    {
                        if (unit.Faction == enemy.Faction && IsActive(unit))
                        {
                            candidates.Add(unit.UnitId);
                        }
                    }

                    return candidates.Count > 0 ? candidates : null;
                }
                case BattleTargetType.Self:
                    return new List<int> { enemy.UnitId };
                default:
                    return null;
            }
        }

        /// <summary>
        /// 等概率选一；只有一项时直接返回，不消费随机数。
        /// </summary>
        private int PickOne(IReadOnlyList<int> candidates)
        {
            if (candidates.Count == 1)
            {
                return candidates[0];
            }

            return candidates[_random.NextInt(0, candidates.Count)];
        }
        /// <summary>
        /// 互斥结束判定（文档 5.6）：
        /// Victory=所有敌人阵亡；AllEscaped=全体玩家逃跑且无人阵亡；
        /// PartialEscapeDefeat=有人逃跑、其余在场玩家全部阵亡；TotalDefeat=全体阵亡且无人逃跑。
        /// 仍有玩家在场时不结束。
        /// </summary>
        private void CheckBattleEnd()
        {
            if (Result != null)
            {
                return;
            }

            bool anyActiveEnemy = false;
            int activePlayers = 0;
            int defeatedPlayers = 0;
            int escapedPlayers = 0;

            foreach (BattleUnit unit in Units)
            {
                if (unit.Faction == BattleFactionType.Enemy)
                {
                    if (IsActive(unit))
                    {
                        anyActiveEnemy = true;
                    }
                }
                else if (IsActive(unit))
                {
                    activePlayers++;
                }
                else if (unit.IsDefeated)
                {
                    defeatedPlayers++;
                }
                else if (unit.IsEscaped)
                {
                    escapedPlayers++;
                }
            }

            BattleOutcomeType? outcome = null;
            if (!anyActiveEnemy)
            {
                outcome = BattleOutcomeType.Victory;
            }
            else if (activePlayers == 0)
            {
                if (escapedPlayers > 0 && defeatedPlayers == 0)
                {
                    outcome = BattleOutcomeType.AllEscaped;
                }
                else if (escapedPlayers == 0 && defeatedPlayers > 0)
                {
                    outcome = BattleOutcomeType.TotalDefeat;
                }
                else if (escapedPlayers > 0 && defeatedPlayers > 0)
                {
                    outcome = BattleOutcomeType.PartialEscapeDefeat;
                }
            }

            if (outcome == null)
            {
                return;
            }

            List<BattlePlayerResult> players = new List<BattlePlayerResult>();
            foreach (BattleUnit unit in Units)
            {
                if (unit.Faction == BattleFactionType.Player)
                {
                    players.Add(new BattlePlayerResult(unit.ConfigId, unit.CurrentHp, unit.CurrentMp,
                        unit.IsDefeated, unit.IsEscaped));
                }
            }

            Result = new BattleResult(EncounterId, outcome.Value, players);
            CurrentActorUnitId = 0;
        }

        private List<BattleEvent> DrainEvents()
        {
            List<BattleEvent> drained = new List<BattleEvent>(PendingEvents);
            PendingEvents.Clear();
            return drained;
        }
    }
}
