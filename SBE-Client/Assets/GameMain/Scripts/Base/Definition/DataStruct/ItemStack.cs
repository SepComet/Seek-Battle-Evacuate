namespace SepCore.Definition
{
    /// <summary>
    /// 物品堆叠。
    /// </summary>
    [System.Serializable]
    public struct ItemStack
    {
        /// <summary>
        /// 物品 ID，对应 ItemConfig.Id。
        /// </summary>
        public int itemId;

        /// <summary>
        /// 数量，不超过配表 ItemConfig.StackLimit。
        /// </summary>
        public int count;

        public ItemStack(int itemId, int count)
        {
            this.itemId = itemId;
            this.count = count;
        }
    }
}