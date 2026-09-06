# 回合制战斗开发 TODO

> 状态：进行中（M0 已完成，M1 起按本文的新架构收敛）
>
> 创建日期：2026-08-31
>
> 架构修订：2026-09-01
>
> 相关设计：`Docs/GameDesign/04_TurnBasedCombat.md`、`Docs/GameDesign/03_RunExploration.md`、`Docs/GameDesign/06_PrototypeScope.md`
>
> 目标：在 Main 场景地图上完成半透明多对多回合制战斗，由 `TurnBattleComponent` 统一承接战斗启动、运行和结果回写，并保持规则内核可独立测试。

## 一、完成纪律

- 里程碑是否完成只看标题中的 `[ ]`；第八节的 22 条设计验收不代替里程碑完成状态。
- 只有完成该里程碑列出的 Unity 编译、EditMode 测试和 Play Mode 可见验收，并记录证据后，才能改为 `[x]`。
- 每次只实现当前里程碑；后续能力不得以“预留接口”为理由提前进入当前阶段。
- 生成代码不能手改。Luban 结构或数据需要调整时，修改源表后运行 `数据表/gen_cli.sh`。
- 战斗内核不直接读写 `SaveData`，不直接查找或操作地图敌人物体、掉落物和局外结算。
- 静态检查、Unity 编译、EditMode 和 Play Mode 分别记录，不互相代替。

每个里程碑完成后按以下格式补证据：

```text
代码状态：未开始 / 进行中 / 已完成
Unity 编译：未验证 / 通过（日期、日志或截图）
EditMode：未验证 / 通过（用例数量、结果）
Play Mode：未验证 / 通过（操作步骤、可见结果、截图或录像）
提交或差异：commit / diff 路径
剩余问题：无 / 具体问题
```

## 二、总体进度

| 里程碑 | 可见结果 | 当前状态 |
| --- | --- | --- |
| M0 战斗壳层与单局基础状态 | Main 场景可打开和关闭半透明战斗壳层，地图更新暂停 | 已完成 |
| M1 架构收敛与 1v1 攻击闭环 | 玩家攻击、敌人反击并产生胜负 | 已完成 |
| M2 4v4 调度、先制与确定性随机 | 多人队伍按实时速度行动，普通和先制顺序正确 | 已完成 |
| M3 技能、目标、MP 与数值效果 | 攻击和技能可合法选目标并结算 HP/MP | 已完成 |
| M4 状态、眩晕与速度变化 | 眩晕跳过行动，速度变化立即刷新顺序 | 已完成 |
| M5 逃跑、结束矩阵与单局回写 | 四种结果正确，连续战斗继承 HP/MP | 已完成 |
| M6 真实探索遭遇与地图恢复 | 碰撞进入战斗，胜利、逃跑、掉落和保护闭环 | 进行中（碰撞进战/警惕/保护/胜利移除已实现，掉落与全灭结算未接） |
| M7 战斗 UI 与输入限制完成 | 1 至 4 人战斗信息和操作完整可用 | 进行中（主要界面与两步确认已实现，背包/保险箱显式门禁与全量验收待补） |
| M8 全量回归与原型完成 | 22 条设计验收和最低测试矩阵全部留证 | 未开始 |

### 验证记录

| 里程碑 | 代码 | Unity 编译 | EditMode | Play Mode | 证据 |
| --- | --- | --- | --- | --- | --- |
| M0 | 已完成 | 通过（2026-08-31，dotnet build SepCore.Runtime.csproj 0 错误；Unity 编辑器内 Play Mode 实测编译通过） | 不适用（M0 无 EditMode 用例） | 通过（2026-08-31，见 M0 证据） | 已记录（M0 小节） |
| M1 | 已完成 | 通过（2026-09-02，dotnet build SepCore.Runtime.csproj 0 错误 0 警告；Unity 编辑器内编译通过） | 通过（2026-09-02，25 个 BattleRuntime 纯逻辑用例全部通过） | 通过（2026-09-02，见 M1 证据） | 已记录（M1 小节） |
| M2 | 已完成 | 通过（2026-09-02，dotnet build SepCore.Runtime.csproj 0 错误 0 警告；Unity 编辑器内编译通过） | 通过（2026-09-02，35 个 BattleRuntime 纯逻辑用例全部通过） | 通过（2026-09-02，见 M2 证据） | 已记录（M2 小节） |
| M3 | 已完成 | 通过（2026-09-03，dotnet build SepCore.Runtime.csproj 0 错误 0 警告；Unity 编辑器内实测编译通过） | 通过（2026-09-03，BattleRuntime 纯逻辑用例 50 个全部通过；M3 新增用例 14 个：技能公式与 MP 扣除、治疗与钳制、SingleAlly 含自己、Self 空目标展开与错误目标拒绝、AllAllies 全体结算、AllEnemies 全体结算、MP 不足拒绝、阵亡/阵营/目标数量拒绝、敌人技能决策与全体技能无额外随机） | 通过（2026-09-03，见 M3 证据） | 已记录（M3 小节） |
| M4 | 已完成 | 通过（dotnet build SepCore.Runtime.csproj 与 Tests.csproj 0 错误 0 警告；Unity 编辑器内实测编译通过） | 通过（BattleRuntime 纯逻辑用例 60 个全部通过；M4 新增用例 10 个：眩晕本轮/下轮跳过、重复取较长、敌人晕玩家、减速即时重排、已行动加速不再动、下限钳制、先制 8 人栏、减速前后栏顺序） | 部分通过（见 M4 证据） | 待补充（见 M4 小节） |
| M5 | 已完成 | 通过（dotnet build SepCore.Runtime.csproj / Tests.csproj / SepCore.Presentation.csproj 0 错误 0 警告；Unity 编辑器内 Play Mode 实测编译通过） | 通过（BattleRuntime 纯逻辑用例 77 个全部通过；M5 新增用例 15 个） | 通过（见 M5 证据，7 项全部验证） | 已记录（M5 小节） |
| M6 | 进行中（2026-09-06 静态检查） | 未验证 | 未验证 | 未验证 | 未记录 |
| M7 | 进行中（2026-09-06 静态检查） | 未验证 | 未验证 | 未验证 | 未记录 |
| M8 | 未开始 | 未验证 | 未验证 | 未验证 | 未记录 |

## 三、已确认的范围与规则

| 项目 | 约定 |
| --- | --- |
| 战斗承载 | 不新增 Battle 场景；在 Main 场景地图上覆盖半透明顶级 `BattleForm` |
| 地图暂停 | 战斗期间暂停地图角色、敌人 AI、碰撞、搜索和单局计时，战斗 UI 继续响应 |
| 模块边界 | 外界通过 `TurnBattleComponent` 启动战斗、提交操作和接收结果；战斗内部不访问场景对象和存档 |
| 内部协作 | 调度、指令校验、效果、状态和敌人决策共享当前 `BattleRuntime`，不为内部函数调用建立请求/响应 DTO |
| 随机数 | 地图生成、敌人决策、逃跑和掉落依次消费同一个本局随机源，战斗不重新播种 |
| 眩晕 | `DurationRounds=N` 影响目标未来 N 次行动机会；`Stun(1)` 跳过下一次行动后失效 |
| 状态重复施加 | 同类状态不叠层，保留较长的剩余持续次数 |
| 友方目标 | `SingleAlly` 包含施法者；`Self` 只能选择施法者自己 |
| 阵亡目标 | 阵亡和已逃跑单位不是合法目标；首版没有战斗内复活 |
| 战斗表现 | 首版使用面板式即时结算，不做站位动画、攻击动画、VFX、Timeline 或音效同步 |
| 操作方式 | 首版使用鼠标点击行动和目标 |
| 行动顺序 UI | 显示当前行动者和本轮剩余行动者；速度变化后立即刷新，不预测下一轮 |

## 四、架构与数据所有权

### 4.1 唯一外部入口

`TurnBattleComponent` 是战斗模块的门面和生命周期所有者。外部模块不直接创建或持有 `BattleRuntime`、`BattleUnit`、调度器和效果结算器。

它负责：

- 持有单局临时玩家状态、计时、探索暂停和战斗占用。
- 使用 `BattleEncounter`、单局玩家状态、配置和本局随机源创建当前战斗。
- 接收 UI 的 `BattleCommand` 并同步推进战斗。
- 向 UI 提供当前只读视图和本次行动记录。
- 战斗结束时把玩家 HP/MP 写回单局临时状态。
- 完成单局状态回写后，通过开战方提供的一次性完成回调返回 `BattleResult`；探索层据此处理敌人实例、掉落、保护和单局失败。
- 关闭战斗时清理当前战斗数据并恢复允许恢复的地图更新。

对外最小调用面：

```csharp
bool TryStartBattle(BattleEncounter encounter, Action<BattleResult> onCompleted);
BattleStep SubmitCommand(BattleCommand command);
BattleViewState GetViewState();
bool IsBattleActive { get; }
```

调试入口可以不提供完成回调。具体 C# 返回形式可以随实现调整，但完成通知必须由 Component 统一发出，且不得把内部可变对象暴露给 UI 或探索层。

### 4.2 内部共享运行时

`TurnBattleComponent` 持有唯一的当前战斗：

```text
TurnBattleComponent
├─ 单局状态
│  ├─ RunPlayerState[]
│  ├─ RunElapsedMs
│  ├─ pause state
│  └─ IRunRandomSource
│
└─ BattleRuntime（仅战斗期间存在）
   ├─ BattleUnit[]
   ├─ RoundNumber
   ├─ CurrentActorUnitId
   ├─ ActedUnitIds
   ├─ IsPreemptive
   ├─ PendingEvents
   └─ BattleResult
```

内部规则可以由多个普通 C# 类协作完成，例如：

- `TurnScheduler`：选择下一行动者、开始下一轮。
- `CommandExecutor`：校验并执行玩家或敌人指令。
- `EffectResolver`：按顺序结算数值和状态效果。
- `EnemyDecision`：从当前可用行动和合法目标中选择指令。

这些名称不是必须提前建立的类型。只有单个类已经承担独立规则且需要独立测试时才提取；简单逻辑保留在 `BattleRuntime` 内。

内部协作规则：

- 所有协作者操作同一个 `BattleRuntime`，不复制单位、轮次和状态集合。
- 一次 `StartBattle` 或 `SubmitCommand` 在主线程同步完成状态推进，不能留下多个并行写入者。
- `BattleRuntime` 是当前战斗事实的唯一来源；UI 视图和行动记录从它生成，不反向驱动规则。
- 战斗内部不使用全局事件总线传递调度、效果和结束状态。
- 内部类型优先使用 `internal`；只有外部真实消费的契约才使用 `public`。

### 4.3 必要的外部数据

| 数据 | 方向 | 作用 |
| --- | --- | --- |
| `BattleEncounter` | 探索层 -> Component | 指定触发敌人实例、敌人队伍预设和是否先制 |
| `BattleCommand` | UI -> Component | 当前玩家的攻击、技能、目标或逃跑选择 |
| `BattleViewState` | Component -> UI | 当前单位数值、行动者、剩余顺序和可用行动 |
| `BattleStep` | Component -> UI | 本次同步推进产生的行动记录、最新视图和可选最终结果 |
| `BattleResult` | Component -> 单局/探索 | 最终 Outcome 和玩家战后 HP/MP |
| `IRunRandomSource` | 单局系统 -> Component | 共享随机序列和 EditMode 可注入随机源 |
| `IBattleConfigProvider` | 配置系统 -> Component | 隔离 Luban 与纯逻辑测试 |

不建立以下中间结构：

- 不建立 Component 到内部 Runtime 的 `BattleStartRequest`；Component 直接使用 Encounter 和单局状态初始化 Runtime。
- 不建立 `BattleController`；Component 是对外控制入口，Runtime 是内部状态和规则核心。
- 不建立 `BattleReturnPlan`；Component 根据 `BattleResult` 回写单局状态，探索层根据 Outcome 执行地图后果。
- 不建立 `IExplorationBattleBridge`；真实探索接入使用现有组件的直接调用或一个明确回调。
- 首版只有随机敌人行为，不建立 `IEnemyBattlePolicy`；出现第二种可替换策略时再提取。
- 不在 `BattleStep` 和 `BattleViewState` 中重复保存流程状态；`Result != null` 表示战斗完成。

### 4.4 必要的内部数据

| 数据 | 生命周期 | 说明 |
| --- | --- | --- |
| `RunPlayerState` | 整个单局 | 跨战斗保留玩家当前 HP/MP 和已结算战斗属性 |
| `BattleRuntime` | 单场战斗 | 单位、轮次、行动机会、事件和最终结果的唯一所有者 |
| `BattleUnit` | 单场战斗 | 运行时 ID、阵营、属性、行动、状态、阵亡和逃跑 |
| `BattleStatus` | 单场战斗 | 状态类型和剩余行动机会次数 |
| `BattleEvent` | 单次推进 | UI 需要展示的行动者、目标和数值/状态变化 |

`BattleViewState` 可以包含单位视图，但内部调度和效果结算不得读取该视图。8 个单位规模下，在外部边界生成只读视图的成本可接受。

### 4.5 目录与程序集

| 位置 | 所有权 |
| --- | --- |
| `Assets/GameMain/Scripts/Runtime/Battle/` | `BattleRuntime`、内部单位和规则、必要的外部数据与配置接口 |
| `Assets/GameMain/Scripts/Runtime/CustomComponent/Run/` | `TurnBattleComponent`、`RunPlayerState` 和单局回写 |
| `Assets/GameMain/Scripts/Runtime/CustomComponent/Random/` | 本局种子与共享随机源 |
| `Assets/GameMain/Scripts/UI/Battle/` | `BattleForm` 和子 View 的表现逻辑 |
| `Assets/GameMain/Tests/EditMode/Battle/` | 直接针对 BattleRuntime 和规则协作者的纯逻辑测试 |

不新增战斗运行时程序集。Runtime 不反向引用 Presentation；`BattleForm` 通过 `GameEntry.TurnBattle` 调用公开战斗入口。EditMode 测试建立独立测试程序集，并通过 `InternalsVisibleTo` 访问内部战斗规则，不为测试把 BattleRuntime 改成公开 API。

### 4.6 完整调用链

```text
队长与地图敌人碰撞
    -> 探索层创建 BattleEncounter
    -> TurnBattleComponent.TryStartBattle
    -> 拒绝重复战斗并校验 Encounter、玩家、配置和随机源
    -> 创建唯一 BattleRuntime
    -> 提交战斗占用，暂停探索更新与计时
    -> 打开 BattleForm
    -> BattleRuntime 推进到等待玩家或战斗结束
    -> BattleForm 读取 BattleViewState
    -> 玩家提交 BattleCommand
    -> TurnBattleComponent 同步校验并推进 BattleRuntime
    -> 内部直接完成调度、效果、敌人自动行动和结束判定
    -> 返回 BattleStep，UI 展示 BattleEvent 和最新视图
    -> BattleResult 产生后，TurnBattleComponent 回写 RunPlayerState
    -> TurnBattleComponent 调用本场战斗的一次性完成回调
    -> 探索层按 BattleResult 的 EncounterId 和 Outcome 处理敌人、掉落、保护或失败
    -> 清理 BattleRuntime，关闭 BattleForm
    -> 允许继续单局时恢复探索更新与计时
```

## 五、边界契约

### 5.1 身份

| 标识 | 用途 |
| --- | --- |
| `EncounterId` | 地图敌人实例的单局唯一标识；不能用敌人配置 ID 代替 |
| `BattleUnitId` | 本场战斗单位的唯一运行时标识；重复敌人配置必须拥有不同 ID |
| `CharacterId` | 玩家角色配置标识，也是战斗结果回写键 |
| `EnemyConfigId` | 敌人种类配置标识，同一敌人队伍内可以重复 |
| `PartyOrder` | 同阵营同速度时的最终并列顺序 |

所有 UI 指令和目标选择都使用 `BattleUnitId`。

### 5.2 遭遇输入

`BattleEncounter` 只包含：

| 字段 | 说明 |
| --- | --- |
| `EncounterId` | 触发碰撞的地图敌人实例 |
| `EnemyPartyConfigId` | 该实例代表的敌人队伍预设 |
| `IsPreemptive` | 碰撞时敌人警惕值未满为 true |

地图坐标、碰撞器、GameObject 和警惕组件由探索层持有，不进入战斗。

### 5.3 单局玩家状态

`RunPlayerState` 保存 `CharacterId`、`PartyOrder`、当前及上限 HP/MP、最终 ATK/MAT/Speed、普通攻击 ID 和技能 ID。

`TurnBattleComponent` 开战时直接从这些状态创建玩家 `BattleUnit`。不再建立字段相同的 `BattlePlayerInput`。战斗只修改 `BattleUnit`，结束时统一回写当前 HP/MP。

### 5.4 玩家指令

`BattleCommand` 至少包含：

| 字段 | 说明 |
| --- | --- |
| `ActorUnitId` | 当前获得行动机会的玩家单位 |
| `CommandType` | `Attack`、`Skill`、`Item` 或 `Escape` |
| `ActionConfigId` | 攻击或技能配置 ID；逃跑为 0 |
| `TargetUnitIds` | 运行时目标 ID 列表 |

- `Item` 按钮可见但首版禁用，不产生指令。
- 只有当前玩家单位能提交指令。
- 全体目标由战斗内核展开，UI 不自行拼装。
- 非法指令不消耗 MP、行动机会或随机数。

### 5.5 UI 视图与行动记录

`BattleViewState` 是 UI 唯一读取面，至少包含：

- 当前轮次和当前行动者。
- 全部单位的 ID、阵营、配置 ID、显示顺序、HP/MP、速度、阵亡、逃跑和状态。
- 本轮从当前行动者开始的剩余顺序。
- 当前玩家可用的行动 ID。

`BattleStep` 至少包含：

- 本次推进产生的有序 `BattleEvent`。
- 推进结束后的 `BattleViewState`。
- 仅在战斗结束时存在的 `BattleResult`。

`BattleEvent` 使用一层记录表达行动者、行动、目标以及数值或状态变化。只有出现无法由一层记录清晰表达的真实表现需求时，才增加子记录类型。

### 5.6 战斗结果

`BattleOutcome` 固定为：

| 结果 | 条件 | 单局是否继续 |
| --- | --- | --- |
| `Victory` | 所有敌人阵亡 | 是 |
| `AllEscaped` | 所有玩家成功逃跑且没有玩家阵亡 | 是 |
| `PartialEscapeDefeat` | 至少一人逃跑，其余仍在战斗中的玩家全部阵亡 | 是 |
| `TotalDefeat` | 所有玩家阵亡且无人逃跑 | 否 |

`BattleResult` 包含 `EncounterId`、`Outcome` 和每名玩家的 `CharacterId`、战后 HP/MP、是否阵亡、是否逃跑。

战斗结果保留原始值，阵亡者 HP 为 0。`TurnBattleComponent` 在非 `TotalDefeat` 结果中统一应用阵亡角色恢复 1 HP/1 MP 的规则。

### 5.7 随机和配置依赖

```csharp
public interface IRunRandomSource
{
    int NextInt(int minInclusive, int maxExclusive);
    bool RollPermille(int successPermille);
}
```

- 本局种子由局外带入，`RandomComponent.BeginRun(seed)` 初始化共享随机源。
- 地图生成、敌人决策、逃跑和掉落依次消费同一个实例。
- 战斗不能按 EncounterId、回合数或时间重新播种。
- `RollPermille(0)` 必须失败，`RollPermille(1000)` 必须成功。
- EditMode 使用可注入序列随机源。

`IBattleConfigProvider` 隔离战斗规则与 `GameEntry.Luban`。配置缺失导致启动失败；正常行动不重复扫描全表完整性。

### 5.8 探索暂停与返回

- `TurnBattleComponent.IsExplorationPaused` 是地图更新统一门禁。
- 玩家移动、队员跟随、敌人巡逻/警惕/追击、搜索、地图碰撞和单局计时遵守该门禁。
- 战斗 UI 和使用 unscaled time 的 UI 反馈不受该门禁影响。
- 不以 `Time.timeScale = 0` 作为唯一暂停机制。
- 开战校验全部通过后才能提交暂停；启动失败不消耗随机数、不修改单局状态。
- `Victory` 返回 EncounterId 并要求探索层移除触发敌人和结算掉落。
- `AllEscaped`、`PartialEscapeDefeat` 返回 EncounterId 并要求探索层恢复触发敌人和开启保护。
- `TotalDefeat` 进入单局失败，不短暂恢复探索。
- 探索层根据 Outcome 执行固定后果，不重新根据玩家 HP 或逃跑数量判定 Outcome。

## 六、固定战斗规则

### 6.1 下一行动者

每次行动或跳过结束后，从本轮尚未获得行动机会且仍在战斗中的单位中选择：

1. 先制第一轮仍有玩家未行动时，只在玩家中选择。
2. 当前速度高者优先。
3. 当前速度相同时，玩家优先于敌人。
4. 同阵营同速度时，`PartyOrder` 小者优先。
5. 已行动单位不会因速度变化再次行动。
6. 候选集为空时开启下一轮；先制限制只适用于第一轮。

阵亡和逃跑单位不进入候选集。眩晕单位会获得行动机会，随后跳过、标记已行动并消耗一次眩晕持续次数。

### 6.2 数值与效果

- 每个 `BattleEffect` 按配置顺序结算。
- 目标变化值为 `FlatValue + SourceStat * SourceScalePermille / 1000`。
- 使用整数运算；首版没有暴击、防御、命中、浮动伤害和元素克制。
- HP/MP 钳制到 0 与对应上限之间。
- HP 到 0 时立即阵亡，之后不能成为合法目标或行动者。
- MP 不足的行动不可执行，不消耗行动机会。
- 战斗中的属性和状态变化不写回探索；只回写最终 HP/MP。

### 6.3 眩晕

- `Stun(1)` 跳过目标未来一次行动机会。
- 目标本轮尚未行动时被眩晕，跳过本轮即将到来的行动。
- 目标本轮已经行动时被眩晕，跳过下一轮行动。
- 重复施加时取当前和新持续次数中的较大值，不相加。
- 战斗结束时清除全部战斗状态。

### 6.4 敌人行动

- 敌人从当前 MP 足够的行动中等概率选择。
- 单体行动从合法目标中等概率选择；全体行动不额外随机目标。
- 没有可执行行动时跳过本次机会，不能形成无限循环。
- 首版逻辑直接读取 `BattleRuntime` 并产生内部指令，不建立策略接口。

### 6.5 逃跑

- 只有当前玩家行动者可以逃跑。
- 每次尝试使用 `GlobalConfig.EscapeSuccessPermille` 和本局随机源独立判定。
- 成功和失败都消耗当前角色本轮行动机会。
- 成功后角色立即离开候选集和合法目标集合，但不视为阵亡。
- 仍有玩家留在战斗中时，单个角色逃跑不结束战斗。

## 七、开发里程碑

### [x] M0 战斗壳层与单局基础状态

目标：在 Main 场景得到第一个可见战斗壳层，并验证暂停和重复进入门禁。

已完成：

- `RandomComponent`（`GameEntry.Random`，Launcher.unity 的 "Random" GameObject）、`IRunRandomSource` 和 `RunRandomSource`。
- `TurnBattleComponent`（`GameEntry.TurnBattle`，Launcher.unity 的 "Turn Battle" GameObject）的玩家临时状态、计时、暂停和战斗占用。
- `RunBattleCoordinator` M0 空流程（`GameEntry.RunBattle`）负责预留占用、构建调试请求、暂停/恢复和打开/关闭 `BattleForm`；M1 将这些职责并回 Component。
- `BattleForm.Logic.cs` 半透明壳层、按钮绑定、道具禁用和占位反馈。
- `BattleDebuggerWindow` 调试入口：仅 Editor/Development Build，路径 Battle/Shell，固定 EncounterId=1、敌人队伍预设 1；缺少单局状态时使用配表角色和固定种子初始化。
- `UIFormType.BattleForm = 103` 和对应 `UIFormConfig` 已导出。
- M0 同时提前建立了一批后续 DTO 和接口；它们不是新架构的既定合同，M1 按第四、五节删除、合并或改名。

已知约束：

- 单局计时暂停来源不止战斗；暂停菜单接入后必须组合暂停来源，不能由战斗结束无条件解除全部暂停。
- `RunRandomSource` 使用 `System.Random`；只要求同一构建内以相同输入复现，不承诺跨平台、跨运行时版本产生相同序列。
- `BattleView` 的回合槽和敌人槽使用运行时实例化模板（`turnSlotsRoot` / `enemySlotsRoot` 和 template），数据接入时由 Logic 创建显示项。

验证证据：

```text
代码状态：已完成
Unity 编译：通过（2026-08-31，dotnet build SepCore.Runtime.csproj 0 错误；Unity 编辑器内编译与 Play Mode 实测通过）
EditMode：不适用（M0 无 EditMode 用例）
Play Mode：通过（2026-08-31，Debugger -> Battle/Shell 实测：
  1. Main 场景未切换、未卸载，半透明 BattleForm 覆盖在地图上；
  2. 调试入口 Open 打开战斗壳层，按钮可点击响应；
  3. 打开后 RunElapsedMs 停止增长，IsTimerPaused、IsExplorationPaused、IsBattleActive 为 true，Close 后计时恢复且标志复位；
  4. 重复点击 Open 被占用检查和 allowMultiInstance=false 拒绝，不会打开第二场。
  截图：未提供）
提交或差异：原验收记录未填写 commit；M0 代码当前已在仓库基线
剩余问题：M0 的提前契约按 2026-09-01 架构修订在 M1 收敛
```

### [x] M1 架构收敛与 1v1 攻击闭环

目标：用最终所有权完成第一场可打完的战斗，不把 M0 的中间契约继续扩散。

实现范围：

- `TurnBattleComponent` 成为唯一外部入口，吸收 `RunBattleCoordinator` 的启动、暂停、UI 打开和结束回写职责。
- 新增内部 `BattleRuntime` 和 `BattleUnit`，Component 持有唯一当前实例。
- 删除 `BattleController` 规划；删除或停止使用 `BattleStartRequest`、`BattlePlayerInput`、`BattleReturnPlan`、`IExplorationBattleBridge` 和 `IEnemyBattlePolicy`。
- 将现有 Snapshot/Advance/Record 类型收敛为 `BattleViewState`、`BattleStep` 和一层 `BattleEvent`；不保留重复 FlowState。
- 支持 1 名玩家和 1 名敌人的轮次、当前行动者和每轮一次行动机会。
- 支持普通攻击、HP 伤害、HP/MP 钳制、阵亡、`Victory` 和 `TotalDefeat`。
- 敌人直接使用首版随机决策并自动反击。
- UI 显示双方 HP、当前行动者和攻击按钮。
- 增加纯逻辑 EditMode 用例，直接验证 BattleRuntime，不依赖 MonoBehaviour 和 UI。
- 建立 Battle EditMode 测试程序集和 `InternalsVisibleTo`，内部规则不因测试改成 public。

明确不做：多人队伍、技能、状态、先制和逃跑。

Play Mode 验收：

- 玩家点击攻击后敌人 HP 立即变化。
- 未结束时敌人自动反击，玩家 HP 立即变化。
- 任一方 HP 归零后停止接受指令并显示结果。
- UI 结果与 BattleEvent、BattleViewState 一致。
- 战斗结束后不存在残留 BattleRuntime，能够再次打开调试战斗。

验证证据：

```text
代码状态：已完成
Unity 编译：通过（2026-09-02，dotnet build SepCore.Runtime.csproj 0 错误 0 警告；Unity 编辑器内 Play Mode 实测编译通过）
EditMode：通过（2026-09-02，新建 Tests 程序集，BattleRuntime 纯逻辑用例 25 个全部通过，
  覆盖创建失败零副作用、攻击伤害公式、HP 钳制、阵亡、Victory/TotalDefeat、
  非法指令不消耗行动、敌人随机决策与固定序列复现、视图字段、连续战斗不继承状态）
Play Mode：通过（2026-09-02，Debugger -> Battle/Shell 实测：
  1. 1v1（角色1 vs 敌人1）开局显示双方 HP、图标与顶部 TurnSlots 一致；
  2. 攻击后敌人 HP 立即变化，0.6s 间歇后敌人自动反击，TurnSlots 高亮逐单位移动且已行动单位不隐藏；
  3. 敌人 HP 归零显示"胜利！"，攻击按钮禁用，高亮熄灭；
  4. 关闭后再次开启可重开新战斗，无残留 BattleRuntime；
  5. 连续两场战斗第二场继承第一场回写的 HP/MP（单局内连续战斗设计预期）。
  截图：未提供）
提交或差异：未提交（working tree 差异：契约收敛、BattleRuntime 内核、组件入口、UI 接线与图标、EditMode 测试）
剩余问题：无阻塞。敌人行动间歇与 UI 注册超时已迁入 GlobalConfig（2026-09-03）；
  调度仍按单位创建顺序轮流，M2 由完整速度/先制规则替换
```

### [x] M2 1 至 4 人多对多、先制与确定性调度

目标：一次完成完整的行动顺序模型。

实现范围：

- 从 `RunPlayerState` 和 `EnemyPartyConfig` 创建最多 4v4 的 BattleUnit。
- 重复敌人配置生成不同 `BattleUnitId`。
- 实现速度、阵营和 `PartyOrder` 并列规则。
- 每次行动后按当前 BattleRuntime 重新选择本轮下一行动者。
- 阵亡单位在轮到前死亡时不再行动。
- 实现先制第一轮玩家全部先行，第二轮恢复普通排序。
- 敌人行动和目标选择只消费注入的本局随机源。
- UI 显示当前行动者和本轮剩余顺序。
- 增加固定随机序列的 EditMode 回归用例。

明确不做：技能、状态、速度修改效果和逃跑。

Play Mode 验收：

- 调试入口可启动 1v1、2v2 和 4v4。
- 普通模式按速度、阵营和队伍顺序行动。
- 先制第一轮所有玩家先于敌人，第二轮恢复普通规则。
- 每个仍在战斗中的单位每轮最多行动一次。
- 两个相同 EnemyConfig 的敌人可以分别选择、受伤和死亡。
- 相同初始状态和随机序列产生相同行动记录。

验证证据：

```text
代码状态：已完成
Unity 编译：通过（2026-09-02，dotnet build SepCore.Runtime.csproj 0 错误 0 警告；Unity 编辑器内编译通过）
EditMode：通过（2026-09-02，BattleRuntime 纯逻辑用例 35 个全部通过；M2 新增调度用例 10 个：
  速度排序、跨阵营同速玩家优先、同阵营同速 PartyOrder、2v2 每轮各行动一次、
  先制第一轮玩家全先行且第二轮恢复速度序、非先制首轮直接速度序、
  重复敌人配置分别击杀、4v4 创建 8 单位、轮到前阵亡移出候选集、4v4 固定随机序列复现）
Play Mode：通过（2026-09-02，Debugger -> Battle/Shell 实测：
  1. 调试入口 1v1 / 2v2 / 4v4 与先制开关可用；
  2. 普通模式按速度、阵营和队伍顺序行动，先制第一轮玩家全先行、第二轮恢复；
  3. 2v2 两个相同敌人可分别受伤和死亡；
  4. 每轮各单位最多行动一次，TurnSlots 按调度顺序显示；
  5. 完成一场后 Close 再 Open 可正常重开并继续行动（修复 UIForm 复用导致按钮监听丢失）；
  6. 敌人数量固定时 EnemySlots/TurnSlots 复用 item，不再逐次销毁实例化。
  截图：未提供）
提交或差异：未提交（working tree 差异：调度规则与先制、调试入口扩展、TurnSlots/EnemySlots 复用、
  敌人图标数据、新增队伍预设 4005、EditMode 调度用例）
剩余问题：无阻塞。行动间歇与 UI 注册超时已迁入 GlobalConfig（2026-09-03）；敌人目标选择（选中态视觉）按文档留到 M3/M7
```

### [x] M3 技能、目标、MP 与数值效果

目标：让当前配表中的数值型攻击和技能完整可用。

实现范围：

- 支持 `Attack` 和 `Skill` 行动配置。
- 支持 `Self`、`SingleAlly`、`AllAllies`、`SingleEnemy` 和 `AllEnemies`。
- 实现 MP 校验、成功后的 MP 扣除、伤害和治疗。
- `EffectResolver` 只在效果逻辑已需要独立测试时从 BattleRuntime 提取；仍直接操作同一 Runtime。
- UI 根据行动配置进入或跳过目标选择，并禁用 MP 不足技能。
- 内核拒绝错误行动者、行动、阵营、目标数量、阵亡目标和逃跑目标。
- BattleEvent 一层记录能够表达当前数值变化；不为未来动画建立额外记录树。

明确不做：眩晕、速度修改、逃跑和道具效果。

Play Mode 验收：

- 角色 1 至 3 的单体伤害、单体治疗和全体伤害完整结算。
- `SingleAlly` 可以选择自己。
- 全体技能一次结算全部合法目标。
- MP 不足或伪造非法指令不消耗行动机会。
- 治疗不超过 MaxHP，伤害不低于 0。

验证证据：

```text
代码状态：已完成
Unity 编译：通过（2026-09-03，dotnet build SepCore.Runtime.csproj 0 错误 0 警告；Unity 编辑器内实测编译通过）
EditMode：通过（2026-09-03，M3 新增纯逻辑用例 14 个（总计 50 个）全部通过，
  覆盖角色 1~3 技能公式与 MP 扣除、治疗钳制、SingleAlly 可选自己、Self 空目标展开与错误目标拒绝、
  AllAllies/AllEnemies 全体一次结算、MP 不足拒绝、阵亡/阵营/目标数量拒绝、敌人随机技能决策与全体技能无额外随机）
Play Mode：通过（2026-09-03，Debugger -> Battle/Shell 实测：
  1. 技能与攻击使用二次确认机制：点击按钮进入待命，点击目标释放；再次点击当前行动按钮取消；
  2. 单体治疗技能点击我方卡片（含自身）正常生效并扣除 MP；
  3. 手动调高技能 MP 消耗后技能按钮置灰，无法释放技能；
  4. 角色 1 技能 101 单体高伤正常结算并扣除 10 MP；
  5. 角色 3 技能 103 全体敌人一次性结算全部存活敌人）
提交或差异：commit 120c00b（BattleRuntime 技能与 5 种目标类型校验展开、敌人技能决策、卡片与槽位点击绑定、
  二次确认与取消机制、敌人行动间歇与 UI 注册超时迁入 GlobalConfig、EditMode 技能用例）
剩余问题：无阻塞。M4 接入眩晕状态与速度改变。
```

### [x] M4 状态、眩晕与实时速度变化

目标：补齐会改变行动机会和当前轮次顺序的战斗内状态。

实现范围：

- 实现 `BattleStatusType.Stun` 和已确认的行动机会持续语义。
- 同类状态刷新持续次数，不叠层。
- 实现 Speed 数值修改并立即重算本轮剩余顺序。
- 已行动单位不会因加速再次行动。
- BattleStatus 只存在于 BattleRuntime，不写回 RunPlayerState。
- BattleViewState 和 BattleEvent 增加当前阶段真实需要的状态字段。

明确不做：复杂状态、每轮伤害、状态动画和永久属性写回。

验证证据：

```text
代码状态：已完成
Unity 编译：通过（dotnet build SepCore.Runtime.csproj 与 Tests.csproj 0 错误 0 警告；Unity 编辑器内实测编译通过）
EditMode：通过（M4 新增纯逻辑用例 10 个（总计 60 个）全部通过，
  覆盖眩晕本轮/下轮跳过、重复施加取较长、敌人晕玩家跳过、减速即时重排、
  已行动单位加速后不再行动、速度下限钳制、先制首轮 8 人栏、减速前后栏顺序）
Play Mode：通过（Debugger -> Battle/Shell 实测：
  1. 减速技能命中后行动栏即时重排，已行动单位保留在栏里；
  2. 眩晕技能命中后未行动目标跳过本轮行动；
  3. 已行动目标被眩晕后跳过下一轮行动；
  4. 重复眩晕保留较长持续 Play Mode 不可观察，EditMode 已覆盖；
  5. 已行动加速不再行动配表暂无加速技能无法实测，EditMode 已覆盖）
提交或差异：commit eca2295（ApplyEffect 速度分支与状态施加、调度眩晕跳过、
  行动栏 DisplayOrder、先制首轮 8 人栏、EditMode 眩晕变速与栏顺序用例）
剩余问题：待补三项 Play Mode 确认后关闭 M4。M5 接入逃跑与结束矩阵。
```

Play Mode 验收：

- 角色 4 的眩晕使尚未行动目标跳过本次行动。
- 已行动目标被眩晕后跳过下一轮行动。
- 重复眩晕只保留较长持续次数。
- 未行动单位速度变化后，剩余顺序立即更新。
- 已行动单位加速后不会再次行动。

### [x] M5 逃跑、结束矩阵与单局回写

目标：完成战斗内部全部结束分支，并把结果正确写回同一局状态。

实现范围：

- 增加 Escape 指令和按钮。
- 使用 `GlobalConfig.EscapeSuccessPermille` 与本局随机源判定。
- 成功和失败都消耗行动机会；成功单位离开候选集和合法目标集合。
- 实现 `Victory`、`AllEscaped`、`PartialEscapeDefeat` 和 `TotalDefeat` 的互斥判定。
- `TurnBattleComponent` 直接消费 BattleResult 并回写 RunPlayerState，不建立 BattleReturnPlan。
- 胜利时存活者保留 HP/MP，阵亡者恢复 1/1。
- 全逃跑保留逃跑时 HP/MP。
- 部分逃跑失败时逃跑者保留值，阵亡者恢复 1/1。
- 清除战斗状态和速度修改。
- `TotalDefeat` 进入单局失败，不恢复探索更新。
- 非 TotalDefeat 关闭 BattleForm 并恢复探索更新。

明确不做：真实地图敌人、掉落和保护碰撞门禁。

验证证据：

```text
代码状态：已完成
Unity 编译：待 Editor 验证（dotnet build 三工程 0 错误 0 警告）
EditMode：待 Editor 验证（M5 新增纯逻辑用例 15 个（总计 77 个），覆盖逃跑成功/失败与行动消耗、
  单人逃跑不结束战斗、四种 Outcome 互斥、逃跑队友胜利保留、非法逃跑拒绝、眩晕逃跑拒绝、
  回写四规则、未知角色与空输入）
Play Mode：通过（Debugger -> Battle/Shell 实测，EscapeSuccessPermille 临时 1000/0，测完恢复 500）：
  1. 已验证：逃跑二次确认，成功/失败都消耗行动，失败后敌人继续行动；
  2. 已验证：单人逃跑成功战斗继续，敌人只选中在场者（Partial 实测中体现）；
  3. 已验证：AllEscaped 与 PartialEscapeDefeat 稳定打出，
     界面文案 + Console 日志 + 重开继承三者一致；
  4. 已验证：连续两场继承 HP/MP，全逃跑保留逃跑时值，部分失败阵亡者恢复 1/1；
  5. 已验证：全员阵亡停留失败界面，手动 Close 也不恢复地图操作；
  6. 已验证：非全灭结果展示后自动关闭并恢复；
  7. 已验证：全过程不碰存档（战斗代码零引用 Save；
     且 persistentDataPath 下根本不存在 save.json，只有 GameFrameworkSetting.dat）；
  Victory/TotalDefeat 见 M1 证据，本里程碑只补逃跑相关两支）
提交或差异：commit 1f722c5（Escape 指令与成功率判定、四种 Outcome、
  回写 helper 与 TotalDefeat 流程、逃跑按钮与结果停留关闭、战斗结束日志、
  EditMode 逃跑回写用例）
剩余问题：无阻塞。M6 接入真实探索遭遇。
```

Play Mode 验收：

- 逃跑失败后角色仍在战斗，敌人继续行动。
- 单人逃跑成功不会在队友仍战斗时结束。
- 四种 BattleOutcome 都能通过调试输入稳定得到。
- 连续两场战斗中，第二场使用第一场写回的 HP/MP。
- 阵亡恢复为 1/1，上一场状态和速度修改不会进入下一场。
- 全员阵亡后不短暂返回可操作地图。
- 全过程不修改 `GameEntry.Save.Data` 或写入 `save.json`。

### [ ] M6 真实探索遭遇、地图恢复、掉落与保护

> 当前代码状态（2026-09-06 静态检查）：队长碰撞进战、警惕/追击、逃跑保护、胜利移除敌人、逃跑恢复敌人均已实现
> （`EnemyPartyLogic`、`TurnBattleComponent.ActivateEscapeProtection`、`EnemyAlertnessTracker`）；
> 胜利掉落与 `TotalDefeat` → 单局失败结算链路未接通。Unity 编译、EditMode、Play Mode 未验证。

目标：接入真实地图，完成碰撞到战斗返回的垂直流程。

前置依赖：探索层提供唯一 EncounterId、敌人队伍预设 ID、警惕状态、地图暂停门禁和敌人实例生命周期操作。

实现范围：

- 队长碰撞创建 BattleEncounter 并调用 `GameEntry.TurnBattle.TryStartBattle`。
- 探索遭遇在启动时提供一次性完成回调，并在回调中消费 BattleResult；不得让 BattleForm 执行地图后果。
- 搜索被战斗中断时保留进度和已掉落物。
- 战斗期间冻结所有地图更新，其他敌人保持战前状态。
- `Victory` 按 EncounterId 移除触发敌人，并使用本局随机源结算掉落。
- 逃跑类结果保留并恢复触发敌人，下次按完整预设重建。
- 逃跑类结果开启 `GlobalConfig.EscapeProtectionMs` 保护。
- 保护期间禁止警惕增长、追击和战斗触发；结束后仍重叠可再次触发。
- 探索层只根据 Component 返回的 EncounterId 和 Outcome 执行固定后果，不读取 BattleRuntime。

明确不做：地图生成、巡逻算法、警惕算法、背包实现和掉落表设计。

Play Mode 验收：

- 警惕未满进入先制战斗，警惕已满进入普通战斗。
- 附近其他敌人不参战，战斗后状态保持。
- 胜利后只有触发敌人消失并可能掉落。
- 逃跑后触发敌人仍在，再战面对完整初始队伍。
- 保护期间不能重入，保护结束仍重叠时可以重入。

### [ ] M7 战斗 UI、输入限制与最小表现完成

> 当前代码状态（2026-09-06 静态检查）：阵亡/逃跑视觉、目标两步确认与取消、行动栏与敌人槽复用已实现（commit e619582）；
> 战斗中禁开背包/保险箱仍是全屏遮罩的隐式遮挡，显式门禁未做。Unity 编译、EditMode、Play Mode 未验证。

目标：达到设计要求的最小可读、可操作战斗界面。

实现范围：

- 我方所有角色显示名称、HP、MP、阵亡和逃跑状态。
- 所有敌人显示名称、HP 条和不可选状态。
- 显示当前行动者和本轮剩余顺序。
- 行动菜单只在等待当前玩家指令时出现。
- 攻击、技能、逃跑可用；道具占位可见但禁用。
- 单体目标有明确选中态；取消目标不消耗行动。
- 战斗期间不能打开共享背包、保险箱或调整保险箱内容。
- 保持半透明覆盖，让玩家确认仍在原地图。
- UI 只持有 BattleViewState 和当前显示所需 BattleEvent，不持有 BattleRuntime 或 BattleUnit。

明确不做：站位、骨骼或序列帧动画、VFX、Timeline、镜头演出和音效同步、UI 文案本地化。

Play Mode 验收：

- 1 至 4 名玩家和敌人的最长名称、HP/MP 不溢出或遮挡。
- 速度变化后顺序显示与下一实际行动者一致。
- 非当前玩家不能操作，敌人自动行动期间不能提交玩家指令。
- 背包和保险箱入口在战斗中不可用。
- 取消目标、MP 不足、阵亡目标和逃跑目标不误消耗行动。

### [ ] M8 全量回归与原型完成

目标：关闭全部战斗设计验收，移除旧架构残留和临时代码。

实现范围：

- 完成第八节 22 条验收映射。
- 完成 EditMode 规则测试和实际探索 Play Mode 流程。
- 删除 M0 遗留且不再使用的 DTO、接口、Coordinator 和重复状态类型。
- 检查公开 Battle 类型；没有外部消费者的实现细节改为 internal。
- 调试入口只在 Editor/Development Build 可用。
- 清理临时日志、硬编码测试数据和无用按钮。
- 检查生成文件、资源收集、UIForm 配表和 Prefab 引用。
- 更新本文档进度、证据和剩余问题。

Play Mode 验收：

- 地图碰撞、战斗、任一结果返回或失败的流程可重复执行。
- 普通、先制、胜利、全逃跑、部分逃跑失败和全员阵亡均留证。
- 连续多场战斗没有重复 UI、随机源重置、地图暂停泄漏或残留 BattleRuntime。
- Unity Console 没有由战斗流程产生的新异常或错误。

## 八、设计验收映射

以下 22 项对应 `Docs/GameDesign/04_TurnBasedCombat.md`。只有对应里程碑完成运行验收后才能勾选。

- [x] 多个玩家和多个敌人可以同时参战。对应 M2。
- [x] 玩家与敌人上限均为 4，并按战备和敌人队伍预设创建。对应 M2。
- [x] 普通轮次每个单位只行动一次，每次行动后按当前速度决定下一位。对应 M2、M4。
- [x] 同速玩家优先，速度变化立即影响尚未行动单位。对应 M2、M4。
- [x] 同阵营同速按队伍顺序行动。对应 M2。
- [x] 死亡、眩晕或无法行动单位跳过本轮。对应 M2、M4。
- [ ] 轮到玩家角色时才显示并处理该角色的行动选择。对应 M1、M7。
- [x] 先制第一轮所有玩家排在所有敌人之前。对应 M2。
- [x] 先制第二轮恢复普通速度规则。对应 M2。
- [x] 玩家可选择攻击、技能和逃跑，道具保留但不可用。对应 M3、M5、M7。
- [ ] 战斗中不能打开共享背包或保险箱。对应 M7。
- [x] 敌人随机选择可用行动和合法目标。对应 M1、M2、M3。
- [ ] 敌人全灭时胜利，玩家全灭时战斗和单局失败。对应 M1、M5。
- [x] 单个角色可逃跑；只要有人逃跑，其他人阵亡不导致单局失败。对应 M5。
- [x] 逃跑使用配置成功率，失败后继续留在战斗。对应 M5。
- [x] 逃跑成功或失败均消耗本轮行动。对应 M5。
- [ ] 逃跑返回战前位置并获得 2 秒保护。对应 M5、M6。
- [x] 逃跑者保留 HP/MP，部分失败中的阵亡者恢复 1/1。对应 M5。
- [x] 任何非全员阵亡结果中的阵亡角色恢复 1/1。对应 M5。
- [ ] 逃跑后地图敌人恢复，重战按完整预设创建。对应 M6。
- [ ] 保护期间不警惕、不追击、不触发战斗，结束仍重叠时可重入。对应 M6。
- [ ] 战斗 UI 显示我方 HP/MP、行动顺序和敌人 HP。对应 M7。

## 九、最低测试矩阵

### EditMode

| 类别 | 必测情况 |
| --- | --- |
| 单位创建 | 1v1、4v4、重复 EnemyConfig、不合法队伍人数 |
| 普通排序 | 速度不同、跨阵营同速、同阵营同速、每轮只行动一次 |
| 动态排序 | 未行动单位加速/减速、已行动单位加速、死亡后移出候选集 |
| 先制 | 第一轮玩家全先行、第二轮恢复普通规则 |
| 目标 | Self、SingleAlly 含自己、全体目标、阵营错误、阵亡和逃跑目标 |
| MP 与效果 | MP 不足、伤害、治疗、全体效果、HP/MP 钳制、顺序效果 |
| 眩晕 | 行动前施加、行动后施加、重复施加、跳过后失效 |
| 敌人决策 | 过滤不可用行动、合法目标、无行动时跳过、固定随机序列 |
| 逃跑 | 0/1000 边界、成功/失败消耗行动、四种结束结果 |
| 回写 | 胜利阵亡恢复、全逃跑保留、部分失败恢复、全灭不恢复探索 |
| 生命周期 | 启动失败无副作用、结束清理 Runtime、连续战斗不继承状态 |

### Play Mode

| 流程 | 必测情况 |
| --- | --- |
| UI 生命周期 | 打开、关闭、重复进入、非当前行动者不可输入 |
| 地图暂停 | 玩家、敌人、搜索、计时停止；战斗 UI 仍响应 |
| 遭遇 | 普通碰撞、先制碰撞、附近敌人不参战 |
| 返回 | 胜利移除敌人、逃跑保留敌人、完整队伍重建 |
| 保护 | 保护期不重入，结束仍重叠可重入 |
| 连战 | HP/MP 延续、状态清除、共享随机序列不重置 |
| 失败 | 全员阵亡进入单局失败，不短暂恢复地图操作 |

## 十、原型完成条件

只有同时满足以下条件，本文档状态才能改为“完成”：

- M0 至 M8 全部标记为 `[x]`，且每个里程碑都有要求的运行证据。
- 第八节 22 条设计验收全部通过。
- EditMode 和 Play Mode 最低测试矩阵全部通过。
- `TurnBattleComponent` 是外界唯一战斗入口，当前战斗只有一个 BattleRuntime 所有者。
- 战斗内部协作者共享 Runtime，没有内部请求/响应 DTO 链和全局事件总线。
- UI、探索层和存档不持有或修改 BattleRuntime、BattleUnit。
- 战斗不直接写 SaveData、不切换 Battle 场景、不重建本局随机源。
- 胜利、逃跑和失败对地图敌人、玩家临时状态、计时和保护期的处理符合设计。
- 正式构建中没有开发调试入口和测试数据。
