using System;
using UnityEngine;
using UnityEngine.Serialization;
using YooAsset;

namespace TEngine
{
    /// <summary>
    /// 资源组件。
    /// </summary>
    [DisallowMultipleComponent]
    public class ResourceModuleDriver : MonoBehaviour
    {
        #region Propreties

        private const int DEFAULT_PRIORITY = 0;

        private IResourceModule _resourceModule;

        private bool _forceUnloadUnusedAssets = false;

        private bool _preorderUnloadUnusedAssets = false;

        private bool _performGCCollect = false;

        private AsyncOperation _asyncOperation = null;

        private float _lastUnloadUnusedAssetsOperationElapseSeconds = 0f;

        [SerializeField]
        private float minUnloadUnusedAssetsInterval = 60f;

        [SerializeField]
        private float maxUnloadUnusedAssetsInterval = 300f;

        [SerializeField]
        private bool useSystemUnloadUnusedAssets = true;

        /// <summary>
        /// 当前最新的包裹版本。
        /// </summary>
        public string PackageVersion { set; get; }

        /// <summary>
        /// 资源包名称。
        /// </summary>
        [SerializeField]
        private string packageName = "DefaultPackage";

        /// <summary>
        /// 资源包名称。
        /// </summary>
        public string PackageName
        {
            get => packageName;
            set => packageName = value;
        }

        /// <summary>
        /// 资源系统运行模式。
        /// </summary>
        [SerializeField]
        private EPlayMode playMode = EPlayMode.EditorSimulateMode;

        /// <summary>
        /// 资源系统运行模式。
        /// <remarks>编辑器内优先使用。</remarks>
        /// </summary>
        public EPlayMode PlayMode
        {
            get
            {
#if UNITY_EDITOR
                //编辑器模式使用。
                return (EPlayMode)UnityEditor.EditorPrefs.GetInt("EditorPlayMode");
#else
                if (playMode == EPlayMode.EditorSimulateMode)
                {
                    playMode = EPlayMode.OfflinePlayMode;
                }
                //运行时使用。
                return playMode;
#endif
            }
            set
            {
#if UNITY_EDITOR
                playMode = value;
#endif
            }
        }
        
        [SerializeField]
        private EncryptionType encryptionType = EncryptionType.None;
        
        /// <summary>
        /// 资源模块的加密类型。
        /// </summary>
        public EncryptionType EncryptionType => encryptionType;

        /// <summary>
        /// 是否支持边玩边下载。
        /// </summary>
        [SerializeField]
        public bool updatableWhilePlaying = false;

        /// <summary>
        /// 是否支持边玩边下载。
        /// </summary>
        public bool UpdatableWhilePlaying => updatableWhilePlaying;

        /// <summary>
        /// 设置异步系统参数，每帧执行消耗的最大时间切片（单位：毫秒）
        /// </summary>
        [SerializeField]
        public long milliseconds = 30;

        /// <summary>
        /// 自动释放资源引用计数为0的资源包
        /// </summary>
        [SerializeField]
        public bool autoUnloadBundleWhenUnused = false;

        public int downloadingMaxNum = 10;

        /// <summary>
        /// 获取或设置同时最大下载数目。
        /// </summary>
        public int DownloadingMaxNum
        {
            get => downloadingMaxNum;
            set => downloadingMaxNum = value;
        }

        [SerializeField]
        public int failedTryAgain = 3;

        public int FailedTryAgain
        {
            get => failedTryAgain;
            set => failedTryAgain = value;
        }

        /// <summary>
        /// 获取当前资源适用的游戏版本号。
        /// </summary>
        public string ApplicableGameVersion => _resourceModule.ApplicableGameVersion;

        /// <summary>
        /// 获取当前内部资源版本号。
        /// </summary>
        public int InternalResourceVersion => _resourceModule.InternalResourceVersion;

        /// <summary>
        /// 获取或设置无用资源释放的最小间隔时间，以秒为单位。
        /// </summary>
        public float MinUnloadUnusedAssetsInterval
        {
            get => minUnloadUnusedAssetsInterval;
            set => minUnloadUnusedAssetsInterval = value;
        }

        /// <summary>
        /// 获取或设置无用资源释放的最大间隔时间，以秒为单位。
        /// </summary>
        public float MaxUnloadUnusedAssetsInterval
        {
            get => maxUnloadUnusedAssetsInterval;
            set => maxUnloadUnusedAssetsInterval = value;
        }

        /// <summary>
        /// 使用系统释放无用资源策略。
        /// </summary>
        public bool UseSystemUnloadUnusedAssets
        {
            get => useSystemUnloadUnusedAssets;
            set => useSystemUnloadUnusedAssets = value;
        }

        /// <summary>
        /// 获取无用资源释放的等待时长，以秒为单位。
        /// </summary>
        public float LastUnloadUnusedAssetsOperationElapseSeconds => _lastUnloadUnusedAssetsOperationElapseSeconds;

        [SerializeField]
        private float assetAutoReleaseInterval = 60f;

        [SerializeField]
        private int assetCapacity = 64;

        [SerializeField]
        private float assetExpireTime = 60f;

        [SerializeField]
        private int assetPriority = 0;

        /// <summary>
        /// 获取或设置资源对象池自动释放可释放对象的间隔秒数。
        /// </summary>
        public float AssetAutoReleaseInterval
        {
            get => _resourceModule.AssetAutoReleaseInterval;
            set => _resourceModule.AssetAutoReleaseInterval = assetAutoReleaseInterval = value;
        }

        /// <summary>
        /// 获取或设置资源对象池的容量。
        /// </summary>
        public int AssetCapacity
        {
            get => _resourceModule.AssetCapacity;
            set => _resourceModule.AssetCapacity = assetCapacity = value;
        }

        /// <summary>
        /// 获取或设置资源对象池对象过期秒数。
        /// </summary>
        public float AssetExpireTime
        {
            get => _resourceModule.AssetExpireTime;
            set => _resourceModule.AssetExpireTime = assetExpireTime = value;
        }

        /// <summary>
        /// 获取或设置资源对象池的优先级。
        /// </summary>
        public int AssetPriority
        {
            get => _resourceModule.AssetPriority;
            set => _resourceModule.AssetPriority = assetPriority = value;
        }

        #endregion

        private void Start()
        {
            _resourceModule = ModuleSystem.GetModule<IResourceModule>();
            if (_resourceModule == null)
            {
                Log.Fatal("Resource module is invalid.");
                return;
            }

            if (PlayMode == EPlayMode.EditorSimulateMode)
            {
                Log.Info("During this run, ResourceModule will use editor resource files, which you should validate first.");
#if !UNITY_EDITOR
                PlayMode = EPlayMode.OfflinePlayMode;
#endif
            }

            _resourceModule.DefaultPackageName = PackageName;
            _resourceModule.PlayMode = PlayMode;
            _resourceModule.EncryptionType = encryptionType;
            _resourceModule.Milliseconds = milliseconds;
            _resourceModule.AutoUnloadBundleWhenUnused = autoUnloadBundleWhenUnused;
            _resourceModule.HostServerURL = Settings.UpdateSetting.GetResDownLoadPath();
            _resourceModule.FallbackHostServerURL = Settings.UpdateSetting.GetFallbackResDownLoadPath();
            _resourceModule.LoadResWayWebGL=Settings.UpdateSetting.GetLoadResWayWebGL();
            _resourceModule.DownloadingMaxNum = DownloadingMaxNum;
            _resourceModule.FailedTryAgain = FailedTryAgain;
            _resourceModule.UpdatableWhilePlaying = UpdatableWhilePlaying;
            _resourceModule.Initialize();
            _resourceModule.AssetAutoReleaseInterval = assetAutoReleaseInterval;
            _resourceModule.AssetCapacity = assetCapacity;
            _resourceModule.AssetExpireTime = assetExpireTime;
            _resourceModule.AssetPriority = assetPriority;
            _resourceModule.SetForceUnloadUnusedAssetsAction(ForceUnloadUnusedAssets);
            Log.Info($"ResourceModule Run Mode：{PlayMode}");
        }

        #region 释放资源

        /// <summary>
        /// 强制执行释放未被使用的资源。
        /// </summary>
        /// <param name="performGCCollect">是否使用垃圾回收。</param>
        public void ForceUnloadUnusedAssets(bool performGCCollect)
        {
            _forceUnloadUnusedAssets = true;
            if (performGCCollect)
            {
                _performGCCollect = true;
            }
        }


        private void Update()
        {
            _lastUnloadUnusedAssetsOperationElapseSeconds += Time.unscaledDeltaTime;
            if (_asyncOperation == null && (_forceUnloadUnusedAssets || _lastUnloadUnusedAssetsOperationElapseSeconds >= maxUnloadUnusedAssetsInterval ||
                                            _preorderUnloadUnusedAssets && _lastUnloadUnusedAssetsOperationElapseSeconds >= minUnloadUnusedAssetsInterval))
            {
                Log.Info("Unload unused assets...");
                _forceUnloadUnusedAssets = false;
                _preorderUnloadUnusedAssets = false;
                _lastUnloadUnusedAssetsOperationElapseSeconds = 0f;
                _asyncOperation = Resources.UnloadUnusedAssets();
                if (useSystemUnloadUnusedAssets)
                {
                    _resourceModule.UnloadUnusedAssets();
                }
            }

            if (_asyncOperation is { isDone: true })
            {
                _asyncOperation = null;
                if (_performGCCollect)
                {
                    Log.Info("GC.Collect...");
                    _performGCCollect = false;
                    GC.Collect();
                }
            }
        }

        #endregion
    }
}