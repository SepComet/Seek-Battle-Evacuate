using SepCore.Definition;
using UnityEngine;

namespace SepCore.Exploration
{
    /// <summary>
    /// 敌人警惕值与视线判定纯逻辑计算器。
    /// 负责圆形（360°）视距判定、距离分段警惕增长、超出视距衰减；
    /// 满警惕进入追击后，除非玩家离开所属房间或处于逃跑保护期，否则无视距离持续保持追逐；
    /// 玩家离开房间或逃跑保护时触发 3 秒脱战倒计时并衰减。
    /// </summary>
    public sealed class EnemyAlertnessTracker
    {
        private readonly ThreatLevelConfig _threatConfig;
        private readonly float _alertMax;
        private readonly float _maxViewDistance;
        private readonly float _alertDecayPerSecond;
        private readonly float _enemyLoseTargetDuration;

        private float _currentAlertness;
        private float _lostTargetTimer;
        private EnemyExplorationState _state;

        public EnemyAlertnessTracker(ThreatLevelConfig threatConfig, GlobalConfig globalConfig)
        {
            _threatConfig = threatConfig;
            _alertMax = globalConfig != null ? globalConfig.AlertMax : 1000f;
            _enemyLoseTargetDuration = globalConfig != null ? globalConfig.EnemyLoseTargetMs / 1000f : 3.0f;

            if (_threatConfig != null)
            {
                _maxViewDistance = _threatConfig.MaxViewDistanceMilli / 1000f;
                _alertDecayPerSecond = _threatConfig.AlertDecayPerSecondMilli;
            }
            else
            {
                _maxViewDistance = 6.0f;
                _alertDecayPerSecond = 200f;
            }

            _currentAlertness = 0f;
            _lostTargetTimer = 0f;
            _state = EnemyExplorationState.Patrol;
        }

        public float AlertMax => _alertMax;
        public float MaxViewDistance => _maxViewDistance;
        public float AlertDecayPerSecond => _alertDecayPerSecond;
        public float EnemyLoseTargetDuration => _enemyLoseTargetDuration;

        public float CurrentAlertness => _currentAlertness;
        public float FillAmount => _alertMax > 0f ? Mathf.Clamp01(_currentAlertness / _alertMax) : 0f;
        public bool IsAlertFull => _currentAlertness >= _alertMax;
        public float LostTargetTimer => _lostTargetTimer;
        public bool IsTargetLost => _lostTargetTimer >= _enemyLoseTargetDuration;
        public EnemyExplorationState State => _state;

        /// <summary>
        /// 每帧更新视线探测与警惕值计算（圆形 360° 视野）。
        /// 满警惕进入追击后，只要玩家未离开所属房间即持续保持追逐（逃跑保护时除外）。
        /// </summary>
        public void UpdateDetection(
            Vector2 enemyPos,
            Vector2 playerPos,
            int? playerRoomIndex,
            int enemyRoomIndex,
            bool isEscapeProtectionActive,
            float deltaTime)
        {
            if (deltaTime <= 0f)
            {
                return;
            }

            // 1. 逃跑保护期生效时：不可发现玩家，警惕值仅衰减不增长，若在追击/丢失状态中则倒计时脱战
            if (isEscapeProtectionActive)
            {
                DecayAlertness(deltaTime);
                HandleLostTargetTracking(deltaTime);
                return;
            }

            bool playerInSameRoom = playerRoomIndex.HasValue && playerRoomIndex.Value == enemyRoomIndex;

            // 2. 处于追击状态（警惕值满）：只要玩家未离开所属房间，无视视距持续保持追逐
            if (_state == EnemyExplorationState.Pursuit)
            {
                if (playerInSameRoom)
                {
                    _currentAlertness = _alertMax;
                    _lostTargetTimer = 0f;
                    return;
                }

                // 玩家离开所属房间，进入脱战丢失倒计时
                DecayAlertness(deltaTime);
                HandleLostTargetTracking(deltaTime);
                return;
            }

            // 3. 非追击状态（巡逻 / 丢失目标中）：玩家不在同房间仅衰减并累计丢失
            if (!playerInSameRoom)
            {
                DecayAlertness(deltaTime);
                HandleLostTargetTracking(deltaTime);
                return;
            }

            // 4. 同房间内的圆形（360°）视距判定
            Vector2 toPlayer = playerPos - enemyPos;
            float distance = toPlayer.magnitude;

            if (distance <= _maxViewDistance)
            {
                // 处于圆形视野范围内：重置丢失计时，按距离分段增长
                _lostTargetTimer = 0f;
                float growthRate = GetGrowthRate(distance);
                _currentAlertness = Mathf.Min(_alertMax, _currentAlertness + growthRate * deltaTime);

                if (_currentAlertness >= _alertMax)
                {
                    _state = EnemyExplorationState.Pursuit;
                }
            }
            else
            {
                // 超出圆形最大视距：衰减并累计丢失目标计时
                DecayAlertness(deltaTime);
                HandleLostTargetTracking(deltaTime);
            }
        }

        private void DecayAlertness(float deltaTime)
        {
            _currentAlertness = Mathf.Max(0f, _currentAlertness - _alertDecayPerSecond * deltaTime);
        }

        private void HandleLostTargetTracking(float deltaTime)
        {
            if (_state == EnemyExplorationState.Pursuit || _state == EnemyExplorationState.LostTarget)
            {
                _lostTargetTimer += deltaTime;
                if (_lostTargetTimer >= _enemyLoseTargetDuration)
                {
                    // 超过 3 秒彻底脱战，返回巡逻
                    _state = EnemyExplorationState.Patrol;
                    _lostTargetTimer = 0f;
                }
                else
                {
                    _state = EnemyExplorationState.LostTarget;
                }
            }
        }

        /// <summary>
        /// 根据距离从 DistanceBands 查找匹配的每秒增长率。
        /// </summary>
        public float GetGrowthRate(float distance)
        {
            if (_threatConfig == null || _threatConfig.DistanceBands == null || _threatConfig.DistanceBands.Count == 0)
            {
                return _alertMax * 0.5f;
            }

            // DistanceBands 按距离阈值升序排列
            foreach (AlertDistanceBand band in _threatConfig.DistanceBands)
            {
                float bandMaxDist = band.MaxDistanceMilli / 1000f;
                if (distance <= bandMaxDist)
                {
                    return band.AlertPerSecondMilli;
                }
            }

            // 若距离在最后一段之内或超出，取最后一段
            return _threatConfig.DistanceBands[_threatConfig.DistanceBands.Count - 1].AlertPerSecondMilli;
        }

        /// <summary>
        /// 重置警惕值与状态（例如战斗结束后或瞬移）。
        /// </summary>
        public void ResetAlertness()
        {
            _currentAlertness = 0f;
            _lostTargetTimer = 0f;
            _state = EnemyExplorationState.Patrol;
        }
    }
}
