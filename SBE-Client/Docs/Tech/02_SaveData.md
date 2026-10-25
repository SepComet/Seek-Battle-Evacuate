# 存档数据结构

> 定义游戏存档中需要持久化的数据。仅覆盖局外状态与已结束单局的结算记录；进行中的单局使用临时状态，不写入存档。
> 相关设计：`Docs/GameDesign/06_PrototypeScope.md`（存档相关验收项）、`Docs/GameDesign/05_LootAndProgression.md`（物品归属）。
> 代码位置：`Assets/GameMain/Scripts/Base/Definition/DataStruct/`（`SaveData`、`ItemStack`、`CharacterSave`、`LoadoutSave`、`RunRecord`），枚举复用 Luban 生成的 `RunResultType`、`DifficultyTier`。

## 设计原则

- 存档只保存局外状态：主仓库内容、角色装备、当前战备配置、已结束单局的结算记录。
- 背包、保险箱、单局进度均为进入单局时创建的临时状态，不落盘；异常关闭直接丢弃。
- 数值上限（堆叠上限、背包格数等）来自 Luban 配表（`ItemConfig.StackLimit`、`GlobalConfig.BackpackSlotCount` 等），不写死在存档中。
- 存档使用 JSON 序列化，便于调试与手动修改；并采用**模块化拆分存储与脏标记按需落盘**机制。

## 模块化拆分与按需落盘架构

为了杜绝频繁拖拽物品时的磁盘 I/O 过载，存档持久化拆分为位于 `Application.persistentDataPath/Save/` 目录下的独立文件：

1. **`warehouse_data.json`（仓库物资数据）**：
   - 包含 `WarehouseDataSave`：仅记录物品资产清单（`instanceId`、`itemId`、`count`）；
   - 仅在资产发生实质增减（撤离带出、商店买卖、装备穿脱）时标记并落盘。
2. **`warehouse_layout.json`（仓库网格布局）**：
   - 包含 `WarehouseLayoutSave`：仅记录空间排布（`instanceId`、`x`、`y`、`isRotated`）；
   - 挪动或旋转道具时仅在内存中更新并标记脏状态，在离开仓库界面或游戏退出时一次性批量落盘，拖拽期间 0 磁盘 I/O。
3. **`character_data.json`（角色与战备数据）**：
   - 包含 `CharacterDataSave`：角色列表 `characters`（穿戴装备）与战备配置 `loadout`；
   - 仅在换装、调整出战或结算同步装备时落盘。
4. **`meta_data.json`（全局元数据）**：
   - 包含 `MetaDataSave`：版本号 `version`、更新时间戳 `updatedAt` 与结算历史 `runHistory`。

### 兼容性与自愈迁移（Migration & Self-Healing）
- **老存档无损迁移**：若检测到旧版单文件 `save.json`，系统自动加载并拆分为四个新文件写入 `Save/` 目录；
- **布局丢失自愈**：若 `warehouse_layout.json` 丢失，物资资产不会损坏，系统会自动通过 `GridItemContainer.TryAutoInsert` 智能寻位恢复网格。

## 序列化格式（单文件视图 / 历史格式）

枚举在 JSON 中以整数值存储：`RunResultType`（`Extracted=1`、`Defeated=2`、`TimedOut=3`、`Quit=4`）、`DifficultyTier`（`Tier1=1`、`Tier2=2`、`Tier3=3`）。

```json
{
  "version": 1,
  "updatedAt": 1756540000000,
  "mainWarehouse": [
    { "itemId": 1001, "count": 3, "x": 0, "y": 0, "isRotated": false },
    { "itemId": 2005, "count": 1, "x": 2, "y": 0, "isRotated": true }
  ],
  "characters": [
    { "characterId": 1, "weaponItemId": 1001, "armorItemId": 0 },
    { "characterId": 2, "weaponItemId": 0,    "armorItemId": 0 }
  ],
  "loadout": {
    "partyCharacterIds": [1, 2],
    "carriedItems": [
      { "itemId": 3002, "count": 5 }
    ],
    "difficultyId": 1
  },
  "runHistory": [
    {
      "outcome": 1,
      "difficultyId": 1,
      "seed": 2026083001,
      "startedAt": 1756540000000,
      "endedAt": 1756541200000
    }
  ]
}
```

## 结构定义

### SaveData（存档根）

| 字段 | 类型 | 说明 |
| --- | --- | --- |
| `version` | int | 存档结构版本，当前为 1；结构变更时递增并处理迁移 |
| `updatedAt` | long | 最后写入时间，Unix 毫秒 |
| `mainWarehouse` | `GridItemStack[]` | 主仓库内容（带网格坐标与旋转状态），容量由 `GlobalConfig.WarehouseSlotCount` 配置，可空 |
| `characters` | `CharacterSave[]` | 拥有的角色，数组顺序即角色入队顺序（速度并列时按此顺序行动） |
| `loadout` | `LoadoutSave` | 当前战备配置 |
| `runHistory` | `RunRecord[]` | 已结束单局的结算记录，可空 |

### GridItemStack（网格物品堆叠）

| 字段 | 类型 | 说明 |
| --- | --- | --- |
| `itemId` | int | 物品 ID，对应 `ItemConfig.Id` |
| `count` | int | 数量，不超过配表 `ItemConfig.StackLimit` |
| `x` | int | 网格列锚点坐标 AnchorX，从 0 开始 |
| `y` | int | 网格行锚点坐标 AnchorY，从 0 开始 |
| `isRotated` | bool | 是否顺时针旋转 90 度 |

主仓库使用 `GridItemStack` 记录网格坐标；单局出战携带等非网格容器继续使用基础 `ItemStack` 堆叠列表存储。

### CharacterSave（角色）

| 字段 | 类型 | 说明 |
| --- | --- | --- |
| `characterId` | int | 角色 ID，对应 `CharacterConfig.Id` |
| `weaponItemId` | int | 武器栏物品 ID，0 表示空栏 |
| `armorItemId` | int | 防具栏物品 ID，0 表示空栏 |

装备穿戴在角色装备栏，不占用共享背包格子；成功撤离与任何非全员阵亡的战斗后均保持穿戴，死亡结算时随角色丢失，结算逻辑负责处理，存档本身不记录"阵亡"等局内状态。

### LoadoutSave（战备配置）

| 字段 | 类型 | 说明 |
| --- | --- | --- |
| `partyCharacterIds` | int[] | 出战角色 ID 及顺序，1~4 人，顺序决定同速时的行动次序 |
| `carriedItems` | `ItemStack[]` | 携带进入地图的物品，开局时填充共享背包；首版无局内效果 |
| `difficultyId` | `DifficultyTier` | 本局难度，对应 `DifficultyConfig` 主键 |

随机数（seed）不属于战备配置：每次进入单局时输入，只写入该局对应的 `RunRecord`，不持久化到 `loadout`。

### RunRecord（结算记录）

| 字段 | 类型 | 说明 |
| --- | --- | --- |
| `outcome` | `RunResultType` | 结算结果 |
| `difficultyId` | `DifficultyTier` | 本局难度 |
| `seed` | long | 本局使用的随机数 |
| `startedAt` | long | 进入单局时间，Unix 毫秒 |
| `endedAt` | long | 结算时间，Unix 毫秒 |

结算结果枚举复用 Luban 生成的 `RunResultType`：

| 值 | 说明 |
| --- | --- |
| `Extracted` | 成功撤离 |
| `Defeated` | 全员阵亡，单局失败 |
| `TimedOut` | 25 分钟上限未撤离，撤离失败 |
| `Quit` | 玩家主动退出单局，按撤离失败结算 |

`Defeated`、`TimedOut`、`Quit` 均只保留保险箱内容，仅用于结算记录的区分。

## 生命周期

- **新存档**：`characters` 由 `GlobalConfig.NewGameCharacterIds` 生成，初始装备取 `CharacterConfig.WeaponItemId` / `ArmorItemId`；`mainWarehouse`、`loadout.partyCharacterIds`、`loadout.carriedItems`、`runHistory` 为空。
- **进入单局**：根据存档中的局外状态创建仅供本局使用的临时状态（背包、保险箱、单局进度），单局进行中只修改临时状态，不写入存档。
- **正常结算**（撤离成功 / 死亡 / 超时 / 主动退出）：按结算规则把物品写入主仓库或丢弃，追加一条 `RunRecord`，一次性写盘。
- **异常关闭**：临时状态丢弃，存档保持进入该局前的状态，不产生结算记录。

## 读写组件

> 代码位置：`Assets/GameMain/Scripts/Runtime/CustomComponent/Save/SaveComponent.cs`，通过 `GameEntry.Save` 访问，挂在 Launcher 场景 `Customs/Save` 物体上。

| API | 说明 |
| --- | --- |
| `Data` | 当前内存中的存档数据，未加载或未创建时为 null |
| `HasSave` | 磁盘上是否已存在存档文件 |
| `IsReady` | 存档数据是否可用 |
| `Load()` | 从磁盘读取存档；文件不存在返回 true 且 `HasSave` 为 false，解析失败返回 false |
| `CreateNewGame()` | 依据配表创建新存档数据（不写盘） |
| `Save()` | 将当前存档写入磁盘，自动更新 `updatedAt`；先写临时文件再替换，避免写入中断损坏存档 |

- 存档文件路径为 `Application.persistentDataPath` 下的 `save.json`（`SaveComponent` 的 `_fileName` 可配置）。
- 加载时若 `version` 与 `SaveData.CurrentVersion` 不一致，记录警告并继续加载，迁移逻辑后续需要时补充。

## 验收对应

对应 `Docs/GameDesign/06_PrototypeScope.md` 存档相关验收项：

- [ ] 退出并重新打开游戏后，局外主仓库、角色装备和战备配置能够从本地存档恢复。
- [ ] 已结束单局的结算数据能够写入本地存档并在重新打开游戏后读取。
- [ ] 重新打开游戏时不会恢复尚未结束的单局。
- [ ] 单局中主动退出时能够按撤离失败结算，保存保险箱内容和本局失败记录。
- [ ] 异常关闭的未结算单局不会修改局外存档，也不会生成结算记录。

## 还没决定的问题

暂无。存档的加密、校验与多存档位支持不在原型范围内，后续需要时再补充。