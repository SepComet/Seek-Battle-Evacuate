using System.Collections.Generic;
using SepCore.Entity;
using UnityGameFramework.Runtime;
using UnityEngine;

namespace SepCore.Exploration
{
    /// <summary>
    /// 蛇形跟随编队控制器（运行时由领队实体挂载）。
    /// 每物理帧把领队位置记录到面包屑轨迹，并驱动所有随从严格沿轨迹按固定间距跟随；
    /// 探索暂停（战斗）期间统一锁定领队移动并冻结随从。
    /// </summary>
    [DisallowMultipleComponent]
    public class SnakePartyController : MonoBehaviour
    {
        /// <summary>
        /// 轨迹长度额外缓冲（米），避免裁剪到最外侧随从的追赶位置。
        /// </summary>
        private const float TrailMaxLengthBuffer = 1.0f;

        /// <summary>
        /// 朝向翻转的最小水平位移（米），与领队控制器规则一致。
        /// </summary>
        private const float FacingFlipThreshold = 0.05f;

        [SerializeField] private float _followSpacing = 1.0f;
        [SerializeField] private float _trailSampleDistance = 0.25f;

        private PlayerCharacterController _leader = null;
        private SnakeTrail _trail = null;
        private readonly List<Follower> _followers = new List<Follower>();
        private bool _wasExplorationPaused = false;
        private System.Func<bool> _isExplorationPausedProvider = null;

        /// <summary>
        /// 获取上一次物理计算记录的探索暂停状态。
        /// </summary>
        internal bool WasExplorationPaused => _wasExplorationPaused;

        /// <summary>
        /// 供测试注入或外部覆盖探索暂停检查。
        /// </summary>
        public void SetExplorationPausedProvider(System.Func<bool> provider)
        {
            _isExplorationPausedProvider = provider;
        }

        private bool IsExplorationPaused => _isExplorationPausedProvider != null
            ? _isExplorationPausedProvider()
            : (GameEntry.TurnBattle != null && GameEntry.TurnBattle.IsExplorationPaused);

        private sealed class Follower
        {
            public Rigidbody2D Rigidbody;
            public SpriteRenderer SpriteRenderer;
            public float ArcFromStart;
        }

        /// <summary>
        /// 绑定领队控制器与随从实体（按跟随顺序），并重置轨迹到领队当前位置。
        /// 由地图构建流程在全部玩家角色实体生成完成后调用。
        /// </summary>
        public void Bind(PlayerCharacterController leader, IReadOnlyList<PlayerCharacterLogic> retinues)
        {
            if (leader == null)
            {
                Log.Error("Snake party leader controller is invalid.");
                return;
            }

            if (retinues == null)
            {
                Log.Error("Snake party retinue list is invalid.");
                return;
            }

            if (_followSpacing <= 0f)
            {
                Log.Error("Snake party follow spacing must be positive, current value is '{0}'.", _followSpacing);
                return;
            }

            _leader = leader;
            _wasExplorationPaused = IsExplorationPaused;
            if (_wasExplorationPaused)
            {
                _leader.CanMove = false;
            }

            _followers.Clear();
            foreach (PlayerCharacterLogic retinue in retinues)
            {
                if (retinue == null)
                {
                    Log.Error("Snake party contains invalid retinue entity.");
                    continue;
                }

                Rigidbody2D retinueRigidbody = retinue.GetComponent<Rigidbody2D>();
                if (retinueRigidbody == null)
                {
                    Log.Error("Retinue entity '{0}' has no Rigidbody2D configured on prefab.", retinue.Id);
                    continue;
                }

                if (retinueRigidbody.bodyType != RigidbodyType2D.Kinematic)
                {
                    Log.Error("Retinue entity '{0}' Rigidbody2D body type must be Kinematic.", retinue.Id);
                    continue;
                }

                _followers.Add(new Follower
                {
                    Rigidbody = retinueRigidbody,
                    SpriteRenderer = retinue.GetComponentInChildren<SpriteRenderer>(),
                    ArcFromStart = 0f
                });
            }

            _trail = new SnakeTrail(_trailSampleDistance);
            _trail.Reset(_leader.transform.position);
        }

        /// <summary>
        /// 供测试或固定物理周期手动推进一帧跟随计算。
        /// </summary>
        public void Step(float deltaTime)
        {
            // 尚未绑定编队（领队 OnShow 与 Bind 之间可能相隔数帧），此时不接管任何行为
            if (_leader == null || _trail == null)
            {
                return;
            }

            // 探索暂停（战斗等）期间锁定领队移动并冻结随从；
            // 仅在暂停状态变化（离开暂停）时恢复 CanMove，避免正常探索中覆盖物资点搜索等逻辑置为 false 的移动锁定。
            bool explorationPaused = IsExplorationPaused;
            if (explorationPaused)
            {
                _wasExplorationPaused = true;
                _leader.CanMove = false;
                return;
            }

            if (_wasExplorationPaused)
            {
                _wasExplorationPaused = false;
                _leader.CanMove = true;
            }

            _trail.Append(_leader.transform.position);
            _trail.Trim(GetTrailMaxLength());

            for (int i = 0; i < _followers.Count; i++)
            {
                Follower follower = _followers[i];

                // 随从沿轨迹追赶，但与领队的弧长距离不小于自身序号对应的间距
                float targetArc = (i + 1) * _followSpacing;
                float maxArcFromStart = Mathf.Max(0f, _trail.HeadArc - targetArc);
                follower.ArcFromStart = Mathf.Min(
                    follower.ArcFromStart + _leader.MoveSpeed * deltaTime, maxArcFromStart);

                Vector2 targetPosition = _trail.GetPointFromStart(follower.ArcFromStart);
                UpdateFacing(follower, targetPosition);
                follower.Rigidbody.MovePosition(targetPosition);
            }
        }

        private void FixedUpdate()
        {
            Step(Time.fixedDeltaTime);
        }

        /// <summary>
        /// 按位移水平分量翻转随从朝向，规则与领队控制器一致。
        /// </summary>
        private static void UpdateFacing(Follower follower, Vector2 targetPosition)
        {
            if (follower.SpriteRenderer == null)
            {
                return;
            }

            float deltaX = targetPosition.x - follower.Rigidbody.position.x;
            if (Mathf.Abs(deltaX) > FacingFlipThreshold)
            {
                follower.SpriteRenderer.flipX = deltaX < 0f;
            }
        }

        /// <summary>
        /// 轨迹最大保留弧长：覆盖全部随从的间距外加采样与缓冲余量。
        /// </summary>
        private float GetTrailMaxLength()
        {
            return _followers.Count * _followSpacing + _trailSampleDistance + TrailMaxLengthBuffer;
        }
    }
}
