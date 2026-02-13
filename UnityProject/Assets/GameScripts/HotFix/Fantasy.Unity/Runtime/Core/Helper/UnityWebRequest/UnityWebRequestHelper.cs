#if FANTASY_UNITY
using System;
using Fantasy.Async;
using UnityEngine;
using UnityEngine.Networking;

namespace Fantasy.Unity
{
    /// <summary>
    /// UnityWebRequest的帮助类
    /// </summary>
    public static class UnityWebRequestHelper
    {
        /// <summary>
        /// 获取一个文本
        /// </summary>
        /// <param name="url"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public static FTask<string> GetText(string url, FCancellationToken cancellationToken = null)
        {
            var task = FTask<string>.Create(false);
            var unityWebRequest = UnityWebRequest.Get(url);
            var unityWebRequestAsyncOperation = unityWebRequest.SendWebRequest();
            
            if (cancellationToken != null)
            {
                cancellationToken.Add(() =>
                {
                    unityWebRequest.Abort();
                    task.SetResult(null);
                });
            }
            
            unityWebRequestAsyncOperation.completed += operation =>
            {
                if (unityWebRequest.result == UnityWebRequest.Result.Success)
                {
                    var text = unityWebRequest.downloadHandler.text;
                    task.SetResult(text);
                }
                else
                {
                    Log.Error(unityWebRequest.error);
                    task.SetResult(null);
                }
            };

            return task;
        }
        
        /// <summary>
        /// 获取一个Sprite
        /// </summary>
        /// <param name="url"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public static FTask<Sprite> GetSprite(string url, FCancellationToken cancellationToken = null)
        {
            var task = FTask<Sprite>.Create(false);
            var unityWebRequest = UnityWebRequestTexture.GetTexture(Uri.EscapeUriString(url));
            var unityWebRequestAsyncOperation = unityWebRequest.SendWebRequest();
            
            if (cancellationToken != null)
            {
                cancellationToken.Add(() =>
                {
                    unityWebRequest.Abort();
                    task.SetResult(null);
                });
            }
            
            unityWebRequestAsyncOperation.completed += operation =>
            {
                if (unityWebRequest.result == UnityWebRequest.Result.Success)
                {
                    var texture = DownloadHandlerTexture.GetContent(unityWebRequest);
                    var sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), Vector2.one * 5, 1f);
                    task.SetResult(sprite);
                }
                else
                {
                    Log.Error(unityWebRequest.error);
                    task.SetResult(null);
                }
            };

            return task;
        }

        /// <summary>
        /// 获取一个Texture
        /// </summary>
        /// <param name="url"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public static FTask<Texture> GetTexture(string url, FCancellationToken cancellationToken = null)
        {
            var task = FTask<Texture>.Create(false);
            var unityWebRequest = UnityWebRequestTexture.GetTexture(Uri.EscapeUriString(url));
            var unityWebRequestAsyncOperation = unityWebRequest.SendWebRequest();
            
            if (cancellationToken != null)
            {
                cancellationToken.Add(() =>
                {
                    unityWebRequest.Abort();
                    task.SetResult(null);
                });
            }
            
            unityWebRequestAsyncOperation.completed += operation =>
            {
                if (unityWebRequest.result == UnityWebRequest.Result.Success)
                {
                    var texture = DownloadHandlerTexture.GetContent(unityWebRequest);
                    task.SetResult(texture);
                }
                else
                {
                    Log.Error(unityWebRequest.error);
                    task.SetResult(null);
                }
            };

            return task;
        }
        
        /// <summary>
        /// 获取Bytes
        /// </summary>
        /// <param name="url"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public static FTask<byte[]> GetBytes(string url, FCancellationToken cancellationToken = null)
        {
            var task = FTask<byte[]>.Create(false);
            var unityWebRequest = UnityWebRequest.Get(url);
            var unityWebRequestAsyncOperation = unityWebRequest.SendWebRequest();
            
            if (cancellationToken != null)
            {
                cancellationToken.Add(() =>
                {
                    unityWebRequest.Abort();
                    task.SetResult(null);
                });
            }
            
            unityWebRequestAsyncOperation.completed += operation =>
            {
                if (unityWebRequest.result == UnityWebRequest.Result.Success)
                {
                    var bytes = unityWebRequest.downloadHandler.data;
                    task.SetResult(bytes);
                }
                else
                {
                    Log.Error(unityWebRequest.error);
                    task.SetResult(null);
                }
            };

            return task;
        }
        
        /// <summary>
        /// 获取AssetBundle
        /// </summary>
        /// <param name="url"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public static FTask<AssetBundle> GetAssetBundle(string url, FCancellationToken cancellationToken = null)
        {
            var task = FTask<AssetBundle>.Create(false);
            var unityWebRequest = UnityWebRequestAssetBundle.GetAssetBundle(Uri.EscapeUriString(url));
            var unityWebRequestAsyncOperation = unityWebRequest.SendWebRequest();
            
            if (cancellationToken != null)
            {
                cancellationToken.Add(() =>
                {
                    unityWebRequest.Abort();
                    task.SetResult(null);
                });
            }

            unityWebRequestAsyncOperation.completed += operation =>
            {
                if (unityWebRequest.result == UnityWebRequest.Result.Success)
                {
                    var assetBundle = DownloadHandlerAssetBundle.GetContent(unityWebRequest);
                    task.SetResult(assetBundle);
                    return;
                }

                Log.Error(unityWebRequest.error);
                task.SetResult(null);
            };

            return task;
        }

        /// <summary>
        /// 获取AudioClip
        /// </summary>
        /// <param name="url"></param>
        /// <param name="audioType"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public static FTask<AudioClip> GetAudioClip(string url, AudioType audioType, FCancellationToken cancellationToken = null)
        {
            var task = FTask<AudioClip>.Create(false);
            var unityWebRequest = UnityWebRequestMultimedia.GetAudioClip(Uri.EscapeUriString(url), audioType);
            var unityWebRequestAsyncOperation = unityWebRequest.SendWebRequest();
            
            if (cancellationToken != null)
            {
                cancellationToken.Add(() =>
                {
                    unityWebRequest.Abort();
                    task.SetResult(null);
                });
            }
            
            unityWebRequestAsyncOperation.completed += operation =>
            {
                if (unityWebRequest.result == UnityWebRequest.Result.Success)
                {
                    var audioClip = DownloadHandlerAudioClip.GetContent(unityWebRequest);
                    task.SetResult(audioClip);
                }
                else
                {
                    Log.Error(unityWebRequest.error);
                    task.SetResult(null);
                }
            };

            return task;
        }
    }
}
#endif