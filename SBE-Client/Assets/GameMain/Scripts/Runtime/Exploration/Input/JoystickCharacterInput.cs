using System;
using UnityEngine;

namespace SepCore.Exploration
{
    /// <summary>
    /// UI 虚拟摇杆与交互按钮输入实现。
    /// 包装 Joystick 组件与交互状态提供源。
    /// </summary>
    public sealed class JoystickCharacterInput : ICharacterInput
    {
        private readonly Joystick _joystick;
        private readonly Func<bool> _isInteractingGetter;
        private readonly Func<bool> _interactTriggeredGetter;
        private readonly Func<bool> _interactReleasedGetter;

        public JoystickCharacterInput(
            Joystick joystick,
            Func<bool> isInteractingGetter = null,
            Func<bool> interactTriggeredGetter = null,
            Func<bool> interactReleasedGetter = null)
        {
            _joystick = joystick;
            _isInteractingGetter = isInteractingGetter ?? (() => false);
            _interactTriggeredGetter = interactTriggeredGetter ?? (() => false);
            _interactReleasedGetter = interactReleasedGetter ?? (() => false);
        }

        public Vector2 MoveVector
        {
            get
            {
                if (_joystick == null)
                {
                    return Vector2.zero;
                }

                Vector2 dir = _joystick.Direction;
                return dir.sqrMagnitude > 1f ? dir.normalized : dir;
            }
        }

        public bool IsInteracting => _isInteractingGetter();

        public bool InteractTriggered => _interactTriggeredGetter();

        public bool InteractReleased => _interactReleasedGetter();

        public bool HasInput => MoveVector.sqrMagnitude > 0.0001f || IsInteracting || InteractTriggered || InteractReleased;
    }
}
