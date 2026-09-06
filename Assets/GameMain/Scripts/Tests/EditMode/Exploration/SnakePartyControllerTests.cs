using NUnit.Framework;
using SepCore.Entity;
using SepCore.Exploration;
using UnityEngine;

namespace SepCore.Tests
{
    [TestFixture]
    public class SnakePartyControllerTests
    {
        [Test]
        public void Step_WhenNotPaused_DoesNotOverrideCanMoveFalse()
        {
            GameObject go = new GameObject("TestLeader");
            try
            {
                PlayerCharacterController leader = go.AddComponent<PlayerCharacterController>();
                SnakePartyController party = go.AddComponent<SnakePartyController>();

                party.SetExplorationPausedProvider(() => false);
                party.Bind(leader, new PlayerCharacterLogic[0]);

                // 模拟物资点交互开始：锁定玩家移动
                leader.CanMove = false;

                // 推进多个物理帧，验证控制器不再无条件覆盖为 true
                for (int i = 0; i < 5; i++)
                {
                    party.Step(0.02f);
                    Assert.IsFalse(leader.CanMove, $"Leader CanMove should remain false on step {i}.");
                }
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void Step_WhenPaused_SetsCanMoveFalse()
        {
            GameObject go = new GameObject("TestLeader");
            try
            {
                PlayerCharacterController leader = go.AddComponent<PlayerCharacterController>();
                SnakePartyController party = go.AddComponent<SnakePartyController>();

                bool isPaused = false;
                party.SetExplorationPausedProvider(() => isPaused);
                party.Bind(leader, new PlayerCharacterLogic[0]);
                leader.CanMove = true;

                // 进入暂停（战斗）
                isPaused = true;
                party.Step(0.02f);

                Assert.IsFalse(leader.CanMove);
                Assert.IsTrue(party.WasExplorationPaused);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void Step_WhenTransitionFromPausedToUnpaused_RestoresCanMoveTrue()
        {
            GameObject go = new GameObject("TestLeader");
            try
            {
                PlayerCharacterController leader = go.AddComponent<PlayerCharacterController>();
                SnakePartyController party = go.AddComponent<SnakePartyController>();

                bool isPaused = true;
                party.SetExplorationPausedProvider(() => isPaused);
                party.Bind(leader, new PlayerCharacterLogic[0]);

                party.Step(0.02f);
                Assert.IsFalse(leader.CanMove);

                // 战斗结束，探索恢复
                isPaused = false;
                party.Step(0.02f);

                Assert.IsTrue(leader.CanMove);
                Assert.IsFalse(party.WasExplorationPaused);

                // 恢复后随后的帧不应再次强行设置或影响 CanMove
                leader.CanMove = false;
                party.Step(0.02f);
                Assert.IsFalse(leader.CanMove);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void PlayerCharacterController_Tick_InteractionSetsCanMoveFalse_MovementImmediatelyStopped()
        {
            GameObject go = new GameObject("TestPlayer");
            try
            {
                PlayerCharacterController controller = go.AddComponent<PlayerCharacterController>();
                VirtualCharacterInput input = new VirtualCharacterInput();
                controller.SetInputSource(input);
                controller.MoveSpeed = 5.0f;

                // 模拟交互事件处理中设置 CanMove = false（如同 ResourcePointLogic.OnInteractStart）
                controller.OnInteractTriggered += c => c.CanMove = false;

                input.MoveVector = new Vector2(1f, 0f);
                input.InteractTriggered = true;
                input.IsInteracting = true;

                controller.Tick(0.02f);

                Assert.IsFalse(controller.CanMove);
                Assert.IsFalse(controller.IsMoving);
                Assert.AreEqual(Vector2.zero, controller.CurrentVelocity);
                Assert.AreEqual(0f, go.transform.position.x, 0.0001f);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }
    }
}
