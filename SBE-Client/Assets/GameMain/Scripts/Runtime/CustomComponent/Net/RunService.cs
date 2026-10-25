using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using SepCore.Definition;
using UnityGameFramework.Runtime;

namespace SepCore.Net
{
    /// <summary>
    /// 一次探索交互请求的结果。服务器拒绝、超时、断线都不是异常路径，
    /// 而是三种不同的"这次没成"：超时与断线可原样重试，
    /// <see cref="RunRequestOutcome.Rejected"/> 表示服务器明确拒绝。
    /// </summary>
    public enum RunRequestOutcome
    {
        /// <summary>服务器返回了 ok 应答。</summary>
        Success,

        /// <summary>超过等待阈值仍未收到应答：按拒绝处理，提示网络异常。</summary>
        Timeout,

        /// <summary>连接不可用（发送失败或等待期间断线）。</summary>
        Disconnected,

        /// <summary>服务器明确拒绝（点不存在 / 无活跃局 / runId 不匹配等），错误码见结果字段。</summary>
        Rejected,
    }

    /// <summary>
    /// 一次未成功的交互（供 UI 提示）。<see cref="PointId"/> 为交互主键（"r&lt;i&gt;"/"e&lt;i&gt;"），
    /// start_run 失败时为 null；<see cref="ErrCode"/> 仅 <see cref="RunRequestOutcome.Rejected"/> 有值。
    /// <see cref="Reason"/> 只会是 Timeout / Disconnected / Rejected，不会是 Success。
    /// </summary>
    public readonly struct RunInteractionRejectedArgs
    {
        public readonly string PointId;
        public readonly RunRequestOutcome Reason;
        public readonly string ErrCode;

        public RunInteractionRejectedArgs(string pointId, RunRequestOutcome reason, string errCode)
        {
            PointId = pointId;
            Reason = reason;
            ErrCode = errCode;
        }
    }

    /// <summary>
    /// start_run 的结果。成功时携带局标识、种子、出生/撤离点与已消耗点现状（重连恢复用）。
    /// </summary>
    public sealed class StartRunResult
    {
        public RunRequestOutcome Outcome { get; }

        /// <summary>局标识；未成功时为 null。</summary>
        public string RunId { get; }

        /// <summary>本局主种子（服务器分配）；未成功时为 0。</summary>
        public long Seed { get; }

        /// <summary>出生点（服务器显式值）；未成功时为 null。</summary>
        public NetVec2 SpawnPoint { get; }

        /// <summary>撤离点（服务器显式值）；未成功时为 null。</summary>
        public NetVec2 ExtractionPoint { get; }

        /// <summary>已消耗点 ID，按开箱序；未成功时为空集合。</summary>
        public List<string> Consumed { get; }

        /// <summary>已开箱清单，与 <see cref="Consumed"/> 同序；未成功时为空集合。</summary>
        public List<NetLootRecord> Looted { get; }

        /// <summary>服务器错误码（仅 Rejected 有值）。</summary>
        public string ErrCode { get; }

        /// <summary>服务器错误说明（仅 Rejected 有值，只用于日志与提示，不解析）。</summary>
        public string ErrMsg { get; }

        public bool Success => Outcome == RunRequestOutcome.Success;

        private StartRunResult(RunRequestOutcome outcome, string runId, long seed, NetVec2 spawnPoint,
            NetVec2 extractionPoint, List<string> consumed, List<NetLootRecord> looted, string errCode, string errMsg)
        {
            Outcome = outcome;
            RunId = runId;
            Seed = seed;
            SpawnPoint = spawnPoint;
            ExtractionPoint = extractionPoint;
            Consumed = consumed;
            Looted = looted;
            ErrCode = errCode;
            ErrMsg = errMsg;
        }

        /// <summary>服务器 ok 应答。</summary>
        public static StartRunResult Ok(string runId, long seed, NetVec2 spawnPoint, NetVec2 extractionPoint,
            List<string> consumed, List<NetLootRecord> looted)
        {
            return new StartRunResult(RunRequestOutcome.Success, runId, seed, spawnPoint, extractionPoint,
                consumed, looted, null, null);
        }

        /// <summary>未成功（超时 / 断线 / 服务器拒绝 / 应答不合契约）。</summary>
        public static StartRunResult Failed(RunRequestOutcome outcome, string errCode, string errMsg)
        {
            return new StartRunResult(outcome, null, 0, null, null, new List<string>(),
                new List<NetLootRecord>(), errCode, errMsg);
        }
    }

    /// <summary>
    /// encounter 的结果。成功时携带本局该敌点的权威队伍与威胁等级。
    /// </summary>
    public sealed class EncounterResult
    {
        public RunRequestOutcome Outcome { get; }

        /// <summary>本局该敌点的敌人队伍配置 ID；未成功时为 0。</summary>
        public int EnemyPartyId { get; }

        /// <summary>本局该敌点的威胁等级（枚举值）；未成功时为 0。</summary>
        public int Threat { get; }

        public string ErrCode { get; }
        public string ErrMsg { get; }

        public bool Success => Outcome == RunRequestOutcome.Success;

        private EncounterResult(RunRequestOutcome outcome, int enemyPartyId, int threat, string errCode, string errMsg)
        {
            Outcome = outcome;
            EnemyPartyId = enemyPartyId;
            Threat = threat;
            ErrCode = errCode;
            ErrMsg = errMsg;
        }

        /// <summary>服务器 ok 应答。</summary>
        public static EncounterResult Ok(int enemyPartyId, int threat)
        {
            return new EncounterResult(RunRequestOutcome.Success, enemyPartyId, threat, null, null);
        }

        /// <summary>未成功（超时 / 断线 / 服务器拒绝）。</summary>
        public static EncounterResult Failed(RunRequestOutcome outcome, string errCode, string errMsg)
        {
            return new EncounterResult(outcome, 0, 0, errCode, errMsg);
        }
    }

    /// <summary>
    /// loot 的结果。成功时携带该点的掉落清单（顺序即掉落序）。
    /// </summary>
    public sealed class LootResult
    {
        public RunRequestOutcome Outcome { get; }

        /// <summary>掉落清单；未成功时为空集合。</summary>
        public List<int> ItemIds { get; }

        public string ErrCode { get; }
        public string ErrMsg { get; }

        public bool Success => Outcome == RunRequestOutcome.Success;

        private LootResult(RunRequestOutcome outcome, List<int> itemIds, string errCode, string errMsg)
        {
            Outcome = outcome;
            ItemIds = itemIds;
            ErrCode = errCode;
            ErrMsg = errMsg;
        }

        /// <summary>服务器 ok 应答。</summary>
        public static LootResult Ok(List<int> itemIds)
        {
            return new LootResult(RunRequestOutcome.Success, itemIds, null, null);
        }

        /// <summary>未成功（超时 / 断线 / 服务器拒绝）。</summary>
        public static LootResult Failed(RunRequestOutcome outcome, string errCode, string errMsg)
        {
            return new LootResult(outcome, new List<int>(), errCode, errMsg);
        }
    }

    /// <summary>
    /// 探索局域服务：封装 start_run / encounter / loot 三条消息的请求发起、超时判定、
    /// 断线放行与错误码归一。失败原因经返回值与 <see cref="InteractionRejected"/> 事件出口传达，
    /// 调用方（UI/流程）据此提示；请求本身不阻塞移动与渲染。
    /// 纯域服务：只依赖 <see cref="NetRpc"/>、<see cref="DifficultyTier"/> 与日志，
    /// 不引用任何 UI / Procedure 类型。
    /// </summary>
    public sealed class RunService
    {
        /// <summary>默认等待阈值（毫秒）。</summary>
        public const int DefaultTimeoutMilliseconds = 3000;

        /// <summary>交互被拒绝/超时/断线时给玩家看的统一文案。</summary>
        public const string RejectionMessage = "网络延迟较大，请检查网络连接";

        private readonly NetRpc _rpc;
        private readonly int _timeoutMilliseconds;
        private readonly Func<int, UniTask> _delay;
        // 与服务器下发的 consumed 同序（按开箱先后）；点位数很小，线性查重足够。
        private readonly List<string> _consumed = new List<string>();

        /// <summary>
        /// 构造：超时阈值与延迟实现由调用方注入（EditMode 测试注入不真等的延迟）。
        /// </summary>
        public RunService(NetRpc rpc, int timeoutMilliseconds = DefaultTimeoutMilliseconds, Func<int, UniTask> delay = null)
        {
            _rpc = rpc;
            _timeoutMilliseconds = timeoutMilliseconds;
            // 网络等待按真实时间计：暂停（timeScale=0）时阈值也应继续走。
            _delay = delay ?? (ms => UniTask.Delay(ms, DelayType.Realtime));
        }

        /// <summary>当前等待阈值（毫秒）。</summary>
        public int TimeoutMilliseconds => _timeoutMilliseconds;

        /// <summary>当前活跃局的标识；未开局或开局失败时为 null。</summary>
        public string RunId { get; private set; }

        /// <summary>当前活跃局的主种子；未开局时为 0。</summary>
        public long Seed { get; private set; }

        /// <summary>是否已持有活跃局（start_run 成功后为 true）。</summary>
        public bool HasActiveRun => !string.IsNullOrEmpty(RunId);

        /// <summary>已消耗（开过箱）的点位 ID，按开箱先后排列（与服务器下发的顺序一致）。</summary>
        public IReadOnlyList<string> ConsumedPointIds => _consumed;

        /// <summary>
        /// 一次交互未成功（超时 / 断线 / 服务器拒绝）。start_run 失败时 PointId 为 null。
        /// 供 UI 提示使用；本服务只做提示出口，不改变任何游戏状态。
        /// </summary>
        public event Action<RunInteractionRejectedArgs> InteractionRejected;

        /// <summary>
        /// 该点位是否已被开箱（本地镜像服务器下发的 consumed，成功 loot 后就地补记）。
        /// </summary>
        public bool IsConsumed(string pointId)
        {
            return pointId != null && _consumed.Contains(pointId);
        }

        /// <summary>
        /// 清空本局缓存（runId / seed / 已消耗点）。单局结算后调用，
        /// 避免下一局还没成功 start_run 时被误当成"仍持有活跃局"上报或交互。
        /// </summary>
        public void ResetRun()
        {
            RunId = null;
            Seed = 0;
            _consumed.Clear();
        }

        /// <summary>
        /// 开局：服务器分配种子并生成全图内容。resume=true 是断线重连的恢复路径，
        /// 无活跃局时服务器拒绝（客户端不得据此误建新局）。
        /// 成功后就地更新缓存的 runId/seed/consumed。
        /// </summary>
        public async UniTask<StartRunResult> StartRunAsync(DifficultyTier difficulty, bool resume = false)
        {
            NetEnvelope request = NetEnvelope.Create(NetMsg.StartRun);
            request.difficulty = (int)difficulty;
            request.resume = resume;

            RunResponse response = await RequestAsync(request, null);
            if (!response.Success)
            {
                return StartRunResult.Failed(response.Outcome, response.ErrCode, response.ErrMsg);
            }

            NetEnvelope body = response.Envelope;
            if (string.IsNullOrEmpty(body.runId))
            {
                Log.Error("start_run response is missing runId.");
                return FailInvalidStartRun();
            }

            if (body.seed <= 0)
            {
                Log.Error("start_run response has invalid seed {0} (runId {1}).", body.seed, body.runId);
                return FailInvalidStartRun();
            }

            if (body.spawnPoint == null || body.extractionPoint == null)
            {
                Log.Error("start_run response is missing spawnPoint or extractionPoint (runId {0}).", body.runId);
                return FailInvalidStartRun();
            }

            // Go 侧 consumed/looted 带 omitempty：空清单不下发，缺失即空集合。
            List<string> consumed = body.consumed ?? new List<string>();
            List<NetLootRecord> looted = body.looted ?? new List<NetLootRecord>();

            RunId = body.runId;
            Seed = body.seed;
            _consumed.Clear();
            foreach (string pointId in consumed)
            {
                if (!_consumed.Contains(pointId))
                {
                    _consumed.Add(pointId);
                }
            }

            Log.Info("Run started. RunId: {0}, Seed: {1}, Consumed: {2}.", RunId, Seed, _consumed.Count);
            return StartRunResult.Ok(RunId, Seed, body.spawnPoint, body.extractionPoint, consumed, looted);
        }

        /// <summary>
        /// 撞敌：请求该敌点的权威队伍构成。未开局时 runId 为空，交由服务器按 rejected 拒绝
        /// （本地不伪造错误码）。encounter 不消耗点，重复请求返回相同结果。
        /// </summary>
        public async UniTask<EncounterResult> EncounterAsync(string pointId)
        {
            NetEnvelope request = NetEnvelope.Create(NetMsg.Encounter);
            request.runId = RunId;
            request.pointId = pointId;

            RunResponse response = await RequestAsync(request, pointId);
            if (!response.Success)
            {
                return EncounterResult.Failed(response.Outcome, response.ErrCode, response.ErrMsg);
            }

            return EncounterResult.Ok(response.Envelope.enemyPartyId, response.Envelope.threat);
        }

        /// <summary>
        /// 开箱：请求该物资点的掉落清单。首次成功即由服务器标记消耗，重试回放同一清单。
        /// 成功后把点位补记进本地 consumed 镜像。
        /// </summary>
        public async UniTask<LootResult> LootAsync(string pointId)
        {
            NetEnvelope request = NetEnvelope.Create(NetMsg.Loot);
            request.runId = RunId;
            request.pointId = pointId;

            RunResponse response = await RequestAsync(request, pointId);
            if (!response.Success)
            {
                return LootResult.Failed(response.Outcome, response.ErrCode, response.ErrMsg);
            }

            // Go 侧 itemIds 带 omitempty：空清单不下发，缺失即空清单。
            List<int> itemIds = response.Envelope.itemIds ?? new List<int>();
            if (!IsConsumed(pointId))
            {
                // 幂等重试（服务器回放同一清单）不重复记，保持与服务器 consumed 同序。
                _consumed.Add(pointId);
            }

            return LootResult.Ok(itemIds);
        }

        /// <summary>
        /// 发请求并归一结果：有应答 = 成功或服务器拒绝；发送失败/等待期间断线 = Disconnected；
        /// 超过阈值 = Timeout（并放弃等待项，迟到应答不匹配任何请求）。
        /// </summary>
        private async UniTask<RunResponse> RequestAsync(NetEnvelope request, string pointId)
        {
            UniTask<NetEnvelope> send = _rpc.SendAsync(request);
            // SendAsync 在首个 await 前已分配 rid 并尝试发送，因此这里的 rid 就是本次请求的关联键。
            long rid = request.rid;
            // 选 (UniTask<T>, UniTask) 重载：结果左侧为应答、右侧表示"到点了"。
            (bool sendCompleted, NetEnvelope response) =
                await UniTask.WhenAny<NetEnvelope>(send, _delay(_timeoutMilliseconds));

            if (!sendCompleted)
            {
                // 超时：放掉等待项，避免 pending 表悬挂。rid 单调递增且不复用，迟到应答不会串到后续请求。
                _rpc.Abandon(rid);
                return Fail(request, pointId, RunRequestOutcome.Timeout, null, null);
            }

            if (response == null)
            {
                // 发送失败，或等待期间断线（NetRpc 以 null 放行全部 pending）。
                return Fail(request, pointId, RunRequestOutcome.Disconnected, null, null);
            }

            if (!response.IsOk)
            {
                return Fail(request, pointId, RunRequestOutcome.Rejected, response.code, response.msg);
            }

            return new RunResponse(RunRequestOutcome.Success, response, null, null);
        }

        private RunResponse Fail(NetEnvelope request, string pointId, RunRequestOutcome outcome, string errCode, string errMsg)
        {
            Log.Warning("Run interaction {0} on {1} failed: {2} ({3}).", request.t, pointId ?? "none",
                outcome, errCode ?? errMsg ?? "no response");
            InteractionRejected?.Invoke(new RunInteractionRejectedArgs(pointId, outcome, errCode));
            return new RunResponse(outcome, null, errCode, errMsg);
        }

        /// <summary>
        /// start_run 收到 ok 但字段不合契约（服务器缺陷）：按拒绝出口让调用方中止进局，不静默兜底。
        /// 协议缺损归入 <see cref="RunRequestOutcome.Rejected"/> 并带 internal 码以示区别。
        /// </summary>
        private StartRunResult FailInvalidStartRun()
        {
            const string reason = "start_run response is invalid";
            InteractionRejected?.Invoke(
                new RunInteractionRejectedArgs(null, RunRequestOutcome.Rejected, NetErrCode.Internal));
            return StartRunResult.Failed(RunRequestOutcome.Rejected, NetErrCode.Internal, reason);
        }

        /// <summary>一次请求的归一结果：成功时携带 ok 应答，失败时只有原因与错误码。</summary>
        private readonly struct RunResponse
        {
            public readonly RunRequestOutcome Outcome;
            public readonly NetEnvelope Envelope;
            private readonly string _errCode;
            private readonly string _errMsg;

            public RunResponse(RunRequestOutcome outcome, NetEnvelope envelope, string errCode, string errMsg)
            {
                Outcome = outcome;
                Envelope = envelope;
                _errCode = errCode;
                _errMsg = errMsg;
            }

            public bool Success => Outcome == RunRequestOutcome.Success;
            public string ErrCode => _errCode;
            public string ErrMsg => _errMsg;
        }
    }
}
