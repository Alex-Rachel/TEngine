#if FANTASY_UNITY
using System;
using Fantasy.Async;
using UnityEngine;
using UnityEngine.Networking;

namespace Fantasy.Unity.Download
{
    public sealed class DownloadTexture : AUnityDownload
    {
        public DownloadTexture(Scene scene, Download download) : base(scene, download)
        {
        }

        public FTask<Texture> StartDownload(string url, bool monitor, FCancellationToken cancellationToken = null)
        {
            var task = FTask<Texture>.Create(false);
            var unityWebRequestAsyncOperation = Start(UnityWebRequestTexture.GetTexture(Uri.EscapeUriString(url)), monitor);
            
            if (cancellationToken != null)
            {
                cancellationToken.Add(() =>
                {
                    Dispose();
                    task.SetResult(null);
                });
            }
            
            unityWebRequestAsyncOperation.completed += operation =>
            {
                try
                {
                    if (UnityWebRequest.result == UnityWebRequest.Result.Success)
                    {
                        var texture = DownloadHandlerTexture.GetContent(UnityWebRequest);
                        task.SetResult(texture);
                    }
                    else
                    {
                        Log.Error(UnityWebRequest.error);
                        task.SetResult(null);
                    }
                }
                finally
                {
                    Dispose();
                }
            };

            return task;
        }
    }
}
#endif