# 07: 断线重连续局

**What to build:** 重连并恢复登录后，经 start_run 幂等恢复活跃 run：按同种子重建场景摆位（预测结果必然一致）、应用服务器返回的已消耗点状态，玩家接着当前进度继续游戏。

**Blocked by:** 06

**Status:** ready-for-agent

- [ ] EditMode 覆盖恢复流程：摆位重建一致、消耗状态正确应用
- [ ] 重连后开过的箱子不可再开、内容与消耗记录一致
- [ ] 无活跃 run 时恢复路径不误建新局

## Comments

### 实施记录（2026-10-26）

**状态：C# 侧已实现并静态自查，EditMode 与 Play Mode 待主 agent / 人工执行回填。**
分支 `ticket/07-reconnect-resume`（基于 `feature/exploration-online-v1`，同 tip，merge 为 no-op），
2 个 commit。未跑 Unity / batchmode / dotnet build（纪律）。新增 3 个 `.cs` 无 `.meta`。

#### 1. 恢复触发与执行（验收项 1、3）

- **`NetComponent` 重上线出口**：`event Action BackOnline` + `bool TryConsumeBackOnline()`
  （一次性标记，取走即清）。仅在两条登录成功路径（`TryLoginAsync`/`TryCredentialLoginAsync` 里
  `OnlineReady = true` 之后）调用 `OnBackOnline()` 置标记并触发事件——两条路径成功前都先把
  `OnlineReady` 置 false，故必然是 false→true 迁移；`OnDisconnected` 与登录失败路径不触发。
- **`ProcedureMain` 轮询接线**：`OnUpdate` → `TryBeginResumeRun()`，条件全部满足才发请求：
  1) 本流程尚未尝试过（`_resumeAttempted`，**一次会话一次尝试**，失败不重试）；
  2) 不在 in-flight（`_resumeInFlight`，**in-flight 去重**）；
  3) 当前子状态是 `MainExplorationBattleState`（用 `Fsm.CurrentState is MainExplorationBattleState`
   判定"是否在探索中"——选它而非 `MainCamera != null`/`Round.HasActiveRun`：MainCamera 在探索与
   战斗全程非空但地图构建早期也可能已生成；`Round.HasActiveRun` 在离线路径也非空，二者都无法
   区分"探索循环内"。子状态判定最稳：地图构建未持有本地局、结算/切场景没有恢复可言）；
  4) `Net.OnlineReady` 且 `TryConsumeBackOnline()` 为 true（false→true 迁移，取走即清）。
- **恢复不重建地图**：直接 `Run.StartRunAsync(session.Difficulty, resume: true)`
  （difficulty 取 `RoundSession.Difficulty`，比 `ProcedureMain.Difficulty` 更贴近本局事实）。
  成功后决策走 `RunResumeEvaluator`（见下）；**绝不**以 resume=false 重开。
- **决策（`RunResumeEvaluator`，纯逻辑可静态测）**：
  - resume 被拒（含应答缺损的 internal 码）→ `RunInvalid`：`AbortInvalidatedRun("本局已失效")` →
    `NetworkNotice.Show` 提示 + 清 Round/TurnBattle/Random/Run 缓存 + 回大厅；
  - 超时/断线 → `NetworkDown`：不失效、恢复尝试结束，玩家继续本地游玩，不自动重试
    （后续交互失败照旧走各自的拒绝提示）；
  - 成功 → 校验 `result.Seed == RoundSession.Seed`，不一致 `Log.Error` + 按失效处理；
    spawn/extraction 与当前局不一致只 `Log.Warning` 保留本地值（服务器幂等保证同 run 必同值）。
- **消耗状态应用**：`ApplyConsumedSnapshot` 遍历 `GameEntry.Entity.GetAllLoadedEntities()`，
  `entity.Logic as ResourcePointLogic` 收集 `Data`，交 `ResourcePointRecovery.Apply`；
  回放与本地不一致抛 `InvalidOperationException`，`ResumeRunAsync` 捕获后同样按失效处理。
  `RunService.IsConsumed` 镜像已由 `StartRunAsync` 内部重建（ticket 05 测试覆盖），未重复灌。

#### 2. 消耗状态应用（验收项 2）

- **`ResourcePointRecovery`**（`Runtime/Entity/`，`SepCore.Entity`，半纯逻辑直接操作
  `ResourcePointData`，不依赖 Entity 组，可 EditMode 直测）：
  - 未就绪点（`ContentsReady == false`）：`ApplyItems(record.itemIds)` 一次性灌入 +
    `DroppedItemCount = ItemCount` 填满 → `IsCompleted` 为真，不可再交互；
  - 已就绪点（本地已开过、可能只掉了一半）：只校验 `ItemIds` 与回放一致，不动本地状态
    （不替玩家把剩余件数掉出来）；
  - 记录匹配不到实体 / 同一点回放两条不一致清单 → 抛 `InvalidOperationException`（不静默）。
- **偏差（超出字面验收项，理由见下）**：`MainMapBuildingState` 装配物资点时应用 start_run
  回放的 `Looted`（新增 `TryGetLootedItems` 查找，命中的点装配成"已开完"）。原因：契约 §7
  规定 start_run 幂等——uid 已有活跃 run 时**无论 resume 真假**都返回该 run 现状。玩家断线后
  若先回大厅再开"新"局，走的就是这条幂等路径（resume=false 但返回旧局），不应用 `Looted`
  会导致箱子被重新拾取。这与恢复路径是同一契约语义的两条进入路径，不补上则验收项 2 在该
  场景不成立。恢复流程本身仍不重建地图（本条只影响"开局即落入活跃 run"的装配）。

#### 3. 测试（`Tests/EditMode/Net/RunResumeTests.cs`，新文件，11 用例）

| 用例 | 覆盖 |
| --- | --- |
| `Evaluate_ResumeRejected_TreatsRunAsInvalid` | resume 被拒 → RunInvalid（验收 3） |
| `Evaluate_Disconnected_DoesNotInvalidateRun` / `Evaluate_Timeout_DoesNotInvalidateRun` | 超时/断线不失效 |
| `Evaluate_SeedMatches_ReturnsApply` | 种子一致 → Apply |
| `Evaluate_SeedMismatch_TreatsRunAsInvalid` | 种子不一致 → 失效 |
| `Apply_ContentsNotReady_FillsContentsAndDropsAll` | 灌入 + 填满 → IsCompleted、清单一致 |
| `Apply_MultipleRecords_AppliesEachByPointId` | 多点按 PointId 各自应用、未开箱点不受影响 |
| `Apply_ContentsAlreadyReadyWithMatchingItems_Succeeds` | 已就绪一致不抛错、不重复应用 |
| `Apply_ContentsReadyButItemsMismatch_Throws` | 清单不一致抛错 |
| `Apply_RecordHasNoMatchingEntity_Throws` | 匹配不到实体抛错 |
| `Apply_DuplicateRecordForSamePoint_Throws` | 同点两条不一致记录抛错 |
| `TryGetLootedItems_FindsRecordByPointId` | 回放清单查找（开局幂等路径） |

- **摆位重建一致性未单测的理由**：摆位由同种子预测保证（ticket 02 种子向量测试已锁
  生成器确定性，ticket 06 的 `MapSpawnPlanTests` 已锁摆位出口），恢复流程不重建地图、
  不走重建摆位路径，"重建一致"在该设计下退化为"种子校验一致"，已由 `Evaluate_SeedMatches_*`
  与 `Evaluate_SeedMismatch_*` 覆盖。
- **NetComponent false→true 迁移与 ProcedureMain 触发接线无法静态测**：NetComponent 是
  Unity 组件（Awake 构造 WebSocketConnection、登录走真实 WS 连接与 GameEntry 启动链），
  ProcedureMain 的子状态机同样需要场景与 UGF 框架启动；EditMode 里无法真实驱动这两段。
  可测的决策/应用面（Evaluate / Apply / TryGetLootedItems）已全部测全。

#### 4. 待验证（主 agent / 人工）

1. **EditMode 回归**（主目录已开 Editor 经 mcp-for-unity）：重点
   `Tests/EditMode/Net/RunResumeTests.cs`（11 用例）+ 全量不回退（RunServiceTests 16 例等）。
2. **编译**：本票新增 `RunResumeEvaluator.cs` / `ResourcePointRecovery.cs` / `RunResumeTests.cs`
   无 `.meta`，主 agent 合并后让 Editor 导入生成并补提交。
3. **只能人工 Play Mode 验**：
   - 探索中断网 → 重连成功 → 箱子状态保持、继续游玩无感；
   - 探索中断网且服务器重启/TTL 过期 → 重连后提示"本局已失效"并回大厅，无新局被误建；
   - 探索中断网恢复失败（服务器不回包）→ 提示网络异常、留在原地、不再反复弹恢复请求；
   - 断线期间回大厅再开"新"局 → 落回旧局且箱子状态与消耗记录一致（幂等开局路径）。

#### 5. 偏差与取舍

1. **失效退场走"中止"而非"结算"**：resume 被拒 = 服务器无这局，走 `AbortInvalidatedRun`
   （提示 + 清单局状态 + 回大厅），不进 `MainSettlementState`。理由：结算会上报 settle
   并把未经验证的局内所得落库（服务器对账对未知 runId 直接跳过），把"服务器已不认账的局"
   变成一次有效结算，等于给 TTL/重启场景开了保留战利品的口子；契约 §9 的语义是
   "本局已失效并回大厅"。代价是玩家丢失本局局内所得（服务器不认这局的代价，与
   spec"崩溃丢局可接受"一致）。
2. **`MainMapBuildingState` 应用 Looted**（见 §2 偏差段）：超出字面票面、在契约授权范围内。
3. **恢复尝试一次**：超时/断线后不自动重试（按主 agent 指示保持简单）。玩家再次断线重连
   不会重新触发（`_resumeAttempted` 已置位）；后续交互失败照旧有各自的拒绝提示，游戏可继续。
4. **`Fsm.CurrentState` 判定探索中**：`IFsm<T>.CurrentState` 属性（dll 确认存在），比
   `MainCamera != null` / `Round.HasActiveRun` 更精确（见 §1）。
5. **难度来源用 `RoundSession.Difficulty`** 而非 `ProcedureMain.Difficulty`（后者来自存档
   loadout，两者同值但 Session 是本局事实）。

#### 6. 潜在编译风险点（静态自查）

1. **`IFsm<T>.CurrentState`**：`GameFramework.dll` 的 `IFsm<T>` 属性（xml doc + dll 符号确认），
   返回 `FsmState<ProcedureMain>`，`is MainExplorationBattleState` 模式匹配合法（同程序集）。
2. **`Entity` 类型歧义**：`ProcedureMain.cs` 同时 `using SepCore.Entity`（纯命名空间）与
   `using UnityGameFramework.Runtime`；`ApplyConsumedSnapshot` 里已写全限定
   `UnityGameFramework.Runtime.Entity` 规避（`SepCore.Entity` 命名空间内无 `Entity` 类型，
   但全限定更明确）。
3. **`GameFrameworkLog` 未设 helper 时 Log 会抛/静默**：`RunResumeEvaluator` 的种子不一致
   路径走 `Log.Error`（全限定 `UnityGameFramework.Runtime.Log`），测试 fixture 的
   `OneTimeSetUp` 已按 `RunServiceTests` 同款注册 `DefaultLogHelper`。
4. **`ResourcePointRecovery` 在 `SepCore.Entity`（Runtime 程序集）引用 `SepCore.Net` 的
   `NetLootRecord`**：Net 域同在 Runtime 程序集（`Runtime/CustomComponent/Net/`），同程序集
   无引用问题；`MainMapBuildingState`（Procedure 程序集）引用它，Procedure asmdef 引用
   Runtime GUID（既有）。
5. **`ResourcePointData.ApplyItems(null)` 会抛 `ArgumentNullException`**：`TryGetLootedItems`
   已把缺失字段归一为空 `List<int>`；`NetLootRecord.itemIds` 字段有初始化器，
   JsonUtility 缺失字段时保留空表，不会传 null。
6. **`Log.Warning` 4 参 / `Log.Error` 3 参重载**：`GameFramework/.../Utility/Log.cs` 有
   `Warning<T1..T4>` / `Error<T1,T2,T3>`，实参（Vector2×4 / long×2+string / Exception）均可。
7. **`UniTask`（非泛型）作为 `ResumeRunAsync` 返回 + `.Forget()`**：与既有
   `RequestLootAsync`（`UniTaskVoid`）略不同——这里需要 `try/finally` 故用 `UniTask` +
   `Forget()`，UniTask 提供 `Forget()` 扩展（`Cysharp.Threading.Tasks` 已 using）。
8. **`NetworkNotice.Show(string)`** 与 `ShowInteractionRejected()` 均 static（`SepCore.Exploration`），
   Procedure 程序集引用 Runtime 合法（既有方向）。
9. **测试文件 `Assert.That(..., out var)`**：`TryGetLootedItems` 的 out 参数在 NUnit 断言内联
   `out List<int> items`（C# 7 out-var，Unity 2022.3 支持）；`out _` 丢弃同样合法。
10. **`itemIds.ToList()`**：测试里 `params int[]` → `List<int>`（`System.Linq` 已 using）。

#### 7. 触发时机设计说明（应主 agent 报告要求）

- **迁移源**：NetComponent 两条登录成功路径（token 静默续登、账密登录）在 `OnlineReady`
  置 true 后同步调用 `OnBackOnline()`。自动重连循环（`Update` → `TryLoginAsync`）与手动
  账密登录都会汇入这两条路径，覆盖"断线自动重连"与"登录界面手动重登"两种重上线方式。
- **一次性**：`_backOnlinePending` 标记 + `TryConsumeBackOnline()` 取走即清；事件
  `BackOnline` 供长期订阅方（当前无）。
- **消费点**：`ProcedureMain.OnUpdate`（ProcedureMain 是当前流程时每帧调用）。在
  `ProcedureMain.OnEnter` 丢弃遗留标记——断线期间在大厅重上的场景由下一局 start_run 的
  服务器幂等承接（返回活跃 run 现状 + 地图构建应用 Looted，见 §2），不走恢复。
- **时序竞态**：重连期间（登录中）`OnlineReady` 为 false，轮询直接跳过；登录完成的同帧
  `TryConsumeBackOnline` 才取走标记。恢复请求自身 3s 超时（RunService 默认阈值）。
