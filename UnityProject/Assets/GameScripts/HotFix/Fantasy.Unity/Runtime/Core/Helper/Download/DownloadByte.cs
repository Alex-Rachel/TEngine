#if FANTASY_UNITY
using Fantasy.Async;
using UnityEngine.Networking;

namespace Fantasy.Unity.Download
{
    public sealed class DownloadByte : AUnityDownload
    {
        public DownloadByte(Scene scene, Download download) : base(scene, download)
        {
        }

        public FTask<byte[]> StartDownload(string url, bool monitor, FCancellationToken cancellationToken = null)
        {
            var task = FTask<byte[]>.Create(false);
            var unityWebRequestAsyncOperation = Start(UnityWebRequest.Get(url), monitor);

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
                        var bytes = UnityWebRequest.downloadHandler.data;
                        task.SetResult(bytes);
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