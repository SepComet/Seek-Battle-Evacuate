using NUnit.Framework;
using SepCore.Entity;
using UnityEngine;

namespace SepCore.Tests
{
    [TestFixture]
    public class EvacuatePointDataTests
    {
        [Test]
        public void Constructor_InitializesCorrectly()
        {
            Vector3 position = new Vector3(15f, 25f, 0f);

            EvacuatePointData data = new EvacuatePointData(
                entityId: 301,
                assetName: "EvacuatePoint",
                position: position
            );

            Assert.AreEqual(301, data.Id);
            Assert.AreEqual("EvacuatePoint", data.AssetName);
            Assert.AreEqual(position, data.Position);
            Assert.AreEqual(Quaternion.identity, data.Rotation);
            Assert.IsTrue(data.IsOpen);
        }

        [Test]
        public void Constructor_ExplicitArguments_InitializesCorrectly()
        {
            Vector3 position = new Vector3(-10f, 0f, 0f);
            Quaternion rotation = Quaternion.Euler(0f, 0f, 180f);

            EvacuatePointData data = new EvacuatePointData(
                entityId: 302,
                assetName: "CustomEvacuatePoint",
                position: position,
                isOpen: false,
                rotation: rotation
            );

            Assert.AreEqual(302, data.Id);
            Assert.AreEqual("CustomEvacuatePoint", data.AssetName);
            Assert.AreEqual(position, data.Position);
            Assert.AreEqual(rotation, data.Rotation);
            Assert.IsFalse(data.IsOpen);
        }

        [Test]
        public void IsOpen_CanBeMutated()
        {
            EvacuatePointData data = new EvacuatePointData(
                entityId: 303,
                assetName: "EvacuatePoint",
                position: Vector3.zero
            );

            Assert.IsTrue(data.IsOpen);

            data.IsOpen = false;
            Assert.IsFalse(data.IsOpen);

            data.IsOpen = true;
            Assert.IsTrue(data.IsOpen);
        }
    }
}
