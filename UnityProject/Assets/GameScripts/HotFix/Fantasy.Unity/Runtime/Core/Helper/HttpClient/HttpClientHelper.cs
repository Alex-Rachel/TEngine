#if !FANTASY_WEBGL
using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using Fantasy.Async;
using Fantasy.Helper;
using Fantasy.Pool;
#pragma warning disable CS8603 // Possible null reference return.

namespace Fantasy.Http
{
    /// <summary>
    /// HTTP帮助类
    /// </summary>
    public static partial class HttpClientHelper
    {
        private static readonly HttpClient Client = new HttpClient(new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (sender, cert, chain, sslPolicyErrors) => true
        });

        /// <summary>
        /// 用Post方式请求string数据
        /// </summary>
        /// <param name="url"></param>
        /// <param name="content"></param>
        /// <returns></returns>
        /// <exception cref="Exception"></exception>
        public static async FTask<string> CallNotDeserializeByPost(string url, HttpContent content)
        {
            var response = await Client.PostAsync(url, content);

            if (response.StatusCode != HttpStatusCode.OK)
            {
                throw new Exception($"Unable to connect to server url {(object)url} HttpStatusCode:{(object)response.StatusCode}");
            }

            return await response.Content.ReadAsStringAsync();
        }
        
        /// <summary>
        /// 用Get方式请求string数据
        /// </summary>
        /// <param name="url"></param>
        /// <returns></returns>
        /// <exception cref="Exception"></exception>
        public static async FTask<string> CallNotDeserializeByGet(string url)
        {
            var response = await Client.GetAsync(url);

            if (response.StatusCode != HttpStatusCode.OK)
            {
                throw new Exception($"Unable to connect to server url {(object)url} HttpStatusCode:{(object)response.StatusCode}");
            }

            return await response.Content.ReadAsStringAsync();
        }

        /// <summary>
        /// 用Post方式请求JSON数据，并自动把JSON转换为对象。
        /// </summary>
        /// <param name="url"></param>
        /// <param name="content"></param>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        public static async FTask<T> CallByPost<T>(string url, HttpContent content)
        {
            return await Deserialize<T>(url, await Client.PostAsync(url, content));
        }

        /// <summary>
        /// 用Post方式请求JSON数据，并自动把JSON转换为对象。
        /// </summary>
        /// <param name="url"></param>
        /// <param name="method"></param>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        public static async FTask<T> CallByPost<T>(string url, HttpMethod method)
        {
            return await Deserialize<T>(url, await Client.SendAsync(new HttpRequestMessage(method, url)));
        }
        
        /// <summary>
        /// 用Get方式请求JSON数据，并自动把JSON转换为对象。
        /// </summary>
        /// <param name="url"></param>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        public static async FTask<T> CallByGet<T>(string url)
        {
            return await Deserialize<T>(url, await Client.GetAsync(url));
        }
        
        /// <summary>
        /// 用Post方式请求JSON数据，并自动把JSON转换为对象。
        /// </summary>
        /// <param name="url"></param>
        /// <param name="id"></param>
        /// <param name="authentication"></param>
        /// <param name="method"></param>
        /// <param name="params"></param>
        /// <typeparam name="TRequest"></typeparam>
        /// <typeparam name="TResponse"></typeparam>
        /// <returns></returns>
        public static async FTask<TResponse> Call<TRequest, TResponse>(string url, int id, AuthenticationHeaderValue authentication, string method, params object[] @params) where TRequest : class, IJsonRpcRequest, new()
        {
            var request = Pool<TRequest>.Rent();
            using var httpClientPool = HttpClientPool.Create();
            var client = httpClientPool.Client;
            client.DefaultRequestHeaders.Authorization = authentication;

            try
            {
                request.Init(method, id, @params);
                var content = new StringContent(request.ToJson(), Encoding.UTF8, "application/json");
                var response = await Deserialize<TResponse>(url, await client.PostAsync(url, content));
                return response;
            }
            catch (Exception e)
            {
                Log.Error(e);
            }
            finally
            {
                Pool<TRequest>.Return(request);
            }

            return default;
        }

        private static async FTask<T> Deserialize<T>(string url, HttpResponseMessage response)
        {
            if (response.StatusCode != HttpStatusCode.OK)
            {
                throw new Exception($"Unable to connect to server url {(object)url} HttpStatusCode:{(object)response.StatusCode}");
            }

            return (await response.Content.ReadAsStringAsync()).Deserialize<T>();
        }
    }
}
#endif