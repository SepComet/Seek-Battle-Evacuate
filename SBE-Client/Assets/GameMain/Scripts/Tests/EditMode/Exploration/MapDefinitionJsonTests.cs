using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using SepCore.Definition;
using SepCore.Exploration;
using UnityEngine;

namespace SepCore.Tests
{
    [TestFixture]
    public class MapDefinitionJsonTests
    {
        private readonly List<MapDefinition> _definitions = new List<MapDefinition>();

        [TearDown]
        public void TearDown()
        {
            foreach (MapDefinition definition in _definitions)
            {
                UnityEngine.Object.DestroyImmediate(definition);
            }

            _definitions.Clear();
        }

        [Test]
        public void Serialize_NormalizesRoomsAndPoints()
        {
            MapDefinition definition = CreateMapDefinition(
                new List<Vector2> { new Vector2(1f, -16f) },
                new List<ResourcePointDefinition> { CreateResourcePoint(new Vector2(-4f, 5f), 1) },
                new List<Vector2> { new Vector2(-19f, 1f) },
                new List<Vector2> { new Vector2(30f, -3f) },
                new List<RoomDefinition>
                {
                    // pointA/pointB 反向书写，导出必须归一化为 min/max
                    new RoomDefinition(new Vector2(-13f, 6f), new Vector2(-23f, -5f)),
                    new RoomDefinition(new Vector2(2f, 10f), new Vector2(5f, 7f), RoomType.Corridor)
                });

            string json = MapDefinitionJson.Serialize(definition);

            StringAssert.Contains("\"type\": 0, \"minX\": -23, \"minY\": -5, \"maxX\": -13, \"maxY\": 6", json);
            StringAssert.Contains("\"type\": 1, \"minX\": 2, \"minY\": 7, \"maxX\": 5, \"maxY\": 10", json);
            StringAssert.Contains("\"x\": -4, \"y\": 5, \"resourcePointId\": 1", json);
            StringAssert.Contains("\"enemySpawnPoints\"", json);
            StringAssert.Contains("{ \"x\": -19, \"y\": 1 }", json);
            StringAssert.Contains("{ \"x\": 1, \"y\": -16 }", json);
            StringAssert.Contains("{ \"x\": 30, \"y\": -3 }", json);
            StringAssert.DoesNotContain("pointA", json);
            StringAssert.DoesNotContain("pointB", json);
        }

        [Test]
        public void Serialize_RejectsEnemyPointOutsideRooms()
        {
            MapDefinition definition = CreateMapDefinition(
                new List<Vector2> { Vector2.zero },
                new List<ResourcePointDefinition> { CreateResourcePoint(Vector2.zero, 1) },
                new List<Vector2> { new Vector2(100f, 100f) },
                new List<Vector2> { Vector2.zero },
                new List<RoomDefinition> { new RoomDefinition(Vector2.zero, new Vector2(5f, 5f)) });

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
                () => MapDefinitionJson.Serialize(definition));
            StringAssert.Contains("does not belong to any room", exception.Message);
        }

        [Test]
        public void Serialize_UsesRoomTileIntervalInclusiveOfMaxTile()
        {
            // 房间覆盖瓦片 [Min, Max] 闭区间：Max 瓦片本身与 Max+1 之前都算房间内
            MapDefinition definition = CreateMapDefinition(
                new List<Vector2> { Vector2.zero },
                new List<ResourcePointDefinition> { CreateResourcePoint(Vector2.zero, 1) },
                new List<Vector2> { new Vector2(10f, 5.5f) },
                new List<Vector2> { Vector2.zero },
                new List<RoomDefinition> { new RoomDefinition(Vector2.zero, new Vector2(10f, 5f)) });

            Assert.DoesNotThrow(() => MapDefinitionJson.Serialize(definition));

            MapDefinition outside = CreateMapDefinition(
                new List<Vector2> { Vector2.zero },
                new List<ResourcePointDefinition> { CreateResourcePoint(Vector2.zero, 1) },
                new List<Vector2> { new Vector2(11f, 5f) },
                new List<Vector2> { Vector2.zero },
                new List<RoomDefinition> { new RoomDefinition(Vector2.zero, new Vector2(10f, 5f)) });

            Assert.Throws<InvalidOperationException>(() => MapDefinitionJson.Serialize(outside));
        }

        [TestCase("resource")]
        [TestCase("enemy")]
        [TestCase("player")]
        [TestCase("extraction")]
        public void Serialize_RejectsEmptyCandidateList(string emptyList)
        {
            MapDefinition definition = CreateMapDefinition(
                emptyList == "player" ? new List<Vector2>() : new List<Vector2> { Vector2.zero },
                emptyList == "resource"
                    ? new List<ResourcePointDefinition>()
                    : new List<ResourcePointDefinition> { CreateResourcePoint(Vector2.zero, 1) },
                emptyList == "enemy" ? new List<Vector2>() : new List<Vector2> { Vector2.zero },
                emptyList == "extraction" ? new List<Vector2>() : new List<Vector2> { Vector2.zero },
                new List<RoomDefinition> { new RoomDefinition(Vector2.zero, new Vector2(5f, 5f)) });

            Assert.Throws<InvalidOperationException>(() => MapDefinitionJson.Serialize(definition));
        }

        [Test]
        public void Serialize_RejectsEmptyRoomList()
        {
            MapDefinition definition = CreateMapDefinition(
                new List<Vector2> { Vector2.zero },
                new List<ResourcePointDefinition> { CreateResourcePoint(Vector2.zero, 1) },
                new List<Vector2> { Vector2.zero },
                new List<Vector2> { Vector2.zero },
                new List<RoomDefinition>());

            Assert.Throws<InvalidOperationException>(() => MapDefinitionJson.Serialize(definition));
        }

        [Test]
        public void RoomIndexAt_ReportsFirstContainingRoom()
        {
            List<RoomDefinition> rooms = new List<RoomDefinition>
            {
                new RoomDefinition(Vector2.zero, new Vector2(5f, 5f)),
                new RoomDefinition(new Vector2(10f, 0f), new Vector2(15f, 5f))
            };

            Assert.AreEqual(0, MapDefinitionJson.RoomIndexAt(rooms, new Vector2(2f, 2f)));
            Assert.AreEqual(1, MapDefinitionJson.RoomIndexAt(rooms, new Vector2(12f, 2f)));
            Assert.AreEqual(-1, MapDefinitionJson.RoomIndexAt(rooms, new Vector2(7f, 2f)));
        }

        private MapDefinition CreateMapDefinition(List<Vector2> playerPoints,
            List<ResourcePointDefinition> resourcePoints, List<Vector2> enemyPoints,
            List<Vector2> extractionPoints, List<RoomDefinition> rooms)
        {
            MapDefinition definition = ScriptableObject.CreateInstance<MapDefinition>();
            _definitions.Add(definition);
            SetField(definition, "_playerSpawnPoints", playerPoints);
            SetField(definition, "_resourcePoints", resourcePoints);
            SetField(definition, "_enemySpawnPoints", enemyPoints);
            SetField(definition, "_extractionPoints", extractionPoints);
            SetField(definition, "_rooms", rooms);
            return definition;
        }

        private static ResourcePointDefinition CreateResourcePoint(Vector2 position, int resourcePointId)
        {
            ResourcePointDefinition definition = new ResourcePointDefinition();
            object boxed = definition;
            SetField(boxed, "_position", position);
            SetField(boxed, "_resourcePointId", resourcePointId);
            return (ResourcePointDefinition)boxed;
        }

        private static void SetField(object target, string fieldName, object value)
        {
            FieldInfo field = target.GetType().GetField(fieldName,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            field.SetValue(target, value);
        }
    }
}
