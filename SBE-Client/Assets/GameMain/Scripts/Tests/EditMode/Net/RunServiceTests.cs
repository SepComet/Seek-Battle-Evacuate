using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using SepCore.Definition;
using SepCore.Net;
using UnityEngine.TestTools;

namespace SepCore.Tests
{
    /// <summary>
    /// 探索局域服务测试：假连接 + 迷你 run 服务器（复刻服务端契约 §7 的应答形状）。
    /// 覆盖三条消息的成功路径、超时判定（阈值可配）、断线放行、服务器拒绝与迟到应答隔离。
    /// 延迟经构造注入，编辑器中不真等；超时由测试显式放行 gate 驱动。
    /// </summary>
    [TestFixture]
    public class RunServiceTests
    {
        [OneTimeSetUp]
        public void SetUpHelpers()
        {
            // EditMode 下没有 GameEntry 启动链，手动注册 UGF 的 JSON / 日志 helper。
            GameFramework.Utility.Json.SetJsonHelper(new UnityGameFramework.Runtime.DefaultJsonHelper());
            GameFramework.GameFrameworkLog.SetLogHelper(new UnityGameFramework.Runtime.DefaultLogHelper());
        }

        /// <summary>协议版本必须与服务器一致（契约 §7），回退即断代。</summary>
        [Test]
        public void ProtocolVersionIsV5()
        {
            Assert.That(NetMsg.Version, Is.EqualTo(5));
            Assert.That(NetMsg.StartRun, Is.EqualTo("start_run"));
            Assert.That(NetMsg.Encounter, Is.EqualTo("encounter"));
            Assert.That(NetMsg.Loot, Is.EqualTo("loot"));
        }

        [Test]
        public void DefaultTimeoutIsThreeSeconds()
        {
            Ctx ctx = CreateService();
            Assert.That(ctx.Service.TimeoutMilliseconds, Is.EqualTo(3000));
            Assert.That(ctx.Service.HasActiveRun, Is.False);
            Assert.That(ctx.Service.RunId, Is.Null);
        }

        /// <summary>
        /// 客户端信封的线上字段名与契约 §7 逐字一致（服务端 protocol_test.go 锁同一组名字）。
        /// 改名即断代，因此既查序列化后的 JSON 文本，也查反序列化能读回。
        /// </summary>
        [Test]
        public void EnvelopeWireNamesMatchProtocolV5()
        {
            NetEnvelope env = NetEnvelope.Create(NetMsg.StartRun);
            env.resume = true;
            env.pointId = "e3";
            env.spawnPoint = new NetVec2(1, -16);
            env.extractionPoint = new NetVec2(30, -3);
            env.consumed = new List<string> { "r2" };
            env.looted = new List<NetLootRecord>
            {
                new NetLootRecord { pointId = "r2", itemIds = new List<int> { 5001, 5003 } },
            };
            env.enemyPartyId = 4101;
            env.threat = 2;
            env.itemIds = new List<int> { 5001, 5003 };
            env.battleLoot = new List<NetStack> { new NetStack(5001, 2) };
            env.runDifficulty = 2;

            string json = GameFramework.Utility.Json.ToJson(env);
            string[] names =
            {
                "\"v\":5", "\"resume\"", "\"pointId\"", "\"spawnPoint\"", "\"extractionPoint\"", "\"runDifficulty\":2",
                "\"consumed\"", "\"looted\"", "\"enemyPartyId\"", "\"threat\"", "\"itemIds\"", "\"battleLoot\"",
            };
            foreach (string name in names)
            {
                Assert.That(json, Does.Contain(name), json);
            }

            NetEnvelope round = GameFramework.Utility.Json.ToObject<NetEnvelope>(json);
            Assert.That(round.v, Is.EqualTo(5));
            Assert.That(round.resume, Is.True);
            Assert.That(round.pointId, Is.EqualTo("e3"));
            Assert.That(round.spawnPoint.x, Is.EqualTo(1f));
            Assert.That(round.extractionPoint.y, Is.EqualTo(-3f));
            Assert.That(round.runDifficulty, Is.EqualTo(2));
            Assert.That(round.consumed.ToArray(), Is.EqualTo(new[] { "r2" }));
            Assert.That(round.looted[0].pointId, Is.EqualTo("r2"));
            Assert.That(round.looted[0].itemIds.ToArray(), Is.EqualTo(new[] { 5001, 5003 }));
            Assert.That(round.enemyPartyId, Is.EqualTo(4101));
            Assert.That(round.threat, Is.EqualTo(2));
            Assert.That(round.itemIds.ToArray(), Is.EqualTo(new[] { 5001, 5003 }));
            Assert.That(round.battleLoot[0].itemId, Is.EqualTo(5001));
            Assert.That(round.battleLoot[0].count, Is.EqualTo(2));
        }

        [UnityTest]
        public IEnumerator StartRunSendsDifficultyAndCachesRunState()
        {
            Ctx ctx = CreateService();
            StartRunResult result = null;
            yield return ctx.Service.StartRunAsync(DifficultyTier.Tier2)
                .ContinueWith(r => result = r).ToCoroutine();

            Assert.That(result, Is.Not.Null);
            Assert.That(result.Success, Is.True);
            Assert.That(result.RunId, Is.EqualTo(FakeRunServer.DefaultRunId));
            Assert.That(result.Seed, Is.EqualTo(FakeRunServer.DefaultSeed));
            Assert.That(result.Difficulty, Is.EqualTo(DifficultyTier.Tier2));
            Assert.That(result.SpawnPoint.x, Is.EqualTo(1f));
            Assert.That(result.SpawnPoint.y, Is.EqualTo(-16f));
            Assert.That(result.ExtractionPoint.x, Is.EqualTo(30f));
            Assert.That(result.ExtractionPoint.y, Is.EqualTo(-3f));
            Assert.That(result.Consumed, Is.Empty);
            Assert.That(result.Looted, Is.Empty);

            // 请求携带难度（resume 缺省 false）与 v4 版本号。
            NetEnvelope request = ctx.Connection.Sent.Single(e => e.t == NetMsg.StartRun);
            Assert.That(request.v, Is.EqualTo(NetMsg.Version));
            Assert.That(request.difficulty, Is.EqualTo((int)DifficultyTier.Tier2));
            Assert.That(request.resume, Is.False);

            // 本地缓存供后续消息与 ticket 07 恢复使用。
            Assert.That(ctx.Service.HasActiveRun, Is.True);
            Assert.That(ctx.Service.RunId, Is.EqualTo(FakeRunServer.DefaultRunId));
            Assert.That(ctx.Service.Seed, Is.EqualTo(FakeRunServer.DefaultSeed));
            Assert.That(ctx.Service.Difficulty, Is.EqualTo(DifficultyTier.Tier2));
            Assert.That(ctx.Rejected, Is.Empty);
        }

        [UnityTest]
        public IEnumerator StartRunResumeAppliesConsumedSnapshot()
        {
            Ctx ctx = CreateService();
            // 服务器侧已有活跃 run：r0 已开箱。
            ctx.Server.Active = true;
            ctx.Server.MarkLooted("r0");

            StartRunResult result = null;
            yield return ctx.Service.StartRunAsync(DifficultyTier.Tier1, resume: true)
                .ContinueWith(r => result = r).ToCoroutine();

            Assert.That(result.Success, Is.True);
            Assert.That(result.Consumed.ToArray(), Is.EqualTo(new[] { "r0" }));
            Assert.That(result.Looted, Has.Count.EqualTo(1));
            Assert.That(result.Looted[0].pointId, Is.EqualTo("r0"));
            Assert.That(result.Looted[0].itemIds.ToArray(), Is.EqualTo(FakeRunServer.R0Items));

            NetEnvelope request = ctx.Connection.Sent.Single(e => e.t == NetMsg.StartRun);
            Assert.That(request.resume, Is.True);
            Assert.That(ctx.Service.IsConsumed("r0"), Is.True);
            Assert.That(ctx.Service.IsConsumed("r1"), Is.False);
        }

        [UnityTest]
        public IEnumerator EncounterReturnsServerPartyAndThreat()
        {
            Ctx ctx = CreateService();
            yield return ctx.Service.StartRunAsync(DifficultyTier.Tier1).ToCoroutine();

            EncounterResult result = null;
            yield return ctx.Service.EncounterAsync("e0")
                .ContinueWith(r => result = r).ToCoroutine();

            Assert.That(result.Success, Is.True);
            Assert.That(result.EnemyPartyId, Is.EqualTo(4101));
            Assert.That(result.Threat, Is.EqualTo(2));

            NetEnvelope request = ctx.Connection.Sent.Single(e => e.t == NetMsg.Encounter);
            Assert.That(request.runId, Is.EqualTo(FakeRunServer.DefaultRunId));
            Assert.That(request.pointId, Is.EqualTo("e0"));
            Assert.That(request.v, Is.EqualTo(NetMsg.Version));
        }

        [UnityTest]
        public IEnumerator LootReturnsItemsAndMarksPointConsumed()
        {
            Ctx ctx = CreateService();
            yield return ctx.Service.StartRunAsync(DifficultyTier.Tier1).ToCoroutine();

            LootResult result = null;
            yield return ctx.Service.LootAsync("r0")
                .ContinueWith(r => result = r).ToCoroutine();

            Assert.That(result.Success, Is.True);
            Assert.That(result.ItemIds.ToArray(), Is.EqualTo(FakeRunServer.R0Items));
            NetEnvelope request = ctx.Connection.Sent.Single(e => e.t == NetMsg.Loot);
            Assert.That(request.runId, Is.EqualTo(FakeRunServer.DefaultRunId));
            Assert.That(request.pointId, Is.EqualTo("r0"));
            Assert.That(ctx.Service.IsConsumed("r0"), Is.True);
        }

        /// <summary>
        /// 结算后清空缓存：下一局还没成功 start_run 时不得被当成"仍持有活跃局"，
        /// 否则结算会上报上局的 runId、交互也会带上过期 runId。
        /// </summary>
        [UnityTest]
        public IEnumerator ResetRun_ClearsRunIdSeedAndConsumed()
        {
            Ctx ctx = CreateService();
            yield return ctx.Service.StartRunAsync(DifficultyTier.Tier1).ToCoroutine();
            yield return ctx.Service.LootAsync("r0").ToCoroutine();
            Assert.That(ctx.Service.HasActiveRun, Is.True);
            Assert.That(ctx.Service.IsConsumed("r0"), Is.True);

            ctx.Service.ResetRun();

            Assert.That(ctx.Service.HasActiveRun, Is.False);
            Assert.That(ctx.Service.RunId, Is.Null);
            Assert.That(ctx.Service.Seed, Is.EqualTo(0L));
            Assert.That(ctx.Service.ConsumedPointIds, Is.Empty);
            Assert.That(ctx.Service.IsConsumed("r0"), Is.False);
        }

        [UnityTest]
        public IEnumerator TimeoutReportsTimeoutAndRaisesEvent()
        {
            Ctx ctx = CreateService(holdResponses: true);
            LootResult result = null;
            UniTask<LootResult> pending = ctx.Service.LootAsync("r0");
            ctx.DelayGate.TrySetResult();
            yield return pending.ContinueWith(r => result = r).ToCoroutine();

            Assert.That(result.Outcome, Is.EqualTo(RunRequestOutcome.Timeout));
            Assert.That(result.Success, Is.False);
            Assert.That(result.ItemIds, Is.Empty);
            Assert.That(result.ErrCode, Is.Null);

            Assert.That(ctx.Rejected, Has.Count.EqualTo(1));
            Assert.That(ctx.Rejected[0].Reason, Is.EqualTo(RunRequestOutcome.Timeout));
            Assert.That(ctx.Rejected[0].PointId, Is.EqualTo("r0"));
            Assert.That(ctx.Rejected[0].ErrCode, Is.Null);
            Assert.That(RunService.RejectionMessage, Is.EqualTo("网络延迟较大，请检查网络连接"));
        }

        /// <summary>小阈值：等到阈值即按超时拒绝，且阈值真的传给了延迟实现。</summary>
        [UnityTest]
        public IEnumerator TimeoutThresholdIsConfigurable()
        {
            Ctx ctx = CreateService(timeoutMs: 25, holdResponses: true);
            LootResult result = null;
            UniTask<LootResult> pending = ctx.Service.LootAsync("r0");
            ctx.DelayGate.TrySetResult();
            yield return pending.ContinueWith(r => result = r).ToCoroutine();

            Assert.That(ctx.Service.TimeoutMilliseconds, Is.EqualTo(25));
            Assert.That(ctx.RequestedDelayMs, Is.EqualTo(25));
            Assert.That(result.Outcome, Is.EqualTo(RunRequestOutcome.Timeout));
        }

        /// <summary>大阈值：应答先到就正常成功（阈值不参与判定成功路径）。</summary>
        [UnityTest]
        public IEnumerator LargeTimeoutThresholdWaitsForServer()
        {
            Ctx ctx = CreateService(timeoutMs: 60000);
            yield return ctx.Service.StartRunAsync(DifficultyTier.Tier1).ToCoroutine();

            EncounterResult result = null;
            yield return ctx.Service.EncounterAsync("e0")
                .ContinueWith(r => result = r).ToCoroutine();

            Assert.That(ctx.Service.TimeoutMilliseconds, Is.EqualTo(60000));
            Assert.That(ctx.RequestedDelayMs, Is.EqualTo(60000));
            Assert.That(result.Success, Is.True);
            Assert.That(result.EnemyPartyId, Is.EqualTo(4101));
        }

        [UnityTest]
        public IEnumerator SendFailureReportsDisconnected()
        {
            Ctx ctx = CreateService();
            ctx.Connection.IsConnected = false;

            EncounterResult result = null;
            yield return ctx.Service.EncounterAsync("e0")
                .ContinueWith(r => result = r).ToCoroutine();

            Assert.That(result.Outcome, Is.EqualTo(RunRequestOutcome.Disconnected));
            Assert.That(result.Success, Is.False);
            Assert.That(ctx.Rejected, Has.Count.EqualTo(1));
            Assert.That(ctx.Rejected[0].Reason, Is.EqualTo(RunRequestOutcome.Disconnected));
            Assert.That(ctx.Rejected[0].PointId, Is.EqualTo("e0"));
        }

        /// <summary>连接仍标记为已连，但本次发送失败（底层写失败）同样按断线放行。</summary>
        [UnityTest]
        public IEnumerator TrySendReturningFalseReportsDisconnected()
        {
            Ctx ctx = CreateService();
            ctx.Connection.FailNextSends = 1;

            LootResult result = null;
            yield return ctx.Service.LootAsync("r0")
                .ContinueWith(r => result = r).ToCoroutine();

            Assert.That(result.Outcome, Is.EqualTo(RunRequestOutcome.Disconnected));
            Assert.That(ctx.Rejected, Has.Count.EqualTo(1));
            Assert.That(ctx.Rejected[0].Reason, Is.EqualTo(RunRequestOutcome.Disconnected));
            Assert.That(ctx.Service.IsConsumed("r0"), Is.False);
        }

        [UnityTest]
        public IEnumerator DisconnectWhileWaitingReportsDisconnected()
        {
            Ctx ctx = CreateService(holdResponses: true);
            LootResult result = null;
            UniTask<LootResult> pending = ctx.Service.LootAsync("r0");
            ctx.Connection.Close();
            yield return pending.ContinueWith(r => result = r).ToCoroutine();

            Assert.That(result.Outcome, Is.EqualTo(RunRequestOutcome.Disconnected));
            Assert.That(ctx.Rejected, Has.Count.EqualTo(1));
            Assert.That(ctx.Rejected[0].Reason, Is.EqualTo(RunRequestOutcome.Disconnected));
            Assert.That(ctx.Service.IsConsumed("r0"), Is.False);
        }

        [UnityTest]
        public IEnumerator ServerRejectionReportsRejectedWithCode()
        {
            Ctx ctx = CreateService();
            yield return ctx.Service.StartRunAsync(DifficultyTier.Tier1).ToCoroutine();

            // 本局被掷为 None 的敌点（e1）视为不存在；不存在的物资点（r99）同样被拒。
            EncounterResult encounter = null;
            yield return ctx.Service.EncounterAsync("e1")
                .ContinueWith(r => encounter = r).ToCoroutine();
            Assert.That(encounter.Outcome, Is.EqualTo(RunRequestOutcome.Rejected));
            Assert.That(encounter.ErrCode, Is.EqualTo(NetErrCode.Rejected));

            LootResult loot = null;
            yield return ctx.Service.LootAsync("r99")
                .ContinueWith(r => loot = r).ToCoroutine();
            Assert.That(loot.Outcome, Is.EqualTo(RunRequestOutcome.Rejected));
            Assert.That(loot.ErrCode, Is.EqualTo(NetErrCode.Rejected));
            Assert.That(loot.ItemIds, Is.Empty);

            Assert.That(ctx.Rejected, Has.Count.EqualTo(2));
            Assert.That(ctx.Rejected[0].PointId, Is.EqualTo("e1"));
            Assert.That(ctx.Rejected[0].ErrCode, Is.EqualTo(NetErrCode.Rejected));
            Assert.That(ctx.Rejected[1].PointId, Is.EqualTo("r99"));
            Assert.That(ctx.Service.IsConsumed("r99"), Is.False);
        }

        [UnityTest]
        public IEnumerator StartRunRejectedWithoutActiveRunOnResume()
        {
            Ctx ctx = CreateService();
            StartRunResult result = null;
            yield return ctx.Service.StartRunAsync(DifficultyTier.Tier1, resume: true)
                .ContinueWith(r => result = r).ToCoroutine();

            Assert.That(result.Outcome, Is.EqualTo(RunRequestOutcome.Rejected));
            Assert.That(result.ErrCode, Is.EqualTo(NetErrCode.Rejected));
            Assert.That(ctx.Service.HasActiveRun, Is.False);
            Assert.That(ctx.Rejected, Has.Count.EqualTo(1));
            Assert.That(ctx.Rejected[0].PointId, Is.Null);
            Assert.That(ctx.Rejected[0].Reason, Is.EqualTo(RunRequestOutcome.Rejected));
        }

        [UnityTest]
        public IEnumerator LateResponseDoesNotAffectNextRequest()
        {
            Ctx ctx = CreateService();
            yield return ctx.Service.StartRunAsync(DifficultyTier.Tier1).ToCoroutine();

            // 第二个请求不回包 → 超时；请求信封留在 Held 里，稍后用手工构造的迟到应答回投。
            ctx.Connection.Responder = json =>
            {
                ctx.Held.Add(GameFramework.Utility.Json.ToObject<NetEnvelope>(json));
                return null;
            };
            LootResult timedOut = null;
            UniTask<LootResult> pending = ctx.Service.LootAsync("r1");
            ctx.DelayGate.TrySetResult();
            yield return pending.ContinueWith(r => timedOut = r).ToCoroutine();
            Assert.That(timedOut.Outcome, Is.EqualTo(RunRequestOutcome.Timeout));
            Assert.That(ctx.Held, Has.Count.EqualTo(1));

            // 下一个请求正常往返（新 rid），成功。
            ctx.Connection.Responder = ctx.Server.Handle;
            ctx.DelayGate = new UniTaskCompletionSource();
            LootResult next = null;
            yield return ctx.Service.LootAsync("r0")
                .ContinueWith(r => next = r).ToCoroutine();
            Assert.That(next.Success, Is.True);
            Assert.That(next.ItemIds.ToArray(), Is.EqualTo(FakeRunServer.R0Items));

            // 迟到的 r1 应答到达：该 rid 已被放弃，既不能串到 r0 的结果上，也不能把 r1 记成已消耗。
            NetEnvelope late = new NetEnvelope
            {
                t = NetMsg.OK,
                v = NetMsg.Version,
                rid = ctx.Held[0].rid,
                itemIds = new List<int> { 9999 },
            };
            ctx.Connection.OnMessage(GameFramework.Utility.Json.ToJson(late));

            Assert.That(next.Success, Is.True);
            Assert.That(next.ItemIds.ToArray(), Is.EqualTo(FakeRunServer.R0Items));
            Assert.That(ctx.Service.IsConsumed("r1"), Is.False);
            Assert.That(ctx.Service.IsConsumed("r0"), Is.True);
            Assert.That(ctx.Rejected, Has.Count.EqualTo(1));
        }

        /// <summary>
        /// 迷你 run 服务器：复刻契约 §7 的应答形状与拒绝语义（幂等/点不存在/无活跃 run）。
        /// </summary>
        private sealed class FakeRunServer
        {
            public const string DefaultRunId = "run-abc";
            public const long DefaultSeed = 123456789;

            public static readonly int[] R0Items = { 5001, 5003 };
            public static readonly int[] R1Items = { 5301 };

            /// <summary>本用户的活跃 run 是否存在（settle/resume 语义的外部开关）。</summary>
            public bool Active;

            private readonly List<string> _consumed = new List<string>();
            private readonly List<NetLootRecord> _looted = new List<NetLootRecord>();

            private static readonly Dictionary<string, int[]> ResourcePoints = new Dictionary<string, int[]>
            {
                { "r0", R0Items },
                { "r1", R1Items },
            };

            // e0 存在（队伍 4101 / 威胁 2）；e1 本局被掷为 None → 视为不存在。
            private static readonly Dictionary<string, int> EnemyParty = new Dictionary<string, int> { { "e0", 4101 } };
            private static readonly Dictionary<string, int> EnemyThreat = new Dictionary<string, int> { { "e0", 2 } };

            /// <summary>模拟该点已被开箱（重连恢复场景）。</summary>
            public void MarkLooted(string pointId)
            {
                if (_consumed.Contains(pointId))
                {
                    return;
                }

                _consumed.Add(pointId);
                NetLootRecord record = new NetLootRecord { pointId = pointId };
                record.itemIds.AddRange(ResourcePoints[pointId]);
                _looted.Add(record);
            }

            public string Handle(string json)
            {
                NetEnvelope response = HandleEnvelope(GameFramework.Utility.Json.ToObject<NetEnvelope>(json));
                return GameFramework.Utility.Json.ToJson(response);
            }

            private NetEnvelope HandleEnvelope(NetEnvelope request)
            {
                switch (request.t)
                {
                    case NetMsg.StartRun:
                        return StartRun(request);
                    case NetMsg.Encounter:
                        return Encounter(request);
                    case NetMsg.Loot:
                        return Loot(request);
                    default:
                        return Err(request, NetErrCode.BadMessage, "unknown " + request.t);
                }
            }

            private NetEnvelope StartRun(NetEnvelope request)
            {
                if (!Active && request.resume)
                {
                    return Err(request, NetErrCode.Rejected, "no active run");
                }

                if ((DifficultyTier)request.difficulty == DifficultyTier.None)
                {
                    return Err(request, NetErrCode.Rejected, "unknown difficulty");
                }

                Active = true;
                NetEnvelope response = Ok(request);
                response.runId = DefaultRunId;
                response.seed = DefaultSeed;
                response.runDifficulty = request.difficulty;
                response.spawnPoint = new NetVec2(1, -16);
                response.extractionPoint = new NetVec2(30, -3);
                // 与服务端 omitempty 语义一致：空清单不下发。
                response.consumed = _consumed.Count > 0 ? new List<string>(_consumed) : null;
                response.looted = _looted.Count > 0 ? new List<NetLootRecord>(_looted) : null;
                return response;
            }

            private NetEnvelope Encounter(NetEnvelope request)
            {
                if (!Active)
                {
                    return Err(request, NetErrCode.Rejected, "no active run");
                }

                if (request.runId != DefaultRunId)
                {
                    return Err(request, NetErrCode.Rejected, "runId does not match the active run");
                }

                if (!EnemyParty.TryGetValue(request.pointId ?? string.Empty, out int partyId))
                {
                    return Err(request, NetErrCode.Rejected, "point " + request.pointId + " does not exist");
                }

                NetEnvelope response = Ok(request);
                response.enemyPartyId = partyId;
                response.threat = EnemyThreat[request.pointId];
                return response;
            }

            private NetEnvelope Loot(NetEnvelope request)
            {
                if (!Active)
                {
                    return Err(request, NetErrCode.Rejected, "no active run");
                }

                if (request.runId != DefaultRunId)
                {
                    return Err(request, NetErrCode.Rejected, "runId does not match the active run");
                }

                if (!ResourcePoints.TryGetValue(request.pointId ?? string.Empty, out int[] itemIds))
                {
                    return Err(request, NetErrCode.Rejected, "point " + request.pointId + " does not exist");
                }

                MarkLooted(request.pointId);
                NetEnvelope response = Ok(request);
                response.itemIds = new List<int>(itemIds);
                return response;
            }

            private static NetEnvelope Ok(NetEnvelope request)
            {
                return new NetEnvelope { t = NetMsg.OK, v = NetMsg.Version, rid = request.rid };
            }

            private static NetEnvelope Err(NetEnvelope request, string code, string msg)
            {
                return new NetEnvelope { t = NetMsg.Err, v = NetMsg.Version, rid = request.rid, code = code, msg = msg };
            }
        }

        private sealed class FakeServerConnection : IServerConnection
        {
            public Func<string, string> Responder = _ => null;
            public readonly List<NetEnvelope> Sent = new List<NetEnvelope>();

            /// <summary>接下来若干次发送直接失败（模拟底层写失败但仍标记已连）。</summary>
            public int FailNextSends;

            public bool IsConnected { get; set; } = true;

            public event Action Disconnected;

            public UniTask<bool> ConnectAsync(string uri)
            {
                IsConnected = true;
                return UniTask.FromResult(true);
            }

            public void Close()
            {
                IsConnected = false;
                Disconnected?.Invoke();
            }

            public bool TrySend(string json)
            {
                if (!IsConnected)
                {
                    return false;
                }

                if (FailNextSends > 0)
                {
                    FailNextSends--;
                    return false;
                }

                Sent.Add(GameFramework.Utility.Json.ToObject<NetEnvelope>(json));
                string resp = Responder(json);
                if (resp != null)
                {
                    OnMessage(resp);
                }

                return true;
            }

            public event Action<string> MessageReceived;

            public void OnMessage(string json)
            {
                MessageReceived?.Invoke(json);
            }
        }

        private sealed class Ctx
        {
            public FakeRunServer Server = new FakeRunServer();
            public FakeServerConnection Connection = new FakeServerConnection();
            public NetRpc Rpc;
            public RunService Service;
            public readonly List<NetEnvelope> Held = new List<NetEnvelope>();
            public readonly List<RunInteractionRejectedArgs> Rejected = new List<RunInteractionRejectedArgs>();

            /// <summary>超时闸门：完成它即"到了等待阈值"；不完成表示应答先到。</summary>
            public UniTaskCompletionSource DelayGate = new UniTaskCompletionSource();

            public int RequestedDelayMs = -1;
        }

        private Ctx CreateService(int timeoutMs = RunService.DefaultTimeoutMilliseconds, bool holdResponses = false)
        {
            Ctx ctx = new Ctx();
            if (holdResponses)
            {
                ctx.Connection.Responder = json =>
                {
                    ctx.Held.Add(GameFramework.Utility.Json.ToObject<NetEnvelope>(json));
                    return null;
                };
            }
            else
            {
                ctx.Connection.Responder = ctx.Server.Handle;
            }

            ctx.Rpc = new NetRpc(ctx.Connection);
            ctx.Service = new RunService(ctx.Rpc, timeoutMs, ms =>
            {
                ctx.RequestedDelayMs = ms;
                return ctx.DelayGate.Task;
            });
            ctx.Service.InteractionRejected += args => ctx.Rejected.Add(args);
            return ctx;
        }
    }
}
