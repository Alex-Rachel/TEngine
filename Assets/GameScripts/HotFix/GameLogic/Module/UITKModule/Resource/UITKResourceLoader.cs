using System.Threading;
using Cysharp.Threading.Tasks;
using TEngine;
using UnityEngine;
using UnityEngine.UIElements;

namespace GameLogic
{
    /// <summary>
    /// 默认 UIToolkit 资源加载器。委托给 YooAsset (IResourceModule)。
    /// </summary>
    public class UITKResourceLoader : IUITKResourceLoader
    {
        private readonly IResourceModule _resource = ModuleSystem.GetModule<IResourceModule>();

        public VisualTreeAsset LoadVisualTreeAsset(string location, string packageName = "")
        {
            return _resource.LoadAsset<VisualTreeAsset>(location, packageName);
        }

        public async UniTask<VisualTreeAsset> LoadVisualTreeAssetAsync(string location, CancellationToken ct = default, string packageName = "")
        {
            return await _resource.LoadAssetAsync<VisualTreeAsset>(location, ct, packageName);
        }

        public StyleSheet LoadStyleSheet(string location, string packageName = "")
        {
            return _resource.LoadAsset<StyleSheet>(location, packageName);
        }

        public async UniTask<StyleSheet> LoadStyleSheetAsync(string location, CancellationToken ct = default, string packageName = "")
        {
            return await _resource.LoadAssetAsync<StyleSheet>(location, ct, packageName);
        }

        public PanelSettings LoadPanelSettings(string location, string packageName = "")
        {
            return _resource.LoadAsset<PanelSettings>(location, packageName);
        }

        public void Unload(Object asset)
        {
            _resource.UnloadAsset(asset);
        }
    }
}
