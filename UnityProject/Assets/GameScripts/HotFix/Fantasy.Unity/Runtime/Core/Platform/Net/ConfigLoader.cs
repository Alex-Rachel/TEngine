#if FANTASY_NET
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using Fantasy.Async;
using Fantasy.Helper;
using Fantasy.IdFactory;
using Fantasy.Network;
using Fantasy.Platform.Net;
// ReSharper disable ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract

namespace Fantasy;

/// <summary>
/// Fantasy框架XML配置文件加载器
/// </summary>
public static class ConfigLoader
{
    /// <summary>
    /// 从XML配置文件初始化所有配置
    /// </summary>
    /// <param name="configPath"></param>
    /// <exception cref="InvalidOperationException"></exception>
    public static async FTask InitializeFromXml(string configPath)
    {
        var configText = await FileHelper.GetTextByRelativePath(configPath);
        var doc = new XmlDocument();
        doc.LoadXml(configText);

        var root = doc.DocumentElement;
        if (root?.LocalName != "fantasy")
        {
            throw new InvalidOperationException("Invalid Fantasy config file format");
        }
        
        // 创建命名空间管理器
        var nsManager = new XmlNamespaceManager(doc.NameTable);
        nsManager.AddNamespace("f", "http://fantasy.net/config");
        // 加载运行时配置
        LoadRuntimeConfig(root, nsManager);
        
        var configTableNode = root.SelectSingleNode("f:configTable", nsManager);
        if (configTableNode != null)
        {
            var configTablePath = configTableNode.Attributes?["path"]?.Value;
            if (string.IsNullOrEmpty(configTablePath))
            {
                throw new InvalidOperationException("The configTable in the Fantasy configuration file lacks path configuration");
            }
            await LoadJsonConfig(configTablePath);
            return;
        }

        var serverNode = root.SelectSingleNode("f:server", nsManager);
        if (serverNode == null)
        {
            throw new InvalidOperationException("Missing server configuration in Fantasy config file");
        }
        
        // 加载框架需要的四个配置文件
        LoadMachineConfig(serverNode, nsManager);
        LoadProcessConfig(serverNode, nsManager);
        LoadWorldConfig(serverNode, nsManager);
        LoadSceneConfig(serverNode, nsManager);
        // 验证所有配置的完整性和正确性
        CheckConfig();
    }

    #region LoadJsonConfig

    private static async FTask LoadJsonConfig(string configTablePath)
    {
        var machineConfigFullPath = FileHelper.GetFullPath($"{configTablePath}/MachineConfigData.Json");
        
        if (!File.Exists(machineConfigFullPath))
        {
            throw new InvalidOperationException($"MachineConfigData.Json not found in the {machineConfigFullPath} directory");
        }
        
        var processConfigFullPath = FileHelper.GetFullPath($"{configTablePath}/ProcessConfigData.Json");
        
        if (!File.Exists(processConfigFullPath))
        {
            throw new InvalidOperationException($"ProcessConfigData.Json not found in the {processConfigFullPath} directory");
        }

        var worldConfigFullPath = FileHelper.GetFullPath($"{configTablePath}/WorldConfigData.Json");
        
        if (!File.Exists(worldConfigFullPath))
        {
            throw new InvalidOperationException($"WorldConfigData.Json not found in the {worldConfigFullPath} directory");
        }
        
        var sceneConfigFullPath = FileHelper.GetFullPath($"{configTablePath}/SceneConfigData.Json");
        
        if (!File.Exists(sceneConfigFullPath))
        {
            throw new InvalidOperationException($"SceneConfigData.Json not found in the {sceneConfigFullPath} directory");
        }

        MachineConfigData.InitializeFromJson(await File.ReadAllTextAsync(machineConfigFullPath, Encoding.UTF8));
        ProcessConfigData.InitializeFromJson(await File.ReadAllTextAsync(processConfigFullPath, Encoding.UTF8));
        WorldConfigData.InitializeFromJson(await File.ReadAllTextAsync(worldConfigFullPath, Encoding.UTF8));
        SceneConfigData.InitializeFromJson(await File.ReadAllTextAsync(sceneConfigFullPath, Encoding.UTF8));
        
        // 验证所有配置的完整性和正确性
        CheckConfig();
    }

    #endregion

    #region LoadXMLConfig

    private static void LoadMachineConfig(XmlNode serverNode, XmlNamespaceManager nsManager)
    {
        var machinesNode = serverNode.SelectSingleNode("f:machines", nsManager);
        if (machinesNode == null)
        {
            throw new InvalidOperationException("Missing machines configuration - at least one machine must be configured");
        }
        
        var machineNodes = machinesNode.SelectNodes("f:machine", nsManager);
        if (machineNodes == null || machineNodes.Count == 0)
        {
            throw new InvalidOperationException("No machine configurations found - at least one machine must be configured");
        }
        
        var machineList = new List<MachineConfig>();
        foreach (XmlNode machineNode in machineNodes)
        {
            var machine = new MachineConfig
            {
                Id = uint.Parse(GetRequiredAttribute(machineNode, "id")),
                OuterIP = GetRequiredAttribute(machineNode, "outerIP"),
                OuterBindIP = GetRequiredAttribute(machineNode, "outerBindIP"),
                InnerBindIP = GetRequiredAttribute(machineNode, "innerBindIP")
            };
            machineList.Add(machine);
        }
        
        MachineConfigData.Initialize(machineList);
    }

    private static void LoadProcessConfig(XmlNode serverNode, XmlNamespaceManager nsManager)
    {
        var processesNode = serverNode.SelectSingleNode("f:processes", nsManager);
        if (processesNode == null)
        {
            throw new InvalidOperationException("Missing processes configuration - at least one process must be configured");
        }

        var processNodes = processesNode.SelectNodes("f:process", nsManager);
        if (processNodes == null || processNodes.Count == 0)
        {
            throw new InvalidOperationException("No process configurations found - at least one process must be configured");
        }

        var processList = new List<ProcessConfig>();
        foreach (XmlNode processNode in processNodes)
        {
            var process = new ProcessConfig
            {
                Id = uint.Parse(GetRequiredAttribute(processNode, "id")),
                MachineId = uint.Parse(GetRequiredAttribute(processNode, "machineId")),
                StartupGroup = uint.Parse(GetRequiredAttribute(processNode, "startupGroup"))
            };
            processList.Add(process);
        }

        ProcessConfigData.Initialize(processList);
    }

    private static void LoadWorldConfig(XmlNode serverNode, XmlNamespaceManager nsManager)
    {
        var worldsNode = serverNode.SelectSingleNode("f:worlds", nsManager);
        if (worldsNode == null)
        {
            throw new InvalidOperationException("Missing worlds configuration - at least one world must be configured");
        }
        
        var worldNodes = worldsNode.SelectNodes("f:world", nsManager);
        if (worldNodes == null || worldNodes.Count == 0)
        {
            throw new InvalidOperationException("No world configurations found - at least one world must be configured");
        }
        
        var worldList = new List<WorldConfig>();

        // 解析world和所有database
        foreach (XmlNode worldNode in worldNodes)
        {
            var id = uint.Parse(GetRequiredAttribute(worldNode, "id"));
            var worldName = GetRequiredAttribute(worldNode, "worldName");
            var databaseNodes = GetChildNodes(worldNode, "f:database", nsManager);
            
            var worldConfig = new WorldConfig
            {
                Id = id,
                WorldName = worldName
            };

            var index = 0;
            worldConfig.DatabaseConfig = new DatabaseConfig[databaseNodes.Count];
            foreach (XmlNode dbNode in databaseNodes)
            {
                var dbConnection = GetOptionalAttribute(dbNode, "dbConnection");
                var dbType = GetRequiredAttribute(dbNode, "dbType");
                var dbName = GetRequiredAttribute(dbNode, "dbName");

                if (string.IsNullOrWhiteSpace(dbConnection))
                {
                    // Log.Warning($"(Fantasy.config) \"DbConnection\" is empty, thus the database-config \"{dbName}({dbType})\" in {worldName} (World Id: {id}) will be ignoured.");
                    dbConnection = string.Empty; 
                }
                
                worldConfig.DatabaseConfig[index++] = new DatabaseConfig(dbConnection, dbName, dbType);
            }
            
            worldList.Add(worldConfig);
        }

        WorldConfigData.Initialize(worldList);
    }

    private static void LoadSceneConfig(XmlNode serverNode, XmlNamespaceManager nsManager)
    {
        var scenesNode = serverNode.SelectSingleNode("f:scenes", nsManager);
        if (scenesNode == null)
        {
            throw new InvalidOperationException("Missing scenes configuration - at least one scene must be configured");
        }
        
        var sceneNodes = scenesNode.SelectNodes("f:scene", nsManager);
        if (sceneNodes == null || sceneNodes.Count == 0)
        {
            throw new InvalidOperationException("No scene configurations found - at least one scene must be configured");
        }
        
        var sceneList = new List<SceneConfig>();
        foreach (XmlNode sceneNode in sceneNodes)
        {
            var scene = new SceneConfig
            {
                Id = uint.Parse(GetRequiredAttribute(sceneNode, "id")),
                ProcessConfigId = uint.Parse(GetRequiredAttribute(sceneNode,  "processConfigId")),
                WorldConfigId = uint.Parse(GetRequiredAttribute(sceneNode, "worldConfigId")),
                SceneRuntimeMode = GetRequiredAttribute(sceneNode, "sceneRuntimeMode"),
                SceneTypeString = GetRequiredAttribute(sceneNode, "sceneTypeString"),
                NetworkProtocol = GetOptionalAttribute(sceneNode, "networkProtocol") ?? string.Empty,
                OuterPort = int.Parse(GetOptionalAttribute(sceneNode, "outerPort") ?? "0"),
                InnerPort = int.Parse(GetRequiredAttribute(sceneNode, "innerPort"))
            };
            scene.SceneType = Scene.SceneTypeDictionary[scene.SceneTypeString];
            sceneList.Add(scene);
        }
        
        SceneConfigData.Initialize(sceneList);
    }

    /// <summary>
    /// 获取必填的属性
    /// </summary>
    private static string GetRequiredAttribute(XmlNode node, string attributeName)
    {
        return node.Attributes?[attributeName]?.Value ?? throw new InvalidOperationException($"Required attribute '{attributeName}' is missing or null");
    }

    /// <summary>
    /// 获取可选的属性
    /// </summary>
    private static string? GetOptionalAttribute(XmlNode? node, string attributeName)
    {
        return node?.Attributes?[attributeName]?.Value;
    }

    /// <summary>
    /// 获取 XMl 父节点下所有指定名称的子节点
    /// </summary>
    private static XmlNodeList GetChildNodes(XmlNode parentNode, string childNodeName, XmlNamespaceManager nsManager)
    {
        return parentNode.SelectNodes(childNodeName, nsManager) ?? throw new InvalidOperationException($"No child nodes named '{childNodeName}' found under parent node");
    }

    #endregion

    #region CheckConfig

    private static void CheckConfig()
    {
        // 检查Machine配置
        CheckMachineConfig();
        // 检查Process配置
        CheckProcessConfig();
        // 检查World配置
        CheckWorldConfig();
        // 检查Scene配置
        CheckSceneConfig();
        // 检查引用关系
        CheckConfigReferences();
        // 检查端口冲突
        CheckPortConflicts();
    }
    
    private static void CheckMachineConfig()
    {
        var machines = MachineConfigData.Instance.List;
        var machineIds = new HashSet<uint>();
        
        foreach (var machine in machines)
        {
            // 检查ID重复
            if (!machineIds.Add(machine.Id))
            {
                throw new InvalidOperationException($"Duplicate machine ID: {machine.Id}");
            }
            
            // 检查必填字段
            if (string.IsNullOrWhiteSpace(machine.OuterIP))
            {
                throw new InvalidOperationException($"Machine {machine.Id}: OuterIP cannot be null or empty");
            }
            
            if (string.IsNullOrWhiteSpace(machine.OuterBindIP))
            {
                throw new InvalidOperationException($"Machine {machine.Id}: OuterBindIP cannot be null or empty");
            }
            
            if (string.IsNullOrWhiteSpace(machine.InnerBindIP))
            {
                throw new InvalidOperationException($"Machine {machine.Id}: InnerBindIP cannot be null or empty");
            }
        }
    }
    
    private static void CheckProcessConfig()
    {
        var processes = ProcessConfigData.Instance.List;
        var processIds = new HashSet<uint>();
        
        foreach (var process in processes)
        {
            // 检查ID重复
            if (!processIds.Add(process.Id))
            {
                throw new InvalidOperationException($"Duplicate process ID: {process.Id}");
            }
            
            // 检查必填字段
            if (process.Id == 0)
            {
                throw new InvalidOperationException("Process ID cannot be 0");
            }
            
            if (process.MachineId == 0)
            {
                throw new InvalidOperationException($"Process {process.Id}: MachineId cannot be 0");
            }
        }
    }
    
    private static void CheckWorldConfig()
    {
        var worlds = WorldConfigData.Instance.List;
        var worldIds = new HashSet<uint>();
        var idFactoryType = IdFactoryHelper.GetIdFactoryType();

        foreach (var world in worlds)
        {
            // 检查ID重复
            if (!worldIds.Add(world.Id))
            {
                throw new InvalidOperationException($"Duplicate world ID: {world.Id}");
            }

            // 检查必填字段
            if (string.IsNullOrWhiteSpace(world.WorldName))
            {
                throw new InvalidOperationException($"World {world.Id}: WorldName cannot be null or empty");
            }

            // 检查IdFactoryType为World时，world.Id最大值为255
            if (idFactoryType == IdFactoryType.World && world.Id > 255)
            {
                throw new InvalidOperationException($"World {world.Id}: When IdFactoryType is World, World ID must be <= 255");
            }

            if (world.DatabaseConfig != null)
            {
                var dbNames = new HashSet<string>();
                foreach (var databaseConfig in world.DatabaseConfig)
                {
                    if (databaseConfig.DbName == null)
                    {
                        throw new InvalidOperationException($"World {world.Id}: DbName config has no value");
                    }

                    if (databaseConfig.DbType == null)
                    {
                        throw new InvalidOperationException($"World {world.Id}: DbType config has no value");
                    }

                    if (!dbNames.Add(databaseConfig.DbName))
                    {
                        throw new InvalidOperationException($"World {world.Id} configuration DbName:{databaseConfig.DbName} repeat");
                    }
                }
            }
        }
    }
    
    private static void CheckSceneConfig()
    {
        var scenes = SceneConfigData.Instance.List;
        var sceneIds = new HashSet<uint>();
        
        foreach (var scene in scenes)
        {
            // 检查ID重复
            if (!sceneIds.Add(scene.Id))
            {
                throw new InvalidOperationException($"Duplicate scene ID: {scene.Id}");
            }
            
            // 检查必填字段
            if (scene.ProcessConfigId == 0)
            {
                throw new InvalidOperationException($"Scene {scene.Id}: ProcessConfigId cannot be 0");
            }
            
            if (scene.WorldConfigId == 0)
            {
                throw new InvalidOperationException($"Scene {scene.Id}: WorldConfigId cannot be 0");
            }
            
            if (string.IsNullOrWhiteSpace(scene.SceneRuntimeMode))
            {
                throw new InvalidOperationException($"Scene {scene.Id}: SceneRuntimeMode cannot be null or empty");
            }
            
            if (string.IsNullOrWhiteSpace(scene.SceneTypeString))
            {
                throw new InvalidOperationException($"Scene {scene.Id}: SceneTypeString cannot be null or empty");
            }
            
            if (scene.InnerPort == 0)
            {
                throw new InvalidOperationException($"Scene {scene.Id}: InnerPort cannot be 0");
            }
            
            if (scene.SceneType == 0)
            {
                throw new InvalidOperationException($"Scene {scene.Id}: SceneType cannot be 0");
            }
            
            // 检查NetworkProtocol配置
            if (scene.OuterPort > 0 && string.IsNullOrWhiteSpace(scene.NetworkProtocol))
            {
                throw new InvalidOperationException($"Scene {scene.Id}: NetworkProtocol must be set when OuterPort > 0");
            }
            
            // 检查Scene ID范围（仅在IdFactoryType.World模式下）
            CheckSceneIdRange(scene);
        }
    }
    
    private static void CheckConfigReferences()
    {
        // 检查Process的MachineId引用
        foreach (var process in ProcessConfigData.Instance.List)
        {
            if (MachineConfigData.Instance.List.All(m => m.Id != process.MachineId))
            {
                throw new InvalidOperationException($"Process {process.Id}: Referenced MachineId {process.MachineId} not found");
            }
        }
        
        // 检查Scene的ProcessConfigId和WorldConfigId引用
        foreach (var scene in SceneConfigData.Instance.List)
        {
            if (ProcessConfigData.Instance.List.All(p => p.Id != scene.ProcessConfigId))
            {
                throw new InvalidOperationException($"Scene {scene.Id}: Referenced ProcessConfigId {scene.ProcessConfigId} not found");
            }
            
            if (WorldConfigData.Instance.List.All(w => w.Id != scene.WorldConfigId))
            {
                throw new InvalidOperationException($"Scene {scene.Id}: Referenced WorldConfigId {scene.WorldConfigId} not found");
            }
        }
    }
    
    private static void CheckPortConflicts()
    {
        // 按机器分组检查端口冲突
        var machinePortMap = new Dictionary<uint, Dictionary<int, uint>>(); // MachineId -> (Port -> SceneId)
        
        foreach (var scene in SceneConfigData.Instance.List)
        {
            // 获取Scene所属的机器ID
            var process = ProcessConfigData.Instance.Get(scene.ProcessConfigId);
            var machineId = process.MachineId;
            
            if (!machinePortMap.ContainsKey(machineId))
            {
                machinePortMap[machineId] = new Dictionary<int, uint>();
            }
            
            var machinePorts = machinePortMap[machineId];
            
            // 检查同一Scene内InnerPort和OuterPort是否冲突
            if (scene.OuterPort > 0 && scene.InnerPort == scene.OuterPort)
            {
                throw new InvalidOperationException($"Scene {scene.Id}: InnerPort and OuterPort cannot use the same port {scene.InnerPort}");
            }
            
            // 检查InnerPort在同一机器下是否与其他端口冲突
            if (machinePorts.TryGetValue(scene.InnerPort, out var conflictSceneId1))
            {
                throw new InvalidOperationException($"Scene {scene.Id}: InnerPort {scene.InnerPort} conflicts with another port from Scene {conflictSceneId1} on Machine {machineId}");
            }
            machinePorts[scene.InnerPort] = scene.Id;
            
            // 检查OuterPort在同一机器下是否与其他端口冲突（如果不为0）
            if (scene.OuterPort > 0)
            {
                if (machinePorts.TryGetValue(scene.OuterPort, out var conflictSceneId))
                {
                    throw new InvalidOperationException($"Scene {scene.Id}: OuterPort {scene.OuterPort} conflicts with another port from Scene {conflictSceneId} on Machine {machineId}");
                }
                machinePorts[scene.OuterPort] = scene.Id;
            }
        }
    }
    
    private static void CheckSceneIdRange(SceneConfig scene)
    {
        // 检查当前是否为World模式的ID工厂
        if (IdFactoryHelper.GetIdFactoryType() != IdFactoryType.World)
        {
            return; // 非World模式不需要检查
        }
        
        var worldConfigId = scene.WorldConfigId;
        var minAllowedId = worldConfigId * 1000 + 1;
        var maxAllowedId = worldConfigId * 1000 + 255;
        
        if (scene.Id < minAllowedId)
        {
            throw new InvalidOperationException($"Scene {scene.Id}: ID must be >= {minAllowedId} (WorldConfigId {worldConfigId} * 1000 + 1)");
        }
        
        if (scene.Id > maxAllowedId)
        {
            throw new InvalidOperationException($"Scene {scene.Id}: ID must be <= {maxAllowedId} (WorldConfigId {worldConfigId} * 1000 + 255)");
        }
    }

    #endregion

    #region LoadRuntimeConfig

    private static void LoadRuntimeConfig(XmlNode root, XmlNamespaceManager nsManager)
    {
        // 加载IdFactory配置
        XmlNode? idFactoryNode = root.SelectSingleNode("f:idFactory", nsManager);
        string idFactoryType = GetOptionalAttribute(idFactoryNode, "type") ?? "Default";
        IdFactoryHelper.Initialize(Enum.Parse<IdFactoryType>(idFactoryType));
        // 加载网络配置
        XmlNode? networkNode = root.SelectSingleNode("f:network", nsManager);
        ProgramDefine.InnerNetwork = Enum.Parse<NetworkProtocolType>(GetOptionalAttribute(networkNode, "inner") ?? "TCP");
        ProgramDefine.MaxMessageSize = int.Parse(GetOptionalAttribute(networkNode, "maxMessageSize") ?? "1048560");
        // 加载会话配置
        XmlNode? sessionNode = root.SelectSingleNode("f:session", nsManager);
        ProgramDefine.SessionIdleCheckerTimeout = int.Parse(GetOptionalAttribute(sessionNode, "idleTimeout") ?? "8000");
        ProgramDefine.SessionIdleCheckerInterval = int.Parse(GetOptionalAttribute(sessionNode, "idleInterval") ?? "5000");
        
        Log.Info($"Current inner network protocol:{ProgramDefine.InnerNetwork.ToString()}");
        Log.Info($"Max Message Size(byte):{ProgramDefine.MaxMessageSize}");
        Log.Info($"Current session idle timeout:{ProgramDefine.SessionIdleCheckerTimeout}");
        Log.Info($"Session-check interval:{ProgramDefine.SessionIdleCheckerInterval} ");
    }

    #endregion
}
#endif
