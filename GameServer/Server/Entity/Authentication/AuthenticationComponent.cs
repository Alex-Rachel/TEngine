namespace Fantasy;

public sealed class AuthenticationComponent : Entitas.Entity
{
    /// <summary>
    /// 缓存注册账号数据字典
    /// </summary>
    public readonly Dictionary<string, Account> CacheAccountList = new Dictionary<string, Account>();
}