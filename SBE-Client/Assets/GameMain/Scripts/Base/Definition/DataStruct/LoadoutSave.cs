using System.Collections.Generic;

namespace SepCore.Definition
{
    /// <summary>
    /// 战备配置。
    /// </summary>
    [System.Serializable]
    public sealed class LoadoutSave
    {
        /// <summary>
        /// 出战角色 ID 及顺序，1~4 人，顺序决定同速时的行动次序。
        /// </summary>
        public int[] partyCharacterIds;

        /// <summary>
        /// 携带进入地图的物品，开局时填充共享背包；首版无局内效果。
        /// </summary>
        public List<ItemStack> carriedItems;

        /// <summary>
        /// 本局难度，对应 DifficultyConfig 主键。
        /// </summary>
        public DifficultyTier difficultyId;

        /// <summary>
        /// 补齐缺失字段，避免反序列化后出现空引用。
        /// </summary>
        public void Normalize()
        {
            if (partyCharacterIds == null)
            {
                partyCharacterIds = System.Array.Empty<int>();
            }

            if (carriedItems == null)
            {
                carriedItems = new List<ItemStack>();
            }
        }
    }
}