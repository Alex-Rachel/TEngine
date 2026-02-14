#if FANTASY_NET
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using Fantasy.Helper;
using Newtonsoft.Json;
// ReSharper disable CollectionNeverUpdated.Global
#pragma warning disable CS8601 // Possible null reference assignment.
#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.

namespace Fantasy.Platform.Net
{
    /// <summary>
    ///  用于管理进程信息
    /// </summary>
    public sealed class ProcessConfigData
    {
	    /// <summary>
	    /// 存放所有ProcessConfig信息
	    /// </summary>
	    public List<ProcessConfig> List;
	    [JsonIgnore] 
	    [IgnoreDataMember]
	    private readonly ConcurrentDictionary<uint, ProcessConfig> _configs = new ConcurrentDictionary<uint, ProcessConfig>();
	    /// <summary>
	    /// 获得ProcessConfigData的实例
	    /// </summary>
	    public static ProcessConfigData Instance { get; private set; }
	    /// <summary>
	    /// 初始化ProcessConfig
	    /// </summary>
	    /// <param name="processConfigJson"></param>
	    public static void InitializeFromJson(string processConfigJson)
	    {
		    try
		    {
			    Instance = processConfigJson.Deserialize<ProcessConfigData>();
			    foreach (var config in Instance.List)
			    {
				    Instance._configs.TryAdd(config.Id, config);
			    }
		    }
		    catch (Exception e)
		    {
			    throw new InvalidOperationException($"ProcessConfigData.Json format error {e.Message}");
		    }
	    }
	    /// <summary>
	    /// 初始化ProcessConfig
	    /// </summary>
	    /// <param name="list"></param>
	    public static void Initialize(List<ProcessConfig> list)
	    {
		    Instance = new ProcessConfigData
		    {
			    List = list
		    };
		    foreach (var config in Instance.List)
		    {
			    Instance._configs.TryAdd(config.Id, config);
		    }
	    }
	    /// <summary>
	    /// 根据Id获取ProcessConfig
	    /// </summary>
	    /// <param name="id"></param>
	    /// <returns></returns>
	    /// <exception cref="FileNotFoundException"></exception>
	    public ProcessConfig Get(uint id)
	    {
		    if (_configs.TryGetValue(id, out var processConfigInfo))
		    {
			    return processConfigInfo;
		    }
            
		    throw new FileNotFoundException($"MachineConfig not find {id} Id");
	    }
	    /// <summary>
	    /// 根据Id获取ProcessConfig
	    /// </summary>
	    /// <param name="id"></param>
	    /// <param name="config"></param>
	    /// <returns></returns>
	    public bool TryGet(uint id, out ProcessConfig config)
	    {
		    return _configs.TryGetValue(id, out config);
	    }
	    /// <summary>
	    /// 按照startupGroup寻找属于startupGroup组的ProcessConfig
	    /// </summary>
	    /// <param name="startupGroup">startupGroup</param>
	    /// <returns></returns>
	    public IEnumerable<ProcessConfig> ForEachByStartupGroup(uint startupGroup)
	    {
		    foreach (var processConfig in List)
		    {
			    if (processConfig.StartupGroup == startupGroup)
			    {
				    yield return processConfig;
			    }
		    }
	    }
    }
    /// <summary>
    /// 表示一个进程配置信息
    /// </summary>
    public sealed class ProcessConfig
    {
	    /// <summary>
	    /// 进程Id
	    /// </summary>
		public uint Id { get; set; } 
	    /// <summary>
	    /// 机器ID
	    /// </summary>
		public uint MachineId { get; set; } 
	    /// <summary>
	    /// 启动组
	    /// </summary>
		public uint StartupGroup { get; set; }   
    }
}
#endif