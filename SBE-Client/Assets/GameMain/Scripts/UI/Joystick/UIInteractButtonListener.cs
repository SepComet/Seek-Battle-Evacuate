using UnityEngine;
using UnityEngine.EventSystems;

namespace SepCore.UI
{
    /// <summary>
    /// UI 交互按钮状态监听辅助组件。
    /// 挂载于 JoystickForm 的 interactButton 上，精准捕获按压（Down/Held）与抬起（Up）事件，
    /// 支持即时点击与持续长按（搜索物资点）两种交互语义。
    /// </summary>
    [DisallowMultipleComponent]
    public class UIInteractButtonListener : MonoBehaviour, IPointerDownHandler, IPointerUpHandler
    {
        private bool _isHeld = false;
        private bool _triggeredThisFrame = false;
        private bool _releasedThisFrame = false;

        /// <summary>
        /// 按钮当前是否处于被按住状态。
        /// </summary>
        public bool IsHeld => _isHeld;

        /// <summary>
        /// 本帧是否刚被按下。
        /// </summary>
        public bool TriggeredThisFrame => _triggeredThisFrame;

        /// <summary>
        /// 本帧是否刚被松开。
        /// </summary>
        public bool ReleasedThisFrame => _releasedThisFrame;

        public void OnPointerDown(PointerEventData eventData)
        {
            _isHeld = true;
            _triggeredThisFrame = true;
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            if (_isHeld)
            {
                _isHeld = false;
                _releasedThisFrame = true;
            }
        }

        private void LateUpdate()
        {
            _triggeredThisFrame = false;
            _releasedThisFrame = false;
        }

        private void OnDisable()
        {
            _isHeld = false;
            _triggeredThisFrame = false;
            _releasedThisFrame = false;
        }
    }
}
