Status: ready-for-human

# P1-M2 经济系统与数据结构扩展（余额上云）

2026-10-28 实现。范围：credits 余额服务端权威 + 价格配表；不含商城界面与买卖协议（P1-M3）。

## 决策记录（grilling 三轮收敛）

- TODO 原文的「SaveData 加金币字段 + 本地 EconomyComponent」方案作废：联网改造后服务器为唯一真源，P1-M1 已承诺「不创建本地货币，余额来源随 P1-M2 接入服务端」。P1-M2 改写为服务端权威实现，TODO.md 范围文本已同步。
- 单一 `credits` 钱包（Go int64 / C# long，快照恒下发非 omitempty）；货币道具（铜币 5001~全币 5005）保持普通物品不动，变现换余额随 P1-M3 商店出售接入。
- 发放：注册时由服务端写入 `tbglobalconfig.initialCredits`（=1000），客户端永不上传余额；migrate 全量上传链路（初始仓库/角色/战备）保持现状不动。
- 存量账号由 migration `0005_credits.sql` 回填 1000；已对运行中的 Docker 库手工执行 ALTER+UPDATE（`docker-entrypoint-initdb.d` 只在空数据卷时自动应用，存量卷不重灌）。
- ItemConfig 增 `sellPrice`（全表=价值）/`buyPrice`（消耗品与 I/II 档武器防具=价值×2，战利品/货币道具/III 档及以上装备=0，83 件中 21 件可购买）；装备档位按名称罗马数字解析而非稀有度（阔剑 I 是 Blue）。服务端本里程碑不读价格字段（购买校验随 P1-M3）。
- 页头显示复用 M1 的 `LobbyCurrencyPending` FormatText key（值改为 `CREDITS: {0:N0}`，快照未落地传 `—`），五个 prefab 零改动；`LobbyPageHeader.RefreshCurrency` 逐帧按已渲染值缓存刷新，断线回退占位。

## 改动清单

- 服务端（SBE-Server）：`migrations/0005_credits.sql`；协议 v5（`protocol.go` 版本与 Snapshot.Credits、`protocol_test.go` 逐字断言更新+新增 credits wire 用例）；`core.State.Credits/Clone/ToSnapshot`、`core.Limits.InitialCredits`；`globaltable` 加载并校验 initialCredits>0；`mysqlstore`（Open/Register/Load/snapshotToState 携带 credits）与 `memorystore`（构造注入+注册发放）；`boot.OpenStore`、`cmd/sbe-server/main.go` 接线与启动日志 `initCredits=`；`cmd/smoke` 断言注册快照 credits>0。
- 客户端（SBE-Client）：`NetProtocol.cs` v5 + `NetSnapshot.credits`；`WarehouseSyncService` 增 `Credits/HasCredits`（ApplySnapshot 落地、断线清回），余额只落内存不写本地缓存；`LobbyPageHeader` 增 RefreshCurrency；EditMode：`WarehouseSyncServiceTests` 增 FakeServer 注册发放语义与 `LoginGrantsInitialCreditsAndSnapshotKeepsItFresh` 用例，`RunServiceTests` v4→v5 断言更新。
- 配置（SBE-Design）：`ItemConfig_物品配置.xlsx` 四 sheet 加 SellPrice/BuyPrice 两列；`GlobalConfig_全局配置.xlsx` 加 initialCredits=1000；`FormatText_格式化文本.xlsx` 的 LobbyCurrencyPending 改格式串；luban_export 重导（Gen + DataTables）。

## 自动化验证（2026-10-28）

- `server_test`：231 通过 0 失败（2 跳过为需 DSN 的 MySQL 集成，另行通过）；新增注册发放/迁移不动余额/结算与 ops 不动余额/幂等回放 credits 往返用例。
- MySQL 真库集成（`SBE_TEST_MYSQL_DSN` 指向 Docker 库）：TestMySQLLoadoutSettlementHistory、TestMySQLSettleOwnershipAndPerUserUID 全过（含 credits 断言）。
- EditMode 全量 batchmode（unity_verify 同款命令）：376 用例 374 绿；2 失败均为存量——fog（Tilemap 无 Grid，已知）与 `PendingEconomicFlush_DoesNotBlockLatencyProbe`（见已知边界）。
- `consistency_check`：0 违规。
- `deploy` + `smoke`：重建后启动日志 `initCredits=1000`；smoke 注册返回 `credits=1000`，ping/login 通过。
- 存量库迁移：users 表 10+ 个既有账号 credits 全部回填 1000。

## 已知边界

- `PendingEconomicFlush_DoesNotBlockLatencyProbe` 在 batchmode 下确定性失败（扣住 Ops 应答后 10 帧内 flush 未发出）。已用 git show HEAD 还原本里程碑全部改动复跑验证：HEAD 同样失败，属存量问题而非本里程碑回归；该用例此前仅在 Editor 内验证过。AGENTS.md 中「存量失败只有 fog」的记录待补。
- settle 幂等回放：升级前落盘的 settlements.snapshot_json 不含 credits，跨版本回放会短暂把余额显示为 0，下次登录拉取即纠正；开发栈单机部署，实际影响可忽略。
- sync_config.sh 在本机 Git Bash 下出现过一次 cp 静默未生效（[OK] 已打印但目标 mtime 未变），已手工拷贝六份配置；未查明根因，复现时优先比对目标文件 mtime。

## 人工 Play Mode 验收（未执行，禁止代勾）

- [ ] 新注册账号：大厅/战备/整备/仓库页头显示 `CREDITS: 1,000`，重登后不变。
- [ ] 存量账号（如 1001）：升级后页头显示 1,000，仓库/角色/战备数据不受影响。
- [ ] docker stop / start sbe-server 断连重连：页头先回 `CREDITS: —`，重连后恢复 1,000。
- [ ] 完成一局撤离结算后余额保持 1,000（本里程碑无余额变动路径）。
- [ ] Supply 入口仍禁用，不出现任何虚构余额。
