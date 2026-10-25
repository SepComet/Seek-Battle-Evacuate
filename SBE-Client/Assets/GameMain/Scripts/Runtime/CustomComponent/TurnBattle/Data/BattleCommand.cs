using System.Collections.Generic;
using SepCore.Definition;

namespace SepCore.Battle
{
    /// <summary>
    /// 玩家指令。
    /// 非法指令（错误行动者、错误阵营、错误目标数量、阵亡或逃跑目标）不得消耗 MP、行动机会或随机数。
    /// </summary>
    public sealed class BattleCommand
    {
        /// <summary>
        /// 当前获得行动机会的玩家单位。
        /// </summary>
        public readonly int ActorUnitId;

        /// <summary>
        /// 指令类型，对应配表 BattleActionType。
        /// </summary>
        public readonly BattleActionType CommandType;

        /// <summary>
        /// 攻击或技能对应的配置 ID；逃跑为 0。
        /// </summary>
        public readonly int ActionConfigId;

        /// <summary>
        /// 按目标类型解析后的唯一运行时目标列表。
        /// </summary>
        public readonly IReadOnlyList<int> TargetUnitIds;

        public BattleCommand(
            int actorUnitId, 
            BattleActionType commandType, 
            int actionConfigId,
            IReadOnlyList<int> targetUnitIds)
        {
            ActorUnitId = actorUnitId;
            CommandType = commandType;
            ActionConfigId = actionConfigId;
            TargetUnitIds = targetUnitIds != null ? Copy(targetUnitIds) : new int[0];
        }

        private static int[] Copy(IReadOnlyList<int> source)
        {
            int[] copy = new int[source.Count];
            for (int i = 0; i < source.Count; i++)
            {
                copy[i] = source[i];
            }

            return copy;
        }
    }
}