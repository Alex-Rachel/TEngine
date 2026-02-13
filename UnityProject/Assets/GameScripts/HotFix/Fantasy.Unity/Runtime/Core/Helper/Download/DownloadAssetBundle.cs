#if FANTASY_UNITY
using System;
using Fantasy.Async;
using UnityEngine;
using UnityEngine.Networking;

namespace Fantasy.Unity.Download
{
    public sealed class DownloadAssetBundle : AUnityDownload
    {
        public DownloadAssetBundle(Scene scene, Download download) : base(scene, download)
        {
        }

        public FTask<AssetBundle> StartDownload(string url, bool monitor, FCancellationToken cancellationToken = null)
        {
            var task = FTask<AssetBundle>.Create(false);
            var unityWebRequestAsyncOperation = Start(UnityWebRequestAssetBundle.GetAssetBundle(Uri.EscapeUriString(url)), monitor);
            
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
                        var assetBundle = DownloadHandlerAssetBundle.GetContent(UnityWebRequest);
                        task.SetResult(assetBundle);
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