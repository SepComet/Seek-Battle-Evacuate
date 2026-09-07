using System.Collections.Generic;
using DG.Tweening;
using GameFramework.Event;
using UnityEngine;
using UnityEngine.Tilemaps;
using UnityGameFramework.Runtime;

namespace SepCore.Exploration
{
    /// <summary>
    /// 迷雾揭示控制器。
    /// 挂在 Main 场景 Grid 上，序列化引用 Tilemap Fog；
    /// 在地图构建阶段预先创建所有房间/走廊的纯黑遮罩 SpriteRenderer，
    /// 监听玩家进入房间事件，清除对应房间矩形内的迷雾瓦片，并驱动对应遮罩平滑淡出。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class FogTilemapController : MonoBehaviour
    {
        private sealed class RoomOverlayData
        {
            public int RoomIndex;
            public GameObject GameObject;
            public SpriteRenderer SpriteRenderer;
            public Tween FadeTween;
            public bool IsRevealed;
        }

        public static FogTilemapController Instance { get; private set; }

        [SerializeField] private Tilemap _fogTilemap = null;
        [SerializeField] private float _fadeDuration = 0.35f;
        [SerializeField] private Ease _fadeEase = Ease.OutQuad;

        private TilemapRenderer _fogTilemapRenderer;
        private Transform _overlayRoot;
        private static Sprite s_SharedFogSprite;
        private RoomOverlayData[] _roomOverlays;

        public float FadeDuration
        {
            get => _fadeDuration;
            set => _fadeDuration = value;
        }

        public Ease FadeEase
        {
            get => _fadeEase;
            set => _fadeEase = value;
        }

        public int OverlayCount => _roomOverlays != null ? _roomOverlays.Length : 0;

        public bool IsRoomRevealed(int roomIndex)
        {
            if (_roomOverlays == null || roomIndex < 0 || roomIndex >= _roomOverlays.Length)
            {
                return false;
            }

            RoomOverlayData overlay = _roomOverlays[roomIndex];
            return overlay != null && overlay.IsRevealed;
        }

        public GameObject GetRoomOverlayObject(int roomIndex)
        {
            if (_roomOverlays == null || roomIndex < 0 || roomIndex >= _roomOverlays.Length)
            {
                return null;
            }

            return _roomOverlays[roomIndex]?.GameObject;
        }

#if UNITY_EDITOR
        internal void SetFogTilemapForTest(Tilemap tilemap)
        {
            _fogTilemap = tilemap;
            _fogTilemapRenderer = tilemap != null ? tilemap.GetComponent<TilemapRenderer>() : null;
        }
#endif

        private void Awake()
        {
            Instance = this;
        }

        private void OnEnable()
        {
            if (_fogTilemap == null)
            {
                Log.Error("FogTilemapController on '{0}' has no fog tilemap assigned.", gameObject.name);
                return;
            }

            _fogTilemapRenderer = _fogTilemap.GetComponent<TilemapRenderer>();
            if (GameEntry.Event != null)
            {
                GameEntry.Event.Subscribe(PlayerEnterRoomEventArgs.EventId, OnPlayerEnterRoom);
            }
        }

        private void OnDisable()
        {
            if (this == null)
            {
                return;
            }

            if (GameEntry.Event != null)
            {
                GameEntry.Event.Unsubscribe(PlayerEnterRoomEventArgs.EventId, OnPlayerEnterRoom);
            }

            ClearOverlays();
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }

            ClearOverlays();
        }

        /// <summary>
        /// 在地图构建阶段预先创建所有房间/走廊的纯黑遮罩矩形（SpriteRenderer），
        /// 避免进入房间时临时动态创建。
        /// </summary>
        public void InitializeRoomOverlays(IReadOnlyList<RoomDefinition> rooms)
        {
            ClearOverlays();

            if (_fogTilemap == null)
            {
                Log.Error("FogTilemapController has no fog tilemap assigned, cannot initialize room overlays.");
                return;
            }

            if (rooms == null || rooms.Count == 0)
            {
                return;
            }

            if (_fogTilemapRenderer == null)
            {
                _fogTilemapRenderer = _fogTilemap.GetComponent<TilemapRenderer>();
            }

            Material sharedMaterial = _fogTilemapRenderer != null ? _fogTilemapRenderer.sharedMaterial : null;
            int sortingLayerId = _fogTilemapRenderer != null ? _fogTilemapRenderer.sortingLayerID : SortingLayer.NameToID("Fog");
            int sortingOrder = (_fogTilemapRenderer != null ? _fogTilemapRenderer.sortingOrder : 0) + 1;

            GameObject rootGo = new GameObject("FogRoomOverlays");
            _overlayRoot = rootGo.transform;
            _overlayRoot.SetParent(_fogTilemap.transform, false);
            _overlayRoot.localPosition = Vector3.zero;
            _overlayRoot.localRotation = Quaternion.identity;
            _overlayRoot.localScale = Vector3.one;

            Sprite sharedSprite = GetOrCreateSharedFogSprite();
            _roomOverlays = new RoomOverlayData[rooms.Count];

            for (int i = 0; i < rooms.Count; i++)
            {
                RoomDefinition room = rooms[i];
                int minX = Mathf.FloorToInt(room.Min.x);
                int maxX = Mathf.CeilToInt(room.Max.x);
                int minY = Mathf.FloorToInt(room.Min.y);
                int maxY = Mathf.CeilToInt(room.Max.y);

                Vector3 minWorld = _fogTilemap.CellToWorld(new Vector3Int(minX, minY, 0));
                Vector3 maxWorld = _fogTilemap.CellToWorld(new Vector3Int(maxX + 1, maxY + 1, 0));

                Vector3 center = (minWorld + maxWorld) * 0.5f;
                center.z = _fogTilemap.transform.position.z;
                Vector3 size = new Vector3(Mathf.Abs(maxWorld.x - minWorld.x), Mathf.Abs(maxWorld.y - minWorld.y), 1f);

                GameObject overlayGo = new GameObject($"RoomOverlay_{i}_{room.RoomType}");
                overlayGo.transform.SetParent(_overlayRoot, false);
                overlayGo.transform.position = center;
                overlayGo.transform.localScale = size;

                SpriteRenderer sr = overlayGo.AddComponent<SpriteRenderer>();
                sr.sprite = sharedSprite;
                sr.color = Color.black;
                if (sharedMaterial != null)
                {
                    sr.sharedMaterial = sharedMaterial;
                }
                sr.sortingLayerID = sortingLayerId;
                sr.sortingOrder = sortingOrder;

                _roomOverlays[i] = new RoomOverlayData
                {
                    RoomIndex = i,
                    GameObject = overlayGo,
                    SpriteRenderer = sr,
                    FadeTween = null,
                    IsRevealed = false
                };
            }

            Log.Info("[FogTilemapController] Initialized {0} room fog overlays for smooth fade-out.", rooms.Count);
        }

        private void OnPlayerEnterRoom(object sender, GameEventArgs e)
        {
            PlayerEnterRoomEventArgs args = (PlayerEnterRoomEventArgs)e;
            RevealRoom(args.RoomIndex, args.Room);
        }

        /// <summary>
        /// 揭示指定房间的迷雾。
        /// 若存在预构建的遮罩，则立即清除 Tilemap 瓦片并驱动遮罩淡出；
        /// 否则直接清除对应瓦片作为兜底。
        /// </summary>
        public void RevealRoom(int roomIndex, RoomDefinition room)
        {
            if (_fogTilemap == null)
            {
                return;
            }

            if (_roomOverlays != null && roomIndex >= 0 && roomIndex < _roomOverlays.Length)
            {
                RoomOverlayData overlay = _roomOverlays[roomIndex];
                if (overlay == null || overlay.IsRevealed)
                {
                    return;
                }

                overlay.IsRevealed = true;

                // 1. 立即清除 Tilemap 瓦片（此时该区域正由纯黑 SpriteRenderer 遮盖，不会发生闪现）
                ClearRoomTiles(room);

                // 2. 纯黑 SpriteRenderer 平滑淡出
                if (overlay.SpriteRenderer != null)
                {
                    if (_fadeDuration <= 0f)
                    {
                        Color c = overlay.SpriteRenderer.color;
                        c.a = 0f;
                        overlay.SpriteRenderer.color = c;
                        if (overlay.GameObject != null)
                        {
                            overlay.GameObject.SetActive(false);
                        }
                    }
                    else
                    {
                        overlay.FadeTween?.Kill();
                        overlay.FadeTween = overlay.SpriteRenderer.DOFade(0f, _fadeDuration)
                            .SetEase(_fadeEase)
                            .OnComplete(() =>
                            {
                                if (overlay.GameObject != null)
                                {
                                    overlay.GameObject.SetActive(false);
                                }
                            });
                    }
                }
            }
            else
            {
                // 兜底：若无对应 overlay 或索引越界，直接清除瓦片
                ClearRoomTiles(room);
            }
        }

        /// <summary>
        /// 清理全部预创建的房间遮罩与补间动画。
        /// </summary>
        public void ClearOverlays()
        {
            if (_roomOverlays != null)
            {
                for (int i = 0; i < _roomOverlays.Length; i++)
                {
                    RoomOverlayData overlay = _roomOverlays[i];
                    if (overlay != null)
                    {
                        overlay.FadeTween?.Kill();
                        overlay.FadeTween = null;
                        if (overlay.GameObject != null)
                        {
                            DestroyObject(overlay.GameObject);
                            overlay.GameObject = null;
                        }
                    }
                }

                _roomOverlays = null;
            }

            if (_overlayRoot != null)
            {
                DestroyObject(_overlayRoot.gameObject);
                _overlayRoot = null;
            }
        }

        private void ClearRoomTiles(RoomDefinition room)
        {
            int minX = Mathf.FloorToInt(room.Min.x);
            int maxX = Mathf.CeilToInt(room.Max.x);
            int minY = Mathf.FloorToInt(room.Min.y);
            int maxY = Mathf.CeilToInt(room.Max.y);

            for (int x = minX; x <= maxX; x++)
            {
                for (int y = minY; y <= maxY; y++)
                {
                    Vector3Int pos = new Vector3Int(x, y, 0);
                    if (_fogTilemap.HasTile(pos))
                    {
                        _fogTilemap.SetTile(pos, null);
                    }
                }
            }
        }

        private static Sprite GetOrCreateSharedFogSprite()
        {
            Texture2D texture = Texture2D.whiteTexture;
            // Unity 内置 Texture2D.whiteTexture 的分辨率为 4x4 像素。
            // 若 pixelsPerUnit 设为 1，则 Sprite 原生世界尺寸为 (4/1)=4 单位，导致按 size 缩放后整体扩大了 4 倍。
            // 将 pixelsPerUnit 设为 texture.width（即 4f），Sprite 原生尺寸即为精确的 1x1 世界单位。
            if (s_SharedFogSprite == null || Mathf.Abs(s_SharedFogSprite.pixelsPerUnit - texture.width) > 0.01f)
            {
                s_SharedFogSprite = Sprite.Create(
                    texture,
                    new Rect(0, 0, texture.width, texture.height),
                    new Vector2(0.5f, 0.5f),
                    texture.width);
                s_SharedFogSprite.name = "FogOverlayWhiteSprite";
            }

            return s_SharedFogSprite;
        }

        private static void DestroyObject(UnityEngine.Object obj)
        {
            if (obj == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Destroy(obj);
            }
            else
            {
                DestroyImmediate(obj);
            }
        }
    }
}
