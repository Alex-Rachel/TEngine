using Cysharp.Threading.Tasks;

namespace TEngine
{
    public interface ILocalizationModule
    {
        /// <summary>
        /// 获取或设置本地化语言。
        /// </summary>
        public Language Language { get; set; }

        /// <summary>
        /// 获取系统语言。
        /// </summary>
        public Language SystemLanguage {get;}

        /// <summary>
        /// 加载语言总表。
        /// </summary>
        public UniTask LoadLanguageTotalAsset(string assetName);

        /// <summary>
        /// 加载语言分表。
        /// </summary>
        /// <param name="language">语言类型。</param>
        /// <param name="setCurrent">是否立刻设置成当前语言。</param>
        /// <param name="fromInit">是否初始化Inner语言。</param>
        public UniTask LoadLanguage(string language, bool setCurrent = false, bool fromInit = false);

        /// <summary>
        /// 检查是否存在该语言。
        /// </summary>
        /// <param name="language">语言。</param>
        /// <returns>是否已加载。</returns>
        public bool CheckLanguage(string language);

        /// <summary>
        /// 设置当前语言。
        /// </summary>
        /// <param name="language">语言名称。</param>
        /// <param name="load">是否加载。</param>
        /// <returns></returns>
        public bool SetLanguage(Language language, bool load = false);

        /// <summary>
        /// 设置当前语言。
        /// </summary>
        /// <param name="language">语言名称。</param>
        /// <param name="load">是否加载。</param>
        /// <returns></returns>
        public bool SetLanguage(string language, bool load = false);

        /// <summary>
        /// 通过语言的Id设置语言。
        /// </summary>
        /// <param name="languageId">语言ID。</param>
        /// <returns>是否设置成功。</returns>
        public bool SetLanguage(int languageId);
    }
}