namespace SepCore.CustomComponent
{
    /// <summary>
    /// 本局共享随机数接口。
    /// 同一单局的地图生成、敌人 AI、逃跑和敌人掉落依次消费同一个实例；
    /// 战斗不能根据 EncounterId、回合数或当前时间重新播种。
    /// </summary>
    public interface IRoundRandomSource
    {
        /// <summary>
        /// 返回 [minInclusive, maxExclusive) 范围内的随机整数。
        /// </summary>
        int NextInt(int minInclusive, int maxExclusive);

        /// <summary>
        /// 以千分比概率判定是否成功；RollPermille(0) 必失败，RollPermille(1000) 必成功。
        /// </summary>
        bool RollPermille(int successPermille);
    }
}
