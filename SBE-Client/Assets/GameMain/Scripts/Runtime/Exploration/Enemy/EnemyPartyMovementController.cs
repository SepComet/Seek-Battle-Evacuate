using System;
using UnityEngine;

namespace SepCore.Exploration
{
    /// <summary>
    /// 敌人队伍移动与空间约束控制器。
    /// 负责在所属房间矩形内巡逻（随机漫游与停顿、超时与卡死自恢复）、在满警惕时追击领队，
    /// 并严格将位移约束在所属房间内（无法冲出房间）。
    /// </summary>
    [DisallowMultipleComponent]
    public class EnemyPartyMovementController : MonoBehaviour
    {
        private const float DefaultPadding = 1.2f;
        private const float ArrivalThreshold = 0.25f;
        private const float MaxPatrolLegDuration = 5.0f;
        private const float StuckCheckInterval = 0.8f;
        private const float MinMoveDistanceForStuckCheck = 0.08f;
        private const float BoundaryMargin = 0.35f;

        [SerializeField] private float _patrolSpeed = 1.5f;
        [SerializeField] private float _chaseSpeed = 3.2f;

        private Rigidbody2D _rigidbody2D;
        private SpriteRenderer _spriteRenderer;
        private Vector2 _roomMin;
        private Vector2 _roomMax;
        private Vector2 _facingDirection = Vector2.right;
        private Vector2 _currentVelocity = Vector2.zero;

        private Vector2 _patrolTarget;
        private float _waitTimer;
        private bool _isWaiting;
        private bool _initialized;

        private float _legTimer;
        private float _stuckTimer;
        private Vector2 _lastStuckCheckPos;

        public Vector2 FacingDirection => _facingDirection;
        public float PatrolSpeed => _patrolSpeed;
        public float ChaseSpeed => _chaseSpeed;
        public Vector2 PatrolTarget => _patrolTarget;
        public bool IsWaiting => _isWaiting;

        private void Awake()
        {
            _rigidbody2D = GetComponent<Rigidbody2D>();
            _spriteRenderer = GetComponentInChildren<SpriteRenderer>();
        }

        private void FixedUpdate()
        {
            if (_rigidbody2D != null)
            {
                _rigidbody2D.velocity = _currentVelocity;
            }
        }

        /// <summary>
        /// 初始化移动约束与房间包围盒。
        /// </summary>
        public void Initialize(Vector2 roomMin, Vector2 roomMax, float patrolSpeed = 1.5f, float chaseSpeed = 3.2f)
        {
            if (_rigidbody2D == null)
            {
                _rigidbody2D = GetComponent<Rigidbody2D>();
            }

            if (_spriteRenderer == null)
            {
                _spriteRenderer = GetComponentInChildren<SpriteRenderer>();
            }

            if (roomMin == roomMax)
            {
                Vector2 currentPos = transform.position;
                _roomMin = currentPos - new Vector2(2f, 2f);
                _roomMax = currentPos + new Vector2(2f, 2f);
            }
            else
            {
                _roomMin = roomMin;
                _roomMax = roomMax;
            }

            _patrolSpeed = patrolSpeed;
            _chaseSpeed = chaseSpeed;

            Vector2 spawnPos = transform.position;
            _lastStuckCheckPos = spawnPos;
            _legTimer = 0f;
            _stuckTimer = 0f;
            _isWaiting = false;
            _waitTimer = 0f;
            _patrolTarget = PickRandomPointInRoom(spawnPos);
            _initialized = true;
        }

        /// <summary>
        /// 驱动房间内随机巡逻移动（含到达判定、单段耗时保护与撞墙/障碍物卡死自恢复）。
        /// </summary>
        public void UpdatePatrol(float deltaTime)
        {
            if (!_initialized || deltaTime <= 0f)
            {
                return;
            }

            Vector2 currentPos = transform.position;

            // 1. 停顿等待中
            if (_isWaiting)
            {
                _waitTimer -= deltaTime;
                if (_waitTimer <= 0f)
                {
                    _isWaiting = false;
                    _legTimer = 0f;
                    _stuckTimer = 0f;
                    _lastStuckCheckPos = currentPos;
                    _patrolTarget = PickRandomPointInRoom(currentPos);
                }

                StopMovement();
                return;
            }

            // 2. 判定是否已到达目标点
            Vector2 toTarget = _patrolTarget - currentPos;
            if (toTarget.sqrMagnitude <= ArrivalThreshold * ArrivalThreshold)
            {
                TriggerWait();
                return;
            }

            // 3. 超时保护：单段移动超过最大耗时仍未到达，主动放弃并重新选点
            _legTimer += deltaTime;
            if (_legTimer >= MaxPatrolLegDuration)
            {
                TriggerWait();
                return;
            }

            // 4. 防卡死检测：若处于移动中但位置被物理墙壁/障碍物阻挡未发生位移，自动重新选点
            _stuckTimer += deltaTime;
            if (_stuckTimer >= StuckCheckInterval)
            {
                float movedDistSqr = (currentPos - _lastStuckCheckPos).sqrMagnitude;
                if (movedDistSqr < MinMoveDistanceForStuckCheck * MinMoveDistanceForStuckCheck)
                {
                    TriggerWait(UnityEngine.Random.Range(0.5f, 1.0f));
                    return;
                }

                _lastStuckCheckPos = currentPos;
                _stuckTimer = 0f;
            }

            // 5. 计算移动方向并施加边界安全约束
            Vector2 moveDir = toTarget.normalized;
            moveDir = ApplyBoundaryConstraint(currentPos, moveDir);

            if (moveDir.sqrMagnitude < 0.001f)
            {
                // 已贴在房间死角边缘，无法继续朝向目标点移动
                TriggerWait();
                return;
            }

            _facingDirection = moveDir;
            UpdateSpriteFlip(moveDir.x);

            _currentVelocity = moveDir.normalized * _patrolSpeed;
            if (_rigidbody2D != null)
            {
                _rigidbody2D.velocity = _currentVelocity;
            }
            else
            {
                transform.position += (Vector3)(_currentVelocity * deltaTime);
            }
        }

        /// <summary>
        /// 驱动向玩家位置加速冲刺追击，位移严格限制在所属房间内。
        /// </summary>
        public void UpdatePursuit(Vector2 playerPosition)
        {
            if (!_initialized)
            {
                return;
            }

            Vector2 currentPos = transform.position;
            Vector2 toPlayer = playerPosition - currentPos;

            if (toPlayer.sqrMagnitude > 0.0001f)
            {
                Vector2 moveDir = toPlayer.normalized;
                moveDir = ApplyBoundaryConstraint(currentPos, moveDir);

                _facingDirection = moveDir;
                UpdateSpriteFlip(moveDir.x);

                _currentVelocity = moveDir.normalized * _chaseSpeed;
                if (_rigidbody2D != null)
                {
                    _rigidbody2D.velocity = _currentVelocity;
                }
                else
                {
                    transform.position += (Vector3)(_currentVelocity * Time.deltaTime);
                }
            }
            else
            {
                StopMovement();
            }
        }

        /// <summary>
        /// 停止当前移动。
        /// </summary>
        public void StopMovement()
        {
            _currentVelocity = Vector2.zero;
            if (_rigidbody2D != null)
            {
                _rigidbody2D.velocity = Vector2.zero;
            }
        }

        /// <summary>
        /// 重置为巡逻状态并重新挑选目标点。
        /// </summary>
        public void ResetToPatrol()
        {
            StopMovement();
            _isWaiting = false;
            _waitTimer = 0f;
            _legTimer = 0f;
            _stuckTimer = 0f;
            Vector2 currentPos = transform.position;
            _lastStuckCheckPos = currentPos;
            _patrolTarget = PickRandomPointInRoom(currentPos);
        }

        private void TriggerWait(float customWaitDuration = -1f)
        {
            _isWaiting = true;
            _waitTimer = customWaitDuration > 0f ? customWaitDuration : UnityEngine.Random.Range(1.0f, 2.2f);
            _legTimer = 0f;
            _stuckTimer = 0f;
            StopMovement();
        }

        private Vector2 ApplyBoundaryConstraint(Vector2 currentPos, Vector2 moveDir)
        {
            if (currentPos.x >= _roomMax.x - BoundaryMargin && moveDir.x > 0f)
            {
                moveDir.x = 0f;
            }

            if (currentPos.x <= _roomMin.x + BoundaryMargin && moveDir.x < 0f)
            {
                moveDir.x = 0f;
            }

            if (currentPos.y >= _roomMax.y - BoundaryMargin && moveDir.y > 0f)
            {
                moveDir.y = 0f;
            }

            if (currentPos.y <= _roomMin.y + BoundaryMargin && moveDir.y < 0f)
            {
                moveDir.y = 0f;
            }

            return moveDir;
        }

        /// <summary>
        /// 在房间包围盒内挑选随机漫游点，留出充足 padding 并在可能的情况下避免挑在脚下原地打转。
        /// </summary>
        private Vector2 PickRandomPointInRoom(Vector2 currentPos)
        {
            float minX = _roomMin.x + DefaultPadding;
            float maxX = _roomMax.x - DefaultPadding;
            float minY = _roomMin.y + DefaultPadding;
            float maxY = _roomMax.y - DefaultPadding;

            if (minX >= maxX)
            {
                minX = (_roomMin.x + _roomMax.x) * 0.5f;
                maxX = minX;
            }

            if (minY >= maxY)
            {
                minY = (_roomMin.y + _roomMax.y) * 0.5f;
                maxY = minY;
            }

            Vector2 bestCandidate = new Vector2((minX + maxX) * 0.5f, (minY + maxY) * 0.5f);
            // 尝试挑选距离当前位置 >= 1.5 米的目标点，确保巡逻有可见位移
            for (int i = 0; i < 6; i++)
            {
                Vector2 candidate = new Vector2(UnityEngine.Random.Range(minX, maxX), UnityEngine.Random.Range(minY, maxY));
                if ((candidate - currentPos).sqrMagnitude >= 1.5f * 1.5f || minX == maxX)
                {
                    return candidate;
                }
                bestCandidate = candidate;
            }

            return bestCandidate;
        }

        private void UpdateSpriteFlip(float moveX)
        {
            if (_spriteRenderer != null)
            {
                if (moveX > 0.05f)
                {
                    _spriteRenderer.flipX = false;
                }
                else if (moveX < -0.05f)
                {
                    _spriteRenderer.flipX = true;
                }
            }
        }
    }
}
