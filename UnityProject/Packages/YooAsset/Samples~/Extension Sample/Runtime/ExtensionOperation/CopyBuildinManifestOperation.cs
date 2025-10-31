using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using YooAsset;

/// <summary>
/// 拷贝内置清单文件到沙盒目录
/// </summary>
public class CopyBuildinManifestOperation : GameAsyncOperation
{
    private enum ESteps
    {
        None,
        CheckHashFile,
        UnpackHashFile,
        CheckManifestFile,
        UnpackManifestFile,
        Done,
    }

    private readonly string _packageName;
    private readonly string _packageVersion;
    private ESteps _steps = ESteps.None;
    private UnityWebFileRequestOperation _hashFileRequestOp;
    private UnityWebFileRequestOperation _manifestFileRequestOp;

    public CopyBuildinManifestOperation(string packageName, string packageVersion)
    {
        _packageName = packageName;
        _packageVersion = packageVersion;
    }
    protected override void OnStart()
    {
        _steps = ESteps.CheckHashFile;
    }
    protected override void OnUpdate()
    {
        if (_steps == ESteps.None || _steps == ESteps.Done)
            return;

        if (_steps == ESteps.CheckHashFile)
        {
            string hashFilePath = GetCacheHashFilePath();
            if (File.Exists(hashFilePath))
            {
                _steps = ESteps.CheckManifestFile;
                return;
            }

            _steps = ESteps.UnpackHashFile;
        }

        if (_steps == ESteps.UnpackHashFile)
        {
            if(_hashFileRequestOp == null)
            {
                string sourcePath = GetBuildinHashFilePath();
                string destPath = GetCacheHashFilePath();
                string url = DownloadSystemHelper.ConvertToWWWPath(sourcePath);
                _hashFileRequestOp = new UnityWebFileRequestOperation(url, destPath, 60);
                OperationSystem.StartOperation(_packageName, _hashFileRequestOp);
            }

            if (_hashFileRequestOp.IsDone == false)
                return;

            if (_hashFileRequestOp.Status == EOperationStatus.Succeed)
            {
                _steps = ESteps.CheckManifestFile;
            }
            else
            {
                _steps = ESteps.Done;
                Status = EOperationStatus.Failed;
                Error = _hashFileRequestOp.Error;
            }
        }

        if (_steps == ESteps.CheckManifestFile)
        {
            string manifestFilePath = GetCacheManifestFilePath();
            if (File.Exists(manifestFilePath))
            {
                _steps = ESteps.Done;
                Status = EOperationStatus.Succeed;
                return;
            }

            _steps = ESteps.UnpackManifestFile;
        }

        if (_steps == ESteps.UnpackManifestFile)
        {
            if (_manifestFileRequestOp == null)
            {
                string sourcePath = GetBuildinManifestFilePath();
                string destPath = GetCacheManifestFilePath();
                string url = DownloadSystemHelper.ConvertToWWWPath(sourcePath);
                _manifestFileRequestOp = new UnityWebFileRequestOperation(url, destPath, 60);
                OperationSystem.StartOperation(_packageName, _manifestFileRequestOp);
            }

            if (_manifestFileRequestOp.IsDone == false)
                return;

            if (_manifestFileRequestOp.Status == EOperationStatus.Succeed)
            {
                _steps = ESteps.Done;
                Status = EOperationStatus.Succeed;
            }
            else
            {
                _steps = ESteps.Done;
                Status = EOperationStatus.Failed;
                Error = _manifestFileRequestOp.Error;
            }
        }
    }
    protected override void OnAbort()
    {
    }

    private string GetBuildinYooRoot()
    {
        return YooAssetSettingsData.GetYooDefaultBuildinRoot();
    }
    private string GetBuildinHashFilePath()
    {
        string fileRoot = GetBuildinYooRoot();
        string fileName = YooAssetSettingsData.GetPackageHashFileName(_packageName, _packageVersion);
        return PathUtility.Combine(fileRoot, _packageName, fileName);
    }
    private string GetBuildinManifestFilePath()
    {
        string fileRoot = GetBuildinYooRoot();
        string fileName = YooAssetSettingsData.GetManifestBinaryFileName(_packageName, _packageVersion);
        return PathUtility.Combine(fileRoot, _packageName, fileName);
    }

    private string GetCacheYooRoot()
    {
        return YooAssetSettingsData.GetYooDefaultCacheRoot();
    }
    private string GetCacheHashFilePath()
    {
        string fileRoot = GetCacheYooRoot();
        string fileName = YooAssetSettingsData.GetPackageHashFileName(_packageName, _packageVersion);
        return PathUtility.Combine(fileRoot, _packageName, fileName);
    }
    private string GetCacheManifestFilePath()
    {
        string fileRoot = GetCacheYooRoot();
        string fileName = YooAssetSettingsData.GetManifestBinaryFileName(_packageName, _packageVersion);
        return PathUtility.Combine(fileRoot, _packageName, fileName);
    }
}
