using GameFramework.Event;
using SepCore.Base;
using SepCore.Definition;
using SepCore.Net;
using UnityGameFramework.Runtime;

namespace SepCore.UI
{
    /// <summary>
    /// 主界面（战备）逻辑（手写 partial，与自动生成的 HomeForm.cs 合并）。
    /// 顶级战备页，编排子表单 SquadForm 与 DeploymentForm 的刷新。
    /// </summary>
    public partial class HomeForm : UGuiForm
    {
        private bool _listenersBound = false;

        protected override void OnOpen(object userData)
        {
            base.OnOpen(userData);
            View.backText.Set();
            View.pageHeader.Open(UIForm.SerialId);
            GameEntry.Event.Subscribe(LobbyPageBackRequestedEventArgs.EventId, OnBackRequested);
            GameEntry.Net.Warehouse.SnapshotApplied += OnSnapshotApplied;
            Refresh(GameEntry.Save.Data);
        }

        protected override void OnClose(bool isShutdown, object userData)
        {
            GameEntry.Net.Warehouse.SnapshotApplied -= OnSnapshotApplied;
            GameEntry.Event.Unsubscribe(LobbyPageBackRequestedEventArgs.EventId, OnBackRequested);
            View.pageHeader.Close();
            UnbindListeners();
            if (!isShutdown)
            {
                GameEntry.Save.SaveDirty();
                GameEntry.Net.NotifyEconomyChanged();
            }
            base.OnClose(isShutdown, userData);
        }

        private void OnSnapshotApplied(NetSnapshot snapshot)
        {
            if (Visible) Refresh(GameEntry.Save.Data, false);
        }

        private void OnBackRequested(object sender, GameEventArgs e)
        {
            if (((LobbyPageBackRequestedEventArgs)e).SerialId == UIForm.SerialId) Close();
        }

        protected override void OnResume()
        {
            base.OnResume();
            Refresh(GameEntry.Save.Data, false);
        }

        /// <summary>
        /// 刷新主界面（战备编队角色列表与出战面板）。
        /// </summary>
        public void Refresh(SaveData save, bool initializeParty = true)
        {
            if (save == null)
            {
                Log.Error("Save data is null, cannot refresh HomeForm.");
                return;
            }

            EnsureListenersBound();
            // 仅首次打开播种默认队伍；空队快照刷新保持编辑中的空队，避免 ack 后自动重新入队。
            View.squadForm.RefreshCharacterList(save.characters, initializeParty: initializeParty);
            View.deploymentForm.Refresh();
        }

        private void EnsureListenersBound()
        {
            if (_listenersBound)
            {
                return;
            }

            _listenersBound = true;
            View.squadForm.OnPartyChanged += OnSquadPartyChanged;
        }

        private void OnSquadPartyChanged()
        {
            View.deploymentForm.Refresh();
        }

        private void OnDestroy()
        {
            UnbindListeners();
        }

        private void UnbindListeners()
        {
            if (_listenersBound)
            {
                _listenersBound = false;
                View.squadForm.OnPartyChanged -= OnSquadPartyChanged;
            }
        }
    }
}
