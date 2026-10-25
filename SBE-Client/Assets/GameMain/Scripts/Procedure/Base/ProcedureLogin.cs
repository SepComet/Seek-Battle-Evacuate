using GameFramework.Fsm;
using GameFramework.Procedure;
using Cysharp.Threading.Tasks;
using SepCore.Base;
using SepCore.Definition;
using SepCore.Net;
using SepCore.UI;
using UnityGameFramework.Runtime;
using ProcedureOwner = GameFramework.Fsm.IFsm<GameFramework.Procedure.IProcedureManager>;

namespace SepCore.Procedure
{
    /// <summary>
    /// 登录过程：在资源预载完成后、进入大厅前完成
    /// 本地缓存读取 → 缓存 token 静默续登 → 失效则打开登录表单走账密注册/登录 →
    /// 首登迁移/初始化（网络层内完成）→ 权威快照落地。
    /// 强制在线：静默续登网络故障周期重试；账密错误由登录表单提示，不进入大厅。
    /// </summary>
    public class ProcedureLogin : ProcedureBase
    {
        private const int SilentRetryDelayMilliseconds = 2000;
        private const int InteractivePollDelayMilliseconds = 500;

        private bool _leaving;

        protected override void OnEnter(ProcedureOwner procedureOwner)
        {
            base.OnEnter(procedureOwner);
            _leaving = false;
            // 独立常驻组，与场景功能页分开；Form 自带 2000 深度偏移，最终排序 32000。
            if (!GameEntry.UI.HasUIGroup(UIComponentExtension.NetworkStatusGroupName))
            {
                GameEntry.UI.AddUIGroup(UIComponentExtension.NetworkStatusGroupName, 3);
            }
            GameEntry.UI.OpenUIForm(UIFormType.NetworkStatusForm);
            if (GameEntry.Net != null)
            {
                GameEntry.Net.SuspendAutoLogin = true;
            }

            Log.Info("[ProcedureLogin] Waiting for server...");
            RunAsync(procedureOwner).Forget();
        }

        private async UniTaskVoid RunAsync(ProcedureOwner procedureOwner)
        {
            // 1. 本地缓存（迁移源）；无存档则初始化新档数据（随后作为迁移上传）
            GameEntry.Save.Load();
            if (!GameEntry.Save.HasSave && !GameEntry.Save.IsReady)
            {
                GameEntry.Save.CreateNewGame();
            }

            // 2. 静默续登：有缓存 token 时由本过程独占驱动（NetComponent 的自动重连已挂起）
            while (!_leaving)
            {
                if (GameEntry.Net != null)
                {
                    NetLoginOutcome outcome = await GameEntry.Net.TryLoginAsync();
                    if (outcome == NetLoginOutcome.Success)
                    {
                        EnterMenu(procedureOwner);
                        return;
                    }
                    if (outcome == NetLoginOutcome.AuthRejected)
                    {
                        // token 失效或无缓存会话 → 转入账密登录
                        break;
                    }

                    Log.Warning("[ProcedureLogin] Server unreachable; retrying token login...");
                }

                await UniTask.Delay(SilentRetryDelayMilliseconds);
            }

            // 3. 交互登录：登录表单驱动账密注册/登录，本过程轮询 OnlineReady
            GameEntry.UI.OpenUIForm(UIFormType.LoginForm);
            Log.Info("[ProcedureLogin] Waiting for account login...");
            while (!_leaving && !GameEntry.Net.OnlineReady)
            {
                await UniTask.Delay(InteractivePollDelayMilliseconds);
            }
            if (!_leaving)
            {
                EnterMenu(procedureOwner);
            }
        }

        private void EnterMenu(ProcedureOwner procedureOwner)
        {
            procedureOwner.SetData<VarInt32>("NextSceneId", (int)SceneType.Menu);
            ChangeState<ProcedureChangeScene>(procedureOwner);
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
