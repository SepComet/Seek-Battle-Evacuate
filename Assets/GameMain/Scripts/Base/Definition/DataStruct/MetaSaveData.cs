using System;
using System.Collections.Generic;

namespace SepCore.Definition
{
    /// <summary>
    /// 全局元数据与单局历史存档数据（对应 meta_data.json）。
    /// </summary>
    [Serializable]
    public sealed class MetaDataSave
    {
        public const int CurrentVersion = 1;

        public int version = CurrentVersion;
        public long updatedAt;
        public List<RoundRecord> runHistory = new List<RoundRecord>();

        public string ToJson()
        {
            return GameFramework.Utility.Json.ToJson(this);
        }

        public static MetaDataSave FromJson(string json)
        {
            if (string.IsNullOrEmpty(json))
            {
                return null;
            }

            MetaDataSave meta = GameFramework.Utility.Json.ToObject<MetaDataSave>(json);
            if (meta != null && meta.runHistory == null)
            {
                meta.runHistory = new List<RoundRecord>();
            }

            return meta;
        }
    }
}
