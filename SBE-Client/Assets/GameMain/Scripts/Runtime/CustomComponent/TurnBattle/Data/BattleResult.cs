using System.Collections.Generic;
using SepCore.Definition;

namespace SepCore.Battle
{
    /// <summary>
    /// 每名参战玩家的战后结果。
    /// 保留原始战斗值：阵亡者 HP 为 0；恢复 1/1 的规则由 TurnBattleComponent 统一应用。
    /// </summary>
    public sealed class BattlePlayerResult
    {
        /// <summary>
        /// 角色配置标识。
        /// </summary>
        public readonly int CharacterId;

        /// <summary>
        /// 战斗结束时的当前 HP。
        /// </summary>
        public readonly int CurrentHp;

        /// <summary>
        /// 战斗结束时的当前 MP。
        /// </summary>
        public readonly int CurrentMp;

        /// <summary>
        /// 是否在本场战斗中阵亡。
        /// </summary>
        public readonly bool WasDefeated;

        /// <summary>
        /// 是否在本场战斗中成功逃跑。
        /// </summary>
        public readonly bool Escaped;

        public BattlePlayerResult(int characterId, int currentHp, int currentMp, bool wasDefeated, bool escaped)
        {
            CharacterId = characterId;
            CurrentHp = currentHp;
            CurrentMp = currentMp;
            WasDefeated = wasDefeated;
            Escaped = escaped;
        }
    }

    /// <summary>
    /// 战斗结果。
    /// </summary>
    public sealed class BattleResult
    {
        /// <summary>
        /// 关联原地图敌人实例。
        /// </summary>
        public readonly int EncounterId;

        /// <summary>
        /// 四种战斗结果之一。
        /// </summary>
        public readonly BattleOutcomeType Outcome;

        /// <summary>
        /// 每名参战玩家的原始战后结果。
        /// </summary>
        public readonly IReadOnlyList<BattlePlayerResult> Players;

        public BattleResult(int encounterId, BattleOutcomeType outcome, IReadOnlyList<BattlePlayerResult> players)
        {
            EncounterId = encounterId;
            Outcome = outcome;
            Players = players != null ? Copy(players) : new BattlePlayerResult[0];
        }

        private static BattlePlayerResult[] Copy(IReadOnlyList<BattlePlayerResult> source)
        {
            BattlePlayerResult[] copy = new BattlePlayerResult[source.Count];
            for (int i = 0; i < source.Count; i++)
            {
                copy[i] = source[i];
            }

            return copy;
        }
    }
}