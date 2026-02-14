#if FANTASY_NET
using System;
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
        public Fantasy.Database.IDatabase Database { get; private set; }
        /// <summary>
        /// 本世界所有数据库, 按照数据库名字生成的索引来存取。
        /// </summary>
        private Fantasy.Database.IDatabase[] AllDatabase { get; init; }

        /// <summary>
        /// 选择数据库为当前使用数据库，成功后会可以用World.Database使用，避免再次查询一下
        /// </summary>
        /// <param name="dbName">数据库名字</param>
        /// <returns></returns>
        public Fantasy.Database.IDatabase SelectDatabase(int dbName)
        {
            var database = this[dbName];
            if (database == null)
            {
                return null;
            }

            Database = database;
            return database;
        }

        /// <summary>
        /// 根据数据库名字获取某个类型的数据库。
        /// </summary>
        /// <typeparam name="T">数据库类型</typeparam>
        /// <param name="dbName">数据库名字</param>
        /// <param name="database">返回的数据库实例</param>
        /// <returns></returns>
        public bool TryGetDatabase<T>(int dbName, out T database) where T : class, Fantasy.Database.IDatabase
        {
            var currentDatabase = this[dbName];
            
            if (currentDatabase == null)
            {
                database = null;
                return false;
            }

            database = currentDatabase as T;
            return true;
        }

        /// <summary>
        /// 根据数据库名字获取某个类型的数据库。
        /// </summary>
        /// <param name="dbName"></param>
        public Fantasy.Database.IDatabase? this[int dbName] => dbName >= 0 && dbName < AllDatabase.Length ? AllDatabase[dbName] : null;

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
            var worldConfig = WorldConfigData.Instance.Get(worldConfigId);
            var dataBaseConfigList = worldConfig.DatabaseConfig;
            Id = worldConfigId;  
            Name = Config.WorldName;

            if (dataBaseConfigList == null || dataBaseConfigList.Length == 0)
            {
                AllDatabase = [];
                return;
            }
            
            AllDatabase = new Fantasy.Database.IDatabase[DataBaseHelper.DatabaseDbName.Count];
            
            for (var i = 0; i < dataBaseConfigList.Length; i++)
            {
                var dataBaseConfig = dataBaseConfigList[i];
                if (dataBaseConfig.DbConnection != null && !string.IsNullOrWhiteSpace(dataBaseConfig.DbConnection))
                {
                    var dbType = dataBaseConfig.DbType.ToLower();
                    switch (dbType)
                    {
                        case "mongodb":
                        case "mongo":
                        {
                            if (dataBaseConfig.DbConnection != null)
                            {
                                try
                                {
                                    var mongoDataBase = new MongoDatabase();
                                    var mongoDataBaseIndex = DataBaseHelper.DatabaseDbName[dataBaseConfig.DbName];
                                    mongoDataBase.Initialize(scene, dataBaseConfig.DbConnection, dataBaseConfig.DbName);
                                    AllDatabase[mongoDataBaseIndex] = mongoDataBase;
                                }
                                catch (Exception e)
                                {
                                    Log.Error($"WorldId:{Id} DbName:{dataBaseConfig.DbName} DbConnection:{dataBaseConfig.DbConnection} Initialization failed. Please check if the target database server can be connected normally.\n{e.Message}");
                                }
                            }
                            break;
                        }
                    }
                }
            }
            
            Database = AllDatabase.Length > 0 ? AllDatabase[0] : null;
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
            Array.Clear(AllDatabase);
        }
    }
}

#endif