# 05: 客户端 RunService 域服务

**What to build:** 预备因子票：新网络域服务封装 start_run/encounter/loot 三消息——请求发起、超时判定、断线放行、错误码归一；拒绝语义以事件出口（供 UI 提示"网络延迟较大，请检查网络连接"）。不触任何 UI。EditMode 用 fake 连接测试。注册进网络域服务清单。

**Blocked by:** 04

**Status:** ready-for-agent

- [ ] EditMode 覆盖成功/超时/断线放行/错误码分支
- [ ] 超时阈值可配置
- [ ] 事件出口携带拒绝原因与目标点，供 UI 消费
- [ ] 不引入对 UI/流程组件的依赖（纯域服务）
