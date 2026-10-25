using System.Collections.Generic;
using NUnit.Framework;
using SepCore.Definition;
using SepCore.Exploration;
using UnityEngine;

namespace SepCore.Tests
{
    [TestFixture]
    public class PlayerRoomTrackerTests
    {
        private List<RoomDefinition> _testRooms;

        [SetUp]
        public void SetUp()
        {
            _testRooms = new List<RoomDefinition>
            {
                // Room 0: Normal room (-10, -10) to (0, 0)
                new RoomDefinition(new Vector2(-10f, -10f), new Vector2(0f, 0f), RoomType.Normal),
                // Room 1: Corridor (0, -5) to (10, -3)
                new RoomDefinition(new Vector2(0f, -5f), new Vector2(10f, -3f), RoomType.Corridor),
                // Room 2: Normal room (10, -10) to (20, 0)
                new RoomDefinition(new Vector2(10f, -10f), new Vector2(20f, 0f), RoomType.Normal),
            };
        }

        [Test]
        public void Initialize_WithStartingPositionInRoom_DetectsInitialRoomAndFiresEvent()
        {
            PlayerRoomTracker tracker = new PlayerRoomTracker();
            int enteredIndex = -1;
            tracker.OnRoomEntered += (idx, room) => enteredIndex = idx;

            tracker.Initialize(_testRooms, new Vector2(-5f, -5f));

            Assert.AreEqual(0, tracker.CurrentRoomIndex);
            Assert.IsNotNull(tracker.CurrentRoom);
            Assert.AreEqual(RoomType.Normal, tracker.CurrentRoom.Value.RoomType);
            Assert.IsFalse(tracker.CurrentRoom.Value.IsCorridor);
            Assert.AreEqual(0, enteredIndex);
        }

        [Test]
        public void Initialize_WithStartingPositionOutsideAnyRoom_CurrentRoomIndexIsNull()
        {
            PlayerRoomTracker tracker = new PlayerRoomTracker();
            bool eventFired = false;
            tracker.OnRoomEntered += (idx, room) => eventFired = true;
            tracker.OnRoomExited += (idx, room) => eventFired = true;

            tracker.Initialize(_testRooms, new Vector2(100f, 100f));

            Assert.IsNull(tracker.CurrentRoomIndex);
            Assert.IsNull(tracker.CurrentRoom);
            Assert.IsFalse(eventFired);
        }

        [Test]
        public void UpdatePosition_WhenStayingInsideSameRoom_ReturnsFalseAndDoesNotTriggerEvents()
        {
            PlayerRoomTracker tracker = new PlayerRoomTracker();
            tracker.Initialize(_testRooms, new Vector2(-5f, -5f));

            int enterCount = 0;
            int exitCount = 0;
            tracker.OnRoomEntered += (idx, room) => enterCount++;
            tracker.OnRoomExited += (idx, room) => exitCount++;

            bool changed = tracker.UpdatePosition(new Vector2(-2f, -2f), isMoving: true);

            Assert.IsFalse(changed);
            Assert.AreEqual(0, tracker.CurrentRoomIndex);
            Assert.AreEqual(0, enterCount);
            Assert.AreEqual(0, exitCount);
        }

        [Test]
        public void UpdatePosition_WhenNotMovingAndNotForced_SkipsCalculation()
        {
            PlayerRoomTracker tracker = new PlayerRoomTracker();
            tracker.Initialize(_testRooms, new Vector2(-5f, -5f));

            int eventCount = 0;
            tracker.OnRoomEntered += (idx, room) => eventCount++;
            tracker.OnRoomExited += (idx, room) => eventCount++;

            // Position changed to corridor, but isMoving = false and force = false
            bool changed = tracker.UpdatePosition(new Vector2(5f, -4f), isMoving: false, force: false);

            Assert.IsFalse(changed);
            Assert.AreEqual(0, tracker.CurrentRoomIndex);
            Assert.AreEqual(0, eventCount);
        }

        [Test]
        public void UpdatePosition_WhenMovingFromNormalRoomToCorridor_TriggersExitedThenEntered()
        {
            PlayerRoomTracker tracker = new PlayerRoomTracker();
            tracker.Initialize(_testRooms, new Vector2(-5f, -5f));

            List<int> exits = new List<int>();
            List<int> enters = new List<int>();
            tracker.OnRoomExited += (idx, room) => exits.Add(idx);
            tracker.OnRoomEntered += (idx, room) => enters.Add(idx);

            // Move to Room 1 (Corridor)
            bool changed = tracker.UpdatePosition(new Vector2(5f, -4f), isMoving: true);

            Assert.IsTrue(changed);
            Assert.AreEqual(1, tracker.CurrentRoomIndex);
            Assert.IsTrue(tracker.CurrentRoom.Value.IsCorridor);
            CollectionAssert.AreEqual(new[] { 0 }, exits);
            CollectionAssert.AreEqual(new[] { 1 }, enters);
        }

        [Test]
        public void UpdatePosition_WhenMovingToUnmappedArea_TriggersExitedAndBecomesNull()
        {
            PlayerRoomTracker tracker = new PlayerRoomTracker();
            tracker.Initialize(_testRooms, new Vector2(5f, -4f));

            int exitIndex = -1;
            bool enterFired = false;
            tracker.OnRoomExited += (idx, room) => exitIndex = idx;
            tracker.OnRoomEntered += (idx, room) => enterFired = true;

            // Move completely outside any room
            bool changed = tracker.UpdatePosition(new Vector2(50f, 50f), isMoving: true);

            Assert.IsTrue(changed);
            Assert.IsNull(tracker.CurrentRoomIndex);
            Assert.IsNull(tracker.CurrentRoom);
            Assert.AreEqual(1, exitIndex);
            Assert.IsFalse(enterFired);
        }

        [Test]
        public void UpdatePosition_OverlappingBoundary_PrefersCurrentRoom()
        {
            List<RoomDefinition> overlappingRooms = new List<RoomDefinition>
            {
                // Room 0: (0, 0) to (10, 10)
                new RoomDefinition(new Vector2(0f, 0f), new Vector2(10f, 10f), RoomType.Normal),
                // Room 1: (8, 0) to (18, 10) (overlaps Room 0 in x=[8, 10])
                new RoomDefinition(new Vector2(8f, 0f), new Vector2(18f, 10f), RoomType.Normal),
            };

            PlayerRoomTracker tracker = new PlayerRoomTracker();
            tracker.Initialize(overlappingRooms, new Vector2(2f, 5f));
            Assert.AreEqual(0, tracker.CurrentRoomIndex);

            int eventCount = 0;
            tracker.OnRoomEntered += (idx, room) => eventCount++;
            tracker.OnRoomExited += (idx, room) => eventCount++;

            // Move into overlapping zone (x=9)
            bool changed = tracker.UpdatePosition(new Vector2(9f, 5f), isMoving: true);
            Assert.IsFalse(changed);
            Assert.AreEqual(0, tracker.CurrentRoomIndex);
            Assert.AreEqual(0, eventCount);

            // Move out of Room 0 into Room 1 only (x=12)
            changed = tracker.UpdatePosition(new Vector2(12f, 5f), isMoving: true);
            Assert.IsTrue(changed);
            Assert.AreEqual(1, tracker.CurrentRoomIndex);
            Assert.AreEqual(1, eventCount / 2); // 1 exit + 1 enter
        }

        [Test]
        public void UpdatePosition_WithForceTrue_EvaluatesEvenWhenIsMovingIsFalse()
        {
            PlayerRoomTracker tracker = new PlayerRoomTracker();
            tracker.Initialize(_testRooms, new Vector2(-5f, -5f));

            int enteredIndex = -1;
            tracker.OnRoomEntered += (idx, room) => enteredIndex = idx;

            // Teleport to Room 2 with isMoving = false, force = true
            bool changed = tracker.UpdatePosition(new Vector2(15f, -5f), isMoving: false, force: true);

            Assert.IsTrue(changed);
            Assert.AreEqual(2, tracker.CurrentRoomIndex);
            Assert.AreEqual(2, enteredIndex);
        }
    }
}
