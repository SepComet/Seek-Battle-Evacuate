using System;
using NUnit.Framework;
using SepCore.Definition;
using SepCore.Entity;
using UnityEngine;

namespace SepCore.Tests
{
    [TestFixture]
    public class EnemyPartyDataTests
    {
        [Test]
        public void Constructor_ValidArguments_InitializesPropertiesCorrectly()
        {
            Vector3 position = new Vector3(5f, -3f, 0f);
            Quaternion rotation = Quaternion.Euler(0f, 0f, 45f);

            EnemyPartyData data = new EnemyPartyData(
                entityId: 201,
                assetName: "EnemyPartyLowThreat",
                position: position,
                enemyPartyId: 4001,
                threatLevel: EnemyPartyThreatLevel.Low,
                rotation: rotation
            );

            Assert.AreEqual(201, data.Id);
            Assert.AreEqual("EnemyPartyLowThreat", data.AssetName);
            Assert.AreEqual(position, data.Position);
            Assert.AreEqual(rotation, data.Rotation);
            Assert.AreEqual(4001, data.EnemyPartyId);
            Assert.AreEqual(EnemyPartyThreatLevel.Low, data.ThreatLevel);
        }

        [Test]
        public void Constructor_NullRotation_DefaultsToIdentity()
        {
            EnemyPartyData data = new EnemyPartyData(
                entityId: 202,
                assetName: "EnemyPartyMiddleThreat",
                position: Vector3.zero,
                enemyPartyId: 4002,
                threatLevel: EnemyPartyThreatLevel.Middle
            );

            Assert.AreEqual(Quaternion.identity, data.Rotation);
        }

        [TestCase(EnemyPartyThreatLevel.Low, "EnemyPartyLowThreat")]
        [TestCase(EnemyPartyThreatLevel.Middle, "EnemyPartyMiddleThreat")]
        [TestCase(EnemyPartyThreatLevel.High, "EnemyPartyHighThreat")]
        public void Constructor_SetsThreatLevelAndAssetName(
            EnemyPartyThreatLevel threatLevel, string assetName)
        {
            EnemyPartyData data = new EnemyPartyData(
                entityId: 203,
                assetName: assetName,
                position: Vector3.zero,
                enemyPartyId: 4001,
                threatLevel: threatLevel
            );

            Assert.AreEqual(threatLevel, data.ThreatLevel);
            Assert.AreEqual(assetName, data.AssetName);
        }
    }
}
