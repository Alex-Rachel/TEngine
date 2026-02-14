#if FANTASY_UNITY
using System;
using Fantasy.Async;
using UnityEngine;
using UnityEngine.Networking;

namespace Fantasy.Unity.Download
{
    public sealed class DownloadSprite : AUnityDownload
    {
        public DownloadSprite(Scene scene, Download download) : base(scene, download)
        {
        }

        public FTask<Sprite> StartDownload(string url, bool monitor, FCancellationToken cancellationToken = null)
        {
            var task = FTask<Sprite>.Create(false);
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
                        var sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), Vector2.one * 5, 1f);
                        task.SetResult(sprite);
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