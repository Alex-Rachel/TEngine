using Cysharp.Threading.Tasks;

namespace TEngine
{
    /// <summary>
    /// 本地化管理模块，负责多语言资源的加载和切换。
    /// </summary>
    public class LocalizationModule : Module, ILocalizationModule
    {
        // 实际的本地化管理器实例。
        private LocalizationManager _localizationManager;

        /// <summary>
        /// 绑定具体的本地化管理器实现。
        /// </summary>
        /// <param name="localizationManager">要绑定的本地化管理器实例。</param>
        public void Bind(LocalizationManager localizationManager)
        {
            _localizationManager = localizationManager;
        }
        
        /// <summary>
        /// 模块初始化方法。
        /// </summary>
        public override void OnInit()
        {
        }

        /// <summary>
        /// 模块关闭方法。
        /// </summary>
        public override void Shutdown()
        {
            UnityEngine.Object.Destroy(_localizationManager);
        }

        /// <summary>
        /// 当前使用的语言（可读写）。
        /// </summary>
        public Language Language
        {
            get => _localizationManager.Language;
            set => _localizationManager.Language = value;
        }

        /// <summary>
        /// 获取系统默认语言。
        /// </summary>
        public Language SystemLanguage => _localizationManager.SystemLanguage;

        /// <summary>
        /// 加载完整的语言资源包。
        /// </summary>
        /// <param name="assetName">要加载的资源包名称</param>
        public async UniTask LoadLanguageTotalAsset(string assetName)
        {
            await _localizationManager.LoadLanguageTotalAsset(assetName);
        }

        /// <summary>
        /// 加载指定语言的本地化资源。
        /// </summary>
        /// <param name="language">要加载的语言。</param>
        /// <param name="setCurrent">是否设置为当前语言。</param>
        /// <param name="fromInit">是否来自初始化流程。</param>
        public async UniTask LoadLanguage(string language, bool setCurrent = false, bool fromInit = false)
        {
            await _localizationManager.LoadLanguage(language, setCurrent, fromInit);
        }

        /// <summary>
        /// 检查指定语言是否可用。
        /// </summary>
        /// <param name="language">要检查的语言名称。</param>
        /// <returns>如果语言可用返回true，否则false。</returns>
        public bool CheckLanguage(string language)
        {
            return _localizationManager.CheckLanguage(language);
        }

        /// <summary>
        /// 设置当前语言（通过枚举值）。
        /// </summary>
        /// <param name="language">要设置的语言枚举值。</param>
        /// <param name="load">是否立即加载语言资源。</param>
        /// <returns>设置是否成功。</returns>
        public bool SetLanguage(Language language, bool load = false)
        {
            return _localizationManager.SetLanguage(language, load);
        }

        /// <summary>
        /// 设置当前语言（通过字符串）。
        /// </summary>
        /// <param name="language">要设置的语言名称。</param>
        /// <param name="load">是否立即加载语言资源。</param>
        /// <returns>设置是否成功。</returns>
        public bool SetLanguage(string language, bool load = false)
        {
            return _localizationManager.SetLanguage(language, load);
        }

        /// <summary>
        /// 设置当前语言（通过语言ID）。
        /// </summary>
        /// <param name="languageId">要设置的语言ID。</param>
        /// <returns>设置是否成功。</returns>
        public bool SetLanguage(int languageId)
        {
            return _localizationManager.SetLanguage(languageId);
        }
    }
}
