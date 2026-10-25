using System;
using Cysharp.Threading.Tasks;
using SepCore.AsyncTask;
using SepCore.Definition;
using UnityEngine;
using UnityGameFramework.Runtime;

namespace SepCore.UI
{
    /// <summary>
    /// 角色预览界面逻辑（手写 partial，与自动生成的 CharacterPreviewForm.cs 合并）。
    /// 负责展示选中角色的头像、名称、加成后属性（HP/MP/ATK/MAT/SPD），
    /// 以及武器和防具的装备状态与卸下触发。
    /// </summary>
    public partial class CharacterPreviewForm : UGuiForm
    {
        public event Action OnUnequipWeaponRequested;
        public event Action OnUnequipArmorRequested;

        private bool _listenersBound = false;
        private int _characterIconVersion = 0;
        private int _weaponIconVersion = 0;
        private int _armorIconVersion = 0;

        /// <summary>
        /// 刷新角色预览信息，包含装备加成属性与槽位状态。
        /// </summary>
        public void Refresh(CharacterSave characterSave)
        {
            EnsureListenersBound();

            CharacterConfig characterConfig = GameEntry.Luban.Get<CharacterConfig>(characterSave.characterId);
            if (characterConfig == null)
            {
                Log.Warning("Can not find character config '{0}' for preview form.", characterSave.characterId);
                return;
            }

            View.characterNameText.text = characterConfig.Name;
            _characterIconVersion++;
            ShowCharacterIconAsync(characterConfig.Icon_Ref, _characterIconVersion).Forget();

            ItemConfig weaponConfig = characterSave.weaponItemId > 0
                ? GameEntry.Luban.Get<ItemConfig>(characterSave.weaponItemId)
                : null;
            ItemConfig armorConfig = characterSave.armorItemId > 0
                ? GameEntry.Luban.Get<ItemConfig>(characterSave.armorItemId)
                : null;

            int totalHp = characterConfig.MaxHp;
            int totalMp = characterConfig.MaxMp;
            int totalAtk = characterConfig.Atk;
            int totalMat = characterConfig.Mat;
            int totalSpeed = characterConfig.Speed;

            if (weaponConfig != null)
            {
                totalHp += weaponConfig.MaxHpBonus;
                totalMp += weaponConfig.MaxMpBonus;
                totalAtk += weaponConfig.AtkBonus;
                totalMat += weaponConfig.MatBonus;
                totalSpeed += weaponConfig.SpeedBonus;
            }

            if (armorConfig != null)
            {
                totalHp += armorConfig.MaxHpBonus;
                totalMp += armorConfig.MaxMpBonus;
                totalAtk += armorConfig.AtkBonus;
                totalMat += armorConfig.MatBonus;
                totalSpeed += armorConfig.SpeedBonus;
            }

            View.hpFormatText.Set(totalHp);
            View.mpFormatText.Set(totalMp);
            View.atkFormatText.Set(totalAtk);
            View.matFormatText.Set(totalMat);
            View.speedFormatText.Set(totalSpeed);

            if (weaponConfig != null)
            {
                View.weaponSlotButton.interactable = true;
                View.weaponNameText.text = weaponConfig.Name;
                _weaponIconVersion++;
                ShowWeaponIconAsync(weaponConfig.Icon_Ref, _weaponIconVersion).Forget();
            }
            else
            {
                View.weaponSlotButton.interactable = false;
                View.weaponNameText.text = "无";
                HideWeaponIcon();
            }

            if (armorConfig != null)
            {
                View.armorSlotButton.interactable = true;
                View.armorNameText.text = armorConfig.Name;
                _armorIconVersion++;
                ShowArmorIconAsync(armorConfig.Icon_Ref, _armorIconVersion).Forget();
            }
            else
            {
                View.armorSlotButton.interactable = false;
                View.armorNameText.text = "无";
                HideArmorIcon();
            }
        }

        private void EnsureListenersBound()
        {
            if (_listenersBound)
            {
                return;
            }

            _listenersBound = true;
            View.weaponSlotButton.onClick.AddListener(OnWeaponSlotButtonClicked);
            View.armorSlotButton.onClick.AddListener(OnArmorSlotButtonClicked);
        }

        private void OnWeaponSlotButtonClicked()
        {
            OnUnequipWeaponRequested?.Invoke();
        }

        private void OnArmorSlotButtonClicked()
        {
            OnUnequipArmorRequested?.Invoke();
        }

        private void HideWeaponIcon()
        {
            _weaponIconVersion++;
            View.weaponIcon.sprite = null;
            View.weaponIcon.gameObject.SetActive(false);
        }

        private void HideArmorIcon()
        {
            _armorIconVersion++;
            View.armorIcon.sprite = null;
            View.armorIcon.gameObject.SetActive(false);
        }

        private async UniTaskVoid ShowCharacterIconAsync(SpriteConfig iconConfig, int iconVersion)
        {
            if (iconConfig == null)
            {
                return;
            }

            Sprite sprite = await SpriteLoader.LoadSpriteAsync(iconConfig);
            if (sprite == null || _characterIconVersion != iconVersion)
            {
                return;
            }

            View.characterIcon.sprite = sprite;
            View.characterIcon.gameObject.SetActive(true);
        }

        private async UniTaskVoid ShowWeaponIconAsync(SpriteConfig iconConfig, int iconVersion)
        {
            if (iconConfig == null)
            {
                return;
            }

            Sprite sprite = await SpriteLoader.LoadSpriteAsync(iconConfig);
            if (sprite == null || _weaponIconVersion != iconVersion)
            {
                return;
            }

            View.weaponIcon.sprite = sprite;
            View.weaponIcon.gameObject.SetActive(true);
        }

        private async UniTaskVoid ShowArmorIconAsync(SpriteConfig iconConfig, int iconVersion)
        {
            if (iconConfig == null)
            {
                return;
            }

            Sprite sprite = await SpriteLoader.LoadSpriteAsync(iconConfig);
            if (sprite == null || _armorIconVersion != iconVersion)
            {
                return;
            }

            View.armorIcon.sprite = sprite;
            View.armorIcon.gameObject.SetActive(true);
        }
    }
}
