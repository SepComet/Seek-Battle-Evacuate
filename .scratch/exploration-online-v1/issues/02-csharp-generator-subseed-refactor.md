# 02: C# 生成器子种子化重构

**What to build:** 预备因子票：生成算法从单一顺序随机流改为子种子结构——每个随机消费点从 hash(masterSeed, kind, index) 派生独立子种子，PRNG 换为双端易精确实现的可移植算法。行为保持不变（同种子同图，既有流程与场景调试不受影响），并为客户端暴露摆位出口（敌人存在性与威胁等级、箱子存在性）。战斗侧随机源完全不动。

**Blocked by:** None (can start immediately)

**Status:** ready-for-agent

- [ ] EditMode 生成测试改为固定种子向量断言，全量回归绿
- [ ] 同一 masterSeed 两次生成结果逐位一致；各子种子互不影响（改物资配置不影响敌人摆位）
- [ ] 可移植 PRNG 有独立单测
- [ ] 摆位出口可独立调用，输出与完整生成的对应 stage 一致
