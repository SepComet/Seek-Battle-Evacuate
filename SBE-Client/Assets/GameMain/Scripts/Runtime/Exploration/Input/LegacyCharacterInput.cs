using UnityEngine;

namespace SepCore.Exploration
{
    /// <summary>
    /// 基于 Unity 旧版输入系统（UnityEngine.Input）的键鼠输入实现。
    /// 默认使用 WASD / 方向键控制移动，E 键控制交互。
    /// </summary>
    public sealed class LegacyCharacterInput : ICharacterInput
    {
        private readonly KeyCode _interactKey;

        public KeyCode InteractKey => _interactKey;

        public LegacyCharacterInput(KeyCode interactKey = KeyCode.E)
        {
            _interactKey = interactKey;
        }

        public Vector2 MoveVector
        {
            get
            {
                float h = Input.GetAxisRaw("Horizontal");
                float v = Input.GetAxisRaw("Vertical");
                Vector2 move = new Vector2(h, v);
                return move.sqrMagnitude > 1f ? move.normalized : move;
            }
        }

        public bool IsInteracting => Input.GetKey(_interactKey);

        public bool InteractTriggered => Input.GetKeyDown(_interactKey);

        public bool InteractReleased => Input.GetKeyUp(_interactKey);

        public bool HasInput => MoveVector.sqrMagnitude > 0.0001f || IsInteracting || InteractTriggered || InteractReleased;
    }
}
