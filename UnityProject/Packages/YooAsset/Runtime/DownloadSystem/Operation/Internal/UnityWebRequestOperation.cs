using System;
using UnityEngine.Networking;
using UnityEngine;

namespace YooAsset
{
    internal abstract class UnityWebRequestOperation : AsyncOperationBase
    {
        protected UnityWebRequest _webRequest;
        protected readonly string _requestURL;
        private bool _isAbort = false;

        /// <summary>
        /// HTTP返回码
        /// </summary>
        public long HttpCode { private set; get; }

        /// <summary>
        /// 当前下载的字节数
        /// </summary>
        public long DownloadedBytes { protected set; get; }

        /// <summary>
        /// 当前下载进度（0f - 1f）
        /// </summary>
        public float DownloadProgress { protected set; get; }

        /// <summary>
        /// 请求的URL地址
        /// </summary>
        public string URL
        {
            get { return _requestURL; }
        }

        internal UnityWebRequestOperation(string url)
        {
            _requestURL = url;
        }
        internal override void InternalAbort()
        {
            //TODO
            // 1. 编辑器下停止运行游戏的时候主动终止下载任务
            // 2. 真机上销毁包裹的时候主动终止下载任务
            if (_isAbort == false)
            {
                if (_webRequest != null)
                {
                    _webRequest.Abort();
                    _isAbort = true;
                }
            }
        }

        /// <summary>
        /// 释放下载器
        /// </summary>
        protected void DisposeRequest()
        {
            if (_webRequest != null)
            {
                //注意：引擎底层会自动调用Abort方法
                _webRequest.Dispose();
                _webRequest = null;
            }
        }

        /// <summary>
        /// 检测请求结果
        /// </summary>
        protected bool CheckRequestResult()
        {
            HttpCode = _webRequest.responseCode;

#if UNITY_2020_3_OR_NEWER
            if (_webRequest.result != UnityWebRequest.Result.Success)
            {
                Error = $"URL : {_requestURL} Error : {_webRequest.error}";
                return false;
            }
            else
            {
                return true;
            }
#else
            if (_webRequest.isNetworkError || _webRequest.isHttpError)
            {
                Error = $"URL : {_requestURL} Error : {_webRequest.error}";
                return false;
            }
            else
            {
                return true;
            }
#endif
        }
    }
}