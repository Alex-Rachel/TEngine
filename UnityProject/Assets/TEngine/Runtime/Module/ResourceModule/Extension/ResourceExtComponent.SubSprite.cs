using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;
using YooAsset;

namespace TEngine
{
    /// <summary>
    /// 资源组件拓展。
    /// </summary>
    internal partial class ResourceExtComponent
    {
        private readonly Dictionary<string, SubAssetsHandle> _subAssetsHandles = new Dictionary<string, SubAssetsHandle>();
        private readonly Dictionary<string, int> _subSpriteReferences = new Dictionary<string, int>();

        public async UniTask SetSubSprite(Image image, string location, string spriteName, bool setNativeSize = false, CancellationToken cancellationToken = default)
        {
            var subSprite = await GetSubSpriteImp(location, spriteName, cancellationToken);

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
            AddReference(image.gameObject, location);
        }
        
        public async UniTask SetSubSprite(SpriteRenderer spriteRenderer, string location, string spriteName, CancellationToken cancellationToken = default)
        {
            var subSprite = await GetSubSpriteImp(location, spriteName, cancellationToken);

            if (spriteRenderer == null)
            {
                Log.Warning($"SetSubAssets Image is null");
                return;
            }

            spriteRenderer.sprite = subSprite;
            AddReference(spriteRenderer.gameObject, location);
        }

        private async UniTask<Sprite> GetSubSpriteImp(string location, string spriteName, CancellationToken cancellationToken = default)
        {
            var assetInfo = YooAssets.GetAssetInfo(location);
            if (assetInfo.IsInvalid)
            {
                throw new GameFrameworkException($"Invalid location: {location}");
            }

            await TryWaitingLoading(location);

            if (!_subAssetsHandles.TryGetValue(location, out var subAssetsHandle))
            {
                subAssetsHandle = YooAssets.LoadSubAssetsAsync<Sprite>(location);
                await subAssetsHandle.ToUniTask(cancellationToken: cancellationToken);
                _subAssetsHandles[location] = subAssetsHandle;
            }

            var subSprite = subAssetsHandle.GetSubAssetObject<Sprite>(spriteName);
            if (subSprite == null)
            {
                throw new GameFrameworkException($"Invalid sprite name: {spriteName}");
            }
            return subSprite;
        }

        private void AddReference(GameObject target,string location)
        {
            var subSpriteReference = target.GetComponent<SubSpriteReference>();
            if (subSpriteReference == null)
            {
                subSpriteReference = target.AddComponent<SubSpriteReference>();
            }
            _subSpriteReferences[location] = _subSpriteReferences.TryGetValue(location, out var count) ? count + 1 : 1;
            subSpriteReference.Reference(location);
        }
        
        internal void DeleteReference(string location)
        {
            if (string.IsNullOrEmpty(location))
            {
                return;
            }
            _subSpriteReferences[location] = _subSpriteReferences.TryGetValue(location, out var count) ? count - 1 : 0;
            if (_subSpriteReferences[location] <= 0)
            {
                var subAssetsHandle = _subAssetsHandles[location];
                subAssetsHandle.Dispose();
                _subAssetsHandles.Remove(location);
                _subSpriteReferences.Remove(location);
            }
        }
    }
    
    [DisallowMultipleComponent]
    public class SubSpriteReference : MonoBehaviour
    {
        private string _location;
        
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