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
    /// 仓库同步服务全链路测试：假连接 + 迷你服务器（复刻服务端核心语义）。
    /// 覆盖：登录迁移、权威快照落地、大厅差分上报、结算断线重试与 runId 上报。
    /// </summary>
    [TestFixture]
    public class WarehouseSyncServiceTests
    {
        private readonly Dictionary<int, ItemConfig> _items = new Dictionary<int, ItemConfig>();

        [OneTimeSetUp]
        public void SetUpItems()
        {
            // EditMode 下没有 GameEntry 启动链，手动注册 UGF 的 JSON helper
            GameFramework.Utility.Json.SetJsonHelper(new UnityGameFramework.Runtime.DefaultJsonHelper());
            GameFramework.GameFrameworkLog.SetLogHelper(new UnityGameFramework.Runtime.DefaultLogHelper());
            _items[5001] = TestConfigFactory.Create<ItemConfig>("Id", 5001, "Name", "i5001", "StackLimit", 99,
                "EquipSlot", EquipmentSlotType.None, "Value", 10, "Width", 1, "Height", 1);
            _items[5101] = TestConfigFactory.Create<ItemConfig>("Id", 5101, "Name", "i5101", "StackLimit", 1,
                "EquipSlot", EquipmentSlotType.Weapon, "Value", 10, "Width", 1, "Height", 1);
            _items[5102] = TestConfigFactory.Create<ItemConfig>("Id", 5102, "Name", "i5102", "StackLimit", 1,
                "EquipSlot", EquipmentSlotType.Weapon, "Value", 10, "Width", 1, "Height", 1);
            _items[5301] = TestConfigFactory.Create<ItemConfig>("Id", 5301, "Name", "i5301", "StackLimit", 5,
                "EquipSlot", EquipmentSlotType.None, "Value", 10, "Width", 1, "Height", 1);
        }

        private GlobalConfig Global()
        {
            return TestConfigFactory.Create<GlobalConfig>(
                "WarehouseFixedColumn", 8, "WarehouseSlotCount", 32,
                "BackpackSlotCount", 16, "SecureSlotCount", 4,
                "NewGameCharacterIds", new List<int> { 1001 });
        }

        private sealed class FakeServerConnection : IServerConnection
        {
            public Func<string, string> Responder = _ => null;
            public List<NetEnvelope> Sent = new List<NetEnvelope>();
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

        /// <summary>
        /// 迷你服务器：复刻 Go 服务端核心语义（应用操作集、铸 UID、迁移、runId 幂等、战备配置、战绩元数据）。
        /// </summary>
        private sealed class FakeServer
        {
            public readonly List<NetSnapRow> Rows = new List<NetSnapRow>();
            public readonly List<int> Characters = new List<int>();
            public readonly HashSet<string> Settled = new HashSet<string>();
            public readonly List<NetRunRecord> History = new List<NetRunRecord>();
            public bool Initialized;
            public long NextUid = 1;
            public long UserId = 4294967301L;

            public int[] Party;
            public int Difficulty;
            public List<NetStack> Carried = new List<NetStack>();

            public string Handle(string json)
            {
                NetEnvelope env = GameFramework.Utility.Json.ToObject<NetEnvelope>(json);
                switch (env.t)
                {
                    case NetMsg.Register:
                    case NetMsg.Auth:
                        return OK(env, token: "tok-1");
                    case NetMsg.Pull:
                        return OK(env);
                    case NetMsg.History:
                        return HistoryOK(env);
                    case NetMsg.Ping:
                        return GameFramework.Utility.Json.ToJson(new NetEnvelope { t = NetMsg.OK, rid = env.rid, v = NetMsg.Version });
                    case NetMsg.Migrate:
                        if (Initialized)
                        {
                            return Err(env, NetErrCode.Rejected, "already initialized");
                        }

                        foreach (NetMigrateChar c in env.migrate.characters)
                        {
                            Characters.Add(c.cid);
                            if (c.weaponItemId > 0)
                            {
                                Rows.Add(new NetSnapRow { uid = ++NextUid, itemId = c.weaponItemId, count = 1, wearer = c.cid });
                            }

                            if (c.armorItemId > 0)
                            {
                                Rows.Add(new NetSnapRow { uid = ++NextUid, itemId = c.armorItemId, count = 1, wearer = c.cid });
                            }
                        }

                        foreach (NetMigrateItem it in env.migrate.items)
                        {
                            Rows.Add(new NetSnapRow { uid = ++NextUid, itemId = it.itemId, count = it.count, x = it.x, y = it.y, r = it.r });
                        }

                        Party = env.migrate.party != null && env.migrate.party.Length > 0 ? env.migrate.party : null;
                        Difficulty = env.migrate.difficulty;
                        Carried = env.migrate.carried != null ? new List<NetStack>(env.migrate.carried) : new List<NetStack>();
                        Initialized = true;
                        return OK(env);
                    case NetMsg.Settle:
                        if (Settled.Contains(env.runId))
                        {
                            return OK(env);
                        }

                        Settled.Add(env.runId);
                        ApplyOps(env.ops);
                        History.Add(new NetRunRecord
                        {
                            runId = env.runId,
                            outcome = env.outcome,
                            difficulty = env.difficulty,
                            seed = env.seed,
                            startedAt = env.startedAt,
                            endedAt = env.endedAt,
                        });
                        return OK(env);
                    case NetMsg.Ops:
                        ApplyOps(env.ops);
                        return OK(env);
                    default:
                        return Err(env, NetErrCode.BadMessage, "unknown " + env.t);
                }
            }

            private void ApplyOps(List<NetOp> ops)
            {
                Dictionary<int, long> tidToUid = new Dictionary<int, long>();
                foreach (NetOp op in ops)
                {
                    switch (op.k)
                    {
                        case NetOpKind.Add:
                            NextUid++;
                            Rows.Add(new NetSnapRow { uid = NextUid, itemId = op.itemId, count = op.count, x = op.x, y = op.y, r = op.r });
                            tidToUid[op.tid] = NextUid;
                            break;
                        case NetOpKind.Del:
                            NetSnapRow del = Rows.First(r => r.uid == op.uid);
                            if (op.count == 0 || op.count >= del.count)
                            {
                                Rows.Remove(del);
                            }
                            else
                            {
                                del.count -= op.count;
                            }

                            break;
                        case NetOpKind.Mov:
                            NetSnapRow mov = Rows.First(r => r.uid == op.uid);
                            mov.x = op.x;
                            mov.y = op.y;
                            mov.r = op.r;
                            break;
                        case NetOpKind.Rel:
                            NetSnapRow rel = Rows.First(r => r.uid == op.uid);
                            rel.wearer = 0;
                            rel.x = op.x;
                            rel.y = op.y;
                            break;
                        case NetOpKind.Wear:
                            long wearUid = op.uid;
                            if (wearUid <= 0 && op.tid > 0)
                            {
                                tidToUid.TryGetValue(op.tid, out wearUid);
                            }

                            if (wearUid > 0)
                            {
                                Rows.First(r => r.uid == wearUid).wearer = op.cid;
                            }
                            else
                            {
                                // wear by itemId：服务器直接铸新行穿上
                                NextUid++;
                                Rows.Add(new NetSnapRow { uid = NextUid, itemId = op.itemId, count = 1, wearer = op.cid });
                            }

                            break;
                        case NetOpKind.Recruit:
                            Characters.Add(op.cid);
                            break;
                        case NetOpKind.Party:
                            Party = op.party != null && op.party.Length > 0 ? op.party : null;
                            break;
                        case NetOpKind.Difficulty:
                            Difficulty = op.difficulty;
                            break;
                        case NetOpKind.Carry:
                            Carried = op.carry != null ? new List<NetStack>(op.carry) : new List<NetStack>();
                            break;
                    }
                }
            }

            private string OK(NetEnvelope req, string token = null)
            {
                NetEnvelope resp = new NetEnvelope { t = NetMsg.OK, rid = req.rid, token = token,
                    userId = req.t == NetMsg.Auth || req.t == NetMsg.Register ? UserId : 0 };
                resp.snapshot = Snapshot();
                return GameFramework.Utility.Json.ToJson(resp);
            }

            private string HistoryOK(NetEnvelope req)
            {
                NetEnvelope resp = new NetEnvelope { t = NetMsg.OK, rid = req.rid };
                resp.history = History.OrderByDescending(r => r.startedAt).Take(100).ToList();
                return GameFramework.Utility.Json.ToJson(resp);
            }

            private static string Err(NetEnvelope req, string code, string msg)
            {
                return GameFramework.Utility.Json.ToJson(new NetEnvelope { t = NetMsg.Err, rid = req.rid, code = code, msg = msg });
            }

            public NetSnapshot Snapshot()
            {
                NetSnapshot snapshot = new NetSnapshot { initialized = Initialized, difficulty = Difficulty };
                foreach (NetSnapRow row in Rows)
                {
                    snapshot.items.Add(new NetSnapRow
                    {
                        uid = row.uid, itemId = row.itemId, count = row.count,
                        x = row.x, y = row.y, r = row.r, wearer = row.wearer,
                    });
                }

                foreach (int cid in Characters)
                {
                    snapshot.characters.Add(new NetSnapChar { cid = cid });
                }

                // 与服务端 omitempty 语义一致：空队列不上报
                snapshot.party = Party != null && Party.Length > 0 ? Party : null;
                snapshot.difficulty = Difficulty;
                snapshot.carried = Carried.Count > 0 ? new List<NetStack>(Carried) : null;
                return snapshot;
            }
        }

        private sealed class Ctx
        {
            public SaveData Save;
            public FakeServer Server = new FakeServer();
            public FakeServerConnection Connection = new FakeServerConnection();
            public WarehouseSyncService Service;
            public int PersistCount;
            public double Now;

            public int CountSent(string type) => Connection.Sent.Count(e => e.t == type);
        }

        private Ctx CreateService()
        {
            Ctx ctx = new Ctx();
            ctx.Save = new SaveData
            {
                mainWarehouse = new List<GridItemStack> { new GridItemStack(5001, 20, 0, 0) },
                characters = new List<CharacterSave> { new CharacterSave(1001, 5101, 0) },
                loadout = new LoadoutSave { partyCharacterIds = new[] { 1001 } },
                runHistory = new List<RoundRecord>(),
            };
            ctx.Connection.Responder = ctx.Server.Handle;
            ctx.Service = new WarehouseSyncService(
                ctx.Connection,
                () => ctx.Save,
                id => _items.TryGetValue(id, out ItemConfig config) ? config : null,
                Global,
                () => ctx.PersistCount++,
                ms => UniTask.CompletedTask,
                () => ctx.Now);
            return ctx;
        }

        [UnityTest]
        public IEnumerator LoginMigratesLocalSaveAndAppliesSnapshot()
        {
            Ctx ctx = CreateService();
            bool ok = false;
            yield return ctx.Service.LoginAsync("ws://test", "device-1", null)
                .ContinueWith(result => ok = result).ToCoroutine();

            Assert.That(ok, Is.True);
            Assert.That(ctx.Service.Token, Is.EqualTo("tok-1"));
            Assert.That(ctx.Service.UserId, Is.EqualTo(ctx.Server.UserId));
            Assert.That(ctx.CountSent(NetMsg.Register), Is.EqualTo(1));
            Assert.That(ctx.CountSent(NetMsg.Migrate), Is.EqualTo(1));
            Assert.That(ctx.Server.Initialized, Is.True);
            Assert.That(ctx.PersistCount, Is.GreaterThanOrEqualTo(1));

            // 服务器铸的行落地：网格 5001 + 1001 穿着 5101
            Assert.That(ctx.Save.mainWarehouse, Has.Count.EqualTo(1));
            Assert.That(ctx.Save.mainWarehouse[0].itemId, Is.EqualTo(5001));
            Assert.That(ctx.Save.characters[0].weaponItemId, Is.EqualTo(5101));
        }

        [UnityTest]
        public IEnumerator FlushAfterLayoutChange_SendsMovAndAppliesAck()
        {
            Ctx ctx = CreateService();
            yield return ctx.Service.LoginAsync("ws://test", "device-1", null).ToCoroutine();

            ctx.Save.mainWarehouse[0] = new GridItemStack(5001, 20, 3, 0);
            yield return ctx.Service.FlushAsync().ToCoroutine();

            NetEnvelope opsEnv = ctx.Connection.Sent.FirstOrDefault(e => e.t == NetMsg.Ops);
            Assert.That(opsEnv, Is.Not.Null);
            Assert.That(opsEnv.ops.Any(o => o.k == NetOpKind.Mov), Is.True);
            Assert.That(ctx.Server.Rows.First(r => r.itemId == 5001).x, Is.EqualTo(3));

            // 二次冲刷无差异：不再发送
            int sentBefore = ctx.Connection.Sent.Count;
            yield return ctx.Service.FlushAsync().ToCoroutine();
            Assert.That(ctx.Connection.Sent.Count, Is.EqualTo(sentBefore));
        }

        [UnityTest]
        public IEnumerator SettleRetriesAfterDropUntilAcked()
        {
            Ctx ctx = CreateService();
            yield return ctx.Service.LoginAsync("ws://test", "device-1", null).ToCoroutine();

            // 局内结算结果：战利品入库 + 招募 + 换新武器（局内捡的）
            ctx.Save.mainWarehouse.Add(new GridItemStack(5301, 4, 1, 0));
            ctx.Save.characters[0] = new CharacterSave(1001, 5102, 0);
            ctx.Save.characters.Add(new CharacterSave(1003, 0, 0));

            ctx.Connection.FailNextSends = 1; // 第一次发送即失败（模拟断线）
            var record = new RoundRecord((RoundResultType)1, DifficultyTier.Tier2, 111, 1000, 2000);
            yield return ctx.Service.SettleAsync("run-42", record).ToCoroutine();

            NetEnvelope settle = ctx.Connection.Sent.FirstOrDefault(e => e.t == NetMsg.Settle);
            Assert.That(settle, Is.Not.Null);
            Assert.That(settle.runId, Is.EqualTo("run-42"));
            Assert.That(settle.outcome, Is.EqualTo(1));
            // 战绩元数据随结算上报
            Assert.That(settle.difficulty, Is.EqualTo(2));
            Assert.That(settle.seed, Is.EqualTo(111));
            Assert.That(settle.startedAt, Is.EqualTo(1000));
            Assert.That(settle.endedAt, Is.EqualTo(2000));
            Assert.That(ctx.Server.Settled, Does.Contain("run-42"));
            Assert.That(ctx.Server.History, Has.Count.EqualTo(1));
            Assert.That(ctx.Server.History[0].seed, Is.EqualTo(111));

            // 权威快照落地：新角色在场、新武器穿着、战利品入库
            Assert.That(ctx.Save.characters.Any(c => c.characterId == 1003), Is.True);
            Assert.That(ctx.Save.characters[0].weaponItemId, Is.EqualTo(5102));
            Assert.That(ctx.Save.mainWarehouse.Any(s => s.itemId == 5301), Is.True);
        }

        [UnityTest]
        public IEnumerator SettleRejected_DoesNotHangAndKeepsLocalState()
        {
            Ctx ctx = CreateService();
            yield return ctx.Service.LoginAsync("ws://test", "device-1", null).ToCoroutine();
            ctx.Connection.Responder = json =>
            {
                NetEnvelope env = GameFramework.Utility.Json.ToObject<NetEnvelope>(json);
                return env.t == NetMsg.Settle
                    ? GameFramework.Utility.Json.ToJson(new NetEnvelope { t = NetMsg.Err, rid = env.rid, code = NetErrCode.Rejected, msg = "bad" })
                    : ctx.Server.Handle(json);
            };

            LogAssert.Expect(UnityEngine.LogType.Error, "Settle rejected by server (runId run-x): rejected: bad");
            yield return ctx.Service.SettleAsync("run-x", new RoundRecord((RoundResultType)2, DifficultyTier.Tier1, 0, 0, 0)).ToCoroutine();

            Assert.That(ctx.Server.Settled, Does.Not.Contain("run-x"));
        }

        [UnityTest]
        public IEnumerator SnapshotWithOverlappingPositions_SelfHeals()
        {
            Ctx ctx = CreateService();
            ctx.Connection.Responder = json =>
            {
                NetEnvelope env = GameFramework.Utility.Json.ToObject<NetEnvelope>(json);
                NetSnapshot snapshot = new NetSnapshot { initialized = true };
                snapshot.characters.Add(new NetSnapChar { cid = 1001 });
                snapshot.items.Add(new NetSnapRow { uid = 1, itemId = 5001, count = 5, x = 0, y = 0 });
                snapshot.items.Add(new NetSnapRow { uid = 2, itemId = 5001, count = 5, x = 0, y = 0 }); // 重叠
                return GameFramework.Utility.Json.ToJson(new NetEnvelope { t = NetMsg.OK, rid = env.rid, token = "tok-1", userId = ctx.Server.UserId, snapshot = snapshot });
            };

            yield return ctx.Service.LoginAsync("ws://test", "device-1", null).ToCoroutine();

            // 5001 堆叠上限 99：两堆重叠落位时合并为单堆，总量守恒
            Assert.That(ctx.Save.mainWarehouse, Has.Count.EqualTo(1));
            Assert.That(ctx.Save.mainWarehouse[0].count, Is.EqualTo(10));
        }

        [UnityTest]
        public IEnumerator MigrateCarriesLoadoutConfig()
        {
            Ctx ctx = CreateService();
            ctx.Save.loadout.difficultyId = DifficultyTier.Tier3;
            ctx.Save.loadout.carriedItems = new List<ItemStack> { new ItemStack(5001, 7) };

            yield return ctx.Service.LoginAsync("ws://test", "device-1", null).ToCoroutine();

            NetEnvelope migrate = ctx.Connection.Sent.FirstOrDefault(e => e.t == NetMsg.Migrate);
            Assert.That(migrate, Is.Not.Null);
            Assert.That(migrate.migrate.party, Is.EqualTo(new[] { 1001 }));
            Assert.That(migrate.migrate.difficulty, Is.EqualTo(3));
            Assert.That(migrate.migrate.carried, Has.Count.EqualTo(1));
            Assert.That(migrate.migrate.carried[0].itemId, Is.EqualTo(5001));

            // 服务器侧战备配置建立，快照回填本地
            Assert.That(ctx.Server.Party, Is.EqualTo(new[] { 1001 }));
            Assert.That(ctx.Server.Difficulty, Is.EqualTo(3));
            Assert.That(ctx.Save.loadout.difficultyId, Is.EqualTo(DifficultyTier.Tier3));
            Assert.That(ctx.Save.loadout.carriedItems, Has.Count.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator SquadAndDifficultyEdits_FlushPartyAndDifficultyOps()
        {
            Ctx ctx = CreateService();
            yield return ctx.Service.LoginAsync("ws://test", "device-1", null).ToCoroutine();

            // 编队编辑：新增角色入队并调整顺序；难度改为 Tier3
            ctx.Save.characters.Add(new CharacterSave(1002, 0, 0));
            ctx.Save.loadout.partyCharacterIds = new[] { 1002, 1001 };
            ctx.Save.loadout.difficultyId = DifficultyTier.Tier3;
            yield return ctx.Service.FlushAsync().ToCoroutine();

            NetEnvelope opsEnv = ctx.Connection.Sent.FirstOrDefault(e => e.t == NetMsg.Ops);
            Assert.That(opsEnv, Is.Not.Null);
            // 固定顺序：recruit 先于 party（party 引用新角色）
            Assert.That(opsEnv.ops.Select(o => o.k).ToArray(),
                Is.EqualTo(new[] { NetOpKind.Recruit, NetOpKind.Party, NetOpKind.Difficulty }));
            Assert.That(opsEnv.ops.First(o => o.k == NetOpKind.Party).party, Is.EqualTo(new[] { 1002, 1001 }));
            Assert.That(opsEnv.ops.First(o => o.k == NetOpKind.Difficulty).difficulty, Is.EqualTo(3));

            // 服务器确认后本地与账本一致；二次冲刷无差异
            Assert.That(ctx.Server.Party, Is.EqualTo(new[] { 1002, 1001 }));
            Assert.That(ctx.Save.loadout.partyCharacterIds, Is.EqualTo(new[] { 1002, 1001 }));
            int sentBefore = ctx.Connection.Sent.Count;
            yield return ctx.Service.FlushAsync().ToCoroutine();
            Assert.That(ctx.Connection.Sent.Count, Is.EqualTo(sentBefore));
        }

        [UnityTest]
        public IEnumerator CarryEdit_FlushesCarryOp()
        {
            Ctx ctx = CreateService();
            yield return ctx.Service.LoginAsync("ws://test", "device-1", null).ToCoroutine();

            ctx.Save.loadout.carriedItems = new List<ItemStack>
            {
                new ItemStack(5001, 3),
                new ItemStack(5301, 2),
            };
            yield return ctx.Service.FlushAsync().ToCoroutine();

            NetEnvelope opsEnv = ctx.Connection.Sent.FirstOrDefault(e => e.t == NetMsg.Ops);
            Assert.That(opsEnv, Is.Not.Null);
            Assert.That(opsEnv.ops, Has.Count.EqualTo(1));
            Assert.That(opsEnv.ops[0].k, Is.EqualTo(NetOpKind.Carry));
            Assert.That(opsEnv.ops[0].carry, Has.Count.EqualTo(2));
            Assert.That(ctx.Server.Carried, Has.Count.EqualTo(2));
            Assert.That(ctx.Save.loadout.carriedItems[0].count, Is.EqualTo(3));
        }

        [UnityTest]
        public IEnumerator ApplySnapshot_EmptyPartyWritesNullAndDifficulty()
        {
            Ctx ctx = CreateService();
            ctx.Connection.Responder = json =>
            {
                NetEnvelope env = GameFramework.Utility.Json.ToObject<NetEnvelope>(json);
                NetSnapshot snapshot = new NetSnapshot { initialized = true, difficulty = 2 };
                snapshot.characters.Add(new NetSnapChar { cid = 1001 });
                snapshot.items.Add(new NetSnapRow { uid = 1, itemId = 5001, count = 5, x = 0, y = 0 });
                // party 不带（=未配置）、carried 带 5001×2
                snapshot.carried = new List<NetStack> { new NetStack(5001, 2) };
                return GameFramework.Utility.Json.ToJson(new NetEnvelope { t = NetMsg.OK, rid = env.rid, token = "tok-1", userId = ctx.Server.UserId, snapshot = snapshot });
            };

            yield return ctx.Service.LoginAsync("ws://test", "device-1", null).ToCoroutine();

            // 快照空队列写回 null（保留"从未配置则默认组队"流程），难度与携带以服务器为准
            Assert.That(ctx.Save.loadout.partyCharacterIds, Is.Null);
            Assert.That(ctx.Save.loadout.difficultyId, Is.EqualTo(DifficultyTier.Tier2));
            Assert.That(ctx.Save.loadout.carriedItems, Has.Count.EqualTo(1));
            Assert.That(ctx.Save.loadout.carriedItems[0].itemId, Is.EqualTo(5001));

            // 本地 null 不产生 party op（不得误清服务器状态），携带一致亦无 op
            int sentBefore = ctx.Connection.Sent.Count;
            yield return ctx.Service.FlushAsync().ToCoroutine();
            Assert.That(ctx.Connection.Sent.Count, Is.EqualTo(sentBefore));
        }

        [UnityTest]
        public IEnumerator LoginHistoryFillsRunHistory()
        {
            Ctx ctx = CreateService();
            ctx.Server.History.Add(new NetRunRecord { runId = "r1", outcome = 1, difficulty = 2, seed = 42, startedAt = 100, endedAt = 200 });
            ctx.Server.History.Add(new NetRunRecord { runId = "r2", outcome = 2, difficulty = 3, seed = 43, startedAt = 300, endedAt = 400 });

            yield return ctx.Service.LoginAsync("ws://test", "device-1", null).ToCoroutine();

            // 服务器战绩整体覆盖本地缓存
            Assert.That(ctx.Save.runHistory, Has.Count.EqualTo(2));
            Assert.That(ctx.Save.runHistory[0].outcome, Is.EqualTo((RoundResultType)2));
            Assert.That(ctx.Save.runHistory[0].seed, Is.EqualTo(43));
            Assert.That(ctx.Save.runHistory[0].difficultyId, Is.EqualTo(DifficultyTier.Tier3));
            Assert.That(ctx.Save.runHistory[0].startedAt, Is.EqualTo(300));
            Assert.That(ctx.Save.runHistory[0].endedAt, Is.EqualTo(400));
            Assert.That(ctx.Save.runHistory[1].outcome, Is.EqualTo(RoundResultType.Extracted));
            Assert.That(ctx.Save.runHistory[1].seed, Is.EqualTo(42));
            Assert.That(ctx.CountSent(NetMsg.History), Is.EqualTo(1));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ApplySnapshot_EmptyOrMissingPartyClearsPreviousConfiguration(bool explicitEmpty)
        {
            Ctx ctx = CreateService();
            NetSnapshot snapshot = ctx.Server.Snapshot();
            snapshot.party = explicitEmpty ? Array.Empty<int>() : null;
            ctx.Service.ApplySnapshot(snapshot);

            Assert.That(ctx.Save.loadout.partyCharacterIds, Is.Null);
            Assert.That(ctx.Save.loadout.difficultyId, Is.EqualTo(DifficultyTier.None));
            Assert.That(ctx.Save.loadout.carriedItems, Is.Empty);
        }

        [UnityTest]
        public IEnumerator LoadoutEdits_ReconnectRestoresServerPartyOrderAndCarry()
        {
            Ctx ctx = CreateService();
            yield return ctx.Service.LoginAsync("ws://test", "device-1", null).ToCoroutine();
            ctx.Save.characters.Add(new CharacterSave(1002, 0, 0));
            ctx.Save.loadout.partyCharacterIds = new[] { 1002, 1001 };
            ctx.Save.loadout.difficultyId = DifficultyTier.Tier3;
            ctx.Save.loadout.carriedItems = new List<ItemStack> { new ItemStack(5001, 7) };
            yield return ctx.Service.FlushAsync().ToCoroutine();
            ctx.Connection.Close();

            // 登录前的本地缓存不能覆盖已经提交的服务器配置。
            ctx.Save.loadout.partyCharacterIds = new[] { 1001 };
            ctx.Save.loadout.difficultyId = DifficultyTier.Tier1;
            ctx.Save.loadout.carriedItems.Clear();
            bool loggedIn = false;
            yield return ctx.Service.LoginAsync("ws://test", "device-1", ctx.Service.Token)
                .ContinueWith(result => loggedIn = result).ToCoroutine();

            Assert.That(loggedIn, Is.True);
            Assert.That(ctx.CountSent(NetMsg.Auth), Is.EqualTo(1));
            Assert.That(ctx.CountSent(NetMsg.Migrate), Is.EqualTo(1));
            Assert.That(ctx.Save.loadout.partyCharacterIds, Is.EqualTo(new[] { 1002, 1001 }));
            Assert.That(ctx.Save.loadout.difficultyId, Is.EqualTo(DifficultyTier.Tier3));
            Assert.That(ctx.Save.loadout.carriedItems, Has.Count.EqualTo(1));
            Assert.That(ctx.Save.loadout.carriedItems[0].count, Is.EqualTo(7));
            int sentBefore = ctx.Connection.Sent.Count;
            yield return ctx.Service.FlushAsync().ToCoroutine();
            Assert.That(ctx.Connection.Sent.Count, Is.EqualTo(sentBefore));
        }

        [UnityTest]
        public IEnumerator ClearLoadout_FlushesEmptyListsAndZeroDifficulty()
        {
            Ctx ctx = CreateService();
            ctx.Save.loadout.difficultyId = DifficultyTier.Tier2;
            ctx.Save.loadout.carriedItems = new List<ItemStack> { new ItemStack(5001, 3) };
            yield return ctx.Service.LoginAsync("ws://test", "device-1", null).ToCoroutine();
            ctx.Save.loadout.partyCharacterIds = Array.Empty<int>();
            ctx.Save.loadout.difficultyId = DifficultyTier.None;
            ctx.Save.loadout.carriedItems.Clear();
            yield return ctx.Service.FlushAsync().ToCoroutine();

            NetEnvelope env = ctx.Connection.Sent.Last(e => e.t == NetMsg.Ops);
            Assert.That(env.v, Is.EqualTo(2));
            Assert.That(env.ops.Select(o => o.k), Is.EqualTo(new[]
                { NetOpKind.Party, NetOpKind.Difficulty, NetOpKind.Carry }));
            Assert.That(env.ops[0].party, Is.Empty);
            Assert.That(env.ops[1].difficulty, Is.Zero);
            Assert.That(env.ops[2].carry, Is.Empty);
            Assert.That(ctx.Server.Party, Is.Null);
            Assert.That(ctx.Server.Difficulty, Is.Zero);
            Assert.That(ctx.Server.Carried, Is.Empty);
            Assert.That(ctx.Save.loadout.partyCharacterIds, Is.Null);
            int sentBefore = ctx.Connection.Sent.Count;
            yield return ctx.Service.FlushAsync().ToCoroutine();
            Assert.That(ctx.Connection.Sent.Count, Is.EqualTo(sentBefore));
        }

        [UnityTest]
        public IEnumerator SettleWithLocalLoadoutChanges_PreservesServerLoadout()
        {
            Ctx ctx = CreateService();
            ctx.Save.loadout.difficultyId = DifficultyTier.Tier2;
            ctx.Save.loadout.carriedItems = new List<ItemStack> { new ItemStack(5001, 3) };
            yield return ctx.Service.LoginAsync("ws://test", "device-1", null).ToCoroutine();
            ctx.Save.loadout.partyCharacterIds = Array.Empty<int>();
            ctx.Save.loadout.difficultyId = DifficultyTier.None;
            ctx.Save.loadout.carriedItems.Clear();
            ctx.Save.mainWarehouse.Add(new GridItemStack(5301, 1, 1, 0));
            const long startedAt = 1780000000000L;
            const long seed = 4294967297L;
            yield return ctx.Service.SettleAsync("run-loadout", new RoundRecord(
                RoundResultType.Extracted, DifficultyTier.Tier2, seed, startedAt, startedAt + 30000))
                .ToCoroutine();

            NetEnvelope settle = ctx.Connection.Sent.Single(e => e.t == NetMsg.Settle);
            Assert.That(settle.ops.Select(o => o.k), Is.EqualTo(new[] { NetOpKind.Add }));
            Assert.That(settle.seed, Is.EqualTo(seed));
            Assert.That(settle.startedAt, Is.EqualTo(startedAt));
            Assert.That(settle.endedAt, Is.EqualTo(startedAt + 30000));
            Assert.That(ctx.Server.Party, Is.EqualTo(new[] { 1001 }));
            Assert.That(ctx.Server.Difficulty, Is.EqualTo(2));
            Assert.That(ctx.Server.Carried.Single().count, Is.EqualTo(3));
            Assert.That(ctx.Save.loadout.partyCharacterIds, Is.EqualTo(new[] { 1001 }));
            Assert.That(ctx.Save.loadout.carriedItems.Single().count, Is.EqualTo(3));
        }

        [UnityTest]
        public IEnumerator LoginWithEmptyHistory_ReplacesStaleLocalHistory()
        {
            Ctx ctx = CreateService();
            ctx.Save.runHistory.Add(new RoundRecord(RoundResultType.Extracted, DifficultyTier.Tier3, 999, 1, 2));
            yield return ctx.Service.LoginAsync("ws://test", "device-1", null).ToCoroutine();

            Assert.That(ctx.Save.runHistory, Is.Empty);
            Assert.That(ctx.CountSent(NetMsg.History), Is.EqualTo(1));
        }

        [Test]
        public void NormalizeLoadout_PreservesUnconfiguredPartyNull()
        {
            LoadoutSave loadout = new LoadoutSave();
            loadout.Normalize();
            loadout.Normalize();

            Assert.That(loadout.partyCharacterIds, Is.Null);
            Assert.That(loadout.carriedItems, Is.Empty);
        }

        [UnityTest]
        public IEnumerator LoginWithRejectedHistory_ReturnsFalseAndPreservesCachedHistory()
        {
            Ctx ctx = CreateService();
            ctx.Save.runHistory.Add(new RoundRecord(RoundResultType.Extracted, DifficultyTier.Tier3, 999, 1, 2));
            ctx.Connection.Responder = json =>
            {
                NetEnvelope env = GameFramework.Utility.Json.ToObject<NetEnvelope>(json);
                return env.t == NetMsg.History
                    ? GameFramework.Utility.Json.ToJson(new NetEnvelope
                    {
                        t = NetMsg.Err, rid = env.rid, code = NetErrCode.Rejected, msg = "history unavailable",
                    })
                    : ctx.Server.Handle(json);
            };
            LogAssert.Expect(UnityEngine.LogType.Error, "History fetch failure: rejected: history unavailable");
            bool loggedIn = true;
            yield return ctx.Service.LoginAsync("ws://test", "device-1", null)
                .ContinueWith(result => loggedIn = result).ToCoroutine();

            Assert.That(loggedIn, Is.False);
            Assert.That(ctx.Save.runHistory, Has.Count.EqualTo(1));
            Assert.That(ctx.Save.runHistory[0].seed, Is.EqualTo(999));
            Assert.That(ctx.CountSent(NetMsg.History), Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator LoginWithHistorySendFailure_ReturnsFalse()
        {
            Ctx ctx = CreateService();
            ctx.Connection.Responder = json =>
            {
                NetEnvelope env = GameFramework.Utility.Json.ToObject<NetEnvelope>(json);
                if (env.t == NetMsg.Migrate)
                {
                    // 迁移完成后的下一次 history 发送失败，模拟该阶段丢失连接。
                    ctx.Connection.FailNextSends = 1;
                }

                return ctx.Server.Handle(json);
            };
            LogAssert.Expect(UnityEngine.LogType.Error, "History fetch failure: connection lost");
            bool loggedIn = true;
            yield return ctx.Service.LoginAsync("ws://test", "device-1", null)
                .ContinueWith(result => loggedIn = result).ToCoroutine();

            Assert.That(loggedIn, Is.False);
        }

        [UnityTest]
        public IEnumerator AccountIdentity_StableAfterReconnectAndClearedOnDisconnect()
        {
            Ctx ctx = CreateService();
            yield return ctx.Service.LoginAsync("ws://test", "device-1", null).ToCoroutine();
            long userId = ctx.Service.UserId;
            ctx.Service.UpdateLatency(true);
            Assert.That(ctx.Service.Latency.HasSamples, Is.True);
            ctx.Connection.Close();
            Assert.That(ctx.Service.UserId, Is.Zero);
            Assert.That(ctx.Service.Latency.HasSamples, Is.False);
            yield return ctx.Service.LoginAsync("ws://test", "device-1", ctx.Service.Token).ToCoroutine();
            Assert.That(ctx.Service.UserId, Is.EqualTo(userId));
            Assert.That(userId, Is.GreaterThan(int.MaxValue));
            Assert.That(ctx.Service.Latency.HasSamples, Is.False);
        }

        [UnityTest]
        public IEnumerator LatencyProbe_UsesMonotonicClockAndDoesNotApplySnapshot()
        {
            Ctx ctx = CreateService();
            yield return ctx.Service.LoginAsync("ws://test", "device-1", null).ToCoroutine();
            int snapshots = 0;
            ctx.Service.SnapshotApplied += _ => snapshots++;
            ctx.Connection.Responder = json =>
            {
                NetEnvelope env = GameFramework.Utility.Json.ToObject<NetEnvelope>(json);
                if (env.t == NetMsg.Ping) ctx.Now += 0.125;
                return ctx.Server.Handle(json);
            };
            ctx.Service.UpdateLatency(true);
            Assert.That(ctx.CountSent(NetMsg.Ping), Is.EqualTo(1));
            Assert.That(ctx.Service.Latency.LatestMilliseconds, Is.EqualTo(125).Within(0.001));
            Assert.That(snapshots, Is.Zero);
            ctx.Service.UpdateLatency(true);
            Assert.That(ctx.CountSent(NetMsg.Ping), Is.EqualTo(1));
            ctx.Now = 2;
            ctx.Service.UpdateLatency(true);
            Assert.That(ctx.CountSent(NetMsg.Ping), Is.EqualTo(2));
            Assert.That(ctx.Service.Latency.SampleCount, Is.EqualTo(2));
        }

        [UnityTest]
        public IEnumerator PingTimeout_IgnoresLateReplyAndRecoversWithoutPendingLeak()
        {
            Ctx ctx = CreateService();
            yield return ctx.Service.LoginAsync("ws://test", "device-1", null).ToCoroutine();
            ctx.Connection.Responder = _ => null;
            ctx.Service.UpdateLatency(true);
            NetEnvelope first = ctx.Connection.Sent.Last();
            ctx.Now = 4;
            ctx.Service.UpdateLatency(true);
            Assert.That(ctx.CountSent(NetMsg.Ping), Is.EqualTo(1));
            ctx.Now = 5;
            ctx.Service.UpdateLatency(true);
            NetEnvelope second = ctx.Connection.Sent.Last();
            Assert.That(ctx.CountSent(NetMsg.Ping), Is.EqualTo(2));
            Assert.That(ctx.Service.Latency.IsTimedOut, Is.True);
            ctx.Connection.OnMessage(GameFramework.Utility.Json.ToJson(new NetEnvelope { t = NetMsg.OK, rid = first.rid }));
            Assert.That(ctx.Service.Latency.HasSamples, Is.False);
            Assert.That(ctx.Service.Latency.HasPendingProbe, Is.True);
            ctx.Now = 5.25;
            ctx.Connection.OnMessage(GameFramework.Utility.Json.ToJson(new NetEnvelope { t = NetMsg.OK, rid = second.rid }));
            Assert.That(ctx.Service.Latency.LatestMilliseconds, Is.EqualTo(250).Within(0.001));
            Assert.That(ctx.Service.Latency.HasPendingProbe, Is.False);
        }

        [UnityTest]
        public IEnumerator PendingEconomicFlush_DoesNotBlockLatencyProbe()
        {
            Ctx ctx = CreateService();
            yield return ctx.Service.LoginAsync("ws://test", "device-1", null).ToCoroutine();
            string heldResponse = null;
            ctx.Connection.Responder = json =>
            {
                NetEnvelope env = GameFramework.Utility.Json.ToObject<NetEnvelope>(json);
                string response = ctx.Server.Handle(json);
                if (env.t != NetMsg.Ops) return response;
                heldResponse = response;
                return null;
            };
            ctx.Save.mainWarehouse[0] = new GridItemStack(5001, 20, 3, 0);
            UniTask flush = ctx.Service.FlushAsync();
            // WaitAsync().AsUniTask() 的完成由 PlayerLoop 继续，先等到经济请求实际发出。
            for (int frame = 0; heldResponse == null && frame < 10; frame++)
            {
                yield return null;
            }
            Assert.That(heldResponse, Is.Not.Null);
            ctx.Service.UpdateLatency(true);
            Assert.That(ctx.CountSent(NetMsg.Ping), Is.EqualTo(1));
            Assert.That(ctx.Service.Latency.HasSamples, Is.True);
            ctx.Connection.OnMessage(heldResponse);
            yield return flush.ToCoroutine();
        }

        [UnityTest]
        public IEnumerator UnauthorizedPing_ClearsCurrentAccountIdentity()
        {
            Ctx ctx = CreateService();
            yield return ctx.Service.LoginAsync("ws://test", "device-1", null).ToCoroutine();
            ctx.Connection.Responder = json =>
            {
                NetEnvelope env = GameFramework.Utility.Json.ToObject<NetEnvelope>(json);
                return GameFramework.Utility.Json.ToJson(new NetEnvelope
                {
                    t = NetMsg.Err, rid = env.rid, code = NetErrCode.Unauthorized,
                });
            };
            ctx.Service.UpdateLatency(true);
            Assert.That(ctx.Service.UserId, Is.Zero);
            Assert.That(ctx.Service.Latency.HasSamples, Is.False);
            ctx.Now = 2;
            ctx.Service.UpdateLatency(true);
            Assert.That(ctx.CountSent(NetMsg.Ping), Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator LoginWithoutAccountIdentity_FailsExplicitly()
        {
            Ctx ctx = CreateService();
            ctx.Server.UserId = 0;
            LogAssert.Expect(UnityEngine.LogType.Error, "Login response missing userId.");
            bool result = true;
            yield return ctx.Service.LoginAsync("ws://test", "device-1", null)
                .ContinueWith(ok => result = ok).ToCoroutine();
            Assert.That(result, Is.False);
            Assert.That(ctx.Service.UserId, Is.Zero);
        }
    }
}
