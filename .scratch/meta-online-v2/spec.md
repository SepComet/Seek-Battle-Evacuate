Status: ready-for-human

# Spec: 局外状态全量上云 v2（meta-online-v2）

## Problem Statement

仓库上云 v1 已把仓库物品、布局、角色花名册与穿戴装备搬到服务器（`warehouse-online-v1`）。但局外状态仍有一批留在本地：出战队列（`loadout.partyCharacterIds`，含顺序）、难度选择（`loadout.difficultyId`）、携带计划（`loadout.carriedItems`）、战绩（`runHistory`）。这些数据换设备即丢，也没有服务器侧记录。本 spec 把剩余局外状态全部上云，服务器成为局外状态的完整唯一真源。

本 spec **显式推翻 v1 的边界决定**（v1 §数据边界："留本地：loadout……runHistory……"）。

## Solution

沿用 v1 全部骨架（WebSocket + JSON 信封 + op-set + rid 幂等 + settle runId 幂等 + ok 回包全量快照），协议版本 1 → 2：

- 新增三个 op：`party`（整队替换）、`difficulty`（设置难度）、`carry`（携带计划整表替换），走现有 `ops`/`settle` 批次与校验路径。
- 快照扩展 `party / difficulty / carried` 三个字段，零值语义统一为"未配置"。
- `settle` 信封扩展 `difficulty / seed / startedAt / endedAt`，落 `settlements` 新列；settlements 表即战绩存储。
- 新增 `history` 消息：登录后拉取最近战绩（上限 100 条，按 startedAt 倒序），客户端 `runHistory` 降级为缓存。
- `migrate` 扩展：首登迁移携带完整战备配置。
- 服务端新增读取 `tbglobalconfig.json`（队伍/背包上限）与 `tbdifficultyconfig.json`（难度枚举），沿用"直接读 gen_cli 导出 JSON"的保留决策。

## User Stories

1. 作为玩家，我在编队界面调整出战角色与顺序后，服务器记住我的队伍，换设备登录队伍不变。
2. 作为玩家，我开局选择的难度被服务器记录，作为战绩的一部分可追溯。
3. 作为玩家，我配置的携带计划（带哪些消耗品）持久保存在服务器。
4. 作为玩家，我重装游戏后，历史战绩（结局/难度/种子/起止时间）从服务器恢复。
5. 作为玩家，我在离线状态下不能修改出战队伍，以便不会做出服务器记不住的操作。
6. 作为开发者，我想让三项战备配置走与经济 op 同一条 ops 通道（rid 幂等、原子应用、全量快照回包），以便同步语义只有一份。
7. 作为开发者，我想让战绩落在 settlements 表（结算幂等表天然按 runId 唯一），以便不引入第二套战绩存储。

## Implementation Decisions

**数据边界（v2 修订）**

- 服务器拥有：仓库物品与布局、角色花名册与穿戴装备（v1 已有）+ 出战队列（含顺序）、难度、携带计划、战绩。
- 本地仅存缓存：SaveData 形状不变，`loadout`/`runHistory` 全部来自服务器快照/回包，本地写盘仅为缓存。
- 强制在线不变；局内玩法零改动。

**op 设计**

- `party {party:[cid…]}`：整队全量替换。顺序即出战序（决定同速行动次序）。不做逐位增删——队列最长 4 人，全量替换最简单且天然消除顺序歧义。
- `difficulty {difficulty:n}`：设置难度枚举值。
- `carry {carry:[{itemId,count}…]}`：携带计划整表替换。携带品无格位、无实例身份（v1 物品模型），全量替换。
- 三者走与经济 op 相同的 `ops` 消息：rid 幂等、原子应用、ok 回全量快照。保留仓库实际既有顺序 recruit→rel→del→add→mov→wear（新招募角色需先存在才能穿戴），末尾追加 party→difficulty→carry。

**零值语义**

- 空队列 / `difficulty=0`（DifficultyTier.None）/ 空携带列表 = "未配置"，合法状态（编辑中间态）。
- 客户端 ApplySnapshot 收到空队列时把 `partyCharacterIds` 写回 `null`（而非空数组），保留 DeploymentForm"从未配置过则默认组队"的现有行为——这也是存量 v1 用户的平滑迁移路径。
- 默认播种仅在打开战备页时执行；同页快照刷新/恢复不会重新播种，避免移除最后一人后被 ack 自动组回默认队伍。LoadoutSave.Normalize 保留 null。

**服务端校验（延续轻校验）**

- party：cid ∈ 该用户花名册、无重复、`0 ≤ len ≤ tbglobalconfig.maxPlayerPartySize`。空队合法。
- difficulty：0 或 ∈ `tbdifficultyconfig.tier` 集合。
- carry：itemId 存在、`1 ≤ count ≤ stackLimit`、条数 ≤ `tbglobalconfig.backpackSlotCount`。
- 结算批次不触碰 party/difficulty/carry（结算只改仓库与装备归属）；apply 顺序上三类新 op 排在花名册操作之后（若同批 recruit + party，party 可引用新招募角色）。

**settle 与战绩**

- settle 信封扩展 `difficulty / seed / startedAt / endedAt`（客户端在 ProcedureMain 与结算处现成），随 ops 一起原子落 `settlements` 新列。
- 幂等语义不变：重复 runId 回放首次结果，元数据不重复写。
- 新消息 `history`：需登录；返回 `{history:[{runId, outcome, difficulty, seed, startedAt, endedAt}]}`，按 startedAt 倒序、上限 100 条。历史不进每条 ok 快照（避免每次 ops 回包携带全量战绩）。
- 客户端登录流程：ApplySnapshot → 拉取 history → 整体覆盖本地 `save.runHistory` → 落盘缓存。history 失败返回登录未完成，交由现有登录流程重试，避免断线后标记 OnlineReady。沿用现有结算流程，在上报前追加本地记录，ack 后以快照与缓存落盘收尾。
- 已知边界：settle 被拒时本地预先追加的战绩与服务端短暂分歧，下次登录 history 整体覆盖纠正；不补做本地历史导入或合并。SaveData 实际记录类型为 RoundRecord / RoundResultType，协议仍使用 outcome 等字段。

**migrate 扩展**

- `MigratePayload` 增加 `party / difficulty / carried`。校验：party ⊆ 本次迁移的角色集合、难度枚举合法、carry 物品合法。缺省字段 = 未配置。

**兼容与迁移**

- `Version = 2`：新旧客户端/服务器互连直接 `bad_message` 干净失败。同仓库同步发版，原型阶段可接受。
- 存量用户（已 initialized）：三项战备在服务器为零值，首次打开编队界面按现有默认逻辑播种，随点击上报；老战绩的 settlements 行元数据列为 0 值，登录拉取 history 会一次性覆盖本地 runHistory（老记录 seed 等归零，可接受）。

**数据库**

- 新表 `user_loadout(user_id PK, difficulty, party_json, carried_json)`；`settlements` ALTER 增加 `difficulty / seed / started_at / ended_at`。
- party/carried 以 JSON 文本列存储（与 settlements.snapshot_json 同风格）；rewrite 全量重写模式不变。

## Testing Decisions

- 服务端（`go test ./...`，经 sbe-dev MCP）：三个新 op 的校验用例（花名册外 cid、重复 cid、超上限、未知难度、未知携带物品、超背包格数）与原子性（任一失败整批回滚）；migrate 带 party；settle 元数据落库 + history 返回内容与排序；full-flow 集成扩展（squad 编辑 → 断线重连 auth → pull 校验 party 保持）。
- 客户端 EditMode（经 mcp-for-unity，Editor 未开留人工回填）：EconomyDiff 三条新规则；FakeServer 扩展三 op 语义，squad 编辑 → flush → 快照回归；ApplySnapshot 空队列保留 null；history 填充 runHistory；settle 信封含元数据。
- 不测第三方 WebSocket 库、MySQL 引擎本身、UGF 与 UI 渲染；仓库 SQL 映射路径另有可选真实 MySQL 集成用例。
- 遵守验证纪律：agent 不启动 Unity、不主动编译；EditMode 优先通过已有 Editor 的 MCP 执行，未加载/无法执行的用例由用户回填。

## Out of Scope

- carriedItems 的消耗语义（局内使用扣减、与仓库的联动）——P1-M4 消耗品闭环时定义，本期仅做镜像同步。
- 战绩界面 / 档案馆 UI。
- 多设备、存档合并、离线模式、反作弊深校验（同 v1）。

## Comments

### 实现记录

### 2026-10-25 中断任务复查与补齐

实现已补齐，状态为待人工验收，未替用户勾选运行验收项。

- 已有服务端协议/core/内存与 MySQL 存储/启动旗标/配置迁移实现通过静态核验；补充非法配置、原子回滚、快照深拷贝、history 最近 100 条/用户隔离/排序/幂等、重连 auth→pull 与 v1 版本拒绝测试。
- 修复客户端战备 diff 实际先于资产操作发射的问题；三类放在末尾。SettleAsync 明确滤除三类战备 op，服务端仍保留通用批次能力。
- 补齐编队、默认播种、难度切换的在线门控与通知；刷新 Toggle 不触发写回；快照刷新保持空队。保留原有 Save() 缓存落盘。
- 补齐 Normalize 保留 null、history 失败不完成登录，以及 FakeServer/重连/清空/顺序/64 位元数据测试。README 与存档所有权文档已更新。
- 与原方案的实物差异：当前 tbglobalconfig.json 是 camelCase 单元素数组；同步脚本实际同步 item/global/difficulty 三份配置，没有第四份服务端读取表。配置副本与客户端 SHA256 一致。

### 验证证据与边界

- sbe-dev `server_test`：62 通过、0 失败、1 跳过，11 个包零失败；见 `server-tests.jsonl`。唯一跳过为真实 MySQL 集成（未配置 SBE_TEST_MYSQL_DSN）。
- mcp-for-unity 已开启 Editor 首轮网络 EditMode：38/38 通过，含三类差分、迁移、空队、重连、history、结算元数据与排除战备；见 `unity-tests.txt`。
- 最后新增的 3 条 Normalize/history 失败回归，定向 MCP 请求未发现测试（summary.total=0），不是通过；见 `unity-extra-tests.txt`。Editor 未重新加载这三条，用例及最终 UI 修改仍待用户刷新后复跑/验收。没有主动请求脚本编译、启动 Unity 或进入 Play Mode。
- 同日后续网络状态 UI 任务中，Editor 自然导入后通过 MCP 复跑网络三组测试，53/53 通过，已包含上述 3 条回归；此前未发现测试的执行记录保留作历史。证据：`../network-status/client-retest.json`。UI 的真实运行验收仍未回填。
- sbe-dev `consistency_check`：0 违规，工具汇总 4 警告，明细为第三方 UniTask 程序集名称引用等；见 `consistency-check.jsonl`。`git diff --check` 无空白错误。
- 潜在编译/逻辑验证缺口：静态未发现明确的新编译错误，但最新新增测试未载入 Editor，不能把首轮测试当作最终全项目编译通过；真实数据库迁移与 SQL 行为未执行；UI 通知/默认播种/空队保持仍需真实界面验收。

### 人工回填清单

- [ ] 在已有 Editor 确认当前项目无编译错误；网络 EditMode 已由 agent 实际复跑通过（见后续记录），人工仍需验证真实 UI 与运行流程。
- [ ] 用独立 MySQL 测试库依次应用 0001/0002/0003（已有 v1 库仅执行 0002 一次，v2 后新增 0003 warehouse 复合主键迁移），设置 SBE_TEST_MYSQL_DSN 运行 mysqlstore 集成用例（含 TestMySQLSettleOwnershipAndPerUserUID 的跨用户 runId 拒绝与按用户 uid 共存）；不以跳过视为通过。
- [ ] 战备页调整队伍与顺序、切换难度，等待 ack 后重连确认恢复；移除最后一人后同页保持空队，Begin 禁用；重新打开页面可按未配置规则播种。
- [ ] 离线后编队与难度不能修改；存量 v1 账户首次打开页面默认组队与难度能上报；确认无快照刷新循环。
- [ ] 完成一局并重登，检查 difficulty/seed/startedAt/endedAt 与 outcome，确认历史覆盖本地缓存；补验 rejected settle 的已知分歧边界。
