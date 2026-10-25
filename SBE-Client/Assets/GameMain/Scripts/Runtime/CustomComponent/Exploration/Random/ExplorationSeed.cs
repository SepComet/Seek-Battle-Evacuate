namespace SepCore.Exploration
{
    /// <summary>
    /// 内容生成的子种子派生（exploration-online-v1 契约 §2）。
    /// 每个随机消费点从独立子种子起步，条目之间不共享随机流，
    /// 因此改动某一类配置（如物资权重）不会影响其他类点位（如敌点）的摆位。
    /// </summary>
    public static class ExplorationSeed
    {
        /// <summary>
        /// 物资点。
        /// </summary>
        public const int KindResource = 1;

        /// <summary>
        /// 敌点。
        /// </summary>
        public const int KindEnemy = 2;

        /// <summary>
        /// 撤离点。
        /// </summary>
        public const int KindExtraction = 3;

        /// <summary>
        /// 出生点。
        /// </summary>
        public const int KindSpawn = 4;

        /// <summary>
        /// 由主种子、点位类型与条目下标派生 64 位子种子：
        /// SubSeed = Hash64(Hash64(Hash64(master) ^ kind) ^ index)。
        /// </summary>
        /// <param name="master">服务器分配的本局主种子（int64 语义，实际取值 [1, 2147483647]）。</param>
        /// <param name="kind">点位类型，取 KindResource / KindEnemy / KindExtraction / KindSpawn。</param>
        /// <param name="index">点位在地图定义列表中的下标；撤离与出生点固定为 0。</param>
        public static ulong SubSeed(long master, int kind, int index)
        {
            unchecked
            {
                ulong hash = SplitMix64.Hash64((ulong)master);
                hash = SplitMix64.Hash64(hash ^ (ulong)kind);
                return SplitMix64.Hash64(hash ^ (ulong)index);
            }
        }
    }
}
