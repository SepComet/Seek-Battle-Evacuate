using System.Collections.Generic;

namespace SepCore.Definition
{
    /// <summary>
    /// 单局结算界面展示数据结构。
    /// 包含单局结果类型、实际耗时、带出物资总价值以及成功带出的物品列表。
    /// </summary>
    public sealed class RoundSettlementData
    {
        /// <summary>
        /// 结算结果类型。
        /// </summary>
        public RoundResultType Outcome { get; set; }

        /// <summary>
        /// 本局耗时（秒）。
        /// </summary>
        public int ElapsedSeconds { get; set; }

        /// <summary>
        /// 本次带出的物资总价值。
        /// </summary>
        public int TotalValue { get; set; }

        /// <summary>
        /// 本次带出的物品列表（成功撤离时为背包+保险箱；失败/超时/战败时仅为保险箱）。
        /// </summary>
        public IReadOnlyList<ItemStack> BroughtItems { get; set; }

        /// <summary>
        /// 是否仅保留保险箱（撤离成功时为 false，其余结果为 true）。
        /// </summary>
        public bool OnlyRetainedSafeCase => Outcome != RoundResultType.Extracted;

        public RoundSettlementData()
        {
        }

        public RoundSettlementData(RoundResultType outcome, int elapsedSeconds, int totalValue, IReadOnlyList<ItemStack> broughtItems)
        {
            Outcome = outcome;
            ElapsedSeconds = elapsedSeconds;
            TotalValue = totalValue;
            BroughtItems = broughtItems;
        }
    }
}
