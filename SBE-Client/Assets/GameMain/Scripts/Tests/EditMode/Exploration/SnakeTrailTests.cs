using NUnit.Framework;
using SepCore.Exploration;
using UnityEngine;

namespace SepCore.Tests
{
    [TestFixture]
    public class SnakeTrailTests
    {
        [Test]
        public void Append_WithinSampleDistance_DoesNotRecord()
        {
            SnakeTrail trail = new SnakeTrail(0.25f);
            trail.Reset(new Vector2(0f, 0f));

            Assert.IsFalse(trail.Append(new Vector2(0.1f, 0f)));
            Assert.AreEqual(0f, trail.HeadArc, 0.0001f);
            Assert.AreEqual(1, trail.PointCount);
        }

        [Test]
        public void Append_BeyondSampleDistance_RecordsAndAccumulatesArc()
        {
            SnakeTrail trail = new SnakeTrail(0.25f);
            trail.Reset(new Vector2(0f, 0f));

            Assert.IsTrue(trail.Append(new Vector2(1f, 0f)));
            Assert.IsTrue(trail.Append(new Vector2(2f, 0f)));
            Assert.IsTrue(trail.Append(new Vector2(3f, 0f)));
            Assert.AreEqual(3f, trail.HeadArc, 0.0001f);
            Assert.AreEqual(4, trail.PointCount);
        }

        [Test]
        public void GetPointFromStart_StraightSegment_ReturnsInterpolatedPoint()
        {
            SnakeTrail trail = new SnakeTrail(0.25f);
            trail.Reset(new Vector2(0f, 0f));
            trail.Append(new Vector2(2f, 0f));

            Assert.AreEqual(0f, trail.GetPointFromStart(0f).x, 0.0001f);
            Assert.AreEqual(0.5f, trail.GetPointFromStart(0.5f).x, 0.0001f);
            Assert.AreEqual(2f, trail.GetPointFromStart(2f).x, 0.0001f);
        }

        [Test]
        public void GetPointFromStart_Corner_InterpolatesAlongBothSegments()
        {
            SnakeTrail trail = new SnakeTrail(0.25f);
            trail.Reset(new Vector2(0f, 0f));
            trail.Append(new Vector2(1f, 0f));
            trail.Append(new Vector2(1f, 1f));

            Vector2 point = trail.GetPointFromStart(1.25f);
            Assert.AreEqual(1f, point.x, 0.0001f);
            Assert.AreEqual(0.25f, point.y, 0.0001f);
        }

        [Test]
        public void GetPointFromStart_OutOfRange_ClampsToTrailEnds()
        {
            SnakeTrail trail = new SnakeTrail(0.25f);
            trail.Reset(new Vector2(0f, 0f));
            trail.Append(new Vector2(1f, 0f));

            Vector2 beforeStart = trail.GetPointFromStart(-1f);
            Vector2 beyondEnd = trail.GetPointFromStart(5f);
            Assert.AreEqual(0f, beforeStart.x, 0.0001f);
            Assert.AreEqual(1f, beyondEnd.x, 0.0001f);
        }

        [Test]
        public void Trim_RemovesOldPoints_KeepsQueryConsistent()
        {
            SnakeTrail trail = new SnakeTrail(0.25f);
            trail.Reset(new Vector2(0f, 0f));
            trail.Append(new Vector2(1f, 0f));
            trail.Append(new Vector2(2f, 0f));
            trail.Append(new Vector2(3f, 0f));

            trail.Trim(1.0f);

            // 弧长 0~2 的点被裁剪，只保留 (2,0)~(3,0) 一段；被裁剪区域的查询钳制到最旧保留点
            Assert.AreEqual(2, trail.PointCount);
            Assert.AreEqual(2f, trail.GetPointFromStart(0f).x, 0.0001f);
            Assert.AreEqual(2f, trail.GetPointFromStart(1.5f).x, 0.0001f);
            Assert.AreEqual(2.5f, trail.GetPointFromStart(2.5f).x, 0.0001f);
            Assert.AreEqual(3f, trail.GetPointFromStart(3f).x, 0.0001f);
            Assert.AreEqual(3f, trail.HeadArc, 0.0001f);
        }

        [Test]
        public void Constructor_NonPositiveSampleDistance_Throws()
        {
            Assert.Throws<System.ArgumentOutOfRangeException>(() => new SnakeTrail(0f));
        }
    }
}
