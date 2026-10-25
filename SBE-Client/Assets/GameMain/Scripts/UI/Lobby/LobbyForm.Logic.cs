using GameFramework.Event;
using SepCore.Definition;
using UnityGameFramework.Runtime;

namespace SepCore.UI
{
    /// <summary>大厅只负责打开独立功能页；功能页关闭后恢复大厅。</summary>
    public partial class LobbyForm : UGuiForm
    {
        private int? _openingSerialId;
        private bool _navigationPending;

        protected override void OnOpen(object userData)
        {
            base.OnOpen(userData);
            _openingSerialId = null;
            _navigationPending = false;
            View.pageHeader.Open(UIForm.SerialId);
            View.titleText.Set();
            View.subtitleText.Set();
            View.expeditionButtonText.Set();
            View.rosterButtonText.Set();
            View.supplyButtonText.Set();
            View.warehouseButtonText.Set();
            View.expeditionButton.onClick.AddListener(OpenExpedition);
            View.rosterButton.onClick.AddListener(OpenRoster);
            View.warehouseButton.onClick.AddListener(OpenWarehouse);
            GameEntry.Event.Subscribe(OpenUIFormFailureEventArgs.EventId, OnOpenPageFailure);
            SetNavigationEnabled(true);
        }

        protected override void OnResume()
        {
            base.OnResume();
            if (_openingSerialId.HasValue && GameEntry.UI.IsLoadingUIForm(_openingSerialId.Value))
            {
                SetNavigationEnabled(false);
                return;
            }
            _openingSerialId = null;
            _navigationPending = false;
            SetNavigationEnabled(true);
            View.pageHeader.RefreshStatus();
        }

        protected override void OnClose(bool isShutdown, object userData)
        {
            View.pageHeader.Close();
            View.expeditionButton.onClick.RemoveListener(OpenExpedition);
            View.rosterButton.onClick.RemoveListener(OpenRoster);
            View.warehouseButton.onClick.RemoveListener(OpenWarehouse);
            GameEntry.Event.Unsubscribe(OpenUIFormFailureEventArgs.EventId, OnOpenPageFailure);
            _openingSerialId = null;
            _navigationPending = false;
            base.OnClose(isShutdown, userData);
        }

        private void OpenExpedition() => OpenPage(UIFormType.HomeForm);
        private void OpenRoster() => OpenPage(UIFormType.LoadoutForm);
        private void OpenWarehouse() => OpenPage(UIFormType.WarehouseForm);

        private void OpenPage(UIFormType page)
        {
            if (_navigationPending || IsTransitioning) return;
            _navigationPending = true;
            SetNavigationEnabled(false);
            PlayExitTransition(() =>
            {
                _openingSerialId = GameEntry.UI.OpenUIForm(page);
                if (!_openingSerialId.HasValue) RestoreNavigation();
            });
        }

        private void SetNavigationEnabled(bool enabled)
        {
            View.expeditionButton.interactable = enabled;
            View.rosterButton.interactable = enabled;
            View.warehouseButton.interactable = enabled;
            // P1-M1 只注册入口，交易能力在 P1-M3 开放。
            View.supplyButton.interactable = false;
        }

        private void OnOpenPageFailure(object sender, GameEventArgs e)
        {
            var failure = (OpenUIFormFailureEventArgs)e;
            if (_openingSerialId != failure.SerialId) return;
            RestoreNavigation();
        }

        private void RestoreNavigation()
        {
            _openingSerialId = null;
            _navigationPending = false;
            SetNavigationEnabled(true);
            PlayEnterTransition();
        }
    }
}
