using UnityGameFramework.Runtime;

namespace SepCore.CustomComponent
{
    /// <summary>
    /// 单局共享随机组件。
    /// 种子由局外（战备/部署界面玩家输入）带入，进入单局时初始化；
    /// 地图生成、物资点、敌人 AI、逃跑判定和敌人掉落依次消费同一个随机源，
    /// 战斗只沿用该随机源，不创建战斗私有随机源。
    /// 对应 Docs/GameDesign/03_RunExploration.md 的随机生成器约定。
    /// </summary>
    public class RandomComponent : GameFrameworkComponent
    {
        private int _seed;
        private IRoundRandomSource _random;

        /// <summary>
        /// 获取本局随机种子；未开始单局时为 0。
        /// </summary>
        public int Seed => _seed;

        /// <summary>
        /// 获取本局共享随机源；未开始单局时为 null。
        /// </summary>
        public IRoundRandomSource Random => _random;

        /// <summary>
        /// 开始一局新的单局：使用局外带入的种子初始化共享随机源。
        /// </summary>
        /// <param name="seed">本局随机种子。</param>
        public void BeginRun(int seed)
        {
            _seed = seed;
            _random = new RoundRandomSource(seed);
        }

        /// <summary>
        /// 结束当前单局，清空种子与随机源。
        /// </summary>
        public void EndRun()
        {
            _seed = 0;
            _random = null;
        }
    }
}