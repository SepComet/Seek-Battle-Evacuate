using NUnit.Framework;
using SepCore.Exploration;

namespace SepCore.Tests
{
    /// <summary>
    /// 子种子派生的固定向量测试（exploration-online-v1 契约 §2）。
    /// </summary>
    [TestFixture]
    public class ExplorationSeedTests
    {
        [Test]
        public void KindConstants_MatchContract()
        {
            Assert.AreEqual(1, ExplorationSeed.KindResource);
            Assert.AreEqual(2, ExplorationSeed.KindEnemy);
            Assert.AreEqual(3, ExplorationSeed.KindExtraction);
            Assert.AreEqual(4, ExplorationSeed.KindSpawn);
        }

        [Test]
        public void SubSeed_MatchesKnownVector()
        {
            Assert.AreEqual(0xBFB3F7294C7F2779UL, ExplorationSeed.SubSeed(123456789L, 1, 0));
            Assert.AreEqual(0x7B9819FCFAD60401UL, ExplorationSeed.SubSeed(123456789L, 1, 1));
            Assert.AreEqual(0x202A6C971A705BEEUL, ExplorationSeed.SubSeed(123456789L, 1, 2));
            Assert.AreEqual(0x41E75059FD28538EUL, ExplorationSeed.SubSeed(123456789L, 2, 0));
            Assert.AreEqual(0x9AE1B3F82DAA67F8UL, ExplorationSeed.SubSeed(123456789L, 2, 3));
            Assert.AreEqual(0x15884FDE4C369CDDUL, ExplorationSeed.SubSeed(123456789L, 3, 0));
            Assert.AreEqual(0xFE1C3CE9428F5C39UL, ExplorationSeed.SubSeed(123456789L, 4, 0));
            Assert.AreEqual(0x0684A9E565657C2EUL, ExplorationSeed.SubSeed(42L, 2, 0));
            Assert.AreEqual(0x075D2F9B21F9014DUL, ExplorationSeed.SubSeed(2147483647L, 2, 2));
        }

        [Test]
        public void SubSeed_IsDeterministic()
        {
            Assert.AreEqual(ExplorationSeed.SubSeed(777L, 2, 5), ExplorationSeed.SubSeed(777L, 2, 5));
        }

        [Test]
        public void SubSeed_ChangesWhenKindChanges()
        {
            Assert.AreNotEqual(ExplorationSeed.SubSeed(777L, 1, 5), ExplorationSeed.SubSeed(777L, 2, 5));
        }

        [Test]
        public void SubSeed_ChangesWhenIndexChanges()
        {
            Assert.AreNotEqual(ExplorationSeed.SubSeed(777L, 1, 5), ExplorationSeed.SubSeed(777L, 1, 6));
        }

        [Test]
        public void SubSeed_ChangesWhenMasterSeedChanges()
        {
            Assert.AreNotEqual(ExplorationSeed.SubSeed(777L, 1, 5), ExplorationSeed.SubSeed(778L, 1, 5));
        }

        [Test]
        public void SubSeed_DifferentKindsAtSameIndex_AreAllDistinct()
        {
            ulong resource = ExplorationSeed.SubSeed(20261025L, ExplorationSeed.KindResource, 0);
            ulong enemy = ExplorationSeed.SubSeed(20261025L, ExplorationSeed.KindEnemy, 0);
            ulong extraction = ExplorationSeed.SubSeed(20261025L, ExplorationSeed.KindExtraction, 0);
            ulong spawn = ExplorationSeed.SubSeed(20261025L, ExplorationSeed.KindSpawn, 0);

            Assert.AreNotEqual(resource, enemy);
            Assert.AreNotEqual(resource, extraction);
            Assert.AreNotEqual(resource, spawn);
            Assert.AreNotEqual(enemy, extraction);
            Assert.AreNotEqual(enemy, spawn);
            Assert.AreNotEqual(extraction, spawn);
        }
    }
}
