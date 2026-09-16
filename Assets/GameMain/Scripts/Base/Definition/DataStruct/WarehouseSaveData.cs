using System;
using System.Collections.Generic;

namespace SepCore.Definition
{
    /// <summary>
    /// 仓库物资单个物品数据。
    /// 仅记录资产本身，不包含空间摆放坐标。
    /// </summary>
    [Serializable]
    public sealed class WarehouseItemData
    {
        /// <summary>
        /// 物品在仓库中的唯一实例 ID。
        /// </summary>
        public int instanceId;

        /// <summary>
        /// 物品配置 ID，对应 ItemConfig.Id。
        /// </summary>
        public int itemId;

        /// <summary>
        /// 堆叠数量。
        /// </summary>
        public int count;

        public WarehouseItemData()
        {
        }

        public WarehouseItemData(int instanceId, int itemId, int count)
        {
            this.instanceId = instanceId;
            this.itemId = itemId;
            this.count = count;
        }
    }

    /// <summary>
    /// 仓库物资数据根存档（对应 warehouse_data.json）。
    /// </summary>
    [Serializable]
    public sealed class WarehouseDataSave
    {
        public List<WarehouseItemData> items = new List<WarehouseItemData>();

        public string ToJson()
        {
            return GameFramework.Utility.Json.ToJson(this);
        }

        public static WarehouseDataSave FromJson(string json)
        {
            if (string.IsNullOrEmpty(json))
            {
                return null;
            }

            WarehouseDataSave data = GameFramework.Utility.Json.ToObject<WarehouseDataSave>(json);
            if (data != null && data.items == null)
            {
                data.items = new List<WarehouseItemData>();
            }

            return data;
        }
    }

    /// <summary>
    /// 仓库单个网格摆放位置信息。
    /// </summary>
    [Serializable]
    public sealed class WarehouseSlotPlacement
    {
        /// <summary>
        /// 对应 WarehouseItemData.instanceId。
        /// </summary>
        public int instanceId;

        /// <summary>
        /// 网格列锚点坐标 AnchorX（从左往右，0 开始）。
        /// </summary>
        public int x;

        /// <summary>
        /// 网格行锚点坐标 AnchorY（从上往下，0 开始）。
        /// </summary>
        public int y;

        /// <summary>
        /// 是否顺时针旋转 90 度。
        /// </summary>
        public bool isRotated;

        public WarehouseSlotPlacement()
        {
        }

        public WarehouseSlotPlacement(int instanceId, int x, int y, bool isRotated)
        {
            this.instanceId = instanceId;
            this.x = x;
            this.y = y;
            this.isRotated = isRotated;
        }
    }

    /// <summary>
    /// 仓库网格布局存档（对应 warehouse_layout.json）。
    /// </summary>
    [Serializable]
    public sealed class WarehouseLayoutSave
    {
        public List<WarehouseSlotPlacement> placements = new List<WarehouseSlotPlacement>();

        public string ToJson()
        {
            return GameFramework.Utility.Json.ToJson(this);
        }

        public static WarehouseLayoutSave FromJson(string json)
        {
            if (string.IsNullOrEmpty(json))
            {
                return null;
            }

            WarehouseLayoutSave layout = GameFramework.Utility.Json.ToObject<WarehouseLayoutSave>(json);
            if (layout != null && layout.placements == null)
            {
                layout.placements = new List<WarehouseSlotPlacement>();
            }

            return layout;
        }
    }
}
