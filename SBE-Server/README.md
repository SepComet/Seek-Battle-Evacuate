# SBE-Server（局外状态全量上云）

Go + WebSocket + MySQL。实现 spec：根目录 `.scratch/warehouse-online-v1/spec.md`、`.scratch/meta-online-v2/spec.md` 与 `.scratch/account-auth/spec.md`。服务端拥有账号（用户名+密码，雪花 uid）、仓库、花名册与装备、出战队列、难度、携带计划和战绩；客户端 SaveData 是本地缓存。

## 快速开始（开发，内存存储）

```bash
# 1. 同步物品、全局与难度三张配置（从客户端 Luban 导出物拷贝；每次改表后重跑）
tools/sync_config.bat          # Windows
bash tools/sync_config.sh      # bash

# 2. 启动（数据在内存里，重启即清空）
go run ./cmd/sbe-server -addr 127.0.0.1:7777
```

## 部署（Docker Compose：MySQL + 服务器一体）

前提：本机装有 Docker Desktop（或任意 Docker 引擎）。

```bash
cd SBE-Server
docker compose up -d --build        # 首次运行会构建镜像并拉取 mysql:8.4

# 冒烟验证（注册 → ping → 登录）
go run ./cmd/smoke -addr 127.0.0.1:7777
```

栈内容：

- `mysql`（mysql:8.4）：数据持久化在命名卷 `mysql-data`；`migrations/0001~0004` 挂载到 `/docker-entrypoint-initdb.d`，**首次初始化**（数据卷为空时）自动建库应用迁移；3306 映射到宿主机仅供工具访问。
- `sbe-server`：多阶段构建的静态二进制 + Luban 导出配置；等 mysql 健康检查通过后启动，`-store mysql`，DSN 指向 compose 内部网络的 `mysql:3306`；7777 对外暴露。
- MySQL root 密码默认 `sbe-dev-2026`，可用环境变量 `MYSQL_ROOT_PASSWORD` 覆盖（compose 会同源注入 DSN 与健康检查）。

常用操作：

```bash
docker compose logs -f sbe-server   # 看服务器日志
docker compose down                 # 停止（数据卷保留）
docker compose down -v              # 停止并清空数据（慎用）
```

改动 Go 代码后 `docker compose up -d --build` 重建镜像即可；只改数据/迁移时注意迁移只在数据卷**首次**初始化时执行，已有卷需 `docker compose down -v` 后重启才会重放迁移。

## 手动部署（不用 Docker）

```bash
# 0. 新库依次执行四份迁移（0004 会清空 users/auth_tokens，旧匿名账号作废）
mysql -u root -p sbe < migrations/0001_init.sql
mysql -u root -p sbe < migrations/0002_loadout.sql
mysql -u root -p sbe < migrations/0003_warehouse_uid_per_user.sql
mysql -u root -p sbe < migrations/0004_account_auth.sql

# 1. 构建与启动（驱动已进 go.mod；如本机模块缓存没有依赖，见下方网络提示）
#    -worker-id：雪花 uid 铸号的实例编号（0~1023），多实例部署必须互异
go build -o bin/sbe-server ./cmd/sbe-server

./bin/sbe-server \
  -store mysql \
  -dsn "user:pass@tcp(127.0.0.1:3306)/sbe?charset=utf8mb4&parseTime=true" \
  -addr 0.0.0.0:7777 \
  -worker-id 1
```

### 网络提示（模块代理）

本机默认 GOPROXY（goproxy.cn）的 IPv6 路由不稳定会导致 `go: module ... connection forcibly closed`。
若拉取依赖失败，改用阿里云镜像（无需改全局配置）：

```bash
GOPROXY=https://mirrors.aliyun.com/goproxy/,direct go mod download
```

或持久化：`go env -w GOPROXY=https://mirrors.aliyun.com/goproxy,direct`。

## 测试

```bash
go test ./...
```

默认覆盖 core 校验、迁移、WebSocket 同步/重连、战绩排序/上限/幂等、runId 归属（同用户回放/跨用户拒绝）、账号注册/登录（重名冲突、错密拒绝）与真实配置导出格式。

MySQL 集成用例默认跳过，一键运行（需要本机有 MySQL 服务在跑）：

```bash
tools/mysql_integration_test.bat -user root -pass 你的密码   # Windows
bash tools/mysql_integration_test.sh -user root -pass 你的密码
```

脚本（`cmd/mysqltest`）会 DROP 并重建固定名称的一次性测试库 `sbe_integration_test`、依次应用四份迁移、再以 `SBE_TEST_MYSQL_DSN` 运行 `go test ./internal/mysqlstore -v` 并透传退出码；该库名固定且每次清空，绝不用于业务数据。用例覆盖 `user_loadout` 读写与清空、settle 元数据落库及幂等 history、跨用户 runId 拒绝与按用户铸号的 uid 共存（依赖 0003 复合主键）、账密注册登录（依赖 0004）。手动等价做法：在已执行四份迁移的独立测试库上设置 `SBE_TEST_MYSQL_DSN` 后运行 `go test ./internal/mysqlstore -v`。测试只清理自身新建账号的数据，不执行建库或迁移。

## 结构

| 目录 | 职责 |
| --- | --- |
| `cmd/sbe-server` | 入口：装配配置表、存储、HTTP/WS 监听 |
| `internal/protocol` | 线上消息结构（信封、操作集、快照） |
| `internal/core` | 纯函数核心：操作集应用（校验+原子合并）、迁移初始化 |
| `internal/itemtable` | 加载 Luban 导出的 tbitemconfig.json |
| `internal/globaltable` | 加载 tbglobalconfig.json 的队伍/背包上限、tbdifficultyconfig.json 的 tier 集合 |
| `internal/ws` | WebSocket 传输层（gorilla/websocket 薄包装） |
| `internal/server` | 会话与分派：rid 去重、register/auth/pull/migrate/settle/ops/history |
| `internal/memorystore` | 内存存储（开发/测试） |
| `internal/mysqlstore` | MySQL 存储 |
| `internal/snowflake` | 雪花 uid 铸号（41 位毫秒 \| 10 位 worker \| 12 位序列） |
| `internal/boot` | 存储装配工厂 |
| `migrations/` | 建表 SQL |

## 协议摘要

信封 `{"t":类型,"v":3,"rid":请求ID,...}`；响应 `{"t":"ok",...,"snapshot":{...}}` 或 `{"t":"err","rid":...,"code":...}`。旧版互连返回 `bad_message`，客户端与服务端需同步发版。

- `register{username,password}` → 注册账号，成功回新 token + `userId` + 快照；用户名 2~32 字符（trim 后）且全局唯一，重名返回 `conflict`，格式非法返回 `rejected`。
- `login{username,password}` → 账密登录，成功回新 token + `userId` + 快照；用户不存在与密码错误统一返回 `unauthorized`。
- `auth{token}` → 凭 token 断线重连，成功回 `userId` + 快照。`userId` 是雪花算法铸造的 64 位账号主键（uid），跨认证方式与断线重连保持不变，不是认证凭据。
- `pull` → 全量局外快照
- `migrate{migrate:{items[],characters[],party[],difficulty,carried[]}}` → 首登迁移（仅未初始化账号，一次性）
- `settle{runId,outcome,difficulty,seed,startedAt,endedAt,ops[]}` → 结算对账；`runId` 幂等（同用户重复回放首次结果，元数据保留首次值）；`runId` 已被其他用户占用时返回 `rejected`
- `ops{ops[]}` → 大厅零散操作；`rid` 去重防重试重复入账
- `history` → 需登录，返回 `history:[{runId,outcome,difficulty,seed,startedAt,endedAt}]`，按 startedAt 倒序最多 100 条；空列表允许省略 history 字段。普通快照不携带战绩。
- `ping` → 需登录，只返回 `{t:"ok",v:2,rid}`，不读取存储、不携带快照或战绩。客户端以单调时钟测量该请求的往返时间（含消息泵送时间），用于常驻网络状态显示。该字段与消息为协议 v2 的兼容扩展。

快照新增 `party[] / difficulty / carried:[{itemId,count}]`。空队列、难度 0、空携带均表示未配置；零值字段允许省略。存量 v1 账号没有 user_loadout 行时采用这三个零值；旧 settlements 元数据新列默认 0。战绩由 settlements 存储，登录后覆盖客户端历史缓存，不迁移本地旧战绩。

操作集（顺序执行，任一失败整批拒绝）：

- `add{tid,itemId,count,x,y,r}` 铸新行（uid 由服务器分配，批内可用 tid 引用）
- `del{uid,count?}` 删行/扣减
- `mov{uid,x,y,r}` 网格移位
- `rel{uid,x,y,r}` 卸下装备回网格
- `wear{cid,slot,uid|tid|itemId}` 穿上（同槽旧装备自动卸回原点）
- `recruit{cid}` 花名册新增
- `party{party:[cid...]}` 整队替换，顺序为出战序；允许空队，角色须在花名册、无重复、不超过全局人数上限
- `difficulty{difficulty:n}` 设定难度，0 或难度表 tier 集合中的值
- `carry{carry:[{itemId,count}...]}` 整表替换，物品须存在，每条数量 1~stackLimit，条数不超过背包格数

客户端固定发射序在既有操作末尾追加 party → difficulty → carry；recruit 在 party 前，故同批可引用新招募角色。服务端沿用通用顺序应用通道；客户端结算只发送资产差异，不发送这三个战备操作，避免局内临时状态回写战备。

## 与 spec 的实现状态

网络恢复后已按 spec 原案切换到 gorilla/websocket，MySQL 驱动也已进 go.mod（无需构建标签）。
Go 侧配置加载直接读取 gen_cli 导出的 `tbitemconfig.json`、`tbglobalconfig.json`、`tbdifficultyconfig.json`（见 `tools/sync_config`），不生成 Luban Go 代码。当前实际导出的全局配置为 camelCase 单元素数组，难度表以 `tier` 为键。入口 `-items / -global / -difficulties` 可指定文件路径；缺失或非法配置使启动失败。

客户端编译、EditMode 与真实运行验收按仓库纪律由已开启的 Editor 或用户执行；Go 测试通过不能替代 Unity 验收或真实 MySQL 验证。
