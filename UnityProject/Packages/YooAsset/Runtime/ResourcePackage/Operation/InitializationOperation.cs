using System.Collections.Generic;
using System.Linq;

namespace YooAsset
{
    public class InitializationOperation : AsyncOperationBase
    {
        private enum ESteps
        {
            None,
            Prepare,
            ClearOldFileSystem,
            InitFileSystem,
            CheckInitResult,
            Done,
        }

        private readonly PlayModeImpl _impl;
        private readonly List<FileSystemParameters> _parametersList;
        private List<FileSystemParameters> _cloneList;
        private FSInitializeFileSystemOperation _initFileSystemOp;
        private ESteps _steps = ESteps.None;

        internal InitializationOperation(PlayModeImpl impl, List<FileSystemParameters> parametersList)
        {
            _impl = impl;
            _parametersList = parametersList;
        }
        internal override void InternalStart()
        {
            _steps = ESteps.Prepare;
        }
        internal override void InternalUpdate()
        {
            if (_steps == ESteps.None || _steps == ESteps.Done)
                return;

            if (_steps == ESteps.Prepare)
            {
                if (_parametersList == null || _parametersList.Count == 0)
                {
                    _steps = ESteps.Done;
                    Status = EOperationStatus.Failed;
                    Error = "The file system parameters is empty !";
                    return;
                }

                foreach (var fileSystemParam in _parametersList)
                {
                    if (fileSystemParam == null)
                    {
                        _steps = ESteps.Done;
                        Status = EOperationStatus.Failed;
                        Error = "An empty object exists in the list!";
                        return;
                    }
                }

                _cloneList = _parametersList.ToList();
                _steps = ESteps.ClearOldFileSystem;
            }

            if (_steps == ESteps.ClearOldFileSystem)
            {
                // 注意：初始化失败后可能会残存一些旧的文件系统！
                foreach (var fileSystem in _impl.FileSystems)
                {
                    fileSystem.OnDestroy();
                }

                _impl.FileSystems.Clear();
                _steps = ESteps.InitFileSystem;
            }

            if (_steps == ESteps.InitFileSystem)
            {
                if (_cloneList.Count == 0)
                {
                    _steps = ESteps.Done;
                    Status = EOperationStatus.Succeed;
                }
                else
                {
                    var fileSystemParams = _cloneList[0];
                    _cloneList.RemoveAt(0);

                    IFileSystem fileSystemInstance = fileSystemParams.CreateFileSystem(_impl.PackageName);
                    if (fileSystemInstance == null)
                    {
                        _steps = ESteps.Done;
                        Status = EOperationStatus.Failed;
                        Error = "Failed to create file system instance !";
                        return;
                    }

                    _impl.FileSystems.Add(fileSystemInstance);
                    _initFileSystemOp = fileSystemInstance.InitializeFileSystemAsync();
                    _initFileSystemOp.StartOperation();
                    AddChildOperation(_initFileSystemOp);
                    _steps = ESteps.CheckInitResult;
                }
            }

            if (_steps == ESteps.CheckInitResult)
            {
                _initFileSystemOp.UpdateOperation();
                Progress = _initFileSystemOp.Progress;
                if (_initFileSystemOp.IsDone == false)
                    return;

                if (_initFileSystemOp.Status == EOperationStatus.Succeed)
                {
                    _steps = ESteps.InitFileSystem;
                }
                else
                {
                    _steps = ESteps.Done;
                    Status = EOperationStatus.Failed;
                    Error = _initFileSystemOp.Error;
                    return;
                }
            }
        }
        internal override string InternalGetDesc()
        {
            return $"PlayMode : {_impl.PlayMode}";
        }
    }
}