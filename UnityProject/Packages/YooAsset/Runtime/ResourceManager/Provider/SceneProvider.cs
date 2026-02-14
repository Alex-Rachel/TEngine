using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace YooAsset
{
    internal sealed class SceneProvider : ProviderOperation
    {
        private readonly LoadSceneParameters _loadParams;
        private bool _suspendLoad;
        private FSLoadSceneOperation _loadSceneOp;

        public SceneProvider(ResourceManager manager, string providerGUID, AssetInfo assetInfo, LoadSceneParameters loadParams, bool suspendLoad) : base(manager, providerGUID, assetInfo)
        {
            _loadParams = loadParams;
            _suspendLoad = suspendLoad;
            SceneName = Path.GetFileNameWithoutExtension(assetInfo.AssetPath);
        }
        protected override void ProcessBundleResult()
        {
            if (_loadSceneOp == null)
            {
                _loadSceneOp = BundleResultObject.LoadSceneOperation(MainAssetInfo, _loadParams, _suspendLoad);
                _loadSceneOp.StartOperation();
                AddChildOperation(_loadSceneOp);
            }

            if (IsWaitForAsyncComplete)
                _loadSceneOp.WaitForAsyncComplete();

            // 注意：场景加载中途可以取消挂起
            if (_suspendLoad == false)
                _loadSceneOp.UnSuspendLoad();

            _loadSceneOp.UpdateOperation();
            Progress = _loadSceneOp.Progress;
            if (_loadSceneOp.IsDone == false)
                return;

            if (_loadSceneOp.Status != EOperationStatus.Succeed)
            {
                InvokeCompletion(_loadSceneOp.Error, EOperationStatus.Failed);
            }
            else
            {
                SceneObject = _loadSceneOp.Result;
                InvokeCompletion(string.Empty, EOperationStatus.Succeed);
            }
        }

        /// <summary>
        /// 解除场景加载挂起操作
        /// </summary>
        public void UnSuspendLoad()
        {
            _suspendLoad = false;
        }
    }
}