using GameFramework.Event;
using GameFramework.Fsm;
using SepCore.Base;
using SepCore.Definition;
using UnityGameFramework.Runtime;

namespace SepCore.Procedure
{
    /// <summary>
    /// 主流程状态：探索与战斗循环。
    /// 负责单局核心游玩循环的维持、单局探索有效计时管理（回合制战斗期间暂停计时）、
    /// 以及单局终止条件（撤离/全灭/超时/退出）的检测。
    /// </summary>
    public sealed class MainExplorationBattleState : FsmState<ProcedureMain>
    {
        private IFsm<ProcedureMain> _fsm;

        protected override void OnEnter(IFsm<ProcedureMain> fsm)
        {
            base.OnEnter(fsm);
            _fsm = fsm;
            Log.Info("[ProcedureMain] Entering MainExplorationBattleState...");

            if (fsm.Owner.RunStartTimeUtcMs == 0)
            {
                fsm.Owner.RunStartTimeUtcMs = System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            }

            GameEntry.Event.Subscribe(EvacuationCompletedEventArgs.EventId, OnEvacuationCompleted);
            GameEntry.Event.Subscribe(BattleTotalDefeatEventArgs.EventId, OnBattleTotalDefeat);

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

            // 1.1 双重兜底：若战斗已结束且为 TotalDefeat，但尚未触发结算
            if (GameEntry.TurnBattle != null && !GameEntry.TurnBattle.IsBattleActive &&
                GameEntry.TurnBattle.LastOutcome == BattleOutcomeType.TotalDefeat)
            {
                Log.Info("[ProcedureMain] Battle total defeat detected without active battle, triggering defeat settlement.");
                fsm.Owner.TriggerSettlement(RoundResultType.Defeated);
                ChangeState<MainSettlementState>(fsm);
                return;
            }

            // 2. 检查单局有效探索计时（注意：回合制战斗中 TurnBattleComponent 自动暂停计时，不计入探索时间）
            GlobalConfig global = GameEntry.Luban.Global.Data;
            if (global != null)
            {
                long explorationElapsedMs = GameEntry.TurnBattle.RunElapsedMs;

                // 2.1 达到 25 分钟：单局探索超时失败
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
            GameEntry.Event.Unsubscribe(EvacuationCompletedEventArgs.EventId, OnEvacuationCompleted);
            GameEntry.Event.Unsubscribe(BattleTotalDefeatEventArgs.EventId, OnBattleTotalDefeat);
            _fsm = null;
            fsm.Owner.CloseJoystickForm();
            fsm.Owner.CloseRoundHUDForm();
            base.OnLeave(fsm, isShutdown);
        }

        private void OnEvacuationCompleted(object sender, GameEventArgs e)
        {
            if (_fsm == null)
            {
                return;
            }

            Log.Info("[ProcedureMain] Evacuation completed at extraction point, requesting settlement.");
            _fsm.Owner.TriggerSettlement(RoundResultType.Extracted);
        }

        private void OnBattleTotalDefeat(object sender, GameEventArgs e)
        {
            if (_fsm == null)
            {
                return;
            }

            Log.Info("[ProcedureMain] Battle total defeat event received, requesting defeat settlement.");
            _fsm.Owner.TriggerSettlement(RoundResultType.Defeated);
        }
    }
}
