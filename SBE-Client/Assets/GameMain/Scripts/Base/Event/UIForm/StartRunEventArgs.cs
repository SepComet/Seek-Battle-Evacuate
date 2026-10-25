using GameFramework;
using GameFramework.Event;

namespace SepCore.Base
{
    /// <summary>
    /// 开始单局探索请求事件。
    /// 由战备界面（DeploymentForm）点击开始按钮触发，通知主菜单流程（ProcedureMenu）切场景进入单局。
    /// </summary>
    public sealed class StartRunEventArgs : GameEventArgs
    {
        public static int EventId => typeof(StartRunEventArgs).GetHashCode();

        public override int Id => EventId;

        public static StartRunEventArgs Create()
        {
            return ReferencePool.Acquire<StartRunEventArgs>();
        }

        public override void Clear()
        {
        }
    }
}
