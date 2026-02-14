using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace TEngine
{
    /// <summary>
    /// 强制更新类型。
    /// </summary>
    public enum UpdateStyle
    {
        /// <summary>
        /// 强制更新(不更新无法进入游戏。)
        /// </summary>
        Force = 1,

        /// <summary>
        /// 非强制(不更新可以进入游戏。)
        /// </summary>
        Optional = 2,
    }

    /// <summary>
    /// 是否提示更新。
    /// </summary>
    public enum UpdateNotice
    {
        /// <summary>
        /// 更新存在提示。
        /// </summary>
        Notice = 1,

        /// <summary>
        /// 更新非提示。
        /// </summary>
        NoNotice = 2,
    }
    /// <summary>
    /// WebGL平台下，
    /// StreamingAssets：跳过远程下载资源直接访问StreamingAssets
    /// Remote：访问远程资源
    /// </summary>
    public enum LoadResWayWebGL
    {
        Remote,
        StreamingAssets,
    }
    
    [CreateAssetMenu(menuName = "TEngine/UpdateSetting", fileName = "UpdateSetting")]
    public class UpdateSetting : ScriptableObject
    {
        /// <summary>
        /// 项目名称。
        /// </summary>
        [SerializeField]
        private string projectName = "Demo";

        public bool Enable
        {
            get
            {
#if ENABLE_HYBRIDCLR
                return true;
#else
                return false;
#endif
            }
        }

        [Header("Auto sync with [HybridCLRGlobalSettings]")]
        public List<string> HotUpdateAssemblies = new List<string>() {"GameProto.dll", "GameLogic.dll" };

        [Header("Need manual setting!")]
        public List<string> AOTMetaAssemblies = new List<string>() { "mscorlib.dll", "System.dll", "System.Core.dll", "TEngine.Runtime.dll" ,"UniTask.dll", "YooAsset.dll"};

        /// <summary>
        /// Dll of main business logic assembly
        /// </summary>
        public string LogicMainDllName = "GameLogic.dll";

        /// <summary>
        /// 程序集文本资产打包Asset后缀名
        /// </summary>
        public string AssemblyTextAssetExtension = ".bytes";

        /// <summary>
        /// 程序集文本资产资源目录
        /// </summary>
        public string AssemblyTextAssetPath = "AssetRaw/DLL";

        [Header("更新设置")]
        public UpdateStyle UpdateStyle = UpdateStyle.Force;

        public UpdateNotice UpdateNotice = UpdateNotice.Notice;

        /// <summary>
        /// 资源服务器地址。
        /// </summary>
        [SerializeField]
        private string ResDownLoadPath = "http://127.0.0.1:8081";

        /// <summary>
        /// 资源服务备用地址。
        /// </summary>
        [SerializeField]
        private string FallbackResDownLoadPath = "http://127.0.0.1:8082";

        /// <summary>
        /// WebGL平台加载本地资源/加载远程资源。
        /// </summary>
        [Header("WebGL设置")]
        [SerializeField]
        private LoadResWayWebGL LoadResWayWebGL = LoadResWayWebGL.Remote;
        /// <summary>
        /// 是否自动你讲打包资源复制到打包后的StreamingAssets地址
        /// </summary>
        [Header("构建资源设置")]
        [SerializeField]
        private bool isAutoAssetCopeToBuildAddress = false;
        /// <summary>
        /// 打包程序资源地址
        /// </summary>
        [SerializeField]
        private string BuildAddress = "../../Builds/Unity_Data/StreamingAssets";
        /// <summary>
        /// 是否使用可寻址资源代替资源路径
        /// 说明：开启此项可以节省运行时清单占用的内存！
        /// </summary>
        [SerializeField, Tooltip("是否使用可寻址资源代替资源路径 说明：开启此项可以节省运行时清单占用的内存！")]
        private bool ReplaceAssetPathWithAddress = false;
        /// <summary>
        /// 是否自动你讲打包资源复制到打包后的StreamingAssets地址
        /// </summary>
        /// <returns></returns>
        public bool IsAutoAssetCopeToBuildAddress()
        {
            return isAutoAssetCopeToBuildAddress;
        }
        /// <summary>
        /// 获取打包程序资源地址
        /// </summary>
        /// <returns></returns>
        public string GetBuildAddress()
        {
            return BuildAddress;
        }

        /// <summary>
        /// 获取是否使用可寻址资源代替资源路径
        /// </summary>
        /// <returns></returns>
        public bool GetReplaceAssetPathWithAddress()
            => ReplaceAssetPathWithAddress;
        
        /// <summary>
        /// 是否加载远程资源
        /// </summary>
        /// <returns></returns>
        public LoadResWayWebGL GetLoadResWayWebGL()
        {
            return LoadResWayWebGL;
        }
        /// <summary>
        /// 获取资源下载路径。
        /// </summary>
        public string GetResDownLoadPath()
        {
            return Path.Combine(ResDownLoadPath, projectName, GetPlatformName()).Replace("\\", "/");
        }

        /// <summary>
        /// 获取备用资源下载路径。
        /// </summary>
        public string GetFallbackResDownLoadPath()
        {
            return Path.Combine(FallbackResDownLoadPath, projectName, GetPlatformName()).Replace("\\", "/");
        }

        /// <summary>
        /// 获取当前的平台名称。
        /// </summary>
        /// <returns>平台名称。</returns>
        public static string GetPlatformName()
        {
#if UNITY_ANDROID
        return "Android";
#elif UNITY_IOS
        return "IOS";
#elif UNITY_WEBGL
        return "WebGL";
#else
            switch (Application.platform)
            {
                case RuntimePlatform.WindowsEditor:
                    return "Windows64";
                case RuntimePlatform.WindowsPlayer:
                    return "Windows64";

                case RuntimePlatform.OSXEditor:
                case RuntimePlatform.OSXPlayer:
                    return "MacOS";

                case RuntimePlatform.IPhonePlayer:
                    return "IOS";

                case RuntimePlatform.Android:
                    return "Android";
                case RuntimePlatform.WebGLPlayer:
                    return "WebGL";

                case RuntimePlatform.PS5:
                    return "PS5";
                default:
                    throw new NotSupportedException($"Platform '{Application.platform.ToString()}' is not supported.");
            }
#endif
        }
    }
}