using Cysharp.Threading.Tasks;
using SepCore.AsyncTask;
using SepCore.Base;
using SepCore.Definition;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityGameFramework.Runtime;

namespace SepCore.UI
{
    public class WarehouseSlotItem : MonoBehaviour
    {
        private static readonly Color RarityWhite = new Color32(245, 247, 248, 255);
        private static readonly Color RarityGreen = new Color32(93, 155, 97, 255);
        private static readonly Color RarityBlue = new Color32(52, 127, 168, 255);
        private static readonly Color RarityGold = new Color32(214, 169, 61, 255);
        private static readonly Color RarityRed = new Color32(185, 87, 79, 255);
        private static readonly Color RarityEmpty = new Color32(111, 101, 90, 255);
        private static readonly Color SelectedBgColor = new Color(0.86f, 0.95f, 1f, 1f);

        [SerializeField] private Image bg;
        [SerializeField] private Button button;
        [SerializeField] private Image icon;
        [SerializeField] private Image rarity;
        [SerializeField] private TextMeshProUGUI quantityText;

        private int _slotId = 0;
        private bool _filled = false;
        private bool _clickBound = false;
        private int _iconVersion = 0;

        /// <summary>
        /// 设置格子在固定网格中的索引，点击事件以此作为唯一标识。
        /// </summary>
        public void SetSlotId(int slotId)
        {
            _slotId = slotId;
        }

        /// <summary>
        /// 用物品堆叠填充格子；物品配置不存在时按空格子显示。
        /// </summary>
        public void SetItem(ItemStack stack)
        {
            ItemConfig config = GameEntry.Luban.Get<ItemConfig>(stack.itemId);
            if (config == null)
            {
                Log.Warning("Can not find item config '{0}' for warehouse slot.", stack.itemId);
                SetEmpty();
                return;
            }

            _filled = true;
            _iconVersion++;
            rarity.color = GetRarityColor(config.Rarity);
            quantityText.text = stack.count.ToString();
            ShowIconAsync(config.Icon_Ref, _iconVersion).Forget();
            BindClick();
            button.interactable = true;
        }

        /// <summary>
        /// 显示为空格子。
        /// </summary>
        public void SetEmpty()
        {
            _filled = false;
            _iconVersion++;
            rarity.color = RarityEmpty;
            quantityText.text = string.Empty;
            HideIcon();
            button.interactable = false;
        }

        /// <summary>
        /// 设置选中高亮。
        /// </summary>
        public void SetSelected(bool selected)
        {
            bg.color = selected ? SelectedBgColor : Color.white;
        }

        private void BindClick()
        {
            if (_clickBound)
            {
                return;
            }

            _clickBound = true;
            button.onClick.AddListener(OnClick);
        }

        private void OnClick()
        {
            if (!_filled)
            {
                return;
            }

            GameEntry.Event.Fire(this, WarehouseSlotItemClickEventArgs.Create(_slotId));
        }

        private void HideIcon()
        {
            icon.sprite = null;
            icon.gameObject.SetActive(false);
        }

        /// <summary>
        /// 异步加载物品图标；iconVersion 用于防止复用格子时旧加载结果覆盖新内容。
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

            icon.sprite = sprite;
            icon.gameObject.SetActive(true);
        }

        private static Color GetRarityColor(Rarity rarity)
        {
            switch (rarity)
            {
                case Rarity.Green:
                    return RarityGreen;
                case Rarity.Blue:
                    return RarityBlue;
                case Rarity.Gold:
                    return RarityGold;
                case Rarity.Red:
                    return RarityRed;
                default:
                    return RarityWhite;
            }
        }
    }
}