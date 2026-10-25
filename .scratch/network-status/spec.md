Status: ready-for-human

# 常驻网络与登录状态 UI

## 范围

新增 NetworkStatusForm（115），从登录阶段起常驻右下角，覆盖大厅、功能页、场景过渡与局内。展示连接/登录状态、服务端账号唯一 userId、当前应用层 RTT 和最近 30 秒均值。只读、点击穿透，不增加局外经济或玩法能力。

## 实现契约

- 协议仍 v2，register/auth 成功回包新增 userId:int64，源于账号存储主键；ping 需已登录，仅回 ok/v/rid，不读写存储，不回快照。
- NetComponent 暴露 ConnectionState（Offline/Connecting/LoggingIn/Online/LoginFailed）、UserId、Latency；登录未完成或断线 UserId=0，不展示 token/设备 UUID。
- WarehouseSyncService 使用独立 ping rid 和 NetworkLatencyMonitor，2 秒探测，5 秒超时，Stopwatch 单调时间不受游戏暂停影响；不占经济操作锁，不在待响应 TCS 字典累积请求。
- 最近 30 秒窗口提供最新/平均/最小/最大/样本数；超时、断线、重登清旧窗口，迟到响应不污染新窗口；UI 每 0.25 秒刷新。TIMEOUT 与无样本有区别，后续成功探测恢复数值。
- UI 使用单实例独立组 NetworkStatus，depth=3，加 Form 原始深度 2000，最终约 32000。现有组深度系数 10000，不能直接升到组4而溢出 Canvas 范围。
- ProcedureLogin 打开 UI；Menu/Main 离开使用 CloseSceneUIForms 保留常驻层。没有在各业务页重复创建浮层。
- 文案来自 FormatText 源表，Form/View 源码由 UIAssetsTools 模板生成；prefab 在已开启 Editor 中创建，UISerializationItem 登记六个 View 引用并由生成工具绑定。未主动调用编译或 Play Mode。

## 验证

- sbe-dev server_test：65 通过、0 失败、1 跳过（未配置真实 MySQL 测试库）；11 个包零失败。见 server-and-consistency.jsonl。
- 已开启 Editor 的 MCP EditMode：53/53 通过、0 失败，包含 NetworkLatencyMonitorTests、WarehouseSyncServiceTests 和 EconomyDiffTests；见 client-retest.json。首轮异步请求测试断言过早，等待请求实际发出后复跑通过，生产逻辑无需修改。
- sbe-dev consistency_check：0 违规，工具汇总 4 警告（现有第三方 UniTask 程序集名称引用等）；git diff --check 无空白错误。不可把静态/编辑器预览当作 Play Mode 验收。
- prefab-validation.json：已有 Editor 原生绑定检查 0 错误，View 六引用齐全，无 missing script；CanvasGroup 与全部 Graphic 不拦截点击。
- preview.png：已有 Editor 的离线静态预览，已检查文本、对齐与可读性；不是运行截图。
- 配置源三份工作簿保留原值，仅新增网络状态配置；sbe-dev luban_export 成功，导出 tbuiformconfig/tbformattext/UIFormType；配置核验记录见同目录。
- 潜在编译/运行缺口：Editor 已自然导入新类型，并由原生工具检查引用，未发现明确编译问题；没有主动请求脚本编译或运行客户端构建。真正的登录状态变化、分辨率布局、常驻排序与场景切换仍须用户 Play Mode 验收。

## 人工验收

- [ ] 连接不可达服务器时，登录阶段可见连接/离线状态；恢复服务器后自动登录并显示账号 ID。
- [ ] 同一账号重连/重新注册凭证后 ID 保持稳定；其他账号不同；界面不泄露 token。
- [ ] 在线后 RTT 与30秒均值周期变化，断线立即隐藏旧ID/延迟；超时显示 TIMEOUT，恢复后更新。
- [ ] 大厅→战备/仓库→局内→大厅，以及场景黑幕期间状态层保持可见、无重复、点击穿透。
- [ ] 暂停游戏或改变 timeScale，采样与刷新仍正常；不同分辨率下检查右下角与操作按钮的遮挡及文字。

## 已知边界

- RTT 为应用层往返耗时，包含帧消息泵调度与处理；不区分上传/下载或 ICMP 延迟。
- ping 超时反映探测不可用，不直接声称账号注销；UI 同时展示传输/登录状态与超时提示。
- 账号仍沿用匿名设备账号机制；不实现账号切换、名称修改或凭证 UI。
