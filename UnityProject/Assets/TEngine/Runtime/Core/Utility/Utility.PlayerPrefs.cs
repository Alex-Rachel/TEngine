namespace TEngine
{
    public static partial class Utility
    {
        /// <summary>
        /// PlayerPrefs 增强工具类。
        /// 功能特性：
        /// 1. 支持全局启用/禁用 PlayerPrefs 功能
        /// 2. 支持用户隔离存储（通过用户ID自动生成复合键）
        /// 3. 扩展布尔值存储支持
        /// </summary>
        public static class PlayerPrefs
        {
            // 字段注释 -----------------------------------
            
            /// <summary>
            /// 全局开关标识（true时所有操作生效）。
            /// </summary>
            private static bool _enable = true;

            /// <summary>
            /// 当前用户标识（用于生成用户隔离的存储键）。
            /// </summary>
            private static string _userId = "";

            // 基础方法注释 -------------------------------

            /// <summary>
            /// 删除所有 PlayerPrefs 数据。
            /// </summary>
            public static void DeleteAll()
            {
                if (!_enable) return;
                UnityEngine.PlayerPrefs.DeleteAll();
            }

            /// <summary>
            /// 删除指定键的存储数据。
            /// </summary>
            /// <param name="key">要删除的数据键名。</param>
            public static void DeleteKey(string key)
            {
                if (!_enable) return;
                UnityEngine.PlayerPrefs.DeleteKey(key);
            }

            // 数值获取方法 -------------------------------

            /// <summary>
            /// 获取浮点数值（未找到返回-1）。
            /// </summary>
            public static float GetFloat(string key)
            {
                return _enable ? UnityEngine.PlayerPrefs.GetFloat(key) : -1f;
            }

            /// <summary>
            /// 获取浮点数值（带默认值）
            /// </summary>
            public static float GetFloat(string key, float defaultValue)
            {
                return _enable ? UnityEngine.PlayerPrefs.GetFloat(key, defaultValue) : -1f;
            }

            /// <summary>
            /// 获取整数值（未找到返回-1）。
            /// </summary>
            public static int GetInt(string key)
            {
                return _enable ? UnityEngine.PlayerPrefs.GetInt(key) : -1;
            }

            /// <summary>
            /// 获取整数值（带默认值）。
            /// </summary>
            public static int GetInt(string key, int defaultValue)
            {
                return _enable ? UnityEngine.PlayerPrefs.GetInt(key, defaultValue) : -1;
            }

            // 字符串获取方法 -----------------------------

            /// <summary>
            /// 获取字符串值（未找到返回空字符串）。
            /// </summary>
            public static string GetString(string key)
            {
                return _enable ? UnityEngine.PlayerPrefs.GetString(key) : string.Empty;
            }

            /// <summary>
            /// 获取字符串值（带默认值）。
            /// </summary>
            public static string GetString(string key, string defaultValue)
            {
                return _enable ? UnityEngine.PlayerPrefs.GetString(key, defaultValue) : string.Empty;
            }

            /// <summary>
            /// 检查指定键是否存在。
            /// </summary>
            public static bool HasKey(string key)
            {
                return _enable && UnityEngine.PlayerPrefs.HasKey(key);
            }

            // 数值设置方法 -------------------------------

            /// <summary>
            /// 设置浮点数值（自动保存）。
            /// </summary>
            public static void SetFloat(string key, float value, bool save = true)
            {
                if (!_enable) return;
                UnityEngine.PlayerPrefs.SetFloat(key, value);
                if (save)
                {
                    UnityEngine.PlayerPrefs.Save();
                }
            }

            /// <summary>
            /// 设置整数值（自动保存）。
            /// </summary>
            public static void SetInt(string key, int value, bool save = true)
            {
                if (!_enable) return;
                UnityEngine.PlayerPrefs.SetInt(key, value);
                if (save)
                {
                    UnityEngine.PlayerPrefs.Save();
                }
            }

            /// <summary>
            /// 设置字符串值（自动保存）。
            /// </summary>
            public static void SetString(string key, string value, bool save = true)
            {
                if (!_enable) return;
                UnityEngine.PlayerPrefs.SetString(key, value);
                if (save)
                {
                    UnityEngine.PlayerPrefs.Save();
                }
            }

            // 布尔值支持方法 -----------------------------

            /// <summary>
            /// 设置布尔值（存储为1或0）。
            /// </summary>
            public static void SetBool(string key, bool value)
            {
                if (!_enable) return;
                SetInt(key, value ? 1 : 0);
            }

            /// <summary>
            /// 获取布尔值（将存储的1/0转换为bool）。
            /// </summary>
            public static bool GetBool(string key, bool defaultValue)
            {
                if (!_enable) return defaultValue;
                int defaultValue1 = defaultValue ? 1 : 0;
                return GetInt(key, defaultValue1) == 1;
            }

            // 用户隔离支持方法 ---------------------------

            /// <summary>
            /// 生成用户隔离的复合键（格式：userId_key）。
            /// </summary>
            private static string GetUserKey(string key)
            {
                return $"{_userId}_{key}";
            }

            /// <summary>
            /// 设置当前用户ID（用于键隔离）。
            /// </summary>
            /// <param name="id">用户唯一标识。</param>
            public static void SetUserId(string id)
            {
                if (!_enable) return;
                _userId = id;
            }

            /// <summary>
            /// 检查用户隔离键是否存在。
            /// </summary>
            public static bool HasUserKey(string key)
            {
                return _enable && HasKey(GetUserKey(key));
            }

            // 用户隔离存储方法 ---------------------------

            /// <summary>
            /// 设置用户隔离的整数值。
            /// </summary>
            public static void SetUserInt(string key, int value)
            {
                if (!_enable) return;
                SetInt(GetUserKey(key), value);
            }

            /// <summary>
            /// 获取用户隔离的整数值。
            /// </summary>
            public static int GetUserInt(string key, int defaultValue)
            {
                return !_enable ? defaultValue : GetInt(GetUserKey(key), defaultValue);
            }

            /// <summary>
            /// 设置用户隔离的浮点数值。
            /// </summary>
            public static void SetUserFloat(string key, float value)
            {
                if (!_enable) return;
                SetFloat(GetUserKey(key), value);
            }

            /// <summary>
            /// 获取用户隔离的浮点数值。
            /// </summary>
            public static float GetUserFloat(string key, float defaultValue)
            {
                return !_enable ? defaultValue : GetFloat(GetUserKey(key), defaultValue);
            }

            /// <summary>
            /// 设置用户隔离的布尔值。
            /// </summary>
            public static void SetUserBool(string key, bool value)
            {
                if (!_enable) return;
                SetInt(GetUserKey(key), value ? 1 : 0);
            }

            /// <summary>
            /// 获取用户隔离的布尔值。
            /// </summary>
            public static bool GetUserBool(string key, bool defaultValue)
            {
                if (!_enable) return defaultValue;
                int defaultValue1 = defaultValue ? 1 : 0;
                return GetInt(GetUserKey(key), defaultValue1) == 1;
            }

            /// <summary>
            /// 设置用户隔离的字符串值。
            /// </summary>
            public static void SetUserString(string key, string value)
            {
                if (!_enable) return;
                SetString(GetUserKey(key), value);
            }

            /// <summary>
            /// 获取用户隔离的字符串值。
            /// </summary>
            public static string GetUserString(string key, string defaultValue)
            {
                return !_enable ? defaultValue : GetString(GetUserKey(key), defaultValue);
            }
            
            /// <summary>
            /// 检查是否存在指定游戏配置项。
            /// </summary>
            /// <param name="settingName">要检查游戏配置项的名称。</param>
            /// <returns>指定的游戏配置项是否存在。</returns>
            public static bool HasSetting(string settingName)
            {
                return PlayerPrefs.HasKey(settingName);
            }
            
            /// <summary>
            /// 保存游戏配置。
            /// </summary>
            /// <returns>是否保存游戏配置成功。</returns>
            public static bool Save()
            {
                UnityEngine.PlayerPrefs.Save();
                return true;
            }
        }
    }
}
