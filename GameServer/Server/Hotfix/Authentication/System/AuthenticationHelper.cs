using Fantasy;
using Fantasy.Async;

namespace System;

public static class AuthenticationHelper
{
    /// <summary>
    /// 登录账号
    /// </summary>
    /// <param name="scene"></param>
    /// <param name="username">用户名</param>
    /// <param name="password">密码</param>
    /// <returns></returns>
    public static async FTask<(uint errorCode, long accountId)> Login(Scene scene, string username, string password)
        => await scene.GetComponent<AuthenticationComponent>().Login(username, password);

    /// <summary>
    /// 注册新账号
    /// </summary>
    /// <param name="scene"></param>
    /// <param name="username">用户名</param>
    /// <param name="password">密码</param>
    /// <param name="source">注册来源</param>
    /// <returns></returns>
    public static async FTask<uint> Register(Scene scene, string username, string password, string source)
        => await scene.GetComponent<AuthenticationComponent>().Register(username, password, source);
}