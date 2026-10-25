using SepCore.Battle;
using SepCore.Definition;

namespace SepCore.UI
{
    /// <summary>
    /// 战斗单位视图的显示辅助：名称与图标解析。
    /// 玩家图标来自 CharacterConfig.Icon_Ref，敌人图标来自 EnemyConfig.Icon_Ref。
    /// </summary>
    public static class BattleUnitViewHelper
    {
        /// <summary>
        /// 获取单位显示名；配置缺失时回退为配置 ID。
        /// </summary>
        public static string GetDisplayName(BattleUnitView unit)
        {
            if (unit == null)
            {
                return string.Empty;
            }

            if (unit.Faction == BattleFactionType.Player)
            {
                CharacterConfig config = GameEntry.Luban.Get<CharacterConfig>(unit.ConfigId);
                return config != null ? config.Name : unit.ConfigId.ToString();
            }
            else
            {
                EnemyConfig config = GameEntry.Luban.Get<EnemyConfig>(unit.ConfigId);
                return config != null ? config.Name : unit.ConfigId.ToString();
            }
        }

        /// <summary>
        /// 获取玩家单位图标配置；配置缺失时返回 null。
        /// </summary>
        public static SpriteConfig GetPlayerIconConfig(int characterId)
        {
            CharacterConfig config = GameEntry.Luban.Get<CharacterConfig>(characterId);
            return config != null ? config.Icon_Ref : null;
        }

        /// <summary>
        /// 获取敌人单位图标配置；配置缺失时返回 null。
        /// </summary>
        public static SpriteConfig GetEnemyIconConfig(int enemyId)
        {
            EnemyConfig config = GameEntry.Luban.Get<EnemyConfig>(enemyId);
            return config != null ? config.Icon_Ref : null;
        }

        /// <summary>
        /// 指定单位是否处于眩晕。
        /// </summary>
        public static bool IsStunned(BattleUnitView unit)
        {
            foreach (BattleStateView state in unit.States)
            {
                if (state.StatusType == BattleStateType.Stun)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 状态显示文本；首版直接使用固定文本，本地化在 M7 接入。
        /// </summary>
        public static string GetStateText(BattleStateType statusType)
        {
            return statusType == BattleStateType.Stun ? "Stun" : string.Empty;
        }
    }
}