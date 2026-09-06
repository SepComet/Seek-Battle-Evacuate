using UnityEngine;

namespace SepCore.Exploration
{
    /// <summary>
    /// 全局输入禁用原因标志位（支持多来源独立加锁与解锁）。
    /// </summary>
    [System.Flags]
    public enum InputDisableReason
    {
        None = 0,
        Backpack = 1 << 0,
        Battle = 1 << 1,
        UI = 1 << 2,
        Custom = 1 << 3,
    }

    /// <summary>
    /// 全局输入桥接中心。
    /// 维护默认的复合输入实例，供未单独注入输入源的角色控制器读取，
    /// 同时允许 UI 界面（如 JoystickForm）在开启与关闭时动态挂载与卸载输入源，
    /// 并提供基于原因标志的全局输入启闭管理（如背包界面、战斗中禁用键鼠与移动）。
    /// </summary>
    public static class CharacterInputBridge
    {
        private static readonly CompositeCharacterInput s_CompositeInput = new CompositeCharacterInput();
        private static readonly LegacyCharacterInput s_LegacyInput = new LegacyCharacterInput();
        private static ICharacterInput s_ActiveUiInput;
        private static InputDisableReason s_DisableReasons = InputDisableReason.None;

        static CharacterInputBridge()
        {
            s_CompositeInput.AddSource(s_LegacyInput);
        }

        /// <summary>
        /// 默认全局复合输入源。
        /// </summary>
        public static ICharacterInput DefaultInput => s_CompositeInput;

        /// <summary>
        /// 全局内置的旧版键鼠输入源。
        /// </summary>
        public static LegacyCharacterInput LegacyInput => s_LegacyInput;

        /// <summary>
        /// 当前已注册的 UI 虚拟输入源（如未注册则为 null）。
        /// </summary>
        public static ICharacterInput ActiveUiInput => s_ActiveUiInput;

        /// <summary>
        /// 当前全局角色输入是否启用（无任何禁用原因时为 true）。
        /// </summary>
        public static bool IsInputEnabled => s_DisableReasons == InputDisableReason.None;

        /// <summary>
        /// 当前累积的输入禁用原因掩码。
        /// </summary>
        public static InputDisableReason DisableReasons => s_DisableReasons;

        /// <summary>
        /// 禁用全局输入（追加指定的禁用原因）。
        /// </summary>
        public static void DisableInput(InputDisableReason reason)
        {
            s_DisableReasons |= reason;
            s_CompositeInput.Enabled = IsInputEnabled;
        }

        /// <summary>
        /// 恢复全局输入（移除指定的禁用原因）。
        /// </summary>
        public static void EnableInput(InputDisableReason reason)
        {
            s_DisableReasons &= ~reason;
            s_CompositeInput.Enabled = IsInputEnabled;
        }

        /// <summary>
        /// 直接设置输入启用状态。
        /// 若为 false，标记为 Custom 禁用原因；若为 true，清除所有禁用原因。
        /// </summary>
        public static void SetInputEnabled(bool enabled)
        {
            if (enabled)
            {
                s_DisableReasons = InputDisableReason.None;
            }
            else
            {
                s_DisableReasons |= InputDisableReason.Custom;
            }

            s_CompositeInput.Enabled = IsInputEnabled;
        }

        /// <summary>
        /// 注册 UI 虚拟输入源（如 JoystickForm 开启时调用）。
        /// </summary>
        public static void RegisterUIInput(ICharacterInput uiInput)
        {
            if (s_ActiveUiInput != null)
            {
                s_CompositeInput.RemoveSource(s_ActiveUiInput);
            }

            s_ActiveUiInput = uiInput;
            if (uiInput != null)
            {
                s_CompositeInput.AddSource(uiInput);
            }
        }

        /// <summary>
        /// 注销 UI 虚拟输入源（如 JoystickForm 关闭时调用）。
        /// </summary>
        public static void UnregisterUIInput()
        {
            if (s_ActiveUiInput != null)
            {
                s_CompositeInput.RemoveSource(s_ActiveUiInput);
                s_ActiveUiInput = null;
            }
        }

        /// <summary>
        /// 重置桥接状态（主要用于测试复位）。
        /// </summary>
        public static void Reset()
        {
            s_DisableReasons = InputDisableReason.None;
            s_CompositeInput.Enabled = true;
            s_CompositeInput.ClearSources();
            s_ActiveUiInput = null;
            s_CompositeInput.AddSource(s_LegacyInput);
        }
    }
}
