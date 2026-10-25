using GameFramework;
using GameFramework.Event;
using SepCore.Definition;

namespace SepCore.Exploration
{
    /// <summary>
    /// 玩家进入房间或走廊事件。
    /// </summary>
    public sealed class PlayerEnterRoomEventArgs : GameEventArgs
    {
        public static readonly int EventId = typeof(PlayerEnterRoomEventArgs).GetHashCode();

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

        public PlayerEnterRoomEventArgs()
        {
            RoomIndex = -1;
            Room = default;
        }

        public static PlayerEnterRoomEventArgs Create(int roomIndex, RoomDefinition room)
        {
            PlayerEnterRoomEventArgs args = ReferencePool.Acquire<PlayerEnterRoomEventArgs>();
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
