using System;
using UnityEngine;

namespace SepCore.Exploration
{
    /// <summary>
    /// 2D 玩家角色探索控制器。
    /// 负责采样角色输入源（ICharacterInput）、驱动 2D 物理/位移运动、维护朝向，并派发交互事件。
    /// </summary>
    [DisallowMultipleComponent]
    public class PlayerCharacterController : MonoBehaviour
    {
        [SerializeField] private float _moveSpeed = 5.0f;
        [SerializeField] private bool _canMove = true;
        [SerializeField] private bool _autoFlipSprite = true;
        [SerializeField] private SpriteRenderer _spriteRenderer = null;
        [SerializeField] private Rigidbody2D _rigidbody2D = null;

        private ICharacterInput _inputSource = null;
        private Vector2 _facingDirection = Vector2.right;
        private Vector2 _currentVelocity = Vector2.zero;

        /// <summary>
        /// 基础移动速度（米/秒）。
        /// </summary>
        public float MoveSpeed
        {
            get => _moveSpeed;
            set => _moveSpeed = Mathf.Max(0f, value);
        }

        /// <summary>
        /// 是否允许移动（长按搜索物资点或处于战斗状态时置为 false 锁定移动）。
        /// </summary>
        public bool CanMove
        {
            get => _canMove;
            set
            {
                _canMove = value;
                if (!_canMove)
                {
                    _currentVelocity = Vector2.zero;
                    if (_rigidbody2D != null)
                    {
                        _rigidbody2D.velocity = Vector2.zero;
                    }
                }
            }
        }

        /// <summary>
        /// 是否根据左右移动方向自动翻转绑定的 SpriteRenderer。
        /// </summary>
        public bool AutoFlipSprite
        {
            get => _autoFlipSprite;
            set => _autoFlipSprite = value;
        }

        /// <summary>
        /// 当前角色是否正在移动。
        /// </summary>
        public bool IsMoving => _currentVelocity.sqrMagnitude > 0.0001f;

        /// <summary>
        /// 角色当前水平/主要朝向向量（归一化，默认为 (1, 0)）。
        /// </summary>
        public Vector2 FacingDirection => _facingDirection;

        /// <summary>
        /// 当前是否面向右侧。
        /// </summary>
        public bool IsFacingRight => _facingDirection.x >= 0f;

        /// <summary>
        /// 当前移动速度向量（米/秒）。
        /// </summary>
        public Vector2 CurrentVelocity => _currentVelocity;

        /// <summary>
        /// 当前绑定的输入源。未显式设置时，默认读取全局 CharacterInputBridge.DefaultInput。
        /// </summary>
        public ICharacterInput InputSource
        {
            get => _inputSource ?? CharacterInputBridge.DefaultInput;
            set => _inputSource = value;
        }

        /// <summary>
        /// 交互键点按触发事件。
        /// </summary>
        public event Action<PlayerCharacterController> OnInteractTriggered;

        /// <summary>
        /// 交互键松开触发事件。
        /// </summary>
        public event Action<PlayerCharacterController> OnInteractReleased;

        /// <summary>
        /// 交互键持续按住触发事件。
        /// </summary>
        public event Action<PlayerCharacterController> OnInteractHeld;

        private void Awake()
        {
            if (_rigidbody2D == null)
            {
                _rigidbody2D = GetComponent<Rigidbody2D>();
            }

            if (_spriteRenderer == null)
            {
                _spriteRenderer = GetComponentInChildren<SpriteRenderer>();
            }
        }

        private void Update()
        {
            Tick(Time.deltaTime);
        }

        private void FixedUpdate()
        {
            if (_rigidbody2D != null)
            {
                _rigidbody2D.velocity = _currentVelocity;
            }
        }

        /// <summary>
        /// 手动推进一帧输入与移动计算（支持在测试用例或自定义驱动中脱离 Unity 引擎周期独立调用）。
        /// </summary>
        /// <param name="deltaTime">本步时间步长（秒）。</param>
        public void Tick(float deltaTime)
        {
            ICharacterInput input = InputSource;
            if (input == null)
            {
                _currentVelocity = Vector2.zero;
                return;
            }

            // 处理交互触发（交互可能改变移动许可，例如进入/退出物资点长按搜索）
            if (input.InteractTriggered)
            {
                OnInteractTriggered?.Invoke(this);
            }

            if (input.InteractReleased)
            {
                OnInteractReleased?.Invoke(this);
            }

            if (input.IsInteracting)
            {
                OnInteractHeld?.Invoke(this);
            }

            Vector2 move = input.MoveVector;
            if (_canMove && move.sqrMagnitude > 0.0001f)
            {
                _currentVelocity = move * _moveSpeed;
                UpdateFacing(move);
            }
            else
            {
                _currentVelocity = Vector2.zero;
            }

            // 若无物理刚体，直接在非物理模式下修改 transform.position
            if (_rigidbody2D == null && _currentVelocity.sqrMagnitude > 0.0001f)
            {
                transform.position += (Vector3)(_currentVelocity * deltaTime);
            }
        }

        /// <summary>
        /// 为控制器显式指定输入源（例如注入虚拟输入源进行测试或切换操作模式）。
        /// </summary>
        public void SetInputSource(ICharacterInput inputSource)
        {
            _inputSource = inputSource;
        }

        private void UpdateFacing(Vector2 move)
        {
            if (Mathf.Abs(move.x) > 0.05f)
            {
                _facingDirection = new Vector2(Mathf.Sign(move.x), 0f);
                if (_autoFlipSprite && _spriteRenderer != null)
                {
                    _spriteRenderer.flipX = _facingDirection.x < 0f;
                }
            }
        }
    }
}
