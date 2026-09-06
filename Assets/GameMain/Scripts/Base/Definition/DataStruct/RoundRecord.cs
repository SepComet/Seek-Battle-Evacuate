namespace SepCore.Definition
{
    /// <summary>
    /// 已结束单局的结算记录。
    /// </summary>
    [System.Serializable]
    public struct RoundRecord
    {
        /// <summary>
        /// 结算结果。
        /// </summary>
        public RoundResultType outcome;

        /// <summary>
        /// 本局难度。
        /// </summary>
        public DifficultyTier difficultyId;

        /// <summary>
        /// 本局使用的随机数。
        /// </summary>
        public long seed;

        /// <summary>
        /// 进入单局时间，Unix 毫秒。
        /// </summary>
        public long startedAt;

        /// <summary>
        /// 结算时间，Unix 毫秒。
        /// </summary>
        public long endedAt;

        public RoundRecord(RoundResultType outcome, DifficultyTier difficultyId, long seed, long startedAt, long endedAt)
        {
            this.outcome = outcome;
            this.difficultyId = difficultyId;
            this.seed = seed;
            this.startedAt = startedAt;
            this.endedAt = endedAt;
        }
    }
}
