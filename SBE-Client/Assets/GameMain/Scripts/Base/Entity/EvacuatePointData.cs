using System;
using UnityEngine;

namespace SepCore.Entity
{
    /// <summary>
    /// 撤离点实体数据。
    /// 包含撤离点开放状态与预制体资产名。
    /// </summary>
    [Serializable]
    public sealed class EvacuatePointData : EntityDataBase
    {
        [SerializeField] private bool _isOpen;

        public EvacuatePointData(
            int entityId,
            string assetName,
            Vector3 position,
            bool isOpen = true,
            Quaternion? rotation = null)
            : base(assetName, entityId)
        {
            Position = position;
            Rotation = rotation ?? Quaternion.identity;
            _isOpen = isOpen;
        }

        /// <summary>
        /// 撤离点当前是否开放。
        /// </summary>
        public bool IsOpen
        {
            get => _isOpen;
            set => _isOpen = value;
        }
    }
}
