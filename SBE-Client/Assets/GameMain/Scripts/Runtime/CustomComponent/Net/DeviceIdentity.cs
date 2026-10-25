using System;
using System.IO;
using UnityEngine;
using UnityGameFramework.Runtime;

namespace SepCore.Net
{
    /// <summary>
    /// 匿名设备身份：设备 UUID 与登录令牌，持久化在 persistentDataPath/Save/ 下。
    /// </summary>
    public static class DeviceIdentity
    {
        [Serializable]
        private class IdentityFile
        {
            public string device;
            public string token;
        }

        private static string FilePath => GameFramework.Utility.Path.GetRegularPath(
            Path.Combine(Application.persistentDataPath, "Save", "device_identity.json"));

        private static string _cachedDevice;

        /// <summary>
        /// 设备 UUID（不存在则生成并落盘）。令牌可能为空（尚未登录过）。
        /// </summary>
        public static void Load(out string device, out string token)
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    IdentityFile identity = GameFramework.Utility.Json.ToObject<IdentityFile>(File.ReadAllText(FilePath));
                    if (identity != null)
                    {
                        _cachedDevice = identity.device;
                        device = identity.device;
                        token = identity.token;
                        return;
                    }
                }
            }
            catch (Exception)
            {
                // 读取失败按新设备处理
            }

            _cachedDevice = Guid.NewGuid().ToString("N");
            device = _cachedDevice;
            token = null;
            Save(device, token);
        }

        /// <summary>
        /// 更新持久化的令牌（设备号保持不变）。
        /// </summary>
        public static void SaveToken(string token)
        {
            Save(_cachedDevice, token);
        }

        private static void Save(string device, string token)
        {
            try
            {
                string dir = Path.GetDirectoryName(FilePath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                File.WriteAllText(FilePath, GameFramework.Utility.Json.ToJson(new IdentityFile
                {
                    device = device,
                    token = token,
                }));
            }
            catch (Exception ex)
            {
                Log.Error("Save device identity failure: {0}", ex);
            }
        }
    }
}
