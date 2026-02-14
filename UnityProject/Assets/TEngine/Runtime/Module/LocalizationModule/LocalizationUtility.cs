using System.Collections.Generic;
using UnityEngine;

namespace TEngine
{
    /// <summary>
    /// 默认本地化辅助器。
    /// </summary>
    public class LocalizationUtility
    {
#if UNITY_EDITOR
        public const string I2GlobalSourcesEditorPath = "Assets/Editor/I2Localization/I2Languages.asset";
#endif

        public const string I2ResAssetNamePrefix = "I2_";

        /// <summary>
        /// 获取系统语言。
        /// </summary>
        public static Language SystemLanguage
        {
            get
            {
                switch (Application.systemLanguage)
                {
                    case UnityEngine.SystemLanguage.Afrikaans: return Language.Afrikaans;
                    case UnityEngine.SystemLanguage.Arabic: return Language.Arabic;
                    case UnityEngine.SystemLanguage.Basque: return Language.Basque;
                    case UnityEngine.SystemLanguage.Belarusian: return Language.Belarusian;
                    case UnityEngine.SystemLanguage.Bulgarian: return Language.Bulgarian;
                    case UnityEngine.SystemLanguage.Catalan: return Language.Catalan;
                    case UnityEngine.SystemLanguage.Chinese: return Language.ChineseSimplified;
                    case UnityEngine.SystemLanguage.ChineseSimplified: return Language.ChineseSimplified;
                    case UnityEngine.SystemLanguage.ChineseTraditional: return Language.ChineseTraditional;
                    case UnityEngine.SystemLanguage.Czech: return Language.Czech;
                    case UnityEngine.SystemLanguage.Danish: return Language.Danish;
                    case UnityEngine.SystemLanguage.Dutch: return Language.Dutch;
                    case UnityEngine.SystemLanguage.English: return Language.English;
                    case UnityEngine.SystemLanguage.Estonian: return Language.Estonian;
                    case UnityEngine.SystemLanguage.Faroese: return Language.Faroese;
                    case UnityEngine.SystemLanguage.Finnish: return Language.Finnish;
                    case UnityEngine.SystemLanguage.French: return Language.French;
                    case UnityEngine.SystemLanguage.German: return Language.German;
                    case UnityEngine.SystemLanguage.Greek: return Language.Greek;
                    case UnityEngine.SystemLanguage.Hebrew: return Language.Hebrew;
                    case UnityEngine.SystemLanguage.Hungarian: return Language.Hungarian;
                    case UnityEngine.SystemLanguage.Icelandic: return Language.Icelandic;
                    case UnityEngine.SystemLanguage.Indonesian: return Language.Indonesian;
                    case UnityEngine.SystemLanguage.Italian: return Language.Italian;
                    case UnityEngine.SystemLanguage.Japanese: return Language.Japanese;
                    case UnityEngine.SystemLanguage.Korean: return Language.Korean;
                    case UnityEngine.SystemLanguage.Latvian: return Language.Latvian;
                    case UnityEngine.SystemLanguage.Lithuanian: return Language.Lithuanian;
                    case UnityEngine.SystemLanguage.Norwegian: return Language.Norwegian;
                    case UnityEngine.SystemLanguage.Polish: return Language.Polish;
                    case UnityEngine.SystemLanguage.Portuguese: return Language.PortuguesePortugal;
                    case UnityEngine.SystemLanguage.Romanian: return Language.Romanian;
                    case UnityEngine.SystemLanguage.Russian: return Language.Russian;
                    case UnityEngine.SystemLanguage.SerboCroatian: return Language.SerboCroatian;
                    case UnityEngine.SystemLanguage.Slovak: return Language.Slovak;
                    case UnityEngine.SystemLanguage.Slovenian: return Language.Slovenian;
                    case UnityEngine.SystemLanguage.Spanish: return Language.Spanish;
                    case UnityEngine.SystemLanguage.Swedish: return Language.Swedish;
                    case UnityEngine.SystemLanguage.Thai: return Language.Thai;
                    case UnityEngine.SystemLanguage.Turkish: return Language.Turkish;
                    case UnityEngine.SystemLanguage.Ukrainian: return Language.Ukrainian;
                    case UnityEngine.SystemLanguage.Unknown: return Language.Unspecified;
                    case UnityEngine.SystemLanguage.Vietnamese: return Language.Vietnamese;
                    default: return Language.Unspecified;
                }
            }
        }

        private static readonly Dictionary<Language, string> _languageMap = new Dictionary<Language, string>();
        private static readonly Dictionary<string, Language> _languageStrMap = new Dictionary<string, Language>();

        static LocalizationUtility()
        {
            RegisterLanguageMap(Language.English);
            RegisterLanguageMap(Language.ChineseSimplified, "Chinese");
            RegisterLanguageMap(Language.ChineseTraditional);
            RegisterLanguageMap(Language.Japanese);
            RegisterLanguageMap(Language.Korean);
        }

        private static void RegisterLanguageMap(Language language, string str = "")
        {
            if (string.IsNullOrEmpty(str))
            {
                str = language.ToString();
            }

            _languageMap[language] = str;
            _languageStrMap[str] = language;
        }

        /// <summary>
        /// 根据语言字符串获取语言枚举。
        /// </summary>
        /// <param name="str">语言字符串。</param>
        /// <returns>语言枚举。</returns>
        public static Language GetLanguage(string str)
        {
            if (string.IsNullOrEmpty(str))
            {
                return Language.Unspecified;
            }

            if (_languageStrMap.TryGetValue(str, out var language))
            {
                return language;
            }

            language = Language.English;
            return language;
        }

        /// <summary>
        /// 根据语言枚举获取语言字符串。
        /// </summary>
        /// <param name="language">语言枚举。</param>
        /// <returns>语言字符串。</returns>
        public static string GetLanguageStr(Language language)
        {
            if (_languageMap.TryGetValue(language, out var ret))
            {
                return ret;
            }

            ret = "English";
            return ret;
        }
    }
}