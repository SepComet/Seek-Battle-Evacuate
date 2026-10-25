using System.Collections.Generic;
using SepCore.Definition;

namespace SepCore.Battle
{
    /// <summary>
    /// 单场战斗的运行时单位，仅 BattleRuntime 及其协作者读写。
    /// UI 与探索层通过 BattleUnitView 读取，不直接持有本类型。
    /// </summary>
    internal sealed class BattleUnit
    {
        /// <summary>
        /// 本场战斗单位的唯一运行时标识；重复配置的敌人拥有不同 ID。
        /// </summary>
        public readonly int UnitId;

        /// <summary>
        /// 阵营。
        /// </summary>
        public readonly BattleFactionType Faction;

        /// <summary>
        /// 配置标识：玩家为 CharacterId，敌人为 EnemyConfigId。
        /// </summary>
        public readonly int ConfigId;

        /// <summary>
        /// 同阵营同速度的最终并列规则顺序。
        /// </summary>
        public readonly int PartyOrder;

        /// <summary>
        /// HP 上限。
        /// </summary>
        public readonly int MaxHp;

        /// <summary>
        /// MP 上限。
        /// </summary>
        public readonly int MaxMp;

        /// <summary>
        /// 攻击。
        /// </summary>
        public readonly int Atk;

        /// <summary>
        /// 魔力。
        /// </summary>
        public readonly int Mat;

        /// <summary>
        /// 当前 HP。
        /// </summary>
        public int CurrentHp;

        /// <summary>
        /// 当前 MP。
        /// </summary>
        public int CurrentMp;

        /// <summary>
        /// 当前速度；可被战斗内效果修改。
        /// </summary>
        public int Speed;

        /// <summary>
        /// 是否阵亡。
        /// </summary>
        public bool IsDefeated;

        /// <summary>
        /// 是否已逃跑。
        /// </summary>
        public bool IsEscaped;

        /// <summary>
        /// 当前剩余状态。
        /// </summary>
        public readonly List<BattleState> Statuses = new List<BattleState>();

        /// <summary>
        /// 该单位可用的行动配置 ID。
        /// </summary>
        public readonly List<int> ActionIds = new List<int>();

        public BattleUnit(int unitId, BattleFactionType faction, int configId, int partyOrder,
            int currentHp, int maxHp, int currentMp, int maxMp, int atk, int mat, int speed)
        {
            UnitId = unitId;
            Faction = faction;
            ConfigId = configId;
            PartyOrder = partyOrder;
            CurrentHp = currentHp;
            MaxHp = maxHp;
            CurrentMp = currentMp;
            MaxMp = maxMp;
            Atk = atk;
            Mat = mat;
            Speed = speed;
        }
    }
}
