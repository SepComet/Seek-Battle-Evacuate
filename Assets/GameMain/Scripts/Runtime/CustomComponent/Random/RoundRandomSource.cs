using System;

namespace SepCore.CustomComponent
{
    /// <summary>
    /// 以 System.Random 实现的本局共享随机源。
    /// 由 TurnBattleComponent 以单局 seed 创建，供地图生成、敌人 AI、逃跑与掉落共用。
    /// EditMode 测试使用可注入的序列随机源，不依赖本类。
    /// </summary>
    public sealed class RoundRandomSource : IRoundRandomSource
    {
        private readonly Random _random;

        /// <summary>
        /// 使用指定种子创建随机源。
        /// </summary>
        public RoundRandomSource(int seed)
        {
            _random = new Random(seed);
        }

        public int NextInt(int minInclusive, int maxExclusive)
        {
            return _random.Next(minInclusive, maxExclusive);
        }

        public bool RollPermille(int successPermille)
        {
            return NextInt(0, 1000) < successPermille;
        }
    }
}
