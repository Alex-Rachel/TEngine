#if FANTASY_NET
// ReSharper disable InconsistentNaming
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using Fantasy.Helper;
using Newtonsoft.Json;
#pragma warning disable CS8601 // Possible null reference assignment.
#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
namespace Fantasy.Platform.Net
{
    /// <summary>
    /// 用于记录服务器物理信息
    /// </summary>
    public sealed class MachineConfigData
    {
        /// <summary>
        /// 存放所有MachineConfigInfo信息
        /// </summary>
        public List<MachineConfig> List;
        [JsonIgnore] 
        [IgnoreDataMember]
        private readonly ConcurrentDictionary<uint, MachineConfig> _configs = new ConcurrentDictionary<uint, MachineConfig>();
        /// <summary>
        /// 获得MachineConfig的实例
        /// </summary>
        public static MachineConfigData Instance { get; private set; }
        /// <summary>
        /// 初始化MachineConfig
        /// </summary>
        /// <param name="machineConfigJson"></param>
        public static void InitializeFromJson(string machineConfigJson)
        {
            try
            {
                Instance = machineConfigJson.Deserialize<MachineConfigData>();
                foreach (var config in Instance.List)
                {
                    Instance._configs.TryAdd(config.Id, config);
                }
            }
            catch (Exception e)
            {
                throw new InvalidOperationException($"MachineConfigData.Json format error {e.Message}");
            }
        }
        /// <summary>
        /// 初始化MachineConfig
        /// </summary>
        /// <param name="list"></param>
        public static void Initialize(List<MachineConfig> list)
        {
            Instance = new MachineConfigData
            {
                List = list
            };
            foreach (var config in Instance.List)
            {
                Instance._configs.TryAdd(config.Id, config);
            }
        }
        /// <summary>
        /// 根据Id获取MachineConfig
        /// </summary>
        /// <param name="id"></param>
        /// <returns></returns>
        /// <exception cref="FileNotFoundException"></exception>
        public MachineConfig Get(uint id)
        {
            if (_configs.TryGetValue(id, out var machineConfigInfo))
            {
                return machineConfigInfo;
            }
            
            throw new FileNotFoundException($"MachineConfig not find {id} Id");
        }
        /// <summary>
        /// 根据Id获取MachineConfig
        /// </summary>
        /// <param name="id"></param>
        /// <param name="config"></param>
        /// <returns></returns>
        public bool TryGet(uint id, out MachineConfig config)
        {
            return _configs.TryGetValue(id, out config);
        }
    }
    /// <summary>
    /// 表示一个物理服务器的信息
    /// </summary>
    public sealed class MachineConfig
    {
        /// <summary>
        /// Id
        /// </summary>
        public uint Id { get; set; }
        /// <summary>
        /// 外网IP
        /// </summary>
        public string OuterIP { get; set; } 
        /// <summary>
        /// 外网绑定IP
        /// </summary>
        public string OuterBindIP { get; set; } 
        /// <summary>
        /// 内网绑定IP
        /// </summary>
        public string InnerBindIP { get; set; }  
    }
}
#endif