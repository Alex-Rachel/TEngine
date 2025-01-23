using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace TEngine
{
    public static partial class Utility
    {
        /// <summary>
        /// Http 相关的实用函数。
        /// </summary>
        public static partial class Http
        {
            /// <summary>
            /// GET请求与获取结果。
            /// </summary>
            /// <param name="url">网络URL。</param>
            /// <param name="timeout">超时时间。</param>
            /// <returns>请求结果。</returns>
            public static async UniTask<string> Get(string url, float timeout = 5f)
            {
                var cts = new CancellationTokenSource();
                cts.CancelAfterSlim(TimeSpan.FromSeconds(timeout));

                using UnityWebRequest unityWebRequest = UnityWebRequest.Get(url);
                return await SendWebRequest(unityWebRequest, cts);
            }

            /// <summary>
            /// Post请求与获取结果.
            /// </summary>
            /// <param name="url">网络URL。</param>
            /// <param name="postData">Post数据。</param>
            /// <param name="timeout">超时时间。</param>
            /// <returns>请求结果。</returns>
            public static async UniTask<string> Post(string url, string postData, float timeout = 5f)
            {
                var cts = new CancellationTokenSource();
                cts.CancelAfterSlim(TimeSpan.FromSeconds(timeout));

                using UnityWebRequest unityWebRequest = UnityWebRequest.PostWwwForm(url, postData);
                return await SendWebRequest(unityWebRequest, cts);
            }

            /// <summary>
            /// Post请求与获取结果.
            /// </summary>
            /// <param name="url">网络URL。</param>
            /// <param name="formFields">Post数据。</param>
            /// <param name="timeout">超时时间。</param>
            /// <returns>请求结果。</returns>
            public static async UniTask<string> Post(string url, Dictionary<string, string> formFields, float timeout = 5f)
            {
                var cts = new CancellationTokenSource();
                cts.CancelAfterSlim(TimeSpan.FromSeconds(timeout));

                using UnityWebRequest unityWebRequest = UnityWebRequest.Post(url, formFields);
                return await SendWebRequest(unityWebRequest, cts);
            }

            /// <summary>
            /// Post请求与获取结果.
            /// </summary>
            /// <param name="url">网络URL。</param>
            /// <param name="formData">Post数据。</param>
            /// <param name="timeout">超时时间。</param>
            /// <returns>请求结果。</returns>
            public static async UniTask<string> Post(string url, WWWForm formData, float timeout = 5f)
            {
                var cts = new CancellationTokenSource();
                cts.CancelAfterSlim(TimeSpan.FromSeconds(timeout));

                using UnityWebRequest unityWebRequest = UnityWebRequest.Post(url, formData);
                return await SendWebRequest(unityWebRequest, cts);
            }

            /// <summary>
            /// 抛出网络请求。
            /// </summary>
            /// <param name="unityWebRequest">UnityWebRequest。</param>
            /// <param name="cts">CancellationTokenSource。</param>
            /// <returns>请求结果。</returns>
            public static async UniTask<string> SendWebRequest(UnityWebRequest unityWebRequest, CancellationTokenSource cts)
            {
                try
                {
                    var (isCanceled, _) = await unityWebRequest.SendWebRequest().WithCancellation(cts.Token).SuppressCancellationThrow();
                    if (isCanceled)
                    {
                        Log.Warning($"HttpPost {unityWebRequest.url} be canceled!");
                        unityWebRequest.Dispose();
                        return string.Empty;
                    }
                }
                catch (OperationCanceledException ex)
                {
                    if (ex.CancellationToken == cts.Token)
                    {
                        Log.Warning("HttpPost Timeout");
                        unityWebRequest.Dispose();
                        return string.Empty;
                    }
                }

                string ret = unityWebRequest.downloadHandler.text;
                unityWebRequest.Dispose();
                return ret;
            }
        }
    }
}