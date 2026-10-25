namespace SepCore.Exploration
{
    /// <summary>
    /// 双端可移植的 SplitMix64 伪随机数发生器（exploration-online-v1 契约 §1）。
    /// Go 侧有逐位同构实现，内容生成的随机一律走本类，不使用 System.Random / UnityEngine.Random。
    /// 战斗侧随机源（RoundRandomSource）与本类无关，保持不动。
    /// </summary>
    public sealed class SplitMix64
    {
        private const ulong Gamma = 0x9E3779B97F4A7C15UL;
        private const ulong Mix1 = 0xBF58476D1CE4E5B9UL;
        private const ulong Mix2 = 0x94D049BB133111EBUL;

        private ulong _state;

        public SplitMix64(ulong seed)
        {
            _state = seed;
        }

        /// <summary>
        /// 推进状态并返回下一个 64 位随机值。
        /// </summary>
        public ulong Next()
        {
            unchecked
            {
                _state += Gamma;
                return Mix(_state);
            }
        }

        /// <summary>
        /// 返回 [minInclusive, maxExclusive) 范围内的随机整数。
        /// 使用取模（与 Go 侧一致，模偏置在原型阶段接受）；调用方保证 maxExclusive &gt; minInclusive。
        /// 区间宽度按 long 计算：Go 的 int 是 64 位，这样两端在跨 int 边界的区间上也逐位一致，
        /// 且结果落在 [minInclusive, maxExclusive) 内不会溢出 int。
        /// </summary>
        public int NextInt(int minInclusive, int maxExclusive)
        {
            unchecked
            {
                long span = (long)maxExclusive - minInclusive;
                return (int)(minInclusive + (long)(Next() % (ulong)span));
            }
        }

        /// <summary>
        /// 无状态哈希（契约 §1 的 Hash64）：对传入值先做 gamma 累加再混合，用于子种子派生。
        /// 与 <see cref="Next"/> 的区别是不持有状态；两者共用同一混合步骤。
        /// </summary>
        public static ulong Hash64(ulong value)
        {
            unchecked
            {
                value += Gamma;
                return Mix(value);
            }
        }

        private static ulong Mix(ulong z)
        {
            unchecked
            {
                z = (z ^ (z >> 30)) * Mix1;
                z = (z ^ (z >> 27)) * Mix2;
                return z ^ (z >> 31);
            }
        }
    }
}
