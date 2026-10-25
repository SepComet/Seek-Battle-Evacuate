using System;
using System.Collections.Generic;
using SepCore.Battle;
using SepCore.Definition;
using UnityGameFramework.Runtime;

namespace SepCore.Exploration
{
    /// <summary>
    /// 战备角色队伍构建器。
    /// 根据存档中的战备出战角色 ID 列表与已穿戴装备（武器/防具），结合配表属性生成单局出战的 PlayerUnitState 列表。
    /// </summary>
    public static class PlayerPartyBuilder
    {
        /// <summary>
        /// 使用 Luban Tables 构建单局出战角色状态列表。
        /// </summary>
        public static List<PlayerUnitState> Build(SaveData save, Tables tables)
        {
            if (tables == null)
            {
                throw new ArgumentNullException(nameof(tables));
            }

            return Build(
                save,
                id => tables.TbCharacterConfig.GetOrDefault(id),
                id => tables.TbItemConfig.GetOrDefault(id),
                tables.TbGlobalConfig?.Data?.NewGameCharacterIds
            );
        }

        /// <summary>
        /// 使用函数提供者构建出战角色状态列表（便于纯逻辑单元测试）。
        /// </summary>
        public static List<PlayerUnitState> Build(
            SaveData save,
            Func<int, CharacterConfig> characterGetter,
            Func<int, ItemConfig> itemGetter,
            IReadOnlyList<int> defaultCharacterIds = null)
        {
            if (characterGetter == null)
            {
                throw new ArgumentNullException(nameof(characterGetter));
            }

            List<PlayerUnitState> partyPlayers = new List<PlayerUnitState>();
            if (save == null)
            {
                return partyPlayers;
            }

            IReadOnlyList<int> partyCharacterIds = save.loadout?.partyCharacterIds;
            if (partyCharacterIds == null || partyCharacterIds.Count == 0)
            {
                // 若战备未指定出战角色，降级读取存档拥有的角色或全局默认角色
                if (save.characters != null && save.characters.Count > 0)
                {
                    List<int> fallbackIds = new List<int>();
                    foreach (CharacterSave c in save.characters)
                    {
                        fallbackIds.Add(c.characterId);
                    }
                    partyCharacterIds = fallbackIds;
                }
                else
                {
                    partyCharacterIds = defaultCharacterIds;
                }
            }

            if (partyCharacterIds == null)
            {
                return partyPlayers;
            }

            int order = 1;
            foreach (int charId in partyCharacterIds)
            {
                CharacterConfig charConfig = characterGetter(charId);
                if (charConfig == null)
                {
                    Log.Error("PlayerPartyBuilder: CharacterConfig '{0}' not found.", charId);
                    continue;
                }

                int maxHp = charConfig.MaxHp;
                int maxMp = charConfig.MaxMp;
                int atk = charConfig.Atk;
                int mat = charConfig.Mat;
                int speed = charConfig.Speed;

                if (itemGetter != null && TryFindCharacterSave(save.characters, charId, out CharacterSave charSave))
                {
                    ApplyItemBonus(charSave.weaponItemId, itemGetter, ref maxHp, ref maxMp, ref atk, ref mat, ref speed);
                    ApplyItemBonus(charSave.armorItemId, itemGetter, ref maxHp, ref maxMp, ref atk, ref mat, ref speed);
                }

                PlayerUnitState unitState = new PlayerUnitState
                {
                    CharacterId = charId,
                    PartyOrder = order++,
                    CurrentHp = maxHp,
                    CurrentMp = maxMp,
                    MaxHp = maxHp,
                    MaxMp = maxMp,
                    Atk = atk,
                    Mat = mat,
                    Speed = speed,
                    AttackActionId = charConfig.AttackActionId,
                    SkillActionId = charConfig.SkillActionId
                };

                partyPlayers.Add(unitState);
            }

            return partyPlayers;
        }

        private static bool TryFindCharacterSave(List<CharacterSave> characters, int characterId, out CharacterSave result)
        {
            result = default;
            if (characters == null)
            {
                return false;
            }

            foreach (CharacterSave c in characters)
            {
                if (c.characterId == characterId)
                {
                    result = c;
                    return true;
                }
            }

            return false;
        }

        private static void ApplyItemBonus(int itemId, Func<int, ItemConfig> itemGetter, ref int maxHp,
            ref int maxMp, ref int atk, ref int mat, ref int speed)
        {
            if (itemId <= 0)
            {
                return;
            }

            ItemConfig itemConfig = itemGetter(itemId);
            if (itemConfig == null)
            {
                return;
            }

            maxHp += itemConfig.MaxHpBonus;
            maxMp += itemConfig.MaxMpBonus;
            atk += itemConfig.AtkBonus;
            mat += itemConfig.MatBonus;
            speed += itemConfig.SpeedBonus;
        }
    }
}
