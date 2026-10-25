//------------------------------------------------------------
// Game Framework
// Copyright © 2013-2021 Jiang Yin. All rights reserved.
// Homepage: https://gameframework.cn/
// Feedback: mailto:ellan@gameframework.cn
//------------------------------------------------------------


namespace SepCore.Utility
{
    public static class AssetUtility
    {
        public static string GetGameMainAsset(string assetName)
        {
            return GameFramework.Utility.Text.Format("Assets/GameMain/{0}", assetName);
        }

        public static string GetConfigAsset(string assetName, bool fromBytes)
        {
            return GameFramework.Utility.Text.Format("Assets/GameMain/Configs/{0}.{1}", assetName,
                fromBytes ? "bytes" : "txt");
        }

        public static string GetDictionaryAsset(string language ,string assetName, bool fromBytes)
        {
            return GameFramework.Utility.Text.Format("Assets/GameMain/Localization/{0}/Dictionaries/{1}.{2}",
                language, assetName, fromBytes ? "bytes" : "xml");
        }

        public static string GetFontAsset(string assetName)
        {
            return GameFramework.Utility.Text.Format("Assets/GameMain/Fonts/{0}.ttf", assetName);
        }

        public static string GetTMPFontAsset(string assetName)
        {
            return GameFramework.Utility.Text.Format("Assets/GameMain/Fonts/{0}.asset", assetName);
        }

        public static string GetSceneAsset(string assetName)
        {
            return GameFramework.Utility.Text.Format("Assets/GameMain/Scenes/{0}.unity", assetName);
        }

        public static string GetMusicAsset(string assetName)
        {
            return GameFramework.Utility.Text.Format("Assets/GameMain/Music/{0}.mp3", assetName);
        }

        public static string GetSoundAsset(string assetName)
        {
            return GameFramework.Utility.Text.Format("Assets/GameMain/Sounds/{0}.wav", assetName);
        }

        public static string GetEntityAsset(string assetPath)
        {
            return GameFramework.Utility.Text.Format("Assets/GameMain/Entities/{0}", assetPath);
        }

        public static string GetUIFormAsset(string assetName)
        {
            return GameFramework.Utility.Text.Format("Assets/GameMain/UI/UIForms/{0}.prefab", assetName);
        }

        public static string GetUISoundAsset(string assetName)
        {
            return GameFramework.Utility.Text.Format("Assets/GameMain/UI/UISounds/{0}.wav", assetName);
        }

        /// <summary>
        /// 获取图片资源路径，assetName 为相对 Assets/GameMain/Textures 的路径，需包含扩展名
        /// </summary>
        public static string GetSpriteAsset(string assetName)
        {
            return GameFramework.Utility.Text.Format("Assets/GameMain/Textures/{0}", assetName);
        }
    }
}
