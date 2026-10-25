using System.Collections.Generic;
using SepCore.Definition;

namespace SepCore.Exploration
{
    /// <summary>
    /// 敌人队伍候选池（纯逻辑）。
    /// 联网流程开局不知道每个敌点的权威队伍（encounter 响应才下发），但实体仍需要一份
    /// 同威胁等级的配置来选 prefab、取警惕参数与战斗结束后的掉落档位；
    /// 这里按配表顺序取该威胁等级的第一支队伍作为占位，开战时一律以服务器响应为准。
    /// </summary>
    public static class EnemyPartyCatalog
    {
        /// <summary>
        /// 取该威胁等级的第一支队伍配置 ID（配表顺序）。找不到时抛错，不做静默兜底。
        /// </summary>
        public static int FindPlaceholderPartyId(IReadOnlyList<EnemyPartyConfig> enemyPartyConfigs,
            EnemyPartyThreatLevel threatLevel)
        {
            for (int i = 0; i < enemyPartyConfigs.Count; i++)
            {
                if (enemyPartyConfigs[i].ThreatConfig == threatLevel)
                {
                    return enemyPartyConfigs[i].Id;
                }
            }

            throw new System.InvalidOperationException(
                $"No enemy party config exists for threat level '{threatLevel}'.");
        }
    }
}
