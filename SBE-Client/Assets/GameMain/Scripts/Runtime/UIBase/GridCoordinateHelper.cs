using UnityEngine;

namespace SepCore.UI
{
    /// <summary>
    /// 网格背包像素物理坐标与网格坐标双向映射辅助类。
    /// 统一以左上角为原点 (Pivot = (0, 1)) 进行数学解算。
    /// </summary>
    public static class GridCoordinateHelper
    {
        /// <summary>
        /// 将网格单元格坐标 (x, y) 转换为以左上角为原点的 LocalPosition (Pivot = (0, 1))。
        /// </summary>
        public static Vector2 GetLocalPosition(int x, int y, int slotSize, int slotGap)
        {
            float posX = x * (slotSize + slotGap);
            float posY = -y * (slotSize + slotGap);
            return new Vector2(posX, posY);
        }

        /// <summary>
        /// 根据物品在网格中占用的列数与行数，解算其实际像素尺寸（包含中间的 Spacing）。
        /// </summary>
        public static Vector2 GetItemPixelSize(int gridWidth, int gridHeight, int slotSize, int slotGap)
        {
            int w = Mathf.Max(1, gridWidth);
            int h = Mathf.Max(1, gridHeight);
            float widthPx = w * slotSize + (w - 1) * slotGap;
            float heightPx = h * slotSize + (h - 1) * slotGap;
            return new Vector2(widthPx, heightPx);
        }

        /// <summary>
        /// 计算由 columns 列 * rows 行构成的整张网格容器的总像素尺寸。
        /// </summary>
        public static Vector2 GetTotalContainerSize(int columns, int rows, int slotSize, int slotGap)
        {
            int c = Mathf.Max(1, columns);
            int r = Mathf.Max(1, rows);
            float totalWidth = c * slotSize + (c - 1) * slotGap;
            float totalHeight = r * slotSize + (r - 1) * slotGap;
            return new Vector2(totalWidth, totalHeight);
        }

        /// <summary>
        /// 根据相对于容器左上角的本地坐标，逆向解算对应的网格坐标 (x, y)。
        /// </summary>
        public static Vector2Int LocalPositionToGrid(Vector2 localPos, int slotSize, int slotGap)
        {
            float step = slotSize + slotGap;
            if (step <= 0f)
            {
                return Vector2Int.zero;
            }

            int x = Mathf.FloorToInt(localPos.x / step);
            int y = Mathf.FloorToInt(-localPos.y / step);
            return new Vector2Int(x, y);
        }

        /// <summary>
        /// 根据相对于容器左上角的本地坐标，四舍五入逆向解算对齐的网格坐标 (x, y)，用于拖拽时的磁吸吸附。
        /// </summary>
        public static Vector2Int RoundLocalPositionToGrid(Vector2 localPos, int slotSize, int slotGap)
        {
            float step = slotSize + slotGap;
            if (step <= 0f)
            {
                return Vector2Int.zero;
            }

            int x = Mathf.RoundToInt(localPos.x / step);
            int y = Mathf.RoundToInt(-localPos.y / step);
            return new Vector2Int(x, y);
        }
    }
}
