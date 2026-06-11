using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UIElements;

namespace GameLogic
{
    /// <summary>
    /// UIToolkit 资源加载器接口。
    /// </summary>
    public interface IUITKResourceLoader
    {
        VisualTreeAsset LoadVisualTreeAsset(string location, string packageName = "");
        UniTask<VisualTreeAsset> LoadVisualTreeAssetAsync(string location, CancellationToken ct = default, string packageName = "");
        StyleSheet LoadStyleSheet(string location, string packageName = "");
        UniTask<StyleSheet> LoadStyleSheetAsync(string location, CancellationToken ct = default, string packageName = "");
        PanelSettings LoadPanelSettings(string location, string packageName = "");
        void Unload(Object asset);
    }
}
