using System.Collections.Generic;

namespace SepCore.Battle
{
    /// <summary>
    /// 一次同步推进的返回值，是 UI 读取行动记录与最新视图的唯一来源。
    /// 不保存独立流程状态：Result 非 null 即表示战斗已完成。
    /// </summary>
    public sealed class BattleStep
    {
        /// <summary>
        /// 本次推进产生的有序行动记录列表。
        /// </summary>
        public readonly IReadOnlyList<BattleEvent> Events;

        /// <summary>
        /// 推进结束后的只读战斗视图。
        /// </summary>
        public readonly BattleViewState View;

        /// <summary>
        /// 仅在战斗完成时存在，否则为 null。
        /// </summary>
        public readonly BattleResult Result;

        public BattleStep(IReadOnlyList<BattleEvent> events, BattleViewState view, BattleResult result)
        {
            Events = events != null ? Copy(events) : new BattleEvent[0];
            View = view;
            Result = result;
        }

        private static BattleEvent[] Copy(IReadOnlyList<BattleEvent> source)
        {
            BattleEvent[] copy = new BattleEvent[source.Count];
            for (int i = 0; i < source.Count; i++)
            {
                copy[i] = source[i];
            }

            return copy;
        }
    }
}
