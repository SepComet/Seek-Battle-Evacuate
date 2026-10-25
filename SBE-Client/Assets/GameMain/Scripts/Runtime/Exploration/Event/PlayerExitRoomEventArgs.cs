using GameFramework;
using GameFramework.Event;
using SepCore.Definition;

namespace SepCore.Exploration
{
    /// <summary>
    /// 玩家离开房间或走廊事件。
    /// </summary>
    public sealed class PlayerExitRoomEventArgs : GameEventArgs
    {
        public static readonly int EventId = typeof(PlayerExitRoomEventArgs).GetHashCode();

        public override int Id => EventId;

        /// <summary>
        /// 房间在地图定义中的索引。
        /// </summary>
        public int RoomIndex { get; private set; }

        /// <summary>
        /// 房间定义数据。
        /// </summary>
        public RoomDefinition Room { get; private set; }

        /// <summary>
        /// 房间类型（普通房间 / 走廊）。
        /// </summary>
        public RoomType RoomType => Room.RoomType;

        public PlayerExitRoomEventArgs()
        {
            RoomIndex = -1;
            Room = default;
        }

        public static PlayerExitRoomEventArgs Create(int roomIndex, RoomDefinition room)
        {
            PlayerExitRoomEventArgs args = ReferencePool.Acquire<PlayerExitRoomEventArgs>();
            args.RoomIndex = roomIndex;
            args.Room = room;
            return args;
        }

        public override void Clear()
        {
            RoomIndex = -1;
            Room = default;
        }
    }
}
