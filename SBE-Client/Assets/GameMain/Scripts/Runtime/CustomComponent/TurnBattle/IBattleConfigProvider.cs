using SepCore.Definition;

namespace SepCore.Battle
{
    /// <summary>
    /// 战斗配置访问接口，隔离 BattleSession 与 GameEntry.Luban。
    /// 运行时适配器读取现有 Luban 表，测试使用内存配置。
    /// 配置缺失属于战斗启动失败，不允许用零值单位继续战斗。
    /// </summary>
    public interface IBattleConfigProvider
    {
        /// <summary>
        /// 按 ID 获取敌人队伍预设。
        /// </summary>
        EnemyPartyConfig GetEnemyParty(int id);

        /// <summary>
        /// 按 ID 获取敌人配置。
        /// </summary>
        EnemyConfig GetEnemy(int id);

        /// <summary>
        /// 按 ID 获取行动配置。
        /// </summary>
        BattleActionConfig GetAction(int id);

        /// <summary>
        /// 获取全局配置。
        /// </summary>
        GlobalConfig GetGlobal();
    }
}