using GameFramework.Event;
using GameFramework.Fsm;
using GameFramework.Procedure;
using SepCore.Base;
using SepCore.Definition;
using SepCore.Exploration;
using SepCore.Net;
using SepCore.UI;
using UnityEngine;
using UnityGameFramework.Runtime;

namespace SepCore.Procedure
{
    public class ProcedureMenu : ProcedureBase
    {
        private bool _startGameRequested = false;
        private bool _logoutRequested = false;
        private bool _leavingMenu;

        protected override void OnInit(IFsm<IProcedureManager> procedureOwner)
        {
            base.OnInit(procedureOwner);
        }

        protected override void OnEnter(IFsm<IProcedureManager> procedureOwner)
        {
            base.OnEnter(procedureOwner);

            CharacterInputBridge.Reset();

            _startGameRequested = false;
            _logoutRequested = false;
            _leavingMenu = false;
            GameEntry.Event.Subscribe(StartRunEventArgs.EventId, OnStartRun);
            GameEntry.Event.Subscribe(LogoutRequestedEventArgs.EventId, OnLogoutRequested);

            GameEntry.UI.OpenUIForm(UIFormType.LobbyForm);
            Log.Info(Application.persistentDataPath);
        }

        protected override void OnUpdate(IFsm<IProcedureManager> procedureOwner, float elapseSeconds,
            float realElapseSeconds)
        {
            base.OnUpdate(procedureOwner, elapseSeconds, realElapseSeconds);

            if (_startGameRequested)
            {
                _startGameRequested = false;
                procedureOwner.SetData<VarInt32>("NextSceneId", (int)SceneType.Main);
                ChangeState<ProcedureChangeScene>(procedureOwner);
            }

            if (_logoutRequested)
            {
                _logoutRequested = false;
                // 断开连接、清除本地会话与缓存存档，回到登录流程。
                // 本地缓存必须重置：随后新注册的账号走 migrate 时上传的才是全新开局，而非上一账号的残留状态。
                GameEntry.Net.Disconnect();
                AccountSession.ClearToken();
                GameEntry.Save.ResetLocalCache();
                ChangeState<ProcedureLogin>(procedureOwner);
            }
        }

        protected override void OnLeave(IFsm<IProcedureManager> procedureOwner, bool isShutdown)
        {
            GameEntry.Event.Unsubscribe(StartRunEventArgs.EventId, OnStartRun);
            GameEntry.Event.Unsubscribe(LogoutRequestedEventArgs.EventId, OnLogoutRequested);
            GameEntry.UI.CloseSceneUIForms();

            base.OnLeave(procedureOwner, isShutdown);
        }

        protected override void OnDestroy(IFsm<IProcedureManager> procedureOwner)
        {
            base.OnDestroy(procedureOwner);
        }

        private void OnStartRun(object sender, GameEventArgs e)
        {
            if (_leavingMenu) return;
            _leavingMenu = true;
            UGuiForm deployment = GameEntry.UI.GetUIForm(UIFormType.HomeForm);
            if (deployment == null)
            {
                Log.Error("Cannot leave menu without the deployment form.");
                _leavingMenu = false;
                return;
            }
            deployment.PlayExitTransition(() => _startGameRequested = true);
        }

        private void OnLogoutRequested(object sender, GameEventArgs e)
        {
            if (_leavingMenu) return;
            _leavingMenu = true;
            _logoutRequested = true;
        }
    }
}
