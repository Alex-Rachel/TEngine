using System;
using System.Threading;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;
#if ODIN_INSPECTOR
using Sirenix.OdinInspector;
#endif

namespace TEngine
{
    [Serializable]
    public class SetSpriteObject : ISetAssetObject
    {
        enum SetType
        {
            None,
            Image,
            SpriteRender,
        }
#if ODIN_INSPECTOR
        [ShowInInspector]
#endif
        private SetType _setType;

#if ODIN_INSPECTOR
        [ShowInInspector]
#endif
        private Image _image;

#if ODIN_INSPECTOR
        [ShowInInspector]
#endif
        private SpriteRenderer _spriteRenderer;

#if ODIN_INSPECTOR
        [ShowInInspector]
#endif
        private Sprite _sprite;
        
        public Object TargetObject { get; set; }

        public string Location { get; private set; }

        private bool _setNativeSize = false;
        Action<Image> _imageCallback;
        Action<SpriteRenderer> _spriteCallback;

        private CancellationToken _cancellationToken;

        public void SetAsset(Object asset)
        {
            _sprite = (Sprite)asset;

            if (_cancellationToken.IsCancellationRequested)
                return;

            if (_image != null)
            {
                _image.sprite = _sprite;
                if (_setNativeSize)
                {
                    _image.SetNativeSize();
                }
                _imageCallback?.Invoke(_image);
            }
            else if (_spriteRenderer != null)
            {
                _spriteRenderer.sprite = _sprite;
                _spriteCallback?.Invoke(_spriteRenderer);
            }
        }

        public bool IsCanRelease()
        {
            if (_setType == SetType.Image)
            {
                return _image == null || _image.sprite == null ||
                       (_sprite != null && _image.sprite != _sprite);
            }
            else if (_setType == SetType.SpriteRender)
            {
                return _spriteRenderer == null || _spriteRenderer.sprite == null ||
                       (_sprite != null && _spriteRenderer.sprite != _sprite);
            }
            return true;
        }

        public void Clear()
        {
            _spriteRenderer = null;
            _image = null;
            Location = null;
            _sprite = null;
            _setType = SetType.None;
            _setNativeSize = false;
            TargetObject = null;
        }

        public static SetSpriteObject Create(Image image, string location, bool setNativeSize = false, Action<Image> callback = null, CancellationToken cancellationToken = default)
        {
            SetSpriteObject item = MemoryPool.Acquire<SetSpriteObject>();
            item._image = image;
            item._setNativeSize = setNativeSize;
            item.Location = location;
            item._cancellationToken = cancellationToken;
            item._setType = SetType.Image;
            item._imageCallback = callback;
            item.TargetObject = image;
            return item;
        }

        public static SetSpriteObject Create(SpriteRenderer spriteRenderer, string location, Action<SpriteRenderer> callback = null, CancellationToken cancellationToken = default)
        {
            SetSpriteObject item = MemoryPool.Acquire<SetSpriteObject>();
            item._spriteRenderer = spriteRenderer;
            item.Location = location;
            item._cancellationToken = cancellationToken;
            item._setType = SetType.SpriteRender;
            item._spriteCallback = callback;
            item.TargetObject = spriteRenderer;
            return item;
        }
    }
}