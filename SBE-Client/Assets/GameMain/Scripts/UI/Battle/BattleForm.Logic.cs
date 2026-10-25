using System.Collections.Generic;
using System.Linq;
using DG.Tweening;
using SepCore.Base;
using SepCore.Battle;
using SepCore.Definition;
using SepCore.Exploration;
using UnityEngine;

namespace SepCore.UI
{
    /// <summary>
    /// 战斗界面逻辑（手写 partial，与自动生成的 BattleForm.cs 合并）。
    /// UI 只读取 BattleViewState / BattleStep / BattleResult，不持有 BattleRuntime 或 BattleUnit；
    /// 玩家指令经 GameEntry.TurnBattle.SubmitCommand 提交，推进结果统一回填界面。
    /// 我方卡片按玩家单位数显示/隐藏；敌人槽和回合顺序槽按当前战局从模板动态实例化。
    /// </summary>
    public partial class BattleForm : UGuiForm
    {
        private const float SlotWidth = 100f;
        private const float SlotHeight = 180f;
        private const float SlotSpacing = 28f;
        private const float StepX = SlotWidth + SlotSpacing;

        private BattleResult _result;
        private int _displayedRound;
        private readonly Dictionary<int, BattleTurnSlotItem> _turnSlotMap = new Dictionary<int, BattleTurnSlotItem>();
        private readonly List<int> _currentDisplayOrder = new List<int>();
        private readonly List<BattleEnemySlotItem> _enemySlots = new List<BattleEnemySlotItem>();
        private BattleActionType _pendingCommandType = BattleActionType.None;
        private int _pendingActionConfigId;
        private int _displayedActorId;
        private int _selectedTargetUnitId;
        private bool _isEnteringAnimation;
        private bool _isFirstTurnSlotsEntry = true;
        private Tween _turnSlotsFadeTween;
        private Tween _delayedEnterCall;
        private Sequence _victorySequence;


        protected override void OnInit(object userData)
        {
            base.OnInit(userData);

            EnsureLayoutGroupDisabled();

            // 道具首版禁用；逃跑 M5 接入
            View.itemButton.interactable = false;
        }

        protected override void OnOpen(object userData)
        {
            base.OnOpen(userData);

            EnsureLayoutGroupDisabled();

            CharacterInputBridge.DisableInput(InputDisableReason.Battle);

            // UIForm 实例复用：每次打开都重新注册按钮监听（OnClose 会移除）
            View.attackButton.onClick.AddListener(OnAttackButtonClick);
            View.skillButton.onClick.AddListener(OnSkillButtonClick);
            View.itemButton.onClick.AddListener(OnItemButtonClick);
            View.escapeButton.onClick.AddListener(OnEscapeButtonClick);

            if (GameEntry.TurnBattle != null)
            {
                GameEntry.TurnBattle.SetStepListener(ApplyStep);
            }

            _result = null;
            _displayedRound = 0;
            _pendingCommandType = BattleActionType.None;
            _pendingActionConfigId = 0;
            _displayedActorId = 0;
            _selectedTargetUnitId = 0;
            _turnSlotMap.Clear();
            _currentDisplayOrder.Clear();
            _enemySlots.Clear();
            _isFirstTurnSlotsEntry = true;

            BattleEnterAnimationParams enterParams = userData as BattleEnterAnimationParams;
            BattleViewState initialViewState =
                GameEntry.TurnBattle != null ? GameEntry.TurnBattle.GetViewState() : null;

            _isEnteringAnimation = enterParams != null && initialViewState != null;

            Refresh(initialViewState);

            if (_isEnteringAnimation)
            {
                StartEnterAnimation(enterParams, initialViewState);
            }
        }

        private void EnsureLayoutGroupDisabled()
        {
            UnityEngine.UI.HorizontalLayoutGroup layoutGroup =
                View.turnSlotsRoot.GetComponent<UnityEngine.UI.HorizontalLayoutGroup>();
            if (layoutGroup != null)
            {
                layoutGroup.enabled = false;
            }
        }

        private void StartEnterAnimation(BattleEnterAnimationParams enterParams, BattleViewState view)
        {
            _isEnteringAnimation = true;
            GameEntry.TurnBattle?.SetAutoAdvancePaused(true);

            // 1. 禁用所有操作面板按钮与文本提示
            View.attackButton.interactable = false;
            View.skillButton.interactable = false;
            View.escapeButton.interactable = false;
            View.itemButton.interactable = false;
            View.currentActorText.text = string.Empty;
            View.tipText.text = string.Empty;

            // 2. 回合顺位栏先隐藏
            CanvasGroup turnGroup = View.turnSlotsRoot.gameObject.GetOrAddComponent<CanvasGroup>();
            _turnSlotsFadeTween?.Kill();
            turnGroup.alpha = 0f;

            GlobalConfig global = GameEntry.Luban.Global.Data;
            float moveInterval = global.BattleInMoveIntervalMs / 1000f;
            float inDuration = global.BattleInDurationMs / 1000f;

            // 3. 玩家卡片错峰飞入
            int activePlayerCount = 0;
            for (int i = 0; i < 4; i++)
            {
                BattleActorCardItem card = GetPlayerCard(i);
                if (!card.gameObject.activeSelf) continue;
                
                Vector3 startPos;
                if (enterParams.PlayerScreenPositions != null && i < enterParams.PlayerScreenPositions.Count)
                {
                    startPos = enterParams.PlayerScreenPositions[i];
                }
                else if (enterParams.PlayerScreenPositions != null && enterParams.PlayerScreenPositions.Count > 0)
                {
                    startPos = enterParams.PlayerScreenPositions[0];
                }
                else
                {
                    startPos = enterParams.EnemyScreenPosition;
                }

                card.PlayEnterAnimation(startPos, i * moveInterval);
                activePlayerCount++;
            }

            // 4. 敌人槽位错峰飞入（从同一个敌人队伍地图点扇出）
            for (int i = 0; i < _enemySlots.Count; i++)
            {
                _enemySlots[i].PlayEnterAnimation(enterParams.EnemyScreenPosition, 0.1f + i * moveInterval);
            }

            // 5. 计算全员就位时长并注册完成回调
            float maxDuration = Mathf.Max(activePlayerCount * moveInterval, 0.1f + _enemySlots.Count * moveInterval) + inDuration + 0.2f;

            _delayedEnterCall?.Kill();
            _delayedEnterCall = DOVirtual.DelayedCall(maxDuration, () => { OnEnterAnimationComplete(view); });
        }

        private void OnEnterAnimationComplete(BattleViewState view)
        {
            _isEnteringAnimation = false;
            _delayedEnterCall = null;

            // 顺位栏淡入并播放错峰滑入动效
            PlayTurnSlotsEnterAnimation(view);

            // 恢复操作面板
            RefreshActionPanel(view);

            // 解除自动推进暂停（若敌人先手或眩晕，正式开始调度）
            GameEntry.TurnBattle?.SetAutoAdvancePaused(false);
        }

        protected override void OnClose(bool isShutdown, object userData)
        {
            CharacterInputBridge.EnableInput(InputDisableReason.Battle);

            if (GameEntry.TurnBattle != null)
            {
                GameEntry.TurnBattle.SetStepListener(null);
                GameEntry.TurnBattle.SetAutoAdvancePaused(false);
            }

            View.attackButton.onClick.RemoveListener(OnAttackButtonClick);
            View.skillButton.onClick.RemoveListener(OnSkillButtonClick);
            View.itemButton.onClick.RemoveListener(OnItemButtonClick);
            View.escapeButton.onClick.RemoveListener(OnEscapeButtonClick);

            _delayedEnterCall?.Kill();
            _delayedEnterCall = null;
            _turnSlotsFadeTween?.Kill();
            _turnSlotsFadeTween = null;
            _victorySequence?.Kill();
            _victorySequence = null;
            _isEnteringAnimation = false;

            for (int i = 0; i < 4; i++)
            {
                BattleActorCardItem card = GetPlayerCard(i);
                card.ResetVisualState();
            }

            for (int i = 0; i < _enemySlots.Count; i++)
            {
                _enemySlots[i]?.ResetVisualState();
            }

            foreach (var kvp in _turnSlotMap)
            {
                if (kvp.Value != null)
                {
                    kvp.Value.transform.DOKill();
                    CanvasGroup slotCg = kvp.Value.GetComponent<CanvasGroup>();
                    if (slotCg != null)
                    {
                        slotCg.DOKill();
                    }
                }
            }

            _turnSlotMap.Clear();
            _currentDisplayOrder.Clear();
            _isFirstTurnSlotsEntry = true;

            ClearSlots(View.turnSlotsRoot, View.battleTurnSlotTemplate.transform);

            CanvasGroup turnGroup = View.turnSlotsRoot.GetComponent<CanvasGroup>();
            if (turnGroup != null)
            {
                turnGroup.DOKill();
                turnGroup.alpha = 1f;
            }

            CanvasGroup formGroup = GetComponent<CanvasGroup>();
            if (formGroup != null)
            {
                formGroup.DOKill();
                formGroup.alpha = 1f;
            }

            _pendingCommandType = BattleActionType.None;
            _pendingActionConfigId = 0;
            _displayedActorId = 0;
            _selectedTargetUnitId = 0;

            base.OnClose(isShutdown, userData);
        }

        private void OnAttackButtonClick()
        {
            if (_isEnteringAnimation)
            {
                return;
            }

            BattleViewState view = GameEntry.TurnBattle.GetViewState();
            if (view == null || view.CurrentActorUnitId == 0)
            {
                return;
            }

            BattleUnitView actor = FindUnit(view, view.CurrentActorUnitId);
            if (actor == null || actor.Faction != BattleFactionType.Player || BattleUnitViewHelper.IsStunned(actor))
            {
                return;
            }

            // 二次确认与取消机制：若已经处于普通攻击待命状态，再次点击普通攻击按钮取消（不攻击）
            if (_pendingCommandType == BattleActionType.Attack)
            {
                ClearPendingAction();
                RefreshActionPanel(view);
                return;
            }

            int attackActionId = FindAttackActionId(view);
            if (attackActionId == 0)
            {
                return;
            }

            _pendingCommandType = BattleActionType.Attack;
            _pendingActionConfigId = attackActionId;
            _selectedTargetUnitId = 0;
            RefreshActionPanel(view);
        }

        private void OnSkillButtonClick()
        {
            if (_isEnteringAnimation)
            {
                return;
            }

            BattleViewState view = GameEntry.TurnBattle.GetViewState();
            if (view == null || view.CurrentActorUnitId == 0)
            {
                return;
            }

            BattleUnitView actor = FindUnit(view, view.CurrentActorUnitId);
            if (actor == null || actor.Faction != BattleFactionType.Player || BattleUnitViewHelper.IsStunned(actor))
            {
                return;
            }

            // 二次确认与取消机制：若已经处于技能待命状态，再次点击技能按钮取消（不放技能）
            if (_pendingCommandType == BattleActionType.Skill)
            {
                ClearPendingAction();
                RefreshActionPanel(view);
                return;
            }

            int skillActionId = FindSkillActionId(view);
            if (skillActionId == 0)
            {
                return;
            }

            BattleActionConfig config = GameEntry.Luban.Get<BattleActionConfig>(skillActionId);
            if (config == null || actor.CurrentMp < config.MpCost)
            {
                return;
            }

            _pendingCommandType = BattleActionType.Skill;
            _pendingActionConfigId = skillActionId;
            _selectedTargetUnitId = 0;
            RefreshActionPanel(view);
        }

        private void ClearPendingAction()
        {
            _pendingCommandType = BattleActionType.None;
            _pendingActionConfigId = 0;
            _selectedTargetUnitId = 0;
        }

        /// <summary>
        /// 两步确认：首次点击合法目标只选中高亮，再次点击同一目标确认释放；
        /// 点击非法目标不改变选中、不消耗行动。
        /// </summary>
        private void ConfirmOrSelectTarget(BattleViewState view, BattleUnitView actor, int targetUnitId,
            List<int> submitTargets)
        {
            if (_selectedTargetUnitId == targetUnitId)
            {
                SubmitPendingCommand(actor.BattleUnitId, _pendingCommandType, _pendingActionConfigId, submitTargets);
                return;
            }

            _selectedTargetUnitId = targetUnitId;
            Refresh(view);
        }

        private void OnEnemySlotClick(int targetEnemyUnitId)
        {
            if (_isEnteringAnimation || _pendingCommandType == BattleActionType.None || _pendingActionConfigId == 0)
            {
                return;
            }

            BattleViewState view = GameEntry.TurnBattle.GetViewState();
            if (view == null || view.CurrentActorUnitId == 0)
            {
                return;
            }

            BattleUnitView actor = FindUnit(view, view.CurrentActorUnitId);
            if (actor == null || actor.Faction != BattleFactionType.Player)
            {
                return;
            }

            BattleActionConfig action = GameEntry.Luban.Get<BattleActionConfig>(_pendingActionConfigId);
            if (action == null)
            {
                return;
            }

            if (action.TargetType == BattleTargetType.SingleEnemy)
            {
                BattleUnitView target = FindUnit(view, targetEnemyUnitId);
                if (target == null || target.Faction != BattleFactionType.Enemy || target.IsDefeated ||
                    target.IsEscaped)
                {
                    return;
                }

                ConfirmOrSelectTarget(view, actor, targetEnemyUnitId, new List<int> { targetEnemyUnitId });
            }
            else if (action.TargetType == BattleTargetType.AllEnemies)
            {
                // 全体目标：点击任意存活敌人选中，再次点击确认释放，目标由内核自动展开
                BattleUnitView target = FindUnit(view, targetEnemyUnitId);
                if (target == null || target.Faction != BattleFactionType.Enemy || target.IsDefeated ||
                    target.IsEscaped)
                {
                    return;
                }

                ConfirmOrSelectTarget(view, actor, targetEnemyUnitId, new List<int>());
            }
        }

        private void OnPlayerCardClick(int targetPlayerUnitId)
        {
            if (_isEnteringAnimation || _pendingCommandType == BattleActionType.None)
            {
                return;
            }

            BattleViewState view = GameEntry.TurnBattle.GetViewState();
            if (view == null || view.CurrentActorUnitId == 0)
            {
                return;
            }

            BattleUnitView actor = FindUnit(view, view.CurrentActorUnitId);
            if (actor == null || actor.Faction != BattleFactionType.Player)
            {
                return;
            }

            // 逃跑确认：点击自身角色卡片释放，无目标无配置
            if (_pendingCommandType == BattleActionType.Escape)
            {
                if (targetPlayerUnitId == actor.BattleUnitId)
                {
                    SubmitPendingCommand(actor.BattleUnitId, BattleActionType.Escape, 0, new List<int>());
                }

                return;
            }

            if (_pendingActionConfigId == 0)
            {
                return;
            }

            BattleActionConfig action = GameEntry.Luban.Get<BattleActionConfig>(_pendingActionConfigId);
            if (action == null)
            {
                return;
            }

            if (action.TargetType == BattleTargetType.SingleAlly)
            {
                BattleUnitView target = FindUnit(view, targetPlayerUnitId);
                // SingleAlly 可以选择队友或施法者自身
                if (target == null || target.Faction != BattleFactionType.Player || target.IsDefeated ||
                    target.IsEscaped)
                {
                    return;
                }

                ConfirmOrSelectTarget(view, actor, targetPlayerUnitId, new List<int> { targetPlayerUnitId });
            }
            else if (action.TargetType == BattleTargetType.AllAllies)
            {
                BattleUnitView target = FindUnit(view, targetPlayerUnitId);
                if (target == null || target.Faction != BattleFactionType.Player || target.IsDefeated ||
                    target.IsEscaped)
                {
                    return;
                }

                ConfirmOrSelectTarget(view, actor, targetPlayerUnitId, new List<int>());
            }
            else if (action.TargetType == BattleTargetType.Self)
            {
                if (targetPlayerUnitId == actor.BattleUnitId)
                {
                    ConfirmOrSelectTarget(view, actor, targetPlayerUnitId, new List<int> { actor.BattleUnitId });
                }
            }
        }

        private void SubmitPendingCommand(int actorUnitId, BattleActionType commandType, int actionConfigId,
            List<int> targets)
        {
            ClearPendingAction();
            BattleStep step = GameEntry.TurnBattle.SubmitCommand(new BattleCommand(
                actorUnitId,
                commandType,
                actionConfigId,
                targets)
            );
            ApplyStep(step);
        }

        private void ApplyStep(BattleStep step)
        {
            if (step == null)
            {
                return;
            }

            ClearPendingAction();

            if (step.Result != null)
            {
                _result = step.Result;
            }

            Refresh(step.View);
            OverlayEventStates(step.Events);

            if (step.Result != null)
            {
                if (step.Result.Outcome == BattleOutcomeType.TotalDefeat)
                {
                    StartCoroutine(CloseAfterTotalDefeatDelay(step.Result));
                }
                else if (step.Result.Outcome == BattleOutcomeType.Victory)
                {
                    PlayVictorySequence(step.Result);
                }
                else
                {
                    StartCoroutine(CloseAfterResultDelay(step.Result));
                }
            }
        }

        /// <summary>
        /// 播放战斗胜利退出序列：存活队员轻弹庆祝、敌方槽位微幅上浮消散淡出、顺位栏淡出、全屏渐隐后关闭战斗。
        /// </summary>
        private void PlayVictorySequence(BattleResult result)
        {
            _victorySequence?.Kill();
            _victorySequence = DOTween.Sequence();

            // 1. 锁定所有操作面板按钮与交互
            View.attackButton.interactable = false;
            View.skillButton.interactable = false;
            View.escapeButton.interactable = false;
            View.itemButton.interactable = false;
            View.currentActorText.text = GetOutcomeText(result.Outcome);
            View.tipText.text = string.Empty;

            // 2. 存活玩家卡片错峰轻弹跳跃庆祝
            for (int i = 0; i < 4; i++)
            {
                BattleActorCardItem card = GetPlayerCard(i);
                if (card.gameObject.activeSelf)
                {
                    card.PlayVictoryCelebrate(i * 0.05f);
                }
            }

            // 3. 所有敌人槽位微幅上浮消散淡出
            for (int i = 0; i < _enemySlots.Count; i++)
            {
                _enemySlots[i].PlayVictoryFadeOut(i * 0.06f);
            }

            // 4. 顶部回合顺位栏同步淡出
            CanvasGroup turnGroup = View.turnSlotsRoot.GetComponent<CanvasGroup>();
            if (turnGroup != null)
            {
                _turnSlotsFadeTween?.Kill();
                _turnSlotsFadeTween = turnGroup.DOFade(0f, 0.35f);
            }

            // 5. 保持展示并短暂停留，给玩家战果反馈（淡出约 0.4s + 停留 0.35s = 0.75s）
            _victorySequence.AppendInterval(0.75f);

            // 6. 整个 BattleForm 渐隐后正式关闭战斗
            CanvasGroup formGroup = GetComponent<CanvasGroup>();
            if (formGroup != null)
            {
                _victorySequence.Append(formGroup.DOFade(0f, 0.3f));
            }

            _victorySequence.OnComplete(() =>
            {
                _victorySequence = null;
                if (_result == result && GameEntry.TurnBattle != null && GameEntry.TurnBattle.IsBattleActive)
                {
                    GameEntry.TurnBattle.CloseBattle();
                }
            });
        }

        /// <summary>
        /// 全灭结果展示停留后关闭战斗并抛出全灭事件，通知主流程进入失败结算。
        /// </summary>
        private System.Collections.IEnumerator CloseAfterTotalDefeatDelay(BattleResult result)
        {
            float delaySeconds = GameEntry.Luban.Global.Data.BattleResultDisplayDelayMs / 1000f;
            yield return new WaitForSecondsRealtime(delaySeconds);

            if (_result == result && GameEntry.TurnBattle != null && GameEntry.TurnBattle.IsBattleActive)
            {
                GameEntry.TurnBattle.CloseBattle();
            }

            GameEntry.Event.Fire(this, BattleTotalDefeatEventArgs.Create());
        }

        /// <summary>
        /// 非全灭结果展示停留后自行关闭战斗（恢复探索）；全灭停留显示失败等待外部处理。
        /// </summary>
        private System.Collections.IEnumerator CloseAfterResultDelay(BattleResult result)
        {
            float delaySeconds = GameEntry.Luban.Global.Data.BattleResultDisplayDelayMs / 1000f;
            yield return new WaitForSecondsRealtime(delaySeconds);

            if (_result == result && GameEntry.TurnBattle != null && GameEntry.TurnBattle.IsBattleActive)
            {
                GameEntry.TurnBattle.CloseBattle();
            }
        }

        /// <summary>
        /// 轮到眩晕单位时飘一次状态名（新行动者且处于眩晕才触发，同一步重复刷新不重复飘）。
        /// </summary>
        private void MaybeSpawnStunFloat(BattleViewState view)
        {
            int actorId = view.CurrentActorUnitId;
            bool isNewActor = actorId != 0 && actorId != _displayedActorId;
            _displayedActorId = actorId;
            if (!isNewActor)
            {
                return;
            }

            BattleUnitView actor = FindUnit(view, actorId);
            if (actor != null && BattleUnitViewHelper.IsStunned(actor))
            {
                SpawnCardFloatText(actorId, BattleUnitViewHelper.GetStateText(BattleStateType.Stun));
            }
        }

        /// <summary>
        /// 按本次推进事件生成飘字：目前只飘 HP 变化数字（-12/+25）。
        /// 状态名不在施加时飘，只在轮到该单位时由 MaybeSpawnStunFloat 飘一次。
        /// 每个事件独立生成一个，不互斥、不被后续刷新打断，淡出后自毁。
        /// </summary>
        private void OverlayEventStates(IReadOnlyList<BattleEvent> events)
        {
            foreach (BattleEvent battleEvent in events)
            {
                int hpDelta = battleEvent.AfterHp - battleEvent.BeforeHp;
                if (hpDelta == 0)
                {
                    continue;
                }

                SpawnCardFloatText(battleEvent.TargetUnitId, hpDelta > 0 ? "+" + hpDelta : hpDelta.ToString());
            }
        }

        private void SpawnCardFloatText(int unitId, string text)
        {
            for (int i = 0; i < 4; i++)
            {
                BattleActorCardItem card = GetPlayerCard(i);
                if (card.gameObject.activeSelf && card.CurrentUnitId == unitId)
                {
                    card.SpawnFloatText(text);
                    return;
                }
            }

            foreach (BattleEnemySlotItem slot in _enemySlots)
            {
                if (slot.CurrentUnitId == unitId)
                {
                    slot.SpawnFloatText(text);
                    return;
                }
            }
        }

        private void Refresh(BattleViewState view)
        {
            if (view == null)
            {
                return;
            }

            RefreshPlayerCards(view);
            RefreshEnemySlots(view);
            RefreshTurnSlots(view);
            RefreshActionPanel(view);
            MaybeSpawnStunFloat(view);
        }

        private void RefreshPlayerCards(BattleViewState view)
        {
            int index = 0;
            foreach (BattleUnitView unit in view.Units)
            {
                if (unit.Faction != BattleFactionType.Player)
                {
                    continue;
                }

                BattleActorCardItem card = GetPlayerCard(index);
                card.gameObject.SetActive(true);
                card.SetOnClick(OnPlayerCardClick);
                card.SetUnit(unit, unit.BattleUnitId == view.CurrentActorUnitId,
                    unit.BattleUnitId == _selectedTargetUnitId);

                index++;
            }

            for (int i = index; i < 4; i++)
            {
                BattleActorCardItem card = GetPlayerCard(i);
                card.gameObject.SetActive(false);
            }
        }

        private void RefreshEnemySlots(BattleViewState view)
        {
            BattleEnemySlotItem template = View.battleEnemySlotTemplate;

            List<BattleUnitView> enemies = new List<BattleUnitView>();
            foreach (BattleUnitView unit in view.Units)
            {
                if (unit.Faction == BattleFactionType.Enemy)
                {
                    enemies.Add(unit);
                }
            }

            // 敌人数量在一场战斗内固定：数量变化（新开战斗/规模不同）时才重建，之后原地复用
            if (_enemySlots.Count != enemies.Count)
            {
                template.gameObject.SetActive(false);
                ClearSlots(View.enemySlotsRoot, template.transform);
                _enemySlots.Clear();
                foreach (BattleUnitView unit in enemies)
                {
                    BattleEnemySlotItem slot = Instantiate(template, View.enemySlotsRoot);
                    slot.gameObject.SetActive(true);
                    _enemySlots.Add(slot);
                }
            }

            for (int i = 0; i < _enemySlots.Count; i++)
            {
                _enemySlots[i].SetOnClick(OnEnemySlotClick);
                _enemySlots[i].SetEnemy(enemies[i], enemies[i].BattleUnitId == view.CurrentActorUnitId,
                    enemies[i].BattleUnitId == _selectedTargetUnitId);
            }
        }

        private void RefreshTurnSlots(BattleViewState view)
        {
            if (view == null || view.DisplayOrder == null)
            {
                return;
            }

            BattleTurnSlotItem template = View.battleTurnSlotTemplate;
            if (template == null)
            {
                return;
            }

            // 如果开场动画正在进行中，静默预备好槽位坐标（置于屏幕右侧），等待 OnEnterAnimationComplete 统一错峰飞入
            if (_isEnteringAnimation)
            {
                BuildOrSyncSlots(view);
                int totalCount = view.DisplayOrder.Count;
                for (int i = 0; i < totalCount; i++)
                {
                    int unitId = view.DisplayOrder[i];
                    if (_turnSlotMap.TryGetValue(unitId, out BattleTurnSlotItem slot) && slot != null)
                    {
                        float targetX = GetTargetLocalPosX(i, totalCount);
                        slot.transform.localPosition = new Vector3(targetX + 600f, 0f, 0f);
                    }
                }

                _currentDisplayOrder.Clear();
                _currentDisplayOrder.AddRange(view.DisplayOrder);
                _displayedRound = view.RoundNumber;
                return;
            }

            // 首次入场（无开场动画直接开战场景，如调试或即时开启）
            if (_isFirstTurnSlotsEntry)
            {
                PlayTurnSlotsEnterAnimation(view);
                return;
            }

            // 检查顺序是否变化（跨轮、单位速度改变/插队超车、单位阵亡或逃跑）
            if (view.RoundNumber != _displayedRound || IsDisplayOrderChanged(view))
            {
                AnimateOrderChange(view);
                _displayedRound = view.RoundNumber;
                return;
            }

            // 同轮内仅当前行动者推进：无位置变动，仅刷新高亮标记
            foreach (var kvp in _turnSlotMap)
            {
                if (kvp.Value != null)
                {
                    kvp.Value.SetCurrentActor(kvp.Key == view.CurrentActorUnitId);
                }
            }
        }

        /// <summary>
        /// 显示列表与视图行动栏顺序不一致时返回 true。
        /// 视图顺序为本轮完整顺序（已行动排前，未行动按当前调度优先级随后）；
        /// 变速重排、插队或单位阵亡/逃跑时触发平滑动画重排。
        /// </summary>
        private bool IsDisplayOrderChanged(BattleViewState view)
        {
            if (view.CurrentActorUnitId == 0)
            {
                return false;
            }

            if (_currentDisplayOrder.Count != view.DisplayOrder.Count)
            {
                return true;
            }

            for (int i = 0; i < view.DisplayOrder.Count; i++)
            {
                if (_currentDisplayOrder[i] != view.DisplayOrder[i])
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 播放开场顺位栏错峰滑入动效：各槽位从屏幕右侧快速位移至居中目标位置。
        /// </summary>
        private void PlayTurnSlotsEnterAnimation(BattleViewState view)
        {
            if (view == null || view.DisplayOrder == null || view.DisplayOrder.Count == 0)
            {
                return;
            }

            CanvasGroup turnGroup = View.turnSlotsRoot.gameObject.GetOrAddComponent<CanvasGroup>();
            _turnSlotsFadeTween?.Kill();
            _turnSlotsFadeTween = turnGroup.DOFade(1f, 0.25f);

            BuildOrSyncSlots(view);

            int totalCount = view.DisplayOrder.Count;
            for (int i = 0; i < totalCount; i++)
            {
                int unitId = view.DisplayOrder[i];
                if (_turnSlotMap.TryGetValue(unitId, out BattleTurnSlotItem slot) && slot != null)
                {
                    float targetX = GetTargetLocalPosX(i, totalCount);
                    slot.transform.DOKill();
                    slot.transform.localPosition = new Vector3(targetX + 600f, 0f, 0f);
                    slot.transform.DOLocalMoveX(targetX, 0.35f).SetEase(Ease.OutCubic).SetDelay(i * 0.04f);
                }
            }

            _currentDisplayOrder.Clear();
            _currentDisplayOrder.AddRange(view.DisplayOrder);
            _displayedRound = view.RoundNumber;
            _isFirstTurnSlotsEntry = false;
        }

        /// <summary>
        /// 行动栏顺位发生改变时的平滑过渡动画：
        /// 1. 离场单位（阵亡/逃跑）缩小淡出；
        /// 2. 存活单位各自平滑平移至新目标位置（实现自然的超车与让位插队效果，绝不重新硬切）；
        /// 3. 超车单位（顺位提升）提升渲染层级并微弹反馈。
        /// </summary>
        private void AnimateOrderChange(BattleViewState view)
        {
            BattleTurnSlotItem template = View.battleTurnSlotTemplate;
            List<int> newOrder = new List<int>(view.DisplayOrder);
            int newTotal = newOrder.Count;

            // 1. 处理移除的单位（阵亡或逃跑）
            List<int> toRemove = new List<int>();
            foreach (var kvp in _turnSlotMap)
            {
                int unitId = kvp.Key;
                if (!newOrder.Contains(unitId))
                {
                    toRemove.Add(unitId);
                    BattleTurnSlotItem slot = kvp.Value;
                    if (slot != null)
                    {
                        slot.transform.DOKill();
                        CanvasGroup cg = slot.gameObject.GetOrAddComponent<CanvasGroup>();
                        cg.DOKill();
                        cg.DOFade(0f, 0.2f);
                        slot.transform.DOScale(0.2f, 0.2f).OnComplete(() =>
                        {
                            if (slot != null && slot.gameObject != null)
                            {
                                Destroy(slot.gameObject);
                            }
                        });
                    }
                }
            }

            for (int i = 0; i < toRemove.Count; i++)
            {
                _turnSlotMap.Remove(toRemove[i]);
            }

            // 2. 更新留存与新增单位并计算各自目标位移
            for (int i = 0; i < newTotal; i++)
            {
                int unitId = newOrder[i];
                BattleUnitView unit = FindUnit(view, unitId);
                if (unit == null)
                {
                    continue;
                }

                float targetX = GetTargetLocalPosX(i, newTotal);
                bool isCurrentActor = (unitId == view.CurrentActorUnitId);

                if (_turnSlotMap.TryGetValue(unitId, out BattleTurnSlotItem slot) && slot != null)
                {
                    slot.SetTurnSlot(unit, isCurrentActor);

                    int oldIndex = _currentDisplayOrder.IndexOf(unitId);
                    // 顺位变动或队伍总人数改变导致居中重排
                    if (oldIndex != i || _currentDisplayOrder.Count != newTotal)
                    {
                        // 若为超车/顺位前移，将渲染层级提至顶层并做轻微弹性反馈
                        if (oldIndex > i)
                        {
                            slot.transform.SetAsLastSibling();
                            slot.transform.DOPunchScale(new Vector3(0.12f, 0.12f, 0f), 0.2f, 1, 0f);
                        }

                        slot.transform.DOKill();
                        slot.transform.DOLocalMoveX(targetX, 0.25f).SetEase(Ease.OutCubic);
                    }
                }
                else
                {
                    // 新增单位进入顺位栏
                    BattleTurnSlotItem newSlot = CreateSlotItem(template);
                    newSlot.SetTurnSlot(unit, isCurrentActor);
                    newSlot.transform.localPosition = new Vector3(targetX + 300f, 0f, 0f);
                    newSlot.transform.DOLocalMoveX(targetX, 0.25f).SetEase(Ease.OutCubic);
                    _turnSlotMap[unitId] = newSlot;
                }
            }

            _currentDisplayOrder.Clear();
            _currentDisplayOrder.AddRange(newOrder);
        }

        /// <summary>
        /// 同步或构建当前顺位槽实例，保持与 view.DisplayOrder 一致。
        /// </summary>
        private void BuildOrSyncSlots(BattleViewState view)
        {
            BattleTurnSlotItem template = View.battleTurnSlotTemplate;
            if (template == null || view == null || view.DisplayOrder == null)
            {
                return;
            }

            List<int> toRemove = new List<int>();
            foreach (int unitId in _turnSlotMap.Keys)
            {
                if (!view.DisplayOrder.Contains(unitId))
                {
                    toRemove.Add(unitId);
                }
            }

            for (int i = 0; i < toRemove.Count; i++)
            {
                int unitId = toRemove[i];
                if (_turnSlotMap.TryGetValue(unitId, out BattleTurnSlotItem slot) && slot != null)
                {
                    slot.transform.DOKill();
                    Destroy(slot.gameObject);
                }

                _turnSlotMap.Remove(unitId);
            }

            for (int i = 0; i < view.DisplayOrder.Count; i++)
            {
                int unitId = view.DisplayOrder[i];
                BattleUnitView unit = FindUnit(view, unitId);
                if (unit == null)
                {
                    continue;
                }

                bool isCurrentActor = (unitId == view.CurrentActorUnitId);
                if (!_turnSlotMap.TryGetValue(unitId, out BattleTurnSlotItem slot) || slot == null)
                {
                    slot = CreateSlotItem(template);
                    _turnSlotMap[unitId] = slot;
                }

                slot.SetTurnSlot(unit, isCurrentActor);
            }
        }

        private BattleTurnSlotItem CreateSlotItem(BattleTurnSlotItem template)
        {
            template.gameObject.SetActive(false);
            BattleTurnSlotItem slot = Instantiate(template, View.turnSlotsRoot);
            SetupSlotRectTransform(slot);
            slot.gameObject.SetActive(true);
            return slot;
        }

        private static void SetupSlotRectTransform(BattleTurnSlotItem slot)
        {
            RectTransform rect = slot.transform as RectTransform;
            if (rect == null)
            {
                return;
            }

            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(SlotWidth, SlotHeight);
            rect.localScale = Vector3.one;

            CanvasGroup cg = slot.GetComponent<CanvasGroup>();
            if (cg != null)
            {
                cg.alpha = 1f;
            }
        }

        private static float GetTargetLocalPosX(int index, int totalCount)
        {
            float startX = -(totalCount - 1) * 0.5f * StepX;
            return startX + index * StepX;
        }

        private void RefreshActionPanel(BattleViewState view)
        {
            if (_result != null)
            {
                View.currentActorText.text = GetOutcomeText(_result.Outcome);
                View.attackButton.interactable = false;
                View.skillButton.interactable = false;
                View.escapeButton.interactable = false;
                View.tipText.text = string.Empty;

                return;
            }

            BattleUnitView actor = FindUnit(view, view.CurrentActorUnitId);
            bool playerTurn = actor != null && actor.Faction == BattleFactionType.Player;
            bool actorStunned = actor != null && BattleUnitViewHelper.IsStunned(actor);

            // 眩晕行动者不显示行动菜单：按钮禁用，提示跳过（其跳过由组件按延迟自动推进）
            if (actorStunned)
            {
                View.attackButton.interactable = false;
                View.skillButton.interactable = false;
                View.escapeButton.interactable = false;
                View.currentActorText.text = string.Format("第 {0} 轮  {1} 被眩晕，跳过行动",
                    view.RoundNumber, BattleUnitViewHelper.GetDisplayName(actor));
                View.tipText.text = BattleUnitViewHelper.GetStateText(BattleStateType.Stun);

                return;
            }

            int skillActionId = playerTurn ? FindSkillActionId(view) : 0;
            BattleActionConfig skillConfig =
                skillActionId != 0 ? GameEntry.Luban.Get<BattleActionConfig>(skillActionId) : null;
            bool canUseSkill = playerTurn && skillConfig != null && actor.CurrentMp >= skillConfig.MpCost;

            View.attackButton.interactable = playerTurn;
            View.skillButton.interactable = canUseSkill;
            View.escapeButton.interactable = playerTurn;

            if (_pendingCommandType == BattleActionType.Escape)
            {
                View.currentActorText.text = "【逃跑】待命中（再次点击取消）";
                View.tipText.text = "点击自身角色卡片确认逃跑";
            }
            else if (_pendingCommandType != BattleActionType.None)
            {
                BattleActionConfig pendingAction = GameEntry.Luban.Get<BattleActionConfig>(_pendingActionConfigId);
                string actionName = pendingAction != null
                    ? pendingAction.Name
                    : (_pendingCommandType == BattleActionType.Attack ? "普通攻击" : "技能");
                View.currentActorText.text = string.Format("【{0}】待命中（再次点击取消）", actionName);
                View.tipText.text = GetPendingTip(pendingAction);
            }
            else
            {
                View.currentActorText.text = string.Format("第 {0} 轮  {1} 行动",
                    view.RoundNumber, actor != null ? BattleUnitViewHelper.GetDisplayName(actor) : string.Empty);
                View.tipText.text = playerTurn ? "请选择行动：攻击或技能" : string.Empty;
            }
        }

        private static string GetPendingTip(BattleActionConfig action)
        {
            if (action == null)
            {
                return "请选择目标";
            }

            switch (action.TargetType)
            {
                case BattleTargetType.SingleEnemy:
                    return "点击目标敌人选中，再次点击确认释放";
                case BattleTargetType.AllEnemies:
                    return "点击任意敌人选中，再次点击确认释放全体攻击";
                case BattleTargetType.SingleAlly:
                    return "点击目标友方选中，再次点击确认释放";
                case BattleTargetType.AllAllies:
                    return "点击任意友方选中，再次点击确认释放全体效果";
                case BattleTargetType.Self:
                    return "点击自身选中，再次点击确认释放";
                default:
                    return "请选择目标";
            }
        }

        private static string GetOutcomeText(BattleOutcomeType outcome)
        {
            switch (outcome)
            {
                case BattleOutcomeType.Victory:
                    return "胜利！";
                case BattleOutcomeType.AllEscaped:
                    return "全员逃跑";
                case BattleOutcomeType.PartialEscapeDefeat:
                    return "部分逃跑，战斗失败";
                case BattleOutcomeType.TotalDefeat:
                    return "全员阵亡，单局失败";
                default:
                    return string.Empty;
            }
        }

        private static int FindAttackActionId(BattleViewState view)
        {
            foreach (int actionId in view.AvailableActionIds)
            {
                BattleActionConfig action = GameEntry.Luban.Get<BattleActionConfig>(actionId);
                if (action != null && action.ActionType == BattleActionType.Attack)
                {
                    return actionId;
                }
            }

            return 0;
        }

        private static int FindSkillActionId(BattleViewState view)
        {
            foreach (int actionId in view.AvailableActionIds)
            {
                BattleActionConfig action = GameEntry.Luban.Get<BattleActionConfig>(actionId);
                if (action != null && action.ActionType == BattleActionType.Skill)
                {
                    return actionId;
                }
            }

            return 0;
        }

        private static int FindFirstActiveEnemy(BattleViewState view)
        {
            foreach (BattleUnitView unit in view.Units)
            {
                if (unit.Faction == BattleFactionType.Enemy && !unit.IsDefeated && !unit.IsEscaped)
                {
                    return unit.BattleUnitId;
                }
            }

            return 0;
        }

        private static BattleUnitView FindUnit(BattleViewState view, int unitId)
        {
            foreach (BattleUnitView unit in view.Units)
            {
                if (unit.BattleUnitId == unitId)
                {
                    return unit;
                }
            }

            return null;
        }

        private BattleActorCardItem GetPlayerCard(int index)
        {
            switch (index)
            {
                case 0:
                    return View.playerCard1;
                case 1:
                    return View.playerCard2;
                case 2:
                    return View.playerCard3;
                case 3:
                    return View.playerCard4;
                default:
                    return null;
            }
        }

        private static void ClearSlots(RectTransform root, Transform template)
        {
            for (int i = root.childCount - 1; i >= 0; i--)
            {
                Transform child = root.GetChild(i);
                if (child == template)
                {
                    continue;
                }

                Destroy(child.gameObject);
            }
        }

        private void OnItemButtonClick()
        {
            // 道具首版禁用，不产生指令
        }

        private void OnEscapeButtonClick()
        {
            if (_isEnteringAnimation)
            {
                return;
            }

            BattleViewState view = GameEntry.TurnBattle.GetViewState();
            if (view == null || view.CurrentActorUnitId == 0)
            {
                return;
            }

            BattleUnitView actor = FindUnit(view, view.CurrentActorUnitId);
            if (actor == null || actor.Faction != BattleFactionType.Player || BattleUnitViewHelper.IsStunned(actor))
            {
                return;
            }

            // 二次确认与取消机制：已处于逃跑待命时再次点击取消
            if (_pendingCommandType == BattleActionType.Escape)
            {
                ClearPendingAction();
                RefreshActionPanel(view);
                return;
            }

            _pendingCommandType = BattleActionType.Escape;
            _pendingActionConfigId = 0;
            _selectedTargetUnitId = 0;
            RefreshActionPanel(view);
        }
    }
}
