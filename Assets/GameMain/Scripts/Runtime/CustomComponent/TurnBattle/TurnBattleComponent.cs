using System;
using System.Collections.Generic;
using SepCore.Definition;
using SepCore.Exploration;
using SepCore.UI;
using UnityEngine;
using UnityGameFramework.Runtime;

namespace SepCore.Battle
{
    /// <summary>
    /// 单局战斗组件，也是战斗模块的唯一外部入口。
    /// 负责本局临时角色状态、计时、探索暂停标志、战斗占用和当前唯一的 BattleRuntime。
    /// 外部通过 TryStartBattle 启动战斗、CloseBattle 关闭调试战斗；
    /// 战斗规则（调度、指令校验、效果、敌人决策）在内部共享同一个 BattleRuntime，
    /// 不为模块内部的函数调用建立请求/响应 DTO。
    /// 本局种子与共享随机源由独立的 RandomComponent 持有，战斗只沿用，不创建私有随机源。
    /// </summary>
    public class TurnBattleComponent : GameFrameworkComponent
    {
        private readonly List<PlayerUnitState> _players = new List<PlayerUnitState>();
        private float _elapsedMs;
        private bool _timerPaused;
        private bool _explorationPaused;
        private bool _battleActive;
        private BattleRuntime _runtime;
        private IBattleConfigProvider _config;
        private Action<BattleResult> _onCompleted;
        private Action<BattleStep> _stepListener;
        private Coroutine _autoAdvanceRoutine;
        private BattleOutcomeType? _lastOutcome;
        private float _escapeProtectionRemaining;

        /// <summary>
        /// 获取最近一场战斗的终局结果。
        /// </summary>
        public BattleOutcomeType? LastOutcome => _lastOutcome;

        /// <summary>
        /// 获取本局临时角色状态列表（只读）。
        /// </summary>
        public IReadOnlyList<PlayerUnitState> Players => _players;

        /// <summary>
        /// 获取本局已流逝时间（毫秒）。
        /// </summary>
        public long RunElapsedMs => (long)_elapsedMs;

        /// <summary>
        /// 获取单局计时是否已暂停。
        /// </summary>
        public bool IsTimerPaused => _timerPaused;

        /// <summary>
        /// 获取探索更新是否已暂停。
        /// 战斗期间为 true，地图更新统一以此作为门禁。
        /// </summary>
        public bool IsExplorationPaused => _explorationPaused;

        /// <summary>
        /// 获取当前是否存在战斗占用。
        /// 战斗重复进入由此标志拒绝。
        /// </summary>
        public bool IsBattleActive => _battleActive;

        /// <summary>
        /// 获取逃跑保护期是否生效中。生效期间敌人不可发现领队、警惕值不增长且碰触不进战。
        /// </summary>
        public bool IsEscapeProtectionActive => _escapeProtectionRemaining > 0f;

        /// <summary>
        /// 激活逃跑保护期。
        /// </summary>
        public void ActivateEscapeProtection()
        {
            if (_config == null)
            {
                _config = new LubanBattleConfigProvider();
            }

            GlobalConfig global = _config.GetGlobal();
            float durationSeconds = (global != null && global.EscapeProtectionMs > 0)
                ? global.EscapeProtectionMs / 1000f
                : 2.0f;
            _escapeProtectionRemaining = durationSeconds;
            Log.Info("Escape protection activated for {0:F1} seconds.", durationSeconds);
        }

        /// <summary>
        /// 获取当前唯一的战斗运行时；仅战斗期间非 null。
        /// </summary>
        internal BattleRuntime Runtime => _runtime;

        /// <summary>
        /// 开始一局新的单局：重置全部临时状态。
        /// </summary>
        public void BeginRun()
        {
            ResetRunState();
        }

        /// <summary>
        /// 结束当前单局，清空全部临时状态。
        /// </summary>
        public void EndRun()
        {
            ResetRunState();
        }

        /// <summary>
        /// 用指定状态替换本局临时角色列表，保持战备顺序。
        /// </summary>
        /// <param name="players">新的临时角色状态，可为空。</param>
        public void ReplacePlayers(IEnumerable<PlayerUnitState> players)
        {
            _players.Clear();
            if (players != null)
            {
                _players.AddRange(players);
            }
        }

        /// <summary>
        /// 尝试开始一场战斗，是战斗模块的唯一外部入口。
        /// 校验通过后预留战斗占用、暂停探索更新与单局计时并打开 BattleForm；
        /// 校验失败不消耗随机数、不修改单局状态。
        /// 战斗完成时通过 onCompleted 一次性回调返回 BattleResult；调试入口可不提供回调。
        /// </summary>
        /// <param name="encounter">探索层创建的遭遇输入。</param>
        /// <param name="onCompleted">战斗结束的一次性完成回调，可为 null。</param>
        /// <returns>是否成功开始。</returns>
        public bool TryStartBattle(BattleEncounter encounter, Action<BattleResult> onCompleted)
        {
            if (encounter == null)
            {
                Log.Error("Can not start battle with null encounter.");
                return false;
            }

            if (_battleActive)
            {
                Log.Warning("Can not start battle because battle occupancy is already active.");
                return false;
            }

            if (_config == null)
            {
                _config = new LubanBattleConfigProvider();
            }

            BattleRuntime runtime = BattleRuntime.Create(encounter, _players, _config, GameEntry.Random.Random);
            if (runtime == null)
            {
                Log.Warning(
                    "Can not start battle because battle runtime can not be created from given encounter/players/config.");
                return false;
            }

            _runtime = runtime;
            _onCompleted = onCompleted;
            _lastOutcome = null;
            _battleActive = true;
            _explorationPaused = true;
            _timerPaused = true;

            CharacterInputBridge.DisableInput(InputDisableReason.Battle);

            GameEntry.UI.OpenUIForm(UIFormType.BattleForm);
            Log.Info("Battle started with encounter '{0}'.", encounter.EncounterId);

            // M2：敌人速度更高时开局行动者为敌人，同样按间歇节奏自动推进
            ScheduleAutoAdvance();
            return true;
        }

        /// <summary>
        /// 提交当前玩家指令并同步推进单次行动，返回本次推进的行动记录、最新视图和可能的最终结果。
        /// 非法指令不消耗行动机会；玩家行动后若轮到敌人，由组件按间歇节奏自动推进敌人回合，
        /// 每一步通过已注册的推进监听推送给 UI。
        /// 战斗完成后向 onCompleted 一次性回调返回 BattleResult。
        /// </summary>
        /// <param name="command">当前玩家行动者的指令。</param>
        /// <returns>本次玩家行动的推进结果；当前没有进行中的战斗时返回 null。</returns>
        public BattleStep SubmitCommand(BattleCommand command)
        {
            if (_runtime == null || _runtime.IsCompleted)
            {
                return null;
            }

            BattleStep step = _runtime.SubmitCommand(command);
            if (step.Result != null)
            {
                FinishBattle(step.Result);
            }
            else
            {
                ScheduleAutoAdvance();
            }

            return step;
        }

        /// <summary>
        /// 注册战斗推进监听；UI 通过它接收组件自动推进（敌人行动）产生的每一步。
        /// </summary>
        /// <param name="listener">推进监听，传入 null 取消注册。</param>
        public void SetStepListener(Action<BattleStep> listener)
        {
            _stepListener = listener;
        }

        /// <summary>
        /// 获取当前战斗的只读视图；没有进行中的战斗时返回 null。
        /// </summary>
        public BattleViewState GetViewState()
        {
            return _runtime != null ? _runtime.BuildViewState() : null;
        }

        /// <summary>
        /// 关闭当前战斗壳层。非 TotalDefeat 恢复探索更新与单局计时；
        /// TotalDefeat 后关闭只收起界面，不恢复（单局已失败）。
        /// M5 起战斗界面在展示结果后自行调用；调试入口可手动调用。
        /// </summary>
        public void CloseBattle()
        {
            if (!_battleActive)
            {
                return;
            }

            UGuiForm form = GameEntry.UI.GetUIForm(UIFormType.BattleForm);
            if (form != null)
            {
                GameEntry.UI.CloseUIForm(form);
            }

            _runtime = null;
            _onCompleted = null;
            StopAutoAdvance();
            _stepListener = null;
            if (_lastOutcome != BattleOutcomeType.TotalDefeat)
            {
                _explorationPaused = false;
                _timerPaused = false;
            }

            CharacterInputBridge.EnableInput(InputDisableReason.Battle);

            _battleActive = false;
            Log.Info("Battle closed.");
        }

        /// <summary>
        /// 设置探索更新暂停状态。
        /// </summary>
        /// <param name="paused">是否暂停探索更新。</param>
        public void SetExplorationPaused(bool paused)
        {
            _explorationPaused = paused;
        }

        /// <summary>
        /// 设置单局计时暂停状态。
        /// </summary>
        /// <param name="paused">是否暂停单局计时。</param>
        public void SetTimerPaused(bool paused)
        {
            _timerPaused = paused;
        }

        private void Update()
        {
            if (_escapeProtectionRemaining > 0f && !_battleActive)
            {
                _escapeProtectionRemaining = Mathf.Max(0f, _escapeProtectionRemaining - Time.deltaTime);
            }

            if (_timerPaused)
            {
                return;
            }

            _elapsedMs += Time.deltaTime * 1000f;
        }

        /// <summary>
        /// 当前行动者是敌人且战斗未结束时，启动带间歇的自动推进协程。
        /// 眩晕玩家同样自动推进：其跳过是独立一拍，UI 按延迟展示该单位行为。
        /// </summary>
        private void ScheduleAutoAdvance()
        {
            if (_autoAdvanceRoutine != null)
            {
                return;
            }

            if (!NeedsAutoAdvance())
            {
                return;
            }

            _autoAdvanceRoutine = StartCoroutine(AutoAdvanceRoutine());
        }

        private bool NeedsAutoAdvance()
        {
            if (_runtime == null || _runtime.IsCompleted || _runtime.CurrentActor == null)
            {
                return false;
            }

            if (_runtime.CurrentActor.Faction == BattleFactionType.Enemy)
            {
                return true;
            }

            return BattleRuntime.IsStunned(_runtime.CurrentActor);
        }

        private System.Collections.IEnumerator AutoAdvanceRoutine()
        {
            GlobalConfig global = _config.GetGlobal();
            float advanceDelaySeconds = global.AutoAdvanceDelayMs / 1000f;
            int registerFrameout = global.RegisterFrameout;

            // 战斗 UI 打开是异步资源加载，等推进监听注册后再推第一步，避免敌人先手事件丢失
            int waitFrames = 0;
            while (_stepListener == null && _runtime != null && !_runtime.IsCompleted &&
                   waitFrames++ < registerFrameout)
            {
                yield return null;
            }

            while (NeedsAutoAdvance())
            {
                yield return new WaitForSeconds(advanceDelaySeconds);

                // 眩晕玩家走跳过推进，其余自动行动者（敌人，含眩晕敌人）走敌人回合推进
                BattleUnit actor = _runtime.CurrentActor;
                BattleStep step = actor.Faction == BattleFactionType.Player
                    ? _runtime.AdvanceStunSkip()
                    : _runtime.AdvanceEnemyTurn();
                if (step.Result != null)
                {
                    FinishBattle(step.Result);
                }

                _stepListener?.Invoke(step);
            }

            _autoAdvanceRoutine = null;
        }

        private void StopAutoAdvance()
        {
            if (_autoAdvanceRoutine != null)
            {
                StopCoroutine(_autoAdvanceRoutine);
                _autoAdvanceRoutine = null;
            }
        }

        /// <summary>
        /// 统一结束流程：回写单局状态、一次性完成回调。
        /// 非 TotalDefeat 由战斗界面展示结果后自行关闭并恢复探索；
        /// TotalDefeat 不回写，界面停留显示失败，由外部处理单局失败。
        /// </summary>
        private void FinishBattle(BattleResult result)
        {
            ApplyResultWriteback(result);
            _lastOutcome = result.Outcome;
            if (result.Outcome == BattleOutcomeType.AllEscaped || result.Outcome == BattleOutcomeType.PartialEscapeDefeat)
            {
                ActivateEscapeProtection();
            }
            LogBattleResult(result);
            Action<BattleResult> callback = _onCompleted;
            _onCompleted = null;
            callback?.Invoke(result);
        }

        /// <summary>
        /// 非 TotalDefeat 结果把玩家战后 HP/MP 回写到单局临时状态，阵亡者按复活值恢复。
        /// </summary>
        private void ApplyResultWriteback(BattleResult result)
        {
            GlobalConfig global = _config.GetGlobal();
            RoundPlayerStateWriteback.Apply(_players, result, global.ReviveHp, global.ReviveMp);
            if (GameEntry.Round?.Session != null)
            {
                GameEntry.Round.ApplyBattleResult(result, global.ReviveHp, global.ReviveMp);
            }
        }

        private static void LogBattleResult(BattleResult result)
        {
            Log.Info("Battle ended with outcome '{0}' (encounter '{1}').", result.Outcome, result.EncounterId);
            if (result.Players == null)
            {
                return;
            }

            foreach (BattlePlayerResult playerResult in result.Players)
            {
                Log.Info("Battle player '{0}': HP {1}, MP {2}, defeated {3}, escaped {4}.",
                    playerResult.CharacterId, playerResult.CurrentHp, playerResult.CurrentMp,
                    playerResult.WasDefeated, playerResult.Escaped);
            }
        }

        private void ResetRunState()
        {
            _players.Clear();
            _elapsedMs = 0;
            _escapeProtectionRemaining = 0f;
            _timerPaused = false;
            _explorationPaused = false;
            _battleActive = false;
            _runtime = null;
            _onCompleted = null;
            _lastOutcome = null;
            StopAutoAdvance();
            _stepListener = null;
            CharacterInputBridge.EnableInput(InputDisableReason.Battle);
        }
    }
}