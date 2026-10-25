using System;
using System.Collections.Generic;
using UnityEngine;

namespace SepCore.Exploration
{
    /// <summary>
    /// 撤离点生成（exploration-online-v1 契约 §3 第 3 段）。
    /// 固定从 SubSeed(master, KindExtraction, 0) 派生 PRNG，只消费一次下标随机。
    /// </summary>
    internal static class ExtractionPointGenerator
    {
        public static Vector2 PlacePoint(IReadOnlyList<Vector2> candidates, long masterSeed)
        {
            if (candidates.Count == 0)
            {
                throw new InvalidOperationException("Map definition does not contain an extraction point.");
            }

            SplitMix64 rng = new SplitMix64(
                ExplorationSeed.SubSeed(masterSeed, ExplorationSeed.KindExtraction, 0));
            return candidates[rng.NextInt(0, candidates.Count)];
        }
    }
}
