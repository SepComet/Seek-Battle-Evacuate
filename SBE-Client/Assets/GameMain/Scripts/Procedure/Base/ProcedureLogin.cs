using GameFramework.Fsm;
using GameFramework.Procedure;
using Cysharp.Threading.Tasks;
using SepCore.Base;
using SepCore.Net;
using UnityGameFramework.Runtime;
using ProcedureOwner = GameFramework.Fsm.IFsm<GameFramework.Procedure.IProcedureManager>;

namespace SepCore.Procedure
{
    /// <summary>
    /// 登录过程：在资源预载完成后、进入大厅前完成
    /// 本地缓存读取 → 连接 → 匿名注册/认证 → 首登迁移/初始化 → 权威快照落地。
    /// 强制在线：失败则周期重试，不进入大厅。
    /// </summary>
    public class ProcedureLogin : ProcedureBase
    {
        private bool _leaving;

        protected override void OnEnter(ProcedureOwner procedureOwner)
        {
            base.OnEnter(procedureOwner);
            _leaving = false;
            if (GameEntry.Net != null)
            {
                GameEntry.Net.SuspendAutoLogin = true;
            }

            Log.Info("[ProcedureLogin] Waiting for server...");
            RunAsync(procedureOwner);
        }

        private async UniTaskVoid RunAsync(ProcedureOwner procedureOwner)
        {
            // 1. 本地缓存（迁移源）；无存档则初始化新档数据（随后作为迁移上传）
            GameEntry.Save.Load();
            if (!GameEntry.Save.HasSave && !GameEntry.Save.IsReady)
            {
                GameEntry.Save.CreateNewGame();
            }

            // 2. 登录循环（强制在线；由本过程独占驱动，NetComponent 的自动重连已挂起）
            while (!_leaving)
            {
                if (GameEntry.Net != null)
                {
                    if (await GameEntry.Net.TryLoginAsync())
                    {
                        procedureOwner.SetData<VarInt32>("NextSceneId", (int)SceneType.Menu);
                        ChangeState<ProcedureChangeScene>(procedureOwner);
                        return;
                    }

                    Log.Warning("[ProcedureLogin] Login failed; retrying...");
                }

                await UniTask.Delay(2000);
            }
        }

        protected override void OnLeave(ProcedureOwner procedureOwner, bool isShutdown)
        {
            _leaving = true;
            if (GameEntry.Net != null)
            {
                GameEntry.Net.SuspendAutoLogin = false;
            }

            base.OnLeave(procedureOwner, isShutdown);
        }
    }
}
