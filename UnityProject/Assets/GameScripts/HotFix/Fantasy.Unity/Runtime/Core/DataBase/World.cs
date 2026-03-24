#if FANTASY_NET
using System;
using System.Collections.Concurrent;
using Fantasy.Database;
using Fantasy.Platform.Net;
#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider adding the 'required' modifier or declaring as nullable.
#pragma warning disable CS8601 // Possible null reference assignment.
#pragma warning disable CS8625 // Cannot convert null literal to non-nullable reference type.
// ReSharper disable ForCanBeConvertedToForeach
#pragma warning disable CS8603 // Possible null reference return.

namespace Fantasy
{
    /// <summary>
    /// 表示一个服务器【世界】
    /// </summary>
    public sealed partial class World : IDisposable
    {
        /// <summary>
        /// 获取世界的唯一标识。
        /// </summary>
        public byte Id { get; private init; }
        /// <summary>
        /// 世界名。
        /// </summary>
        public string Name { get; private init; }
        /// <summary>
        /// 第一个数据库
        /// </summary>
        public IDatabase Database { get; private set; }
        /// <summary>
        /// 本世界所有数据库, 按照数据库名字生成的索引来存取。
        /// </summary>
        private ConcurrentDictionary<string, IDatabase> AllDatabase { get; init; }

        /// <summary>
        /// 根据数据库名字获取某个类型的数据库。
        /// </summary>
        /// <typeparam name="T">数据库类型</typeparam>
        /// <param name="dbName">数据库名字</param>
        /// <param name="database">返回的数据库实例</param>
        /// <returns></returns>
        public bool TryGetDatabase(string dbName, out IDatabase database)
        {
            var currentDatabase = this[dbName];
            
            if (currentDatabase == null)
            {
                database = null;
                return false;
            }

            database = currentDatabase;
            return true;
        }

        /// <summary>
        /// 根据数据库名字获取某个类型的数据库。
        /// </summary>
        /// <param name="dbName"></param>
        public Fantasy.Database.IDatabase? this[string dbName]
        {
            get
            {
                if (string.IsNullOrWhiteSpace(dbName))
                {
                    throw new ArgumentNullException(nameof(dbName));
                }

                if (AllDatabase.TryGetValue(dbName, out var database))
                {
                    return database;
                }
                
                Log.Error($"dbName:{dbName} Database not found.");
                return null;
            }
        }

        /// <summary>
        /// 获取游戏世界的配置信息。
        /// </summary>
        public WorldConfig Config => WorldConfigData.Instance.Get(Id);

        /// <summary>
        /// 使用指定的配置信息创建一个游戏世界实例。
        /// </summary>
        /// <param name="scene"></param>
        /// <param name="worldConfigId"></param>
        private World(Scene scene, byte worldConfigId)
        {
            Id = worldConfigId;  
            Name = WorldConfigData.Instance.Get(worldConfigId).WorldName;
            AllDatabase = new ConcurrentDictionary<string, IDatabase>();
            
            var worldConfig = WorldConfigData.Instance.Get(worldConfigId);
            var dataBaseConfigList = worldConfig.DatabaseConfig;

            if (dataBaseConfigList == null || dataBaseConfigList.Length == 0)
            {
                return;
            }
            
            for (var i = 0; i < dataBaseConfigList.Length; i++)
            {
                var dataBaseConfig = dataBaseConfigList[i];
                
                if (!string.IsNullOrWhiteSpace(dataBaseConfig.DbConnection))
                {
                    var dbType = dataBaseConfig.DbType.ToLower();
                    
                    switch (dbType)
                    {
                        case "mongodb":
                        case "mongo":
                        {
                            try
                            {
                                var mongoDataBase = new MongoDatabase();
                                mongoDataBase.Initialize(scene, dataBaseConfig.DbConnection, dataBaseConfig.DbName);
                                AllDatabase.TryAdd(dataBaseConfig.DbName, mongoDataBase);
                            }
                            catch (Exception e)
                            {
                                Log.Error($"WorldId:{Id} DbName:{dataBaseConfig.DbName} DbConnection:{dataBaseConfig.DbConnection} Initialization failed. Please check if the target database server can be connected normally.\n{e.Message}");
                            }
                            break;
                        }
                    }
                }
            }

            if (worldConfig.Default == null)
            {
                return;
            }
            
            if (!string.IsNullOrWhiteSpace(worldConfig.Default.DbConnection))
            {
                Database = AllDatabase[worldConfig.Default.DbName];
            }
        }

        /// <summary>
        /// 创建一个指定唯一标识的游戏世界实例。
        /// </summary>
        /// <param name="scene"></param>
        /// <param name="id">游戏世界的唯一标识。</param>
        /// <returns>游戏世界实例。</returns>
        internal static World Create(Scene scene, byte id)
        {
            if (!WorldConfigData.Instance.TryGet(id, out var worldConfigData))
            {
                return null;
            }

            if (worldConfigData.DatabaseConfig == null || worldConfigData.DatabaseConfig.Length == 0)
            {
                return null;
            }
            
            return new World(scene, id);
        }

        /// <summary>
        /// 释放游戏世界资源。
        /// </summary>
        public void Dispose()
        {
            AllDatabase.Clear();
        }
    }
}

#endif