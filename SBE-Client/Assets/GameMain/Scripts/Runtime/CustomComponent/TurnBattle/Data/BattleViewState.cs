using System.Collections.Generic;
using SepCore.Definition;

namespace SepCore.Battle
{
    /// <summary>
    /// 单个战斗状态的只读视图。
    /// </summary>
    public sealed class BattleStateView
    {
        /// <summary>
        /// 状态类型。
        /// </summary>
        public readonly BattleStateType StatusType;

        /// <summary>
        /// 剩余持续次数（行动机会）；重复施加取较大值，移除后为 0。
        /// </summary>
        public readonly int RemainingRounds;

        public BattleStateView(BattleStateType statusType, int remainingRounds)
        {
            StatusType = statusType;
            RemainingRounds = remainingRounds;
        }
    }

    /// <summary>
    /// 单个战斗单位的只读视图。
    /// 内部调度与效果结算不得读取本视图；视图只在外部边界按需生成。
    /// </summary>
    public sealed class BattleUnitView
    {
        /// <summary>
        /// 本场战斗单位的唯一运行时标识；重复配置的敌人拥有不同 ID。
        /// </summary>
        public readonly int BattleUnitId;

        /// <summary>
        /// 阵营。
        /// </summary>
        public readonly BattleFactionType Faction;

        /// <summary>
        /// 配置标识：玩家为 CharacterId，敌人为 EnemyConfigId。
        /// </summary>
        public readonly int ConfigId;

        /// <summary>
        /// 同阵营同速度的最终并列规则顺序，也是显示顺序。
        /// </summary>
        public readonly int PartyOrder;

        /// <summary>
        /// 当前 HP。
        /// </summary>
        public readonly int CurrentHp;

        /// <summary>
        /// HP 上限。
        /// </summary>
        public readonly int MaxHp;

        /// <summary>
        /// 当前 MP。
        /// </summary>
        public readonly int CurrentMp;

        /// <summary>
        /// MP 上限。
        /// </summary>
        public readonly int MaxMp;

        /// <summary>
        /// 当前速度。
        /// </summary>
        public readonly int Speed;

        /// <summary>
        /// 是否阵亡。
        /// </summary>
        public readonly bool IsDefeated;

        /// <summary>
        /// 是否已逃跑。
        /// </summary>
        public readonly bool IsEscaped;

        /// <summary>
        /// 当前剩余状态列表。
        /// </summary>
        public readonly IReadOnlyList<BattleStateView> States;

        public BattleUnitView(int battleUnitId, BattleFactionType faction, int configId, int partyOrder,
            int currentHp, int maxHp, int currentMp, int maxMp, int speed,
            bool isDefeated, bool isEscaped, IReadOnlyList<BattleStateView> statuses)
        {
            BattleUnitId = battleUnitId;
            Faction = faction;
            ConfigId = configId;
            PartyOrder = partyOrder;
            CurrentHp = currentHp;
            MaxHp = maxHp;
            CurrentMp = currentMp;
            MaxMp = maxMp;
            Speed = speed;
            IsDefeated = isDefeated;
            IsEscaped = isEscaped;
            States = statuses != null ? Copy(statuses) : new BattleStateView[0];
        }

        private static BattleStateView[] Copy(IReadOnlyList<BattleStateView> source)
        {
            BattleStateView[] copy = new BattleStateView[source.Count];
            for (int i = 0; i < source.Count; i++)
            {
                copy[i] = source[i];
            }

            return copy;
        }
    }

    /// <summary>
    /// 当前战斗的只读视图，是 UI 唯一允许读取的战斗状态。
    /// 每次推进后在外部边界生成，不把内部可变对象暴露给 UI 或探索层。
    /// </summary>
    public sealed class BattleViewState
    {
        /// <summary>
        /// 当前轮次，从 1 开始。
        /// </summary>
        public readonly int RoundNumber;

        /// <summary>
        /// 当前行动者；战斗完成时为 0。
        /// </summary>
        public readonly int CurrentActorUnitId;

        /// <summary>
        /// 全部单位的只读视图，保持稳定显示顺序。
        /// </summary>
        public readonly IReadOnlyList<BattleUnitView> Units;

        /// <summary>
        /// 本轮从当前行动者开始的剩余运行时单位 ID；速度变化后重建。
        /// </summary>
        public readonly IReadOnlyList<int> RemainingTurnOrder;

        /// <summary>
        /// 本轮行动栏显示顺序：已行动单位按行动先后排前，未行动单位按当前调度优先级随后；
        /// 先制第一轮敌人排在全部未行动玩家之后。速度变化或单位阵亡后重建。
        /// </summary>
        public readonly IReadOnlyList<int> DisplayOrder;

        /// <summary>
        /// 当前玩家可用的行动配置 ID；当前行动者不是玩家或战斗完成时为空。
        /// </summary>
        public readonly IReadOnlyList<int> AvailableActionIds;

        public BattleViewState(int roundNumber, int currentActorUnitId,
            IReadOnlyList<BattleUnitView> units, IReadOnlyList<int> remainingTurnOrder,
            IReadOnlyList<int> availableActionIds, IReadOnlyList<int> displayOrder)
        {
            RoundNumber = roundNumber;
            CurrentActorUnitId = currentActorUnitId;
            Units = units != null ? Copy(units) : new BattleUnitView[0];
            RemainingTurnOrder = remainingTurnOrder != null ? Copy(remainingTurnOrder) : new int[0];
            AvailableActionIds = availableActionIds != null ? Copy(availableActionIds) : new int[0];
            DisplayOrder = displayOrder != null ? Copy(displayOrder) : new int[0];
        }

        private static BattleUnitView[] Copy(IReadOnlyList<BattleUnitView> source)
        {
            BattleUnitView[] copy = new BattleUnitView[source.Count];
            for (int i = 0; i < source.Count; i++)
            {
                copy[i] = source[i];
            }

            return copy;
        }

        private static int[] Copy(IReadOnlyList<int> source)
        {
            int[] copy = new int[source.Count];
            for (int i = 0; i < source.Count; i++)
            {
                copy[i] = source[i];
            }

            return copy;
        }
    }
}
