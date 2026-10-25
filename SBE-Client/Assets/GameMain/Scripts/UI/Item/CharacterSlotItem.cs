using Cysharp.Threading.Tasks;
using SepCore.AsyncTask;
using SepCore.Definition;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityGameFramework.Runtime;

namespace SepCore.UI
{
    public class CharacterSlotItem : MonoBehaviour
    {
        [SerializeField] private Image _bg;
        [SerializeField] private Image _icon;
        [SerializeField] private TextMeshProUGUI _characterName;
        [SerializeField] private FormatTextUI _hpText;
        [SerializeField] private FormatTextUI _mpText;
        [SerializeField] private FormatTextUI _speedText;
        [SerializeField] private FormatTextUI _atkText;
        [SerializeField] private FormatTextUI _matText;
        [SerializeField] private Button _button;

        private static readonly Color SelectedBgColor = new Color(0.86f, 0.95f, 1f, 1f);

        private int _iconVersion = 0;
        private System.Action _onClick = null;

        /// <summary>
        /// 设置格子点击回调。
        /// </summary>
        public void SetOnClick(System.Action onClick)
        {
            _onClick = onClick;
            _button.onClick.RemoveAllListeners();
            if (_onClick != null)
            {
                _button.onClick.AddListener(() => _onClick?.Invoke());
            }
        }

        /// <summary>
        /// 设置选中高亮。
        /// </summary>
        public void SetSelected(bool selected)
        {
            _bg.color = selected ? SelectedBgColor : Color.white;
        }

        /// <summary>
        /// 用存档角色填充格子；角色配置不存在时按空格子显示。
        /// </summary>
        /// <param name="save">存档角色数据。</param>
        /// <param name="applyEquipmentBonus">是否计入武器与防具属性加成。默认 false（展示原始属性，如 Loadout 角色列表）；为 true 时展示加成后属性（如 Home 战备编队列表）。</param>
        public void SetCharacter(CharacterSave save, bool applyEquipmentBonus = false)
        {
            CharacterConfig config = GameEntry.Luban.Get<CharacterConfig>(save.characterId);
            if (config == null)
            {
                Log.Warning("Can not find character config '{0}' for squad slot.", save.characterId);
                SetEmpty();
                return;
            }

            int hp = config.MaxHp;
            int mp = config.MaxMp;
            int speed = config.Speed;
            int atk = config.Atk;
            int mat = config.Mat;

            if (applyEquipmentBonus)
            {
                if (save.weaponItemId > 0)
                {
                    ItemConfig weaponConfig = GameEntry.Luban.Get<ItemConfig>(save.weaponItemId);
                    if (weaponConfig != null)
                    {
                        hp += weaponConfig.MaxHpBonus;
                        mp += weaponConfig.MaxMpBonus;
                        speed += weaponConfig.SpeedBonus;
                        atk += weaponConfig.AtkBonus;
                        mat += weaponConfig.MatBonus;
                    }
                }

                if (save.armorItemId > 0)
                {
                    ItemConfig armorConfig = GameEntry.Luban.Get<ItemConfig>(save.armorItemId);
                    if (armorConfig != null)
                    {
                        hp += armorConfig.MaxHpBonus;
                        mp += armorConfig.MaxMpBonus;
                        speed += armorConfig.SpeedBonus;
                        atk += armorConfig.AtkBonus;
                        mat += armorConfig.MatBonus;
                    }
                }
            }

            _characterName.text = config.Name;
            _hpText.Set(hp);
            _mpText.Set(mp);
            _speedText.Set(speed);
            _atkText.Set(atk);
            _matText.Set(mat);
            _iconVersion++;
            ShowIconAsync(config.Icon_Ref, _iconVersion).Forget();
        }

        /// <summary>
        /// 显示为空格子。
        /// </summary>
        public void SetEmpty()
        {
            _iconVersion++;
            _characterName.text = string.Empty;
            _hpText.Clear();
            _mpText.Clear();
            _speedText.Clear();
            _atkText.Clear();
            _matText.Clear();
            HideIcon();
            SetSelected(false);
            _button.onClick.RemoveAllListeners();
            _onClick = null;
        }

        private void HideIcon()
        {
            _icon.sprite = null;
            _icon.gameObject.SetActive(false);
        }

        /// <summary>
        /// 异步加载角色图标；iconVersion 用于防止复用格子时旧加载结果覆盖新内容。
        /// </summary>
        private async UniTaskVoid ShowIconAsync(SpriteConfig iconConfig, int iconVersion)
        {
            if (iconConfig == null)
            {
                return;
            }

            Sprite sprite = await SpriteLoader.LoadSpriteAsync(iconConfig);
            if (sprite == null || _iconVersion != iconVersion)
            {
                return;
            }

            _icon.sprite = sprite;
            _icon.gameObject.SetActive(true);
        }
    }
}
