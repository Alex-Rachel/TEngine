using UnityEngine;
using UnityEngine.Scripting;

/// <summary>
/// 防止裁剪引用。
/// <remarks>如果在主工程无引用，link.xml的防裁剪也无效。</remarks>
/// <remarks>最好是AOT显示保留引用，Preserve有可能还会裁成员变量。</remarks>
/// </summary>
[Preserve]
public class DisStripCode : MonoBehaviour
{
    private void Awake()
    {
        //UnityEngine.Physics
        RegisterType<Collider>();
        RegisterType<Collider2D>();
        RegisterType<Collision>();
        RegisterType<Collision2D>();
        RegisterType<CapsuleCollider2D>();

        RegisterType<Rigidbody>();
        RegisterType<Rigidbody2D>();
        
        RegisterType<Ray>();
        RegisterType<Ray2D>();

        //UnityEngine.Graphics
        RegisterType<Mesh>();
        RegisterType<MeshRenderer>();

        //UnityEngine.Animation
        RegisterType<AnimationClip>();
        RegisterType<AnimationCurve>();
        RegisterType<AnimationEvent>();
        RegisterType<AnimationState>();
        RegisterType<Animator>();
        RegisterType<Animation>();

#if UNITY_IOS || PLATFORM_IOS
        /* 
        // IOSCamera ios下相机权限的问题，用这种方法就可以解决了 问题防裁剪。
        foreach (var _ in WebCamTexture.devices)
        {
        } 
        */ 
#endif
    }

    private void RegisterType<T>()
    {
#if UNITY_EDITOR && false
      Debug.Log($"DisStripCode RegisterType :{typeof(T)}");
#endif
    }
}