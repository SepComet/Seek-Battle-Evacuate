using GameFramework.UI;
using SepCore.Definition;
using SepCore.Utility;
using UnityGameFramework.Runtime;

namespace SepCore.UI
{
    public static class UIComponentExtension
    {
        public static bool HasUIForm(this UIComponent uiComponent, UIFormType uiFormType, string uiGroupName = null)
        {
            return uiComponent.HasUIForm((int)uiFormType, uiGroupName);
        }

        public static bool HasUIForm(this UIComponent uiComponent, int uiFormId, string uiGroupName = null)
        {
            UIFormConfig uiFormConfig = GameEntry.Luban.Get<UIFormConfig>(uiFormId);
            if (uiFormConfig == null)
            {
                return false;
            }

            string assetName = AssetUtility.GetUIFormAsset(uiFormConfig.AssetName);
            if (string.IsNullOrEmpty(uiGroupName))
            {
                return uiComponent.HasUIForm(assetName);
            }

            IUIGroup uiGroup = uiComponent.GetUIGroup(uiGroupName);
            if (uiGroup == null)
            {
                return false;
            }

            return uiGroup.HasUIForm(assetName);
        }

        public static UGuiForm GetUIForm(this UIComponent uiComponent, UIFormType uiFormType, string uiGroupName = null)
        {
            return uiComponent.GetUIForm((int)uiFormType, uiGroupName);
        }

        public static UGuiForm GetUIForm(this UIComponent uiComponent, int uiFormId, string uiGroupName = null)
        {
            UIFormConfig uiFormConfig = GameEntry.Luban.Get<UIFormConfig>(uiFormId);
            if (uiFormConfig == null)
            {
                return null;
            }

            string assetName = AssetUtility.GetUIFormAsset(uiFormConfig.AssetName);
            UIForm uiForm = null;
            if (string.IsNullOrEmpty(uiGroupName))
            {
                uiForm = uiComponent.GetUIForm(assetName);
                if (uiForm == null)
                {
                    return null;
                }

                return (UGuiForm)uiForm.Logic;
            }

            IUIGroup uiGroup = uiComponent.GetUIGroup(uiGroupName);
            if (uiGroup == null)
            {
                return null;
            }

            uiForm = (UIForm)uiGroup.GetUIForm(assetName);
            if (uiForm == null)
            {
                return null;
            }

            return (UGuiForm)uiForm.Logic;
        }

        public static void CloseUIForm(this UIComponent uiComponent, UGuiForm uiForm)
        {
            uiComponent.CloseUIForm(uiForm.UIForm);
        }

        public static int? OpenUIForm(this UIComponent uiComponent, UIFormType uiFormType, object userData = null)
        {
            return uiComponent.OpenUIForm((int)uiFormType, userData);
        }

        public static int? OpenUIForm(this UIComponent uiComponent, int uiFormId, object userData = null)
        {
            UIFormConfig uiFormConfig = GameEntry.Luban.Get<UIFormConfig>(uiFormId);
            if (uiFormConfig == null)
            {
                Log.Warning("Can not load UI form '{0}' from data table.", uiFormId.ToString());
                return null;
            }

            string assetName = AssetUtility.GetUIFormAsset(uiFormConfig.AssetName);
            if (!uiFormConfig.AllowMultiInstance)
            {
                if (uiComponent.IsLoadingUIForm(assetName))
                {
                    return null;
                }

                if (uiComponent.HasUIForm(assetName))
                {
                    return null;
                }
            }

            return uiComponent.OpenUIForm(assetName, uiFormConfig.UIGroupName, Constant.AssetPriority.UIFormAsset,
                uiFormConfig.PauseCoveredUIForm, userData);
        }

        public static void OpenDialog(this UIComponent uiComponent, DialogParams dialogParams)
        {
            uiComponent.OpenUIForm(UIFormType.DialogForm, dialogParams);
        }
    }
}
