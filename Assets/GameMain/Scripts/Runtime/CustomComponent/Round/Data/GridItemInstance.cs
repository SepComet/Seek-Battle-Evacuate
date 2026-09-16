using System;
using SepCore.Definition;

namespace SepCore.Run
{
    /// <summary>
    /// 网格背包/仓库中的物品实例数据。
    /// 记录物品的网格锚点坐标、堆叠数量与旋转状态。
    /// </summary>
    public sealed class GridItemInstance
    {
        public int InstanceId { get; set; }
        public int ItemId { get; set; }
        public int Count { get; set; }
        public int AnchorX { get; set; }
        public int AnchorY { get; set; }
        public bool IsRotated { get; set; }

        public GridItemInstance()
        {
        }

        public GridItemInstance(int instanceId, int itemId, int count, int anchorX = 0, int anchorY = 0, bool isRotated = false)
        {
            InstanceId = instanceId;
            ItemId = itemId;
            Count = count;
            AnchorX = anchorX;
            AnchorY = anchorY;
            IsRotated = isRotated;
        }

        /// <summary>
        /// 获取物品当前在网格中占用的列数（已计入旋转）。
        /// </summary>
        public int GetWidth(Func<int, ItemConfig> getter)
        {
            if (getter == null)
            {
                return 1;
            }

            ItemConfig config = getter(ItemId);
            if (config == null)
            {
                return 1;
            }

            int w = Math.Max(1, config.Width);
            int h = Math.Max(1, config.Height);
            return IsRotated ? h : w;
        }

        /// <summary>
        /// 获取物品当前在网格中占用的行数（已计入旋转）。
        /// </summary>
        public int GetHeight(Func<int, ItemConfig> getter)
        {
            if (getter == null)
            {
                return 1;
            }

            ItemConfig config = getter(ItemId);
            if (config == null)
            {
                return 1;
            }

            int w = Math.Max(1, config.Width);
            int h = Math.Max(1, config.Height);
            return IsRotated ? w : h;
        }
    }
}
