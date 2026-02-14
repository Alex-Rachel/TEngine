using System.Threading;
using Cysharp.Threading.Tasks;
using TEngine;
using UnityEngine;
using UnityEngine.UI;
using System;

public static class SetSpriteExtensions
{
    /// <summary>
    /// 设置图片。
    /// </summary>
    /// <param name="image">UI/Image。</param>
    /// <param name="location">资源定位地址。</param>
    /// <param name="setNativeSize">是否使用原始分辨率。</param>
    /// <param name="callback">设置完Sprite的回调</param>
    /// <param name="cancellationToken">取消设置资源的Token。</param>
    public static void SetSprite(this Image image, string location, bool setNativeSize = false, Action<Image> callback = null, CancellationToken cancellationToken = default)
    {
        ResourceExtComponent.Instance.SetAssetByResources<Sprite>(SetSpriteObject.Create(image, location, setNativeSize, callback, cancellationToken), cancellationToken).Forget();
    }

    /// <summary>
    /// 设置图片。
    /// </summary>
    /// <param name="spriteRenderer">2D/SpriteRender。</param>
    /// <param name="location">资源定位地址。</param>
    /// <param name="callback">设置完Sprite的回调</param>
    /// <param name="cancellationToken">取消设置资源的Token。</param>
    public static void SetSprite(this SpriteRenderer spriteRenderer, string location, Action<SpriteRenderer> callback = null, CancellationToken cancellationToken = default)
    {
        ResourceExtComponent.Instance.SetAssetByResources<Sprite>(SetSpriteObject.Create(spriteRenderer, location, callback, cancellationToken), cancellationToken).Forget();
    }

    /// <summary>
    /// 设置子图片。
    /// </summary>
    /// <param name="image">UI/Image。</param>
    /// <param name="location">资源定位地址。</param>
    /// <param name="spriteName">子图片名称。</param>
    /// <param name="setNativeSize">是否使用原始分辨率。</param>
    /// <param name="cancellationToken">取消设置资源的Token。</param>
    public static void SetSubSprite(this Image image, string location, string spriteName, bool setNativeSize = false, CancellationToken cancellationToken = default)
    {
        ResourceExtComponent.Instance.SetSubSprite(image, location, spriteName, setNativeSize, cancellationToken).Forget();
    }
    
    /// <summary>
    /// 设置子图片。
    /// </summary>
    /// <param name="spriteRenderer">2D/SpriteRender。</param>
    /// <param name="location">资源定位地址。</param>
    /// <param name="spriteName">子图片名称。</param>
    /// <param name="cancellationToken">取消设置资源的Token。</param>
    public static void SetSubSprite(this SpriteRenderer spriteRenderer, string location, string spriteName, CancellationToken cancellationToken = default)
    {
        ResourceExtComponent.Instance.SetSubSprite(spriteRenderer, location, spriteName, cancellationToken).Forget();
    }
}