# SBE-Server（仓库上云 v1 服务端）

Go + WebSocket + MySQL。实现 spec：`.scratch/warehouse-online-v1/spec.md`。

## 快速开始（开发，内存存储）

```bash
# 1. 同步物品配置（从客户端 Luban 导出物拷贝；每次改表后重跑）
tools/sync_config.bat          # Windows
bash tools/sync_config.sh      # bash

# 2. 启动（数据在内存里，重启即清空）
go run ./cmd/sbe-server -addr 127.0.0.1:7777
```

## 生产部署（MySQL）

```bash
# 0. 建库并执行 migrations/0001_init.sql
mysql -u root -p sbe < migrations/0001_init.sql

# 1. 拉取 MySQL 驱动（一次性，需要网络）
go get github.com/go-sql-driver/mysql

# 2. 以 sbe_mysql 标签构建
go build -tags sbe_mysql -o bin/sbe-server ./cmd/sbe-server

# 3. 启动
./bin/sbe-server \
  -store mysql \
  -dsn "user:pass@tcp(127.0.0.1:3306)/sbe?charset=utf8mb4&parseTime=true" \
  -addr 0.0.0.0:7777
```

> 未加 `-tags sbe_mysql` 构建时只有内存存储（`-store mysql` 会报错）。默认构建保持零第三方依赖，可离线编译与测试。

## 测试

```bash
go test ./...
```

## 结构

| 目录 | 职责 |
| --- | --- |
| `cmd/sbe-server` | 入口：装配配置表、存储、HTTP/WS 监听 |
| `internal/protocol` | 线上消息结构（信封、操作集、快照） |
| `internal/core` | 纯函数核心：操作集应用（校验+原子合并）、迁移初始化 |
| `internal/itemtable` | 加载 Luban 导出的 tbitemconfig.json |
| `internal/ws` | 最小 RFC 6455 WebSocket（握手、帧编解码、ping/pong） |
| `internal/server` | 会话与分派：rid 去重、register/auth/pull/migrate/settle/ops |
| `internal/memorystore` | 内存存储（开发/测试） |
| `internal/mysqlstore` | MySQL 存储（build tag `sbe_mysql`） |
| `internal/boot` | 存储装配工厂 |
| `migrations/` | 建表 SQL |

## 协议摘要

信封 `{"t":类型,"v":1,"rid":请求ID,...}`；响应 `{"t":"ok",...,"snapshot":{...}}` 或 `{"t":"err","rid":...,"code":...}`。

- `register{device}` → 新 token + 快照；`auth{token}` → 快照
- `migrate{items[],characters[]}` → 首登迁移（仅未初始化账号，一次性）
- `settle{runId,outcome,ops[]}` → 结算对账；`runId` 幂等（重复回放首次结果）
- `ops{ops[]}` → 大厅零散操作；`rid` 去重防重试重复入账

操作集（顺序执行，任一失败整批拒绝）：

- `add{tid,itemId,count,x,y,r}` 铸新行（uid 由服务器分配，批内可用 tid 引用）
- `del{uid,count?}` 删行/扣减
- `mov{uid,x,y,r}` 网格移位
- `rel{uid,x,y,r}` 卸下装备回网格
- `wear{cid,slot,uid|tid|itemId}` 穿上（同槽旧装备自动卸回原点）
- `recruit{cid}` 花名册新增

## 与 spec 的两处实现偏差（环境受限，均已确认可后补）

1. **WebSocket 库**：spec 定 gorilla/websocket，因构建环境无外网、模块代理不可达，改为标准库手写最小实现（`internal/ws`）。行为以 RFC 6455 为准、有回环与回显测试；将来可整体替换。
2. **Go 侧配置加载**：spec 定 Luban Go 代码生成，因生成物依赖 Luban 的 Go 运行时（外部模块），改为直接读取 gen_cli 导出的 `tbitemconfig.json`（同源生成物，见 `tools/sync_config`）。需要强类型共享时再切回。
