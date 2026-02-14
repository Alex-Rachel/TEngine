#if FANTASY_NET
using System;
using System.Collections.Frozen;
using MongoDB.Driver;
#pragma warning disable CS0649 // Field is never assigned to, and will always have its default value
#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider adding the 'required' modifier or declaring as nullable.

namespace Fantasy.Database;

/// <summary>
/// 数据库设置助手
/// </summary>
public static class DataBaseSetting
{
    /// <summary>
    /// MongoDB 初始化自定义委托，当设置了这个委托后，就不会自动创建MongoClient，把创建权交给自定义。
    /// </summary>
    public static Func<DataBaseCustomConfig, MongoClient>? MongoDbCustomInitialize;
}

/// <summary>
/// 数据库配置帮助配
/// </summary>
public static class DataBaseHelper
{
    /// <summary>
    /// 数据库名字常量字典，对应数据库名称和索引
    /// </summary>
    internal static FrozenDictionary<string, int> DatabaseDbName;
}

/// <summary>
/// 数据库自定义连接参数
/// </summary>
public sealed class DataBaseCustomConfig
{
    /// <summary>
    /// 当前Scene
    /// </summary>
    public Scene Scene;
    /// <summary>
    /// 连接字符串
    /// </summary>
    public string ConnectionString;
    /// <summary>
    /// 数据库名字
    /// </summary>
    public string DBName;
}
#endif
