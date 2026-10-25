using System;
using System.Collections.Generic;
using System.Linq;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using SepCore.Definition;
using SepCore.Net;

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

#pragma warning disable 67
            public event Action Disconnected;
#pragma warning restore 67

            public UniTask<bool> ConnectAsync(string uri)
            {
                IsConnected = true;
                return UniTask.FromResult(true);
            }

            public void Close()
            {
                IsConnected = false;
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

            private void OnMessage(string json)
            {
                MessageReceived?.Invoke(json);
            }
        }

        /// <summary>
        /// 迷你服务器：复刻 Go 服务端核心语义（应用操作集、铸 UID、迁移、runId 幂等）。
        /// </summary>
        private sealed class FakeServer
        {
            public readonly List<NetSnapRow> Rows = new List<NetSnapRow>();
            public readonly List<int> Characters = new List<int>();
            public readonly HashSet<string> Settled = new HashSet<string>();
            public bool Initialized;
            public long NextUid = 1;

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

                        Initialized = true;
                        return OK(env);
                    case NetMsg.Settle:
                        if (Settled.Contains(env.runId))
                        {
                            return OK(env);
                        }

                        Settled.Add(env.runId);
                        ApplyOps(env.ops);
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
                            long uid = op.uid > 0 ? op.uid : tidToUid[op.tid];
                            if (uid > 0)
                            {
                                Rows.First(r => r.uid == uid).wearer = op.cid;
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
                    }
                }
            }

            private string OK(NetEnvelope req, string token = null)
            {
                NetEnvelope resp = new NetEnvelope { t = NetMsg.OK, rid = req.rid, token = token };
                resp.snapshot = Snapshot();
                return GameFramework.Utility.Json.ToJson(resp);
            }

            private static string Err(NetEnvelope req, string code, string msg)
            {
                return GameFramework.Utility.Json.ToJson(new NetEnvelope { t = NetMsg.Err, rid = req.rid, code = code, msg = msg });
            }

            public NetSnapshot Snapshot()
            {
                NetSnapshot snapshot = new NetSnapshot { initialized = Initialized };
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
                ms => UniTask.CompletedTask);
            return ctx;
        }

        [Test]
        public void LoginMigratesLocalSaveAndAppliesSnapshot()
        {
            Ctx ctx = CreateService();
            bool ok = ctx.Service.LoginAsync("ws://test", "device-1", null).GetAwaiter().GetResult();

            Assert.That(ok, Is.True);
            Assert.That(ctx.Service.Token, Is.EqualTo("tok-1"));
            Assert.That(ctx.CountSent(NetMsg.Register), Is.EqualTo(1));
            Assert.That(ctx.CountSent(NetMsg.Migrate), Is.EqualTo(1));
            Assert.That(ctx.Server.Initialized, Is.True);
            Assert.That(ctx.PersistCount, Is.GreaterThanOrEqualTo(1));

            // 服务器铸的行落地：网格 5001 + 1001 穿着 5101
            Assert.That(ctx.Save.mainWarehouse, Has.Count.EqualTo(1));
            Assert.That(ctx.Save.mainWarehouse[0].itemId, Is.EqualTo(5001));
            Assert.That(ctx.Save.characters[0].weaponItemId, Is.EqualTo(5101));
        }

        [Test]
        public void FlushAfterLayoutChange_SendsMovAndAppliesAck()
        {
            Ctx ctx = CreateService();
            ctx.Service.LoginAsync("ws://test", "device-1", null).GetAwaiter().GetResult();

            ctx.Save.mainWarehouse[0] = new GridItemStack(5001, 20, 3, 0);
            ctx.Service.FlushAsync().GetAwaiter().GetResult();

            NetEnvelope opsEnv = ctx.Connection.Sent.FirstOrDefault(e => e.t == NetMsg.Ops);
            Assert.That(opsEnv, Is.Not.Null);
            Assert.That(opsEnv.ops.Any(o => o.k == NetOpKind.Mov), Is.True);
            Assert.That(ctx.Server.Rows.First(r => r.itemId == 5001).x, Is.EqualTo(3));

            // 二次冲刷无差异：不再发送
            int sentBefore = ctx.Connection.Sent.Count;
            ctx.Service.FlushAsync().GetAwaiter().GetResult();
            Assert.That(ctx.Connection.Sent.Count, Is.EqualTo(sentBefore));
        }

        [Test]
        public void SettleRetriesAfterDropUntilAcked()
        {
            Ctx ctx = CreateService();
            ctx.Service.LoginAsync("ws://test", "device-1", null).GetAwaiter().GetResult();

            // 局内结算结果：战利品入库 + 招募 + 换新武器（局内捡的）
            ctx.Save.mainWarehouse.Add(new GridItemStack(5301, 4, 1, 0));
            ctx.Save.characters[0] = new CharacterSave(1001, 5102, 0);
            ctx.Save.characters.Add(new CharacterSave(1003, 0, 0));

            ctx.Connection.FailNextSends = 1; // 第一次发送即失败（模拟断线）
            ctx.Service.SettleAsync("run-42", 1).GetAwaiter().GetResult();

            NetEnvelope settle = ctx.Connection.Sent.FirstOrDefault(e => e.t == NetMsg.Settle);
            Assert.That(settle, Is.Not.Null);
            Assert.That(settle.runId, Is.EqualTo("run-42"));
            Assert.That(settle.outcome, Is.EqualTo(1));
            Assert.That(ctx.Server.Settled, Does.Contain("run-42"));

            // 权威快照落地：新角色在场、新武器穿着、战利品入库
            Assert.That(ctx.Save.characters.Any(c => c.characterId == 1003), Is.True);
            Assert.That(ctx.Save.characters[0].weaponItemId, Is.EqualTo(5102));
            Assert.That(ctx.Save.mainWarehouse.Any(s => s.itemId == 5301), Is.True);
        }

        [Test]
        public void SettleRejected_DoesNotHangAndKeepsLocalState()
        {
            Ctx ctx = CreateService();
            ctx.Service.LoginAsync("ws://test", "device-1", null).GetAwaiter().GetResult();
            ctx.Connection.Responder = json =>
            {
                NetEnvelope env = GameFramework.Utility.Json.ToObject<NetEnvelope>(json);
                return env.t == NetMsg.Settle
                    ? GameFramework.Utility.Json.ToJson(new NetEnvelope { t = NetMsg.Err, rid = env.rid, code = NetErrCode.Rejected, msg = "bad" })
                    : ctx.Server.Handle(json);
            };

            ctx.Service.SettleAsync("run-x", 2).GetAwaiter().GetResult();

            Assert.That(ctx.Server.Settled, Does.Not.Contain("run-x"));
        }

        [Test]
        public void SnapshotWithOverlappingPositions_SelfHeals()
        {
            Ctx ctx = CreateService();
            ctx.Connection.Responder = json =>
            {
                NetEnvelope env = GameFramework.Utility.Json.ToObject<NetEnvelope>(json);
                NetSnapshot snapshot = new NetSnapshot { initialized = true };
                snapshot.characters.Add(new NetSnapChar { cid = 1001 });
                snapshot.items.Add(new NetSnapRow { uid = 1, itemId = 5001, count = 5, x = 0, y = 0 });
                snapshot.items.Add(new NetSnapRow { uid = 2, itemId = 5001, count = 5, x = 0, y = 0 }); // 重叠
                return GameFramework.Utility.Json.ToJson(new NetEnvelope { t = NetMsg.OK, rid = env.rid, token = "tok-1", snapshot = snapshot });
            };

            ctx.Service.LoginAsync("ws://test", "device-1", null).GetAwaiter().GetResult();

            Assert.That(ctx.Save.mainWarehouse, Has.Count.EqualTo(2));
            Assert.That(ctx.Save.mainWarehouse.Sum(s => s.count), Is.EqualTo(10));
            HashSet<string> positions = new HashSet<string>(
                ctx.Save.mainWarehouse.Select(s => s.x + "," + s.y));
            Assert.That(positions.Count, Is.EqualTo(2), "重叠坐标应被容器规范化拆开");
        }
    }
}
