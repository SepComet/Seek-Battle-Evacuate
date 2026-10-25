using NUnit.Framework;
using SepCore.Entity;
using UnityEngine;

namespace SepCore.Tests
{
    [TestFixture]
    public class WorldFloatTextDataTests
    {
        [Test]
        public void Constructor_InitializesCorrectly_WithDefaultValues()
        {
            Vector3 position = new Vector3(3f, 4f, 0f);
            Color color = Color.yellow;

            WorldFloatTextData data = new WorldFloatTextData(
                entityId: 301,
                assetName: "WorldFloatText",
                position: position,
                text: "+120",
                color: color
            );

            Assert.AreEqual(301, data.Id);
            Assert.AreEqual("WorldFloatText", data.AssetName);
            Assert.AreEqual(position, data.Position);
            Assert.AreEqual("+120", data.Text);
            Assert.AreEqual(color, data.Color);
            Assert.AreEqual(0.65f, data.Duration, 0.001f);
            Assert.AreEqual(0.8f, data.FloatDistance, 0.001f);
            Assert.AreEqual(Quaternion.identity, data.Rotation);
        }

        [Test]
        public void Constructor_InitializesCorrectly_WithCustomValues()
        {
            Vector3 position = new Vector3(1f, 2f, 0f);
            Color color = Color.red;

            WorldFloatTextData data = new WorldFloatTextData(
                entityId: 302,
                assetName: "WorldFloatText",
                position: position,
                text: "+5,000",
                color: color,
                duration: 1.0f,
                floatDistance: 1.5f,
                rotation: Quaternion.Euler(0f, 0f, 45f)
            );

            Assert.AreEqual(302, data.Id);
            Assert.AreEqual("+5,000", data.Text);
            Assert.AreEqual(color, data.Color);
            Assert.AreEqual(1.0f, data.Duration, 0.001f);
            Assert.AreEqual(1.5f, data.FloatDistance, 0.001f);
            Assert.AreEqual(Quaternion.Euler(0f, 0f, 45f), data.Rotation);
        }
    }
}
