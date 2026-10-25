# 04: 服务器 run 会话与协议 v4

**What to build:** 协议版本升 v4。新增 start_run（服务器选种子、显式出生/撤离点，幂等——已有活跃 run 返回现状）、encounter（校验点存在并返回队伍构成，幂等）、loot（校验并标记消耗，按 (runId, pointId) 幂等）；settle 扩展对账（带出物资 ⊆ 开箱所得 ∪ 携带入场 ∪ 战斗所得，战斗所得本阶段信任客户端）；单用户单活跃会话（内存态）与 TTL 清理。fake WS 客户端全流程测试。

**Blocked by:** 03

**Status:** ready-for-agent

- [ ] start_run 幂等：已有活跃 run 返回现状（含已消耗点集合），新开局分配种子
- [ ] encounter/loot 覆盖点不存在/已消耗/无活跃 run 分支；重试返回相同结果
- [ ] settle 对账开箱所得；战斗所得不校验（边界记录在案）
- [ ] TTL 清理超时未结算 run 有测试
- [ ] fake WS 全流程 start_run→encounter→loot→settle 绿

## Comments

### 实施记录（2026-10-25）

**状态：Go 侧已实现，`go test ./...` 与 `go vet ./...` 全绿。** 本票纯 Go，无 C#/Luban 改动；
客户端对接（RunService、探流程装配、重连）是 ticket 05/06/07。

#### 1. 协议 v4（`internal/protocol`）

- `Version = 4`（原 3）。三条新消息 `MsgStartRun = "start_run"` / `MsgEncounter = "encounter"` / `MsgLoot = "loot"`。
- 新增线上类型：`Vec2{x,y}`（float64）、`LootRecord{pointId,itemIds}`。
- `Envelope` 新增字段，JSON 名逐字按契约 §7「线上字段」：
  `resume bool`（请求）、`pointId string`（请求）、`spawnPoint *Vec2` / `extractionPoint *Vec2`（响应）、
  `consumed []string`（响应）、`looted []LootRecord`（响应）、`enemyPartyId int`（响应）、`threat int`（响应）、
  `itemIds []int`（响应，loot 与 start_run 内层共用）、`battleLoot []Stack`（settle 请求）。
- **既有字段零改名/零挪语义**：`seed` 仍是同一个 `int64`（语义升级为"本局主种子"，值来源改为服务器分配）、
  `difficulty` / `runId` / `outcome` / `ops` / `startedAt` / `endedAt` 全部原样。
- `internal/protocol/protocol_test.go`（新）用 `json.Marshal` **逐字断言 wire format**：三条消息的请求/响应、
  start_run 的 consumed/looted 形状、settle 的 battleLoot，并有一条用例断言 settle 的既有字段名与 `seed` 值不变——
  改字段名会先红（客户端按 JSON 名解析）。

#### 2. run 会话管理器（`internal/run`，新包）

- `Manager`：`map[userID]*session` + `sync.Mutex`，单用户单活跃 run。
- `Config{Layout, Tables, Seed, RunID, TTL, Now}`：生产装配只给前两项，其余是测试接缝
  （固定种子/可预测 runId/假时钟），缺省分别是 `crypto/rand` 种子、16 字节随机 hex runId、4h TTL、`time.Now`。
- `Start(userID, difficulty, resume) (View, error)`：
  - 有活跃 run → 返回现状（`RunID/Seed/Difficulty/Spawn/Extraction/Consumed/Looted`），不新建、不改难度，续期 TTL；
  - 无活跃 run + `resume=true` → `core.ErrRejected`，msg `op rejected: no active run`；
  - 无活跃 run + `resume=false` → 分配种子（`[1, 2147483647]`，`crypto/rand` 取低 31 位；**不用 `math/rand`**）→
    `mapgen.Generate` → 记出生/撤离（取生成结果，不重算）→ 入库。
  - 难度校验用 `Tables.Difficulties.Weights(tier)`（既校验 tier 存在，也保证该档有威胁权重，避免开局后才炸）；
    非法难度**不留下活跃 run**。
- `Encounter(userID, runID, pointID) (partyID, threat, error)`：runId 校验 → 点位解析（规范十进制，`e<i>`）→
  越界/`Present=false` 均 err。**不消耗**，重复请求同结果。
- `Loot(userID, runID, pointID) ([]int, error)`：已消耗 → 回放同一清单；否则解析 `r<i>` → 首次成功记入
  `looted`/`consumed`（同序，按开箱先后）。清单在 `looted` 里存副本，返回的切片也是副本（外部改动不影响会话）。
- `ValidateSettle(userID, runID, battleLoot, added, carried)`：`allow = lootedSum + carriedInSum + battleLootSum`，
  任一 `added[itemId] > allow[itemId]` → `ErrRejected`；负数 `battleLoot` 拒绝。
- `LootedSums`、`Active`、`Finish(userID, runID)`（runId 匹配才删）、`Sweep(now) int`、
  `StartSweeper(interval) stop`（幂等 stop，`cmd/sbe-server` 装配）。
- 被拒的请求**不续期** `LastTouched`（防止无效流量把 run 永生），有用例钉住。

#### 3. 分派层（`internal/server`）

- `Server.Run *run.Manager` 新装配点（`cmd/sbe-server` 里 `run.NewManager(...)` + `StartSweeper(0)`；
  未装配时三条新消息返回 `internal`，纯经济用例的既有测试零改动）。
- 三条新消息都走 `requireAuth`；响应直接构造（不带 `Snapshot`）：
  start_run 回 `runId/seed/spawnPoint/extractionPoint/consumed/looted`；encounter 回 `enemyPartyId/threat`；
  loot 回 `itemIds`。
- settle 扩展对账按 `notes/server-run-session.md` 的顺序：
  `Active(userID, runId)` 判定 → 取 `Store.Load(...).Carried` 作携带入场 → `core.SumAdds(env.Ops)` →
  `Run.ValidateSettle(...)` → `ApplySettle` → **只有真正落地（`already==false`）才 `Run.Finish`**。
- `core` 新增两个纯 helper：`SumAdds([]protocol.Op) map[int]int`（只统计 `add`，del/mov/wear 不产生取得）、
  `SumStacks([]core.Stack) map[int]int`。
- 日志摘要（`detailFor`）补三条新消息一行摘要，便于联调对日志。

#### 4. 测试

`internal/run/run_test.go`（20 个用例，内联配置 + 假时钟，期望内容由 `mapgen.Generate` 在测试内独立算出）：
开局幂等/难度拒绝/resume 无 run、encounter 的正常/None/越界/非敌点/外来 runId/跨用户、
loot 的正常/回放/未知点/外来 runId、结算额度（恰好用完/超 1 件/凭空 add/负数 battleLoot/拒后仍可用）、
对账跳过边界、`LootedSums`、`Active`、`Finish`、TTL（超时清理/交互续期/被拒请求不续期）、
种子落在 `[1,2147483647]` 且 500 次有 ≥400 个不同值、注入越界种子报 server 缺陷错误、`StartSweeper` 可停。

`internal/server/run_test.go`（8 个用例，fake WS 客户端，与 `server_test.go` 同一套 `dial/call`）：
- `TestStartRunIsIdempotentAndReflectsLoot`：两次 start_run 同 runId/seed/points；loot 后 resume 读到 consumed/looted；
- `TestStartRunResumeWithoutActiveRunRejected`：resume 无 run → rejected(msg 含 `no active run`)，resume=false 才新建；
- `TestStartRunRejectsUnknownDifficulty`；
- `TestEncounterFlow`：正常 / `e<i>`(None) / `e99` / 物资点当敌点 / 外来 runId / 重复同结果；
- `TestLootFlow`：正常 / 重复同清单 / `r99` / 敌点当物资点 / 外来 runId / consumed 顺序；
- `TestRunMessagesRequireLoginAndActiveRun`：三条新消息的未登录（unauthorized）与无活跃 run（rejected）；
- `TestSettleReconciliationRejectsOverLimit`：开箱 1 件报 2 件 → rejected 且整批不落地、run 仍活跃；
  合法上报落地后 encounter/resume 均报无活跃 run；
- `TestSettleAllowanceIncludesCarriedInAndBattleLoot`：携带入场计入额度、battleLoot 计入额度、battleLoot 超额度拒绝；
- `TestSweepExpiresIdleRun`：`Sweep` 两次（1 后 0），之后 encounter/resume 报无活跃 run；
- `TestFullFlowStartEncounterLootSettle`：全流程绿，且 history 里的 runId/seed 与 start_run 一致。

`internal/protocol/protocol_test.go`（8 个用例，逐字 wire format）与 `internal/core/apply_test.go` 的两个
`SumAdds`/`SumStacks` 用例。

**TDD**：先写 `internal/server/run_test.go` 与 `internal/run/run_test.go` 确认红灯
（`package sbe-server/internal/run is not in std` → 编译不过），再写实现转绿；
实现过程中测试自身修了三处（种子与期望内容不一致、`reflect.DeepEqual` 比进 RID）。

#### 5. 验证结果

```
$ cd SBE-Server && "/c/Program Files/Go/bin/go.exe" vet ./...      # 无输出（通过）
$ "/c/Program Files/Go/bin/go.exe" test -count=1 ./...
ok  sbe-server/cmd/sbe-server 0.941s        ok  sbe-server/internal/mapgen 0.503s
ok  sbe-server/internal/core 0.494s         ok  sbe-server/internal/memorystore 0.660s
ok  sbe-server/internal/gametable 0.602s    ok  sbe-server/internal/mysqlstore 0.799s
ok  sbe-server/internal/globaltable 0.533s  ok  sbe-server/internal/protocol 0.519s
ok  sbe-server/internal/itemtable 0.563s    ok  sbe-server/internal/run 0.430s
ok  sbe-server/internal/mapdef 0.571s       ok  sbe-server/internal/server 1.977s
ok  sbe-server/internal/snowflake 0.424s    ok  sbe-server/internal/store 0.588s
ok  sbe-server/internal/ws 0.879s           （另有 4 个包 no test files）
```

`-race` 未能执行：本机 go 的 race 需要 cgo 而 PATH 里没有 gcc（`cgo: C compiler "gcc" not found`）。
并发安全性靠 `run.Manager` 的单锁结构与 `memorystore` 同风格保证，未做运行时竞态验证。

#### 6. 与契约 §7 的逐项核对

| 契约项 | 实现 | 结论 |
| --- | --- | --- |
| `Version = 4` | `protocol.Version = 4`，有测试钉住 | 一致 |
| start_run 请求 `difficulty` + `resume`（缺省 false） | `Envelope.Difficulty` 既有字段 + 新 `Resume`（`omitempty`，缺省即 false） | 一致 |
| start_run 响应 `runId/seed/spawnPoint/extractionPoint/consumed/looted` | 全部下发；`Vec2{x,y}` float64、`LootRecord{pointId,itemIds}` 逐字 | 一致 |
| 幂等：已有活跃 run 无论 resume 真假返回现状 | `Start` 先判活跃 run 再判 resume | 一致 |
| `resume=true` 无活跃 run → rejected msg `"no active run"` | msg 为 `op rejected: no active run`（`ErrRejected` 前缀来自 `core.ErrRejected.Error()`；契约说客户端不解析 msg） | 一致（msg 带前缀） |
| 已有活跃 run 但 runId 不同且 resume=false：仍返回现状 | 是（`Start` 不看 runId，按 uid 取活跃 run） | 一致 |
| `difficulty` 必须是难度表 tier | `Difficulties.Weights(difficulty)` 校验 | 一致 |
| encounter 响应 `enemyPartyId` + `threat` | 是，取本局生成结果 | 一致 |
| 无活跃 run / runId 不匹配 → rejected | 是 | 一致 |
| pointId 不是本局生成出的敌点 → rejected | 越界、`Present=false`、前缀/非规范十进制全部 rejected | 一致 |
| encounter 幂等、不消耗 | 是（用例重复三次同结果，之后 loot 仍可） | 一致 |
| loot 响应 `itemIds` | 是 | 一致 |
| 首次成功标记消耗并记入 looted；重试回放 | 是（`looted` 记副本，回放返回副本） | 一致 |
| 无活跃 run / runId 不匹配 / 点不存在 → rejected | 是（含 `Present` 检查） | 一致 |
| settle 新增可选 `battleLoot []Stack` | 是 | 一致 |
| 对账 `allow = looted + carriedIn + battleLoot` | 是；`carriedIn` 取服务器快照 `Carried`，不采信客户端当次上报 | 一致 |
| 超限 → rejected 整批不落地 | 是（对账在 `ApplySettle` 之前；用例断言 pull 无残留） | 一致 |
| runId 匹配时结算成功后清除活跃 run | 是，且只在 `already==false` 时 | 一致 |
| runId 幂等语义沿用现状 | 未动 `ApplySettle`（内存/MySQL 实现零改动） | 一致 |
| 不新增错误码，全部 `rejected` | 全部 `fmt.Errorf("%w: ...", core.ErrRejected)`，`classify` 未改 | 一致 |
| Envelope 新增字段名 | 见上「1. 协议 v4」，有 wire format 测试 | 一致 |
| 种子范围 `[1, 2147483647]` | `SeedMin/SeedMax` 常量 + 分配器 + 越界即 server 缺陷错误（不夹取） | 一致 |

#### 7. 已知边界与偏差

1. **对账跳过的边界（契约明确要求记录在案）**：只有"该用户存在活跃 run **且** runId 匹配"时才校验；
   无活跃 run 或 runId 不认识时**不校验**（settle 可能来自旧版客户端或幂等补报，保持 v3 兼容）。
   后果：一个用户若从没调 start_run（或 run 已被 TTL 清理/已结算），settle 的物品来源不受额度约束——
   与 v3 行为一致，属于"探索对账只覆盖活跃局"的既定取舍。
2. **encounter 的 `Present=false` 与"越界"返回同一类错误**（`point e<i> does not exist`）：契约 §6 要求
   None 点"视为不存在"，两者语义相同，未区分错误信息。
3. **`Difficulty` 是既有字段复用**：start_run 请求复用 `Envelope.Difficulty`，没有新增 `startDifficulty` 之类字段
   （契约 §7 的请求示例就是 `"difficulty":2`）。
4. **`Vec2` 与 `mapdef.Vec2` 是两个类型**：`protocol` 不应依赖内部包，分派层做一次显式字段拷贝
   （`internal/protocol` 只依赖标准库）。
5. **`Server.Run` 可空**：既有纯经济测试（`New(items(), limits())`）不需要生成器配置，因此保留 nil 装配点；
   生产路径 `cmd/sbe-server` 必装配。nil 时三条新消息返回 `internal`（配置缺失，不是客户端问题）。
6. **TTL 判定用 `>` 而非 `>=`**：`Sweep` 在 `now - lastTouched == TTL` 时不清理，边界取"超过 TTL"字面语义。
7. **`math/rand` 零使用**：种子走 `crypto/rand`，内容随机走 `mapgen.SplitMix64`（契约 §1 的禁用清单）。

#### 8. 待验证项（需主 agent / 用户）

1. `deploy` + `smoke`（ticket 08 第一条）：容器内起服务后跑 `cmd/smoke`；本票未改 smoke 的消息集
   （register/ping/login 冒烟仍有效），如需覆盖 start_run 需 ticket 08 决定是否扩展。
2. MySQL 存储下的 settle 对账路径：本票的 run 会话全在内存，对账与存储后端无关；但 `-store mysql` 的
   真实联调未在本 worktree 执行（`internal/mysqlstore` 用例只覆盖存储层，不经过 run 会话）。
3. `-race` 未跑（本机无 gcc，见上）。
4. 客户端协议 v4 对接（ticket 05/06/07）：`NetMsg.Version` 需同步升 4，字段名按本文件的 wire format 用例。

