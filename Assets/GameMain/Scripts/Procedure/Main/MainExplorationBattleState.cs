using GameFramework.Fsm;
using SepCore.Definition;
using UnityGameFramework.Runtime;

namespace SepCore.Procedure
{
    /// <summary>
    /// 主流程状态：探索与战斗循环。
    /// 负责单局核心游玩循环的维持、单局探索有效计时管理（回合制战斗期间暂停计时）、
    /// 20 分钟撤离点揭示、以及单局终止条件（撤离/全灭/超时/退出）的检测。
    /// </summary>
    public sealed class MainExplorationBattleState : FsmState<ProcedureMain>
    {
        protected override void OnEnter(IFsm<ProcedureMain> fsm)
        {
            base.OnEnter(fsm);
            Log.Info("[ProcedureMain] Entering MainExplorationBattleState...");

            if (fsm.Owner.RunStartTimeUtcMs == 0)
            {
                fsm.Owner.RunStartTimeUtcMs = System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            }

            fsm.Owner.OpenJoystickForm();
            fsm.Owner.OpenRoundHUDForm();
        }

        protected override void OnUpdate(IFsm<ProcedureMain> fsm, float elapseSeconds, float realElapseSeconds)
        {
            base.OnUpdate(fsm, elapseSeconds, realElapseSeconds);

            // 1. 若外部（如战斗全灭、主动退出或撤离交互）已触发结算请求
            if (fsm.Owner.PendingOutcome.HasValue)
            {
                Log.Info("[ProcedureMain] Pending outcome detected: {0}. Switching to SettlementState.",
                    fsm.Owner.PendingOutcome.Value);
                ChangeState<MainSettlementState>(fsm);
                return;
            }

            // 2. 检查单局有效探索计时（注意：回合制战斗中 TurnBattleComponent 自动暂停计时，不计入探索时间）
            GlobalConfig global = GameEntry.Luban.Global.Data;
            if (global != null)
            {
                long explorationElapsedMs = GameEntry.TurnBattle.RunElapsedMs;

                // 2.1 达到 20 分钟：揭示撤离点位置
                if (global.ExtractionRevealTimeMs > 0 &&
                    explorationElapsedMs >= global.ExtractionRevealTimeMs &&
                    !fsm.Owner.IsExtractionPointRevealed)
                {
                    fsm.Owner.RevealExtractionPoint();
                }

                // 2.2 达到 25 分钟：单局探索超时失败
                if (global.RunTimeLimitMs > 0 && explorationElapsedMs >= global.RunTimeLimitMs)
                {
                    Log.Info("[ProcedureMain] Run timed out: {0} ms / {1} ms exploration limit reached.",
                        explorationElapsedMs, global.RunTimeLimitMs);
                    fsm.Owner.TriggerSettlement(RoundResultType.TimedOut);
                    ChangeState<MainSettlementState>(fsm);
                    return;
                }
            }
        }

        protected override void OnLeave(IFsm<ProcedureMain> fsm, bool isShutdown)
        {
            fsm.Owner.CloseJoystickForm();
            fsm.Owner.CloseRoundHUDForm();
            base.OnLeave(fsm, isShutdown);
        }
    }
}
