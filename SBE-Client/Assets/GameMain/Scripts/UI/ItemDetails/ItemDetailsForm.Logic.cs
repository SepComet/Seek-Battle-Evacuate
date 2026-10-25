using Cysharp.Threading.Tasks;
using SepCore.AsyncTask;
using SepCore.Definition;
using UnityEngine;
using UnityGameFramework.Runtime;

namespace SepCore.UI
{
    /// <summary>
    /// 道具详情界面逻辑（手写 partial，与自动生成的 ItemDetailsForm.cs 合并）。
    /// 只提供详情显示能力，由调用方（WarehouseForm）传入物品 ID 后刷新。
    /// </summary>
    public partial class ItemDetailsForm : UGuiForm
    {
        private int _iconVersion = 0;

        /// <summary>
        /// 按物品 ID 刷新详情界面；配置不存在时清空显示。
        /// </summary>
        public void Refresh(int itemId)
        {
            _iconVersion++;
            ItemDetailsView view = View;

            ItemConfig config = GameEntry.Luban.Get<ItemConfig>(itemId);
            if (config == null)
            {
                Log.Warning("Can not find item config '{0}' for item details.", itemId);
                ClearDetails(view);
                return;
            }

            view.itemDetailNameText.text = config.Name;
            view.itemDetailTypeText.text = GetItemTypeName(config.ItemType) + "  /  " + GetRarityName(config.Rarity);
            view.hpFormatText.Set(config.MaxHpBonus);
            view.atkFormatText.Set(config.AtkBonus);
            view.mpFormatText.Set(config.MaxMpBonus);
            view.matFormatText.Set(config.MatBonus);
            view.speedFormatText.Set(0);
            view.stackFormatText.Set(config.StackLimit);
            view.itemDetailDescriptionText.text = string.Empty;
            view.itemDetailIcon.gameObject.SetActive(false);

            ShowIconAsync(config.Icon_Ref, _iconVersion).Forget();
        }

        private static void ClearDetails(ItemDetailsView view)
        {
            view.itemDetailNameText.text = string.Empty;
            view.itemDetailTypeText.text = string.Empty;
            view.itemDetailDescriptionText.text = string.Empty;
            view.itemDetailIcon.gameObject.SetActive(false);
        }

        /// <summary>
        /// 异步加载物品图标；iconVersion 用于防止连续刷新时旧加载结果覆盖新内容。
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

            View.itemDetailIcon.sprite = sprite;
            View.itemDetailIcon.gameObject.SetActive(true);
        }

        private static string GetItemTypeName(ItemType itemType)
        {
            switch (itemType)
            {
                case ItemType.Loot:
                    return "战利品";
                case ItemType.Equipment:
                    return "装备";
                case ItemType.Consumable:
                    return "消耗品";
                default:
                    return "未知";
            }
        }

        private static string GetRarityName(Rarity rarity)
        {
            switch (rarity)
            {
                case Rarity.White:
                    return "白";
                case Rarity.Green:
                    return "绿";
                case Rarity.Blue:
                    return "蓝";
                case Rarity.Gold:
                    return "金";
                case Rarity.Red:
                    return "红";
                default:
                    return "无";
            }
        }
    }
}