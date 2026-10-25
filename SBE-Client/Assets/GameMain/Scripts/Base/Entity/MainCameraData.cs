using System;
using UnityEngine;

namespace SepCore.Entity
{
    /// <summary>
    /// 主摄像机实体数据。
    /// 包含主摄像机实体的编号、资产名与初始空间变换。
    /// </summary>
    [Serializable]
    public sealed class MainCameraData : EntityDataBase
    {
        public MainCameraData(
            int entityId,
            string assetName,
            Vector3 position,
            Quaternion? rotation = null)
            : base(assetName, entityId)
        {
            Position = position;
            Rotation = rotation ?? Quaternion.identity;
        }
    }
}
