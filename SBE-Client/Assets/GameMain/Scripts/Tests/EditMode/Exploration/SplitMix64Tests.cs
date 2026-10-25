using NUnit.Framework;
using SepCore.Exploration;

namespace SepCore.Tests
{
    /// <summary>
    /// SplitMix64 与 Go 侧逐位同构的固定向量测试（exploration-online-v1 契约 §1）。
    /// </summary>
    [TestFixture]
    public class SplitMix64Tests
    {
        [Test]
        public void Next_WithSeedZero_MatchesKnownVector()
        {
            SplitMix64 rng = new SplitMix64(0UL);

            Assert.AreEqual(0xE220A8397B1DCDAFUL, rng.Next());
            Assert.AreEqual(0x6E789E6AA1B965F4UL, rng.Next());
            Assert.AreEqual(0x06C45D188009454FUL, rng.Next());
            Assert.AreEqual(0xF88BB8A8724C81ECUL, rng.Next());
            Assert.AreEqual(0x1B39896A51A8749BUL, rng.Next());
        }

        [Test]
        public void Next_WithSeedOneAndMaxValue_MatchesKnownVector()
        {
            SplitMix64 seedOne = new SplitMix64(1UL);
            Assert.AreEqual(0x910A2DEC89025CC1UL, seedOne.Next());
            Assert.AreEqual(0xBEEB8DA1658EEC67UL, seedOne.Next());
            Assert.AreEqual(0xF893A2EEFB32555EUL, seedOne.Next());

            SplitMix64 seedMax = new SplitMix64(ulong.MaxValue);
            Assert.AreEqual(0xE4D971771B652C20UL, seedMax.Next());
            Assert.AreEqual(0xE99FF867DBF682C9UL, seedMax.Next());
        }

        [Test]
        public void Hash64_MatchesKnownVector()
        {
            Assert.AreEqual(0xE220A8397B1DCDAFUL, SplitMix64.Hash64(0UL));
            Assert.AreEqual(0x910A2DEC89025CC1UL, SplitMix64.Hash64(1UL));
            Assert.AreEqual(0xBDD732262FEB6E95UL, SplitMix64.Hash64(42UL));
        }

        [Test]
        public void NextInt_WithSeedZero_MatchesKnownValues()
        {
            Assert.AreEqual(0, new SplitMix64(0UL).NextInt(0, 1));
            Assert.AreEqual(1, new SplitMix64(0UL).NextInt(0, 2));
            Assert.AreEqual(5, new SplitMix64(0UL).NextInt(0, 10));
            Assert.AreEqual(5, new SplitMix64(0UL).NextInt(5, 6));
            Assert.AreEqual(100, new SplitMix64(0UL).NextInt(100, 101));
            Assert.AreEqual(8, new SplitMix64(0UL).NextInt(7, 9));
            Assert.AreEqual(8, new SplitMix64(1UL).NextInt(7, 9));
        }

        [Test]
        public void NextInt_WithSpanWiderThanInt32_MatchesGoReference()
        {
            // 区间宽度 4294967295 超出 int 表示范围：必须按 long 计算跨度，
            // 否则 (ulong)(int 溢出结果) 会得到错误取模。期望值由契约 §1 的 Go 实现算出。
            Assert.AreEqual(-583109143, new SplitMix64(0UL).NextInt(int.MinValue, int.MaxValue));
            Assert.AreEqual(1841471675, new SplitMix64(42UL).NextInt(int.MinValue, int.MaxValue));
            Assert.AreEqual(-534254766, new SplitMix64(123456789UL).NextInt(int.MinValue, int.MaxValue));
        }

        [Test]
        public void NextInt_StaysWithinHalfOpenRange()
        {
            SplitMix64 rng = new SplitMix64(20261025UL);
            for (int i = 0; i < 5000; i++)
            {
                int value = rng.NextInt(-13, 17);
                Assert.That(value, Is.GreaterThanOrEqualTo(-13).And.LessThan(17));
            }
        }

        [Test]
        public void NextInt_SingleElementSpan_AlwaysReturnsMin()
        {
            SplitMix64 rng = new SplitMix64(7UL);
            for (int i = 0; i < 16; i++)
            {
                Assert.AreEqual(3, rng.NextInt(3, 4));
            }
        }

        [Test]
        public void Next_WithDifferentSeeds_ProducesDifferentStreams()
        {
            SplitMix64 first = new SplitMix64(1UL);
            SplitMix64 second = new SplitMix64(2UL);

            bool anyDifference = false;
            for (int i = 0; i < 8; i++)
            {
                if (first.Next() != second.Next())
                {
                    anyDifference = true;
                }
            }

            Assert.IsTrue(anyDifference);
        }

        [Test]
        public void Next_ResetsOnlyWithNewInstance()
        {
            SplitMix64 rng = new SplitMix64(99UL);
            ulong first = rng.Next();
            ulong second = rng.Next();
            Assert.AreNotEqual(first, second);

            SplitMix64 replay = new SplitMix64(99UL);
            Assert.AreEqual(first, replay.Next());
            Assert.AreEqual(second, replay.Next());
        }
    }
}
