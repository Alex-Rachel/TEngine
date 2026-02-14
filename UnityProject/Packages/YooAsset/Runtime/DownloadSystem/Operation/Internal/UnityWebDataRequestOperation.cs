using UnityEngine.Networking;
using UnityEngine;

namespace YooAsset
{
    internal class UnityWebDataRequestOperation : UnityWebRequestOperation
    {
        protected enum ESteps
        {
            None,
            CreateRequest,
            Download,
            Done,
        }

        private UnityWebRequestAsyncOperation _requestOperation;
        private ESteps _steps = ESteps.None;

        /// <summary>
        /// 响应的超时时间（单位：秒），在经过Timeout的秒数后尝试中止。
        /// 注意：当Timeout设置为0时，不会应用超时。
        /// 注意：设置的超时值可能应用于Android上的每个URL重定向，这可能会导致响应时间增加。
        /// </summary>
        private readonly int _timeout;

        /// <summary>
        /// 请求结果
        /// </summary>
        public byte[] Result { private set; get; }


        internal UnityWebDataRequestOperation(string url, int timeout) : base(url)
        {
            _timeout = timeout;
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
                    var fileData = _webRequest.downloadHandler.data;
                    if (fileData == null || fileData.Length == 0)
                    {
                        _steps = ESteps.Done;
                        Status = EOperationStatus.Failed;
                        Error = $"URL : {_requestURL} Download handler data is null or empty !";
                    }
                    else
                    {
                        _steps = ESteps.Done;
                        Result = fileData;
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
            DownloadHandlerBuffer handler = new DownloadHandlerBuffer();
            _webRequest = DownloadSystemHelper.NewUnityWebRequestGet(_requestURL);
            _webRequest.timeout = _timeout;
            _webRequest.downloadHandler = handler;
            _webRequest.disposeDownloadHandlerOnDispose = true;
            _requestOperation = _webRequest.SendWebRequest();
        }
    }
}