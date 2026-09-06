using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using SepCore.AsyncTask;
using SepCore.Definition;
using SepCore.Run;
using UnityEngine;
using UnityEngine.UI;

namespace SepCore.UI
{
    /// <summary>
    /// 角色详情与选中物品面板逻辑（手写 partial，与自动生成的 CharacterForm.cs 合并）。
    /// 作为嵌套表单被顶级表单（RoundBackpackForm）编排，管理出战角色 1~4 页签切换、属性与装备展示、以及选中物品详情与移入保险箱操作。
    /// </summary>
    public partial class CharacterForm : UGuiForm
    {
        private IReadOnlyList<RoundCharacterState> _party;
        private int _selectedCharacterIndex = 0;
        private int _characterIconVersion = 0;
        private bool _listenersBound = false;
        private Action _onMoveToSafeClicked;

        /// <summary>
        /// 注册移入保险箱按钮点击回调。
        /// </summary>
        public void SetOnMoveToSafeClicked(Action callback)
        {
            _onMoveToSafeClicked = callback;
        }

        /// <summary>
        /// 刷新队伍出战角色列表与当前选中的角色属性。
        /// </summary>
        public void RefreshParty(IReadOnlyList<RoundCharacterState> party)
        {
            _party = party;
            EnsureListenersBound();

            int partyCount = party != null ? party.Count : 0;
            View.characterTab01Toggle.gameObject.SetActive(partyCount > 0);
            View.characterTab02Toggle.gameObject.SetActive(partyCount > 1);
            View.characterTab03Toggle.gameObject.SetActive(partyCount > 2);
            View.characterTab04Toggle.gameObject.SetActive(partyCount > 3);

            if (_selectedCharacterIndex >= partyCount)
            {
                _selectedCharacterIndex = Math.Max(0, partyCount - 1);
            }

            SyncTabToggles();
            RefreshCharacterDetails();
        }

        /// <summary>
        /// 刷新右侧物品详情面板与移入保险箱按钮状态。
        /// </summary>
        public void RefreshSelectedItem(ItemStack stack, bool isBackpackSlot, bool canMoveToSafe)
        {
            if (stack.itemId <= 0 || stack.count <= 0)
            {
                ClearSelectedItem();
                return;
            }

            ItemConfig config = GameEntry.Luban.Get<ItemConfig>(stack.itemId);
            if (config == null)
            {
                ClearSelectedItem();
                return;
            }

            View.selectedItemIcon.gameObject.SetActive(true);
            ShowSpriteAsync(config.Icon_Ref, View.selectedItemIcon).Forget();

            View.selectedItemNameFormatText.Set(config.Name, stack.count);
            View.selectedItemRarityText.SetText(config.Rarity.ToString());

            View.moveToSafeButton.interactable = isBackpackSlot && canMoveToSafe;
        }

        /// <summary>
        /// 清空物品详情展示并禁用转移按钮。
        /// </summary>
        public void ClearSelectedItem()
        {
            View.selectedItemIcon.gameObject.SetActive(false);
            View.selectedItemNameFormatText.Clear();
            View.selectedItemRarityText.SetText(string.Empty);
            View.moveToSafeButton.interactable = false;
        }

        private void EnsureListenersBound()
        {
            if (_listenersBound)
            {
                return;
            }

            _listenersBound = true;

            View.characterTab01Toggle.onValueChanged.AddListener(isOn => { if (isOn) SelectCharacter(0); });
            View.characterTab02Toggle.onValueChanged.AddListener(isOn => { if (isOn) SelectCharacter(1); });
            View.characterTab03Toggle.onValueChanged.AddListener(isOn => { if (isOn) SelectCharacter(2); });
            View.characterTab04Toggle.onValueChanged.AddListener(isOn => { if (isOn) SelectCharacter(3); });

            View.moveToSafeButton.onClick.AddListener(OnMoveToSafeButtonClick);
        }

        private void OnMoveToSafeButtonClick()
        {
            _onMoveToSafeClicked?.Invoke();
        }

        private void SelectCharacter(int index)
        {
            _selectedCharacterIndex = index;
            RefreshCharacterDetails();
        }

        private void SyncTabToggles()
        {
            View.characterTab01Toggle.SetIsOnWithoutNotify(_selectedCharacterIndex == 0);
            View.characterTab02Toggle.SetIsOnWithoutNotify(_selectedCharacterIndex == 1);
            View.characterTab03Toggle.SetIsOnWithoutNotify(_selectedCharacterIndex == 2);
            View.characterTab04Toggle.SetIsOnWithoutNotify(_selectedCharacterIndex == 3);
        }

        private void RefreshCharacterDetails()
        {
            _characterIconVersion++;

            if (_party == null || _selectedCharacterIndex < 0 || _selectedCharacterIndex >= _party.Count)
            {
                View.characterIcon.gameObject.SetActive(false);
                View.characterIcon.sprite = null;
                View.memberHpFormatText.Clear();
                View.memberMpFormatText.Clear();
                View.attackFormatText.Clear();
                View.magicFormatText.Clear();
                View.speedFormatText.Clear();
                View.weaponNameText.SetText(string.Empty);
                View.weaponIcon.gameObject.SetActive(false);
                View.armorNameText.SetText(string.Empty);
                View.armorIcon.gameObject.SetActive(false);
                return;
            }

            RoundCharacterState character = _party[_selectedCharacterIndex];

            CharacterConfig characterConfig = GameEntry.Luban.Get<CharacterConfig>(character.CharacterId);
            if (characterConfig != null && characterConfig.Icon_Ref != null)
            {
                View.characterIcon.gameObject.SetActive(true);
                ShowCharacterIconAsync(characterConfig.Icon_Ref, _characterIconVersion).Forget();
            }
            else
            {
                View.characterIcon.gameObject.SetActive(false);
                View.characterIcon.sprite = null;
            }

            View.memberHpFormatText.Set(character.CurrentHp, character.MaxHp);
            View.memberMpFormatText.Set(character.CurrentMp, character.MaxMp);
            View.attackFormatText.Set(character.Atk);
            View.magicFormatText.Set(character.Mat);
            View.speedFormatText.Set(character.Speed);

            if (character.WeaponItemId > 0)
            {
                ItemConfig weaponConfig = GameEntry.Luban.Get<ItemConfig>(character.WeaponItemId);
                View.weaponNameText.SetText(weaponConfig != null ? weaponConfig.Name : string.Empty);
                if (weaponConfig != null)
                {
                    View.weaponIcon.gameObject.SetActive(true);
                    ShowSpriteAsync(weaponConfig.Icon_Ref, View.weaponIcon).Forget();
                }
                else
                {
                    View.weaponIcon.gameObject.SetActive(false);
                }
            }
            else
            {
                View.weaponNameText.SetText(string.Empty);
                View.weaponIcon.gameObject.SetActive(false);
            }

            if (character.ArmorItemId > 0)
            {
                ItemConfig armorConfig = GameEntry.Luban.Get<ItemConfig>(character.ArmorItemId);
                View.armorNameText.SetText(armorConfig != null ? armorConfig.Name : string.Empty);
                if (armorConfig != null)
                {
                    View.armorIcon.gameObject.SetActive(true);
                    ShowSpriteAsync(armorConfig.Icon_Ref, View.armorIcon).Forget();
                }
                else
                {
                    View.armorIcon.gameObject.SetActive(false);
                }
            }
            else
            {
                View.armorNameText.SetText(string.Empty);
                View.armorIcon.gameObject.SetActive(false);
            }
        }

        private async UniTaskVoid ShowCharacterIconAsync(SpriteConfig spriteConfig, int iconVersion)
        {
            if (spriteConfig == null)
            {
                return;
            }

            Sprite sprite = await SpriteLoader.LoadSpriteAsync(spriteConfig);
            if (sprite != null && _characterIconVersion == iconVersion)
            {
                View.characterIcon.sprite = sprite;
                View.characterIcon.gameObject.SetActive(true);
            }
        }

        private async UniTaskVoid ShowSpriteAsync(SpriteConfig spriteConfig, Image targetImage)
        {
            if (spriteConfig == null || targetImage == null)
            {
                return;
            }

            Sprite sprite = await SpriteLoader.LoadSpriteAsync(spriteConfig);
            if (sprite != null && targetImage != null)
            {
                targetImage.sprite = sprite;
                targetImage.gameObject.SetActive(true);
            }
        }
    }
}
