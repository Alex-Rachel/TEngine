using System;
using System.IO;

namespace YooAsset
{
    internal sealed class ClearUnusedCacheManifestFilesOperation : FSClearCacheFilesOperation
    {
        private enum ESteps
        {
            None,
            CheckManifest,
            ClearUnusedCacheFiles,
            Done,
        }

        private readonly DefaultCacheFileSystem _fileSystem;
        private readonly PackageManifest _manifest;
        private ESteps _steps = ESteps.None;


        internal ClearUnusedCacheManifestFilesOperation(DefaultCacheFileSystem fileSystem, PackageManifest manifest)
        {
            _fileSystem = fileSystem;
            _manifest = manifest;
        }
        internal override void InternalStart()
        {
            _steps = ESteps.CheckManifest;
        }
        internal override void InternalUpdate()
        {
            if (_steps == ESteps.None || _steps == ESteps.Done)
                return;

            if (_steps == ESteps.CheckManifest)
            {
                if (_manifest == null)
                {
                    _steps = ESteps.Done;
                    Status = EOperationStatus.Failed;
                    Error = "Can not found active package manifest !";
                }
                else
                {
                    _steps = ESteps.ClearUnusedCacheFiles;
                }
            }

            if (_steps == ESteps.ClearUnusedCacheFiles)
            {
                try
                {
                    string activeManifestFileName = YooAssetSettingsData.GetManifestBinaryFileName(_manifest.PackageName, _manifest.PackageVersion);
                    string activeHashFileName = YooAssetSettingsData.GetPackageHashFileName(_manifest.PackageName, _manifest.PackageVersion);

                    // 注意：如果正在下载资源清单，会有几率触发异常！
                    string directoryRoot = _fileSystem.GetCacheManifestFilesRoot();
                    DirectoryInfo directoryInfo = new DirectoryInfo(directoryRoot);
                    if (directoryInfo.Exists)
                    {
                        foreach (FileInfo fileInfo in directoryInfo.GetFiles())
                        {
                            string fileName = fileInfo.Name;
                            if (fileName == DefaultCacheFileSystemDefine.AppFootPrintFileName)
                                continue;
                            if (fileName == activeManifestFileName || fileName == activeHashFileName)
                                continue;

                            fileInfo.Delete();
                        }
                    }

                    _steps = ESteps.Done;
                    Status = EOperationStatus.Succeed;
                }
                catch (Exception ex)
                {
                    _steps = ESteps.Done;
                    Error = ex.Message;
                    Status = EOperationStatus.Failed;
                }
            }
        }
    }
}