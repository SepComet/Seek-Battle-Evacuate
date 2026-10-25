using SepCore.Base;
using UnityEngine;
using UnityGameFramework.Runtime;

namespace SepCore.UI
{
    /// <summary>
    /// 道具拖拽顶层遮罩表单逻辑（手写 partial，与自动生成的 ItemDragOverlayForm.cs 合并）。
    /// 作为顶级表单挂载于 Overlay 组，提供全屏四角压暗视效与双侧旋转按钮能力。
    /// </summary>
    public partial class ItemDragOverlayForm : UGuiForm
    {
        protected override void OnOpen(object userData)
        {
            base.OnOpen(userData);

            // 防御性时序检查：若打开完成时当前拖拽已结束（如玩家极快速点击放下），立即关闭防残留
            if (!InventoryForm.IsAnyDragging)
            {
                Close(true);
                return;
            }

            // 拖拽辅助面板无需 0.3 秒渐显，立即呈现满透明度
            CanvasGroup canvasGroup = GetComponent<CanvasGroup>();
            if (canvasGroup != null)
            {
                canvasGroup.alpha = 1f;
            }

            View.rotateButtonLeft.onClick.AddListener(OnRotateButtonClicked);
            View.rotateButtonRight.onClick.AddListener(OnRotateButtonClicked);
        }

        protected override void OnClose(bool isShutdown, object userData)
        {
            View.rotateButtonLeft.onClick.RemoveListener(OnRotateButtonClicked);
            View.rotateButtonRight.onClick.RemoveListener(OnRotateButtonClicked);

            base.OnClose(isShutdown, userData);
        }

        private void OnRotateButtonClicked()
        {
            GameEntry.Event.Fire(this, ItemRotateRequestedEventArgs.Create());
        }
    }
}
