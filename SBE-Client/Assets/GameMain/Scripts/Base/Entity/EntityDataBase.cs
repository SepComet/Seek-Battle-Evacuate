//------------------------------------------------------------
// Game Framework
// Copyright © 2013-2021 Jiang Yin. All rights reserved.
// Homepage: https://gameframework.cn/
// Feedback: mailto:ellan@gameframework.cn
//------------------------------------------------------------

using System;
using UnityEngine;

namespace SepCore.Entity
{
    [Serializable]
    public abstract class EntityDataBase
    {
        [SerializeField] private string m_AssetName = string.Empty; 
        
        [SerializeField] private int m_Id = 0;

        [SerializeField] private Vector3 m_Position = Vector3.zero;

        [SerializeField] private Quaternion m_Rotation = Quaternion.identity;

        public EntityDataBase(string assetName, int entityId)
        {
            m_AssetName = assetName;
            m_Id = entityId;
        }

        /// <summary>
        /// 实体名称
        /// </summary>
        public string AssetName => m_AssetName;
        
        /// <summary>
        /// 实体编号。
        /// </summary>
        public int Id => m_Id;

        /// <summary>
        /// 实体位置。
        /// </summary>
        public Vector3 Position
        {
            get => m_Position;
            set => m_Position = value;
        }

        /// <summary>
        /// 实体朝向。
        /// </summary>
        public Quaternion Rotation
        {
            get => m_Rotation;
            set => m_Rotation = value;
        }
    }
}
