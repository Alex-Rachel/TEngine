namespace Fantasy;

public enum LockType
{
    None = 0,

    /// <summary>
    /// 鉴权注册锁
    /// </summary>
    Authentication_RegisterLock = 1,

    /// <summary>
    /// 鉴权登录锁
    /// </summary>
    Authentication_LoginLock = 2,
}