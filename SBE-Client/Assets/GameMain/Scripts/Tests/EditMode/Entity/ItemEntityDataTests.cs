using NUnit.Framework;
using SepCore.Definition;
using SepCore.Entity;
using UnityEngine;

namespace SepCore.Tests
{
    [TestFixture]
    public class ItemEntityDataTests
    {
        [Test]
        public void Constructor_InitializesCorrectly_WithoutSpawnFromPosition()
        {
            Vector3 position = new Vector3(5f, 10f, 0f);

            ItemEntityData data = new ItemEntityData(
                entityId: 201,
                assetName: "ItemEntity",
                position: position,
                itemId: 1001,
                count: 3,
                rarity: Rarity.Blue
            );

            Assert.AreEqual(201, data.Id);
            Assert.AreEqual("ItemEntity", data.AssetName);
            Assert.AreEqual(position, data.Position);
            Assert.AreEqual(1001, data.ItemId);
            Assert.AreEqual(3, data.Count);
            Assert.AreEqual(Rarity.Blue, data.Rarity);
            Assert.AreEqual(Quaternion.identity, data.Rotation);
            Assert.IsNull(data.SpawnFromPosition);
        }

        [Test]
        public void Constructor_WithSpawnFromPosition_StoresCorrectly()
        {
            Vector3 dropPosition = new Vector3(8f, 12f, 0f);
            Vector3 spawnFromPosition = new Vector3(2f, 3f, 0f);

            ItemEntityData data = new ItemEntityData(
                entityId: 202,
                assetName: "ItemEntity",
                position: dropPosition,
                itemId: 2005,
                count: 1,
                rarity: Rarity.Gold,
                rotation: Quaternion.identity,
                spawnFromPosition: spawnFromPosition
            );

            Assert.AreEqual(dropPosition, data.Position);
            Assert.IsNotNull(data.SpawnFromPosition);
            Assert.AreEqual(spawnFromPosition, data.SpawnFromPosition.Value);
        }

        [Test]
        public void Constructor_ClampsCountToAtLeastOne()
        {
            ItemEntityData data = new ItemEntityData(
                entityId: 203,
                assetName: "ItemEntity",
                position: Vector3.zero,
                itemId: 3001,
                count: 0,
                rarity: Rarity.White
            );

            Assert.AreEqual(1, data.Count);
        }
    }
}
