using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using GameFramework.Fsm;
using GameFramework.Procedure;
using SepCore.Definition;
using SepCore.Entity;
using SepCore.Exploration;
using SepCore.Net;
using SepCore.Run;
using SepCore.UI;
using UnityEngine;
using UnityGameFramework.Runtime;

namespace SepCore.Procedure
{
    /// <summary>
    /// 主玩法流程（SceneType.Main 场景）。
    /// 内部维护分层状态机，统一编排：
    /// 1. MainMapBuildingState（地图构建）
    /// 2. MainExplorationBattleState（探索与战斗循环）
    /// 3. MainSettlementState（撤离结算）
    /// </summary>
    public class ProcedureMain : ProcedureBase
    {
        private IFsm<IProcedureManager> _procedureOwner;
        private IFsm<ProcedureMain> _fsm;
        private bool _resumeAttempted;
        private bool _resumeInFlight;

        /// <summary>
        /// 本局开放的撤离点世界坐标。
        /// </summary>
        public Vector2 ExtractionPoint { get; set; }

        /// <summary>
        /// 本局玩家出生点世界坐标。
        /// </summary>
        public Vector2 PlayerSpawnPoint { get; set; }

        /// <summary>
        /// 本局单局难度。
        /// </summary>
        public DifficultyTier Difficulty { get; set; }

        /// <summary>
        /// 本局进入场景时间（Unix 毫秒现实时间戳）。
        /// </summary>
        public long RunStartTimeUtcMs { get; set; }

        /// <summary>
        /// 待处理的单局结算结果。
        /// </summary>
        public RoundResultType? PendingOutcome { get; private set; }

        /// <summary>
        /// 本局主摄像机实体逻辑引用。
        /// </summary>
        public MainCameraLogic MainCamera { get; set; }

        /// <summary>
        /// 结算后是否自动返回大厅菜单（无结算 UI 时默认 true）。
        /// </summary>
        public bool AutoReturnToMenu { get; set; } = true;

        /// <summary>
        /// 当前子状态机实例。
        /// </summary>
        public IFsm<ProcedureMain> Fsm => _fsm;

        protected override void OnInit(IFsm<IProcedureManager> procedureOwner)
        {
            base.OnInit(procedureOwner);
        }

        protected override void OnEnter(IFsm<IProcedureManager> procedureOwner)
        {
            base.OnEnter(procedureOwner);
            Log.Info("[ProcedureMain] Procedure entered.");

            CharacterInputBridge.Reset();

            _procedureOwner = procedureOwner;
            ExtractionPoint = Vector2.zero;
            PlayerSpawnPoint = Vector2.zero;
            PendingOutcome = null;
            MainCamera = null;
            RunStartTimeUtcMs = 0;
            // 恢复尝试按"单局流程"计次：重进流程（新局）允许新一轮恢复尝试；
            // 重上线标记一并丢弃——断线期间在大厅重上的场景由下一局 start_run 幂等承接，不走恢复。
            _resumeAttempted = false;
            _resumeInFlight = false;
            GameEntry.Net?.TryConsumeBackOnline();

            Difficulty = DifficultyTier.Tier1;
            if (GameEntry.Save.Data?.loadout != null)
            {
                Difficulty = GameEntry.Save.Data.loadout.difficultyId;
            }

            // 创建并启动子状态机
            _fsm = GameEntry.Fsm.CreateFsm("MainProcedureFsm", this,
                new MainMapBuildingState(),
                new MainExplorationBattleState(),
                new MainSettlementState());

            _fsm.Start<MainMapBuildingState>();
        }

        protected override void OnUpdate(IFsm<IProcedureManager> procedureOwner, float elapseSeconds,
            float realElapseSeconds)
        {
            base.OnUpdate(procedureOwner, elapseSeconds, realElapseSeconds);
            TryBeginResumeRun();
        }

        protected override void OnLeave(IFsm<IProcedureManager> procedureOwner, bool isShutdown)
        {
            if (_fsm != null)
            {
                GameEntry.Fsm.DestroyFsm(_fsm);
                _fsm = null;
            }

            _procedureOwner = null;
            PendingOutcome = null;
            MainCamera = null;

            if (GameEntry.UI != null)
            {
                GameEntry.UI.CloseSceneUIForms();
            }

            CharacterInputBridge.Reset();

            Log.Info("[ProcedureMain] Procedure left.");
            base.OnLeave(procedureOwner, isShutdown);
        }

        protected override void OnDestroy(IFsm<IProcedureManager> procedureOwner)
        {
            base.OnDestroy(procedureOwner);
        }

        /// <summary>
        /// 打开虚拟摇杆与交互界面（JoystickForm）。
        /// </summary>
        public void OpenJoystickForm()
        {
            if (GameEntry.UI != null)
            {
                GameEntry.UI.OpenUIForm(UIFormType.JoystickForm);
            }
        }

        /// <summary>
        /// 关闭虚拟摇杆与交互界面（JoystickForm）。
        /// </summary>
        public void CloseJoystickForm()
        {
            if (GameEntry.UI != null)
            {
                UGuiForm joystickForm = GameEntry.UI.GetUIForm(UIFormType.JoystickForm);
                if (joystickForm != null)
                {
                    GameEntry.UI.CloseUIForm(joystickForm);
                }
            }
        }

        /// <summary>
        /// 打开局内 HUD 界面（RoundHUDForm），并携带本局撤离点与小地图视窗数据供标记定位。
        /// </summary>
        public void OpenRoundHUDForm()
        {
            if (GameEntry.UI != null)
            {
                GameEntry.UI.OpenUIForm(UIFormType.RoundHUDForm, BuildExtractionMarkerData());
            }
        }

        /// <summary>
        /// 关闭局内 HUD 界面（RoundHUDForm）。
        /// </summary>
        public void CloseRoundHUDForm()
        {
            if (GameEntry.UI != null)
            {
                UGuiForm roundHUDForm = GameEntry.UI.GetUIForm(UIFormType.RoundHUDForm);
                if (roundHUDForm != null)
                {
                    GameEntry.UI.CloseUIForm(roundHUDForm);
                }
            }
        }

        /// <summary>
        /// 打开战局简报/过场横幅界面（DeploymentBriefingForm）。
        /// </summary>
        public void OpenDeploymentBriefingForm(DeploymentBannerData data = null)
        {
            if (GameEntry.UI != null)
            {
                GameEntry.UI.OpenUIForm(UIFormType.DeploymentBriefingForm, data);
            }
        }

        /// <summary>
        /// 关闭战局简报/过场横幅界面（DeploymentBriefingForm）。
        /// </summary>
        public void CloseDeploymentBriefingForm()
        {
            if (GameEntry.UI != null)
            {
                UGuiForm briefingForm = GameEntry.UI.GetUIForm(UIFormType.DeploymentBriefingForm);
                if (briefingForm != null)
                {
                    GameEntry.UI.CloseUIForm(briefingForm);
                }
            }
        }

        /// <summary>
        /// 触发单局结算，设置结果并在下一个生命周期切换至结算状态。
        /// </summary>
        public void TriggerSettlement(RoundResultType outcome)
        {
            PendingOutcome = outcome;
        }

        /// <summary>
        /// 组装小地图撤离点标记数据：撤离点世界坐标，以及小地图视窗来源与覆盖的世界尺寸。
        /// </summary>
        private ExtractionMarkerData BuildExtractionMarkerData()
        {
            if (MainCamera == null)
            {
                Log.Error("[ProcedureMain] Main camera is missing, cannot build extraction marker data.");
                return null;
            }

            return new ExtractionMarkerData
            {
                ExtractionPoint = ExtractionPoint,
                MainCamera = MainCamera,
                ViewportSize = MainCamera.MinimapViewportSize
            };
        }

        /// <summary>
        /// 设置暂停菜单状态（暂停或恢复单局探索计时）。
        /// </summary>
        public void SetPauseMenuPaused(bool paused)
        {
            GameEntry.TurnBattle.SetTimerPaused(paused);
            Log.Info("[ProcedureMain] Pause menu timer pause set to: {0}.", paused);
        }

        /// <summary>
        /// 退出单局场景，切换至大厅流程。
        /// </summary>
        public void ReturnToMenu()
        {
            if (_procedureOwner == null)
            {
                Log.Error("[ProcedureMain] Procedure owner is invalid. Cannot return to menu.");
                return;
            }

            Log.Info("[ProcedureMain] Returning to Menu procedure...");
            _procedureOwner.SetData<VarInt32>("NextSceneId", (int)SceneType.Menu);
            ChangeState<ProcedureChangeScene>(_procedureOwner);
        }

        /// <summary>
        /// 判定本局已失效（断线重连恢复被拒或种子不一致）：提示玩家、清理单局状态并回大厅。
        /// 绝不以 resume=false 重开新局（契约 §9 恢复流程）。
        /// </summary>
        public void AbortInvalidatedRun(string message)
        {
            Log.Error("[ProcedureMain] Run invalidated; aborting to menu. Reason: {0}", message);

            NetworkNotice.Show(message);

            if (GameEntry.Round != null && GameEntry.Round.HasActiveRun)
            {
                GameEntry.Round.EndRun();
            }

            GameEntry.TurnBattle.EndRun();
            GameEntry.Random.EndRun();
            GameEntry.Net?.Run.ResetRun();

            _resumeAttempted = true;
            _resumeInFlight = false;
            ReturnToMenu();
        }

        /// <summary>
        /// 断线重连后的恢复入口（轮询）：仅在探索状态、本流程尚未尝试过恢复、且网络刚完成
        /// false→true 重上线迁移时，发起一次 start_run(resume=true)。
        /// in-flight 期间与失败后都不再重试（一次会话一次尝试，失败按失效/继续本地游玩处理）。
        /// </summary>
        private void TryBeginResumeRun()
        {
            if (_resumeAttempted || _resumeInFlight)
            {
                return;
            }

            // 只在探索循环内恢复：地图构建尚未持有本地局（Round.Session 未建），
            // 结算/切场景阶段没有恢复可言。
            if (!(_fsm != null && _fsm.CurrentState is MainExplorationBattleState))
            {
                return;
            }

            NetComponent net = GameEntry.Net;
            if (net == null || !net.OnlineReady)
            {
                return;
            }

            if (!net.TryConsumeBackOnline())
            {
                return;
            }

            // 探索中本地局必然已建立（地图构建成功才会进入探索状态）；没有即是装配缺陷。
            RoundSession session = GameEntry.Round != null ? GameEntry.Round.Session : null;
            if (session == null)
            {
                Log.Error("[ProcedureMain] Exploration is running but Round.Session is missing; cannot resume.");
                _resumeAttempted = true;
                return;
            }

            _resumeAttempted = true;
            _resumeInFlight = true;
            ResumeRunAsync(session).Forget();
        }

        /// <summary>
        /// 恢复执行：start_run(resume=true) → 决策（种子校验/失效/网络态）→ 应用服务器回放的消耗状态。
        /// 恢复不重建地图：摆位由同种子预测保证一致（spec 用户故事 4）。
        /// </summary>
        private async UniTask ResumeRunAsync(RoundSession session)
        {
            try
            {
                Log.Info("[ProcedureMain] Back online; resuming run with seed {0} (difficulty {1}).",
                    session.Seed, session.Difficulty);

                StartRunResult result = await GameEntry.Net.Run.StartRunAsync(session.Difficulty, resume: true);

                RunResumeDecision decision = RunResumeEvaluator.Evaluate(result, session.Seed);
                switch (decision.Action)
                {
                    case RunResumeAction.RunInvalid:
                        AbortInvalidatedRun("本局已失效");
                        return;

                    case RunResumeAction.NetworkDown:
                        // 网络仍不可用：本局不失效，恢复尝试就此结束，玩家继续本地游玩；
                        // 后续交互失败照旧走各自的拒绝提示，不再自动重试恢复。
                        Log.Warning("[ProcedureMain] Run resume attempt ended without success ({0}); staying in current run.",
                            decision.Result != null ? decision.Result.Outcome.ToString() : "unknown");
                        NetworkNotice.ShowInteractionRejected();
                        return;
                }

                // 成功：服务器 spawn/extraction 与当前局理论上必同值（服务器幂等保证同 run 必同种子与点位），
                // 不一致只记日志，不中断恢复。
                if (decision.Result.SpawnPoint == null || decision.Result.ExtractionPoint == null)
                {
                    Log.Error("[ProcedureMain] Run resume response is missing spawn/extraction points (runId {0}).",
                        decision.Result.RunId);
                }
                else
                {
                    Vector2 serverSpawn = new Vector2(decision.Result.SpawnPoint.x, decision.Result.SpawnPoint.y);
                    Vector2 serverExtraction = new Vector2(decision.Result.ExtractionPoint.x, decision.Result.ExtractionPoint.y);
                    if (serverSpawn != PlayerSpawnPoint || serverExtraction != ExtractionPoint)
                    {
                        Log.Warning(
                            "[ProcedureMain] Run resume points differ from current run (spawn {0}/{1}, extraction {2}/{3}); keeping local.",
                            serverSpawn, PlayerSpawnPoint, serverExtraction, ExtractionPoint);
                    }
                }

                ApplyConsumedSnapshot(decision.Result.Looted);
                Log.Info("[ProcedureMain] Run resumed. RunId: {0}, consumed points: {1}.",
                    decision.Result.RunId, decision.Result.Consumed.Count);
            }
            catch (InvalidOperationException ex)
            {
                // 服务器回放与本地状态不一致（ResourcePointRecovery 抛出）：不静默继续，按失效处理。
                Log.Error("[ProcedureMain] Run resume state mismatch: {0}", ex);
                AbortInvalidatedRun("本局已失效");
            }
            finally
            {
                _resumeInFlight = false;
            }
        }

        /// <summary>
        /// 把服务器回放的已开箱清单应用到本局物资点实体上：开过的箱子不可再开、内容与消耗记录一致。
        /// </summary>
        private static void ApplyConsumedSnapshot(List<NetLootRecord> looted)
        {
            List<ResourcePointData> points = new List<ResourcePointData>();
            foreach (UnityGameFramework.Runtime.Entity entity in GameEntry.Entity.GetAllLoadedEntities())
            {
                if (entity != null && entity.Logic is ResourcePointLogic resourcePointLogic &&
                    resourcePointLogic.Data != null)
                {
                    points.Add(resourcePointLogic.Data);
                }
            }

            ResourcePointRecovery.Apply(points, looted);
        }
    }
}
