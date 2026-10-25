namespace SepCore.Exploration
{
    /// <summary>
    /// 撞敌请求的节流状态（纯逻辑，不含 Unity 依赖，便于 EditMode 直测）。
    /// 碰撞回调由 OnCollisionStay2D 每物理帧驱动，而 encounter 是一次网络往返：
    /// 请求在途时不得重复发起，被拒绝后退避一段时间再允许重试，
    /// 否则超时/拒绝会在玩家贴着敌人时形成每秒数十次的刷屏请求。
    /// </summary>
    public sealed class EncounterRequestGate
    {
        /// <summary>被拒绝（超时/断线/服务器拒绝）后的最短重试间隔（秒）。</summary>
        public const float RejectionCooldownSeconds = 1f;

        private float _cooldownRemaining;
        private bool _inFlight;

        /// <summary>是否有一次 encounter 请求在途。</summary>
        public bool InFlight => _inFlight;

        /// <summary>剩余退避时间（秒）；0 表示不在冷却中。</summary>
        public float CooldownRemaining => _cooldownRemaining;

        /// <summary>当前是否允许发起新的 encounter 请求（无在途且不在冷却）。</summary>
        public bool CanRequest => !_inFlight && _cooldownRemaining <= 0f;

        /// <summary>推进时间；由宿主在每帧或每次碰撞回调里驱动。</summary>
        public void Tick(float deltaTime)
        {
            if (_cooldownRemaining <= 0f)
            {
                return;
            }

            _cooldownRemaining = deltaTime >= _cooldownRemaining ? 0f : _cooldownRemaining - deltaTime;
        }

        /// <summary>
        /// 抢占一次请求；已不允许时返回 false（调用方据此直接放弃本次碰撞）。
        /// </summary>
        public bool TryBegin()
        {
            if (!CanRequest)
            {
                return false;
            }

            _inFlight = true;
            return true;
        }

        /// <summary>请求成功并已进入战斗：清空在途标志（战斗态由实体自身的 _isInBattle 承担）。</summary>
        public void Complete()
        {
            _inFlight = false;
        }

        /// <summary>请求未成功：清空在途标志并进入退避冷却。</summary>
        public void Reject()
        {
            _inFlight = false;
            _cooldownRemaining = RejectionCooldownSeconds;
        }

        /// <summary>重置（实体隐藏/重新显示时调用）。</summary>
        public void Reset()
        {
            _inFlight = false;
            _cooldownRemaining = 0f;
        }
    }
}
