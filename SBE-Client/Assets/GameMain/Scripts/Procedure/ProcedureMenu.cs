using GameFramework.Event;
using GameFramework.Fsm;
using GameFramework.Procedure;
using SepCore.Base;
using SepCore.Definition;
using SepCore.Exploration;
using SepCore.UI;
using UnityEngine;
using UnityGameFramework.Runtime;

namespace SepCore.Procedure
{
    public class ProcedureMenu : ProcedureBase
    {
        private bool _startGameRequested = false;

        protected override void OnInit(IFsm<IProcedureManager> procedureOwner)
        {
            base.OnInit(procedureOwner);
        }

        protected override void OnEnter(IFsm<IProcedureManager> procedureOwner)
        {
            base.OnEnter(procedureOwner);

            CharacterInputBridge.Reset();

            _startGameRequested = false;
            GameEntry.Event.Subscribe(StartRunEventArgs.EventId, OnStartRun);

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
        }

        protected override void OnLeave(IFsm<IProcedureManager> procedureOwner, bool isShutdown)
        {
            GameEntry.Event.Unsubscribe(StartRunEventArgs.EventId, OnStartRun);
            GameEntry.UI.CloseAllLoadedUIForms();

            base.OnLeave(procedureOwner, isShutdown);
        }

        protected override void OnDestroy(IFsm<IProcedureManager> procedureOwner)
        {
            base.OnDestroy(procedureOwner);
        }

        private void OnStartRun(object sender, GameEventArgs e)
        {
            _startGameRequested = true;
        }
    }
}
