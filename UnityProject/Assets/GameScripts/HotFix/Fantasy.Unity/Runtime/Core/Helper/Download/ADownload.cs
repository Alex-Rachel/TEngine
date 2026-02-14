#if FANTASY_UNITY
using System;
using Fantasy.Async;
using UnityEngine.Networking;

namespace Fantasy.Unity.Download
{
    public abstract class AUnityDownload : IDisposable
    {
        private long _timeId;
        private ulong _totalDownloadedBytes;
        private Download _download;
        protected UnityWebRequest UnityWebRequest;
        private FCancellationToken _cancellationToken;
        private Scene Scene;

        protected AUnityDownload(Scene scene,Download download)
        {
            Scene = scene;
            _download = download;
            _download.Tasks.Add(this);
        }

        protected UnityWebRequestAsyncOperation Start(UnityWebRequest unityWebRequest, bool monitor)
        {
            UnityWebRequest = unityWebRequest;
            _timeId = Scene.TimerComponent.Unity.RepeatedTimer(33, Update);
            return UnityWebRequest.SendWebRequest();
        }

        private void Update()
        {
            var downloadSpeed = UnityWebRequest.downloadedBytes - _totalDownloadedBytes;
            _download.DownloadSpeed += downloadSpeed;
            _download.TotalDownloadedBytes += downloadSpeed;
            _totalDownloadedBytes = UnityWebRequest.downloadedBytes;
        }

        public virtual void Dispose()
        {
            Update();
            _totalDownloadedBytes = 0;
            UnityWebRequest?.Dispose();
            _download.Tasks.Remove(this);
            Scene.TimerComponent.Unity.Remove(ref _timeId);
            _download = null;
        }
    }
}
#endif