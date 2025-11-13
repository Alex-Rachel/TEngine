using TEngine;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace TEngine
{
    [DisallowMultipleComponent]
    [UnityEngine.Scripting.Preserve]
    public sealed partial class UIComponent : MonoBehaviour
    {
        [SerializeField]
        private GameObject uiRoot = null;

        [SerializeField]
        private bool _isOrthographic = true;

        private Transform _instanceRoot = null;

        private IUIModule _uiModule;

        public const int UIHideLayer = 2; // Ignore Raycast
        public const int UIShowLayer = 5; // UI


        private void Awake()
        {
            _uiModule = ModuleSystem.GetModule<IUIModule>();
            if (uiRoot == null)
            {
                throw new GameFrameworkException("UIRoot Prefab is invalid.");
            }

            GameObject obj = Instantiate(uiRoot, Vector3.zero, Quaternion.identity);
            obj.name = "[UI Root]";
            _instanceRoot = obj.transform;
            Object.DontDestroyOnLoad(_instanceRoot);
            _uiModule.Initialize(_instanceRoot, _isOrthographic);
        }

        private void Start()
        {
            _uiModule.SetTimerManager(ModuleSystem.GetModule<ITimerModule>());
        }


        #region 设置安全区域

        /// <summary>
        /// 设置屏幕安全区域（异形屏支持）。
        /// </summary>
        /// <param name="safeRect">安全区域</param>
        public void ApplyScreenSafeRect(Rect safeRect)
        {
            CanvasScaler scaler = _uiModule.UICanvasRoot.GetComponent<CanvasScaler>();
            if (scaler == null)
            {
                Log.Error($"Not found {nameof(CanvasScaler)} !");
                return;
            }

            // Convert safe area rectangle from absolute pixels to UGUI coordinates
            float rateX = scaler.referenceResolution.x / Screen.width;
            float rateY = scaler.referenceResolution.y / Screen.height;
            float posX = (int)(safeRect.position.x * rateX);
            float posY = (int)(safeRect.position.y * rateY);
            float width = (int)(safeRect.size.x * rateX);
            float height = (int)(safeRect.size.y * rateY);

            float offsetMaxX = scaler.referenceResolution.x - width - posX;
            float offsetMaxY = scaler.referenceResolution.y - height - posY;

            // 注意：安全区坐标系的原点为左下角
            var rectTrans = _uiModule.UICanvasRoot.transform as RectTransform;
            if (rectTrans != null)
            {
                rectTrans.offsetMin = new Vector2(posX, posY); //锚框状态下的屏幕左下角偏移向量
                rectTrans.offsetMax = new Vector2(-offsetMaxX, -offsetMaxY); //锚框状态下的屏幕右上角偏移向量
            }
        }

        /// <summary>
        /// 模拟IPhoneX异形屏
        /// </summary>
        public void SimulateIPhoneXNotchScreen()
        {
            Rect rect;
            if (Screen.height > Screen.width)
            {
                // 竖屏Portrait
                float deviceWidth = 1125;
                float deviceHeight = 2436;
                rect = new Rect(0f / deviceWidth, 102f / deviceHeight, 1125f / deviceWidth, 2202f / deviceHeight);
            }
            else
            {
                // 横屏Landscape
                float deviceWidth = 2436;
                float deviceHeight = 1125;
                rect = new Rect(132f / deviceWidth, 63f / deviceHeight, 2172f / deviceWidth, 1062f / deviceHeight);
            }

            Rect safeArea = new Rect(Screen.width * rect.x, Screen.height * rect.y, Screen.width * rect.width, Screen.height * rect.height);
            ApplyScreenSafeRect(safeArea);
        }

        #endregion
    }
}