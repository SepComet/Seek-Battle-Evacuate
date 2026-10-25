using System.Collections.Generic;
using GameFramework.Event;
using SepCore.Base;
using SepCore.Definition;
using SepCore.Net;
using UnityEngine;
using UnityGameFramework.Runtime;

namespace SepCore.UI
{
    /// <summary>
    /// 主仓库界面逻辑（手写 partial，与自动生成的 WarehouseForm.cs 合并）。
    /// 顶级仓库页负责读取存档并向下分发数据；
    /// 监听格子点击事件，驱动选中高亮与详情界面刷新。
    /// InventoryForm 与 ItemDetailsForm 保持能力型子组件。
    /// </summary>
    public partial class WarehouseForm : UGuiForm
    {
        private bool _eventBound = false;

        protected override void OnOpen(object userData)
        {
            base.OnOpen(userData);
            View.backText.Set();
            View.pageHeader.Open(UIForm.SerialId);
            GameEntry.Event.Subscribe(LobbyPageBackRequestedEventArgs.EventId, OnBackRequested);
            GameEntry.Net.Sync.SnapshotApplied += OnSnapshotApplied;
            Refresh(GameEntry.Save.Data.mainWarehouse);
        }

        protected override void OnClose(bool isShutdown, object userData)
        {
            GameEntry.Net.Sync.SnapshotApplied -= OnSnapshotApplied;
            GameEntry.Event.Unsubscribe(LobbyPageBackRequestedEventArgs.EventId, OnBackRequested);
            View.pageHeader.Close();
            UnbindEvent();
            if (!isShutdown)
            {
                GameEntry.Save.SaveDirty();
                GameEntry.Net.NotifyEconomyChanged();
            }
            base.OnClose(isShutdown, userData);
        }

        private void OnSnapshotApplied(NetSnapshot snapshot)
        {
            if (Visible) Refresh(GameEntry.Save.Data.mainWarehouse);
        }

        private void OnBackRequested(object sender, GameEventArgs e)
        {
            if (((LobbyPageBackRequestedEventArgs)e).SerialId == UIForm.SerialId) Close();
        }

        protected override void OnResume()
        {
            base.OnResume();
            Refresh(GameEntry.Save.Data.mainWarehouse);
        }

        /// <summary>
        /// 用主仓库内容刷新整个仓库界面（物品列表）。
        /// </summary>
        public void Refresh(IReadOnlyList<GridItemStack> stacks)
        {
            EnsureEventBound();
            View.inventoryPanelForm.RefreshList(stacks);
        }

        private void EnsureEventBound()
        {
            if (_eventBound)
            {
                return;
            }

            _eventBound = true;
            GameEntry.Event.Subscribe(WarehouseSlotItemClickEventArgs.EventId, OnWarehouseSlotItemClick);
        }

        public void UnbindEvent()
        {
            if (!_eventBound)
            {
                return;
            }

            _eventBound = false;
            GameEntry.Event.Unsubscribe(WarehouseSlotItemClickEventArgs.EventId, OnWarehouseSlotItemClick);
        }

        private void OnDisable()
        {
            UnbindEvent();
        }

        private void OnWarehouseSlotItemClick(object sender, GameEventArgs e)
        {
            if (this == null)
            {
                return;
            }

            WarehouseSlotItemClickEventArgs ne = (WarehouseSlotItemClickEventArgs)e;
            int slotId = ne.SlotId;

            View.inventoryPanelForm.SetSelectedSlot(slotId);

            int itemId = View.inventoryPanelForm.GetItemIdAtSlot(slotId);
            if (itemId != 0)
            {
                View.itemDetailsPanelForm.Refresh(itemId);
            }
        }
    }
}
