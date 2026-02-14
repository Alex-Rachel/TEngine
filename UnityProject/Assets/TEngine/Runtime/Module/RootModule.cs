using System;
using UnityEngine;

namespace TEngine
{
    /// <summary>
    /// 基础模块。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class RootModule : MonoBehaviour
    {
        private static RootModule _instance = null;

        public static RootModule Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = Utility.Unity.FindObjectOfType<RootModule>();
                }
                return _instance;
            }
        }
        
        private const int DEFAULT_DPI = 96; // default windows dpi

        private float _gameSpeedBeforePause = 1f;

        [SerializeField]
        private Language editorLanguage = Language.Unspecified;

        [SerializeField]
        private string textHelperTypeName = "TEngine.DefaultTextHelper";

        [SerializeField]
        private string logHelperTypeName = "TEngine.DefaultLogHelper";

        [SerializeField]
        private string jsonHelperTypeName = "TEngine.DefaultJsonHelper";

        [SerializeField]
        private int frameRate = 120;

        [SerializeField]
        private float gameSpeed = 1f;

        [SerializeField]
        private bool runInBackground = true;

        [SerializeField]
        private bool neverSleep = true;

        /// <summary>
        /// 获取或设置编辑器语言（仅编辑器内有效）。
        /// </summary>
        public Language EditorLanguage
        {
            get => editorLanguage;
            set => editorLanguage = value;
        }

        /// <summary>
        /// 获取或设置游戏帧率。
        /// </summary>
        public int FrameRate
        {
            get => frameRate;
            set => Application.targetFrameRate = frameRate = value;
        }

        /// <summary>
        /// 获取或设置游戏速度。
        /// </summary>
        public float GameSpeed
        {
            get => gameSpeed;
            set => Time.timeScale = gameSpeed = value >= 0f ? value : 0f;
        }

        /// <summary>
        /// 获取游戏是否暂停。
        /// </summary>
        public bool IsGamePaused => gameSpeed <= 0f;

        /// <summary>
        /// 获取是否正常游戏速度。
        /// </summary>
        public bool IsNormalGameSpeed => Math.Abs(gameSpeed - 1f) < 0.01f;

        /// <summary>
        /// 获取或设置是否允许后台运行。
        /// </summary>
        public bool RunInBackground
        {
            get => runInBackground;
            set => Application.runInBackground = runInBackground = value;
        }

        /// <summary>
        /// 获取或设置是否禁止休眠。
        /// </summary>
        public bool NeverSleep
        {
            get => neverSleep;
            set
            {
                neverSleep = value;
                Screen.sleepTimeout = value ? SleepTimeout.NeverSleep : SleepTimeout.SystemSetting;
            }
        }

        /// <summary>
        /// 游戏框架模块初始化。
        /// </summary>
        private void Awake()
        {
            _instance = this;
            InitTextHelper();
            InitLogHelper();
            Log.Info("Unity Version: {0}", Application.unityVersion);

            InitJsonHelper();

            Utility.Converter.ScreenDpi = Screen.dpi;
            if (Utility.Converter.ScreenDpi <= 0)
            {
                Utility.Converter.ScreenDpi = DEFAULT_DPI;
            }

            Application.targetFrameRate = frameRate;
            Time.timeScale = gameSpeed;
            Application.runInBackground = runInBackground;
            Screen.sleepTimeout = neverSleep ? SleepTimeout.NeverSleep : SleepTimeout.SystemSetting;

            Application.lowMemory += OnLowMemory;
            GameTime.StartFrame();
        }

        private void Update()
        {
            GameTime.StartFrame();
            ModuleSystem.Update(GameTime.deltaTime, GameTime.unscaledDeltaTime);
        }

        private void FixedUpdate()
        {
            GameTime.StartFrame();
        }

        private void LateUpdate()
        {
            GameTime.StartFrame();
        }

        private void OnApplicationQuit()
        {
            Application.lowMemory -= OnLowMemory;
            StopAllCoroutines();
        }

        private void OnDestroy()
        {
#if !UNITY_EDITOR
            ModuleSystem.Shutdown();
#endif
        }

        /// <summary>
        /// 暂停游戏。
        /// </summary>
        public void PauseGame()
        {
            if (IsGamePaused)
            {
                return;
            }

            _gameSpeedBeforePause = GameSpeed;
            GameSpeed = 0f;
        }

        /// <summary>
        /// 恢复游戏。
        /// </summary>
        public void ResumeGame()
        {
            if (!IsGamePaused)
            {
                return;
            }

            GameSpeed = _gameSpeedBeforePause;
        }

        /// <summary>
        /// 重置为正常游戏速度。
        /// </summary>
        public void ResetNormalGameSpeed()
        {
            if (IsNormalGameSpeed)
            {
                return;
            }

            GameSpeed = 1f;
        }

        internal void Shutdown()
        {
            Destroy(gameObject);
        }

        private void InitTextHelper()
        {
            if (string.IsNullOrEmpty(textHelperTypeName))
            {
                return;
            }

            Type textHelperType = Utility.Assembly.GetType(textHelperTypeName);
            if (textHelperType == null)
            {
                Log.Error("Can not find text helper type '{0}'.", textHelperTypeName);
                return;
            }

            Utility.Text.ITextHelper textHelper = (Utility.Text.ITextHelper)Activator.CreateInstance(textHelperType);
            if (textHelper == null)
            {
                Log.Error("Can not create text helper instance '{0}'.", textHelperTypeName);
                return;
            }

            Utility.Text.SetTextHelper(textHelper);
        }

        private void InitLogHelper()
        {
            if (string.IsNullOrEmpty(logHelperTypeName))
            {
                return;
            }

            Type logHelperType = Utility.Assembly.GetType(logHelperTypeName);
            if (logHelperType == null)
            {
                throw new GameFrameworkException(Utility.Text.Format("Can not find log helper type '{0}'.",
                    logHelperTypeName));
            }

            GameFrameworkLog.ILogHelper
                logHelper = (GameFrameworkLog.ILogHelper)Activator.CreateInstance(logHelperType);
            if (logHelper == null)
            {
                throw new GameFrameworkException(Utility.Text.Format("Can not create log helper instance '{0}'.",
                    logHelperTypeName));
            }

            GameFrameworkLog.SetLogHelper(logHelper);
        }

        private void InitJsonHelper()
        {
            if (string.IsNullOrEmpty(jsonHelperTypeName))
            {
                return;
            }

            Type jsonHelperType = Utility.Assembly.GetType(jsonHelperTypeName);
            if (jsonHelperType == null)
            {
                Log.Error("Can not find JSON helper type '{0}'.", jsonHelperTypeName);
                return;
            }

            Utility.Json.IJsonHelper jsonHelper = (Utility.Json.IJsonHelper)Activator.CreateInstance(jsonHelperType);
            if (jsonHelper == null)
            {
                Log.Error("Can not create JSON helper instance '{0}'.", jsonHelperTypeName);
                return;
            }

            Utility.Json.SetJsonHelper(jsonHelper);
        }

        private void OnLowMemory()
        {
            Log.Warning("Low memory reported...");

            IObjectPoolModule objectPoolModule = ModuleSystem.GetModule<IObjectPoolModule>();
            if (objectPoolModule != null)
            {
                objectPoolModule.ReleaseAllUnused();
            }

            IResourceModule resourceModule = ModuleSystem.GetModule<IResourceModule>();
            if (resourceModule != null)
            {
                resourceModule.OnLowMemory();
            }
        }
    }
}