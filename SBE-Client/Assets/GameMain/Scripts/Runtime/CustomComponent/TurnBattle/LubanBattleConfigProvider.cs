using SepCore.CustomComponent;
using SepCore.Definition;

namespace SepCore.Battle
{
    /// <summary>
    /// 战斗配置的运行时适配器，隔离战斗规则与 GameEntry.Luban。
    /// 配置缺失返回 null，由启动校验决定失败；正常行动不重复扫描全表完整性。
    /// EditMode 测试使用内存配置，不使用本类。
    /// </summary>
    internal sealed class LubanBattleConfigProvider : IBattleConfigProvider
    {
        public EnemyPartyConfig GetEnemyParty(int id)
        {
            return SafeGet(() => GameEntry.Luban.Get<EnemyPartyConfig>(id));
        }

        public EnemyConfig GetEnemy(int id)
        {
            return SafeGet(() => GameEntry.Luban.Get<EnemyConfig>(id));
        }

        public BattleActionConfig GetAction(int id)
        {
            return SafeGet(() => GameEntry.Luban.Get<BattleActionConfig>(id));
        }

        public GlobalConfig GetGlobal()
        {
            return SafeGet(() => GameEntry.Luban.Global != null ? GameEntry.Luban.Global.Data : null);
        }

        private static T SafeGet<T>(System.Func<T> getter) where T : class
        {
            try
            {
                return GameEntry.Luban != null && GameEntry.Luban.IsReady ? getter() : null;
            }
            catch (System.Exception)
            {
                return null;
            }
        }
    }
}
