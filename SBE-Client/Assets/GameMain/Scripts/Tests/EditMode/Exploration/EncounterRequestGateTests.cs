using NUnit.Framework;
using SepCore.Exploration;

namespace SepCore.Tests
{
    /// <summary>
    /// 撞敌请求节流（纯逻辑）：碰撞回调每物理帧触发，请求是在途网络往返，
    /// 必须保证（1）在途期间不重复发（2）失败后有最短退避（3）成功后不再被冷却挡住。
    /// </summary>
    [TestFixture]
    public class EncounterRequestGateTests
    {
        [Test]
        public void NewGate_AllowsOneRequest()
        {
            EncounterRequestGate gate = new EncounterRequestGate();

            Assert.IsTrue(gate.CanRequest);
            Assert.IsFalse(gate.InFlight);
            Assert.AreEqual(0f, gate.CooldownRemaining);

            Assert.IsTrue(gate.TryBegin());
            Assert.IsFalse(gate.CanRequest);
            Assert.IsTrue(gate.InFlight);
        }

        /// <summary>
        /// 在途期间碰撞回调继续每帧到达：必须全部被挡掉，不能发出第二个请求。
        /// </summary>
        [Test]
        public void WhileInFlight_FurtherRequestsAreDropped()
        {
            EncounterRequestGate gate = new EncounterRequestGate();
            Assert.IsTrue(gate.TryBegin());

            for (int i = 0; i < 100; i++)
            {
                gate.Tick(0.02f);
                Assert.IsFalse(gate.TryBegin());
            }

            Assert.IsTrue(gate.InFlight);
        }

        /// <summary>
        /// 被拒绝后进入至少 1 秒冷却；冷却期间即使没有在途请求也不得再发。
        /// </summary>
        [Test]
        public void AfterRejection_CooldownBlocksRequests()
        {
            EncounterRequestGate gate = new EncounterRequestGate();
            gate.TryBegin();
            gate.Reject();

            Assert.IsFalse(gate.InFlight);
            Assert.AreEqual(EncounterRequestGate.RejectionCooldownSeconds, gate.CooldownRemaining);
            Assert.GreaterOrEqual(gate.CooldownRemaining, 1f);
            Assert.IsFalse(gate.CanRequest);
            Assert.IsFalse(gate.TryBegin());

            gate.Tick(0.4f);
            Assert.IsFalse(gate.TryBegin());

            gate.Tick(0.4f);
            Assert.IsFalse(gate.TryBegin());

            // 累计满 1.0s 后解禁（浮点残差用一次多余推进吃掉）。
            gate.Tick(0.4f);
            Assert.AreEqual(0f, gate.CooldownRemaining);
            Assert.IsTrue(gate.TryBegin());
        }

        [Test]
        public void Tick_OvershootingCooldown_ClampsToZero()
        {
            EncounterRequestGate gate = new EncounterRequestGate();
            gate.TryBegin();
            gate.Reject();

            gate.Tick(10f);

            Assert.AreEqual(0f, gate.CooldownRemaining);
            Assert.IsTrue(gate.CanRequest);
        }

        /// <summary>
        /// 成功后只清在途标志：战斗态由实体自身承担，不应顺带进入冷却，
        /// 否则战败/逃跑出来后还要白等 1 秒才能再撞。
        /// </summary>
        [Test]
        public void AfterSuccess_NoCooldown()
        {
            EncounterRequestGate gate = new EncounterRequestGate();
            gate.TryBegin();
            gate.Complete();

            Assert.IsFalse(gate.InFlight);
            Assert.AreEqual(0f, gate.CooldownRemaining);
            Assert.IsTrue(gate.CanRequest);
        }

        [Test]
        public void Reset_ClearsBothFlags()
        {
            EncounterRequestGate gate = new EncounterRequestGate();
            gate.TryBegin();
            gate.Reject();
            Assert.IsFalse(gate.CanRequest);

            gate.Reset();

            Assert.IsFalse(gate.InFlight);
            Assert.AreEqual(0f, gate.CooldownRemaining);
            Assert.IsTrue(gate.CanRequest);
        }

        /// <summary>
        /// 退避中再次被拒（极端：只有一次在途请求，但状态机被外部重复调用）时冷却重置，
        /// 保证不会因为重复 Reject 把冷却做短。
        /// </summary>
        [Test]
        public void RepeatedReject_RestartsCooldown()
        {
            EncounterRequestGate gate = new EncounterRequestGate();
            gate.TryBegin();
            gate.Reject();
            gate.Tick(0.8f);
            Assert.AreEqual(0.2f, gate.CooldownRemaining, 0.0001f);

            gate.Reject();

            Assert.AreEqual(EncounterRequestGate.RejectionCooldownSeconds, gate.CooldownRemaining);
        }
    }
}
