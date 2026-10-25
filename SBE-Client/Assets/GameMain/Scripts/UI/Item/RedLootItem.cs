using Cysharp.Threading.Tasks;
using SepCore.AsyncTask;
using SepCore.Definition;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityGameFramework.Runtime;

namespace SepCore.UI
{
    /// <summary>
    /// 结算界面红品质战利品列表项组件。
    /// 负责展示道具图标、道具名称、堆叠数量与总价值。
    /// </summary>
    public class RedLootItem : MonoBehaviour
    {
        [SerializeField] private Image _icon;
        [SerializeField] private TextMeshProUGUI _name;
        [SerializeField] private FormatTextUI _quantity;
        [SerializeField] private FormatTextUI _totalValue;

        private int _iconVersion;

        /// <summary>
        /// 使用物品堆叠填充红装槽位数据。
        /// </summary>
        public void SetItem(ItemStack stack)
        {
            SetItem(stack.itemId, stack.count);
        }

        /// <summary>
        /// 使用物品 ID 与数量填充红装槽位数据。
        /// </summary>
        public void SetItem(int itemId, int count)
        {
            ItemConfig config = GameEntry.Luban.Get<ItemConfig>(itemId);
            if (config == null)
            {
                Log.Warning("Can not find item config '{0}' for red loot item.", itemId);
                ResetItem();
                return;
            }

            _iconVersion++;
            _name.text = config.Name;

            if (_quantity != null)
            {
                _quantity.Set(count);
            }

            int totalValue = config.Value * count;
            if (_totalValue != null)
            {
                _totalValue.Set(totalValue);
            }

            if (config.Icon_Ref != null)
            {
                ShowIconAsync(config.Icon_Ref, _iconVersion).Forget();
            }
            else
            {
                HideIcon();
            }
        }

        /// <summary>
        /// 重置列表项展示状态。
        /// </summary>
        public void ResetItem()
        {
            _iconVersion++;
            HideIcon();

            if (_name != null)
            {
                _name.text = string.Empty;
            }

            if (_quantity != null)
            {
                _quantity.Clear();
            }

            if (_totalValue != null)
            {
                _totalValue.Clear();
            }
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

        private async UniTaskVoid ShowIconAsync(SpriteConfig iconConfig, int iconVersion)
        {
            if (iconConfig == null || _icon == null)
            {
                return;
            }

            Sprite sprite = await SpriteLoader.LoadSpriteAsync(iconConfig);
            if (sprite == null || _iconVersion != iconVersion || this == null)
            {
                return;
            }

            _icon.sprite = sprite;
            _icon.gameObject.SetActive(true);
        }
    }
}
