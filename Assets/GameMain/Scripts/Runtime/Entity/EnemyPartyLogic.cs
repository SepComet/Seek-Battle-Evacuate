using System;
using SepCore.Battle;
using SepCore.Definition;
using SepCore.Exploration;
using UnityEngine;
using UnityEngine.UI;
using UnityGameFramework.Runtime;

namespace SepCore.Entity
{
    /// <summary>
    /// 敌人队伍实体逻辑。
    /// 负责整合巡逻与追击移动（EnemyPartyMovementController）、正面 180° 视野与警惕值计算（EnemyAlertnessTracker），
    /// 头顶警惕 UI（Image.fillAmount 与变红），
    /// 以及与玩家队伍发生碰撞时触发回合制战斗（警惕未满先制，警惕已满普通）和战后处理。
    /// </summary>
    public sealed class EnemyPartyLogic : EntityBase
    {
        private EnemyPartyData _data;
        private EnemyPartyConfig _config;
        private EnemyPartyMovementController _movementController;
        private EnemyAlertnessTracker _alertnessTracker;
        private EnemyExplorationState _lastState;
        private bool _isInBattle;

        [SerializeField] private GameObject _alertUIRoot;
        [SerializeField] private Image _alertImage;
        [SerializeField] private Color _normalAlertColor = Color.white;
        [SerializeField] private Color _fullAlertColor = Color.red;

        /// <summary>
        /// 当前绑定的敌人队伍实体数据。
        /// </summary>
        public EnemyPartyData Data => _data;

        /// <summary>
        /// 当前敌人队伍对应的 Luban 静态配置。
        /// </summary>
        public EnemyPartyConfig Config => _config;

        /// <summary>
        /// 当前敌人警惕值与视线判定器。
        /// </summary>
        public EnemyAlertnessTracker AlertnessTracker => _alertnessTracker;

        /// <summary>
        /// 当前敌人队伍移动控制器。
        /// </summary>
        public EnemyPartyMovementController MovementController => _movementController;

        /// <summary>
        /// 当前是否正处于战斗或正在触发战斗。
        /// </summary>
        public bool IsInBattle => _isInBattle;

        /// <summary>
        /// 当前探索行为状态（巡逻、追击、丢失目标）。
        /// </summary>
        public EnemyExplorationState State => _alertnessTracker != null ? _alertnessTracker.State : EnemyExplorationState.Patrol;

        /// <summary>
        /// 当前警惕度归一化比例（0 ~ 1）。
        /// </summary>
        public float AlertFillAmount => _alertnessTracker != null ? _alertnessTracker.FillAmount : 0f;

        /// <summary>
        /// 警惕 UI 根节点 GameObject（通常为 Canvas）。
        /// </summary>
        public GameObject AlertUIRoot => _alertUIRoot;

        /// <summary>
        /// 警惕进度条 Image。
        /// </summary>
        public Image AlertImage => _alertImage;

        /// <summary>
        /// 未满警惕时的图标颜色。
        /// </summary>
        public Color NormalAlertColor
        {
            get => _normalAlertColor;
            set => _normalAlertColor = value;
        }

        /// <summary>
        /// 满警惕时的图标颜色（默认红色）。
        /// </summary>
        public Color FullAlertColor
        {
            get => _fullAlertColor;
            set => _fullAlertColor = value;
        }

        protected override void OnShow(object userData)
        {
            base.OnShow(userData);

            _data = userData as EnemyPartyData;
            if (_data == null)
            {
                Log.Error("Enemy party entity data is invalid.");
                return;
            }

            _config = GameEntry.Luban.Get<EnemyPartyConfig>(_data.EnemyPartyId);
            if (_config == null)
            {
                Log.Error("Enemy party config '{0}' is invalid.", _data.EnemyPartyId);
                return;
            }

            GlobalConfig globalConfig = GameEntry.Luban.Global != null ? GameEntry.Luban.Global.Data : null;
            float patrolSpeed = (globalConfig != null && globalConfig.PatrolSpeed > 0)
                ? globalConfig.PatrolSpeed / 1000f
                : 1.5f;
            float chaseSpeed = (globalConfig != null && globalConfig.ChaseSpeed > 0)
                ? globalConfig.ChaseSpeed / 1000f
                : 3.2f;

            _movementController = GetComponent<EnemyPartyMovementController>();
            if (_movementController == null)
            {
                _movementController = gameObject.AddComponent<EnemyPartyMovementController>();
            }
            _movementController.Initialize(_data.RoomMin, _data.RoomMax, patrolSpeed, chaseSpeed);

            ThreatLevelConfig threatConfig = _config.ThreatConfig_Ref ?? GameEntry.Luban.Get<ThreatLevelConfig>((int)_config.ThreatConfig);
            _alertnessTracker = new EnemyAlertnessTracker(threatConfig, globalConfig);

            _lastState = EnemyExplorationState.Patrol;
            _isInBattle = false;

            EnsureAlertImage();
            UpdateAlertUI();
        }

        protected override void OnHide(bool isShutdown, object userData)
        {
            if (_movementController != null)
            {
                _movementController.StopMovement();
            }

            _movementController = null;
            _alertnessTracker = null;
            _alertImage = null;
            _alertUIRoot = null;
            _data = null;
            _config = null;
            _isInBattle = false;
            base.OnHide(isShutdown, userData);
        }

        protected override void OnUpdate(float elapseSeconds, float realElapseSeconds)
        {
            base.OnUpdate(elapseSeconds, realElapseSeconds);

            if (_data == null || _movementController == null || _alertnessTracker == null)
            {
                return;
            }

            // 探索暂停、战斗进行中或自身在战斗中时，停止一切探索移动与视线更新
            if (_isInBattle || (GameEntry.TurnBattle != null && (GameEntry.TurnBattle.IsBattleActive || GameEntry.TurnBattle.IsExplorationPaused)))
            {
                _movementController.StopMovement();
                UpdateAlertUI();
                return;
            }

            PlayerCharacterLogic leader = PlayerCharacterLogic.Leader;
            bool hasLeader = leader != null && leader.Available;
            Vector2 playerPos = hasLeader ? (Vector2)leader.transform.position : Vector2.zero;
            int? playerRoomIndex = hasLeader ? leader.CurrentRoomIndex : null;
            bool isEscapeProtected = GameEntry.TurnBattle != null && GameEntry.TurnBattle.IsEscapeProtectionActive;

            Vector2 enemyPos = transform.position;

            if (hasLeader)
            {
                _alertnessTracker.UpdateDetection(
                    enemyPos,
                    playerPos,
                    playerRoomIndex,
                    _data.RoomIndex,
                    isEscapeProtected,
                    elapseSeconds);
            }
            else
            {
                _alertnessTracker.UpdateDetection(
                    enemyPos,
                    enemyPos,
                    null,
                    _data.RoomIndex,
                    isEscapeProtected,
                    elapseSeconds);
            }

            // 状态机转换时触发对应控制器的重置动作
            if (_lastState != _alertnessTracker.State)
            {
                if (_alertnessTracker.State == EnemyExplorationState.Patrol)
                {
                    _movementController.ResetToPatrol();
                }
                _lastState = _alertnessTracker.State;
            }

            switch (_alertnessTracker.State)
            {
                case EnemyExplorationState.Patrol:
                    _movementController.UpdatePatrol(elapseSeconds);
                    break;

                case EnemyExplorationState.Pursuit:
                    if (hasLeader)
                    {
                        _movementController.UpdatePursuit(playerPos);
                    }
                    else
                    {
                        _movementController.StopMovement();
                    }
                    break;

                case EnemyExplorationState.LostTarget:
                    _movementController.StopMovement();
                    break;
            }

            UpdateAlertUI();
        }

        private void OnCollisionEnter2D(Collision2D collision)
        {
            HandleCollision(collision.gameObject);
        }

        private void OnCollisionStay2D(Collision2D collision)
        {
            HandleCollision(collision.gameObject);
        }

        private void OnTriggerEnter2D(Collider2D other)
        {
            HandleCollision(other.gameObject);
        }

        private void HandleCollision(GameObject other)
        {
            if (other == null || _isInBattle || _data == null)
            {
                return;
            }

            if (GameEntry.TurnBattle == null || GameEntry.TurnBattle.IsBattleActive || GameEntry.TurnBattle.IsExplorationPaused)
            {
                return;
            }

            if (GameEntry.TurnBattle.IsEscapeProtectionActive)
            {
                return;
            }

            PlayerCharacterLogic player = other.GetComponentInParent<PlayerCharacterLogic>() ?? other.GetComponent<PlayerCharacterLogic>();
            if (player == null || !player.IsLeader)
            {
                return;
            }

            // 碰触玩家队伍，根据警惕度是否满值判定先制还是普通战斗
            bool isPreemptive = _alertnessTracker == null || !_alertnessTracker.IsAlertFull;
            _isInBattle = true;
            _movementController?.StopMovement();

            BattleEncounter encounter = new BattleEncounter(Entity.Id, _data.EnemyPartyId, isPreemptive);
            Log.Info("Enemy party '{0}' triggered battle with player (preemptive: {1}).", Entity.Id, isPreemptive);

            bool started = GameEntry.TurnBattle.TryStartBattle(encounter, OnBattleCompleted);
            if (!started)
            {
                _isInBattle = false;
            }
        }

        private void OnBattleCompleted(BattleResult result)
        {
            if (result == null)
            {
                _isInBattle = false;
                return;
            }

            if (result.Outcome == BattleOutcomeType.Victory)
            {
                _isInBattle = false;
                GameEntry.Entity.HideEntity(this);
            }
            else
            {
                _isInBattle = false;
                _alertnessTracker?.ResetAlertness();
                _movementController?.ResetToPatrol();
                _lastState = EnemyExplorationState.Patrol;
                UpdateAlertUI();
            }
        }

        /// <summary>
        /// 设置警惕进度条 Image 引用（主要供测试或外部显式绑定）。
        /// </summary>
        public void SetAlertImage(Image image)
        {
            _alertImage = image;
            if (_alertImage != null && _alertUIRoot == null)
            {
                _alertUIRoot = _alertImage.transform.parent != null
                    ? _alertImage.transform.parent.gameObject
                    : _alertImage.gameObject;
            }
            UpdateAlertUI();
        }

        /// <summary>
        /// 设置警惕 UI 根节点与进度条 Image（主要供测试或外部显式绑定）。
        /// </summary>
        public void SetAlertUI(GameObject uiRoot, Image alertImage)
        {
            _alertUIRoot = uiRoot;
            _alertImage = alertImage;
            UpdateAlertUI();
        }

        private void EnsureAlertImage()
        {
            if (_alertImage != null)
            {
                if (_alertUIRoot == null)
                {
                    Transform canvasTransform = transform.Find("Canvas");
                    _alertUIRoot = canvasTransform != null ? canvasTransform.gameObject : _alertImage.transform.parent?.gameObject;
                }
                return;
            }

            Transform canvas = transform.Find("Canvas");
            if (canvas != null)
            {
                _alertUIRoot = canvas.gameObject;
                Transform alertTransform = canvas.Find("alert");
                if (alertTransform != null)
                {
                    _alertImage = alertTransform.GetComponent<Image>();
                }
            }

            if (_alertImage == null)
            {
                Image[] images = GetComponentsInChildren<Image>(true);
                for (int i = 0; i < images.Length; i++)
                {
                    if (images[i].gameObject.name.Equals("alert", StringComparison.OrdinalIgnoreCase))
                    {
                        _alertImage = images[i];
                        if (_alertUIRoot == null)
                        {
                            _alertUIRoot = images[i].transform.parent != null
                                ? images[i].transform.parent.gameObject
                                : images[i].gameObject;
                        }
                        break;
                    }
                }
            }

            if (_alertUIRoot == null && _alertImage != null)
            {
                _alertUIRoot = _alertImage.transform.parent != null
                    ? _alertImage.transform.parent.gameObject
                    : _alertImage.gameObject;
            }
        }

        /// <summary>
        /// 刷新警惕 UI 的显示状态、fillAmount 与颜色。
        /// 警惕值为 0 时隐藏整个 UI；大于 0 时显示，未满时为 NormalAlertColor（默认白），满警惕时切换为 FullAlertColor（红）。
        /// </summary>
        public void UpdateAlertUI()
        {
            float currentAlertness = _alertnessTracker != null ? _alertnessTracker.CurrentAlertness : 0f;
            bool isVisible = currentAlertness > 0.0001f;

            if (_alertUIRoot != null)
            {
                if (_alertUIRoot.activeSelf != isVisible)
                {
                    _alertUIRoot.SetActive(isVisible);
                }
            }
            else if (_alertImage != null)
            {
                if (_alertImage.gameObject.activeSelf != isVisible)
                {
                    _alertImage.gameObject.SetActive(isVisible);
                }
            }

            if (_alertImage == null)
            {
                return;
            }

            if (!isVisible)
            {
                _alertImage.fillAmount = 0f;
                _alertImage.color = _normalAlertColor;
                return;
            }

            float fill = _alertnessTracker != null ? _alertnessTracker.FillAmount : 0f;
            bool isFull = _alertnessTracker != null && _alertnessTracker.IsAlertFull;

            _alertImage.fillAmount = fill;
            _alertImage.color = isFull ? _fullAlertColor : _normalAlertColor;
        }
    }
}
