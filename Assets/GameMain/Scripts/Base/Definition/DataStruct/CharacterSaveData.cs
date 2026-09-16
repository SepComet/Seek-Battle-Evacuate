using System;
using System.Collections.Generic;

namespace SepCore.Definition
{
    /// <summary>
    /// 角色与战备独立存档数据（对应 character_data.json）。
    /// </summary>
    [Serializable]
    public sealed class CharacterDataSave
    {
        public List<CharacterSave> characters = new List<CharacterSave>();
        public LoadoutSave loadout = new LoadoutSave();

        public string ToJson()
        {
            return GameFramework.Utility.Json.ToJson(this);
        }

        public static CharacterDataSave FromJson(string json)
        {
            if (string.IsNullOrEmpty(json))
            {
                return null;
            }

            CharacterDataSave data = GameFramework.Utility.Json.ToObject<CharacterDataSave>(json);
            if (data != null)
            {
                if (data.characters == null)
                {
                    data.characters = new List<CharacterSave>();
                }

                if (data.loadout == null)
                {
                    data.loadout = new LoadoutSave();
                }

                data.loadout.Normalize();
            }

            return data;
        }
    }
}
