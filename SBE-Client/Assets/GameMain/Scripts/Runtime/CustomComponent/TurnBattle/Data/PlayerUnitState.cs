namespace SepCore.Battle
{
    /// <summary>
    /// 单局临时角色状态。
    /// 数值由单局快照构建器在开战前从角色与装备结算得到；战斗期间只读，战斗结束回写当前 HP/MP。
    /// </summary>
    public sealed class PlayerUnitState
    {
        /// <summary>
        /// 角色配置标识。
        /// </summary>
        public int CharacterId;

        /// <summary>
        /// 当前战备中的顺序，1 开始。
        /// </summary>
        public int PartyOrder;

        /// <summary>
        /// 当前 HP。
        /// </summary>
        public int CurrentHp;

        /// <summary>
        /// 当前 MP。
        /// </summary>
        public int CurrentMp;

        /// <summary>
        /// 已结算装备加成的最终 HP 上限。
        /// </summary>
        public int MaxHp;

        /// <summary>
        /// 已结算装备加成的最终 MP 上限。
        /// </summary>
        public int MaxMp;

        /// <summary>
        /// 已结算装备加成的最终攻击。
        /// </summary>
        public int Atk;

        /// <summary>
        /// 已结算装备加成的最终魔力。
        /// </summary>
        public int Mat;

        /// <summary>
        /// 已结算装备加成的最终速度。
        /// </summary>
        public int Speed;

        /// <summary>
        /// 普通攻击行动配置 ID。
        /// </summary>
        public int AttackActionId;

        /// <summary>
        /// 该角色首版唯一技能行动配置 ID。
        /// </summary>
        public int SkillActionId;
    }
}
