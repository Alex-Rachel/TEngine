using Fantasy.Entitas;

namespace Fantasy;

public class Account : Entity
{
    /// <summary>
    /// 用户名
    /// </summary>
    public string Username { get; set; }

    /// <summary>
    /// 登录密码
    /// </summary>
    public string Password { get; set; }

    /// <summary>
    /// 账号创建时间
    /// </summary>
    public long CreateTime { get; set; }

    /// <summary>
    /// 账号登陆时间
    /// </summary>
    public long LoginTime { get; set; }
}