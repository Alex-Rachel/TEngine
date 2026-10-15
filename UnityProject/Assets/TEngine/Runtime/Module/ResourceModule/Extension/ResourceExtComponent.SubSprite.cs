using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.U2D;
using YooAsset;

namespace TEngine
{
    /// <summary>
    /// 资源组件拓展。
    /// </summary>
    internal partial class ResourceExtComponent
    {
        private sealed class SubSpriteAsset
        {
            public HandleBase Handle;
            public int References;
            // GetSprite 返回克隆；按名字复用，并随图集一起释放。
            public readonly Dictionary<string, Sprite> Sprites = new Dictionary<string, Sprite>();
        }

        private readonly Dictionary<string, SubSpriteAsset> _subSpriteAssets = new Dictionary<string, SubSpriteAsset>();

        public async UniTask SetSubSprite(Image image, string location, string spriteName, bool setNativeSize = false, CancellationToken cancellationToken = default)
        {
            var subSprite = await GetSubSpriteImp(image, location, spriteName, cancellationToken);

            if (image == null)
            {
                Log.Warning($"SetSubAssets Image is null");
                return;
            }

            image.sprite = subSprite;
            if (setNativeSize)
            {
                image.SetNativeSize();
            }
        }
        
        public async UniTask SetSubSprite(SpriteRenderer spriteRenderer, string location, string spriteName, CancellationToken cancellationToken = default)
        {
            var subSprite = await GetSubSpriteImp(spriteRenderer, location, spriteName, cancellationToken);

            if (spriteRenderer == null)
            {
                Log.Warning($"SetSubAssets Image is null");
                return;
            }

            spriteRenderer.sprite = subSprite;
        }

        private async UniTask<Sprite> GetSubSpriteImp(Component target, string location, string spriteName, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (target == null)
            {
                return null;
            }

            var assetInfo = YooAssets.GetAssetInfo(location);
            if (assetInfo.IsInvalid)
            {
                throw new GameFrameworkException($"Invalid location: {location}");
            }

            if (!_subSpriteAssets.TryGetValue(location, out var asset))
            {
                var isAtlas = assetInfo.AssetPath.EndsWith(".spriteatlas", StringComparison.OrdinalIgnoreCase)
                    || assetInfo.AssetPath.EndsWith(".spriteatlasv2", StringComparison.OrdinalIgnoreCase);
                asset = new SubSpriteAsset();
                if (isAtlas)
                {
                    asset.Handle = YooAssets.LoadAssetAsync<SpriteAtlas>(location);
                }
                else
                {
                    asset.Handle = YooAssets.LoadSubAssetsAsync<Sprite>(location);
                }
                // 在等待前缓存，列表并发加载共享同一个句柄。
                _subSpriteAssets.Add(location, asset);
            }

            // 等待中的请求也持有引用，单个请求取消不能释放其他请求的句柄。
            asset.References++;
            try
            {
                await UniTask.WaitUntil(() => asset.Handle.IsDone, cancellationToken: cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                if (asset.Handle.Status != EOperationStatus.Succeed)
                {
                    throw new GameFrameworkException($"Failed to load sprites: {location} ({assetInfo.AssetPath}), {asset.Handle.LastError}");
                }
                if (target == null)
                {
                    return null;
                }

                Sprite subSprite;
                if (asset.Handle is AssetHandle atlasHandle)
                {
                    if (!asset.Sprites.TryGetValue(spriteName, out subSprite))
                    {
                        var atlas = atlasHandle.AssetObject as SpriteAtlas;
                        subSprite = atlas != null ? atlas.GetSprite(spriteName) : null;
                        if (subSprite != null)
                        {
                            asset.Sprites.Add(spriteName, subSprite);
                        }
                    }
                }
                else
                {
                    subSprite = ((SubAssetsHandle)asset.Handle).GetSubAssetObject<Sprite>(spriteName);
                }
                if (subSprite == null)
                {
                    throw new GameFrameworkException($"Invalid sprite name: {spriteName}, location: {location} ({assetInfo.AssetPath})");
                }
                AddReference(target.gameObject, location);
                return subSprite;
            }
            finally
            {
                DeleteReference(location);
            }
        }

        private void AddReference(GameObject target,string location)
        {
            var subSpriteReference = target.GetComponent<SubSpriteReference>();
            if (subSpriteReference == null)
            {
                subSpriteReference = target.AddComponent<SubSpriteReference>();
            }
            if (subSpriteReference.Location == location)
            {
                return;
            }
            _subSpriteAssets[location].References++;
            subSpriteReference.Reference(location);
        }
        
        internal void DeleteReference(string location)
        {
            if (string.IsNullOrEmpty(location) || !_subSpriteAssets.TryGetValue(location, out var asset))
            {
                return;
            }
            if (--asset.References <= 0)
            {
                foreach (var sprite in asset.Sprites.Values)
                {
                    UnityEngine.Object.Destroy(sprite);
                }
                asset.Handle.Dispose();
                _subSpriteAssets.Remove(location);
            }
        }
    }
    
    [DisallowMultipleComponent]
    public class SubSpriteReference : MonoBehaviour
    {
        private string _location;

        internal string Location => _location;
        
        public void Reference(string location)
        {
            if (_location != null && _location != location)
            {
                ResourceExtComponent.Instance?.DeleteReference(_location);
            }
            _location = location;
        }
        
        private void OnDestroy()
        {
            if (_location != null)
            {
                ResourceExtComponent.Instance?.DeleteReference(_location);
            }
        }
    }
}
