using TEngine;
using UnityEngine;
using UnityEngine.UIElements;

namespace GameLogic
{
    public sealed partial class UITKModule
    {
        private const int LAYER_COUNT = 5;
        private const int LAYER_SORT_INTERVAL = 2000;

        private GameObject _uiTKRoot;
        private UIDocument[] _layerDocuments = new UIDocument[LAYER_COUNT];
        private VisualElement[] _layerRoots = new VisualElement[LAYER_COUNT];

        private static readonly string[] LayerNames = { "Panel_Bottom", "Panel_UI", "Panel_Top", "Panel_Tips", "Panel_System" };

        private const string DEFAULT_PANEL_SETTINGS = "DefaultPanelSettings";

        private void InitPanels()
        {
            _uiTKRoot = new GameObject("UITKRoot");
            Object.DontDestroyOnLoad(_uiTKRoot);

            // 加载 PanelSettings 资产
            PanelSettings panelSettings = Resource.LoadPanelSettings(DEFAULT_PANEL_SETTINGS);
            if (panelSettings == null)
            {
                Log.Error("UITKModule: Failed to load PanelSettings, trying Resources fallback.");
                panelSettings = Resources.Load<PanelSettings>(DEFAULT_PANEL_SETTINGS);
            }

            for (int i = 0; i < LAYER_COUNT; i++)
            {
                var panelGo = new GameObject(LayerNames[i]);
                panelGo.transform.SetParent(_uiTKRoot.transform);

                var doc = panelGo.AddComponent<UIDocument>();
                doc.panelSettings = panelSettings;
                doc.sortingOrder = i * LAYER_SORT_INTERVAL;

                _layerDocuments[i] = doc;
                _layerRoots[i] = doc.rootVisualElement;
            }
        }

        private void DestroyPanels()
        {
            if (_uiTKRoot != null)
            {
                Object.Destroy(_uiTKRoot);
                _uiTKRoot = null;
            }
        }

        internal void AttachToLayer(UITKWindow window)
        {
            int layer = window.WindowLayer;
            if (layer < 0 || layer >= LAYER_COUNT)
            {
                Log.Error($"UITKModule: Invalid window layer {layer} for {window.WindowName}");
                layer = (int)UILayer.UI;
            }
            _layerRoots[layer].Add(window.RootElement);
        }
    }
}
