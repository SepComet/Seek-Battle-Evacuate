using System;
using System.IO;
using UnityEngine;
using UnityGameFramework.Runtime;

namespace SepCore.Net
{
    /// <summary>
    /// 账号会话缓存：uid、登录令牌与用户名，持久化在 persistentDataPath/Save/ 下。
    /// token 用于静默续登；被服务器拒绝时调用 <see cref="ClearToken"/>（保留用户名供登录框预填）。
    /// 密码不落盘。
    /// </summary>
    public static class AccountSession
    {
        [Serializable]
        private class SessionFile
        {
            public long uid;
            public string token;
            public string username;
        }

        private static string FilePath => GameFramework.Utility.Path.GetRegularPath(
            Path.Combine(Application.persistentDataPath, "Save", "account_session.json"));

        private static string LegacyFilePath => GameFramework.Utility.Path.GetRegularPath(
            Path.Combine(Application.persistentDataPath, "Save", "device_identity.json"));

        private static long _cachedUid;
        private static string _cachedUsername;

        /// <summary>
        /// 读取缓存的会话；文件不存在或损坏时返回空值。首次加载顺带清理旧版设备身份文件。
        /// </summary>
        public static void Load(out long uid, out string token, out string username)
        {
            if (File.Exists(LegacyFilePath))
            {
                try
                {
                    File.Delete(LegacyFilePath);
                }
                catch (Exception ex)
                {
                    Log.Error("Delete legacy device identity failure: {0}", ex);
                }
            }

            uid = 0;
            token = null;
            username = null;
            try
            {
                if (!File.Exists(FilePath))
                {
                    return;
                }

                SessionFile session = GameFramework.Utility.Json.ToObject<SessionFile>(File.ReadAllText(FilePath));
                if (session == null)
                {
                    return;
                }

                _cachedUid = session.uid;
                _cachedUsername = session.username;
                uid = session.uid;
                token = string.IsNullOrEmpty(session.token) ? null : session.token;
                username = session.username;
            }
            catch (Exception ex)
            {
                Log.Error("Load account session failure: {0}", ex);
            }
        }

        /// <summary>
        /// 更新持久化的令牌（uid 与用户名保持不变，用于静默续登刷新 token）。
        /// </summary>
        public static void SaveToken(string token)
        {
            Save(_cachedUid, token, _cachedUsername);
        }

        /// <summary>
        /// 账密登录/注册成功后写入完整会话。
        /// </summary>
        public static void Save(long uid, string token, string username)
        {
            _cachedUid = uid;
            _cachedUsername = username;
            try
            {
                string dir = Path.GetDirectoryName(FilePath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                File.WriteAllText(FilePath, GameFramework.Utility.Json.ToJson(new SessionFile
                {
                    uid = uid,
                    token = token,
                    username = username,
                }));
            }
            catch (Exception ex)
            {
                Log.Error("Save account session failure: {0}", ex);
            }
        }

        /// <summary>
        /// 清空令牌（token 被服务器拒绝后调用）；uid 与用户名保留供登录框预填。
        /// </summary>
        public static void ClearToken()
        {
            Save(_cachedUid, null, _cachedUsername);
        }
    }
}
