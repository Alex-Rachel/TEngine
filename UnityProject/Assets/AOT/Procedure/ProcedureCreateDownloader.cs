using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Launcher;
using TEngine;
using UnityEngine;
using UnityEngine.Networking;
using YooAsset;
using ProcedureOwner = TEngine.IFsm<TEngine.IProcedureModule>;
using Utility = TEngine.Utility;

namespace Procedure
{
    public class ProcedureCreateDownloader : ProcedureBase
    {
        private int _curTryCount;

        private const int MAX_TRY_COUNT = 3;

        public override bool UseNativeDialog { get; }

        private ProcedureOwner _procedureOwner;

        private ResourceDownloaderOperation _downloader;

        private int _totalDownloadCount;

        private string _totalSizeMb;

        protected override void OnEnter(ProcedureOwner procedureOwner)
        {
            _procedureOwner = procedureOwner;

            Log.Info("创建补丁下载器");

            LauncherMgr.ShowUI<LoadUpdateUI>($"创建补丁下载器...");

            CreateDownloader().Forget();
        }

        private async UniTaskVoid CreateDownloader()
        {
            await UniTask.Delay(TimeSpan.FromSeconds(0.5f));

            _downloader = _resourceModule.CreateResourceDownloader();

            if (_downloader.TotalDownloadCount == 0)
            {
                Log.Info("Not found any download files !");
                ChangeState<ProcedureDownloadOver>(_procedureOwner);
            }
            else
            {
                //A total of 10 files were found that need to be downloaded
                Log.Info($"Found total {_downloader.TotalDownloadCount} files that need download ！");

                // 发现新更新文件后，挂起流程系统
                // 注意：开发者需要在下载前检测磁盘空间不足
                _totalDownloadCount = _downloader.TotalDownloadCount;
                long totalDownloadBytes = _downloader.TotalDownloadBytes;

                float sizeMb = totalDownloadBytes / 1048576f;
                sizeMb = Mathf.Clamp(sizeMb, 0.1f, float.MaxValue);
                _totalSizeMb = sizeMb.ToString("f1");

                LauncherMgr.ShowMessageBox($"Found update patch files, Total count {_totalDownloadCount} Total size {_totalSizeMb}MB",
                    StartDownFile, Application.Quit);
            }
        }

        void StartDownFile()
        {
            ChangeState<ProcedureDownloadFile>(_procedureOwner);
        }
    }
}