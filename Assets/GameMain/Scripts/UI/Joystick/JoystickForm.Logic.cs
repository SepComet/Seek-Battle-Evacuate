using GameFramework.Event;
using SepCore.Base;
using SepCore.Exploration;
using UnityEngine;
using UnityEngine.UI;
using UnityGameFramework.Runtime;

namespace SepCore.UI
{
    /// <summary>
    /// 虚拟摇杆与交互按钮界面逻辑（手写 partial，与自动生成的 JoystickForm.cs 合并）。
    /// 负责将界面上的 VariableJoystick 与交互按钮组件包装为 JoystickCharacterInput，
    /// 并在界面打开和关闭时向全局 CharacterInputBridge 注册与注销。
    /// 监听领队当前可交互目标变更，实时切换交互按钮的可用状态与半透明置灰。
    /// </summary>
    public partial class JoystickForm : UGuiForm
    {
        private JoystickCharacterInput _inputSource = null;
        private UIInteractButtonListener _interactListener => View.interactListener;

        /// <summary>
        /// 当前界面提供的角色输入源实例。
        /// </summary>
        public ICharacterInput InputSource => _inputSource;

        protected override void OnInit(object userData)
        {
            base.OnInit(userData);
            
            _inputSource = new JoystickCharacterInput(
                View.joystick,
                () => _interactListener != null && _interactListener.IsHeld,
                () => _interactListener != null && _interactListener.TriggeredThisFrame,
                () => _interactListener != null && _interactListener.ReleasedThisFrame);
        }

        protected override void OnOpen(object userData)
        {
            base.OnOpen(userData);

            if (_inputSource != null)
            {
                CharacterInputBridge.RegisterUIInput(_inputSource);
            }

            // 初始默认为不可交互状态，等待领队探测器触发事件
            SetInteractButtonState(false);
            GameEntry.Event.Subscribe(LeaderInteractTargetChangedEventArgs.EventId, OnInteractTargetChanged);
        }

        protected override void OnClose(bool isShutdown, object userData)
        {
            GameEntry.Event.Unsubscribe(LeaderInteractTargetChangedEventArgs.EventId, OnInteractTargetChanged);
            CharacterInputBridge.UnregisterUIInput();

            base.OnClose(isShutdown, userData);
        }

        private void OnInteractTargetChanged(object sender, GameEventArgs e)
        {
            if (e is LeaderInteractTargetChangedEventArgs args)
            {
                SetInteractButtonState(args.HasTarget);
            }
        }

        private void SetInteractButtonState(bool canInteract)
        {
            if (_interactListener == null)
            {
                return;
            }

            _interactListener.enabled = canInteract;
            Image image = _interactListener.GetComponent<Image>();
            if (image != null)
            {
                image.color = canInteract ? Color.white : new Color(1f, 1f, 1f, 0.3f);
            }
        }
    }
}
