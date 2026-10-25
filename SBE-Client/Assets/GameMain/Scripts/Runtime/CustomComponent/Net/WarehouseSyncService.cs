using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using SepCore.Definition;
using SepCore.Run;
using UnityGameFramework.Runtime;

namespace SepCore.Net
{
    /// <summary>
    /// 仓库同步服务：登录/迁移/拉取、结算上报（runId 幂等 + 断线重试）、大厅操作差分上报、
    /// 权威快照落地。所有出站工作经互斥串行化，保证结算先于其后的大厅操作。
    /// 依赖全部由构造注入，可在 EditMode 用假连接完整测试。
    /// </summary>
    public sealed class WarehouseSyncService
    {
        private readonly IServerConnection _connection;
        private readonly Func<SaveData> _getSave;
        private readonly Func<int, ItemConfig> _itemGetter;
        private readonly Func<GlobalConfig> _globalGetter;
        private readonly Action _persist;
        private readonly SemaphoreSlim _mutex = new SemaphoreSlim(1, 1);
        private readonly Dictionary<long, UniTaskCompletionSource<NetEnvelope>> _pending =
            new Dictionary<long, UniTaskCompletionSource<NetEnvelope>>();

        private readonly WarehouseLedger _ledger = new WarehouseLedger();
        private readonly Func<int, UniTask> _delay;
        private long _nextRid;
        private string _token;

        public WarehouseSyncService(
            IServerConnection connection,
            Func<SaveData> getSave,
            Func<int, ItemConfig> itemGetter,
            Func<GlobalConfig> globalGetter,
            Action persist,
            Func<int, UniTask> delay = null)
        {
            _connection = connection;
            _getSave = getSave;
            _itemGetter = itemGetter;
            _globalGetter = globalGetter;
            _persist = persist;
            _delay = delay ?? (ms => UniTask.Delay(ms));
            _connection.MessageReceived += OnMessageReceived;
            _connection.Disconnected += OnDisconnected;
        }

        /// <summary>
        /// 当前登录令牌（登录成功后由宿主持久化）。
        /// </summary>
        public string Token => _token;

        /// <summary>
        /// 账本是否已与服务器同步过（决定差分是否可用）。
        /// </summary>
        public bool HasSynced => _ledger.HasSynced;

        /// <summary>
        /// 权威快照已落地事件（参数为新快照），大厅 UI 据此刷新。
        /// </summary>
        public event Action<NetSnapshot> SnapshotApplied;

        /// <summary>
        /// 完整登录流程：连接 → auth/register → （首登）迁移/初始化 → 拉取并落地。
        /// 返回是否成功；失败由调用方重试。
        /// </summary>
        public async UniTask<bool> LoginAsync(string serverUri, string deviceUuid, string savedToken)
        {
            if (!_connection.IsConnected)
            {
                bool connected = await _connection.ConnectAsync(serverUri);
                if (!connected)
                {
                    return false;
                }
            }

            NetEnvelope resp = null;
            if (!string.IsNullOrEmpty(savedToken))
            {
                resp = await SendAsync(NetEnvelope.Create(NetMsg.Auth), env => env.token = savedToken);
            }

            if (resp == null || !resp.IsOk)
            {
                NetEnvelope register = NetEnvelope.Create(NetMsg.Register);
                register.device = deviceUuid;
                resp = await SendAsync(register);
                if (resp == null || !resp.IsOk)
                {
                    Log.Error("Register failure: {0}", Describe(resp));
                    return false;
                }
            }

            _token = resp.token;
            if (string.IsNullOrEmpty(_token))
            {
                _token = savedToken;
            }

            NetSnapshot snapshot = resp.snapshot;
            if (snapshot == null)
            {
                Log.Error("Login response missing snapshot.");
                return false;
            }

            if (!snapshot.initialized)
            {
                NetEnvelope migrate = NetEnvelope.Create(NetMsg.Migrate);
                migrate.migrate = BuildMigratePayload(_getSave());
                NetEnvelope migrateResp = await SendAsync(migrate);
                if (migrateResp == null || !migrateResp.IsOk)
                {
                    Log.Error("Migrate failure: {0}", Describe(migrateResp));
                    return false;
                }

                snapshot = migrateResp.snapshot;
            }

            ApplySnapshot(snapshot);
            _persist();
            Log.Info("Warehouse sync login OK. Rows: {0}, Characters: {1}.", snapshot.items.Count, snapshot.characters.Count);
            return true;
        }

        /// <summary>
        /// 大厅经济操作差分上报（穿卸/布局/整理后调用）。串行入队，失败仅记日志——
        /// 服务器为唯一真源，未上报成功的改动会在下次登录拉取时被权威快照纠正。
        /// </summary>
        public async UniTask FlushAsync()
        {
            await RunSerialized(async () =>
            {
                if (_token == null || !_ledger.HasSynced || !_connection.IsConnected)
                {
                    return;
                }

                List<NetOp> ops = EconomyDiff.Compute(_ledger, _getSave(), _itemGetter);
                if (ops.Count == 0)
                {
                    return;
                }

                NetEnvelope env = NetEnvelope.Create(NetMsg.Ops);
                env.ops = ops;
                NetEnvelope resp = await SendAsync(env);
                if (resp != null && resp.IsOk && resp.snapshot != null)
                {
                    ApplySnapshot(resp.snapshot);
                    _persist();
                }
                else
                {
                    Log.Error("Lobby flush failure: {0}", Describe(resp));
                }
            });
        }

        /// <summary>
        /// 结算上报：操作集在入口一次捕获，之后按 runId 幂等重试直至服务器确认；
        /// 确认后以权威快照落地并写本地缓存。强制在线语义：断线期间一直等待。
        /// </summary>
        public async UniTask SettleAsync(string runId, int outcome)
        {
            List<NetOp> ops = EconomyDiff.Compute(_ledger, _getSave(), _itemGetter);
            await RunSerialized(async () =>
            {
                while (true)
                {
                    if (_token == null)
                    {
                        await _delay(2000);
                        continue;
                    }

                    if (!_connection.IsConnected)
                    {
                        await _delay(2000);
                        continue;
                    }

                    NetEnvelope env = NetEnvelope.Create(NetMsg.Settle);
                    env.runId = runId;
                    env.outcome = outcome;
                    env.ops = ops;
                    NetEnvelope resp = await SendAsync(env);
                    if (resp == null)
                    {
                        // 断线：rid 变化无妨，服务器按 runId 幂等
                        continue;
                    }

                    if (!resp.IsOk)
                    {
                        Log.Error("Settle rejected by server (runId {0}): {1}", runId, Describe(resp));
                        return;
                    }

                    if (resp.snapshot != null)
                    {
                        ApplySnapshot(resp.snapshot);
                    }

                    _persist();
                    Log.Info("Settle acked. RunId: {0}, Outcome: {1}.", runId, outcome);
                    return;
                }
            });
        }

        /// <summary>
        /// 把权威快照写入内存存档并重建账本。
        /// 仓库网格经容器往返规范化（重叠/越界自愈）；角色穿戴按穿戴行反推；
        /// 花名册顺序以服务器为准；loadout 仅做与花名册的交集修补。
        /// </summary>
        public void ApplySnapshot(NetSnapshot snapshot)
        {
            SaveData save = _getSave();
            if (save == null)
            {
                Log.Error("ApplySnapshot with no SaveData.");
                return;
            }

            // 1. 仓库网格行（经容器规范化）
            List<GridItemStack> stacks = new List<GridItemStack>();
            foreach (NetSnapRow row in snapshot.items)
            {
                if (row.wearer == 0 && row.itemId > 0 && row.count > 0)
                {
                    stacks.Add(new GridItemStack(row.itemId, row.count, row.x, row.y, row.r));
                }
            }

            GlobalConfig global = _globalGetter();
            if (global == null)
            {
                Log.Error("Global config is not ready.");
                return;
            }

            int columns = global.WarehouseFixedColumn;
            int rows = (global.WarehouseSlotCount + columns - 1) / columns;
            GridItemContainer container = new GridItemContainer(columns, rows, _itemGetter);
            container.LoadFromSaveData(stacks);
            save.mainWarehouse = container.ToSaveData();

            // 2. 花名册与穿戴
            Dictionary<int, CharacterSave> byCid = new Dictionary<int, CharacterSave>();
            if (save.characters != null)
            {
                foreach (CharacterSave c in save.characters)
                {
                    byCid[c.characterId] = c;
                }
            }

            List<CharacterSave> characters = new List<CharacterSave>(snapshot.characters.Count);
            foreach (NetSnapChar snapChar in snapshot.characters)
            {
                CharacterSave c = byCid.TryGetValue(snapChar.cid, out CharacterSave existing)
                    ? existing
                    : new CharacterSave(snapChar.cid, 0, 0);
                c.weaponItemId = 0;
                c.armorItemId = 0;
                characters.Add(c);
            }

            foreach (NetSnapRow row in snapshot.items)
            {
                if (row.wearer == 0)
                {
                    continue;
                }

                ItemConfig config = _itemGetter(row.itemId);
                for (int i = 0; i < characters.Count; i++)
                {
                    if (characters[i].characterId != row.wearer)
                    {
                        continue;
                    }

                    CharacterSave c = characters[i];
                    if (config != null && config.EquipSlot == EquipmentSlotType.Armor)
                    {
                        c.armorItemId = row.itemId;
                    }
                    else
                    {
                        c.weaponItemId = row.itemId;
                    }

                    characters[i] = c;
                    break;
                }
            }

            save.characters = characters;

            // 3. 战备列表与花名册求交（服务器为准增删，保留本地顺序）
            if (save.loadout?.partyCharacterIds != null)
            {
                HashSet<int> roster = new HashSet<int>();
                foreach (NetSnapChar snapChar in snapshot.characters)
                {
                    roster.Add(snapChar.cid);
                }

                List<int> party = new List<int>();
                foreach (int cid in save.loadout.partyCharacterIds)
                {
                    if (roster.Contains(cid) && !party.Contains(cid))
                    {
                        party.Add(cid);
                    }
                }

                save.loadout.partyCharacterIds = party.ToArray();
            }

            _ledger.Rebuild(snapshot);
            SnapshotApplied?.Invoke(snapshot);
        }

        private NetMigratePayload BuildMigratePayload(SaveData save)
        {
            NetMigratePayload payload = new NetMigratePayload();
            if (save == null)
            {
                return payload;
            }

            if (save.mainWarehouse != null)
            {
                foreach (GridItemStack stack in save.mainWarehouse)
                {
                    if (stack.itemId > 0 && stack.count > 0)
                    {
                        payload.items.Add(new NetMigrateItem
                        {
                            itemId = stack.itemId,
                            count = stack.count,
                            x = stack.x,
                            y = stack.y,
                            r = stack.isRotated,
                        });
                    }
                }
            }

            if (save.characters != null)
            {
                foreach (CharacterSave c in save.characters)
                {
                    payload.characters.Add(new NetMigrateChar
                    {
                        cid = c.characterId,
                        weaponItemId = c.weaponItemId,
                        armorItemId = c.armorItemId,
                    });
                }
            }

            return payload;
        }

        private async UniTask RunSerialized(Func<UniTask> work)
        {
            await _mutex.WaitAsync().AsUniTask();
            try
            {
                await work();
            }
            finally
            {
                _mutex.Release();
            }
        }

        private async UniTask<NetEnvelope> SendAsync(NetEnvelope env, Action<NetEnvelope> mutate = null)
        {
            env.rid = ++_nextRid;
            mutate?.Invoke(env);
            UniTaskCompletionSource<NetEnvelope> tcs = new UniTaskCompletionSource<NetEnvelope>();
            _pending[env.rid] = tcs;
            string json = GameFramework.Utility.Json.ToJson(env);
            if (!_connection.TrySend(json))
            {
                _pending.Remove(env.rid);
                return null;
            }

            return await tcs.Task;
        }

        private void OnMessageReceived(string message)
        {
            NetEnvelope resp = GameFramework.Utility.Json.ToObject<NetEnvelope>(message);
            if (resp == null)
            {
                Log.Error("Malformed server message.");
                return;
            }

            if (resp.rid != 0 && _pending.TryGetValue(resp.rid, out UniTaskCompletionSource<NetEnvelope> tcs))
            {
                _pending.Remove(resp.rid);
                tcs.TrySetResult(resp);
            }
        }

        private void OnDisconnected()
        {
            foreach (KeyValuePair<long, UniTaskCompletionSource<NetEnvelope>> pair in _pending)
            {
                pair.Value.TrySetResult(null);
            }

            _pending.Clear();
        }

        private static string Describe(NetEnvelope resp)
        {
            if (resp == null)
            {
                return "connection lost";
            }

            return resp.IsOk ? "ok" : string.Format("{0}: {1}", resp.code, resp.msg);
        }
    }
}
