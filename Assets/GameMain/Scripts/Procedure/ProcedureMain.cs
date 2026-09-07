using GameFramework.Fsm;
using GameFramework.Procedure;
using SepCore.Definition;
using SepCore.Entity;
using SepCore.Exploration;
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
                GameEntry.UI.CloseAllLoadedUIForms();
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
    }
}
