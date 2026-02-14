using System;
using System.IO;

namespace YooAsset
{
    internal sealed class ClearAllCacheManifestFilesOperation : FSClearCacheFilesOperation
    {
        private enum ESteps
        {
            None,
            ClearAllCacheFiles,
            Done,
        }

        private readonly DefaultCacheFileSystem _fileSystem;
        private ESteps _steps = ESteps.None;


        internal ClearAllCacheManifestFilesOperation(DefaultCacheFileSystem fileSystem)
        {
            _fileSystem = fileSystem;
        }
        internal override void InternalStart()
        {
            _steps = ESteps.ClearAllCacheFiles;
        }
        internal override void InternalUpdate()
        {
            if (_steps == ESteps.None || _steps == ESteps.Done)
                return;

            if (_steps == ESteps.ClearAllCacheFiles)
            {
                try
                {
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