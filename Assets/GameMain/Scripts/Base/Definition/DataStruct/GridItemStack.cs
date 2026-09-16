namespace SepCore.Definition
{
    /// <summary>
    /// 网格物品持久化堆叠数据。
    /// 记录物品配置 ID、数量、网格锚点坐标与旋转状态。
    /// </summary>
    [System.Serializable]
    public struct GridItemStack
    {
        /// <summary>
        /// 物品 ID，对应 ItemConfig.Id。
        /// </summary>
        public int itemId;

        /// <summary>
        /// 数量，不超过配表 ItemConfig.StackLimit。
        /// </summary>
        public int count;

        /// <summary>
        /// 网格列坐标 (AnchorX，从左往右，0 开始)。
        /// </summary>
        public int x;

        /// <summary>
        /// 网格行坐标 (AnchorY，从上往下，0 开始)。
        /// </summary>
        public int y;

        /// <summary>
        /// 是否顺时针旋转 90 度。
        /// </summary>
        public bool isRotated;

        public GridItemStack(int itemId, int count, int x = 0, int y = 0, bool isRotated = false)
        {
            this.itemId = itemId;
            this.count = count;
            this.x = x;
            this.y = y;
            this.isRotated = isRotated;
        }
    }
}
