using System.Collections.Generic;
using SepCore.Definition;

namespace SepCore.Battle
{
    /// <summary>
    /// 单局玩家状态回写（纯逻辑，可独立测试）。
    /// 非 TotalDefeat 结果把战后 HP/MP 写回单局临时状态；阵亡者按复活值恢复；
    /// TotalDefeat 不回写。战斗中的状态与速度修改只存在于 BattleRuntime，不进入下一场。
    /// </summary>
    internal static class RoundPlayerStateWriteback
    {
        public static void Apply(IList<PlayerUnitState> players, BattleResult result, int reviveHp, int reviveMp)
        {
            if (players == null || result == null)
            {
                return;
            }

            if (result.Outcome == BattleOutcomeType.TotalDefeat)
            {
                return;
            }

            if (result.Players == null)
            {
                return;
            }

            foreach (BattlePlayerResult playerResult in result.Players)
            {
                PlayerUnitState unitState = Find(players, playerResult.CharacterId);
                if (unitState == null)
                {
                    continue;
                }

                if (playerResult.WasDefeated)
                {
                    unitState.CurrentHp = reviveHp;
                    unitState.CurrentMp = reviveMp;
                }
                else
                {
                    unitState.CurrentHp = playerResult.CurrentHp;
                    unitState.CurrentMp = playerResult.CurrentMp;
                }
            }
        }

        private static PlayerUnitState Find(IList<PlayerUnitState> players, int characterId)
        {
            foreach (PlayerUnitState player in players)
            {
                if (player != null && player.CharacterId == characterId)
                {
                    return player;
                }
            }

            return null;
        }
    }
}
