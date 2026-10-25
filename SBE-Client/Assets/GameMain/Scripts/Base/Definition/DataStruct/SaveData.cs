using System.Collections.Generic;
using GameFramework;

namespace SepCore.Definition
{
    /// <summary>
    /// 游戏存档根数据。
    /// 对应 Docs/Tech/02_SaveData.md。
    /// </summary>
    [System.Serializable]
    public sealed class SaveData
    {
        /// <summary>
        /// 当前存档结构版本。
        /// </summary>
        public const int CurrentVersion = 1;

        /// <summary>
        /// 存档结构版本。
        /// </summary>
        public int version = CurrentVersion;

        /// <summary>
        /// 最后写入时间，Unix 毫秒。
        /// </summary>
        public long updatedAt;

        /// <summary>
        /// 主仓库内容（带网格坐标与旋转状态）。
        /// </summary>
        public List<GridItemStack> mainWarehouse;

        /// <summary>
        /// 拥有的角色，数组顺序即入队顺序。
        /// </summary>
        public List<CharacterSave> characters;

        /// <summary>
        /// 当前战备配置。
        /// </summary>
        public LoadoutSave loadout;

        /// <summary>
        /// 已结束单局的结算记录。
        /// </summary>
        public List<RoundRecord> runHistory;

        /// <summary>
        /// 序列化为 JSON 字符串。
        /// </summary>
        public string ToJson()
        {
            return GameFramework.Utility.Json.ToJson(this);
        }

        /// <summary>
        /// 从 JSON 字符串反序列化；json 为空或反序列化失败时返回 null。
        /// </summary>
        public static SaveData FromJson(string json)
        {
            if (string.IsNullOrEmpty(json))
            {
                return null;
            }

            SaveData saveData = GameFramework.Utility.Json.ToObject<SaveData>(json);
            if (saveData == null)
            {
                return null;
            }

            saveData.Normalize();
            return saveData;
        }

        /// <summary>
        /// 补齐缺失字段，避免反序列化后出现空引用。
        /// </summary>
        private void Normalize()
        {
            if (mainWarehouse == null)
            {
                mainWarehouse = new List<GridItemStack>();
            }

            if (characters == null)
            {
                characters = new List<CharacterSave>();
            }

            if (loadout == null)
            {
                loadout = new LoadoutSave();
            }

            loadout.Normalize();

            if (runHistory == null)
            {
                runHistory = new List<RoundRecord>();
            }
        }
    }
}