using System.Collections.Generic;
using NUnit.Framework;
using SepCore.Definition;
using SepCore.Exploration;
using UnityEngine;

namespace SepCore.Tests
{
    [TestFixture]
    public class EnemyAlertnessTrackerTests
    {
        private ThreatLevelConfig _threatConfig;
        private GlobalConfig _globalConfig;

        [SetUp]
        public void SetUp()
        {
            AlertDistanceBand band1 = TestConfigFactory.Create<AlertDistanceBand>(
                "MaxDistanceMilli", 2000,
                "AlertPerSecondMilli", 500);

            AlertDistanceBand band2 = TestConfigFactory.Create<AlertDistanceBand>(
                "MaxDistanceMilli", 6000,
                "AlertPerSecondMilli", 250);

            _threatConfig = TestConfigFactory.Create<ThreatLevelConfig>(
                "Id", EnemyPartyThreatLevel.Low,
                "Name", "LowThreat",
                "MaxViewDistanceMilli", 6000,
                "AlertDecayPerSecondMilli", 200,
                "DistanceBands", new List<AlertDistanceBand> { band1, band2 });

            _globalConfig = TestConfigFactory.Create<GlobalConfig>(
                "AlertMax", 1000,
                "EnemyLoseTargetMs", 3000,
                "ChaseSpeed", 3000,
                "PatrolSpeed", 1500,
                "PlayerSpeed", 3500);
        }

        [Test]
        public void Detect_PlayerAnyDirectionWithinRange_IncreasesAlertnessCircularly()
        {
            EnemyAlertnessTracker tracker = new EnemyAlertnessTracker(_threatConfig, _globalConfig);
            Vector2 enemyPos = Vector2.zero;

            // 1. Right direction (1.5m <= 2.0m): band1 with 500/s
            Vector2 playerRight = new Vector2(1.5f, 0f);
            tracker.UpdateDetection(enemyPos, playerRight, 0, 0, false, 1.0f);
            Assert.AreEqual(500f, tracker.CurrentAlertness, 0.01f);

            // 2. Reset and test Left direction (-1.5m, formerly "behind"): also circular 360° detection with 500/s
            tracker.ResetAlertness();
            Vector2 playerLeft = new Vector2(-1.5f, 0f);
            tracker.UpdateDetection(enemyPos, playerLeft, 0, 0, false, 1.0f);
            Assert.AreEqual(500f, tracker.CurrentAlertness, 0.01f);

            // 3. Medium range (4.0m <= 6.0m): band2 with 250/s
            tracker.ResetAlertness();
            Vector2 playerUp = new Vector2(0f, 4.0f);
            tracker.UpdateDetection(enemyPos, playerUp, 0, 0, false, 1.0f);
            Assert.AreEqual(250f, tracker.CurrentAlertness, 0.01f);
        }

        [Test]
        public void Detect_PlayerBeyondMaxDistance_AlertnessDecays()
        {
            EnemyAlertnessTracker tracker = new EnemyAlertnessTracker(_threatConfig, _globalConfig);
            Vector2 enemyPos = Vector2.zero;

            // First increase alertness
            tracker.UpdateDetection(enemyPos, new Vector2(1.5f, 0f), 0, 0, false, 1.0f);
            Assert.AreEqual(500f, tracker.CurrentAlertness, 0.01f);

            // Player moves to 8m (max is 6m)
            Vector2 playerFar = new Vector2(8.0f, 0f);
            tracker.UpdateDetection(enemyPos, playerFar, 0, 0, false, 1.0f);

            // Decays by 200/s: 500 - 200 = 300
            Assert.AreEqual(300f, tracker.CurrentAlertness, 0.01f);
        }

        [Test]
        public void Detect_AlertnessReachesMax_TransitionsToPursuit()
        {
            EnemyAlertnessTracker tracker = new EnemyAlertnessTracker(_threatConfig, _globalConfig);
            Vector2 enemyPos = Vector2.zero;
            Vector2 playerPos = new Vector2(1.0f, 0f);

            Assert.AreEqual(EnemyExplorationState.Patrol, tracker.State);
            Assert.IsFalse(tracker.IsAlertFull);

            // Band1 gives 500/s, so 2 seconds gives 1000 (AlertMax)
            tracker.UpdateDetection(enemyPos, playerPos, 0, 0, false, 2.0f);

            Assert.AreEqual(1000f, tracker.CurrentAlertness, 0.01f);
            Assert.IsTrue(tracker.IsAlertFull);
            Assert.AreEqual(EnemyExplorationState.Pursuit, tracker.State);
            Assert.AreEqual(1.0f, tracker.FillAmount, 0.001f);
        }

        [Test]
        public void Detect_InPursuit_MaintainsPursuitAnywhereInSameRoomEvenBeyondMaxViewDistance()
        {
            EnemyAlertnessTracker tracker = new EnemyAlertnessTracker(_threatConfig, _globalConfig);
            Vector2 enemyPos = Vector2.zero;
            Vector2 playerPos = new Vector2(1.0f, 0f);

            // Enter Pursuit
            tracker.UpdateDetection(enemyPos, playerPos, 0, 0, false, 2.0f);
            Assert.AreEqual(EnemyExplorationState.Pursuit, tracker.State);
            Assert.IsTrue(tracker.IsAlertFull);

            // Player runs 15 meters away (well beyond 6m maxViewDistance), but STILL in room 0!
            Vector2 playerFarInRoom = new Vector2(15.0f, 0f);
            tracker.UpdateDetection(enemyPos, playerFarInRoom, 0, 0, false, 5.0f);

            // Pursuit must remain active, alertness remains full, lost target timer remains 0
            Assert.AreEqual(EnemyExplorationState.Pursuit, tracker.State);
            Assert.AreEqual(1000f, tracker.CurrentAlertness, 0.01f);
            Assert.IsTrue(tracker.IsAlertFull);
            Assert.AreEqual(0f, tracker.LostTargetTimer);
        }

        [Test]
        public void Detect_InPursuit_PlayerLeavesRoom_AlertnessDecaysAndLosesTargetAfter3Seconds()
        {
            EnemyAlertnessTracker tracker = new EnemyAlertnessTracker(_threatConfig, _globalConfig);
            Vector2 enemyPos = Vector2.zero;
            Vector2 playerPos = new Vector2(1.0f, 0f);

            // Enter Pursuit
            tracker.UpdateDetection(enemyPos, playerPos, 0, 0, false, 2.0f);
            Assert.AreEqual(EnemyExplorationState.Pursuit, tracker.State);

            // Player exits into room 1 (or corridor)
            int enemyRoom = 0;
            int playerRoom = 1;

            // 1 second later: LostTarget state, timer = 1.0s, alertness decayed to 800
            tracker.UpdateDetection(enemyPos, playerPos, playerRoom, enemyRoom, false, 1.0f);
            Assert.AreEqual(EnemyExplorationState.LostTarget, tracker.State);
            Assert.AreEqual(1.0f, tracker.LostTargetTimer, 0.01f);
            Assert.AreEqual(800f, tracker.CurrentAlertness, 0.01f);
            Assert.IsFalse(tracker.IsTargetLost);

            // 2.1 seconds later (total 3.1s > 3.0s lose target duration):
            tracker.UpdateDetection(enemyPos, playerPos, playerRoom, enemyRoom, false, 2.1f);
            Assert.AreEqual(EnemyExplorationState.Patrol, tracker.State);
            Assert.AreEqual(0f, tracker.LostTargetTimer);
        }

        [Test]
        public void Detect_InPursuit_EscapeProtectionForcesLostTargetAndDecay()
        {
            EnemyAlertnessTracker tracker = new EnemyAlertnessTracker(_threatConfig, _globalConfig);
            Vector2 enemyPos = Vector2.zero;
            Vector2 playerPos = new Vector2(1.0f, 0f);

            // Enter Pursuit
            tracker.UpdateDetection(enemyPos, playerPos, 0, 0, false, 2.0f);
            Assert.AreEqual(EnemyExplorationState.Pursuit, tracker.State);

            // Escape protection activates while in the same room
            tracker.UpdateDetection(enemyPos, playerPos, 0, 0, true, 1.0f);

            Assert.AreEqual(EnemyExplorationState.LostTarget, tracker.State);
            Assert.AreEqual(1.0f, tracker.LostTargetTimer, 0.01f);
            Assert.AreEqual(800f, tracker.CurrentAlertness, 0.01f);
        }

        [Test]
        public void Detect_EscapeProtectionActive_AlertnessDoesNotIncrease()
        {
            EnemyAlertnessTracker tracker = new EnemyAlertnessTracker(_threatConfig, _globalConfig);
            Vector2 enemyPos = Vector2.zero;
            Vector2 playerPos = new Vector2(1.0f, 0f);

            // Player is close, but escape protection is active
            tracker.UpdateDetection(enemyPos, playerPos, 0, 0, true, 1.0f);

            Assert.AreEqual(0f, tracker.CurrentAlertness);
            Assert.AreEqual(EnemyExplorationState.Patrol, tracker.State);
        }

        [Test]
        public void ResetAlertness_ClearsCurrentAlertnessAndReturnsToPatrol()
        {
            EnemyAlertnessTracker tracker = new EnemyAlertnessTracker(_threatConfig, _globalConfig);
            Vector2 enemyPos = Vector2.zero;
            Vector2 playerPos = new Vector2(1.0f, 0f);

            // Enter Pursuit
            tracker.UpdateDetection(enemyPos, playerPos, 0, 0, false, 2.0f);
            Assert.AreEqual(EnemyExplorationState.Pursuit, tracker.State);
            Assert.IsTrue(tracker.IsAlertFull);

            tracker.ResetAlertness();

            Assert.AreEqual(0f, tracker.CurrentAlertness);
            Assert.AreEqual(EnemyExplorationState.Patrol, tracker.State);
            Assert.AreEqual(0f, tracker.LostTargetTimer);
            Assert.IsFalse(tracker.IsAlertFull);
        }

        [Test]
        public void FillAmount_ReflectsNormalizedAlertnessAccurately()
        {
            EnemyAlertnessTracker tracker = new EnemyAlertnessTracker(_threatConfig, _globalConfig);
            Assert.AreEqual(0f, tracker.FillAmount);
            Assert.IsFalse(tracker.IsAlertFull);

            Vector2 enemyPos = Vector2.zero;
            Vector2 playerPos = new Vector2(1.5f, 0f);

            // 1s with 500/s rate -> 500 / 1000 = 0.5
            tracker.UpdateDetection(enemyPos, playerPos, 0, 0, false, 1.0f);
            Assert.AreEqual(0.5f, tracker.FillAmount, 0.001f);
            Assert.IsFalse(tracker.IsAlertFull);

            // Another 1s -> 1000 / 1000 = 1.0
            tracker.UpdateDetection(enemyPos, playerPos, 0, 0, false, 1.0f);
            Assert.AreEqual(1.0f, tracker.FillAmount, 0.001f);
            Assert.IsTrue(tracker.IsAlertFull);
        }
    }
}
