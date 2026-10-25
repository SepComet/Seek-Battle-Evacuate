using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using SepCore.Entity;
using SepCore.Net;
using UnityEngine;

namespace SepCore.Tests
{
    /// <summary>
    /// 断线重连恢复流程测试（ticket 07）。
    /// 覆盖两块可静态测的纯逻辑：
    /// 1. <see cref="RunResumeEvaluator"/>：恢复决策（种子一致应用 / 不一致失效 / resume 被拒失效 / 超时不断失效）。
    /// 2. <see cref="ResourcePointRecovery"/>：把服务器回放的已开箱清单应用到物资点数据上。
    /// NetComponent 的 false→true 重上线事件与 ProcedureMain 的触发接线依赖 Unity 组件生命周期与
    /// GameEntry 场景链路，EditMode 无法真实驱动（NetComponent.Awake 需要场景组件、登录走真实 WebSocket），
    /// 只能留待 Play Mode 人工验收；本文件把事件之外的全部决策与应用逻辑测全。
    /// </summary>
    [TestFixture]
    public class RunResumeTests
    {
        [OneTimeSetUp]
        public void SetUpHelpers()
        {
            // EditMode 下没有 GameEntry 启动链，手动注册 UGF 的日志 helper
            // （恢复决策的种子不一致路径会走 GameFramework 的 Log.Error）。
            GameFramework.GameFrameworkLog.SetLogHelper(new UnityGameFramework.Runtime.DefaultLogHelper());
        }

        private const long LocalSeed = 123456789L;

        #region 恢复决策（RunResumeEvaluator）

        [Test]
        public void Evaluate_ResumeRejected_TreatsRunAsInvalid()
        {
            // resume 被拒 = 服务器无活跃 run = 本局已失效，不允许以 resume=false 重开新局。
            StartRunResult result = StartRunResult.Failed(RunRequestOutcome.Rejected, NetErrCode.Rejected, "no active run");

            RunResumeDecision decision = RunResumeEvaluator.Evaluate(result, LocalSeed);

            Assert.That(decision.Action, Is.EqualTo(RunResumeAction.RunInvalid));
            Assert.That(decision.Result, Is.EqualTo(result));
        }

        [Test]
        public void Evaluate_Disconnected_DoesNotInvalidateRun()
        {
            // 断线放行：本局数据层仍在，恢复尝试就此结束，不回大厅。
            StartRunResult result = StartRunResult.Failed(RunRequestOutcome.Disconnected, null, null);

            RunResumeDecision decision = RunResumeEvaluator.Evaluate(result, LocalSeed);

            Assert.That(decision.Action, Is.EqualTo(RunResumeAction.NetworkDown));
        }

        [Test]
        public void Evaluate_Timeout_DoesNotInvalidateRun()
        {
            // 超时与断线同语义：不失效、结束本次恢复尝试，玩家继续本地游玩。
            StartRunResult result = StartRunResult.Failed(RunRequestOutcome.Timeout, null, null);

            RunResumeDecision decision = RunResumeEvaluator.Evaluate(result, LocalSeed);

            Assert.That(decision.Action, Is.EqualTo(RunResumeAction.NetworkDown));
        }

        [Test]
        public void Evaluate_SeedMatches_ReturnsApply()
        {
            StartRunResult result = StartRunResult.Ok("run-abc", LocalSeed,
                new NetVec2(1, -16), new NetVec2(30, -3),
                new List<string> { "r0" },
                new List<NetLootRecord> { LootRecord("r0", 5001, 5003) });

            RunResumeDecision decision = RunResumeEvaluator.Evaluate(result, LocalSeed);

            Assert.That(decision.Action, Is.EqualTo(RunResumeAction.Apply));
            Assert.That(decision.Result, Is.EqualTo(result));
        }

        [Test]
        public void Evaluate_SeedMismatch_TreatsRunAsInvalid()
        {
            // 服务器认为活跃 run 的种子与本地不一致（理论不该发生）：不静默继续，按失效处理。
            StartRunResult result = StartRunResult.Ok("run-abc", 987654321L,
                new NetVec2(1, -16), new NetVec2(30, -3), new List<string>(), new List<NetLootRecord>());

            RunResumeDecision decision = RunResumeEvaluator.Evaluate(result, LocalSeed);

            Assert.That(decision.Action, Is.EqualTo(RunResumeAction.RunInvalid));
        }

        #endregion

        #region 消耗状态应用（ResourcePointRecovery）

        [Test]
        public void Apply_ContentsNotReady_FillsContentsAndDropsAll()
        {
            ResourcePointData point = PendingPoint("r0", 1);
            List<ResourcePointData> points = new List<ResourcePointData> { point };
            List<NetLootRecord> looted = new List<NetLootRecord> { LootRecord("r0", 5001, 5003) };

            ResourcePointRecovery.Apply(points, looted);

            Assert.That(point.ContentsReady, Is.True);
            Assert.That(point.ItemIds.ToArray(), Is.EqualTo(new[] { 5001, 5003 }));
            Assert.That(point.DroppedItemCount, Is.EqualTo(point.ItemCount));
            Assert.That(point.IsCompleted, Is.True);
        }

        [Test]
        public void Apply_MultipleRecords_AppliesEachByPointId()
        {
            ResourcePointData r0 = PendingPoint("r0", 1);
            ResourcePointData r1 = PendingPoint("r1", 2);
            ResourcePointData untouched = PendingPoint("r2", 3);
            List<NetLootRecord> looted = new List<NetLootRecord> { LootRecord("r1", 5301), LootRecord("r0", 5001, 5003) };

            ResourcePointRecovery.Apply(new List<ResourcePointData> { r0, r1, untouched }, looted);

            Assert.That(r0.ItemIds.ToArray(), Is.EqualTo(new[] { 5001, 5003 }));
            Assert.That(r1.ItemIds.ToArray(), Is.EqualTo(new[] { 5301 }));
            Assert.That(r0.IsCompleted, Is.True);
            Assert.That(r1.IsCompleted, Is.True);
            // 未开箱点保持内容待定，仍可正常交互。
            Assert.That(untouched.ContentsReady, Is.False);
            Assert.That(untouched.IsCompleted, Is.False);
        }

        [Test]
        public void Apply_ContentsAlreadyReadyWithMatchingItems_Succeeds()
        {
            // 本地已开过箱（响应已灌入）后重连：校验清单一致即不抛错、不重复应用。
            ResourcePointData point = PendingPoint("r0", 1);
            point.ApplyItems(new[] { 5001, 5003 });
            point.DroppedItemCount = 1; // 本地只掉了一半，恢复不替玩家掉落

            ResourcePointRecovery.Apply(new List<ResourcePointData> { point },
                new List<NetLootRecord> { LootRecord("r0", 5001, 5003) });

            Assert.That(point.ItemIds.ToArray(), Is.EqualTo(new[] { 5001, 5003 }));
            Assert.That(point.DroppedItemCount, Is.EqualTo(1));
        }

        [Test]
        public void Apply_ContentsReadyButItemsMismatch_Throws()
        {
            // 本地已就绪但清单与服务器回放不一致：状态错乱，不静默继续，直接抛错（调用方捕获后按失效处理）。
            ResourcePointData point = PendingPoint("r0", 1);
            point.ApplyItems(new[] { 5001 });

            Assert.Throws<InvalidOperationException>(() => ResourcePointRecovery.Apply(
                new List<ResourcePointData> { point },
                new List<NetLootRecord> { LootRecord("r0", 5001, 5003) }));
        }

        [Test]
        public void Apply_RecordHasNoMatchingEntity_Throws()
        {
            // 匹配不到实体在理论外（服务器只回放本局生成点）：按装配缺陷报错，不静默跳过。
            ResourcePointData point = PendingPoint("r1", 2);

            Assert.Throws<InvalidOperationException>(() => ResourcePointRecovery.Apply(
                new List<ResourcePointData> { point },
                new List<NetLootRecord> { LootRecord("r0", 5001) }));
        }

        [Test]
        public void Apply_DuplicateRecordForSamePoint_Throws()
        {
            // 服务器同一点回放两条不同清单的记录属于协议缺损：第二条按"已就绪但清单不一致"报错。
            ResourcePointData point = PendingPoint("r0", 1);

            Assert.Throws<InvalidOperationException>(() => ResourcePointRecovery.Apply(
                new List<ResourcePointData> { point },
                new List<NetLootRecord> { LootRecord("r0", 5001), LootRecord("r0", 5001, 5002) }));
        }

        [Test]
        public void Apply_EmptyLooted_MakesNoChanges()
        {
            ResourcePointData point = PendingPoint("r0", 1);

            ResourcePointRecovery.Apply(new List<ResourcePointData> { point }, new List<NetLootRecord>());

            Assert.That(point.ContentsReady, Is.False);
        }

        #endregion

        #region 回放清单查找（开局幂等落入活跃 run 的装配路径）

        [Test]
        public void TryGetLootedItems_FindsRecordByPointId()
        {
            List<NetLootRecord> looted = new List<NetLootRecord>
            {
                LootRecord("r1", 5301),
                LootRecord("r0", 5001, 5003),
            };

            Assert.That(ResourcePointRecovery.TryGetLootedItems(looted, "r0", out List<int> items), Is.True);
            Assert.That(items.ToArray(), Is.EqualTo(new[] { 5001, 5003 }));

            Assert.That(ResourcePointRecovery.TryGetLootedItems(looted, "r9", out List<int> missing), Is.False);
            Assert.That(missing, Is.Null);

            Assert.That(ResourcePointRecovery.TryGetLootedItems(null, "r0", out _), Is.False);
        }

        #endregion

        private static ResourcePointData PendingPoint(string pointId, int resourcePointId)
        {
            return ResourcePointData.CreatePendingContents(100 + resourcePointId, "ResourcePoint",
                new Vector3(resourcePointId, 0f, 0f), resourcePointId, pointId);
        }

        private static NetLootRecord LootRecord(string pointId, params int[] itemIds)
        {
            return new NetLootRecord { pointId = pointId, itemIds = itemIds.ToList() };
        }
    }
}
