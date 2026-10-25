# 04: 服务器 run 会话与协议 v4

**What to build:** 协议版本升 v4。新增 start_run（服务器选种子、显式出生/撤离点，幂等——已有活跃 run 返回现状）、encounter（校验点存在并返回队伍构成，幂等）、loot（校验并标记消耗，按 (runId, pointId) 幂等）；settle 扩展对账（带出物资 ⊆ 开箱所得 ∪ 携带入场 ∪ 战斗所得，战斗所得本阶段信任客户端）；单用户单活跃会话（内存态）与 TTL 清理。fake WS 客户端全流程测试。

**Blocked by:** 03

**Status:** ready-for-agent

- [ ] start_run 幂等：已有活跃 run 返回现状（含已消耗点集合），新开局分配种子
- [ ] encounter/loot 覆盖点不存在/已消耗/无活跃 run 分支；重试返回相同结果
- [ ] settle 对账开箱所得；战斗所得不校验（边界记录在案）
- [ ] TTL 清理超时未结算 run 有测试
- [ ] fake WS 全流程 start_run→encounter→loot→settle 绿
