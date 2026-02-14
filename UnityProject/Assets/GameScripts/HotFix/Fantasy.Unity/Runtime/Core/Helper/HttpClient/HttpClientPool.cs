#if !FANTASY_WEBGL
using System;
using System.Collections.Generic;
using System.Net.Http;
#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.

namespace Fantasy.Http
{
    internal class HttpClientPool : IDisposable
    {
        private bool IsDispose { get; set; }
        public HttpClient Client { get; private set; }
        private static readonly Queue<HttpClientPool> Pools = new Queue<HttpClientPool>();
        private static readonly HttpClientHandler ClientHandler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (sender, cert, chain, sslPolicyErrors) => true
        };
    
        public static HttpClientPool Create()
        {
            if (Pools.TryDequeue(out var httpClientPool))
            {
                httpClientPool.IsDispose = false;
                return httpClientPool;
            }

            httpClientPool = new HttpClientPool();
            httpClientPool.Client = new HttpClient(ClientHandler);
            return httpClientPool;
        }

        public void Dispose()
        {
            if (IsDispose)
            {
                return;
            }

            IsDispose = true;
            Pools.Enqueue(this);
        }
    }
}
#endif