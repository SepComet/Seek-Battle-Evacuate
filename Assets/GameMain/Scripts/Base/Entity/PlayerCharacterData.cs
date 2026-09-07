using System;
using UnityEngine;

namespace SepCore.Entity
{
    /// <summary>
    /// 玩家角色实体数据。
    /// 包含角色配置 ID 与队伍序号（1 为领队，其余为随从）。
    /// </summary>
    [Serializable]
    public sealed class PlayerCharacterData : EntityDataBase
    {
        [SerializeField] private int _characterId;
        [SerializeField] private int _partyOrder;

        public PlayerCharacterData(
            int entityId,
            string assetName,
            Vector3 position,
            int characterId,
            int partyOrder,
            Quaternion? rotation = null)
            : base(assetName, entityId)
        {
            Position = position;
            Rotation = rotation ?? Quaternion.identity;
            _characterId = characterId;
            _partyOrder = partyOrder;
        }

        /// <summary>
        /// 角色配置 ID（对应 CharacterConfig.Id）
        /// </summary>
        public int CharacterId => _characterId;

        /// <summary>
        /// 队伍序号（1 起，1 为领队）
        /// </summary>
        public int PartyOrder => _partyOrder;

        /// <summary>
        /// 是否为领队（队伍第 1 位，承担与其他实体交互的职责）
        /// </summary>
        public bool IsLeader => _partyOrder == 1;
    }
}
