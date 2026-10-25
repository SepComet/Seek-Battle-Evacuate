using System;
using System.Collections.Generic;
using NUnit.Framework;
using SepCore.Entity;
using UnityEngine;

namespace SepCore.Tests
{
    [TestFixture]
    public class ResourcePointDataTests
    {
        [Test]
        public void Constructor_ValidArguments_InitializesPropertiesCorrectly()
        {
            Vector3 position = new Vector3(10f, 20f, 0f);
            Quaternion rotation = Quaternion.Euler(0f, 0f, 90f);
            List<int> itemIds = new List<int> { 5001, 5002, 5003 };

            ResourcePointData data = new ResourcePointData(
                entityId: 100,
                assetName: "ResourcePoint1",
                position: position,
                resourcePointId: 1,
                itemIds: itemIds,
                rotation: rotation
            );

            Assert.AreEqual(100, data.Id);
            Assert.AreEqual("ResourcePoint1", data.AssetName);
            Assert.AreEqual(position, data.Position);
            Assert.AreEqual(rotation, data.Rotation);
            Assert.AreEqual(1, data.ResourcePointId);
            Assert.AreEqual(3, data.ItemCount);
            CollectionAssert.AreEqual(new[] { 5001, 5002, 5003 }, data.ItemIds);
        }

        [Test]
        public void Constructor_NullRotation_DefaultsToIdentity()
        {
            ResourcePointData data = new ResourcePointData(
                entityId: 1,
                assetName: "ResourcePoint1",
                position: Vector3.one,
                resourcePointId: 2,
                itemIds: new List<int>()
            );

            Assert.AreEqual(Quaternion.identity, data.Rotation);
            Assert.AreEqual(0, data.ItemCount);
        }

        [Test]
        public void Constructor_NullItemIds_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() =>
            {
                _ = new ResourcePointData(
                    entityId: 1,
                    assetName: "ResourcePoint1",
                    position: Vector3.zero,
                    resourcePointId: 1,
                    itemIds: null
                );
            });
        }

        [Test]
        public void ItemIds_IsDefensiveCopy_ExternalModificationsDoNotAffect()
        {
            List<int> sourceList = new List<int> { 101, 102 };
            ResourcePointData data = new ResourcePointData(
                entityId: 1,
                assetName: "ResourcePoint1",
                position: Vector3.zero,
                resourcePointId: 1,
                itemIds: sourceList
            );

            sourceList.Add(103);

            Assert.AreEqual(2, data.ItemCount);
            CollectionAssert.AreEqual(new[] { 101, 102 }, data.ItemIds);
        }

        [Test]
        public void SearchProgressTime_DefaultsToZero_AndClampsToNonNegative()
        {
            ResourcePointData data = new ResourcePointData(
                entityId: 1,
                assetName: "ResourcePoint1",
                position: Vector3.zero,
                resourcePointId: 1,
                itemIds: new List<int> { 101 }
            );

            Assert.AreEqual(0f, data.SearchProgressTime);

            data.SearchProgressTime = 1.5f;
            Assert.AreEqual(1.5f, data.SearchProgressTime);

            data.SearchProgressTime = -0.5f;
            Assert.AreEqual(0f, data.SearchProgressTime);
        }

        [Test]
        public void DroppedItemCount_And_IsCompleted_BehaveCorrectly()
        {
            ResourcePointData data = new ResourcePointData(
                entityId: 1,
                assetName: "ResourcePoint1",
                position: Vector3.zero,
                resourcePointId: 1,
                itemIds: new List<int> { 101, 102 }
            );

            Assert.AreEqual(0, data.DroppedItemCount);
            Assert.IsFalse(data.IsCompleted);

            data.DroppedItemCount = 1;
            Assert.AreEqual(1, data.DroppedItemCount);
            Assert.IsFalse(data.IsCompleted);

            data.DroppedItemCount = 2;
            Assert.AreEqual(2, data.DroppedItemCount);
            Assert.IsTrue(data.IsCompleted);

            // 超出范围应被截断在 [0, ItemCount]
            data.DroppedItemCount = 99;
            Assert.AreEqual(2, data.DroppedItemCount);

            data.DroppedItemCount = -5;
            Assert.AreEqual(0, data.DroppedItemCount);
            Assert.IsFalse(data.IsCompleted);
        }

        [Test]
        public void IsCompleted_EmptyItems_ReturnsTrueImmediately()
        {
            ResourcePointData data = new ResourcePointData(
                entityId: 1,
                assetName: "ResourcePoint1",
                position: Vector3.zero,
                resourcePointId: 1,
                itemIds: new List<int>()
            );

            Assert.IsTrue(data.IsCompleted);
        }

        [Test]
        public void Constructor_StoresPointId()
        {
            ResourcePointData data = new ResourcePointData(
                entityId: 1,
                assetName: "ResourcePoint1",
                position: Vector3.zero,
                resourcePointId: 1,
                itemIds: new List<int>(),
                pointId: "r2");

            Assert.AreEqual("r2", data.PointId);
            Assert.IsTrue(data.ContentsReady);
        }

        /// <summary>
        /// 联网流程：开局内容待定（等 loot 响应）时，箱子必须仍可交互，
        /// 否则玩家根本无法发起第一次开箱请求。
        /// </summary>
        [Test]
        public void CreatePendingContents_IsInteractableAndNotCompleted()
        {
            ResourcePointData data = ResourcePointData.CreatePendingContents(
                entityId: 7, assetName: "ResourcePoint1", position: Vector3.zero,
                resourcePointId: 1, pointId: "r0");

            Assert.AreEqual("r0", data.PointId);
            Assert.IsFalse(data.ContentsReady);
            Assert.AreEqual(0, data.ItemCount);
            Assert.IsFalse(data.IsCompleted);
        }

        /// <summary>
        /// loot 响应到达后灌入清单：内容就绪、数量正确、掉落进度归零，随后按阈值逐件掉完才算完成。
        /// </summary>
        [Test]
        public void ApplyItems_FillsContentsThenCompletesAfterAllDropped()
        {
            ResourcePointData data = ResourcePointData.CreatePendingContents(
                entityId: 7, assetName: "ResourcePoint1", position: Vector3.zero,
                resourcePointId: 1, pointId: "r0");

            data.ApplyItems(new[] { 5001, 5003 });

            Assert.IsTrue(data.ContentsReady);
            Assert.AreEqual(2, data.ItemCount);
            CollectionAssert.AreEqual(new[] { 5001, 5003 }, data.ItemIds);
            Assert.AreEqual(0, data.DroppedItemCount);
            Assert.AreEqual(0f, data.SearchProgressTime);
            Assert.IsFalse(data.IsCompleted);

            data.DroppedItemCount = 1;
            Assert.IsFalse(data.IsCompleted);

            data.DroppedItemCount = 2;
            Assert.IsTrue(data.IsCompleted);
        }

        [Test]
        public void ApplyItems_EmptyLoot_CompletesImmediately()
        {
            ResourcePointData data = ResourcePointData.CreatePendingContents(
                entityId: 7, assetName: "ResourcePoint1", position: Vector3.zero,
                resourcePointId: 1, pointId: "r0");

            data.ApplyItems(new List<int>());

            Assert.IsTrue(data.ContentsReady);
            Assert.IsTrue(data.IsCompleted);
        }

        [Test]
        public void ApplyItems_IsDefensiveCopy()
        {
            ResourcePointData data = ResourcePointData.CreatePendingContents(
                entityId: 7, assetName: "ResourcePoint1", position: Vector3.zero,
                resourcePointId: 1, pointId: "r0");

            List<int> source = new List<int> { 101, 102 };
            data.ApplyItems(source);
            source.Add(103);

            Assert.AreEqual(2, data.ItemCount);
            CollectionAssert.AreEqual(new[] { 101, 102 }, data.ItemIds);
        }

        [Test]
        public void ApplyItems_Null_ThrowsArgumentNullException()
        {
            ResourcePointData data = ResourcePointData.CreatePendingContents(
                entityId: 7, assetName: "ResourcePoint1", position: Vector3.zero,
                resourcePointId: 1, pointId: "r0");

            Assert.Throws<ArgumentNullException>(() => data.ApplyItems(null));
        }

        /// <summary>
        /// 内容只允许灌入一次：重复灌入说明状态机出错，直接抛错而不是静默覆盖
        /// （覆盖会丢掉已经掉落一半的进度）。
        /// </summary>
        [Test]
        public void ApplyItems_WhenContentsAlreadyReady_Throws()
        {
            ResourcePointData data = new ResourcePointData(
                entityId: 1,
                assetName: "ResourcePoint1",
                position: Vector3.zero,
                resourcePointId: 1,
                itemIds: new List<int> { 101 },
                pointId: "r0");

            Assert.Throws<InvalidOperationException>(() => data.ApplyItems(new[] { 102 }));
        }
    }
}
