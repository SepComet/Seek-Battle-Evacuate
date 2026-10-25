# 02: C# 生成器子种子化重构

**What to build:** 预备因子票：生成算法从单一顺序随机流改为子种子结构——每个随机消费点从 hash(masterSeed, kind, index) 派生独立子种子，PRNG 换为双端易精确实现的可移植算法。行为保持不变（同种子同图，既有流程与场景调试不受影响），并为客户端暴露摆位出口（敌人存在性与威胁等级、箱子存在性）。战斗侧随机源完全不动。

**Blocked by:** None (can start immediately)

**Status:** implemented-pending-unity-verify

- [x] EditMode 生成测试改为固定种子向量断言，全量回归绿（测试已写；EditMode 实跑待主 agent 经 mcp-for-unity 执行）
- [x] 同一 masterSeed 两次生成结果逐位一致；各子种子互不影响（改物资配置不影响敌人摆位）
- [x] 可移植 PRNG 有独立单测
- [x] 摆位出口可独立调用，输出与完整生成的对应 stage 一致

## Comments

### 交付内容（分支 `ticket/02-csharp-subseed`）

三个 commit，已 merge `feature/exploration-online-v1`（无冲突，两侧文件零重叠）：

- `90006d0 refactor(client): 探索生成器子种子化——SplitMix64/子种子派生与摆位出口`
- `4d6513a test(client): SplitMix64 与子种子派生的已知值单测`
- `6f7b885 test(client): MapBuilder 改种子向量断言——四组向量 + 子种子隔离 + 摆位一致性`

新增文件：
- `SBE-Client/Assets/GameMain/Scripts/Runtime/CustomComponent/Exploration/Random/SplitMix64.cs`
- `SBE-Client/Assets/GameMain/Scripts/Runtime/CustomComponent/Exploration/Random/ExplorationSeed.cs`
- `SBE-Client/Assets/GameMain/Scripts/Tests/EditMode/Exploration/SplitMix64Tests.cs`
- `SBE-Client/Assets/GameMain/Scripts/Tests/EditMode/Exploration/ExplorationSeedTests.cs`

修改文件：
- `Exploration/MapBuilder/MapBuilder.cs`（构造签名 `IRoundRandomSource` → `long masterSeed`；新增 `BuildPlacement`/`BuildPlacementAsync`）
- `Exploration/MapBuilder/MapBuildResult.cs`（新增 `ResourcePointPlacementData`/`EnemyPointPlacementData`/`MapPlacement`；`EnemyPointBuildData` 新增 `Index`）
- `Exploration/MapBuilder/PointGenerator/{Resource,Enemy,Extraction,PlayerSpawn}PointGenerator.cs`
- `Procedure/Main/MainMapBuildingState.cs`（`new MapBuilder(difficulty, GameEntry.Random.Seed)`）
- `Tests/EditMode/Exploration/MapBuilderTests.cs`

### 契约遵循要点

- §1 SplitMix64 逐字实现；`NextInt` 用取模不用拒绝采样/乘法高位。
- §2 `SubSeed = Hash64(Hash64(Hash64(master) ^ kind) ^ index)`，kind 常量 1/2/3/4。
- §3 消费序：物资点 `count → (rarity → bucketIndex) × count`；敌点 `threat →（非 None）partyIndex`；撤离/出生各一次下标随机。
- §4 摆位与完整生成**共用同一 stage 代码**，不是第二套逻辑：
  - `EnemyPointGenerator.RollPlacement` 是威胁掷点的唯一实现，`PlacePoint`（摆位）与 `BuildPoint`（完整）都走它；完整生成在同一次掷点后继续消费队伍池。
  - `ResourcePointGenerator.BuildPoint` 先调用 `PlacePoint` 取位置/存在性，再掷物品，杜绝位置来源分叉。
- 报错路径不做静态兜底（全零权重、空队伍池、空稀有度桶、未知稀有度、空候选列表均抛 `InvalidOperationException`）。

### 期望值复算（三条独立路径一致）

`notes/vectors.md` 的期望输出与 `MapBuilderTests.cs` 内嵌常量已逐字核对。复算用了三份彼此独立的实现：

1. `notes/vector_ref.py`（上一轮留下的 Python 参考脚本）；
2. 按契约 §1–§3 文本重写的第二份 Python；
3. **契约 §1/§2 里给定的权威 Go 代码**（`Hash64`/`NewRng`/`NextInt`/`SubSeed` 原样抄写 + §3 生成逻辑）。

三者输出与 C# 常量全等：向量 A/B/C/D 的 `ExpectedFull`（9/9/6/9 行）与 `ExpectedPlacement`（9/9/6/9 行），以及全部 9 组 `SubSeed` 已知值、SplitMix64 种子 0 的前 5 个输出、`Hash64(0/1/42)`、`NextInt` 已知值。

向量 D 的隔离断言成立：与 A 仅物资配置不同，R 行全不同、E 行 4 行逐字相同。

### 上一轮半成品的修正

审查发现并修掉的偏差（均已并入 commit）：

1. `SplitMix64` 私有方法原名 `Finalize`——与 `object.Finalize` 同名会触发 CS0465（尝试引入析构函数）。改名 `Mix`。
2. `NextInt` 原用 `(ulong)(maxExclusive - minInclusive)`，跨度超出 int 范围时 int 先溢出：改为按 `long` 计算跨度，与 Go 的 64 位 int 语义逐位一致（新增宽区间已知值单测）。
3. `MapBuildResult`/`MapPlacement` 构造函数对 `rooms` 用 `rooms != null ? ... : new List<...>()` 兜底，违反仓库"不做静态回退"规则：改为必填参数。
4. `EnemyPointGenerator` 的摆位与完整生成原为两条独立的威胁掷点代码（虽然结果相同），违反契约 §4"不允许第二套逻辑"：合并为 `RollPlacement`。
5. `MapBuilder.BuildPlacement` 原只能在测试里手工装配置调用，缺生产入口：新增 `BuildPlacementAsync()`（与 `BuildAsync` 对称地加载/卸载 MapDefinition）。
6. 摆位测试原只比对文本，未验证 `Present`/`Position`/`ThreatLevel` 字段级一致：新增 `AssertPlacementMatchesBuildStages` 结构级断言。
7. 新增 `BuildPlacement_ChangingOnlyResourceConfig_LeavesEnemyAndSpawnUntouched`，把子种子隔离也在摆位出口上钉一遍。
8. `Build_WithSameMasterSeed_IsBitIdentical` 补充摆位出口的重复调用一致性。

### 待验证（需人工 / 主 agent 执行）

- **C# 编译与 EditMode 实跑未执行**（worktree 内无法启动 Unity，按纪律禁止 batchmode/dotnet build）。请在主目录已开 Editor 内经 mcp-for-unity 跑 EditMode，重点看：
  - `SplitMix64Tests`（12 个用例）、`ExplorationSeedTests`（7 个用例）
  - `MapBuilderTests`（4 组向量 × 4 个参数化用例 + 12 个单点用例）
- 新增 `.cs` 未写 `.meta`（按契约 §11 交给 Unity 导入生成；合并后需在主目录让 Editor 刷新并补提交 4 个 `.meta` 与 `Random/` 文件夹的 `.meta`）。

### 已知偏差 / 边界

- `notes/vectors.md` 里"物资点 present 本阶段恒 true"的说明保留；向量文本中没有 `E<n> present=false party=` 的形态（不存在点不输出 party），与 C# `FormatFull` 的写法一致。
- `MapPlacement.EnemyPoints` 与地图定义**同序同长**（不存在的点也占位、`Present=false`），而 `MapBuildResult.EnemyPoints` 只含存在点——两种语义都显式写在 XML 注释里，测试分别断言。
- 战斗侧随机源（`RoundRandomSource`/`RandomComponent`/`IRoundRandomSource`）零改动。
- `MainMapBuildingState` 的 `new MapBuilder(difficulty, GameEntry.Random.Seed)` 只改了参数来源；联网改造（服务器下发种子 + 摆位预测装配）属 ticket 06，本票不做。
