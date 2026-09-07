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
        private LobbyPageType _currentPageType = LobbyPageType.Home;
        private Vector2 _markerInitialAnchoredPosition;

        protected override void OnInit(object userData)
        {
            base.OnInit(userData);
            _markerInitialAnchoredPosition = View.selectionMarkerObject.anchoredPosition;
        }

        protected override void OnOpen(object userData)
        {
            base.OnOpen(userData);

            _currentPageType = LobbyPageType.Home;
            View.homeToggle.isOn = true;
            
            View.homeToggle.onValueChanged.AddListener(OnHomeToggleValueChanged);
            View.warehouseToggle.onValueChanged.AddListener(OnWarehouseToggleValueChanged);
            View.loadoutToggle.onValueChanged.AddListener(OnLoadoutToggleValueChanged);

            RefreshStorageStatus();
            SwitchPage(LobbyPageType.Home, View.homeToggle);
        }

        protected override void OnClose(bool isShutdown, object userData)
        {
            if (_currentPageType == LobbyPageType.Loadout)
            {
                GameEntry.Save.Save();
            }

            View.selectionMarkerObject.DOKill();
            View.selectionMarkerObject.SetParent(View.homeToggle.transform, false);
            View.selectionMarkerObject.anchoredPosition = _markerInitialAnchoredPosition;

            View.homeToggle.onValueChanged.RemoveListener(OnHomeToggleValueChanged);
            View.warehouseToggle.onValueChanged.RemoveListener(OnWarehouseToggleValueChanged);
            View.loadoutToggle.onValueChanged.RemoveListener(OnLoadoutToggleValueChanged);

            base.OnClose(isShutdown, userData);
        }

        private void OnHomeToggleValueChanged(bool isOn)
        {
            if (isOn)
            {
                SwitchPage(LobbyPageType.Home, View.homeToggle);
            }
        }

        private void OnWarehouseToggleValueChanged(bool isOn)
        {
            if (isOn)
            {
                SwitchPage(LobbyPageType.Warehouse, View.warehouseToggle);
            }
        }

        private void OnLoadoutToggleValueChanged(bool isOn)
        {
            if (isOn)
            {
                SwitchPage(LobbyPageType.Loadout, View.loadoutToggle);
            }
        }

        private void SwitchPage(LobbyPageType pageType, Toggle activeToggle)
        {
            if (_currentPageType == LobbyPageType.Loadout && pageType != LobbyPageType.Loadout)
            {
                GameEntry.Save.Save();
            }

            _currentPageType = pageType;
            bool showHome = pageType == LobbyPageType.Home;
            bool showWarehouse = pageType == LobbyPageType.Warehouse;
            bool showLoadout = pageType == LobbyPageType.Loadout;

            View.homeForm.gameObject.SetActive(showHome);
            View.warehouseForm.gameObject.SetActive(showWarehouse);
            View.loadoutForm.gameObject.SetActive(showLoadout);

            MoveSelectionMarker(activeToggle.transform as RectTransform);

            if (showHome)
            {
                RefreshHome();
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
            if (targetToggle == null || marker.parent == targetToggle)
            {
                return;
            }

            // 把标记相对目标 Toggle 锚点(anchorMin)的偏移，换算到 Toggle pivot 的局部坐标空间：
            // 锚点相对 pivot 的偏移 = (anchor - pivot) * rect 尺寸。
            Vector3 targetLocalPosition = new Vector3(
                _markerInitialAnchoredPosition.x + (marker.anchorMin.x - targetToggle.pivot.x) * targetToggle.rect.width,
                _markerInitialAnchoredPosition.y + (marker.anchorMin.y - targetToggle.pivot.y) * targetToggle.rect.height,
                0f);
            Vector3 targetWorldPosition = targetToggle.TransformPoint(targetLocalPosition);
            marker.DOKill();
            float duration = GameEntry.Luban.Global.Data.LobbySelectionMarkerMoveDurationMs / 1000f;
            marker.DOMove(targetWorldPosition, duration)
                .SetEase(Ease.OutCubic)
                .OnComplete(() =>
                {
                    marker.SetParent(targetToggle, true);
                    marker.anchoredPosition = _markerInitialAnchoredPosition;
                });
        }

        private void RefreshHome()
        {
            SaveData save = GameEntry.Save.Data;
            if (save == null)
            {
                Log.Error("Save data is null, cannot refresh home.");
                return;
            }

            View.homeForm.Refresh(save);
        }

        private void RefreshWarehouse()
        {
            SaveData save = GameEntry.Save.Data;
            View.warehouseForm.Refresh(save != null ? save.mainWarehouse : null);
            RefreshStorageStatus();
        }

        private void RefreshStorageStatus()
        {
            SaveData save = GameEntry.Save.Data;
            int count = save != null && save.mainWarehouse != null ? save.mainWarehouse.Count : 0;
            View.storageFormatText.Set(count);
        }

        private void RefreshLoadout()
        {
            View.loadoutForm.Refresh();
        }
    }
}
