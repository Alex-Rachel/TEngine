using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
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

        public async UniTask SetSubSprite(ISetAssetObject setAssetObject, string spriteName, CancellationToken cancellationToken)
        {
            var target = setAssetObject.TargetObject;
            var location = setAssetObject.Location;
            if (target == null || _isDestroyed)
            {
                MemoryPool.Release(setAssetObject);
                return;
            }

            var loadingState = CreateLoadingState(target, location, cancellationToken);
            var linkedTokenSource = loadingState.Cts;
            SubSpriteAsset asset = null;
            var setAssetObjectTransferred = false;
            try
            {
                linkedTokenSource.Token.ThrowIfCancellationRequested();
                var assetInfo = YooAssets.GetAssetInfo(location);
                if (assetInfo.IsInvalid)
                {
                    throw new GameFrameworkException($"Invalid location: {location}");
                }

                if (!_subSpriteAssets.TryGetValue(location, out asset))
                {
                    var isAtlas = assetInfo.AssetPath.EndsWith(".spriteatlas", StringComparison.OrdinalIgnoreCase)
                        || assetInfo.AssetPath.EndsWith(".spriteatlasv2", StringComparison.OrdinalIgnoreCase);
                    asset = new SubSpriteAsset
                    {
                        Handle = isAtlas
                            ? (HandleBase)YooAssets.LoadAssetAsync<SpriteAtlas>(location)
                            : YooAssets.LoadSubAssetsAsync<Sprite>(location)
                    };
                    // 在等待前缓存，列表并发加载共享同一个句柄。
                    _subSpriteAssets.Add(location, asset);
                }

                // 等待期间持有引用；设置成功后交给普通 Sprite 使用记录管理。
                asset.References++;
                await UniTask.WaitUntil(() => asset.Handle.IsDone, cancellationToken: linkedTokenSource.Token);
                if (!IsCurrentRequest(target, loadingState))
                {
                    return;
                }
                if (asset.Handle.Status != EOperationStatus.Succeed)
                {
                    throw new GameFrameworkException($"Failed to load sprites: {location} ({assetInfo.AssetPath}), {asset.Handle.LastError}");
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
                DetachCurrentRequest(target, loadingState);
                setAssetObjectTransferred = true;
                SetAsset(setAssetObject, subSprite, true);
            }
            catch (OperationCanceledException) when (linkedTokenSource.IsCancellationRequested)
            {
                // 请求被替换或组件销毁属于正常取消流程。
            }
            finally
            {
                DetachCurrentRequest(target, loadingState);
                if (!setAssetObjectTransferred)
                {
                    if (asset != null)
                    {
                        ReleaseSubSprite(location);
                    }
                    MemoryPool.Release(setAssetObject);
                }
                MemoryPool.Release(loadingState);
            }
        }

        private void ReleaseSubSprite(string location)
        {
            if (string.IsNullOrEmpty(location) || !_subSpriteAssets.TryGetValue(location, out var asset))
            {
                return;
            }
            if (--asset.References <= 0)
            {
                ReleaseSubSpriteAsset(asset);
                _subSpriteAssets.Remove(location);
            }
        }

        private static void ReleaseSubSpriteAsset(SubSpriteAsset asset)
        {
            foreach (var sprite in asset.Sprites.Values)
            {
                UnityEngine.Object.Destroy(sprite);
            }
            asset.Sprites.Clear();
            asset.Handle.Dispose();
        }

        private void ReleaseSubSpriteAssets()
        {
            foreach (var asset in _subSpriteAssets.Values)
            {
                ReleaseSubSpriteAsset(asset);
            }
            _subSpriteAssets.Clear();
        }
    }
}
