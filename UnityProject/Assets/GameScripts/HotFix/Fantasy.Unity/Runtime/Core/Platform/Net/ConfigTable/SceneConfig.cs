#if FANTASY_NET
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using Fantasy.DataStructure.Collection;
using Fantasy.Helper;
using Fantasy.IdFactory;
using Newtonsoft.Json;
#pragma warning disable CS8603 // Possible null reference return.
#pragma warning disable CS8601 // Possible null reference assignment.

#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
namespace Fantasy.Platform.Net
{
	/// <summary>
	/// 存放所有SceneConfigInfo信息
	/// </summary>
	public sealed class SceneConfigData
	{
		/// <summary>
		/// 存放所有SceneConfig信息
		/// </summary>
		public List<SceneConfig> List;
		[JsonIgnore] 
		[IgnoreDataMember]
		private readonly ConcurrentDictionary<uint, SceneConfig> _configs = new ConcurrentDictionary<uint, SceneConfig>();
		[JsonIgnore] 
		[IgnoreDataMember]
		private readonly OneToManyList<int, SceneConfig> _sceneConfigBySceneType = new OneToManyList<int, SceneConfig>();
		[JsonIgnore] 
		[IgnoreDataMember]
		private readonly OneToManyList<uint, SceneConfig> _sceneConfigByProcess = new OneToManyList<uint, SceneConfig>();
		[JsonIgnore] [IgnoreDataMember]
		private readonly Dictionary<int, Dictionary<int, List<SceneConfig>>> _worldSceneTypes = new Dictionary<int, Dictionary<int, List<SceneConfig>>>();
		/// <summary>
		/// 获得SceneConfigData的实例
		/// </summary>
		public static SceneConfigData Instance { get; private set; }
		/// <summary>
		/// 初始化SceneConfig
		/// </summary>
		/// <param name="sceneConfigJson"></param>
		public static void InitializeFromJson(string sceneConfigJson)
		{
			try
			{
				Instance = sceneConfigJson.Deserialize<SceneConfigData>();
				Initialize();
			}
			catch (Exception e)
			{
				throw new InvalidOperationException($"SceneConfigData.Json format error {e.Message}");
			}
		}
		/// <summary>
		/// 初始化SceneConfig
		/// </summary>
		public static void Initialize(List<SceneConfig> sceneConfigs)
		{
			Instance = new SceneConfigData() { List = sceneConfigs };
			Initialize();
		}

		private static void Initialize()
		{
			foreach (var config in Instance.List)
			{
				config.Initialize();
				Instance._configs.TryAdd(config.Id, config);
				Instance._sceneConfigByProcess.Add(config.ProcessConfigId, config);
				Instance._sceneConfigBySceneType.Add(config.SceneType, config);
				
				var configWorldConfigId = (int)config.WorldConfigId;
				
				if (!Instance._worldSceneTypes.TryGetValue(configWorldConfigId, out var sceneConfigDic))
				{
					sceneConfigDic = new Dictionary<int, List<SceneConfig>>();
					Instance._worldSceneTypes.Add(configWorldConfigId, sceneConfigDic);
				}

				if (!sceneConfigDic.TryGetValue(config.SceneType, out var sceneConfigList))
				{
					sceneConfigList = new List<SceneConfig>();
					sceneConfigDic.Add(config.SceneType, sceneConfigList);
				}
				
				sceneConfigList.Add(config);
			}
		}

		/// <summary>
		/// 根据Id获取SceneConfig
		/// </summary>
		/// <param name="id"></param>
		/// <returns></returns>
		/// <exception cref="FileNotFoundException"></exception>
		public SceneConfig Get(uint id)
		{
			if (_configs.TryGetValue(id, out var sceneConfigInfo))
			{
				return sceneConfigInfo;
			}

			throw new FileNotFoundException($"WorldConfig not find {id} Id");
		}

		/// <summary>
		/// 根据Id获取SceneConfig
		/// </summary>
		/// <param name="id"></param>
		/// <param name="config"></param>
		/// <returns></returns>
		public bool TryGet(uint id, out SceneConfig config)
		{
			return _configs.TryGetValue(id, out config);
		}
		
		/// <summary>
		/// 获得SceneConfig
		/// </summary>
		/// <param name="serverConfigId"></param>
		/// <returns></returns>
		public List<SceneConfig> GetByProcess(uint serverConfigId)
		{
			return _sceneConfigByProcess.TryGetValue(serverConfigId, out var list) ? list : new List<SceneConfig>();
		}

		/// <summary>
		/// 获得SceneConfig
		/// </summary>
		/// <param name="sceneType"></param>
		/// <returns></returns>
		public List<SceneConfig> GetSceneBySceneType(int sceneType)
		{
			return !_sceneConfigBySceneType.TryGetValue(sceneType, out var list) ? new List<SceneConfig>() : list;
		}

		/// <summary>
		/// 获得SceneConfig
		/// </summary>
		/// <param name="world"></param>
		/// <param name="sceneType"></param>
		/// <returns></returns>
		public List<SceneConfig> GetSceneBySceneType(int world, int sceneType)
		{
			if (!_worldSceneTypes.TryGetValue(world, out var sceneConfigDic))
			{
				return new List<SceneConfig>();
			}

			if (!sceneConfigDic.TryGetValue(sceneType, out var list))
			{
				return new List<SceneConfig>();
			}

			return list;
		}
	}

	/// <summary>
    /// 表示一个Scene配置信息
    /// </summary>
    public sealed class SceneConfig
    {
	    /// <summary>
	    /// ID
	    /// </summary>
	    public uint Id { get; set; }
	    /// <summary>
	    /// 进程Id
	    /// </summary>
	    public uint ProcessConfigId { get; set; }
	    /// <summary>
	    /// 世界Id
	    /// </summary>
	    public uint WorldConfigId { get; set; }
	    /// <summary>
	    /// Scene运行类型
	    /// </summary>
	    public string SceneRuntimeMode { get; set; }
	    /// <summary>
	    /// Scene类型
	    /// </summary>
	    public string SceneTypeString { get; set; }
	    /// <summary>
	    /// 协议类型
	    /// </summary>
	    public string NetworkProtocol { get; set; }
	    /// <summary>
	    /// 外网端口
	    /// </summary>
	    public int OuterPort { get; set; }
	    /// <summary>
	    /// 内网端口
	    /// </summary>
	    public int InnerPort { get; set; }
	    /// <summary>
	    /// Scene类型
	    /// </summary>
	    public int SceneType { get; set; }
	    /// <summary>
	    /// Address
	    /// </summary>
	    [JsonIgnore] 
	    [IgnoreDataMember]
	    public long Address { get; private set; }
		/// <summary>
		/// 初始化方法
		/// </summary>
	    public void Initialize()
	    {
		    Address = IdFactoryHelper.RuntimeId(false, 0, Id, (byte)WorldConfigId, 0);
	    }
    }
}
#endif