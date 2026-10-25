using NUnit.Framework;
using SepCore.Exploration;
using UnityEngine;

namespace SepCore.Tests
{
    [TestFixture]
    public class CharacterInputTests
    {
        [SetUp]
        public void SetUp()
        {
            CharacterInputBridge.Reset();
        }

        [TearDown]
        public void TearDown()
        {
            CharacterInputBridge.Reset();
        }

        [Test]
        public void VirtualCharacterInput_MoveVector_ClampsMagnitudeToOne()
        {
            VirtualCharacterInput input = new VirtualCharacterInput();

            input.MoveVector = new Vector2(2f, 0f);
            Assert.AreEqual(1f, input.MoveVector.magnitude, 0.0001f);
            Assert.AreEqual(1f, input.MoveVector.x, 0.0001f);
            Assert.AreEqual(0f, input.MoveVector.y, 0.0001f);

            input.MoveVector = new Vector2(0.5f, 0.5f);
            Assert.AreEqual(0.5f, input.MoveVector.x, 0.0001f);
            Assert.AreEqual(0.5f, input.MoveVector.y, 0.0001f);
        }

        [Test]
        public void VirtualCharacterInput_InteractionProperties_ReflectValuesAndReset()
        {
            VirtualCharacterInput input = new VirtualCharacterInput();

            Assert.IsFalse(input.HasInput);
            Assert.IsFalse(input.IsInteracting);
            Assert.IsFalse(input.InteractTriggered);
            Assert.IsFalse(input.InteractReleased);

            input.IsInteracting = true;
            input.InteractTriggered = true;
            input.InteractReleased = true;

            Assert.IsTrue(input.HasInput);
            Assert.IsTrue(input.IsInteracting);
            Assert.IsTrue(input.InteractTriggered);
            Assert.IsTrue(input.InteractReleased);

            input.Reset();

            Assert.IsFalse(input.HasInput);
            Assert.IsFalse(input.IsInteracting);
            Assert.IsFalse(input.InteractTriggered);
            Assert.IsFalse(input.InteractReleased);
            Assert.AreEqual(Vector2.zero, input.MoveVector);
        }

        [Test]
        public void CompositeCharacterInput_CombinesMoveVectorsAndClamps()
        {
            CompositeCharacterInput composite = new CompositeCharacterInput();
            VirtualCharacterInput sourceA = new VirtualCharacterInput();
            VirtualCharacterInput sourceB = new VirtualCharacterInput();

            composite.AddSource(sourceA);
            composite.AddSource(sourceB);

            // 单源移动
            sourceA.MoveVector = new Vector2(0.5f, 0f);
            sourceB.MoveVector = Vector2.zero;
            Assert.AreEqual(0.5f, composite.MoveVector.x, 0.0001f);
            Assert.AreEqual(0f, composite.MoveVector.y, 0.0001f);

            // 双源同向移动，超出 1 自动归一化
            sourceB.MoveVector = new Vector2(0.8f, 0f);
            Assert.AreEqual(1f, composite.MoveVector.magnitude, 0.0001f);
            Assert.AreEqual(1f, composite.MoveVector.x, 0.0001f);

            // 双源正交移动，向量相加
            sourceA.MoveVector = new Vector2(0.3f, 0f);
            sourceB.MoveVector = new Vector2(0f, 0.4f);
            Assert.AreEqual(0.3f, composite.MoveVector.x, 0.0001f);
            Assert.AreEqual(0.4f, composite.MoveVector.y, 0.0001f);
            Assert.AreEqual(0.5f, composite.MoveVector.magnitude, 0.0001f);
        }

        [Test]
        public void CompositeCharacterInput_CombinesInteractionStates()
        {
            CompositeCharacterInput composite = new CompositeCharacterInput();
            VirtualCharacterInput sourceA = new VirtualCharacterInput();
            VirtualCharacterInput sourceB = new VirtualCharacterInput();

            composite.AddSource(sourceA);
            composite.AddSource(sourceB);

            Assert.IsFalse(composite.IsInteracting);
            Assert.IsFalse(composite.InteractTriggered);
            Assert.IsFalse(composite.InteractReleased);

            // A 触发，B 无输入
            sourceA.InteractTriggered = true;
            Assert.IsTrue(composite.InteractTriggered);
            Assert.IsFalse(composite.IsInteracting);

            // B 持续按住
            sourceA.InteractTriggered = false;
            sourceB.IsInteracting = true;
            Assert.IsTrue(composite.IsInteracting);
            Assert.IsFalse(composite.InteractTriggered);

            // A 松开按键
            sourceA.InteractReleased = true;
            Assert.IsTrue(composite.InteractReleased);
        }

        [Test]
        public void CompositeCharacterInput_RemoveAndClearSources()
        {
            CompositeCharacterInput composite = new CompositeCharacterInput();
            VirtualCharacterInput source = new VirtualCharacterInput();

            composite.AddSource(source);
            Assert.AreEqual(1, composite.Sources.Count);

            source.MoveVector = new Vector2(1f, 0f);
            Assert.AreEqual(1f, composite.MoveVector.x, 0.0001f);

            composite.RemoveSource(source);
            Assert.AreEqual(0, composite.Sources.Count);
            Assert.AreEqual(Vector2.zero, composite.MoveVector);
        }

        [Test]
        public void CharacterInputBridge_RegisterAndUnregister()
        {
            VirtualCharacterInput uiInput = new VirtualCharacterInput();

            Assert.IsNull(CharacterInputBridge.ActiveUiInput);

            CharacterInputBridge.RegisterUIInput(uiInput);
            Assert.AreSame(uiInput, CharacterInputBridge.ActiveUiInput);

            // 通过 DefaultInput 验证能够采到 UI 输入
            uiInput.MoveVector = new Vector2(0f, 1f);
            Assert.AreEqual(1f, CharacterInputBridge.DefaultInput.MoveVector.y, 0.0001f);

            CharacterInputBridge.UnregisterUIInput();
            Assert.IsNull(CharacterInputBridge.ActiveUiInput);
            Assert.AreEqual(0f, CharacterInputBridge.DefaultInput.MoveVector.y, 0.0001f);
        }

        [Test]
        public void PlayerCharacterController_Tick_MovesAndUpdatesFacing()
        {
            GameObject go = new GameObject("TestPlayer");
            try
            {
                PlayerCharacterController controller = go.AddComponent<PlayerCharacterController>();
                VirtualCharacterInput input = new VirtualCharacterInput();
                controller.SetInputSource(input);
                controller.MoveSpeed = 4.0f;

                // 向左移动
                input.MoveVector = new Vector2(-1f, 0f);
                controller.Tick(0.5f);

                Assert.IsTrue(controller.IsMoving);
                Assert.AreEqual(new Vector2(-4f, 0f), controller.CurrentVelocity);
                Assert.AreEqual(new Vector2(-1f, 0f), controller.FacingDirection);
                Assert.IsFalse(controller.IsFacingRight);
                Assert.AreEqual(-2.0f, go.transform.position.x, 0.0001f);

                // 向右移动
                input.MoveVector = new Vector2(1f, 0f);
                controller.Tick(0.5f);

                Assert.IsTrue(controller.IsMoving);
                Assert.AreEqual(new Vector2(4f, 0f), controller.CurrentVelocity);
                Assert.AreEqual(new Vector2(1f, 0f), controller.FacingDirection);
                Assert.IsTrue(controller.IsFacingRight);
                Assert.AreEqual(0.0f, go.transform.position.x, 0.0001f);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void PlayerCharacterController_CanMoveFalse_StopsMovement()
        {
            GameObject go = new GameObject("TestPlayer");
            try
            {
                PlayerCharacterController controller = go.AddComponent<PlayerCharacterController>();
                VirtualCharacterInput input = new VirtualCharacterInput();
                controller.SetInputSource(input);
                controller.MoveSpeed = 5.0f;

                input.MoveVector = new Vector2(1f, 0f);
                controller.CanMove = false;
                controller.Tick(1.0f);

                Assert.IsFalse(controller.IsMoving);
                Assert.AreEqual(Vector2.zero, controller.CurrentVelocity);
                Assert.AreEqual(0f, go.transform.position.x, 0.0001f);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void PlayerCharacterController_InteractionEvents_Trigger()
        {
            GameObject go = new GameObject("TestPlayer");
            try
            {
                PlayerCharacterController controller = go.AddComponent<PlayerCharacterController>();
                VirtualCharacterInput input = new VirtualCharacterInput();
                controller.SetInputSource(input);

                int triggeredCount = 0;
                int releasedCount = 0;
                int heldCount = 0;

                controller.OnInteractTriggered += _ => triggeredCount++;
                controller.OnInteractReleased += _ => releasedCount++;
                controller.OnInteractHeld += _ => heldCount++;

                input.InteractTriggered = true;
                input.IsInteracting = true;
                controller.Tick(0.1f);

                Assert.AreEqual(1, triggeredCount);
                Assert.AreEqual(1, heldCount);
                Assert.AreEqual(0, releasedCount);

                input.InteractTriggered = false;
                input.IsInteracting = false;
                input.InteractReleased = true;
                controller.Tick(0.1f);

                Assert.AreEqual(1, triggeredCount);
                Assert.AreEqual(1, heldCount);
                Assert.AreEqual(1, releasedCount);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void CompositeCharacterInput_EnabledFalse_ZerosMoveAndInteractions()
        {
            CompositeCharacterInput composite = new CompositeCharacterInput();
            VirtualCharacterInput source = new VirtualCharacterInput();
            composite.AddSource(source);

            source.MoveVector = new Vector2(1f, 0f);
            source.InteractTriggered = true;
            source.IsInteracting = true;
            source.InteractReleased = true;

            Assert.IsTrue(composite.HasInput);
            Assert.AreEqual(1f, composite.MoveVector.x, 0.0001f);
            Assert.IsTrue(composite.InteractTriggered);
            Assert.IsTrue(composite.IsInteracting);
            Assert.IsTrue(composite.InteractReleased);

            composite.Enabled = false;

            Assert.IsFalse(composite.HasInput);
            Assert.AreEqual(Vector2.zero, composite.MoveVector);
            Assert.IsFalse(composite.InteractTriggered);
            Assert.IsFalse(composite.IsInteracting);
            Assert.IsFalse(composite.InteractReleased);

            composite.Enabled = true;

            Assert.IsTrue(composite.HasInput);
            Assert.AreEqual(1f, composite.MoveVector.x, 0.0001f);
            Assert.IsTrue(composite.InteractTriggered);
            Assert.IsTrue(composite.IsInteracting);
            Assert.IsTrue(composite.InteractReleased);
        }

        [Test]
        public void CharacterInputBridge_DisableAndEnableInput_ControlsDefaultInput()
        {
            VirtualCharacterInput uiInput = new VirtualCharacterInput();
            CharacterInputBridge.RegisterUIInput(uiInput);
            uiInput.MoveVector = new Vector2(0.5f, 0.5f);
            uiInput.InteractTriggered = true;

            Assert.IsTrue(CharacterInputBridge.IsInputEnabled);
            Assert.IsTrue(CharacterInputBridge.DefaultInput.HasInput);

            CharacterInputBridge.DisableInput(InputDisableReason.Backpack);

            Assert.IsFalse(CharacterInputBridge.IsInputEnabled);
            Assert.AreEqual(InputDisableReason.Backpack, CharacterInputBridge.DisableReasons);
            Assert.IsFalse(CharacterInputBridge.DefaultInput.HasInput);
            Assert.AreEqual(Vector2.zero, CharacterInputBridge.DefaultInput.MoveVector);
            Assert.IsFalse(CharacterInputBridge.DefaultInput.InteractTriggered);

            CharacterInputBridge.EnableInput(InputDisableReason.Backpack);

            Assert.IsTrue(CharacterInputBridge.IsInputEnabled);
            Assert.AreEqual(InputDisableReason.None, CharacterInputBridge.DisableReasons);
            Assert.IsTrue(CharacterInputBridge.DefaultInput.HasInput);
        }

        [Test]
        public void CharacterInputBridge_MultipleDisableReasons_RequiresAllClearedToEnable()
        {
            CharacterInputBridge.DisableInput(InputDisableReason.Backpack);
            CharacterInputBridge.DisableInput(InputDisableReason.Battle);

            Assert.IsFalse(CharacterInputBridge.IsInputEnabled);
            Assert.AreEqual(InputDisableReason.Backpack | InputDisableReason.Battle, CharacterInputBridge.DisableReasons);

            // 仅关闭背包，战斗仍在继续
            CharacterInputBridge.EnableInput(InputDisableReason.Backpack);
            Assert.IsFalse(CharacterInputBridge.IsInputEnabled);
            Assert.AreEqual(InputDisableReason.Battle, CharacterInputBridge.DisableReasons);

            // 战斗结束，全部原因解除后恢复输入
            CharacterInputBridge.EnableInput(InputDisableReason.Battle);
            Assert.IsTrue(CharacterInputBridge.IsInputEnabled);
            Assert.AreEqual(InputDisableReason.None, CharacterInputBridge.DisableReasons);
        }
    }
}
