#if FANTASY_UNITY
namespace Fantasy.Platform.Unity
{
    public static class AppDefine
    {
        public static string RemoteUpdatePath;
        public static bool EditorModel = true;
        public const string VersionName = "version.bytes";
        public const string VersionMD5Name = "version.md5";
        public const string AssetBundleManifestName = "Fantasy";
        public static bool IsEditor => UnityEngine.Application.isEditor && EditorModel;
        public static string AssetBundleSaveDirectory => "Assets/AssetBundles";
        public static string LocalAssetBundlePath => UnityEngine.Application.streamingAssetsPath;
        public static string RemoteAssetBundlePath => UnityEngine.Application.persistentDataPath;
        public static string PersistentDataVersion => $"{UnityEngine.Application.persistentDataPath}/{VersionName}";
        public static string StreamingAssetsVersion => $"{UnityEngine.Application.streamingAssetsPath}/{VersionName}";
    }
}
#endif