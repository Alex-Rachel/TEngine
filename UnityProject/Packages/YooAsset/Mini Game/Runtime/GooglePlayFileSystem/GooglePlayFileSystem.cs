#if UNITY_ANDROID && GOOGLE_PLAY
using System.Collections.Generic;
using UnityEngine;
using YooAsset;
using System.Linq;
using System;

public static class GooglePlayFileSystemCreater
{
    public static FileSystemParameters CreateFileSystemParameters(string packageRoot)
    {
        string fileSystemClass = $"{nameof(GooglePlayFileSystem)},YooAsset.MiniGame";
        var fileSystemParams = new FileSystemParameters(fileSystemClass, packageRoot);
        return fileSystemParams;
    }
}

/// <summary>
/// 兼容谷歌Play Asset Delivery的文件系统
/// </summary>
internal class GooglePlayFileSystem : DefaultBuildinFileSystem
{
    public GooglePlayFileSystem()
    {
    }

    public override FSLoadBundleOperation LoadBundleFile(PackageBundle bundle)
    {
        if (bundle.BundleType == (int)EBuildBundleType.AssetBundle)
        {
            var operation = new GPFSLoadAssetBundleOperation(this, bundle);
            return operation;
        }
        else
        {
            string error = $"{nameof(GooglePlayFileSystem)} not support load bundle type : {bundle.BundleType}";
            var operation = new FSLoadBundleCompleteOperation(error);
            return operation;
        }
    }
}
#endif