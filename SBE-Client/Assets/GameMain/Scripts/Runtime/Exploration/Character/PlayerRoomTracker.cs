using System;
using System.Collections.Generic;
using UnityEngine;
using UnityGameFramework.Runtime;

namespace SepCore.Exploration
{
    /// <summary>
    /// 玩家所在房间/区域追踪器。
    /// 负责采样玩家坐标、判定当前所属房间（含普通房间与走廊），
    /// 并结合当前房间局部性缓存与静止状态优化，在房间切换时广播 GameFramework 事件。
    /// </summary>
    public sealed class PlayerRoomTracker
    {
        private IReadOnlyList<RoomDefinition> _rooms;
        private int? _currentRoomIndex;

        /// <summary>
        /// 当进入房间或走廊时触发的回调事件（参数：房间索引，房间定义）。
        /// </summary>
        public event Action<int, RoomDefinition> OnRoomEntered;

        /// <summary>
        /// 当离开房间或走廊时触发的回调事件（参数：房间索引，房间定义）。
        /// </summary>
        public event Action<int, RoomDefinition> OnRoomExited;

        /// <summary>
        /// 当前关联的全部房间定义集合。
        /// </summary>
        public IReadOnlyList<RoomDefinition> Rooms => _rooms;

        /// <summary>
        /// 当前玩家所在的房间索引。若不在任何已配置房间内，则为 null。
        /// </summary>
        public int? CurrentRoomIndex => _currentRoomIndex;

        /// <summary>
        /// 当前玩家所在的房间定义。若不在任何房间内，则为 null。
        /// </summary>
        public RoomDefinition? CurrentRoom =>
            _currentRoomIndex.HasValue && _rooms != null && _currentRoomIndex.Value >= 0 && _currentRoomIndex.Value < _rooms.Count
                ? _rooms[_currentRoomIndex.Value]
                : (RoomDefinition?)null;

        /// <summary>
        /// 初始化房间追踪器并根据初始坐标确定起始房间。
        /// </summary>
        /// <param name="rooms">地图配置的全部房间列表。</param>
        /// <param name="initialPosition">玩家初始世界坐标。</param>
        public void Initialize(IReadOnlyList<RoomDefinition> rooms, Vector2 initialPosition)
        {
            _rooms = rooms ?? Array.Empty<RoomDefinition>();
            _currentRoomIndex = null;
            UpdatePosition(initialPosition, isMoving: true, force: true);
        }

        /// <summary>
        /// 根据最新坐标更新玩家所在房间。
        /// </summary>
        /// <param name="position">玩家当前坐标。</param>
        /// <param name="isMoving">当前是否处于移动状态。若为 false 且 force 为 false，则跳过计算。</param>
        /// <param name="force">是否强制检测（例如瞬移、战后复位或开局初始化）。</param>
        /// <returns>若发生房间/走廊切换则返回 true，否则返回 false。</returns>
        public bool UpdatePosition(Vector2 position, bool isMoving = true, bool force = false)
        {
            if (!isMoving && !force)
            {
                return false;
            }

            if (_rooms == null || _rooms.Count == 0)
            {
                return false;
            }

            // 1. 局部性缓存检查：若依然在当前房间内部，直接快速返回，防止重叠边界抖动
            if (_currentRoomIndex.HasValue)
            {
                int currentIndex = _currentRoomIndex.Value;
                if (currentIndex >= 0 && currentIndex < _rooms.Count && _rooms[currentIndex].Contains(position))
                {
                    return false;
                }
            }

            // 2. 走出当前房间，检索最新所属房间
            int? newRoomIndex = null;
            for (int i = 0; i < _rooms.Count; i++)
            {
                if (_rooms[i].Contains(position))
                {
                    newRoomIndex = i;
                    break;
                }
            }

            // 3. 判断是否发生房间变更
            if (newRoomIndex == _currentRoomIndex)
            {
                return false;
            }

            int? previousRoomIndex = _currentRoomIndex;
            _currentRoomIndex = newRoomIndex;

            RoomDefinition? previousRoom = previousRoomIndex.HasValue && previousRoomIndex.Value >= 0 && previousRoomIndex.Value < _rooms.Count
                ? _rooms[previousRoomIndex.Value]
                : (RoomDefinition?)null;

            RoomDefinition? newRoom = newRoomIndex.HasValue && newRoomIndex.Value >= 0 && newRoomIndex.Value < _rooms.Count
                ? _rooms[newRoomIndex.Value]
                : (RoomDefinition?)null;

            // 4. 打印房间切换日志（便于在编辑器和实机中核对边界与坐标）
            if (previousRoom.HasValue && newRoom.HasValue)
            {
                Log.Info("[Exploration] Player switched room: Room #{0} ({1}) -> Room #{2} ({3}, Min:{4}, Max:{5}) at position ({6:F2}, {7:F2}).",
                    previousRoomIndex.Value, previousRoom.Value.RoomType, newRoomIndex.Value, newRoom.Value.RoomType,
                    newRoom.Value.Min, newRoom.Value.Max, position.x, position.y);
            }
            else if (newRoom.HasValue)
            {
                Log.Info("[Exploration] Player entered room: Room #{0} ({1}, Min:{2}, Max:{3}) at position ({4:F2}, {5:F2}).",
                    newRoomIndex.Value, newRoom.Value.RoomType, newRoom.Value.Min, newRoom.Value.Max, position.x, position.y);
            }
            else if (previousRoom.HasValue)
            {
                Log.Info("[Exploration] Player exited Room #{0} ({1}) to unmapped area at position ({2:F2}, {3:F2}).",
                    previousRoomIndex.Value, previousRoom.Value.RoomType, position.x, position.y);
            }

            // 5. 派发离开旧房间事件
            if (previousRoom.HasValue)
            {
                if (GameEntry.Event != null)
                {
                    GameEntry.Event.Fire(this, PlayerExitRoomEventArgs.Create(previousRoomIndex.Value, previousRoom.Value));
                }

                OnRoomExited?.Invoke(previousRoomIndex.Value, previousRoom.Value);
            }

            // 6. 派发进入新房间事件
            if (newRoom.HasValue)
            {
                if (GameEntry.Event != null)
                {
                    GameEntry.Event.Fire(this, PlayerEnterRoomEventArgs.Create(newRoomIndex.Value, newRoom.Value));
                }

                OnRoomEntered?.Invoke(newRoomIndex.Value, newRoom.Value);
            }

            return true;
        }

        /// <summary>
        /// 重置房间追踪器状态。
        /// </summary>
        public void Reset()
        {
            _rooms = null;
            _currentRoomIndex = null;
        }
    }
}
