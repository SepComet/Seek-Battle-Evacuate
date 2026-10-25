using UnityEngine;

namespace SepCore.Exploration
{
    /// <summary>
    /// 虚拟角色输入实现。
    /// 允许通过代码直接赋值，专门用于 EditMode 单元测试、录像回放或脚本 AI 控制。
    /// </summary>
    public sealed class VirtualCharacterInput : ICharacterInput
    {
        private Vector2 _moveVector = Vector2.zero;

        public Vector2 MoveVector
        {
            get => _moveVector;
            set => _moveVector = value.sqrMagnitude > 1f ? value.normalized : value;
        }

        public bool IsInteracting { get; set; }

        public bool InteractTriggered { get; set; }

        public bool InteractReleased { get; set; }

        public bool HasInput => _moveVector.sqrMagnitude > 0.0001f || IsInteracting || InteractTriggered || InteractReleased;

        public void Reset()
        {
            _moveVector = Vector2.zero;
            IsInteracting = false;
            InteractTriggered = false;
            InteractReleased = false;
        }
    }
}
