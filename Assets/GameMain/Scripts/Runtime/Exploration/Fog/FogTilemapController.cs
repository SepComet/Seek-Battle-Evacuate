using GameFramework.Event;
using UnityEngine;
using UnityEngine.Tilemaps;
using UnityGameFramework.Runtime;

namespace SepCore.Exploration
{
    /// <summary>
    /// 迷雾揭示控制器。
    /// 挂在 Main 场景 Grid 上，序列化引用 Tilemap Fog；
    /// 监听玩家进入房间事件，清除对应房间矩形内的迷雾瓦片。
    /// 每局重新进入 Main 场景时迷雾随场景绘制自动重置，无需手动恢复。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class FogTilemapController : MonoBehaviour
    {
        [SerializeField] private Tilemap _fogTilemap = null;

        private void OnEnable()
        {
            if (_fogTilemap == null)
            {
                Log.Error("FogTilemapController on '{0}' has no fog tilemap assigned.", gameObject.name);
                return;
            }

            GameEntry.Event.Subscribe(PlayerEnterRoomEventArgs.EventId, OnPlayerEnterRoom);
        }

        private void OnDisable()
        {
            if (this == null)
            {
                return;
            }

            GameEntry.Event.Unsubscribe(PlayerEnterRoomEventArgs.EventId, OnPlayerEnterRoom);
        }

        private void OnPlayerEnterRoom(object sender, GameEventArgs e)
        {
            PlayerEnterRoomEventArgs args = (PlayerEnterRoomEventArgs)e;
            RevealRoom(args.Room);
        }

        private void RevealRoom(RoomDefinition room)
        {
            // 房间 Min/Max 为瓦片坐标；两点围成的区域为闭区间，
            // 覆盖的瓦片范围即 [floor(Min), ceil(Max)]。
            for (int x = Mathf.FloorToInt(room.Min.x); x <= Mathf.CeilToInt(room.Max.x); x++)
            {
                for (int y = Mathf.FloorToInt(room.Min.y); y <= Mathf.CeilToInt(room.Max.y); y++)
                {
                    _fogTilemap.SetTile(new Vector3Int(x, y, 0), null);
                }
            }
        }
    }
}