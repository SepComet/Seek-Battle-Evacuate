using System.Collections.Generic;
using NUnit.Framework;
using SepCore.Definition;
using SepCore.Exploration;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace SepCore.Tests
{
    [TestFixture]
    public class FogTilemapControllerTests
    {
        private GameObject _rootGo;
        private Tilemap _tilemap;
        private FogTilemapController _controller;
        private List<RoomDefinition> _testRooms;

        [SetUp]
        public void SetUp()
        {
            _rootGo = new GameObject("TestFogRoot");
            _tilemap = _rootGo.AddComponent<Tilemap>();
            _rootGo.AddComponent<TilemapRenderer>();
            _controller = _rootGo.AddComponent<FogTilemapController>();
            _controller.SetFogTilemapForTest(_tilemap);
            _controller.FadeDuration = 0f;

            _testRooms = new List<RoomDefinition>
            {
                new RoomDefinition(new Vector2(-10f, -10f), new Vector2(0f, 0f), RoomType.Normal),
                new RoomDefinition(new Vector2(0f, -5f), new Vector2(10f, -3f), RoomType.Corridor),
                new RoomDefinition(new Vector2(10f, -10f), new Vector2(20f, 0f), RoomType.Normal)
            };
        }

        [TearDown]
        public void TearDown()
        {
            if (_controller != null)
            {
                _controller.ClearOverlays();
            }

            if (_rootGo != null)
            {
                Object.DestroyImmediate(_rootGo);
            }
        }

        [Test]
        public void InitializeRoomOverlays_WithNullOrEmpty_DoesNotCreateOverlays()
        {
            _controller.InitializeRoomOverlays(null);
            Assert.AreEqual(0, _controller.OverlayCount);

            _controller.InitializeRoomOverlays(new List<RoomDefinition>());
            Assert.AreEqual(0, _controller.OverlayCount);
        }

        [Test]
        public void InitializeRoomOverlays_CreatesCorrectNumberOfOverlaysWithSpriteRenderers()
        {
            _controller.InitializeRoomOverlays(_testRooms);

            Assert.AreEqual(3, _controller.OverlayCount);

            for (int i = 0; i < _testRooms.Count; i++)
            {
                Assert.IsFalse(_controller.IsRoomRevealed(i));

                GameObject overlay = _controller.GetRoomOverlayObject(i);
                Assert.IsNotNull(overlay, $"Overlay for room {i} should exist.");
                Assert.IsTrue(overlay.activeSelf, $"Overlay for room {i} should be active initially.");

                SpriteRenderer sr = overlay.GetComponent<SpriteRenderer>();
                Assert.IsNotNull(sr, $"Overlay for room {i} should have a SpriteRenderer.");
                Assert.AreEqual(Color.black, sr.color);

                // 验证基础 Sprite 为 1x1 世界单位，而非 4x4
                Assert.AreEqual(1f, sr.sprite.bounds.size.x, 0.001f);
                Assert.AreEqual(1f, sr.sprite.bounds.size.y, 0.001f);

                // 验证局部缩放严格对齐覆盖瓦片数量 [floor(Min), ceil(Max)]
                float expectedWidth = Mathf.CeilToInt(_testRooms[i].Max.x) - Mathf.FloorToInt(_testRooms[i].Min.x) + 1;
                float expectedHeight = Mathf.CeilToInt(_testRooms[i].Max.y) - Mathf.FloorToInt(_testRooms[i].Min.y) + 1;
                Assert.AreEqual(expectedWidth, overlay.transform.localScale.x, 0.001f);
                Assert.AreEqual(expectedHeight, overlay.transform.localScale.y, 0.001f);
            }
        }

        [Test]
        public void RevealRoom_InstantFade_MarksRevealedAndDeactivatesOverlay()
        {
            _controller.FadeDuration = 0f;
            _controller.InitializeRoomOverlays(_testRooms);

            _controller.RevealRoom(0, _testRooms[0]);

            Assert.IsTrue(_controller.IsRoomRevealed(0));
            GameObject overlay0 = _controller.GetRoomOverlayObject(0);
            Assert.IsNotNull(overlay0);
            Assert.IsFalse(overlay0.activeSelf, "Overlay should be deactivated after reveal.");

            // 其他房间仍未揭示且激活
            Assert.IsFalse(_controller.IsRoomRevealed(1));
            Assert.IsTrue(_controller.GetRoomOverlayObject(1).activeSelf);
        }

        [Test]
        public void RevealRoom_CalledTwice_IsIdempotent()
        {
            _controller.FadeDuration = 0f;
            _controller.InitializeRoomOverlays(_testRooms);

            _controller.RevealRoom(0, _testRooms[0]);
            Assert.IsTrue(_controller.IsRoomRevealed(0));

            // 第二次调用不应报错，状态保持已揭示
            Assert.DoesNotThrow(() => _controller.RevealRoom(0, _testRooms[0]));
            Assert.IsTrue(_controller.IsRoomRevealed(0));
        }

        [Test]
        public void ClearOverlays_DestroysAllOverlaysAndResetsCount()
        {
            _controller.InitializeRoomOverlays(_testRooms);
            Assert.AreEqual(3, _controller.OverlayCount);

            _controller.ClearOverlays();

            Assert.AreEqual(0, _controller.OverlayCount);
            Assert.IsNull(_controller.GetRoomOverlayObject(0));
        }
    }
}
