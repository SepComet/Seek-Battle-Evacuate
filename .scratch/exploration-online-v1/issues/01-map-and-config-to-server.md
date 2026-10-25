# 01: 地图与配置数据上服务器

**What to build:** 编辑器菜单把 MapDefinition 地图资产序列化为 JSON 进 DataTables，sync_config 增补该 JSON 与资源点、敌队伍两张配置表，服务器启动即可加载生成所需的全套数据。导出时校验硬性不变量：每个敌人点必须归属某个房间，违反直接报错。服务器加载沿用"直接读 gen_cli 导出 JSON"的既定决策。

**Blocked by:** None (can start immediately)

**Status:** ready-for-agent

- [ ] 导出产出的 JSON 覆盖房间矩形、物资点/敌人点/出生与撤离候选列表
- [ ] sync_config 同步后服务器启动加载成功，加载用例跑真实导出 JSON
- [ ] 资源点配置（含掉落权重）与敌队伍配置（含威胁池）在服务器可查询
- [ ] 敌人点不在任何房间内时，导出侧报错拒绝产出

> 四条均已在代码与 Go 测试层面就绪；勾选留待主 agent 跑完编辑器重导 + EditMode 后回填（见 Comments「待主 agent 验证」）。

## Comments

### 实施记录（2026-10-25，接手半成品后补齐）

**客户端（导出侧）**

- `Scripts/Runtime/CustomComponent/Exploration/MapBuilder/MapDefinitionJson.cs`（`SepCore.Exploration`，Runtime 程序集）：`Serialize` 纯函数序列化器，房间归一化为 min/max、坐标整数值输出为整数，`Validate` 校验房间/四类点位非空 + 每个敌人点必须归属房间（`RoomIndexAt` 用 `x >= minX && x < maxX+1` 语义，与 `RoomDefinition.Contains` 逐字一致），违反抛 `InvalidOperationException`。
- `Scripts/Editor/MapDefinitionExporter.cs`（`SepCore.Editor`）：菜单 `Utility/Map/Export Map Definition JSON`，读 `Assets/GameMain/MapDefinition.asset` → 序列化 → 写 `DataTables/tbmapdefinition.json`（UTF-8 无 BOM、LF）。校验失败只报错不写文件，既有文件保持不变。输出路径改由 `Application.dataPath` 推工程根（不再依赖 `Directory.GetCurrentDirectory()`）。
- `DataTables/tbmapdefinition.json`（不入 Luban 注册，走一致性预检白名单）：9 房间 / 7 物资点 / 5 敌点 / 3 出生 / 2 撤离。已用脚本逐字段比对 `MapDefinition.asset` 的 YAML（含 pointA/pointB 反序归一化），完全一致。
- `Scripts/Tests/EditMode/Exploration/MapDefinitionJsonTests.cs`：覆盖反序房间归一化、敌人点越界拒绝、Max 瓦片边界（`Max+1` 半开）、四类点位空列表拒绝、空房间拒绝、`RoomIndexAt` 首命中。测试用反射灌私有序列化字段，先例见 `MapBuilderTests`。

**服务器（加载侧）**

- `internal/mapdef`：`Load(path)` → `Layout`；`Room.Contains` 逐字实现契约 §8 语义；`Validate` 校验房间边界、四类点位非空、`resourcePointId > 0`、敌人点必须归属房间（违反返回 error）。`RoomIndexAt` 重叠时取列表序最小。
- `internal/gametable`：`LoadResourcePoints` → `id → LootConfigs[{difficulty,min~max,五档权重}]`（`LootFor(difficulty)` 缺失返回 ok=false，调用方报错）；`LoadEnemyParties` → `id → {EnemyIDs, Threat}` 并按威胁建有序池（`Pool(threat)` 桶序 = 配置表序，生成器取模依赖它）。校验含 id 非法/重复、权重全零、数量区间、难度重复等。
- `internal/itemtable`：新增 `LoadCatalog`，在物品表之上建稀有度分桶（`RarityPool`，桶序 = JSON 数组序）；`Load` 保留为不建桶的旧入口（现有调用零改动）。`core.ItemDef` 补 `Rarity`，`core` 增 `RarityWhite..Red` 常量与 `RarityOrder`（与客户端 `RarityOrder` 同序）。
- `cmd/sbe-server/config.go`（新增）：把六份配置的加载与跨表校验收进 `loadConfigs`，`main` 只负责旗标与装配，任一步失败 `log.Fatalf`。新增跨表校验：地图里每个 `resourcePointId` 必须在资源点表存在（否则生成期必炸，提前到启动期暴露）。
- `cmd/sbe-server/main.go`：新增 `-map / -resourcepoints / -enemyparties` 旗标（默认指向 `config/` 下同名文件），启动日志补布局与两张表摘要。
- `tools/sync_config.sh|.bat`：增补 `tbresourcepointconfig.json`、`tbenemypartyconfig.json`、`tbmapdefinition.json` 三份复制。
- `config/` 下三份 JSON 已提交（Dockerfile 整目录 COPY）。
- `tools/sbe-dev-mcp/check.go`：新增 `nonLubanStems = {"tbmapdefinition"}` 白名单并注释原因，`DataTables/tbmapdefinition.json` 不再被判为"无对应注册表"。

**测试（全部读真实导出产物，非内联字符串）**

- `cmd/sbe-server/config_test.go`（新增）：
  - `TestLoadConfigsFromRealExports`：用 `config/` 下六份真实文件跑完启动加载链路，断言物品全五档稀有度桶非空、难度集合非空、地图五类数据齐全、每个地图物资点的每种难度都有掉落配置、威胁 1~3 队伍池非空。
  - `TestConfigSyncedFromClientDataTables`：`config/` 与客户端 `DataTables/` 三份探索配置逐字节一致（换行归一化后），漏跑 `sync_config` 时先红。
  - `TestLoadConfigsRejectsUnknownResourcePointID` / `TestLoadConfigsRejectsEnemyPointOutsideRooms`：加载期拒绝路径。
- `internal/mapdef/mapdef_test.go`：真实布局的房间/物资点/敌人归属逐项断言（9/7/5/3/2，敌人点归属房间 `[0,0,1,4,4]`）+ 瓦片区间语义表驱动 + 敌人点越界/各类非法布局拒绝。
- `internal/gametable/gametable_test.go`：真实表按难度/威胁可查（资源点 1 难度 1 权重 70/25/5/0/0、数量 1~2，难度 3 红权重 10；威胁池 `1:[4001,4002] 2:[4101,4102] 3:[4201,4202,4203]`，threat 0 空池）+ 非法表拒绝表驱动。
- `internal/itemtable/itemtable_test.go`：稀有度分桶顺序、真实表五档全覆盖（83 条）、非法 rarity 拒绝。

**验证结果**

```
$ cd SBE-Server && go vet ./...        # 通过
$ go test -count=1 ./...
ok  sbe-server/cmd/sbe-server 0.912s
ok  sbe-server/internal/core 0.412s
ok  sbe-server/internal/gametable 0.572s
ok  sbe-server/internal/globaltable 0.503s
ok  sbe-server/internal/itemtable 0.529s
ok  sbe-server/internal/mapdef 0.532s
ok  sbe-server/internal/memorystore 0.644s
ok  sbe-server/internal/mysqlstore 1.013s
ok  sbe-server/internal/server 1.666s
ok  sbe-server/internal/snowflake 0.416s
ok  sbe-server/internal/store 0.569s
ok  sbe-server/internal/ws 1.291s
```

`tools/sbe-dev-mcp` 经 `go build`/`go vet` 通过；用 stdio 实跑 `consistency_check`：**3 组检查 0 违规**（10 个既有 asmdef 警告与本次改动无关，为未安装包的内置模块引用）。

**修复的半成品缺陷**

- `tools/sbe-dev-mcp/check.go` 引用了 `nonLubanStems` 但从未声明 —— 该模块编译不过（`go vet` 报 `undefined: nonLubanStems`）。已补声明（放在组 1 注释区，含白名单原因）。
- `main.go` 原先在 `main()` 里平铺加载六份配置，无法被测试覆盖；已抽到 `loadConfigs`，启动级加载用例因此可测。

**待主 agent 验证（本 worktree 内禁止启动 Unity / 编译 C#）**

1. 编辑器菜单 `Utility/Map/Export Map Definition JSON` 重导一次，确认产物与已提交的 `tbmapdefinition.json` 一致（本次提交的是实施者按 `Serialize` 规则手工核对的产物，未经编辑器实跑）。
2. EditMode 跑 `MapDefinitionJsonTests`（新文件无 `.meta`，需 Editor 先导入生成，再由主目录补提交）。
3. 顺带确认 `SepCore.Editor` 编译通过（已静态核对 asmdef：`SepCore.Editor` 引用 `SepCore.Base`/`SepCore.Runtime`/`SepCore.Presentation` 的 GUID 均在，`MapDefinitionJson` 只依赖 `System.*` 与 `UnityEngine`，无 `UnityEditor` 引用）。

**已知偏差 / 决策**

- 地图 JSON 的坐标格式化用 `"0.###"`（整数输出为整数），契约只要求坐标一致、未规定格式；最大三位小数足以覆盖瓦片坐标。
- 服务器加载期多了一条契约未要求的跨表校验（`resourcePointId` 必须在资源点表存在）。属契约"加载失败即 Fatal"的延展，不放宽任何既有约束；若认为超出票范围可单独撤掉。
- `itemtable.Load` 保留双入口（`Load` / `LoadCatalog`）而非直接改签名，避免动既有调用点；`core.ItemDef` 加 `Rarity` 字段对所有使用处是键值字面量，无需改。
- 未提交任何构建产物（`tools/sbe-dev-mcp/sbe-dev-mcp.exe`、`bin/` 均未出现）；新 `.cs` 未手写 `.meta`，交给 Editor 导入。
