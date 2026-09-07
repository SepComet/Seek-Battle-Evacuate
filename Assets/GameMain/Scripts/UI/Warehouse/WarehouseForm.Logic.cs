using System.Collections.Generic;
using GameFramework.Event;
using SepCore.Base;
using SepCore.Definition;
using UnityEngine;
using UnityGameFramework.Runtime;

namespace SepCore.UI
{
    /// <summary>
    /// 主仓库界面逻辑（手写 partial，与自动生成的 WarehouseForm.cs 合并）。
    /// 只提供仓库界面能力，数据由调用方（LobbyForm）传入；
    /// 监听格子点击事件，驱动选中高亮与详情界面刷新。
    /// 注意：本表单作为嵌套表单使用，UGF 生命周期不会被框架调用，
    /// 事件订阅改为幂等式（EnsureEventBound）。
    /// </summary>
    public partial class WarehouseForm : UGuiForm
    {
        private bool _eventBound = false;

        /// <summary>
        /// 用主仓库内容刷新整个仓库界面（物品列表）。
        /// </summary>
        public void Refresh(IReadOnlyList<ItemStack> stacks)
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