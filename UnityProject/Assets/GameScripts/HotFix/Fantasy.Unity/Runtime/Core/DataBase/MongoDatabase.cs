#if FANTASY_NET
using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Fantasy.Async;
using Fantasy.DataStructure.Collection;
using Fantasy.Entitas;
using Fantasy.Helper;
using Fantasy.Serialize;
using MongoDB.Bson;
using MongoDB.Driver;
#pragma warning disable CS8602 // Dereference of a possibly null reference.
#pragma warning disable CS8603 // Possible null reference return.
#pragma warning disable CS8625 // Cannot convert null literal to non-nullable reference type.
#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.

namespace Fantasy.Database
{
    /// <summary>
    /// 使用 MongoDB 数据库的实现。
    /// </summary>
    public sealed partial class MongoDatabase : IDatabase
    {
        private const int DefaultTaskSize = 1024;
        private Scene _scene;
        private MongoClient _mongoClient;
        private BsonPackHelper _serializer;
        private IMongoDatabase _mongoDatabase;
        private CoroutineLock _dataBaseLock;
        private readonly HashSet<string> _collections = new HashSet<string>();
        /// <summary>
        /// 获得当前数据的类型
        /// </summary>
        public DatabaseType DatabaseType { get; } = DatabaseType.MongoDB;
        /// <summary>
        /// 数据库名字
        /// </summary>
        public string Name { get; private set; }

        /// <summary>
        /// 获得对应数据的操作实例
        /// </summary>
        public object GetDatabaseInstance => _mongoDatabase;
        /// <summary>
        /// 初始化 MongoDB 数据库连接并记录所有集合名。
        /// </summary>
        /// <param name="scene">场景对象。</param>
        /// <param name="connectionString">数据库连接字符串。</param>
        /// <param name="dbName">数据库名称。</param>
        /// <returns>初始化后的数据库实例。</returns>
        public IDatabase Initialize(Scene scene, string connectionString, string dbName)
        {
            try
            {
                Log.Info($"dbName:{dbName} Initialize the db database and connect to the target.");
                _scene = scene;
                _mongoClient = DataBaseSetting.MongoDbCustomInitialize != null
                    ? DataBaseSetting.MongoDbCustomInitialize(new DataBaseCustomConfig()
                    {
                        Scene = scene, ConnectionString = connectionString, DBName = dbName
                    })
                    : new MongoClient(connectionString);
                Name = dbName;
                _mongoDatabase = _mongoClient.GetDatabase(dbName);
                _dataBaseLock = scene.CoroutineLockComponent.Create(GetType().TypeHandle.Value.ToInt64());
                // 记录所有集合名
                _collections.UnionWith(_mongoDatabase.ListCollectionNames().ToList());
                _serializer = SerializerManager.BsonPack;
                Log.Info($"dbName:{dbName} Database connection successful.");
            }
            catch (Exception e)
            {
                Log.Error($"dbName:{dbName} cannot connect to the database. Please check if the connectionString is correct or the network conditions.\n{e.Message}");
            }
            
            return this;
        }
        
        /// <summary>
        /// 销毁释放资源。
        /// </summary>
        public void Dispose()
        {
            // 优先释放协程锁。
            _dataBaseLock.Dispose();
            // 清理资源。
            Name = null;
            _scene = null;
            _serializer = null;
            _mongoDatabase = null;
            _dataBaseLock = null;
            _collections.Clear();
            _mongoClient.Dispose();
        }

        #region Other

        /// <summary>
        /// 对满足条件的文档中的某个数值字段进行求和操作。
        /// </summary>
        /// <typeparam name="T">实体类型。</typeparam>
        /// <param name="filter">用于筛选文档的表达式。</param>
        /// <param name="sumExpression">要对其进行求和的字段表达式。</param>
        /// <param name="collection">集合名称，可选。如果未指定，将使用实体类型的名称。</param>
        /// <returns>满足条件的文档中指定字段的求和结果。</returns>
        public async FTask<long> Sum<T>(Expression<Func<T, bool>> filter, Expression<Func<T, object>> sumExpression, string collection = null) where T : Entity
        {
            var member = (MemberExpression)((UnaryExpression)sumExpression.Body).Operand;
            var projection = new BsonDocument("_id", "null").Add("Result", new BsonDocument("$sum", $"${member.Member.Name}"));
            var data = await GetCollection<T>(collection).Aggregate().Match(filter).Group(projection).FirstOrDefaultAsync();
            return data == null ? 0 : Convert.ToInt64(data["Result"]);
        }

        #endregion

        #region GetCollection

        /// <summary>
        /// 获取指定集合中的 MongoDB 文档的 IMongoCollection 对象。
        /// </summary>
        /// <typeparam name="T">实体类型。</typeparam>
        /// <param name="collection">集合名称，可选。如果未指定，将使用实体类型的名称。</param>
        /// <returns>IMongoCollection 对象。</returns>
        private IMongoCollection<T> GetCollection<T>(string collection = null)
        {
            return _mongoDatabase.GetCollection<T>(collection ?? typeof(T).Name);
        }

        /// <summary>
        /// 获取指定集合中的 MongoDB 文档的 IMongoCollection 对象，其中实体类型为 Entity。
        /// </summary>
        /// <param name="name">集合名称。</param>
        /// <returns>IMongoCollection 对象。</returns>
        private IMongoCollection<Entity> GetCollection(string name)
        {
            return _mongoDatabase.GetCollection<Entity>(name);
        }

        #endregion

        #region Count

        /// <summary>
        /// 统计指定集合中满足条件的文档数量。
        /// </summary>
        /// <typeparam name="T">实体类型。</typeparam>
        /// <param name="collection">集合名称，可选。如果未指定，将使用实体类型的名称。</param>
        /// <returns>满足条件的文档数量。</returns>
        public async FTask<long> Count<T>(string collection = null) where T : Entity
        {
            return await GetCollection<T>(collection).CountDocumentsAsync(d => true);
        }

        /// <summary>
        /// 统计指定集合中满足条件的文档数量。
        /// </summary>
        /// <typeparam name="T">实体类型。</typeparam>
        /// <param name="filter">用于筛选文档的表达式。</param>
        /// <param name="collection">集合名称，可选。如果未指定，将使用实体类型的名称。</param>
        /// <returns>满足条件的文档数量。</returns>
        public async FTask<long> Count<T>(Expression<Func<T, bool>> filter, string collection = null) where T : Entity
        {
            return await GetCollection<T>(collection).CountDocumentsAsync(filter);
        }

        #endregion

        #region Exist

        /// <summary>
        /// 判断指定集合中是否存在文档。
        /// </summary>
        /// <typeparam name="T">实体类型。</typeparam>
        /// <param name="collection">集合名称，可选。如果未指定，将使用实体类型的名称。</param>
        /// <returns>如果存在文档则返回 true，否则返回 false。</returns>
        public async FTask<bool> Exist<T>(string collection = null) where T : Entity
        {
            return await Count<T>(collection) > 0;
        }

        /// <summary>
        /// 判断指定集合中是否存在满足条件的文档。
        /// </summary>
        /// <typeparam name="T">实体类型。</typeparam>
        /// <param name="filter">用于筛选文档的表达式。</param>
        /// <param name="collection">集合名称，可选。如果未指定，将使用实体类型的名称。</param>
        /// <returns>如果存在满足条件的文档则返回 true，否则返回 false。</returns>
        public async FTask<bool> Exist<T>(Expression<Func<T, bool>> filter, string collection = null) where T : Entity
        {
            return await Count(filter, collection) > 0;
        }

        #endregion

        #region Query

        /// <summary>
        /// 在不加数据库锁定的情况下，查询指定 ID 的文档。
        /// </summary>
        /// <typeparam name="T">文档实体类型。</typeparam>
        /// <param name="id">要查询的文档 ID。</param>
        /// <param name="isDeserialize">是否在查询后反序列化,执行反序列化后会自动将实体注册到框架系统中，并且能正常使用组件相关功能。</param>
        /// <param name="collection">集合名称。</param>
        /// <returns>查询到的文档。</returns>
        public async FTask<T> QueryNotLock<T>(long id, bool isDeserialize = false, string collection = null) where T : Entity
        {
            var cursor = await GetCollection<T>(collection).FindAsync(d => d.Id == id);
            var v = await cursor.FirstOrDefaultAsync();

            if (isDeserialize && v != null)
            {
                v.Deserialize(_scene);
            }

            return v;
        }

        /// <summary>
        /// 查询指定 ID 的文档，并加数据库锁定以确保数据一致性。
        /// </summary>
        /// <typeparam name="T">文档实体类型。</typeparam>
        /// <param name="id">要查询的文档 ID。</param>
        /// <param name="isDeserialize">是否在查询后反序列化,执行反序列化后会自动将实体注册到框架系统中，并且能正常使用组件相关功能。</param>
        /// <param name="collection">集合名称。</param>
        /// <returns>查询到的文档。</returns>
        public async FTask<T> Query<T>(long id, bool isDeserialize = false, string collection = null) where T : Entity
        {
            using (await _dataBaseLock.Wait(id))
            {
                var cursor = await GetCollection<T>(collection).FindAsync(d => d.Id == id);
                var v = await cursor.FirstOrDefaultAsync();

                if (isDeserialize && v != null)
                {
                    v.Deserialize(_scene);
                }

                return v;
            }
        }

        /// <summary>
        /// 通过分页查询并返回满足条件的文档数量和日期列表（不加锁）。
        /// </summary>
        /// <typeparam name="T">文档实体类型。</typeparam>
        /// <param name="filter">查询过滤条件。</param>
        /// <param name="pageIndex">页码。</param>
        /// <param name="pageSize">每页大小。</param>
        /// <param name="isDeserialize">是否在查询后反序列化,执行反序列化后会自动将实体注册到框架系统中，并且能正常使用组件相关功能。</param>
        /// <param name="collection">集合名称。</param>
        /// <returns>满足条件的文档数量和日期列表。</returns>
        public async FTask<(int count, List<T> dates)> QueryCountAndDatesByPage<T>(Expression<Func<T, bool>> filter, int pageIndex, int pageSize, bool isDeserialize = false, string collection = null) where T : Entity
        {
            using (await _dataBaseLock.Wait(RandomHelper.RandInt64() % DefaultTaskSize))
            {
                var count = await Count(filter);
                var dates = await QueryByPage(filter, pageIndex, pageSize, isDeserialize, collection);
                return ((int)count, dates);
            }
        }

        /// <summary>
        /// 通过分页查询并返回满足条件的文档数量和日期列表（加锁）。
        /// </summary>
        /// <typeparam name="T">文档实体类型。</typeparam>
        /// <param name="filter">查询过滤条件。</param>
        /// <param name="pageIndex">页码。</param>
        /// <param name="pageSize">每页大小。</param>
        /// <param name="cols">要查询的列名称数组。</param>
        /// <param name="isDeserialize">是否在查询后反序列化,执行反序列化后会自动将实体注册到框架系统中，并且能正常使用组件相关功能。</param>
        /// <param name="collection">集合名称。</param>
        /// <returns>满足条件的文档数量和日期列表。</returns>
        public async FTask<(int count, List<T> dates)> QueryCountAndDatesByPage<T>(Expression<Func<T, bool>> filter, int pageIndex, int pageSize, string[] cols, bool isDeserialize = false, string collection = null) where T : Entity
        {
            using (await _dataBaseLock.Wait(RandomHelper.RandInt64() % DefaultTaskSize))
            {
                var count = await Count(filter);
                var dates = await QueryByPage(filter, pageIndex, pageSize, cols, isDeserialize, collection);
                return ((int)count, dates);
            }
        }

        /// <summary>
        /// 通过分页查询并返回满足条件的文档列表（不加锁）。
        /// </summary>
        /// <typeparam name="T">文档实体类型。</typeparam>
        /// <param name="filter">查询过滤条件。</param>
        /// <param name="pageIndex">页码。</param>
        /// <param name="pageSize">每页大小。</param>
        /// <param name="isDeserialize">是否在查询后反序列化,执行反序列化后会自动将实体注册到框架系统中，并且能正常使用组件相关功能。</param>
        /// <param name="collection">集合名称。</param>
        /// <returns>满足条件的文档列表。</returns>
        public async FTask<List<T>> QueryByPage<T>(Expression<Func<T, bool>> filter, int pageIndex, int pageSize, bool isDeserialize = false, string collection = null) where T : Entity
        {
            using (await _dataBaseLock.Wait(RandomHelper.RandInt64() % DefaultTaskSize))
            {
                var list = await GetCollection<T>(collection).Find(filter).Skip((pageIndex - 1) * pageSize)
                    .Limit(pageSize)
                    .ToListAsync();

                if (!isDeserialize || list is not { Count: > 0 })
                {
                    return list;
                }

                foreach (var entity in list)
                {
                    entity.Deserialize(_scene);
                }

                return list;
            }
        }

        /// <summary>
        /// 通过分页查询并返回满足条件的文档列表（加锁）。
        /// </summary>
        /// <typeparam name="T">文档实体类型。</typeparam>
        /// <param name="filter">查询过滤条件。</param>
        /// <param name="pageIndex">页码。</param>
        /// <param name="pageSize">每页大小。</param>
        /// <param name="cols">要查询的列名称数组。</param>
        /// <param name="isDeserialize">是否在查询后反序列化,执行反序列化后会自动将实体注册到框架系统中，并且能正常使用组件相关功能。</param>
        /// <param name="collection">集合名称。</param>
        /// <returns>满足条件的文档列表。</returns>
        public async FTask<List<T>> QueryByPage<T>(Expression<Func<T, bool>> filter, int pageIndex, int pageSize, string[] cols, bool isDeserialize = false, string collection = null) where T : Entity
        {
            using (await _dataBaseLock.Wait(RandomHelper.RandInt64() % DefaultTaskSize))
            {
                var projection = Builders<T>.Projection.Include("");

                foreach (var col in cols)
                {
                    projection = projection.Include(col);
                }
                
                var list = await GetCollection<T>(collection).Find(filter).Project<T>(projection)
                    .Skip((pageIndex - 1) * pageSize).Limit(pageSize).ToListAsync();

                if (!isDeserialize || list is not { Count: > 0 })
                {
                    return list;
                }

                foreach (var entity in list)
                {
                    entity.Deserialize(_scene);
                }

                return list;
            }
        }

        /// <summary>
        /// 通过分页查询并返回满足条件的文档列表，并按指定表达式进行排序（加锁）。
        /// </summary>
        /// <typeparam name="T">文档实体类型。</typeparam>
        /// <param name="filter">查询过滤条件。</param>
        /// <param name="pageIndex">页码。</param>
        /// <param name="pageSize">每页大小。</param>
        /// <param name="orderByExpression">排序表达式。</param>
        /// <param name="isAsc">是否升序排序。</param>
        /// <param name="isDeserialize">是否在查询后反序列化,执行反序列化后会自动将实体注册到框架系统中，并且能正常使用组件相关功能。</param>
        /// <param name="collection">集合名称。</param>
        /// <returns>满足条件的文档列表。</returns>
        public async FTask<List<T>> QueryByPageOrderBy<T>(Expression<Func<T, bool>> filter, int pageIndex, int pageSize, Expression<Func<T, object>> orderByExpression, bool isAsc = true, bool isDeserialize = false, string collection = null) where T : Entity
        {
            using (await _dataBaseLock.Wait(RandomHelper.RandInt64() % DefaultTaskSize))
            {
                List<T> list;
                
                if (isAsc)
                {
                    list = await GetCollection<T>(collection).Find(filter).SortBy(orderByExpression).Skip((pageIndex - 1) * pageSize).Limit(pageSize).ToListAsync();
                }
                else
                {
                    list = await GetCollection<T>(collection).Find(filter).SortByDescending(orderByExpression).Skip((pageIndex - 1) * pageSize).Limit(pageSize).ToListAsync();
                }

                if (!isDeserialize || list is not { Count: > 0 })
                {
                    return list;
                }
                
                foreach (var entity in list)
                {
                    entity.Deserialize(_scene);
                }

                return list;
            }
        }

        /// <summary>
        /// 通过指定过滤条件查询并返回满足条件的第一个文档（加锁）。
        /// </summary>
        /// <typeparam name="T">文档实体类型。</typeparam>
        /// <param name="filter">查询过滤条件。</param>
        /// <param name="isDeserialize">是否在查询后反序列化,执行反序列化后会自动将实体注册到框架系统中，并且能正常使用组件相关功能。</param>
        /// <param name="collection">集合名称。</param>
        /// <returns>满足条件的第一个文档，如果未找到则为 null。</returns>
        public async FTask<T?> First<T>(Expression<Func<T, bool>> filter, bool isDeserialize = false, string collection = null) where T : Entity
        {
            using (await _dataBaseLock.Wait(RandomHelper.RandInt64() % DefaultTaskSize))
            {
                var cursor = await GetCollection<T>(collection).FindAsync(filter);
                var t = await cursor.FirstOrDefaultAsync();

                if (isDeserialize && t != null)
                {
                    t.Deserialize(_scene);
                }

                return t;
            }
        }

        /// <summary>
        /// 通过指定 JSON 格式查询并返回满足条件的第一个文档（加锁）。
        /// </summary>
        /// <typeparam name="T">文档实体类型。</typeparam>
        /// <param name="json">JSON 查询条件。</param>
        /// <param name="cols">要查询的列名称数组。</param>
        /// <param name="isDeserialize">是否在查询后反序列化,执行反序列化后会自动将实体注册到框架系统中，并且能正常使用组件相关功能。</param>
        /// <param name="collection">集合名称。</param>
        /// <returns>满足条件的第一个文档。</returns>
        public async FTask<T> First<T>(string json, string[] cols, bool isDeserialize = false, string collection = null) where T : Entity
        {
            using (await _dataBaseLock.Wait(RandomHelper.RandInt64() % DefaultTaskSize))
            {
                var projection = Builders<T>.Projection.Include("");

                foreach (var col in cols)
                {
                    projection = projection.Include(col);
                }

                var options = new FindOptions<T, T> { Projection = projection };

                FilterDefinition<T> filterDefinition = new JsonFilterDefinition<T>(json);

                var cursor = await GetCollection<T>(collection).FindAsync(filterDefinition, options);
                var t = await cursor.FirstOrDefaultAsync();

                if (isDeserialize && t != null)
                {
                    t.Deserialize(_scene);
                }

                return t;
            }
        }

        /// <summary>
        /// 通过指定过滤条件查询并返回满足条件的文档列表，并按指定表达式进行排序（加锁）。
        /// </summary>
        /// <typeparam name="T">文档实体类型。</typeparam>
        /// <param name="filter">查询过滤条件。</param>
        /// <param name="orderByExpression">排序表达式。</param>
        /// <param name="isAsc">是否升序排序。</param>
        /// <param name="isDeserialize">是否在查询后反序列化,执行反序列化后会自动将实体注册到框架系统中，并且能正常使用组件相关功能。</param>
        /// <param name="collection">集合名称。</param>
        /// <returns>满足条件的文档列表。</returns>
        public async FTask<List<T>> QueryOrderBy<T>(Expression<Func<T, bool>> filter, Expression<Func<T, object>> orderByExpression, bool isAsc = true, bool isDeserialize = false, string collection = null) where T : Entity
        {
            using (await _dataBaseLock.Wait(RandomHelper.RandInt64() % DefaultTaskSize))
            {
                List<T> list;
                
                if (isAsc)
                {
                    list = await GetCollection<T>(collection).Find(filter).SortBy(orderByExpression).ToListAsync();
                }
                else
                {
                    list = await GetCollection<T>(collection).Find(filter).SortByDescending(orderByExpression).ToListAsync();
                }

                if (!isDeserialize || list is not { Count: > 0 })
                {
                    return list;
                }
                
                foreach (var entity in list)
                {
                    entity.Deserialize(_scene);
                }

                return list;
            }
        }

        /// <summary>
        /// 通过指定过滤条件查询并返回满足条件的文档列表（加锁）。
        /// </summary>
        /// <typeparam name="T">文档实体类型。</typeparam>
        /// <param name="filter">查询过滤条件。</param>
        /// <param name="isDeserialize">是否在查询后反序列化,执行反序列化后会自动将实体注册到框架系统中，并且能正常使用组件相关功能。</param>
        /// <param name="collection">集合名称。</param>
        /// <returns>满足条件的文档列表。</returns>
        public async FTask<List<T>> Query<T>(Expression<Func<T, bool>> filter, bool isDeserialize = false, string collection = null) where T : Entity
        {
            using (await _dataBaseLock.Wait(RandomHelper.RandInt64() % DefaultTaskSize))
            {
                var cursor = await GetCollection<T>(collection).FindAsync(filter);
                var list = await cursor.ToListAsync();
                
                if (!isDeserialize || list is not { Count: > 0 })
                {
                    return list;
                }
                
                foreach (var entity in list)
                {
                    entity.Deserialize(_scene);
                }
                
                return list;
            }
        }

        /// <summary>
        /// 根据指定 ID 加锁查询多个集合中的文档。
        /// </summary>
        /// <param name="id">文档 ID。</param>
        /// <param name="collectionNames">要查询的集合名称列表。</param>
        /// <param name="result">查询结果存储列表。</param>
        /// <param name="isDeserialize">是否在查询后反序列化,执行反序列化后会自动将实体注册到框架系统中，并且能正常使用组件相关功能。</param>
        public async FTask Query(long id, List<string>? collectionNames, List<Entity> result, bool isDeserialize = false)
        {
            using (await _dataBaseLock.Wait(id))
            {
                if (collectionNames == null || collectionNames.Count == 0)
                {
                    return;
                }

                foreach (var collectionName in collectionNames)
                {
                    var cursor = await GetCollection(collectionName).FindAsync(d => d.Id == id);

                    var e = await cursor.FirstOrDefaultAsync();

                    if (e == null)
                    {
                        continue;
                    }

                    if (isDeserialize)
                    {
                        e.Deserialize(_scene);
                    }

                    result.Add(e);
                }
            }
        }

        /// <summary>
        /// 根据指定的 JSON 查询条件查询并返回满足条件的文档列表（加锁）。
        /// </summary>
        /// <typeparam name="T">文档实体类型。</typeparam>
        /// <param name="json">JSON 查询条件。</param>
        /// <param name="isDeserialize">是否在查询后反序列化,执行反序列化后会自动将实体注册到框架系统中，并且能正常使用组件相关功能。</param>
        /// <param name="collection">集合名称。</param>
        /// <returns>满足条件的文档列表。</returns>
        public async FTask<List<T>> QueryJson<T>(string json, bool isDeserialize = false, string collection = null) where T : Entity
        {
            using (await _dataBaseLock.Wait(RandomHelper.RandInt64() % DefaultTaskSize))
            {
                FilterDefinition<T> filterDefinition = new JsonFilterDefinition<T>(json);
                var cursor = await GetCollection<T>(collection).FindAsync(filterDefinition);
                var list = await cursor.ToListAsync();
                
                if (!isDeserialize || list is not { Count: > 0 })
                {
                    return list;
                }
                
                foreach (var entity in list)
                {
                    entity.Deserialize(_scene);
                }
                
                return list;
            }
        }

        /// <summary>
        /// 根据指定的 JSON 查询条件查询并返回满足条件的文档列表，并选择指定的列（加锁）。
        /// </summary>
        /// <typeparam name="T">文档实体类型。</typeparam>
        /// <param name="json">JSON 查询条件。</param>
        /// <param name="cols">要查询的列名称数组。</param>
        /// <param name="isDeserialize">是否在查询后反序列化,执行反序列化后会自动将实体注册到框架系统中，并且能正常使用组件相关功能。</param>
        /// <param name="collection">集合名称。</param>
        /// <returns>满足条件的文档列表。</returns>
        public async FTask<List<T>> QueryJson<T>(string json, string[] cols, bool isDeserialize = false, string collection = null) where T : Entity
        {
            using (await _dataBaseLock.Wait(RandomHelper.RandInt64() % DefaultTaskSize))
            {
                var projection = Builders<T>.Projection.Include("");

                foreach (var col in cols)
                {
                    projection = projection.Include(col);
                }

                var options = new FindOptions<T, T> { Projection = projection };

                FilterDefinition<T> filterDefinition = new JsonFilterDefinition<T>(json);

                var cursor = await GetCollection<T>(collection).FindAsync(filterDefinition, options);
                var list = await cursor.ToListAsync();
                
                if (!isDeserialize || list is not { Count: > 0 })
                {
                    return list;
                }
                
                foreach (var entity in list)
                {
                    entity.Deserialize(_scene);
                }
                
                return list;
            }
        }

        /// <summary>
        /// 根据指定的 JSON 查询条件和任务 ID 查询并返回满足条件的文档列表（加锁）。
        /// </summary>
        /// <typeparam name="T">文档实体类型。</typeparam>
        /// <param name="taskId">任务 ID。</param>
        /// <param name="json">JSON 查询条件。</param>
        /// <param name="isDeserialize">是否在查询后反序列化,执行反序列化后会自动将实体注册到框架系统中，并且能正常使用组件相关功能。</param>
        /// <param name="collection">集合名称。</param>
        /// <returns>满足条件的文档列表。</returns>
        public async FTask<List<T>> QueryJson<T>(long taskId, string json, bool isDeserialize = false, string collection = null) where T : Entity
        {
            using (await _dataBaseLock.Wait(taskId))
            {
                FilterDefinition<T> filterDefinition = new JsonFilterDefinition<T>(json);
                var cursor = await GetCollection<T>(collection).FindAsync(filterDefinition);
                var list = await cursor.ToListAsync();
                
                if (!isDeserialize || list is not { Count: > 0 })
                {
                    return list;
                }
                
                foreach (var entity in list)
                {
                    entity.Deserialize(_scene);
                }
                
                return list;
            }
        }

        /// <summary>
        /// 根据指定过滤条件查询并返回满足条件的文档列表，选择指定的列（加锁）。
        /// </summary>
        /// <typeparam name="T">文档实体类型。</typeparam>
        /// <param name="filter">查询过滤条件。</param>
        /// <param name="cols">要查询的列名称数组。</param>
        /// <param name="isDeserialize">是否在查询后反序列化,执行反序列化后会自动将实体注册到框架系统中，并且能正常使用组件相关功能。</param>
        /// <param name="collection">集合名称。</param>
        /// <returns>满足条件的文档列表。</returns>
        public async FTask<List<T>> Query<T>(Expression<Func<T, bool>> filter, string[] cols, bool isDeserialize = false, string collection = null) where T : Entity
        {
            using (await _dataBaseLock.Wait(RandomHelper.RandInt64() % DefaultTaskSize))
            {
                var projection = Builders<T>.Projection.Include("_id");

                foreach (var t in cols)
                {
                    projection = projection.Include(t);
                }

                var list = await GetCollection<T>(collection).Find(filter).Project<T>(projection).ToListAsync();
                
                if (!isDeserialize || list is not { Count: > 0 })
                {
                    return list;
                }
                
                foreach (var entity in list)
                {
                    entity.Deserialize(_scene);
                }
                
                return list;
            }
        }

        /// <summary>
        /// 根据指定过滤条件查询并返回满足条件的文档列表，选择指定的列（加锁）。
        /// </summary>
        /// <param name="filter">文档实体类型。</param>
        /// <param name="cols">查询过滤条件。</param>
        /// <param name="isDeserialize">要查询的列名称数组。</param>
        /// <param name="collection">是否在查询后反序列化,执行反序列化后会自动将实体注册到框架系统中，并且能正常使用组件相关功能。</param>
        /// <typeparam name="T">集合名称。</typeparam>
        /// <returns></returns>
        public async FTask<List<T>> Query<T>(Expression<Func<T, bool>> filter, Expression<Func<T, object>>[] cols, bool isDeserialize = false, string collection = null) where T : Entity
        {
            using (await _dataBaseLock.Wait(RandomHelper.RandInt64() % DefaultTaskSize))
            {
                var projection = Builders<T>.Projection.Include("_id");

                foreach (var col in cols)
                {
                    if (col.Body is not MemberExpression memberExpression)
                    {
                        throw new ArgumentException("Lambda expression must be a member access expression.");
                    }

                    projection = projection.Include(memberExpression.Member.Name);
                }

                var list = await GetCollection<T>(collection).Find(filter).Project<T>(projection).ToListAsync();

                if (!isDeserialize || list is not { Count: > 0 })
                {
                    return list;
                }

                foreach (var entity in list)
                {
                    entity.Deserialize(_scene);
                }

                return list;
            }
        }

        #endregion

        #region Save

        /// <summary>
        /// 保存实体对象到数据库（加锁）。
        /// </summary>
        /// <typeparam name="T">实体类型。</typeparam>
        /// <param name="transactionSession">事务会话对象。</param>
        /// <param name="entity">要保存的实体对象。</param>
        /// <param name="collection">集合名称。</param>
        public async FTask Save<T>(object transactionSession, T? entity, string collection = null) where T : Entity
        {
            if (entity == null)
            {
                Log.Error($"save entity is null: {typeof(T).Name}");
                return;
            }

            var clone = _serializer.CloneEntity<T>(entity);

            using (await _dataBaseLock.Wait(clone.Id))
            {
                await GetCollection<T>(collection).ReplaceOneAsync(
                    (IClientSessionHandle)transactionSession, d => d.Id == clone.Id, clone,
                    new ReplaceOptions { IsUpsert = true });
            }
        }

        /// <summary>
        /// 保存实体对象到数据库（加锁）。
        /// </summary>
        /// <typeparam name="T">实体类型。</typeparam>
        /// <param name="entity">要保存的实体对象。</param>
        /// <param name="collection">集合名称。</param>
        public async FTask Save<T>(T? entity, string collection = null) where T : Entity
        {
            if (entity == null)
            {
                Log.Error($"save entity is null: {typeof(T).Name}");

                return;
            }

            var clone = _serializer.CloneEntity(entity);

            using (await _dataBaseLock.Wait(clone.Id))
            {
                await GetCollection<T>(collection).ReplaceOneAsync(d => d.Id == clone.Id, clone, new ReplaceOptions { IsUpsert = true });
            }
        }

        /// <summary>
        /// 保存实体对象到数据库（加锁）。
        /// </summary>
        /// <param name="filter">保存的条件表达式。</param>
        /// <param name="entity">实体类型。</param>
        /// <param name="collection">集合名称。</param>
        /// <typeparam name="T"></typeparam>
        public async FTask Save<T>(Expression<Func<T, bool>> filter, T? entity, string collection = null) where T : Entity, new()
        {
            if (entity == null)
            {
                Log.Error($"save entity is null: {typeof(T).Name}");
                return;
            }

            T clone = _serializer.CloneEntity(entity);

            using (await _dataBaseLock.Wait(clone.Id))
            {
                await GetCollection<T>(collection).ReplaceOneAsync<T>(filter, clone, new ReplaceOptions { IsUpsert = true });
            }
        }

        /// <summary>
        /// 保存多个实体对象到数据库（加锁）。
        /// </summary>
        /// <param name="id">文档 ID。</param>
        /// <param name="entities">要保存的实体对象列表。</param>
        public async FTask Save(long id, List<(Entity, string)>? entities)
        {
            if (entities == null || entities.Count == 0)
            {
                Log.Error("save entity is null");
                return;
            }

            using var listPool = ListPool<(Entity, string)>.Create();
            
            foreach (var entity in entities)
            {
                listPool.Add((_serializer.CloneEntity(entity.Item1), entity.Item2));
            }

            using (await _dataBaseLock.Wait(id))
            {
                foreach (var clone in listPool)
                {
                    try
                    {
                        await GetCollection(clone.Item2).ReplaceOneAsync(d => d.Id == clone.Item1.Id, clone.Item1, new ReplaceOptions { IsUpsert = true });
                    }
                    catch (Exception e)
                    {
                        Log.Error($"Save List Entity Error: {clone.Item2} {clone}\n{e}");
                    }
                }
            }
        }

        #endregion

        #region Insert

        /// <summary>
        /// 插入单个实体对象到数据库（加锁）。
        /// </summary>
        /// <typeparam name="T">实体类型。</typeparam>
        /// <param name="entity">要插入的实体对象。</param>
        /// <param name="collection">集合名称。</param>
        public async FTask Insert<T>(T? entity, string collection = null) where T : Entity, new()
        {
            if (entity == null)
            {
                Log.Error($"insert entity is null: {typeof(T).Name}");
                return;
            }

            var clone = _serializer.CloneEntity(entity);
            
            using (await _dataBaseLock.Wait(entity.Id))
            {
                await GetCollection<T>(collection).InsertOneAsync(clone);
            }
        }

        /// <summary>
        /// 批量插入实体对象列表到数据库（加锁）。
        /// </summary>
        /// <typeparam name="T">实体类型。</typeparam>
        /// <param name="list">要插入的实体对象列表。</param>
        /// <param name="collection">集合名称。</param>
        public async FTask InsertBatch<T>(IEnumerable<T> list, string collection = null) where T : Entity, new()
        {
            using (await _dataBaseLock.Wait(RandomHelper.RandInt64() % DefaultTaskSize))
            {
                await GetCollection<T>(collection).InsertManyAsync(list);
            }
        }

        /// <summary>
        /// 批量插入实体对象列表到数据库（加锁）。
        /// </summary>
        /// <typeparam name="T">实体类型。</typeparam>
        /// <param name="transactionSession">事务会话对象。</param>
        /// <param name="list">要插入的实体对象列表。</param>
        /// <param name="collection">集合名称。</param>
        public async FTask InsertBatch<T>(object transactionSession, IEnumerable<T> list, string collection = null)
            where T : Entity, new()
        {
            using (await _dataBaseLock.Wait(RandomHelper.RandInt64() % DefaultTaskSize))
            {
                await GetCollection<T>(collection).InsertManyAsync((IClientSessionHandle)transactionSession, list);
            }
        }
        
        /// <summary>
        /// 插入BsonDocument到数据库（加锁）。
        /// </summary>
        /// <param name="bsonDocument"></param>
        /// <param name="taskId"></param>
        /// <typeparam name="T"></typeparam>
        public async Task Insert<T>(BsonDocument bsonDocument, long taskId) where T : Entity
        {
            using (await _dataBaseLock.Wait(taskId))
            {
                await GetCollection<BsonDocument>(typeof(T).Name).InsertOneAsync(bsonDocument);
            }
        }

        #endregion

        #region Remove

        /// <summary>
        /// 根据ID删除单个实体对象（加锁）。
        /// </summary>
        /// <typeparam name="T">实体类型。</typeparam>
        /// <param name="transactionSession">事务会话对象。</param>
        /// <param name="id">要删除的实体的ID。</param>
        /// <param name="collection">集合名称。</param>
        /// <returns>删除的实体数量。</returns>
        public async FTask<long> Remove<T>(object transactionSession, long id, string collection = null)
            where T : Entity, new()
        {
            using (await _dataBaseLock.Wait(id))
            {
                var result = await GetCollection<T>(collection)
                    .DeleteOneAsync((IClientSessionHandle)transactionSession, d => d.Id == id);
                return result.DeletedCount;
            }
        }

        /// <summary>
        /// 根据ID删除单个实体对象（加锁）。
        /// </summary>
        /// <typeparam name="T">实体类型。</typeparam>
        /// <param name="id">要删除的实体的ID。</param>
        /// <param name="collection">集合名称。</param>
        /// <returns>删除的实体数量。</returns>
        public async FTask<long> Remove<T>(long id, string collection = null) where T : Entity, new()
        {
            using (await _dataBaseLock.Wait(id))
            {
                var result = await GetCollection<T>(collection).DeleteOneAsync(d => d.Id == id);
                return result.DeletedCount;
            }
        }

        /// <summary>
        /// 根据ID和筛选条件删除多个实体对象（加锁）。
        /// </summary>
        /// <typeparam name="T">实体类型。</typeparam>
        /// <param name="coroutineLockQueueKey">异步锁Id。</param>
        /// <param name="transactionSession">事务会话对象。</param>
        /// <param name="filter">筛选条件。</param>
        /// <param name="collection">集合名称。</param>
        /// <returns>删除的实体数量。</returns>
        public async FTask<long> Remove<T>(long coroutineLockQueueKey, object transactionSession,
            Expression<Func<T, bool>> filter, string collection = null) where T : Entity, new()
        {
            using (await _dataBaseLock.Wait(coroutineLockQueueKey))
            {
                var result = await GetCollection<T>(collection)
                    .DeleteManyAsync((IClientSessionHandle)transactionSession, filter);
                return result.DeletedCount;
            }
        }

        /// <summary>
        /// 根据ID和筛选条件删除多个实体对象（加锁）。
        /// </summary>
        /// <typeparam name="T">实体类型。</typeparam>
        /// <param name="coroutineLockQueueKey">异步锁Id。</param>
        /// <param name="filter">筛选条件。</param>
        /// <param name="collection">集合名称。</param>
        /// <returns>删除的实体数量。</returns>
        public async FTask<long> Remove<T>(long coroutineLockQueueKey, Expression<Func<T, bool>> filter,
            string collection = null) where T : Entity, new()
        {
            using (await _dataBaseLock.Wait(coroutineLockQueueKey))
            {
                var result = await GetCollection<T>(collection).DeleteManyAsync(filter);
                return result.DeletedCount;
            }
        }

        #endregion

        #region Index

        /// <summary>
        /// 创建数据库索引（加锁）。
        /// </summary>
        /// <param name="collection"></param>
        /// <param name="keys"></param>
        /// <typeparam name="T"></typeparam>
        /// <code>
        /// 使用例子(可多个):
        /// 1 : Builders.IndexKeys.Ascending(d=>d.Id)
        /// 2 : Builders.IndexKeys.Descending(d=>d.Id).Ascending(d=>d.Name)
        /// 3 : Builders.IndexKeys.Descending(d=>d.Id),Builders.IndexKeys.Descending(d=>d.Name)
        /// </code>
        public async FTask CreateIndex<T>(string collection, params object[]? keys) where T : Entity
        {
            if (keys == null || keys.Length <= 0)
            {
                return;
            }

            var indexModels = new List<CreateIndexModel<T>>();

            foreach (object key in keys)
            {
                IndexKeysDefinition<T> indexKeysDefinition = (IndexKeysDefinition<T>)key;

                indexModels.Add(new CreateIndexModel<T>(indexKeysDefinition));
            }

            await GetCollection<T>(collection).Indexes.CreateManyAsync(indexModels);
        }

        /// <summary>
        /// 创建数据库的索引（加锁）。
        /// </summary>
        /// <typeparam name="T">实体类型。</typeparam>
        /// <param name="keys">索引键定义。</param>
        public async FTask CreateIndex<T>(params object[]? keys) where T : Entity
        {
            if (keys == null)
            {
                return;
            }

            List<CreateIndexModel<T>> indexModels = new List<CreateIndexModel<T>>();

            foreach (object key in keys)
            {
                IndexKeysDefinition<T> indexKeysDefinition = (IndexKeysDefinition<T>)key;

                indexModels.Add(new CreateIndexModel<T>(indexKeysDefinition));
            }

            await GetCollection<T>().Indexes.CreateManyAsync(indexModels);
        }

        /// <summary>
        /// 创建数据库的索引（加锁）。
        /// </summary>
        /// <param name="keys"></param>
        /// <param name="options"></param>
        /// <typeparam name="T"></typeparam>
        public async FTask CreateIndex<T>(object[]? keys, object[]? options) where T : Entity
        {
            if (keys == null  || options == null)
            {
                return;
            }
            
            var indexModels = new List<CreateIndexModel<T>>();

            for (var i = 0; i < keys.Length; i++)
            {
                var indexKeysDefinition = (IndexKeysDefinition<T>)keys[i];
                var createIndexOptions = (CreateIndexOptions)options[i];
                indexModels.Add(new CreateIndexModel<T>(indexKeysDefinition, createIndexOptions));
            }

            await GetCollection<T>().Indexes.CreateManyAsync(indexModels);
        }

        #endregion

        #region CreateDB

        /// <summary>
        /// 创建数据库集合（如果不存在）。
        /// </summary>
        /// <typeparam name="T">实体类型。</typeparam>
        public async FTask CreateDB<T>() where T : Entity
        {
            // 已经存在数据库表
            string name = typeof(T).Name;

            if (_collections.Contains(name))
            {
                return;
            }

            await _mongoDatabase.CreateCollectionAsync(name);

            _collections.Add(name);
        }

        /// <summary>
        /// 创建数据库集合（如果不存在）。
        /// </summary>
        /// <param name="type">实体类型。</param>
        public async FTask CreateDB(Type type)
        {
            string name = type.Name;

            if (_collections.Contains(name))
            {
                return;
            }

            await _mongoDatabase.CreateCollectionAsync(name);

            _collections.Add(name);
        }

        #endregion
    }
}
#endif