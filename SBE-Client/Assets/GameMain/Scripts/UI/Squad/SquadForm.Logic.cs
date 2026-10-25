using System;
using System.Collections.Generic;
using SepCore.Definition;
using UnityEngine;
using UnityGameFramework.Runtime;

namespace SepCore.UI
{
    /// <summary>
    /// 编队界面逻辑（手写 partial，与自动生成的 SquadForm.cs 合并）。
    /// 维护出战队伍列表（最多 4 人，允许 0 人），支持卡片点击入队/离队；
    /// 选中的出战角色在 ScrollView 中优先按出战位次排列，其余未选中的角色紧随其后。
    /// </summary>
    public partial class SquadForm : UGuiForm
    {
        public event Action OnPartyChanged;

        private readonly List<int> _partyCharacterIds = new List<int>();
        private readonly List<CharacterSave> _allCharacters = new List<CharacterSave>();
        private readonly List<CharacterSlotItem> _activeSlots = new List<CharacterSlotItem>();
        private bool _applyEquipmentBonus = true;

        /// <summary>
        /// 按传入的角色列表重建角色槽列表 UI，每名角色一个槽位。
        /// 出战角色槽位默认展示装备加成后的属性。
        /// </summary>
        public void RefreshCharacterList(IReadOnlyList<CharacterSave> characters, bool applyEquipmentBonus = true, bool initializeParty = true)
        {
            _applyEquipmentBonus = applyEquipmentBonus;
            _allCharacters.Clear();
            if (characters != null)
            {
                _allCharacters.AddRange(characters);
            }

            InitPartyCharacterIds(initializeParty);
            RebuildSlots();
        }

        private void InitPartyCharacterIds(bool initializeParty)
        {
            _partyCharacterIds.Clear();

            SaveData save = GameEntry.Save.Data;
            if (save == null)
            {
                return;
            }

            if (save.loadout == null)
            {
                save.loadout = new LoadoutSave();
            }

            int maxParty = GameEntry.Luban.Global?.Data != null ? GameEntry.Luban.Global.Data.MaxPlayerPartySize : 4;
            int[] savedParty = save.loadout.partyCharacterIds;

            if (savedParty != null)
            {
                for (int i = 0; i < savedParty.Length && _partyCharacterIds.Count < maxParty; i++)
                {
                    int charId = savedParty[i];
                    if (HasCharacter(charId) && !_partyCharacterIds.Contains(charId))
                    {
                        _partyCharacterIds.Add(charId);
                    }
                }
            }
            else if (initializeParty && _allCharacters.Count > 0)
            {
                // 仅当存档从未初始化过 partyCharacterIds (为 null) 时，才默认选取前 1~maxParty 人
                for (int i = 0; i < Math.Min(_allCharacters.Count, maxParty); i++)
                {
                    _partyCharacterIds.Add(_allCharacters[i].characterId);
                }
            }

            if (GameEntry.Net == null || GameEntry.Net.OnlineReady)
            {
                bool changed = savedParty == null
                    ? _partyCharacterIds.Count > 0
                    : savedParty.Length != _partyCharacterIds.Count;
                for (int i = 0; savedParty != null && !changed && i < savedParty.Length; i++)
                {
                    changed = savedParty[i] != _partyCharacterIds[i];
                }

                save.loadout.partyCharacterIds = _partyCharacterIds.ToArray();
                if (changed)
                {
                    GameEntry.Net?.NotifyEconomyChanged();
                }
            }
        }

        private bool HasCharacter(int characterId)
        {
            for (int i = 0; i < _allCharacters.Count; i++)
            {
                if (_allCharacters[i].characterId == characterId)
                {
                    return true;
                }
            }

            return false;
        }

        private bool TryGetCharacterSave(int characterId, out CharacterSave result)
        {
            for (int i = 0; i < _allCharacters.Count; i++)
            {
                if (_allCharacters[i].characterId == characterId)
                {
                    result = _allCharacters[i];
                    return true;
                }
            }

            result = default;
            return false;
        }

        private void RebuildSlots()
        {
            SquadView squadView = View;
            CharacterSlotItem template = squadView.characterSlotTemplate;
            template.gameObject.SetActive(false);

            for (int i = squadView.characterSlotRoot.childCount - 1; i >= 0; i--)
            {
                Transform child = squadView.characterSlotRoot.GetChild(i);
                if (child == template.transform)
                {
                    continue;
                }

                Destroy(child.gameObject);
            }

            _activeSlots.Clear();

            int maxParty = GameEntry.Luban.Global?.Data != null ? GameEntry.Luban.Global.Data.MaxPlayerPartySize : 4;
            squadView.squadMemberFormatText.Set(_partyCharacterIds.Count, maxParty);

            // 1. 先按出战队伍次序排列选中的出战角色
            for (int i = 0; i < _partyCharacterIds.Count; i++)
            {
                int charId = _partyCharacterIds[i];
                if (TryGetCharacterSave(charId, out CharacterSave charSave))
                {
                    CharacterSlotItem slot = Instantiate(template, squadView.characterSlotRoot);
                    slot.gameObject.SetActive(true);
                    slot.SetCharacter(charSave, _applyEquipmentBonus);
                    slot.SetSelected(true);
                    slot.SetOnClick(() => OnCharacterSlotClicked(charId));
                    _activeSlots.Add(slot);
                }
            }

            // 2. 其余未选中的角色紧随其后
            for (int i = 0; i < _allCharacters.Count; i++)
            {
                int charId = _allCharacters[i].characterId;
                if (_partyCharacterIds.Contains(charId))
                {
                    continue;
                }

                CharacterSlotItem slot = Instantiate(template, squadView.characterSlotRoot);
                slot.gameObject.SetActive(true);
                slot.SetCharacter(_allCharacters[i], _applyEquipmentBonus);
                slot.SetSelected(false);
                slot.SetOnClick(() => OnCharacterSlotClicked(charId));
                _activeSlots.Add(slot);
            }
        }

        private void OnCharacterSlotClicked(int characterId)
        {
            // 强制在线：出战队伍由服务器记账，未完成登录同步时禁止编辑
            if (GameEntry.Net != null && !GameEntry.Net.OnlineReady)
            {
                Log.Error("Cannot edit squad while offline.");
                return;
            }

            int maxParty = GameEntry.Luban.Global?.Data != null ? GameEntry.Luban.Global.Data.MaxPlayerPartySize : 4;

            if (_partyCharacterIds.Contains(characterId))
            {
                // 已在出战队伍中：直接移除（允许队伍 0 人）
                _partyCharacterIds.Remove(characterId);
            }
            else
            {
                // 未在出战队伍中，尝试添加到队尾（上限 maxParty 名角色）
                if (_partyCharacterIds.Count >= maxParty)
                {
                    return;
                }

                _partyCharacterIds.Add(characterId);
            }

            SaveData save = GameEntry.Save.Data;
            if (save != null)
            {
                if (save.loadout == null)
                {
                    save.loadout = new LoadoutSave();
                }

                save.loadout.partyCharacterIds = _partyCharacterIds.ToArray();
            }

            // 出战队伍属于服务器记账的战备配置，差分上报
            GameEntry.Net?.NotifyEconomyChanged();

            RebuildSlots();
            OnPartyChanged?.Invoke();
        }
    }
}
