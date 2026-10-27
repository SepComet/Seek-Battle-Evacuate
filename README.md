# Seek-Battle-Evacuate（SBE）

单人撤离类 Roguelike 原型：探索 → 回合制战斗 → 撤离结算，局外经济与进度全量上云。仓库含三个子项目与配套工具链，客户端 Unity 2022.3.62f3c1（URP 2D + GameFramework 框架 + Luban 配置表），服务端 Go + WebSocket + MySQL。

## 目录结构

| 目录 | 内容 |
| --- | --- |
| `SBE-Client/` | Unity 客户端。游戏代码在 `Assets/GameMain/Scripts/`，按程序集分层：`Base`（常量、存档结构、Luban 生成代码）、`Runtime`（战斗内核 `BattleRuntime` 纯逻辑、自定义组件：Luban/Random/Run/Save/Net）、`UI`（各界面 Form/View）、`Procedure`（UGF 流程）、`Tests`（EditMode 测试）。框架源码在 `Assets/GameFramework/`（UGF） |
| `SBE-Client/Docs/` | 项目文档。`GameDesign/` 设计文档（核心循环、探索、回合制战斗、掉落与局外成长），`Tech/` 技术文档（Luban 表约定、存档、UI 维护、探索与敌人 AI），`Tech/TODO.md` 为研发路线与当前里程碑 |
| `SBE-Design/` | Luban 配置源。`Datas/*.xlsx` 源表（角色/敌人/掉落/难度/战斗行动等，`__tables__.xlsx` 注册）、`Defines/` 枚举定义、`gen_cli.bat` / `gen_cli.sh` 一键导出（C# 代码 + json/bin 数据 → 客户端） |
| `SBE-Server/` | Go 游戏服务端（局外状态全量上云：账号、仓库、花名册与装备、出战队列、难度、携带计划、战绩）。`cmd/` 入口（服务器与冒烟工具）、`internal/` 业务实现、`migrations/` 建表、`docker-compose.yml` MySQL + 服务器一键部署，详见其 `README.md` |
| `tools/sbe-dev-mcp/` | 本地开发 MCP server（Go 标准库单二进制）：Luban 导表、Go 服务端测试、仓库一致性预检、Docker 部署与冒烟、Unity batchmode 编译检查 + EditMode 测试（`unity_verify`），见其 `README.md` |
| `docs/` | Agent 协作约定（issue tracker、domain 文档的组织方式） |
| `.scratch/` | 本地 issue tracker：各特性的 spec 与工单（`warehouse-online-v1`、`account-auth`、`exploration-online-v1` 等），markdown 文件即状态源 |

## 快速开始

**客户端**：Unity Hub 打开 `SBE-Client/`（首次导入较久），打开 Launcher 场景进 Play Mode。服务端不在线时客户端会自动重连，本地单机流程不受影响。

**服务端（开发，内存存储）**：

```bash
SBE-Server/tools/sync_config.bat   # 同步客户端配置表（改表或重导地图后重跑）
cd SBE-Server && go run ./cmd/sbe-server
```

**服务端（Docker 一键部署，MySQL + 服务器）**：

```bash
cd SBE-Server && docker compose up -d --build
go run ./cmd/smoke    # 冒烟：注册 → ping → 登录
```

**改配置表**：只改 `SBE-Design/Datas/` 源表，跑 `SBE-Design/gen_cli.bat`（或 sbe-dev MCP 的 `luban_export`），`Gen/` 下生成代码不可手改。

## 更多文档

- 研发路线与当前里程碑：`SBE-Client/Docs/Tech/TODO.md`
- 技术约定（Luban / 存档 / UI / 探索 AI）：`SBE-Client/Docs/Tech/`
- 服务端架构与部署细节：`SBE-Server/README.md`
- Agent 工作约定：`AGENTS.md`
