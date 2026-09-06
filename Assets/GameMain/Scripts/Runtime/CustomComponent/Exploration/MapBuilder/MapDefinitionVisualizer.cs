using System.Collections.Generic;
#if UNITY_EDITOR
using UnityEditor;
#endif
using UnityEngine;

namespace SepCore.Exploration
{
    /// <summary>
    /// MapDefinition 的 Scene 可视化调试组件（仅编辑器下绘制，运行时无任何行为）。
    /// 挂在场景空物体上并绑定 MapDefinition 资产，即可在 Scene 视图中直观查看
    /// 房间矩形与玩家出生点/物资点/敌人出生点/撤离点的分布，替代纯数字坐标填表。
    /// 绘制与运行时对齐：瓦片世界坐标 = 瓦片坐标 + 0.5f（见 MainMapBuildingState.TileToWorldPosition），
    /// 房间矩形覆盖瓦片闭区间 [Min, Max]，即世界空间 [Min, Max + 1)，与 RoomDefinition.Contains 判定一致。
    /// 序号与 Inspector 列表的 Element 序号一一对应，便于按编号回到 SO 上修改对应条目。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MapDefinitionVisualizer : MonoBehaviour
    {
        [SerializeField] private MapDefinition _mapDefinition;

        [SerializeField] private bool _alwaysShow = true;

        [SerializeField] private bool _showRooms = true;

        [SerializeField] private bool _showRoomGrid;

        [SerializeField] private bool _showLabels = true;

        [SerializeField] private bool _showPlayerSpawnPoints = true;

        [SerializeField] private bool _showResourcePoints = true;

        [SerializeField] private bool _showEnemySpawnPoints = true;

        [SerializeField] private bool _showExtractionPoints = true;

        private const float PointRadius = 0.35f;

        private const float PointDotRadius = 0.08f;

        private const float LabelOffsetY = 0.5f;

        private static readonly Color RoomNormalColor = new Color(1f, 1f, 1f, 0.9f);

        private static readonly Color RoomCorridorColor = new Color(1f, 0.8f, 0f, 0.9f);

        private static readonly Color RoomNormalFillColor = new Color(1f, 1f, 1f, 0.05f);

        private static readonly Color RoomCorridorFillColor = new Color(1f, 0.8f, 0f, 0.08f);

        private static readonly Color RoomGridColor = new Color(1f, 1f, 1f, 0.12f);

        private static readonly Color PlayerSpawnColor = new Color(0f, 1f, 1f, 1f);

        private static readonly Color ResourcePointColor = new Color(1f, 0.5f, 0f, 1f);

        private static readonly Color EnemySpawnColor = new Color(1f, 0.2f, 0.2f, 1f);

        private static readonly Color ExtractionColor = new Color(0.2f, 1f, 0.3f, 1f);

        private void OnDrawGizmos()
        {
            if (_alwaysShow)
            {
                DrawAll();
            }
        }

        private void OnDrawGizmosSelected()
        {
            // 未勾选常驻显示时，仅选中该物体才绘制，避免与 OnDrawGizmos 重复叠加
            if (!_alwaysShow)
            {
                DrawAll();
            }
        }

        private void DrawAll()
        {
            if (_mapDefinition == null)
            {
                return;
            }

            if (_showRooms)
            {
                DrawRooms();
            }

            if (_showPlayerSpawnPoints)
            {
                DrawPoints(_mapDefinition.PlayerSpawnPoints, PlayerSpawnColor, "Player Spawn");
            }

            if (_showResourcePoints)
            {
                DrawResourcePoints();
            }

            if (_showEnemySpawnPoints)
            {
                DrawPoints(_mapDefinition.EnemySpawnPoints, EnemySpawnColor, "Enemy Spawn");
            }

            if (_showExtractionPoints)
            {
                DrawPoints(_mapDefinition.ExtractionPoints, ExtractionColor, "Extraction");
            }
        }

        private void DrawRooms()
        {
            IReadOnlyList<RoomDefinition> rooms = _mapDefinition.Rooms;
            for (int i = 0; i < rooms.Count; i++)
            {
                RoomDefinition room = rooms[i];
                Vector2 min = room.Min;
                Vector2 max = room.Max;

                // 房间覆盖瓦片 [Min, Max]，世界空间矩形为 [Min, Max + 1)，与 Contains 的半开区间一致
                Vector3 center = new Vector3((min.x + max.x + 1f) * 0.5f, (min.y + max.y + 1f) * 0.5f, 0f);
                Vector3 size = new Vector3(max.x - min.x + 1f, max.y - min.y + 1f, 0f);

                Gizmos.color = room.IsCorridor ? RoomCorridorColor : RoomNormalColor;
                Gizmos.DrawWireCube(center, size);
                Gizmos.color = room.IsCorridor ? RoomCorridorFillColor : RoomNormalFillColor;
                Gizmos.DrawCube(center, size);

                if (_showRoomGrid)
                {
                    DrawRoomGrid(min, max);
                }

                DrawLabel(new Vector3(min.x, max.y + 1f + 0.2f, 0f), $"Room {i} - {room.RoomType}");
            }
        }

        private void DrawRoomGrid(Vector2 min, Vector2 max)
        {
            Gizmos.color = RoomGridColor;
            float left = min.x;
            float right = max.x + 1f;
            float bottom = min.y;
            float top = max.y + 1f;
            for (float x = left; x <= right; x += 1f)
            {
                Gizmos.DrawLine(new Vector3(x, bottom, 0f), new Vector3(x, top, 0f));
            }

            for (float y = bottom; y <= top; y += 1f)
            {
                Gizmos.DrawLine(new Vector3(left, y, 0f), new Vector3(right, y, 0f));
            }
        }

        private void DrawResourcePoints()
        {
            IReadOnlyList<ResourcePointDefinition> resourcePoints = _mapDefinition.ResourcePoints;
            Gizmos.color = ResourcePointColor;
            for (int i = 0; i < resourcePoints.Count; i++)
            {
                Vector3 world = TileCenter(resourcePoints[i].Position);
                Gizmos.DrawWireSphere(world, PointRadius);
                Gizmos.DrawSphere(world, PointDotRadius);
                DrawLabel(world + new Vector3(0f, LabelOffsetY, 0f),
                    $"Resource {i} (id={resourcePoints[i].ResourcePointId}) ({resourcePoints[i].Position.x}, {resourcePoints[i].Position.y})");
            }
        }

        private void DrawPoints(IReadOnlyList<Vector2> tilePositions, Color color, string labelPrefix)
        {
            Gizmos.color = color;
            for (int i = 0; i < tilePositions.Count; i++)
            {
                Vector3 world = TileCenter(tilePositions[i]);
                Gizmos.DrawWireSphere(world, PointRadius);
                Gizmos.DrawSphere(world, PointDotRadius);
                DrawLabel(world + new Vector3(0f, LabelOffsetY, 0f),
                    $"{labelPrefix} {i} ({tilePositions[i].x}, {tilePositions[i].y})");
            }
        }

        /// <summary>
        /// 瓦片坐标转世界坐标：原点在瓦片左下角，加 0.5 对齐到瓦片中心（与运行时 TileToWorldPosition 一致）。
        /// </summary>
        private static Vector3 TileCenter(Vector2 tilePos)
        {
            return new Vector3(tilePos.x + 0.5f, tilePos.y + 0.5f, 0f);
        }

        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        private void DrawLabel(Vector3 position, string text)
        {
            if (!_showLabels)
            {
                return;
            }

#if UNITY_EDITOR
            Handles.Label(position, text);
#endif
        }
    }
}
