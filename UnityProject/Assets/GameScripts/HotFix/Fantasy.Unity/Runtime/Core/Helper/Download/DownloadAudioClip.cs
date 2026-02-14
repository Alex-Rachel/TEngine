#if FANTASY_UNITY
using System;
using Fantasy.Async;
using UnityEngine;
using UnityEngine.Networking;

namespace Fantasy.Unity.Download
{
    public sealed class DownloadAudioClip : AUnityDownload
    {
        public DownloadAudioClip(Scene scene, Download download) : base(scene, download)
        {
        }

        public FTask<AudioClip> StartDownload(string url, AudioType audioType, bool monitor, FCancellationToken cancellationToken = null)
        {
            var task = FTask<AudioClip>.Create(false);
            var unityWebRequestAsyncOperation = Start(UnityWebRequestMultimedia.GetAudioClip(Uri.EscapeUriString(url), audioType), monitor);
            
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
                        var audioClip = DownloadHandlerAudioClip.GetContent(UnityWebRequest);
                        task.SetResult(audioClip);
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
