# 05: 客户端 RunService 域服务

**What to build:** 预备因子票：新网络域服务封装 start_run/encounter/loot 三消息——请求发起、超时判定、断线放行、错误码归一；拒绝语义以事件出口（供 UI 提示"网络延迟较大，请检查网络连接"）。不触任何 UI。EditMode 用 fake 连接测试。注册进网络域服务清单。

**Blocked by:** 04

**Status:** resolved

- [x] EditMode 覆盖成功/超时/断线放行/错误码分支
- [x] 超时阈值可配置
- [x] 事件出口携带拒绝原因与目标点，供 UI 消费
- [x] 不引入对 UI/流程组件的依赖（纯域服务）

## Comments

### 实施记录（2026-10-25）

**状态：C# 侧已实现，EditMode 待主 agent 在已开 Editor 内经 mcp-for-unity 执行回填。**
本票纯客户端，无 Luban/服务器改动。

#### 1. 协议客户端对齐（`NetProtocol.cs`）

- `NetMsg.Version` 3 → **4**；新增 `StartRun = "start_run"`、`Encounter = "encounter"`、`Loot = "loot"`。
- 新增 DTO：`NetVec2 { float x; float y; }`、`NetLootRecord { string pointId; List<int> itemIds; }`。
- `NetEnvelope` 增补 10 个字段，JSON 名与 Go 端 tag **逐字一致**（已用脚本比对
  `SBE-Server/internal/protocol/protocol.go` 的 `// ---- 探索 run` 段落，缺失 0、多余 0）：
  `resume / pointId / spawnPoint / extractionPoint / consumed / looted / enemyPartyId / threat /
  itemIds / battleLoot`。既有字段零改名。
- 坐标用 `float`（契约 §7 允许 `float x/y` 或 `NetVec2`），Go 端是 `float64`；瓦片坐标均为整数，
  float↔float64 无精度损失。

#### 2. `RunService`（`Runtime/CustomComponent/Net/RunService.cs`，命名空间 `SepCore.Net`）

- 构造：`RunService(NetRpc rpc, int timeoutMilliseconds = 3000, Func<int, UniTask> delay = null)`。
  缺省 delay 用 `UniTask.Delay(ms, DelayType.Realtime)`（暂停时阈值照走）；EditMode 注入不真等的 gate。
- 三条方法：`StartRunAsync(DifficultyTier, bool resume = false)`、`EncounterAsync(string pointId)`、
  `LootAsync(string pointId)`，返回 `StartRunResult` / `EncounterResult` / `LootResult`，
  各自带 `Outcome`（`Success|Timeout|Disconnected|Rejected`）、失败时的 `ErrCode`/`ErrMsg`。
- **超时**：`UniTask.WhenAny<NetEnvelope>(send, _delay(timeoutMs))`；右侧（含泛型单参重载
  `WhenAny<T>(UniTask<T>, UniTask)`）先到 → `Timeout`。
- **断线放行**：`SendAsync` 返回 null（发送失败或等待期间断线）→ `Disconnected`，
  不抛异常、不阻塞；`LootAsync` 此时**不**补记 consumed。
- **错误码归一**：服务器 `err` → `Rejected` + `code`/`msg`；超时 → `Timeout`（`ErrCode` 为 null）。
- **事件出口**：`event Action<RunInteractionRejectedArgs> InteractionRejected`，
  `RunInteractionRejectedArgs { string PointId; RunRequestOutcome Reason; string ErrCode; }`；
  start_run 失败时 `PointId` 为 null。
- **文案常量**：`public const string RejectionMessage = "网络延迟较大，请检查网络连接"`。
- **状态缓存**：`RunId` / `Seed` / `ConsumedPointIds` / `HasActiveRun` / `IsConsumed(pointId)`，
  `StartRunAsync` 成功后就地更新（含 resume 恢复的 consumed/looted），成功 loot 后补记该点。
  另提供 `ResetRun()` 供 06/07 结算后或判废后丢弃缓存（**未在契约中要求，若 06 不需要可删**）。
- 依赖面：`NetRpc`、`SepCore.Definition.DifficultyTier`、UGF `Log` —— 无 UI/Procedure 引用。

#### 3. `NetRpc` 的改动（唯一一处）

新增 `public void Abandon(long rid)`：超时时把等待项移出 `_pending` 并以 null 完成，
避免请求表悬挂。`SendAsync` 签名与语义**零改动**，既有调用方（登录/结算/ops/ping）行为不变。
`RunService` 在超时后调用它——rid 单调递增不复用，迟到应答匹配不到任何请求。

#### 4. 注册

`NetComponent` 增加 `public RunService Run { get; private set; }`，`Awake` 里 `Run = new RunService(_rpc);`
（紧邻 `Warehouse` 构造之后，`Warehouse` 的构造参数保持原样）。

#### 5. 测试（`Tests/EditMode/Net/RunServiceTests.cs`）

假连接（`FakeServerConnection`，含 `FailNextSends`）+ 迷你 run 服务器（`FakeRunServer`，
复刻契约 §7 的应答形状与 reject 语义）。14 个用例：

| 用例 | 覆盖点 |
| --- | --- |
| `ProtocolVersionIsV4` | `NetMsg.Version == 4` + 三条消息名（防回退） |
| `EnvelopeWireNamesMatchProtocolV4` | 信封 10 个新字段的 JSON 名 + 反序列化回读 |
| `DefaultTimeoutIsThreeSeconds` | 默认阈值 3000 |
| `StartRunSendsDifficultyAndCachesRunState` | 成功路径：runId/seed/spawn/extraction、请求体 v4+difficulty+resume=false、缓存更新 |
| `StartRunResumeAppliesConsumedSnapshot` | resume=true 应用 consumed/looted |
| `EncounterReturnsServerPartyAndThreat` | 成功路径：enemyPartyId/threat、请求体 runId/pointId |
| `LootReturnsItemsAndMarksPointConsumed` | 成功路径：itemIds、本地 consumed 补记 |
| `TimeoutReportsTimeoutAndRaisesEvent` | 超时 → Timeout + 事件（pointId/Reason/ErrCode=null）+ 文案常量 |
| `TimeoutThresholdIsConfigurable` | 小阈值 25ms：阈值传给 delay 且判超时 |
| `LargeTimeoutThresholdWaitsForServer` | 大阈值 60000ms：应答先到即成功 |
| `SendFailureReportsDisconnected` | `IsConnected=false` → Disconnected |
| `TrySendReturningFalseReportsDisconnected` | 连接仍在但发送失败 → Disconnected |
| `DisconnectWhileWaitingReportsDisconnected` | 等待期间 Close → Disconnected，不补记 consumed |
| `ServerRejectionReportsRejectedWithCode` | e1（None 点）/ r99 → Rejected + code |
| `StartRunRejectedWithoutActiveRunOnResume` | resume 无活跃 run → Rejected，无缓存 |
| `LateResponseDoesNotAffectNextRequest` | 超时后迟到应答不影响下一次请求与 consumed |

#### 6. 与契约 §7/§10 的逐项核对

| 契约项 | 实现 | 结论 |
| --- | --- | --- |
| `NetMsg.Version = 4` | 是，有测试钉住 | 一致 |
| 三条消息名 `start_run`/`encounter`/`loot` | 是 | 一致 |
| Envelope 10 个新字段 JSON 名 | 逐字一致（脚本比对 + 测试钉住） | 一致 |
| start_run 请求 `difficulty` + `resume`（缺省 false） | `difficulty`/`resume` 字段，参数缺省 false | 一致 |
| start_run 响应 runId/seed/spawnPoint/extractionPoint/consumed/looted | 全部返回到 `StartRunResult` | 一致 |
| encounter 响应 enemyPartyId/threat | 是 | 一致 |
| loot 响应 itemIds | 是 | 一致 |
| 超时默认 3000ms、可配、delay 注入 | 是（`DelayType.Realtime`） | 一致 |
| 断线放行（不抛、不阻塞） | 返回 `Disconnected` | 一致 |
| 服务器 err → `Rejected` 带 code | 是 | 一致 |
| 事件 `InteractionRejected`（pointId / 原因枚举 / 错误码） | 是 | 一致 |
| 文案常量 `网络延迟较大，请检查网络连接` | `RunService.RejectionMessage` | 一致 |
| `NetComponent.Run` 与 `Warehouse` 同形状 | 是 | 一致 |
| `NetRpc` 仅加可选能力，既有调用零改动 | 加了**独立方法** `Abandon(rid)`，`SendAsync` 未动 | 一致（更保守） |
| 不引用 UI/Procedure | 仅 NetRpc + DifficultyTier + Log | 一致 |

#### 7. 偏差与取舍

1. **超时放弃等待项用新增 `Abandon(rid)` 而非给 `SendAsync` 加参数**：契约 §10 允许
   `SendAsync(env, mutate, timeoutMs)`，但那会让 `RunService` 与通用 RPC 层耦合超时语义，
   且要改动既有方法签名（虽然是可选参数）。改成独立方法后 `SendAsync` 一行未动，
   其它域服务（Warehouse）行为完全不受影响。
2. **`start_run` 应答字段缺损（ok 但缺 runId/seed/spawn/extraction）**：按"不做静态兜底"原则报
   `Log.Error` 并返回 `Rejected + NetErrCode.Internal`（同时发 `InteractionRejected`）。
   契约的结果枚举只有四态，把服务器缺陷归入 `Rejected` 是最接近的语义；带 `internal` 码便于调用方区分。
3. **`LootAsync` 成功后本地补记 consumed 前先判重**：服务器 loot 幂等（回放同一清单），
   重复成功不应让本地 `ConsumedPointIds` 出现重复项。
4. **`float` 而非 `double`**：契约允许；瓦片坐标为整数，双端取值一致。
5. **`ResetRun()` 是超出本票验收项的便利方法**（3 行）。ticket 07 判废/结算后需要丢缓存；
   若主 agent 认为属于提前实现，可直接删除，本票验收项不依赖它。

#### 8. 待验证项（需主 agent / 用户）

1. **EditMode 尚未执行**（本 worktree 无 Editor，纪律禁止启动）。请在主工作目录的已开 Editor 里
   经 mcp-for-unity 跑 `Tests/EditMode/Net/RunServiceTests.cs`；预期 14 个用例全绿。
   重点看两类用例：`TimeoutReportsTimeoutAndRaisesEvent`（依赖注入的 gate，涉及 UniTask.WhenAny 时序）
   与 `LateResponseDoesNotAffectNextRequest`（依赖 `Abandon` 后的 rid 不复用）。
2. `RunService.cs` / `RunServiceTests.cs` 是新增文件，**无 `.meta`**：需主 agent 合并后在主目录让
   Editor 导入生成并补提交。
3. 编译验证留给 Editor：本票未执行 `dotnet build` / `msbuild`（纪律）。静态自查的潜在风险点见下。
4. 与真服务器的联调属于 ticket 06/08（本票只有 fake 连接）。

#### 9. 潜在编译风险点（静态自查，逐条）

1. **`UniTask.WhenAny<T>(UniTask<T>, UniTask)` 的元组元素名**：返回
   `UniTask<(bool hasResultLeft, T result)>`；代码用 `(bool sendCompleted, NetEnvelope response) = await ...`
   显式解构。元组元素名不参与解构匹配（按位置），所以即使 `hasResultLeft` 改名也不会破编译——
   但**若该重载被解析到 `WhenAny<T>(UniTask<T>[] tasks)`（params）就会编译失败**。为避免歧义，
   调用处显式写了泛型实参：`UniTask.WhenAny<NetEnvelope>(send, _delay(...))`，两个实参分别是
   `UniTask<NetEnvelope>` 与 `UniTask`，只有左右重载匹配。
2. **`DelayType.Realtime`**：`UniTask.Delay(int, DelayType, ...)` 存在于项目内 UniTask 源码
   （`Runtime/UniTask.Delay.cs:159`），`DelayType` 在同文件定义。
3. **`IReadOnlyList<string>` 返回值**：`_consumed` 是 `List<string>`，隐式满足；`System.Collections.Generic`
   已 using。Unity 2022.3 默认 .NET Standard 2.1，`IReadOnlyList` 可用。
4. **`readonly struct`**：仓库既有先例（`WarehouseSyncService.cs` 的 `LoginResult`、
   Luban `SegmentSaveState`），C# 8+ 无问题。
5. **`Log.Warning(format, arg1..arg4)`**：`request.t`(string) / `pointId ?? "none"`(string) /
   `outcome`(enum) / `errCode ?? errMsg ?? "no response"`(string) 都是泛型单参重载可用类型；
   UGF `Log` 的 `Error<T1,T2>` / `Warning<T1..T4>` 重载齐全（已核对 `Log.cs`）。
   枚举以 `{2}` 格式化输出名字（`Enum.ToString()`）。
6. **`Envelope.consumed`/`looted`/`itemIds` 为 null 的可能**：`JsonUtility` 对缺失数组字段保留
   字段初始值；`List<T>` 字段无初始化 → 反序列化缺失时为 null，代码已 `?? new List<...>()`。
   注意 `NetLootRecord.itemIds` 有初始值 `new List<int>()`，`JsonUtility` 对**缺失**字段不会清空
   初始值（保持空 list），符合预期。
7. **`[UnityTest]` 里 `yield return ...ToCoroutine()`**：与 `WarehouseSyncServiceTests` 同模式；
   `ContinueWith(r => result = r)` 返回 `UniTask`（`Action<T>` 重载），`ToCoroutine()` 返回 `IEnumerator`。
8. **`Assert.That(json, Does.Contain(name), json)`**：NUnit 3.x 的三参数重载是
   `(bool condition, string message, params object[] args)` / `(actual, constraint, message, args)`；
   `Does.Contain` 用于字符串是合法 constraint。仓库 NUnit 版本为 Unity 内置 3.5+
   （`WarehouseSyncServiceTests` 用了 `Is.EqualTo`/`Has.Count.EqualTo`/`Does.Contain` 等同类 API）。
9. **`float` 断言**：改成 `1f` / `-16f` 等字面量，避免 `int` 与 `float` 比较时的隐式转换告警
   （实际 `Is.EqualTo(1)` 也能过，但显式 float 更稳）。
10. **`NetVec2` 的 `x`/`y` 是 `float`，Go 端 `float64`**：`JsonUtility` 序列化为
    `{"x":1,"y":-16}`（整数 float 不带小数），与 Go 的 `%g` 输出一致；反序列化无问题。
11. **新增 `Abandon` 与 `_pending` 的并发**：与既有 `SendAsync`/`OnDisconnected` 一致，全部在主线程
    （由 `NetComponent.Update` 泵送），未加锁——与现有代码同假设。
12. **`NetComponent.Run` 与 `SepCore.Run` 命名空间**：`NetComponent` 在 `SepCore.Net`，
   属性名 `Run` 与命名空间 `SepCore.Run` 不冲突（属性访问需实例限定）；已 grep 确认无既有
   成员/类型叫 `RunService`/`StartRunResult`/`EncounterResult`/`LootResult`。
13. **`NetProtocol.cs` 里 `<see cref="consumed"/>` 引用小写字段**：XML doc 的 cref 到字段合法；
    若编译器把 doc 警告视为错误会有影响，但仓库未开启 `DocumentationFile` + warnaserror。

### 结票记录（2026-10-26，主 agent）

Status 置 resolved：实现已合并进 `feature/exploration-online-v1`，双轴代码审查通过（结论见 spec Comments），
agent 可验的验收项已勾选；标注"待人工"的项统一归 **ticket 08**（Play Mode / 联调 / 网络劣化演练）。

- Go 侧证据：`go test -count=1 ./...` 全绿；真实容器 deploy+smoke 绿，wsprobe 实测协议 v4 往返。
- C# 侧证据：mcp-for-unity 全量 EditMode 375 条，除存量 `FogTilemapControllerTests`（Tilemap 无 Grid）外全绿。
- 双端种子向量逐字一致（notes/vectors.md，三方复算）。
