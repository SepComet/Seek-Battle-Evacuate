using SepCore.Definition;

namespace SepCore.Battle
{
    /// <summary>
    /// 一次效果对单个目标的一层行动记录。
    /// 表达行动者、行动、目标以及数值或状态变化；
    /// StatusType 为 None 时表示没有状态变化。
    /// </summary>
    public sealed class BattleEvent
    {
        /// <summary>
        /// 行动者。
        /// </summary>
        public readonly int ActorUnitId;

        /// <summary>
        /// 指令类型，对应配表 BattleActionType。
        /// </summary>
        public readonly BattleActionType CommandType;

        /// <summary>
        /// 攻击或技能配置 ID；逃跑为 0。
        /// </summary>
        public readonly int ActionConfigId;

        /// <summary>
        /// 效果结算的目标单位。
        /// </summary>
        public readonly int TargetUnitId;

        /// <summary>
        /// 效果结算前的 HP。
        /// </summary>
        public readonly int BeforeHp;

        /// <summary>
        /// 效果结算后的 HP。
        /// </summary>
        public readonly int AfterHp;

        /// <summary>
        /// 效果结算前的 MP。
        /// </summary>
        public readonly int BeforeMp;

        /// <summary>
        /// 效果结算后的 MP。
        /// </summary>
        public readonly int AfterMp;

        /// <summary>
        /// 施加或刷新的状态类型；无状态变化时为 None。
        /// </summary>
        public readonly BattleStateType StatusType;

        /// <summary>
        /// 变化后的剩余持续次数（行动机会）；状态被移除时为 0。
        /// </summary>
        public readonly int StatusRemainingRounds;

        public BattleEvent(int actorUnitId, BattleActionType commandType, int actionConfigId,
            int targetUnitId, int beforeHp, int afterHp, int beforeMp, int afterMp,
            BattleStateType statusType, int statusRemainingRounds)
        {
            ActorUnitId = actorUnitId;
            CommandType = commandType;
            ActionConfigId = actionConfigId;
            TargetUnitId = targetUnitId;
            BeforeHp = beforeHp;
            AfterHp = afterHp;
            BeforeMp = beforeMp;
            AfterMp = afterMp;
            StatusType = statusType;
            StatusRemainingRounds = statusRemainingRounds;
        }
    }
}
