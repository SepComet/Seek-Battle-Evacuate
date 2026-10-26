# 06: 客户端联网装配与交互链路

**What to build:** 地图构建流程接入 start_run：本局种子、出生/撤离点取服务器显式值，敌人摆位与箱子存在性走本地预测（生成器摆位出口），雾保持纯演出。开箱走 loot、撞敌走 encounter（响应携带队伍数据后再开战）；交互超时按拒绝处理并提示，移动与渲染永不被网络阻塞。大厅战备的种子输入退役；场景调试 fallback 保留本地生成。

**Blocked by:** 02, 04, 05

**Status:** resolved

- [ ] 联网正常流程整局可玩（对容器服务器联调验证）
- [ ] 开箱所得与服务器记录一致；撞敌开战的队伍构成来自服务器响应
- [ ] 超时/断线时交互拒绝有提示，移动不受影响
- [x] EditMode 全量回归绿
- [ ] 场景调试路径（无服务器）可用本地种子生成

## Comments

### 实施记录（2026-10-25）

**状态：C# 侧已实现并静态自查，EditMode 与 Play Mode 待人工/主 agent 执行回填。**
分支 `ticket/06-client-wiring`，6 个实现 commit + 1 个加固 commit。未跑 Unity / batchmode / dotnet build（纪律）。

#### 1. 地图构建接入 start_run（验收项 1、5）

- `MainMapBuildingState` 重写为单一路径 + 两种种子来源：
  - **在线**（`Net != null && OnlineReady`）：`Run.StartRunAsync(difficulty, resume:false)` →
    `GameEntry.Random.BeginRun((int)seed)` + `Round.BeginRun(difficulty, seed)`（同一 seed，契约 §5）→
    `BuildPlacementAsync()` 取敌点/物资点预测摆位 → 出生/撤离点取服务器显式值。
  - **离线**（`Net == null` 或 `!OnlineReady`，场景调试）：本地时间戳种子 `(int)(Ticks & 0x7FFFFFFF)` +
    `BuildAsync()` 完整本地生成（保留原路径）。种子同样播给 `Random` 与 `Round`。
- **新增 `MapSpawnPlan`**（`Runtime/.../MapBuilder/MapSpawnPlan.cs`，纯数据）：把两条来源归一成
  同一份 `ResourcePointSpawnSpec` / `EnemySpawnSpec` 清单，实体实例化只有一条代码路径。
  `FromBuildResult`（离线，清单+队伍齐全）/ `FromPlacement`（在线，清单空、队伍用本地占位）。
- **失败不进半成品局**：start_run 超时/拒绝/应答缺损 → `Log.Error` + `NetworkNotice.ShowInteractionRejected()`
  + `AbortToMenu`（清 `Round`/`TurnBattle`/`Random` 并 `ReturnToMenu()`）。构建中途抛异常（原代码只记日志、
  停在空场景）现在也走同一路径。
- 雾 `InitializeRoomOverlays` 位置不变、纯演出，不等网络。
- **settle 用服务器 runId**：联网路径 `Net.Run.RunId`（离线仍 `Guid.NewGuid()`）；
  结算后 `Net.Run.ResetRun()` 清缓存，避免下一局未 start_run 时误用上局 runId。

#### 2. 开箱走 loot（验收项 2、3）

- `ResourcePointData` 语义调整为"内容可待定"：
  - 新增 `PointId`（"r&lt;i&gt;"）与 `ContentsReady`；
  - `IsCompleted => ContentsReady && dropped >= count` —— **内容未到货时箱子仍可交互**，
    否则玩家永远发不出第一次 loot 请求（这正是"空 itemIds ⇒ 立即 IsCompleted"的陷阱）；
  - 新增 `CreatePendingContents(...)` 工厂与 `ApplyItems(IEnumerable<int>)`（一次灌入，重复调用抛
    `InvalidOperationException`，不静默覆盖已掉一半的进度）；
  - 公开构造签名只在末尾追加可选 `pointId`，既有命名参数调用零改动。
- `ResourcePointLogic`：长按开始（`OnInteractStart`）时若内容未就绪 → 发 `LootAsync(pointId)`，
  **本帧 return，不锁移动、不推进进度**；`OnInteractHold` 在等待期直接 return（不发重复请求）。
  成功 → `ApplyItems` + `InitializeDropThresholds()` 重算 `_totalSearchTime`/阈值，玩家仍按着则
  下一次 Hold 补 `BeginSearching`（此时才锁移动、显示进度）并照旧逐稀有度阈值 `DropItem`。
  失败/超时/拒绝 → `OnInteractEnd`（恢复移动）+ `NetworkNotice` 提示，箱子保持可再交互。
- 响应回来时校验 `Available && ReferenceEquals(_data, data)`：实体池化复用后不会把 A 箱清单灌进 B 箱。
- **提示**：新增 `NetworkNotice`（`Runtime/.../Exploration/Interact/`）：`GameEntry.UI.OpenDialog` +
  `DialogParams{Mode=1, PauseGame=false}`，全局 3s 节流（贴脸反复长按不叠一屏对话框），文案取
  `RunService.RejectionMessage`。未新增任何需要 prefab 的 UI。

#### 3. 撞敌走 encounter（验收项 2、3）

- `EnemyPartyLogic.HandleCollision` 保持同步，网络分支改 fire-and-forget：
  `RequestEncounterAsync(...).Forget()`，碰撞主循环永不 await。
- **新增 `EncounterRequestGate`**（纯逻辑，可 EditMode 直测）：`InFlight` 去重 + 拒绝后
  `RejectionCooldownSeconds = 1f` 退避；`Tick` 由 `OnUpdate(realElapseSeconds)` 推进（脱离接触后冷却也走完）。
- 在线成功 → 用响应 `enemyPartyId` 构造 `BattleEncounter` 开战；`threat` 与预测不一致只 `Log.Warning`
  （本地 threat 只用于 prefab 选取与警惕参数）。
- 在线失败/超时 → 不开战 + 冷却 + 提示。`TryStartBattle` 被拒时也进冷却（否则下一物理帧立刻重发）。
- 离线（场景调试）→ 沿用 `_data.EnemyPartyId` 直接开战，与原行为一致。
- 额外防护：响应回来时校验 `ReferenceEquals(_data, data)`（池化复用不串点）与
  `IsPlayerStillNearby()`（2.5 世界单位；等待期间已跑远则作废本次遭遇，避免"逃远了还被远程拉进战斗"）。

#### 4. 种子输入退役（验收项 4）

- `DeploymentForm.Logic.cs`：删掉 `seedInput.text` 读写、`OnRandomizeButtonClick`、
  `GameEntry.Random.BeginRun(seed)`。**prefab 上 `seedInput`/`randomizeButton` 控件保留**（未改 prefab）。
- 播种统一由 `MainMapBuildingState` 在拿到服务器种子（或离线时间戳）后执行。

#### 5. 结算 runId 与战斗所得（验收项 5）

- `MainSettlementState`：联网用 `Net.Run.RunId`；离线 `Guid.NewGuid()`（原语义）。
- **顺带修的账目缺口**：服务器 settle 对账额度 = 开箱所得 + 携带入场 + **战斗所得**，
  而客户端此前从不上报 `battleLoot`。一旦结算改用服务器 runId（对账从此生效），
  任何"带出敌人掉落"的局都会被 `rejected` 整批不落地。因此本轮补上：
  - `ItemEntityData` 增加 `isBattleLoot`（默认 false；敌人掉落置 true，箱子/丢弃来源为 false）；
  - `ItemEntityLogic` 拾取入背包时 `RoundSession.RecordBattleLoot(itemId, addedCount)`；
  - `RoundSession.BattleLoot`（按 itemId 汇总）+ `RecordBattleLoot`；
  - `WarehouseSyncService.SettleAsync(runId, record, battleLoot = null)` 带 `env.battleLoot`（空则不带上字段）。
- **偏差说明**：因本阶段服务器信任客户端上报（spec 既定边界），只记"已拾取进背包"的量，
  不记"掉在地上没捡"的量——漏报只会让额度更紧，绝不会放宽。

#### 6. 测试（EditMode，新增 5 个 / 扩充 7 个文件）

| 文件 | 覆盖 |
| --- | --- |
| `Exploration/PointIdsTests.cs`（新） | 点位 ID 格式 "r0"/"e2"；摆位与完整生成同下标同主键（验收(a)） |
| `Exploration/MapSpawnPlanTests.cs`（新） | 离线带全清单/队伍；联网清单空、出生/撤离取服务器值、Threat=None 不入列且下标不重排、无同威胁队伍直接抛错 |
| `Exploration/EncounterRequestGateTests.cs`（新） | 在途去重、拒绝冷却 ≥1s、Tick 夹取、成功无冷却、Reset、重复 Reject 重置冷却（验收(c)） |
| `Entity/ResourcePointDataTests.cs` | 新增待定内容可交互、`ApplyItems` 灌入后阈值/完成状态、空清单立即完成、防御拷贝、重复灌入抛错（验收(b)） |
| `Entity/EnemyPartyDataTests.cs` | pointId 存取、联网占位队伍 ID=0..（现为本地占位配置） |
| `Entity/ItemEntityDataTests.cs` | `isBattleLoot` 默认 false / 显式 true |
| `Exploration/MapBuilderTests.cs` | 新增断言：`Build` 与 `BuildPlacement` 的 Index/PointId 逐项一致 |
| `Net/RunServiceTests.cs` | 新增 `ResetRun_ClearsRunIdSeedAndConsumed` |
| `Net/WarehouseSyncServiceTests.cs` | 新增 settle 携带 / 省略 battleLoot |
| `Run/RoundSessionTests.cs` | 新增 `RecordBattleLoot` 汇总与非法入参 |

既有 51 个探索测试与 16 个 RunService 用例均未改语义；`ResourcePointData` 的两条既有断言
（空 itemIds ⇒ ItemCount 0 / IsCompleted true）仍成立，因为公开构造默认 `contentsReady: true`。

#### 7. 待验证（本 worktree 无 Editor，纪律禁止启动）

**必须由主 agent 在已开 Editor 里跑（经 mcp-for-unity）：**
1. EditMode 全量回归。新增/改动最快的一组：`PointIdsTests`、`MapSpawnPlanTests`、
   `EncounterRequestGateTests`、`ResourcePointDataTests`、`WarehouseSyncServiceTests`（settle 两条）。
2. 编译。本票改了 `ResourcePointData`/`EnemyPartyData`/`ItemEntityData` 三个 `SepCore.Base` 类型的
   构造签名（均为末尾可选参数），以及 `ResourcePointPlacementData`/`EnemyPointPlacementData`/
   `ResourcePointBuildData` 的 **internal** 构造（加了 `int index` 首参）——用 `InternalsVisibleTo("Tests")`
   的测试也同步改了。

**只能人工 Play Mode 验：**
3. 正常网络整局：进房雾散、敌人已在、箱子可开、开箱掉落与服务器一致、撞敌开战队伍来自响应。
4. 人为劣化（加延迟/断网）：开箱与撞敌出现"网络延迟较大，请检查网络连接"，**移动始终不卡**；
   网络恢复后可继续开同一箱、撞同一敌。
5. 断线重连续局属于 ticket 07（本票只保证 start_run(resume) 的装配入口可用）。

**已知未做**：`Report` 到 `ProcedureMain` 的 run 起点时间仍用客户端 `DateTimeOffset`（`RunStartTimeUtcMs`），
未改为服务器时间（协议未下发 startedAt，属 07/后续话题）。

#### 8. 潜在编译风险点（静态自查）

1. **`MapBuildResult` 系 internal 构造加了 `int index` 首参**：`Tests` 程序集靠
   `Runtime/AssemblyInfo.cs` 的 `InternalsVisibleTo("Tests")` 可见；若未来把测试程序集改名会断。
2. **`ResourcePointBuildData` 第四参是 `IList<int>`**，测试里传 `new[]{...}`（`int[]`，实现
   `IList<int>`）合法；生产代码传 `List<int>` 也合法。
3. **`EnemyPointPlacementData` / `ResourcePointPlacementData` 的 `PointId` 在构造里由 `PointIds` 生成**：
   `MapBuildResult.cs` 与 `PointIds.cs` 同在 `SepCore.Exploration` 命名空间、同一程序集，无可见性问题。
4. **`NetworkNotice` 用 `GameEntry.UI`（UGF 静态入口，`public static UIComponent UI`）+ `OpenDialog`
   扩展方法（`SepCore.UI.UIComponentExtension` 在 Runtime 程序集）**：`NetworkNotice` 也在 Runtime，
   方向合法（无 Runtime→Presentation 引用）。`GameEntry` 是 `SepCore.Runtime` 里的 `partial class`
   （`Runtime/Base/*.cs`），`SepCore.Exploration` 命名空间下的类引用**全局命名空间**的 `GameEntry` 无需 using。
5. **`EnemyPartyLogic` 的 `RequestEncounterAsync` 是 `async UniTaskVoid` + `.Forget()`**：与
   `PlayerCharacterLogic`/`NetComponent` 里的既有用法一致；`UniTaskVoid.Forget()` 在 UniTask 源码中。
6. **`DialogParams` 在 `SepCore.UI`**，`NetworkNotice` 已 `using SepCore.UI;`。
7. **`MainSettlementState` 的 `IReadOnlyList<ItemStack>` 需要 `System.Collections.Generic`**（已 using）与
   `SepCore.Definition`（`ItemStack` 所在，已 using）。
8. **`WarehouseSyncService` 新增私有 `ToNetStacks`**：`NetStack` 在 `SepCore.Net`（同文件命名空间），
   `ItemStack` 在 `SepCore.Definition`（已 using）。
9. **`RoundSession` 新增 `System.Collections.Generic.List`/`ItemStack`**：文件原本已 using
   `SepCore.Definition` 与 `System.Collections.Generic`。
10. **`EnemyPartyLogic` 引用 `EncounterRequestGate`（`SepCore.Exploration`）**：文件已
    `using SepCore.Exploration;`。`Cysharp.Threading.Tasks`（`UniTaskVoid`）已 using。
11. **`ResourcePointLogic` 引用 `LootResult`（`SepCore.Net`）与 `GameEntry.Net.Run`**：已
    `using SepCore.Net;`；`UniTask`/`UniTaskVoid` 用的是 `Cysharp.Threading.Tasks`。
12. **`EncounterRequestGate`/`NetworkNotice` 无 Unity 依赖之外的引用**：`NetworkNotice` 用
    `UnityEngine.Time.unscaledTime`、`Log`；`EncounterRequestGate` 纯 C#（无 UnityEngine using）。
13. **`Log.Info` 6 参重载**（`MainSettlementState` 一行 6 个占位符）：已核对
    `GameFramework/.../Utility/Log.cs` 有 `Info<T1..T6>` 与同族 `Warning`。
14. **`MainMapBuildingState` 的 `AbortToMenu` 是 `static`**：只用 `GameEntry.*` 与传入的 `fsm`，
    不触实例字段；`ReturnToMenu()` 是 `ProcedureMain` 的 public 方法，经 `fsm.Owner` 调用。
15. **新增 3 个 `.cs` 无 `.meta`**（`PointIds`/`MapSpawnPlan`/`EnemyPartyCatalog`/`NetworkNotice`/
    `EncounterRequestGate` 及 3 个测试文件）：需主 agent 合并后在主目录让 Editor 导入生成并补提交。
16. **`Zone`/`using` 未用到**：`MapSpawnPlan.cs` 用 `System.Collections.ObjectModel`（`ReadOnlyCollection`）、
    `System.Array.Empty<int>()` 全限定调用（该文件未 using System，故写全名）。

### 结票记录（2026-10-26，主 agent）

Status 置 resolved：实现已合并进 `feature/exploration-online-v1`，双轴代码审查通过（结论见 spec Comments），
agent 可验的验收项已勾选；标注"待人工"的项统一归 **ticket 08**（Play Mode / 联调 / 网络劣化演练）。

- Go 侧证据：`go test -count=1 ./...` 全绿；真实容器 deploy+smoke 绿，wsprobe 实测协议 v4 往返。
- C# 侧证据：mcp-for-unity 全量 EditMode 375 条，除存量 `FogTilemapControllerTests`（Tilemap 无 Grid）外全绿。
- 双端种子向量逐字一致（notes/vectors.md，三方复算）。
