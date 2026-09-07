using DG.Tweening;
using SepCore.Definition;
using UnityEngine;
using UnityEngine.UI;
using UnityGameFramework.Runtime;

namespace SepCore.UI
{
    /// <summary>
    /// 大厅界面逻辑（手写 partial，与自动生成的 LobbyForm.cs 合并）。
    /// 导航使用 Toggle + ToggleGroup 管理选中状态；本类只负责页面切换与标记动画。
    /// </summary>
    public partial class LobbyForm : UGuiForm
    {
        private const float SelectionMarkerMoveDuration = 0.25f;
        private GameObject _loadoutPage;
        private LoadoutForm _loadoutForm;
        private LobbyPage _currentPage = LobbyPage.CombatReadiness;

        private enum LobbyPage
        {
            CombatReadiness,
            Warehouse,
            Loadout,
        }

        protected override void OnInit(object userData)
        {
            base.OnInit(userData);

            _loadoutPage = transform.Find("SafeFrame/LoadoutForm").gameObject;
            if (_loadoutPage != null)
            {
                _loadoutForm = _loadoutPage.GetComponent<LoadoutForm>();
            }

            View.homeToggle.onValueChanged.AddListener(OnHomeToggleValueChanged);
            View.warehouseToggle.onValueChanged.AddListener(OnWarehouseToggleValueChanged);
            View.loadoutToggle.onValueChanged.AddListener(OnLoadoutToggleValueChanged);
        }

        protected override void OnOpen(object userData)
        {
            base.OnOpen(userData);
            SwitchPage(LobbyPage.CombatReadiness, View.homeToggle);
        }

        protected override void OnClose(bool isShutdown, object userData)
        {
            if (_currentPage == LobbyPage.Loadout)
            {
                GameEntry.Save.Save();
            }

            if (View.selectionMarkerObject != null)
            {
                View.selectionMarkerObject.DOKill();
            }

            View.homeToggle.onValueChanged.RemoveListener(OnHomeToggleValueChanged);
            View.warehouseToggle.onValueChanged.RemoveListener(OnWarehouseToggleValueChanged);
            View.loadoutToggle.onValueChanged.RemoveListener(OnLoadoutToggleValueChanged);

            base.OnClose(isShutdown, userData);
        }

        private void OnHomeToggleValueChanged(bool isOn)
        {
            if (isOn)
            {
                SwitchPage(LobbyPage.CombatReadiness, View.homeToggle);
            }
        }

        private void OnWarehouseToggleValueChanged(bool isOn)
        {
            if (isOn)
            {
                SwitchPage(LobbyPage.Warehouse, View.warehouseToggle);
            }
        }

        private void OnLoadoutToggleValueChanged(bool isOn)
        {
            if (isOn)
            {
                SwitchPage(LobbyPage.Loadout, View.loadoutToggle);
            }
        }

        private void SwitchPage(LobbyPage page, Toggle activeToggle)
        {
            if (_currentPage == LobbyPage.Loadout && page != LobbyPage.Loadout)
            {
                GameEntry.Save.Save();
            }

            _currentPage = page;
            bool showCombatReadiness = page == LobbyPage.CombatReadiness;
            bool showWarehouse = page == LobbyPage.Warehouse;
            bool showLoadout = page == LobbyPage.Loadout;

            _loadoutPage.SetActive(showLoadout);

            if (View.homeForm != null)
            {
                View.homeForm.gameObject.SetActive(showCombatReadiness);
            }

            if (View.warehouseForm != null)
            {
                View.warehouseForm.gameObject.SetActive(showWarehouse);
            }

            MoveSelectionMarker(activeToggle.transform as RectTransform);

            if (showCombatReadiness)
            {
                RefreshCombatReadiness();
            }

            if (showWarehouse)
            {
                RefreshWarehouse();
            }

            if (showLoadout)
            {
                RefreshLoadout();
            }
        }

        private void MoveSelectionMarker(RectTransform targetToggle)
        {
            RectTransform marker = View.selectionMarkerObject;
            if (marker == null || targetToggle == null || marker.parent == targetToggle)
            {
                return;
            }

            // 把标记相对目标 Toggle 锚点(anchorMin)的偏移，换算到 Toggle pivot 的局部坐标空间：
            // 锚点相对 pivot 的偏移 = (anchor - pivot) * rect 尺寸。
            Vector3 targetLocalPosition = new Vector3(
                marker.anchoredPosition.x + (marker.anchorMin.x - targetToggle.pivot.x) * targetToggle.rect.width,
                marker.anchoredPosition.y + (marker.anchorMin.y - targetToggle.pivot.y) * targetToggle.rect.height,
                0f);
            Vector3 targetWorldPosition = targetToggle.TransformPoint(targetLocalPosition);
            marker.DOKill();
            marker.DOMove(targetWorldPosition, SelectionMarkerMoveDuration)
                .SetEase(Ease.OutCubic)
                .OnComplete(() => marker.SetParent(targetToggle, true));
        }

        private void RefreshCombatReadiness()
        {
            HomeForm form = View.homeForm;
            if (form == null)
            {
                Log.Warning("LobbyForm combat readiness form is not configured.");
                return;
            }

            SaveData save = GameEntry.Save.Data;
            if (save == null || form.View == null || form.View.squadForm == null)
            {
                Log.Warning("LobbyForm can not refresh combat readiness.");
                return;
            }

            if (form.View.squadForm != null)
            {
                form.View.squadForm.RefreshCharacterList(save.characters);
            }

            if (form.View.deploymentForm != null)
            {
                form.View.deploymentForm.Refresh();
            }
        }

        private void RefreshWarehouse()
        {
            if (View.warehouseForm == null)
            {
                Log.Warning("LobbyForm warehouse form is not configured.");
                return;
            }

            SaveData save = GameEntry.Save.Data;
            View.warehouseForm.Refresh(save != null ? save.mainWarehouse : null);
        }

        private void RefreshLoadout()
        {
            if (_loadoutForm == null && _loadoutPage != null)
            {
                _loadoutForm = _loadoutPage.GetComponent<LoadoutForm>();
            }

            if (_loadoutForm == null)
            {
                Log.Warning("LobbyForm loadout form component is not configured.");
                return;
            }

            _loadoutForm.Refresh();
        }
    }
}
