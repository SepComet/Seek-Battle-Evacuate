namespace SepCore.Battle
{
    /// <summary>
    /// 遭遇输入，由探索层在碰撞发生时创建。
    /// 地图坐标、碰撞器、GameObject 和警惕组件不进入战斗请求，由探索层继续持有。
    /// </summary>
    public sealed class BattleEncounter
    {
        /// <summary>
        /// 触发碰撞的地图敌人实例的单局唯一标识；不能使用敌人配置 ID 代替。
        /// </summary>
        public readonly int EncounterId;

        /// <summary>
        /// 该地图敌人代表的敌人队伍预设 ID。
        /// </summary>
        public readonly int EnemyPartyConfigId;

        /// <summary>
        /// 碰撞时敌人警惕值未满为 true，否则为 false。
        /// </summary>
        public readonly bool IsPreemptive;

        public BattleEncounter(int encounterId, int enemyPartyConfigId, bool isPreemptive)
        {
            EncounterId = encounterId;
            EnemyPartyConfigId = enemyPartyConfigId;
            IsPreemptive = isPreemptive;
        }
    }
}