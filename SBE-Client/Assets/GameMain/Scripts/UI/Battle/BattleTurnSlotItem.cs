using Cysharp.Threading.Tasks;
using SepCore.AsyncTask;
using SepCore.Battle;
using SepCore.Definition;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SepCore.UI
{
    [DisallowMultipleComponent]
    public sealed class BattleTurnSlotItem : MonoBehaviour
    {
        [SerializeField] private GameObject activeMarker;
        [SerializeField] private Image icon;
        [SerializeField] private TextMeshProUGUI label;

        private int _iconVersion;
        private int _currentUnitId;

        /// <summary>
        /// 填充一个回合顺序槽：单位显示名、配置图标与当前行动者标记。
        /// 槽被复用到同一单位时不重复加载图标。
        /// </summary>
        public void SetTurnSlot(BattleUnitView unit, bool isCurrentActor)
        {
            label.text = BattleUnitViewHelper.GetDisplayName(unit);

            SetCurrentActor(isCurrentActor);

            if (_currentUnitId != unit.BattleUnitId)
            {
                _currentUnitId = unit.BattleUnitId;
                _iconVersion++;
                LoadIconAsync(unit, _iconVersion).Forget();
            }
        }

        /// <summary>
        /// 只更新当前行动者标记，不改变显示名。
        /// 同轮内单位行动后由 Logic 逐槽移动高亮。
        /// </summary>
        public void SetCurrentActor(bool isCurrentActor)
        {
            activeMarker.SetActive(isCurrentActor);
        }

        /// <summary>
        /// 异步加载单位图标（玩家/敌人分别走各自配置）；iconVersion 防止旧加载覆盖新内容。
        /// </summary>
        private async UniTaskVoid LoadIconAsync(BattleUnitView unit, int iconVersion)
        {
            SpriteConfig iconConfig = unit.Faction == BattleFactionType.Player
                ? BattleUnitViewHelper.GetPlayerIconConfig(unit.ConfigId)
                : BattleUnitViewHelper.GetEnemyIconConfig(unit.ConfigId);
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
    }
}