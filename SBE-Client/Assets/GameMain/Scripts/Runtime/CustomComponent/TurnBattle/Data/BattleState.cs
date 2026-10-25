using SepCore.Definition;

namespace SepCore.Battle
{
    /// <summary>
    /// 单场战斗中的状态，只存在于 BattleRuntime。
    /// 同类状态不叠层，重复施加保留较长的剩余行动机会次数。
    /// </summary>
    public sealed class BattleState
    {
        /// <summary>
        /// 状态类型。
        /// </summary>
        public BattleStateType Type;

        /// <summary>
        /// 剩余持续次数（行动机会）。
        /// </summary>
        public int RemainingRounds;

        public BattleState(BattleStateType type, int remainingRounds)
        {
            Type = type;
            RemainingRounds = remainingRounds;
        }
    }
}
