using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using SepCore.Definition;
using SepCore.Run;
using UnityGameFramework.Runtime;

namespace SepCore.Net
{
    /// <summary>登录结果三态。</summary>
    public enum NetLoginOutcome
    {
        /// <summary>登录成功，权威快照已落地。</summary>
        Success,
        /// <summary>服务器明确拒绝凭据（token 失效/账密错误/重名等），重试无意义，需交互登录。</summary>
        AuthRejected,
        /// <summary>网络或服务器故障，可原样重试。</summary>
        Failed,
    }

    /// <summary>一次登录尝试的结果。AuthRejected 时 ErrCode 为服务器错误码，其余为 null。</summary>
    public readonly struct LoginResult
    {
        public readonly NetLoginOutcome Outcome;
        public readonly string ErrCode;

        public LoginResult(NetLoginOutcome outcome, string errCode = null)
        {
            Outcome = outcome;
            ErrCode = errCode;
        }

        public bool Success => Outcome == NetLoginOutcome.Success;
    }

    /// <summary>
    /// 仓库同步服务：登录/迁移/拉取、结算上报（runId 幂等 + 断线重试）、大厅操作差分上报、
    /// 权威快照落地。所有出站工作经互斥串行化，保证结算先于其后的大厅操作。
    /// 信封收发与请求关联委托给 <see cref="NetRpc"/>，依赖全部由构造注入，
    /// 可在 EditMode 用假连接完整测试。
    /// </summary>
    public sealed class WarehouseSyncService
    {
        private readonly NetRpc _rpc;
        private readonly Func<SaveData> _getSave;
        private readonly Func<int, ItemConfig> _itemGetter;
        private readonly Func<GlobalConfig> _globalGetter;
        private readonly Action _persist;
        private readonly SemaphoreSlim _mutex = new SemaphoreSlim(1, 1);
        private readonly WarehouseLedger _ledger = new WarehouseLedger();
        private readonly Func<int, UniTask> _delay;
        private string _token;

        public WarehouseSyncService(
            NetRpc rpc,
            Func<SaveData> getSave,
            Func<int, ItemConfig> itemGetter,
            Func<GlobalConfig> globalGetter,
            Action persist,
            Func<int, UniTask> delay = null)
        {
            _rpc = rpc;
            _getSave = getSave;
            _itemGetter = itemGetter;
            _globalGetter = globalGetter;
            _persist = persist;
            _delay = delay ?? (ms => UniTask.Delay(ms));
            _rpc.Disconnected += OnDisconnected;
            _rpc.Unauthorized += OnUnauthorized;
        }

        /// <summary>
        /// 当前登录令牌（登录成功后由宿主持久化）。
        /// </summary>
        public string Token => _token;

        /// <summary>服务端账号主键；未完成登录或已断线时为 0，不暴露认证令牌。</summary>
        public long UserId { get; private set; }

        /// <summary>
        /// 账本是否已与服务器同步过（决定差分是否可用）。
        /// </summary>
        public bool HasSynced => _ledger.HasSynced;

        /// <summary>
        /// 权威快照已落地事件（参数为新快照），大厅 UI 据此刷新。
        /// </summary>
        public event Action<NetSnapshot> SnapshotApplied;

        /// <summary>
        /// 缓存 token 静默续登：连接 → auth → （首登）迁移 → 快照落地 → 拉取战绩。
        /// 无缓存 token 时直接返回 AuthRejected（调用方应转入账密登录）。
        /// </summary>
        public async UniTask<LoginResult> TryTokenLoginAsync(string serverUri)
        {
            AccountSession.Load(out _, out string token, out _);
            if (string.IsNullOrEmpty(token))
            {
                return new LoginResult(NetLoginOutcome.AuthRejected);
            }

            return await SendLoginAsync(serverUri, token, () =>
            {
                NetEnvelope env = NetEnvelope.Create(NetMsg.Auth);
                env.token = token;
                return env;
            });
        }

        /// <summary>
        /// 账密注册（register=true）或登录（register=false）：连接 → register/login → 后置流程同上。
        /// 成功后由宿主（NetComponent）持久化 uid/token/username。
        /// </summary>
        public async UniTask<LoginResult> TryCredentialLoginAsync(string serverUri, string username, string password, bool register)
        {
            return await SendLoginAsync(serverUri, null, () =>
            {
                NetEnvelope env = NetEnvelope.Create(register ? NetMsg.Register : NetMsg.Login);
                env.username = username;
                env.password = password;
                return env;
            });
        }

        /// <summary>
        /// 登录公共流程：连接 → 发送登录请求 → 校验应答身份 → （首登）迁移 → 快照落地 → 拉取战绩。
        /// 应答缺失（超时/断连）视为可重试故障；有应答但被拒为 AuthRejected（附错误码）。
        /// </summary>
        private async UniTask<LoginResult> SendLoginAsync(string serverUri, string fallbackToken, System.Func<NetEnvelope> buildRequest)
        {
            UserId = 0;
            _rpc.ResetLatency();
            if (!_rpc.IsConnected)
            {
                bool connected = await _rpc.ConnectAsync(serverUri);
                if (!connected)
                {
                    return new LoginResult(NetLoginOutcome.Failed);
                }
            }

            NetEnvelope resp = await _rpc.SendAsync(buildRequest());
            if (resp == null || !resp.IsOk)
            {
                Log.Error("Login request failure: {0}", Describe(resp));
                return resp == null
                    ? new LoginResult(NetLoginOutcome.Failed)
                    : new LoginResult(NetLoginOutcome.AuthRejected, resp.code);
            }

            _token = resp.token;
            if (string.IsNullOrEmpty(_token))
            {
                // auth 应答不下发新 token，沿用缓存令牌
                _token = fallbackToken;
            }
            if (string.IsNullOrEmpty(_token))
            {
                Log.Error("Login response missing token.");
                return new LoginResult(NetLoginOutcome.Failed);
            }

            if (resp.userId <= 0)
            {
                Log.Error("Login response missing userId.");
                return new LoginResult(NetLoginOutcome.Failed);
            }
            long userId = resp.userId;

            NetSnapshot snapshot = resp.snapshot;
            if (snapshot == null)
            {
                Log.Error("Login response missing snapshot.");
                return new LoginResult(NetLoginOutcome.Failed);
            }

            if (!snapshot.initialized)
            {
                NetEnvelope migrate = NetEnvelope.Create(NetMsg.Migrate);
                migrate.migrate = BuildMigratePayload(_getSave());
                NetEnvelope migrateResp = await _rpc.SendAsync(migrate);
                if (migrateResp == null || !migrateResp.IsOk)
                {
                    Log.Error("Migrate failure: {0}", Describe(migrateResp));
                    return new LoginResult(NetLoginOutcome.Failed);
                }

                snapshot = migrateResp.snapshot;
            }

            ApplySnapshot(snapshot);
            _persist();
            Log.Info("Warehouse sync login OK. Rows: {0}, Characters: {1}.", snapshot.items.Count, snapshot.characters.Count);

            // 拉取战绩并覆盖本地缓存（settlements 审计数据即战绩存储）。
            // 拉取失败由登录流程重试，避免断线后仍把 OnlineReady 标记为 true。
            NetEnvelope historyResp = await _rpc.SendAsync(NetEnvelope.Create(NetMsg.History));
            if (historyResp != null && historyResp.IsOk)
            {
                ApplyHistory(historyResp.history);
                _persist();
            }
            else
            {
                Log.Error("History fetch failure: {0}", Describe(historyResp));
                return new LoginResult(NetLoginOutcome.Failed);
            }

            UserId = userId;
            return new LoginResult(NetLoginOutcome.Success);
        }

        /// <summary>
        /// 用服务器战绩整体覆盖本地 runHistory 缓存。
        /// </summary>
        public void ApplyHistory(List<NetRunRecord> history)
        {
            SaveData save = _getSave();
            if (save == null)
            {
                Log.Error("ApplyHistory with no SaveData.");
                return;
            }

            save.runHistory = new List<RoundRecord>();
            if (history != null)
            {
                foreach (NetRunRecord run in history)
                {
                    save.runHistory.Add(new RoundRecord(
                        (RoundResultType)run.outcome,
                        (DifficultyTier)run.difficulty,
                        run.seed,
                        run.startedAt,
                        run.endedAt));
                }
            }
        }

        /// <summary>
        /// 大厅经济操作差分上报（穿卸/布局/整理后调用）。串行入队，失败仅记日志——
        /// 服务器为唯一真源，未上报成功的改动会在下次登录拉取时被权威快照纠正。
        /// </summary>
        public async UniTask FlushAsync()
        {
            await RunSerialized(async () =>
            {
                if (_token == null || !_ledger.HasSynced || !_rpc.IsConnected)
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
                NetEnvelope resp = await _rpc.SendAsync(env);
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
        /// record 为本局战绩（结局/难度/种子/起止时间），随结算落服务器审计列。
        /// </summary>
        public async UniTask SettleAsync(string runId, RoundRecord record)
        {
            List<NetOp> ops = EconomyDiff.Compute(_ledger, _getSave(), _itemGetter);
            // 结算仅提交资产变化，战备配置由大厅 ops 独立同步。
            ops.RemoveAll(op => op.k == NetOpKind.Party || op.k == NetOpKind.Difficulty || op.k == NetOpKind.Carry);
            await RunSerialized(async () =>
            {
                while (true)
                {
                    if (_token == null)
                    {
                        await _delay(2000);
                        continue;
                    }

                    if (!_rpc.IsConnected)
                    {
                        await _delay(2000);
                        continue;
                    }

                    NetEnvelope env = NetEnvelope.Create(NetMsg.Settle);
                    env.runId = runId;
                    env.outcome = (int)record.outcome;
                    env.difficulty = (int)record.difficultyId;
                    env.seed = record.seed;
                    env.startedAt = record.startedAt;
                    env.endedAt = record.endedAt;
                    env.ops = ops;
                    NetEnvelope resp = await _rpc.SendAsync(env);
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
                    Log.Info("Settle acked. RunId: {0}, Outcome: {1}.", runId, record.outcome);
                    return;
                }
            });
        }

        /// <summary>
        /// 把权威快照写入内存存档并重建账本。
        /// 仓库网格经容器往返规范化（重叠/越界自愈）；角色穿戴按穿戴行反推；
        /// 花名册顺序以服务器为准；战备配置（出战队列/难度/携带计划）以服务器为准，
        /// 快照空队列写回 null 保留"从未配置则默认组队"的既有流程。
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

            // 3. 战备配置以服务器为准（快照空队列 = 未配置，写回 null 保留默认组队流程）
            if (save.loadout == null)
            {
                save.loadout = new LoadoutSave();
            }

            HashSet<int> roster = new HashSet<int>();
            foreach (NetSnapChar snapChar in snapshot.characters)
            {
                roster.Add(snapChar.cid);
            }

            List<int> party = new List<int>();
            if (snapshot.party != null)
            {
                foreach (int cid in snapshot.party)
                {
                    if (roster.Contains(cid) && !party.Contains(cid))
                    {
                        party.Add(cid);
                    }
                }
            }

            save.loadout.partyCharacterIds = party.Count > 0 ? party.ToArray() : null;
            save.loadout.difficultyId = (DifficultyTier)snapshot.difficulty;

            save.loadout.carriedItems = new List<ItemStack>();
            if (snapshot.carried != null)
            {
                foreach (NetStack stack in snapshot.carried)
                {
                    if (stack.itemId > 0 && stack.count > 0)
                    {
                        save.loadout.carriedItems.Add(new ItemStack(stack.itemId, stack.count));
                    }
                }
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

            if (save.loadout != null)
            {
                if (save.loadout.partyCharacterIds != null && save.loadout.partyCharacterIds.Length > 0)
                {
                    payload.party = (int[])save.loadout.partyCharacterIds.Clone();
                }

                payload.difficulty = (int)save.loadout.difficultyId;

                if (save.loadout.carriedItems != null)
                {
                    foreach (ItemStack stack in save.loadout.carriedItems)
                    {
                        if (stack.itemId > 0 && stack.count > 0)
                        {
                            payload.carried.Add(new NetStack(stack.itemId, stack.count));
                        }
                    }
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

        private void OnDisconnected()
        {
            ResetLoginState();
        }

        private void OnUnauthorized()
        {
            ResetLoginState();
        }

        private void ResetLoginState()
        {
            UserId = 0;
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
