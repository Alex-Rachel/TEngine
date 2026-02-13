using Fantasy;
using Fantasy.Async;
using Fantasy.Entitas;
using Fantasy.Entitas.Interface;
using Fantasy.Helper;
using Fantasy.Platform.Net;

#pragma warning disable CS8602 // 解引用可能出现空引用。
#pragma warning disable CS8600 // 将 null 字面量或可能为 null 的值转换为非 null 类型。

namespace System;

internal static class AuthenticationComponentSystem
{
    internal static async FTask<(uint errorCode, long accountId)> Login(this AuthenticationComponent self, string userName, string password)
    {
        Log.Debug("登录请求");
        // 1、检查传递的参数是否完整以及合法
        if (string.IsNullOrEmpty(userName) || string.IsNullOrEmpty(password))
        {
            // 代表账号参数不完整或不合法
            return (1001, 0);
        }

        var scene = self.Scene;
        var worldDatabase = scene.World.Database;
        var userNameHashCode = userName.GetHashCode();

        // 利用协程锁 解决异步原子性问题
        using (var @lock =
               await scene.CoroutineLockComponent.Wait((int)LockType.Authentication_LoginLock, userNameHashCode))
        {
            Account account = null;

            uint result = 0;
            account = await worldDatabase.First<Account>(d => d.Username == userName && d.Password == password);

            if (account == null)
            {
                Log.Debug("[数据库] 用户名或者密码错误");
                // 用户名或者密码错误
                result = 1004;
            }
            else
            {
                Log.Debug("[数据库] 登录成功");
                // 更新登录时间 保存到数据库
                account.LoginTime = TimeHelper.Now;
                await worldDatabase.Save(account);
                account.Deserialize(scene);
            }

            if (result != 0)
            {
                return (result, 0);
            }

            return (result, account.Id);
        }
    }


    /// <summary>
    /// 鉴权注册接口
    /// </summary>
    internal static async FTask<uint> Register(this AuthenticationComponent self, string userName, string password,
        string source)
    {
        // 1、检查传递的参数是否完整以及合法
        if (string.IsNullOrEmpty(userName) || string.IsNullOrEmpty(password))
        {
            // 代表注册账号参数不完整或不合法
            return 1001;
        }

        var userNameHashCode = userName.GetHashCode();
        var scene = self.Scene;

        // 利用协程锁 解决异步原子性问题
        using (var @lock = await scene.CoroutineLockComponent.Wait((int)LockType.Authentication_RegisterLock, userNameHashCode))
        {
            // 利用缓存来减少频繁请求数据或缓存的压力
            if (self.CacheAccountList.TryGetValue(userName, out var account))
            {
                Log.Info($"[Register] 缓存中的数据");
                // 代表用户已经存在
                return 1002;
            }

            Log.Info($"[Register] 数据库中的数据");
            // 2、数据库查询账号是否存在
            var worldDatabase = scene.World.Database;
            bool isExist = await worldDatabase.Exist<Account>(d => d.Username == userName);

            if (isExist)
            {
                // 代表用户已经存在
                return 1002;
            }

            // 3、执行到这里 表示数据库或缓存没有该账号的注册信息 需要创建一个
            account = Entity.Create<Account>(scene, true, true);
            account.Username = userName;
            account.Password = password;
            account.CreateTime = TimeHelper.Now;

            // 4、写入实体到数据库中
            await worldDatabase.Save(account);
            // 5、把账号缓存到字典中
            self.CacheAccountList.TryAdd(userName, account);

            Log.Info($"[Register] source: [{source}]注册一个账号 用户名: [{userName}] 账户ID: [{account.Id}]");
            // 代表注册成功
            return 0;
        }
    }
}