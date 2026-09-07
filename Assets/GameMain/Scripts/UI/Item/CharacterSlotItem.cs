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
            if (_button != null)
            {
                _button.onClick.RemoveAllListeners();
                if (_onClick != null)
                {
                    _button.onClick.AddListener(() => _onClick?.Invoke());
                }
            }
        }

        /// <summary>
        /// 设置选中高亮。
        /// </summary>
        public void SetSelected(bool selected)
        {
            if (_bg != null)
            {
                _bg.color = selected ? SelectedBgColor : Color.white;
            }
        }

        /// <summary>
        /// 用存档角色填充格子；角色配置不存在时按空格子显示。
        /// </summary>
        public void SetCharacter(CharacterSave save)
        {
            CharacterConfig config = GameEntry.Luban.Get<CharacterConfig>(save.characterId);
            if (config == null)
            {
                Log.Warning("Can not find character config '{0}' for squad slot.", save.characterId);
                SetEmpty();
                return;
            }

            _characterName.text = config.Name;
            _hpText.Set(config.MaxHp);
            _mpText.Set(config.MaxMp);
            _speedText.Set(config.Speed);
            _atkText.Set(config.Atk);
            _matText.Set(config.Mat);
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
            if (_button != null)
            {
                _button.onClick.RemoveAllListeners();
            }
            _onClick = null;
        }

        private void HideIcon()
        {
            if (_icon == null)
            {
                return;
            }

            _icon.sprite = null;
            _icon.gameObject.SetActive(false);
        }

        /// <summary>
        /// 异步加载角色图标；iconVersion 用于防止复用格子时旧加载结果覆盖新内容。
        /// </summary>
        private async UniTaskVoid ShowIconAsync(SpriteConfig iconConfig, int iconVersion)
        {
            if (iconConfig == null || _icon == null)
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
