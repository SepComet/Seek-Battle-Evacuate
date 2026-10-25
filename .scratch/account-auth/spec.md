# 账号密码体系（雪花 uid + 用户名密码登录）

> 状态：已实现并经 MCP 验证（服务端 76 用例全绿；客户端 EditMode 全量 266 条仅存量 fog 环境失败）。
> 人工验收清单见文末，不以自动化通过视为人工验收完成。

## 背景与目标

仓库上云 v2（`meta-online-v2`）用设备 UUID 匿名注册，换设备即丢号且无法找回。本 spec 把账号体系升级为用户名+密码：

- 服务端以**雪花算法铸造的 64 位 uid** 为用户主键（无自增），唯一记录一名玩家。
- 注册=创建账号（用户名全局唯一），登录=账密校验；token 静默续登机制保留。
- 客户端提供登录/注册表单；缓存 token 有效时无感续登，失效才弹表单。
- 协议 v2 → **v3**（register 语义破坏性变更，旧版互连 bad_message 干净断代，同仓库同步发版）。

## 服务端设计决策

1. **雪花 uid**（`internal/snowflake`）：41 位毫秒时间戳 | 10 位 worker | 12 位序列，纪元 2026-01-01。worker 由 `-worker-id` 旗标注入（默认 1，多实例必须互异）；时钟回拨返回错误拒绝铸号（防重复 uid）。老自增 id（1,2,3…）与雪花值域（约 1e17）永不冲突。
2. **users 表重建**（migration `0004_account_auth.sql`）：`uid BIGINT PK`（无自增）+ `username VARCHAR(32) UNIQUE` + `password_hash` + `initialized`。已确认决策：**旧匿名账号与名下数据作废**，auth_tokens 一并清空；warehouse_items/settlements 等残留孤儿行属 dev 垃圾，正式部署建议新库。
3. **凭据校验与哈希**（`internal/store/credentials.go`）：用户名 trim 后 2~32 字符、无控制字符、区分大小写唯一；密码 6~64 字符。哈希 `pbkdf2-sha256`（stdlib `crypto/pbkdf2`，210k 迭代 ≈ 服务端 100ms/次，原型期安全/体验平衡；格式含 iter 字段，提强度只改常量，旧哈希仍可校验）。校验用常量时间比较。登录时"用户不存在"与"密码错误"统一返回 `ErrInvalidCredentials`，防用户名枚举。
4. **错误码**：重名 → 新增 `conflict`；凭据错误 → `unauthorized`；格式非法 → `rejected`。
5. **注册并发**：用户名唯一由 `uk_users_username` 兜底，1062 冲突映射为 ErrUsernameTaken（与 runId 归属同款手法）；v2 修复的 loadForUpdate 行锁、settle 归属过滤继续有效。
6. **已知边界**：ws 明文传输密码（上线走 wss/反代 TLS）；登录无速率限制与锁定（公网部署前需补）；token 永不过期（沿用 v2）。

## 客户端设计决策

1. **AccountSession**（替代 DeviceIdentity）：`Save/account_session.json` 存 uid/token/username；密码不落盘；token 被拒 `ClearToken`（保留用户名预填）；首次加载删除旧 `device_identity.json`。
2. **登录流**（`ProcedureLogin`）：缓存 token → 每 2s 静默续登（网络故障可重试）→ `AuthRejected`（无 token/被拒）→ 打开 `LoginForm` → 轮询 OnlineReady 进大厅。SuspendAutoLogin 机制不变。
3. **登录结果三态** `NetLoginOutcome`：Success / AuthRejected（附服务器错误码，UI 映射文案）/ Failed（网络，可重试）。登录 UI 文案走 FormatText 表（AccountError* keys），不在代码硬编码。
4. **LoginForm**：`UI/Login/` 生成 Form/View + 手写 Logic；prefab 由 `Utility/UI/Build Login Form Prefab`（UIPrefabBuilder 新增）构建，控件登记 usernameInput/passwordInput/loginButton/registerButton/errorText，中文标签用 MainTMPFont 的 TMP 文本（LegacyRuntime.ttf 无 CJK 字形）。
5. 客户端预校验与服务端规则一致（trim/长度），但服务端仍是最终裁决。

## 实现记录（2026-10-25）

- 服务端：snowflake 包 + store 接口（Register/Login/Auth）+ credentials.go + memorystore/mysqlstore 双实现 + protocol v3 + boot/main 装配 + migration 0004 + README。
- 配置表：`__enums__.xlsx` UIFormType 加 LoginForm=116、`UIFormConfig` 加行、`FormatText` 加 8 个 Account* 文案行；luban_export 完成。
- 客户端：NetProtocol v3 镜像、AccountSession、WarehouseSyncService（TryTokenLoginAsync/TryCredentialLoginAsync）、NetComponent、ProcedureLogin 两段式登录流、LoginForm.Logic.cs、UIPrefabBuilder 新增登录表单构建菜单。
- prefab 构建与 View/Form 生成、组件挂载经 mcp-for-unity 在已开启 Editor 内完成。
- 测试：服务端 76 通过（snowflake/credentials/memorystore/ws 全流程 v3）；客户端 EditMode 266 条中除存量 fog 环境失败外全绿，含新增注册重名/错密/token 续登/AccountSession 用例。
- 测试附带修正：`UnauthorizedPing_ClearsCurrentAccountIdentity` 的探测计数断言对齐 NetRpc 拆分后的语义（rpc 层不感知登录态，身份门禁由 NetComponent OnlineReady 负责）。

### 部署闭环（2026-10-25 追加）

- Docker Compose 部署栈落地：`Dockerfile`（多阶段构建）+ `docker-compose.yml`（mysql:8.4 + sbe-server 同网络、迁移经 docker-entrypoint-initdb.d 首次自动应用、数据卷持久化、MySQL root 密码默认 sbe-dev-2026 可用 MYSQL_ROOT_PASSWORD 覆盖）；冒烟探针 `cmd/smoke`；一键集成测试 `cmd/mysqltest` + `tools/mysql_integration_test.(bat|sh)`。
- **冒烟测试抓到真 bug**：`mysqlstore` 内 4 处 SQL 残留旧列名 `users.id`（loadForUpdate/lockUser/Migrate/UPDATE initialized；另有测试 cleanup 2 处），0004 改雪花主键后即失效——注册成功但 Load 报 Error 1054。已全部改为 `uid`，对容器化真库复跑通过。教训印证：内存测试结构上不可能覆盖 schema 级错误，真库集成用例是必需品。
- 对容器化 MySQL 实测：`cmd/mysqltest` 全绿（TestMySQLLoadoutSettlementHistory / TestMySQLSettleOwnershipAndPerUserUID），**真库验收闭环**；`cmd/smoke` 对容器服务器全绿（雪花 uid 正确铸号）。
- sbe-dev MCP 新增 `deploy`（一键 compose build+up+force-recreate+等待监听）与 `smoke`（封装 cmd/smoke）工具（server v0.2.0），部署冒烟一句话完成。
- 网络注意：本机直连 docker.io 走 IPv6 黑洞，基础镜像（golang:1.27-alpine / alpine:3.21）需挂代理拉取；构建内 go mod download 走 goproxy.cn 不受影响。

### 退出登录（2026-10-25 追加）

- LobbyForm 左列 warehouse 按钮下方新增 `logoutButton`（300×64，页头 BackButton 同款深色底 + MainTMPFont 中文标签"退出登录"），prefab 经 mcp-for-unity execute_code 修改并重生成 View。
- 流程：LobbyForm 点击 → `LogoutRequestedEventArgs` → `ProcedureMenu` 编排：`NetComponent.Disconnect()` 断开 → `AccountSession.ClearToken()`（用户名保留预填）→ `SaveComponent.ResetLocalCache()`（删本地存档文件 + 回到新开局默认数据）→ `ChangeState<ProcedureLogin>` 走完整登录流。
- **本地缓存必须重置的原因**：否则新注册账号的 migrate 会把上一账号的本地残留状态上传（"首设备定初始状态"语义的副作用）；服务器是唯一真源，重登任意账号由快照恢复，删除缓存零损失。
- 已知边界：服务端 token 不吊销（协议 v3 无 logout 消息，旧 token 在服务器侧仍有效但被客户端丢弃）；登出无确认对话框（误点代价仅为重新登录，服务器数据无损）。

## 人工回填清单

- [ ] Play Mode：注册新账号 → 进大厅；重登（缓存 token 无感续登，不弹表单）；登出态删 `account_session.json` 后重登弹表单；错误密码/重名提示文案正确。
- [ ] Play Mode：断线重连（kill 服务端再拉起）自动恢复；首登迁移在新账号上正常。
- [ ] 真库：一键跑 `tools/mysql_integration_test.bat -user root -pass 你的密码`（重建一次性测试库 sbe_integration_test + 应用 0001~0004 + 运行 mysqlstore 集成用例，含跨用户 runId 拒绝、uid 共存、账密注册登录）；需要本机 MySQL 服务在跑；不以跳过视为通过。——2026-10-25 已由 agent 对容器化 MySQL（compose 栈）实测两用例 PASS，待人工确认后勾选。
- [ ] 多实例部署前确认 `-worker-id` 互异。
- [ ] 工作区中存在非本任务的未提交改动（NetRpc.cs/NetworkLatencyMonitor.cs 为未跟踪新文件，服务类拆分、HomeForm/LoadoutForm/WarehouseForm/MainSettlementState 修改），提交时需与账号体系改动分开整理或经确认一并提交。
