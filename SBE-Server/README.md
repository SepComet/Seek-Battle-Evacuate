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

# 1. 构建与启动（驱动已进 go.mod；如本机模块缓存没有依赖，见下方网络提示）
go build -o bin/sbe-server ./cmd/sbe-server

./bin/sbe-server \
  -store mysql \
  -dsn "user:pass@tcp(127.0.0.1:3306)/sbe?charset=utf8mb4&parseTime=true" \
  -addr 0.0.0.0:7777
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

## 结构

| 目录 | 职责 |
| --- | --- |
| `cmd/sbe-server` | 入口：装配配置表、存储、HTTP/WS 监听 |
| `internal/protocol` | 线上消息结构（信封、操作集、快照） |
| `internal/core` | 纯函数核心：操作集应用（校验+原子合并）、迁移初始化 |
| `internal/itemtable` | 加载 Luban 导出的 tbitemconfig.json |
| `internal/ws` | WebSocket 传输层（gorilla/websocket 薄包装） |
| `internal/server` | 会话与分派：rid 去重、register/auth/pull/migrate/settle/ops |
| `internal/memorystore` | 内存存储（开发/测试） |
| `internal/mysqlstore` | MySQL 存储 |
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

## 与 spec 的实现状态

网络恢复后已按 spec 原案切换到 gorilla/websocket，MySQL 驱动也已进 go.mod（无需构建标签）。
剩余一项保留决策：Go 侧配置加载直接读取 gen_cli 导出的 `tbitemconfig.json`（Luban 同源生成物，
见 `tools/sync_config`）而非 Luban Go 代码生成——JSON 加载已满足 v1 校验需求，待服务器需要
读更多表（掉落校验、商店）时再评估切换。
