# 01: 地图与配置数据上服务器

**What to build:** 编辑器菜单把 MapDefinition 地图资产序列化为 JSON 进 DataTables，sync_config 增补该 JSON 与资源点、敌队伍两张配置表，服务器启动即可加载生成所需的全套数据。导出时校验硬性不变量：每个敌人点必须归属某个房间，违反直接报错。服务器加载沿用"直接读 gen_cli 导出 JSON"的既定决策。

**Blocked by:** None (can start immediately)

**Status:** ready-for-agent

- [ ] 导出产出的 JSON 覆盖房间矩形、物资点/敌人点/出生与撤离候选列表
- [ ] sync_config 同步后服务器启动加载成功，加载用例跑真实导出 JSON
- [ ] 资源点配置（含掉落权重）与敌队伍配置（含威胁池）在服务器可查询
- [ ] 敌人点不在任何房间内时，导出侧报错拒绝产出
