using System.Collections.Generic;
using UnityEngine;

namespace TEngine
{
    internal sealed partial class UIModule
    {
        public Camera UICamera { get; set; }
        public Canvas UICanvas;
        public Transform UICanvasRoot { get; set; }
        public Transform UIRoot;

        private const int UI_ROOT_OFFSET = 1000;
        private const int LAYER_DISTANCE = 1000;

        private const int LAYER_DEEP = 2000;
        private const int WINDOW_DEEP = 100;

        private readonly RectTransform[] m_AllWindowLayer = new RectTransform[(int)UILayer.All];

        private RectTransform UICacheLayer;
        private bool _isOrthographic;

        public void Initlize(Transform root, bool isOrthographic)
        {
            UIRoot = root;
            Object.DontDestroyOnLoad(root.gameObject);

            UIRoot.transform.position = new Vector3(UI_ROOT_OFFSET, UI_ROOT_OFFSET, 0);

            UICanvas = UIRoot.GetComponentInChildren<Canvas>();
            UICamera = UICanvas.worldCamera;
            UICanvasRoot = UICanvas.transform;

            _isOrthographic = isOrthographic;
            UICamera.orthographic = isOrthographic;
            if (!isOrthographic)
            {
                UICamera.nearClipPlane = 10;
                UICamera.farClipPlane = 1000;
            }

            const int len = (int)UILayer.All;
            for (var i = len - 1; i >= 0; i--)
            {
                AddLayer(i);
            }

            AddLayer((int)UILayer.All);
            InitUIBlock();
        }

        private void AddLayer(int layer)
        {
            var layerObject = new GameObject($"Layer{layer}-{(UILayer)layer}");
            var rect = layerObject.AddComponent<RectTransform>();
            rect.SetParent(UICanvasRoot);
            rect.localScale = Vector3.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchorMax = Vector2.one;
            rect.anchorMin = Vector2.zero;
            rect.sizeDelta = Vector2.zero;
            rect.localRotation = Quaternion.identity;
            rect.localPosition = new Vector3(0, 0, layer * (_isOrthographic ? LAYER_DISTANCE : 0));
            if (layer == (int)UILayer.All)
            {
                UICacheLayer = rect;
                return;
            }

            m_AllWindowLayer[layer] = rect;
            _openUI[layer] = new LayerData(16);
        }


        public RectTransform GetLayerRect(int layer)
        {
            return m_AllWindowLayer[layer];
        }
    }
}
