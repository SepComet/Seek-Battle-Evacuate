# Luban 数据表组件

> 负责加载 Luban 导出的二进制配置表（.bytes），向业务提供统一访问入口。
> 相关组件：`Assets/GameMain/Scripts/Runtime/CustomComponent/Luban/LubanComponent.cs`

## 数据流

```
Datas/*.xlsx (Excel 配置源)
        │  gen_cli.sh（Luban 导出，产出 代码 + 数据）
        ▼
┌──────────────────────┬────────────────────────────────┐
│ Assets/GameMain/     │ Assets/GameMain/Scripts/Base/Gen│
│ DataTables/tb*.bytes │ Tables.cs + Tb* + *Config bean  │
└──────────────────────┴────────────────────────────────┘
        │ GameEntry.Luban.LoadTables()（运行时加载）
        ▼
  GameEntry.Luban.Get<T>(id) / GetTable<T>()
```

## 目录约定

| 路径 | 内容 |
| --- | --- |
| `数据表/Datas/` | 配置源 xlsx（`__tables__.xlsx` 注册所有表） |
| `数据表/Defines/` | Luban 类型定义（枚举、结构） |
| `数据表/luban.conf` | 导出配置（target=client，topModule=`SepCore.Definition`） |
| `数据表/gen_cli.sh` | 导出脚本；`path.txt` 指定输出根目录（`../Assets/GameMain/`） |
| `Assets/GameMain/DataTables/` | 导出数据：`tb*.bytes`（运行时用）+ `tb*.json`（调试对照用） |
| `Assets/GameMain/Scripts/Base/Gen/` | 生成代码：`Tables` 门面、`Tb*` 表类、`*Config` 数据行类、枚举 |
| `Assets/GameMain/Scripts/ThirdParty/Luban/` | Luban 运行时（`ByteBuf`、`BeanBase` 等，程序集 `Luban.Runtime`） |
| `Assets/GameMain/Scripts/Runtime/CustomComponent/Luban/` | `LubanComponent` 运行时组件 |

## 组件 API

| API | 说明 |
| --- | --- |
| `LoadTables(onSuccess, onFailure)` | 异步并发加载全部表并构建 `Tables`，完成后回调 |
| `IsReady` | `Tables` 是否已构建完成 |
| `Get<T>(int id)` | 单行查询；未找到返回 `null`；表未加载抛 `InvalidOperationException`；未注册类型抛 `NotSupportedException` |
| `GetTable<T>()` | 整表查询，返回 `IReadOnlyList<T>`（怪物池、掉落池等需要遍历的场景用） |

用法示例：

```csharp
EnemyConfig enemy = GameEntry.Luban.Get<EnemyConfig>(3001);
IReadOnlyList<ItemConfig> allItems = GameEntry.Luban.GetTable<ItemConfig>();
UIFormConfig form = GameEntry.Luban.Get<UIFormConfig>(UIFormType.DialogForm); // enum 可转 int
```

## 命名与结构约定

- 表名统一小写 `tbxxxconfig`（Excel 表名、导出文件名、`Tables` 构造参数、`TableNames` 数组四者必须一致）。
- 生成类型命名：数据行 `XxxConfig`（如 `EnemyConfig`）、表类 `TbXxxConfig`（如 `TbEnemyConfig`）、枚举直接命名（`Rarity`、`DifficultyTier`），全部在命名空间 `SepCore.Definition`。
- 主键绝大多数为 `int`；枚举主键的表（`tbrarityconfig`、`tbdifficultyconfig`）在 `TableAccessors` 注册时做显式转换，业务侧仍传 `int`。
- 单行表（`mode=one`，如 `tbglobalconfig`）无主键 `Get`，**不注册**进访问器，通过 `Tables.TbGlobalConfig.Data` 访问。
- 数据行类继承 `Luban.BeanBase`。

## 新增一张表的完整流程（4 步）

1. **建表**：在 `数据表/Datas/` 新增 `XxxConfig_xx.xlsx`，并在 `__tables__.xlsx` 注册表名与结构。
2. **导出**：运行 `数据表/gen_cli.sh`（两个 Luban 命令都要跑：先 `-c cs-bin` 生成代码，再 `-d json -d bin` 导出数据）。
3. **登记表名**：`LubanComponent.TableNames` 追加小写表名（`"tbxxxconfig"`）。
4. **注册访问器**：`LubanComponent.TableAccessors` 追加一行：
   ```csharp
   { typeof(XxxConfig), Accessor(tables => id => tables.TbXxxConfig.GetOrDefault(id), tables => tables.TbXxxConfig.DataList) },
   // 枚举主键：Accessor(tables => id => tables.TbXxxConfig.GetOrDefault((YyyEnum)id), tables => tables.TbXxxConfig.DataList)
   ```

## 资源与打包约定

- 运行时按 `Assets/GameMain/DataTables/{表名}.bytes` 路径经 `GameEntry.Resource.LoadAsset` 加载，`Constant.AssetPriority.DataTableAsset` 作为优先级。
- 正式打包需资源收集包含全部 `tb*.bytes`，否则打包后加载失败。
- 加载中途某张表失败会走 `onFailure` 回调，预加载流程（`ProcedurePreload`）会停在加载界面，需排查表名/路径/资源收集。

## 与旧 DataTable 组件的关系

- 旧的 GameFramework `DataTableComponent` + txt 表 + 手写 `DR*` 行类已全部移除（2026-08 迁移）。
- `DataTableComponent` 组件本身仍挂在场景（框架内置组件，GameEntry 初始化需要），但已无任何数据加载。
- 预加载与所有消费方（Entity/Sound/UI/Scene 扩展）均走 `GameEntry.Luban`。