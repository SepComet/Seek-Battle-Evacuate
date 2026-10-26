# 03: Go 生成器与双端交叉验证

**What to build:** Go 侧实现与 C# 同构的生成算法（消费 01 的配置加载器），含房间归属不变量校验（生成时敌人点必须归属房间，违反报错）。测试断言与 02 定义的种子向量逐位一致——这是双端同构的回归锚，向量常量两侧逐字相同。

**Blocked by:** 01, 02

**Status:** resolved

- [x] go test 全绿，含种子向量交叉用例；向量常量与 C# 侧逐字一致
- [x] 生成器为纯函数（配置进、结果出），可被会话层直接复用
- [x] 敌人点无法归属房间时生成报错，不做静默处理

## Comments

**状态：已实现，Go 侧全绿；C# 侧 EditMode 待主 agent 在已开 Editor 中跑。**

### 交付物

- `SBE-Server/internal/mapgen/`（新包）
  - `splitmix.go`：SplitMix64 / `Hash64` / `NextInt` / `SubSeed` 与 `Kind*` 常量，逐字按契约 §1/§2。
    `NextInt` 的跨度按 `int64` 计算（入参可能跨 int32 边界，见下「偏差」）。
  - `mapgen.go`：`Generate(master int64, difficulty int, layout *mapdef.Layout, tables *gametable.Tables) (*Content, error)`。
    纯函数：不碰 IO、不持状态、不修改入参（有 `TestGenerateIsPure` 钉住）。
  - 出参 `Content`：`PlayerSpawnPoint`、`ExtractionPoint`、`ResourcePoints[i]{PointID,Index,Position,ResourcePointID,Present,ItemIDs}`、
    `EnemyPoints[i]{PointID,Index,Position,RoomIndex,Present,Threat,EnemyPartyID}`；两类列表与地图定义**同序同长**
    （不存在的敌点也在列表内 `Present=false`，与 C# `MapPlacement` 形状一致）。
    点位 ID 由导出的 `ResourcePointID(i)` / `EnemyPointID(i)` 构造（`r<i>` / `e<i>`，契约 §6）。
  - 消费序严格照 §3：物资点 `数量 →（稀有度 → 桶内下标）× 数量`；敌点 `威胁 →（非 None 时）队伍池下标`，
    None 时不再消费；撤离/出生各一次下标随机。
  - 错误路径全部返回 error：nil 入参、缺难度权重、缺物资点配置、缺该难度掉落配置、数量区间非法、
    稀有度/威胁权重全零、稀有度桶空、队伍池空、出生/撤离候选为空、敌点不归属任何房间。
- 配置面（最小改动，既有公开行为不变）
  - `gametable/difficulty.go`：`LoadDifficulties` 读 `tbdifficultyconfig.json` 的 `enemyParties`，
    校验 tier 合法/不重复、威胁 ∈ [0,3]、权重非负且和不小于 1；`DifficultyTable.Weights(tier)`。
  - `gametable/tables.go`：`Tables` 聚合（Resources/Parties/Difficulties/Items）与 `NewResourceTable` /
    `NewEnemyPartyTable`（后者按威胁建池，池序 = 入参序）；`gametable.ItemCatalog` 接口（`Get` + `RarityPool`）。
  - `itemtable`：抽出 `NewCatalog([]CatalogEntry)`，`LoadCatalog` 改走同一校验路径（原有 JSON 加载行为不变）。
  - `cmd/sbe-server`：`loadConfigs` 增加难度权重加载，`configs.Tables()` 装配生成器入参；
    启动日志补 `difficulties=N`。

### 测试与验证

```
$ cd SBE-Server && "/c/Program Files/Go/bin/go.exe" vet ./...      # 无输出（通过）
$ "/c/Program Files/Go/bin/go.exe" test -count=1 ./...
ok  sbe-server/cmd/sbe-server 0.973s        ok  sbe-server/internal/mapdef 0.540s
ok  sbe-server/internal/core 0.453s         ok  sbe-server/internal/mapgen 0.531s
ok  sbe-server/internal/gametable 0.669s    ok  sbe-server/internal/memorystore 0.669s
ok  sbe-server/internal/globaltable 0.583s  ok  sbe-server/internal/mysqlstore 0.740s
ok  sbe-server/internal/itemtable 0.612s    ok  sbe-server/internal/server 1.735s
ok  sbe-server/internal/snowflake 0.447s    ok  sbe-server/internal/store 0.641s
ok  sbe-server/internal/ws 1.310s           （另有 4 个包 no test files）
```

`internal/mapgen/vectors_test.go`（向量常量逐字来自 `notes/vectors.md`，用内联配置对象，不读 `config/`）：

| 用例 | 断言组数 |
| --- | --- |
| `TestSplitMix64KnownValues` | 3 个种子 / 10 个 64 位已知值 |
| `TestHash64KnownValues` | 3 |
| `TestNextIntKnownValues` | 7 个小区间 + 3 个跨 int32 宽区间 |
| `TestSubSeedKnownValues` | 9 |
| `TestSeedVectorsFullBuild` | 4 组完整生成（A/B/C/D）逐行 |
| `TestSeedVectorsPlacementIsFullProjection` | 4 组摆位 = 完整文本投影，且 fixture 两侧常量互钉 |
| `TestSubSeedIsolation_...` | A vs D：R 行变、E 行与 spawn/extraction 逐字同 |
| `TestDifferentMasterSeed_ProducesDifferentMap` | vectors.md §6 的 masterSeed+3 排查向量 |

`internal/mapgen/mapgen_test.go`：真实配置冒烟（6 份真实配置 × 难度 1~3 × masterSeed 1/123456789/2147483647，
只断言一致性不变量：点位与布局对齐、物品在物品表且同点不重复、队伍 id 在队伍表且威胁匹配、敌点 `RoomIndex` 有效且
`Room.Contains`、出生/撤离取自候选）+ 纯函数性 + 10 条错误路径子用例 + 点位 ID 构造。

**TDD 记录**：先写两个测试文件并确认红灯（`undefined: gametable.ThreatWeight` → `undefined: Content`），
再写 `splitmix.go`/`mapgen.go` 转绿。为确认向量测试真有约束力，另做一次变异验证
（把 `KindResource` 由 1 改为 9）：A/B/C/D 四组向量与隔离用例全部转红，还原后复绿。

### 与 C# 实现对齐情况

逐条核对 C# 侧（`MapBuilder.cs` / `PointGenerator/*` / `Random/*`）与契约，**未发现 C# 与契约冲突**，
Go 侧按 C# 的实际行为逐位实现，要点：

- 威胁掷点是敌点摆位的唯一实现（C# `RollPlacement`）；Go 同样只在 `generateEnemyPoints` 里掷一次，
  存在性 = `threat != 0`，None 时不消费队伍池随机。
- 物资点 `Present` 恒 true，完整生成的 `Position`/`ResourcePointID` 直接来自地图定义（C# 同一来源）。
- 稀有度顺序固定 White→Green→Blue→Gold→Red（`core.RarityOrder`），桶内顺序 = 物品表顺序，桶为每点独立副本。
- 出生/撤离同一条 `placePoint`（子种子 index 固定 0，只消费一次 `NextInt`）。
- C# 的 `MapBuildResult.EnemyPoints` 只含存在的点、`MapPlacement.EnemyPoints` 全量；Go 的 `Content.EnemyPoints`
  取**全量**形态（服务器需要按 `e<i>` 判断"该点本局是否存在"，契约 §6 的 err 分支依赖它）。
  这不算第二套逻辑：全量 + `Present` 可通过一次过滤得到 C# 的 Build 形态。

### 偏差与决策

1. **`NextInt` 跨度按 int64 算**：契约 §1 的 Go 代码写 `uint64(maxExclusive - min)`，而 vectors.md §0 要求
   跨越 int32 的区间（`int.MinValue..int.MaxValue`）也要双端一致（C# 侧 `SplitMix64.NextInt` 用 `long` 算跨度）。
   Go 的 `int` 是 64 位，直接 `maxExclusive - min` 不会溢出，但为与 C# 语义显式对齐，写成
   `uint64(int64(maxExclusive) - int64(min))`，并用三条宽区间已知值钉住。契约 §1 的代码注释本身只在
   "Go 的 int 是 64 位"的语境下成立；本实现不改变数值结果。
2. **难度威胁权重单独加载**：`globaltable.Load` 只取 `tier` 集合（既有校验用），生成需要 `enemyParties` 权重，
   故在 `gametable` 另加 `LoadDifficulties` 读同一个 `tbdifficultyconfig.json`。既有 `globaltable` 行为未动。
3. **`gametable.Tables` 用接口引用物品目录**（`ItemCatalog`），避免 `gametable` 依赖 `itemtable`
   （两者原本互不依赖，保持同层）。`itemtable.Catalog` 隐式满足该接口，装配点在 `cmd/sbe-server`。
4. **生成器自建第二次房间校验**：`Generate` 入口调 `validateEnemyRooms`，不依赖调用方是否走过 `mapdef.Load`
   （契约 §8「两处校验」；生成器是可被直接调用的纯函数）。同时要求 `len(Rooms) > 0`。
5. **`Content` 不携带 `Rooms`**：服务器侧无需房间数据（摆位下发不含房间）；C# 的 `MapBuildResult.Rooms`
   是客户端渲染需要。若后续要做服务器侧房间归属校验再做增量。
6. **物品目录的稀有度合法性前置到加载期**：`itemtable` 本来就在加载时拒绝 [1,5] 之外的值，
   因此 Go 侧"物品表里出现非法稀有度"不是生成期错误路径（C# 侧 `BuildItemPools` 才检查，
   因为 C# 拿到的是 Luban 表对象）。这是加载分层差异，不改变任何合法配置下的输出。
7. **新增 `gametable.NewResourceTable` / `NewEnemyPartyTable` 不做校验**（重复 id 后者覆盖），
   文档注释已写明"生产路径必须走 `Load*`"；它们的存在只为内联配置测试与免 IO 装配。

### 待验证项（需主 agent / 用户）

1. C# EditMode：`MapBuilderTests` 全量（本票未改 C#，但这批向量常量在 Go 侧首次被实跑，
   建议与 C# 侧结果一并确认两端文本逐字一致）。
2. `internal/mapgen/mapgen_test.go` 的真实配置冒烟目前覆盖难度 1~3；若 `tbdifficultyconfig` 新增档位，
   该用例需要同步扩展（当前硬编码 1..3，与 `gametable_test.go` 既有写法一致）。

### 结票记录（2026-10-26，主 agent）

Status 置 resolved：实现已合并进 `feature/exploration-online-v1`，双轴代码审查通过（结论见 spec Comments），
agent 可验的验收项已勾选；标注"待人工"的项统一归 **ticket 08**（Play Mode / 联调 / 网络劣化演练）。

- Go 侧证据：`go test -count=1 ./...` 全绿；真实容器 deploy+smoke 绿，wsprobe 实测协议 v4 往返。
- C# 侧证据：mcp-for-unity 全量 EditMode 375 条，除存量 `FogTilemapControllerTests`（Tilemap 无 Grid）外全绿。
- 双端种子向量逐字一致（notes/vectors.md，三方复算）。
