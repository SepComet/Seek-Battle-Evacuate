using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace SepCore.Exploration
{
    /// <summary>
    /// MapDefinition 资产到服务器固定布局 JSON（DataTables/tbmapdefinition.json）的纯函数序列化器。
    /// 数据契约见 .scratch/exploration-online-v1/notes/contracts.md §8：
    /// 房间写归一化 min/max（不写 pointA/pointB，避免次序歧义），坐标取整数时按整数输出；
    /// 导出前校验硬性不变量——敌人点必须落在某个房间内（判定语义与 RoomDefinition.Contains 一致），
    /// 且四类点位候选列表与房间列表非空；违反即抛异常，不产出半成品数据。
    /// </summary>
    public static class MapDefinitionJson
    {
        /// <summary>
        /// 序列化为服务器布局 JSON。校验失败抛 InvalidOperationException（调用方不得写文件）。
        /// </summary>
        public static string Serialize(MapDefinition mapDefinition)
        {
            if (mapDefinition == null)
            {
                throw new ArgumentNullException(nameof(mapDefinition));
            }

            Validate(mapDefinition);

            StringBuilder builder = new StringBuilder(2048);
            builder.Append("{\n");

            AppendRooms(builder, mapDefinition.Rooms);
            builder.Append(",\n");

            AppendHeader(builder, "resourcePoints");
            IReadOnlyList<ResourcePointDefinition> resourcePoints = mapDefinition.ResourcePoints;
            for (int i = 0; i < resourcePoints.Count; i++)
            {
                ResourcePointDefinition point = resourcePoints[i];
                builder.Append("    { \"x\": ").Append(FormatCoordinate(point.Position.x))
                    .Append(", \"y\": ").Append(FormatCoordinate(point.Position.y))
                    .Append(", \"resourcePointId\": ").Append(point.ResourcePointId.ToString(CultureInfo.InvariantCulture))
                    .Append(" }");
                AppendItemSeparator(builder, i, resourcePoints.Count);
            }

            builder.Append("],\n");

            AppendSpawnPoints(builder, "enemySpawnPoints", mapDefinition.EnemySpawnPoints);
            builder.Append(",\n");
            AppendSpawnPoints(builder, "playerSpawnPoints", mapDefinition.PlayerSpawnPoints);
            builder.Append(",\n");
            AppendSpawnPoints(builder, "extractionPoints", mapDefinition.ExtractionPoints);

            builder.Append("}\n");
            return builder.ToString();
        }

        /// <summary>
        /// 校验导出所需的硬性不变量，违反抛 InvalidOperationException。
        /// </summary>
        public static void Validate(MapDefinition mapDefinition)
        {
            if (mapDefinition == null)
            {
                throw new ArgumentNullException(nameof(mapDefinition));
            }

            if (mapDefinition.Rooms.Count == 0)
            {
                throw new InvalidOperationException("Map definition does not contain a room.");
            }

            if (mapDefinition.ResourcePoints.Count == 0)
            {
                throw new InvalidOperationException("Map definition does not contain a resource point.");
            }

            if (mapDefinition.EnemySpawnPoints.Count == 0)
            {
                throw new InvalidOperationException("Map definition does not contain an enemy spawn point.");
            }

            if (mapDefinition.PlayerSpawnPoints.Count == 0)
            {
                throw new InvalidOperationException("Map definition does not contain a player spawn point.");
            }

            if (mapDefinition.ExtractionPoints.Count == 0)
            {
                throw new InvalidOperationException("Map definition does not contain an extraction point.");
            }

            IReadOnlyList<Vector2> enemySpawnPoints = mapDefinition.EnemySpawnPoints;
            for (int i = 0; i < enemySpawnPoints.Count; i++)
            {
                if (RoomIndexAt(mapDefinition.Rooms, enemySpawnPoints[i]) < 0)
                {
                    Vector2 position = enemySpawnPoints[i];
                    throw new InvalidOperationException(
                        $"Enemy spawn point {i} ({FormatCoordinate(position.x)}, {FormatCoordinate(position.y)}) " +
                        "does not belong to any room.");
                }
            }
        }

        /// <summary>
        /// 返回坐标所在房间的下标（列表序），不在任何房间内返回 -1。
        /// 判定与 RoomDefinition.Contains 一致：房间覆盖瓦片闭区间 [Min, Max]，瓦片占 [x, x+1)。
        /// </summary>
        public static int RoomIndexAt(IReadOnlyList<RoomDefinition> rooms, Vector2 point)
        {
            for (int i = 0; i < rooms.Count; i++)
            {
                if (rooms[i].Contains(point))
                {
                    return i;
                }
            }

            return -1;
        }

        private static void AppendRooms(StringBuilder builder, IReadOnlyList<RoomDefinition> rooms)
        {
            AppendHeader(builder, "rooms");
            for (int i = 0; i < rooms.Count; i++)
            {
                RoomDefinition room = rooms[i];
                Vector2 min = room.Min;
                Vector2 max = room.Max;
                builder.Append("    { \"type\": ").Append(((int)room.RoomType).ToString(CultureInfo.InvariantCulture))
                    .Append(", \"minX\": ").Append(FormatCoordinate(min.x))
                    .Append(", \"minY\": ").Append(FormatCoordinate(min.y))
                    .Append(", \"maxX\": ").Append(FormatCoordinate(max.x))
                    .Append(", \"maxY\": ").Append(FormatCoordinate(max.y))
                    .Append(" }");
                AppendItemSeparator(builder, i, rooms.Count);
            }

            builder.Append("]");
        }

        private static void AppendSpawnPoints(StringBuilder builder, string name, IReadOnlyList<Vector2> points)
        {
            AppendHeader(builder, name);
            for (int i = 0; i < points.Count; i++)
            {
                builder.Append("    { \"x\": ").Append(FormatCoordinate(points[i].x))
                    .Append(", \"y\": ").Append(FormatCoordinate(points[i].y))
                    .Append(" }");
                AppendItemSeparator(builder, i, points.Count);
            }

            builder.Append("]");
        }

        private static void AppendHeader(StringBuilder builder, string name)
        {
            builder.Append("  \"").Append(name).Append("\": [\n");
        }

        private static void AppendItemSeparator(StringBuilder builder, int index, int count)
        {
            builder.Append(index + 1 < count ? ",\n" : "\n");
        }

        /// <summary>
        /// 坐标文本：整数输出为整数（当前资产全是整数），非整数保留最多三位小数，避免浮点格式差异。
        /// </summary>
        private static string FormatCoordinate(float value)
        {
            return value.ToString("0.###", CultureInfo.InvariantCulture);
        }
    }
}
