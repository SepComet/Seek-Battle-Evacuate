using System.Collections.Generic;
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
        private BattleResult _result;
        private int _displayedRound;
        private readonly List<BattleTurnSlotItem> _turnSlots = new List<BattleTurnSlotItem>();
        private readonly List<int> _turnSlotUnitIds = new List<int>();
        private readonly List<BattleEnemySlotItem> _enemySlots = new List<BattleEnemySlotItem>();
        private BattleActionType _pendingCommandType = BattleActionType.None;
        private int _pendingActionConfigId;
        private int _displayedActorId;
        private int _selectedTargetUnitId;
        private bool _isEnteringAnimation;
        private Tween _turnSlotsFadeTween;
        private Tween _delayedEnterCall;
        private Sequence _victorySequence;

        /// <summary>
        /// 战斗结果展示停留时间（秒），之后非全灭结果自动关闭战斗界面。
        /// </summary>
        private const float ResultDisplayDelaySeconds = 1.5f;

        protected override void OnInit(object userData)
        {
            base.OnInit(userData);

            // 道具首版禁用；逃跑 M5 接入
            View.itemButton.interactable = false;
        }

        protected override void OnOpen(object userData)
        {
            base.OnOpen(userData);

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
            _turnSlots.Clear();
            _turnSlotUnitIds.Clear();
            _enemySlots.Clear();

            BattleViewState initialViewState = GameEntry.TurnBattle != null ? GameEntry.TurnBattle.GetViewState() : null;
            Refresh(initialViewState);

            BattleEnterAnimationParams enterParams = userData as BattleEnterAnimationParams;
            if (enterParams != null && initialViewState != null)
            {
                StartEnterAnimation(enterParams, initialViewState);
            }
            else
            {
                _isEnteringAnimation = false;
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

            // 3. 玩家卡片错峰飞入
            int activePlayerCount = 0;
            for (int i = 0; i < 4; i++)
            {
                BattleActorCardItem card = GetPlayerCard(i);
                if (card != null && card.gameObject.activeSelf)
                {
                    Vector3 startPos = (enterParams.PlayerScreenPositions != null && i < enterParams.PlayerScreenPositions.Count)
                        ? enterParams.PlayerScreenPositions[i]
                        : (enterParams.PlayerScreenPositions != null && enterParams.PlayerScreenPositions.Count > 0
                            ? enterParams.PlayerScreenPositions[0]
                            : enterParams.EnemyScreenPosition);

                    card.PlayEnterAnimation(startPos, i * 0.08f);
                    activePlayerCount++;
                }
            }

            // 4. 敌人槽位错峰飞入（从同一个敌人队伍地图点扇出）
            for (int i = 0; i < _enemySlots.Count; i++)
            {
                _enemySlots[i].PlayEnterAnimation(enterParams.EnemyScreenPosition, 0.1f + i * 0.08f);
            }

            // 5. 计算全员就位时长并注册完成回调
            float maxDuration = Mathf.Max(activePlayerCount * 0.08f, 0.1f + _enemySlots.Count * 0.08f) + 0.62f;

            _delayedEnterCall?.Kill();
            _delayedEnterCall = DOVirtual.DelayedCall(maxDuration, () =>
            {
                OnEnterAnimationComplete(view);
            });
        }

        private void OnEnterAnimationComplete(BattleViewState view)
        {
            _isEnteringAnimation = false;
            _delayedEnterCall = null;

            // 顺位栏淡入
            CanvasGroup turnGroup = View.turnSlotsRoot.gameObject.GetOrAddComponent<CanvasGroup>();
            _turnSlotsFadeTween?.Kill();
            _turnSlotsFadeTween = turnGroup.DOFade(1f, 0.25f);

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
                card?.ResetVisualState();
            }

            for (int i = 0; i < _enemySlots.Count; i++)
            {
                _enemySlots[i]?.ResetVisualState();
            }

            CanvasGroup turnGroup = View.turnSlotsRoot.GetComponent<CanvasGroup>();
            if (turnGroup != null)
            {
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
                if (target == null || target.Faction != BattleFactionType.Enemy || target.IsDefeated || target.IsEscaped)
                {
                    return;
                }

                ConfirmOrSelectTarget(view, actor, targetEnemyUnitId, new List<int> { targetEnemyUnitId });
            }
            else if (action.TargetType == BattleTargetType.AllEnemies)
            {
                // 全体目标：点击任意存活敌人选中，再次点击确认释放，目标由内核自动展开
                BattleUnitView target = FindUnit(view, targetEnemyUnitId);
                if (target == null || target.Faction != BattleFactionType.Enemy || target.IsDefeated || target.IsEscaped)
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
                if (target == null || target.Faction != BattleFactionType.Player || target.IsDefeated || target.IsEscaped)
                {
                    return;
                }

                ConfirmOrSelectTarget(view, actor, targetPlayerUnitId, new List<int> { targetPlayerUnitId });
            }
            else if (action.TargetType == BattleTargetType.AllAllies)
            {
                BattleUnitView target = FindUnit(view, targetPlayerUnitId);
                if (target == null || target.Faction != BattleFactionType.Player || target.IsDefeated || target.IsEscaped)
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
                if (card != null && card.gameObject.activeSelf)
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
            yield return new WaitForSecondsRealtime(ResultDisplayDelaySeconds);

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
            yield return new WaitForSecondsRealtime(ResultDisplayDelaySeconds);

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
            BattleTurnSlotItem template = View.battleTurnSlotTemplate;

            if (view.RoundNumber != _displayedRound ||
                (view.CurrentActorUnitId != 0 && !_turnSlotUnitIds.Contains(view.CurrentActorUnitId)) ||
                IsDisplayOrderChanged(view))
            {
                RebuildTurnSlots(view, template);
                _displayedRound = view.RoundNumber;
                return;
            }

            // 同轮内只移动当前行动者高亮，不隐藏已行动单位
            for (int i = 0; i < _turnSlots.Count; i++)
            {
                _turnSlots[i].SetCurrentActor(_turnSlotUnitIds[i] == view.CurrentActorUnitId);
            }
        }

        /// <summary>
        /// 显示列表与视图行动栏顺序不一致时返回 true。
        /// 视图顺序为本轮完整顺序（已行动按行动先后排前，未行动按当前调度优先级随后，
        /// 先制第一轮敌人排在玩家之后）；变速重排或单位阵亡时触发重建。
        /// </summary>
        private bool IsDisplayOrderChanged(BattleViewState view)
        {
            if (view.CurrentActorUnitId == 0)
            {
                return false;
            }

            if (_turnSlotUnitIds.Count != view.DisplayOrder.Count)
            {
                return true;
            }

            for (int i = 0; i < view.DisplayOrder.Count; i++)
            {
                if (_turnSlotUnitIds[i] != view.DisplayOrder[i])
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 刷新本轮行动栏顺序：已行动单位按行动先后排前，
        /// 未行动单位按当前调度优先级随后，当前行动者高亮；本轮内已行动单位保持可见。
        /// 数量相同时复用槽对象，只重建单位映射与内容。
        /// </summary>
        private void RebuildTurnSlots(BattleViewState view, BattleTurnSlotItem template)
        {
            List<BattleUnitView> units = new List<BattleUnitView>();
            foreach (int unitId in view.DisplayOrder)
            {
                BattleUnitView unit = FindUnit(view, unitId);
                if (unit != null)
                {
                    units.Add(unit);
                }
            }

            if (_turnSlots.Count != units.Count)
            {
                template.gameObject.SetActive(false);
                ClearSlots(View.turnSlotsRoot, template.transform);
                _turnSlots.Clear();
                foreach (BattleUnitView unit in units)
                {
                    BattleTurnSlotItem slot = Instantiate(template, View.turnSlotsRoot);
                    slot.gameObject.SetActive(true);
                    _turnSlots.Add(slot);
                }
            }

            _turnSlotUnitIds.Clear();
            for (int i = 0; i < _turnSlots.Count; i++)
            {
                _turnSlotUnitIds.Add(units[i].BattleUnitId);
                _turnSlots[i].SetTurnSlot(units[i], units[i].BattleUnitId == view.CurrentActorUnitId);
            }
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
            BattleActionConfig skillConfig = skillActionId != 0 ? GameEntry.Luban.Get<BattleActionConfig>(skillActionId) : null;
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
                string actionName = pendingAction != null ? pendingAction.Name : (_pendingCommandType == BattleActionType.Attack ? "普通攻击" : "技能");
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
