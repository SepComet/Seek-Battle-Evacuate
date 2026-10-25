using GameFramework.Event;
using SepCore.Base;

namespace SepCore.UI
{
    /// <summary>P1-M1 注册商店页面，买卖能力留待 P1-M3。</summary>
    public partial class ShopForm : UGuiForm
    {
        protected override void OnOpen(object userData)
        {
            base.OnOpen(userData);
            View.backText.Set();
            View.titleText.Set();
            View.detailText.Set();
            View.pageHeader.Open(UIForm.SerialId);
            GameEntry.Event.Subscribe(LobbyPageBackRequestedEventArgs.EventId, OnBackRequested);
        }

        protected override void OnClose(bool isShutdown, object userData)
        {
            View.pageHeader.Close();
            GameEntry.Event.Unsubscribe(LobbyPageBackRequestedEventArgs.EventId, OnBackRequested);
            base.OnClose(isShutdown, userData);
        }

        private void OnBackRequested(object sender, GameEventArgs e)
        {
            if (((LobbyPageBackRequestedEventArgs)e).SerialId == UIForm.SerialId) Close();
        }
    }
}
