namespace SepCore.Net
{
    /// <summary>
    /// 断线重连恢复流程的处理动作。
    /// </summary>
    public enum RunResumeAction
    {
        /// <summary>恢复成功：校验通过，应用服务器回放的消耗状态。</summary>
        Apply,

        /// <summary>本局已失效（resume 被拒 = 服务器无活跃 run；或种子不一致）：回大厅，绝不重开新局。</summary>
        RunInvalid,

        /// <summary>网络仍不可用（超时/断线）：本局不失效，结束本次恢复尝试，玩家继续本地游玩。</summary>
        NetworkDown,
    }

    /// <summary>
    /// 一次恢复尝试的处理结果：动作 + 交回调用方的 start_run 结果（供日志与提示）。
    /// </summary>
    public sealed class RunResumeDecision
    {
        public RunResumeAction Action { get; }
        public StartRunResult Result { get; }

        public RunResumeDecision(RunResumeAction action, StartRunResult result)
        {
            Action = action;
            Result = result;
        }
    }

    /// <summary>
    /// 断线重连续局的恢复决策（纯逻辑，可静态测）。
    /// 语义（契约 §7 / §9 恢复流程）：resume 被拒 = 服务器无活跃 run = 本局已失效，回大厅且
    /// 绝不以 resume=false 重开新局；超时/断线不失效本局，结束本次恢复尝试（不自动重试）；
    /// 成功时校验服务器种子与当前局一致（不一致按失效处理，不静默继续），
    /// spawn/extraction 与当前局不一致只记日志（服务器幂等保证同 run 必同值）。
    /// </summary>
    public static class RunResumeEvaluator
    {
        /// <summary>
        /// 依据 start_run(resume=true) 的结果决定恢复流程的下一步。
        /// </summary>
        /// <param name="result">start_run(resume=true) 的结果。</param>
        /// <param name="localSeed">当前局本地持有的主种子（RoundSession.Seed）。</param>
        public static RunResumeDecision Evaluate(StartRunResult result, long localSeed)
        {
            if (result == null)
            {
                return new RunResumeDecision(RunResumeAction.NetworkDown, null);
            }

            if (!result.Success)
            {
                // 超时/断线：本局数据层仍在，只是这次没恢复成；按契约不失效、不重试。
                // 服务器明确拒绝（resume=true 且无活跃 run）：服务器已没有这局 → 本局已失效。
                // start_run 应答缺损归入 Rejected（internal 码），同按失效处理。
                return new RunResumeDecision(
                    result.Outcome == RunRequestOutcome.Rejected ? RunResumeAction.RunInvalid : RunResumeAction.NetworkDown,
                    result);
            }

            if (result.Seed != localSeed)
            {
                UnityGameFramework.Runtime.Log.Error(
                    "Run resume seed mismatch: server {0} vs local {1} (runId {2}). Run is invalid.",
                    result.Seed, localSeed, result.RunId);
                return new RunResumeDecision(RunResumeAction.RunInvalid, result);
            }

            return new RunResumeDecision(RunResumeAction.Apply, result);
        }
    }
}
