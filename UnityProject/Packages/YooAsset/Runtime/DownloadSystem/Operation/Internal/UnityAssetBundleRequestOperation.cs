using UnityEngine.Networking;
using UnityEngine;

namespace YooAsset
{
    internal class UnityAssetBundleRequestOperation : UnityWebRequestOperation
    {
        protected enum ESteps
        {
            None,
            CreateRequest,
            Download,
            Done,
        }

        private UnityWebRequestAsyncOperation _requestOperation;
        private DownloadHandlerAssetBundle _downloadhandler;
        private readonly PackageBundle _packageBundle;
        private readonly bool _disableUnityWebCache;
        private ESteps _steps = ESteps.None;

        /// <summary>
        /// 请求结果
        /// </summary>
        public AssetBundle Result { private set; get; }

        internal UnityAssetBundleRequestOperation(PackageBundle packageBundle, bool disableUnityWebCache, string url) : base(url)
        {
            _packageBundle = packageBundle;
            _disableUnityWebCache = disableUnityWebCache;
        }
        internal override void InternalStart()
        {
            _steps = ESteps.CreateRequest;
        }
        internal override void InternalUpdate()
        {
            if (_steps == ESteps.None || _steps == ESteps.Done)
                return;

            if (_steps == ESteps.CreateRequest)
            {
                CreateWebRequest();
                _steps = ESteps.Download;
            }

            if (_steps == ESteps.Download)
            {
                DownloadProgress = _webRequest.downloadProgress;
                DownloadedBytes = (long)_webRequest.downloadedBytes;
                Progress = _requestOperation.progress;
                if (_requestOperation.isDone == false)
                    return;

                if (CheckRequestResult())
                {
                    AssetBundle assetBundle = _downloadhandler.assetBundle;
                    if (assetBundle == null)
                    {
                        _steps = ESteps.Done;
                        Status = EOperationStatus.Failed;
                        Error = $"URL : {_requestURL} Download handler asset bundle object is null !";
                    }
                    else
                    {
                        _steps = ESteps.Done;
                        Result = assetBundle;
                        Status = EOperationStatus.Succeed;
                    }
                }
                else
                {
                    _steps = ESteps.Done;
                    Status = EOperationStatus.Failed;
                }

                // 注意：最终释放请求器
                DisposeRequest();
            }
        }

        private void CreateWebRequest()
        {
            _downloadhandler = CreateWebDownloadHandler();
            _webRequest = DownloadSystemHelper.NewUnityWebRequestGet(_requestURL);
            _webRequest.downloadHandler = _downloadhandler;
            _webRequest.disposeDownloadHandlerOnDispose = true;
            _requestOperation = _webRequest.SendWebRequest();
        }
        private DownloadHandlerAssetBundle CreateWebDownloadHandler()
        {
            if (_disableUnityWebCache)
            {
                var downloadhandler = new DownloadHandlerAssetBundle(_requestURL, _packageBundle.UnityCRC);
#if UNITY_2020_3_OR_NEWER
                downloadhandler.autoLoadAssetBundle = false;
#endif
                return downloadhandler;
            }
            else
            {
                // 注意：优先从浏览器缓存里获取文件
                // The file hash defining the version of the asset bundle.
                Hash128 fileHash = Hash128.Parse(_packageBundle.FileHash);
                var downloadhandler = new DownloadHandlerAssetBundle(_requestURL, fileHash, _packageBundle.UnityCRC);
#if UNITY_2020_3_OR_NEWER
                downloadhandler.autoLoadAssetBundle = false;
#endif
                return downloadhandler;
            }
        }
    }
}