#if UNITY_6000_3_OR_NEWER && !UNITY_6000_5_OR_NEWER && MPPM_AVAILABLE
// Play Mode Scenario commands for Unity 6.3 / 6.4 (MPPM 2.0.2).
//
// The 6.5+/6.7 implementation in PlayModeScenarioCommands.cs targets the MPPM 3.0 orchestrated
// scenario API (Unity.PlayMode.Editor.PlayModeScenario / PlayModeScenarioManager / OrchestratedScenario),
// which does not exist on 6.3. On 6.3 the same behaviour is delivered against the older MPPM 2.0.2 API:
//   * the scenario asset is ScenarioConfig : Unity.PlayMode.Editor.PlayModeConfiguration
//   * active-config + start/stop live on the PlayModeManager ScriptableSingleton
//   * run state lives on the ScenarioRunner ScriptableSingleton (ScenarioState enum)
//   * players are InstanceDescription subclasses keyed by Name (no GUID), in private List<> fields
//   * creation goes through PlayModeConfigurationUtils.CreatePlayModeConfig
// Every one of those types is `internal`, so this file is entirely reflection based.
//
// Command names, arguments and return shapes mirror PlayModeScenarioCommands.cs so the CLI surface is
// identical across Unity versions. Only one of the two files compiles for any given editor version.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Unity.Pipeline.Commands;
using Unity.Pipeline.Models;
using UnityEditor;
using UnityEngine;

namespace Unity.Pipeline.Editor.Commands
{
    static class PlayModeScenarioCommands
    {
        // Our MPPM scenario commands join the editor.playmode group, alongside the core
        // play/stop/pause controls. Each command's name is derived from its method name.
        const string Group = "editor.playmode";

        [CliGroupCommand(Group,
            "List all Play Mode Scenario assets in the project",
            Tags = new[] { "editor/playmode" })]
        public static object ListScenarios()
        {
            var scenarios = Mppm63.FindScenarioAssets()
                .Select(obj => new
                {
                    name = obj.name,
                    path = AssetDatabase.GetAssetPath(obj),
                    type = obj.GetType().Name
                })
                .ToList();

            return new { count = scenarios.Count, scenarios };
        }

        [CliGroupCommand(Group,
            "Set the active Play Mode Scenario by asset path (Edit Mode / Idle only)",
            Tags = new[] { "editor/playmode" })]
        public static object SetActiveScenario(
            [CliArg("path", "Asset path of the scenario, e.g. Assets/Settings/PlayMode/2Players.asset", Required = true)] string path)
        {
            if (string.IsNullOrEmpty(path))
                throw new ArgumentException("path is required.");

            var state = Mppm63.CurrentPlayModeState();
            if (state != "NotRunning")
                throw new InvalidOperationException($"Cannot change the active scenario while play mode state is '{state}'. Stop it first (editor.playmode stop_scenario).");

            var scenario = Mppm63.LoadScenarioAsset(path, out var loadError);
            if (scenario == null)
                throw new InvalidOperationException(loadError ?? $"No Play Mode Scenario asset at '{path}'.");

            Mppm63.SetActiveConfig(scenario);

            return new
            {
                active = new { name = scenario.name, path = AssetDatabase.GetAssetPath(scenario) },
                state = Mppm63.ScenarioState()
            };
        }

        [CliGroupCommand(Group,
            "Start the active Play Mode Scenario (enters Play Mode with its players)",
            Tags = new[] { "editor/playmode" })]
        public static object StartScenario()
        {
            var active = Mppm63.GetActiveScenarioAsset();
            if (active == null)
                throw new InvalidOperationException($"No scenario asset is active. {Mppm63.DefaultScenarioNote}");

            Mppm63.Start();

            return new
            {
                started = true,
                active = new { name = active.name, path = AssetDatabase.GetAssetPath(active) },
                state = Mppm63.ScenarioState(),
                message = "Scenario starting. Player editors launch asynchronously; poll editor.playmode get_scenario_status or editor.playmode list_instances."
            };
        }

        [CliGroupCommand(Group,
            "Stop the active/running Play Mode Scenario",
            Tags = new[] { "editor/playmode" })]
        public static object StopScenario()
        {
            Mppm63.Stop();

            return new
            {
                stopped = true,
                state = Mppm63.ScenarioState(),
                note = "State settles asynchronously; poll editor.playmode get_scenario_status for the final state."
            };
        }

        [CliGroupCommand(Group,
            "Live state of the active/running Play Mode Scenario: its state plus one entry per player the scenario declares, each with its run state and, once its Editor is up, the port and project path to reach it.",
            MainThreadRequired = true,
            Tags = new[] { "editor/playmode" })]
        public static object GetScenarioStatus()
        {
            var active = Mppm63.GetActiveScenarioAsset();
            var declared = Mppm63.DescribePlayers(active, out var playerError);
            var mppmPlayers = MppmReflection.ReadPlayers(out var mppmError);
            var live = InstanceCommands.LiveInstances();

            var players = (declared ?? new List<ScenarioPlayer>()).Select(p =>
            {
                var mppmPlayer = mppmPlayers.FirstOrDefault(
                    m => string.Equals(m.Name, p.Name, StringComparison.OrdinalIgnoreCase));
                var instance = FindInstance(live, p, mppmPlayer);

                return (object)new
                {
                    id = p.Id,
                    name = p.Name,
                    type = p.Type,
                    state = mppmPlayer?.State,
                    reachable = instance != null,
                    vpId = instance?.VpId,
                    port = instance?.Port,
                    endpoints = instance?.Endpoints,
                    projectPath = instance?.ProjectPath
                };
            }).ToList();

            return new
            {
                state = Mppm63.ScenarioState(),
                playMode = EditorApplication.isPaused ? "paused" : (EditorApplication.isPlaying ? "playing" : "stopped"),
                active = active == null ? null : new { name = active.name, path = AssetDatabase.GetAssetPath(active) },
                isDefaultScenario = active == null,
                players,
                playerError = playerError ?? mppmError,
                note = active == null ? Mppm63.DefaultScenarioNote : null
            };
        }

        internal static EditorInstanceDescriptor FindInstance(
            List<EditorInstanceDescriptor> live, ScenarioPlayer declared, MppmPlayer mppmPlayer)
        {
            if (declared.Type == MppmInstanceInfo.RoleMain)
                return live.FirstOrDefault(d => d.Role == MppmInstanceInfo.RoleMain);

            // MPPM records are joined to declared players by name alone, so a local player sharing a
            // clone's name can carry that clone's vpId. Only a declared clone may use it.
            if (declared.Type != MppmInstanceInfo.RoleClone)
                return null;

            if (!string.IsNullOrEmpty(mppmPlayer?.VpId))
            {
                return live.FirstOrDefault(
                    d => d.Role == MppmInstanceInfo.RoleClone
                        && string.Equals(d.VpId, mppmPlayer.VpId, StringComparison.OrdinalIgnoreCase));
            }

            return live.FirstOrDefault(
                d => d.Role == MppmInstanceInfo.RoleClone
                    && string.Equals(d.InstanceName, declared.Name, StringComparison.OrdinalIgnoreCase));
        }

        [CliGroupCommand(Group,
            "List the players a Play Mode Scenario asset declares (main, clones and local players), with the ids editor.playmode remove_player takes",
            Tags = new[] { "editor/playmode" })]
        public static object ListPlayers(
            [CliArg("path", "Scenario asset path (defaults to the active scenario).")] string path = null)
        {
            UnityEngine.Object scenario;
            if (path == null)
            {
                scenario = Mppm63.GetActiveScenarioAsset();
            }
            else
            {
                scenario = Mppm63.LoadScenarioAsset(path, out var loadError);
                if (scenario == null)
                    throw new InvalidOperationException(loadError ?? $"No Play Mode Scenario asset at '{path}'.");
            }

            var declared = Mppm63.DescribePlayers(scenario, out var playerError);

            return new
            {
                path = scenario == null ? null : AssetDatabase.GetAssetPath(scenario),
                count = declared?.Count ?? 0,
                players = declared?.Select(p => new
                {
                    id = p.Id,
                    name = p.Name,
                    type = p.Type,
                    controller = p.Controller,
                    runMode = p.RunMode
                }).ToList(),
                playerError,
                note = scenario == null ? Mppm63.DefaultScenarioNote : null
            };
        }

        [CliGroupCommand(Group,
            "Create a new Play Mode Scenario asset (auto-adds the Main Editor player). On 6.3 the asset is written under Assets/Settings/PlayMode/<name>.asset." + ScenarioCreateNotes.DescriptionSuffix,
            Tags = new[] { "editor/playmode" })]
        public static object CreateScenario(
            [CliArg("name", "Scenario name (also the asset file name).", Required = true)] string name,
            [CliArg("path", "Optional asset path; on 6.3 only the file name is used (the folder is fixed to Assets/Settings/PlayMode).")] string path = null)
        {
            // 6.3's PlayModeConfigurationUtils.CreatePlayModeConfig fixes the folder, so a supplied
            // path only contributes the file name. Mirror the 6.7 command surface but honour that.
            var effectiveName = name;
            if (string.IsNullOrWhiteSpace(effectiveName) && !string.IsNullOrEmpty(path))
                effectiveName = System.IO.Path.GetFileNameWithoutExtension(path);

            if (string.IsNullOrWhiteSpace(effectiveName))
                throw new ArgumentException("name is required.");

            // On 6.3 the name is used verbatim as the asset file name, so validate it here rather than
            // letting CreatePlayModeConfig throw an opaque InvalidOperationException on a path separator.
            if (effectiveName.IndexOfAny(System.IO.Path.GetInvalidFileNameChars()) >= 0 || effectiveName.IndexOf('\\') >= 0)
                throw new ArgumentException($"Scenario name '{effectiveName}' contains characters that are not valid in a file name (for example '/' or '\\'). It is written under Assets/Settings/PlayMode/<name>.asset, so pick a plain file name.");

            // The folder is fixed, so refuse rather than silently overwriting an existing scenario there.
            var targetPath = $"Assets/Settings/PlayMode/{effectiveName}.asset";
            if (AssetDatabase.LoadMainAssetAtPath(targetPath) != null || System.IO.File.Exists(targetPath))
                throw new InvalidOperationException($"A scenario already exists at '{targetPath}'. Choose a different name or delete the existing asset first; create refuses rather than overwriting it.");

            var created = Mppm63.CreateScenario(effectiveName, out var error);
            if (created == null)
                throw new InvalidOperationException(error);

            var createdPath = AssetDatabase.GetAssetPath(created);
            var active = Mppm63.GetActiveScenarioAsset() == created;
            return new
            {
                created = true,
                name = created.name,
                path = createdPath,
                active,
                note = active ? null : ScenarioCreateNotes.NotActive(createdPath)
            };
        }

        [CliGroupCommand(Group,
            "Add a player to a scenario asset. type: clone | local",
            Tags = new[] { "editor/playmode" })]
        public static object AddPlayer(
            [CliArg("path", "Scenario asset path (defaults to the active scenario).")] string path = null,
            [CliArg("type", "Player type: 'clone' (virtual player) or 'local' (built player).", Required = true)] string type = null,
            [CliArg("name", "Name for the new player (optional).")] string name = null)
        {
            var runState = Mppm63.CurrentPlayModeState();
            if (runState != "NotRunning")
                throw new InvalidOperationException($"Cannot add a player while play mode state is '{runState}'. Stop it first (editor.playmode stop_scenario).");

            var scenario = Mppm63.ResolveScenario(path, out var resolveError);
            if (scenario == null)
                throw new InvalidOperationException(resolveError);

            var resolvedPath = AssetDatabase.GetAssetPath(scenario);
            var id = Mppm63.AddInstance(scenario, type, name, out var error);
            if (id == null)
                throw new InvalidOperationException(error);

            return new { added = true, id, type, path = resolvedPath };
        }

        [CliGroupCommand(Group,
            "Remove a scenario player by its id (on 6.3 the id is the player Name). Destructive: requires confirm=true (use dry-run to preview).",
            Tags = new[] { "editor/playmode" })]
        public static object RemovePlayer(
            [CliArg("id", "Player id to remove (the player Name on 6.3).", Required = true)] string id,
            [CliArg("path", "Scenario asset path (defaults to the active scenario).")] string path = null,
            [CliArg("confirm", "Must be true to actually remove the player. Without it the command refuses (destructive guard).")] bool confirm = false,
            [CliArg("dry-run", "If true, report the player that would be removed without changing the scenario.")] bool dryRun = false)
        {
            var runState = Mppm63.CurrentPlayModeState();
            if (runState != "NotRunning")
                throw new InvalidOperationException($"Cannot remove a player while play mode state is '{runState}'. Stop it first (editor.playmode stop_scenario).");

            if (string.Equals(id, MppmInstanceInfo.MainInstanceName, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"'{MppmInstanceInfo.MainInstanceName}' is the scenario's main Editor and cannot be removed; only clone and local players can. It is listed by editor.playmode list_players for reference but is not a removable player.");

            var scenario = Mppm63.ResolveScenario(path, out var resolveError);
            if (scenario == null)
                throw new InvalidOperationException(resolveError);

            var scenarioPath = AssetDatabase.GetAssetPath(scenario);
            if (dryRun)
            {
                var player = Mppm63.DescribePlayers(scenario, out var describeError)?
                    .FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));
                if (player == null)
                    throw new InvalidOperationException(describeError ?? $"No player with id '{id}' in '{scenarioPath}'. List them with editor.playmode list_players.");

                return new { status = "dry_run", wouldRemove = new { id = player.Id, name = player.Name, type = player.Type }, path = scenarioPath };
            }

            if (!confirm)
                throw new ArgumentException($"Refusing to remove player '{id}' from '{scenarioPath}'. Pass confirm=true to remove it (rewrites the scenario asset, not undoable via Unity's Undo), or dry-run=true to preview.");

            if (!Mppm63.RemoveInstance(scenario, id, out var error))
                throw new InvalidOperationException(error);

            return new { removed = true, id, path = scenarioPath };
        }
    }

    /// <summary>
    /// Reflection facade over the MPPM 2.0.2 (Unity 6.3/6.4) scenario API. All target types are
    /// internal to UnityEditor.MultiplayerModule / UnityEditor.PlayModeModule, so every access here
    /// uses NonPublic binding flags and walks the type hierarchy for inherited non-public members.
    /// </summary>
    internal static class Mppm63
    {
        const string ScenarioConfigTypeName = "Unity.Multiplayer.PlayMode.Editor.ScenarioConfig";
        const string PlayModeConfigurationTypeName = "Unity.PlayMode.Editor.PlayModeConfiguration";
        const string PlayModeManagerTypeName = "Unity.PlayMode.Editor.PlayModeManager";
        const string PlayModeConfigUtilsTypeName = "Unity.PlayMode.Editor.PlayModeConfigurationUtils";
        const string ScenarioRunnerTypeName = "Unity.Multiplayer.PlayMode.Editor.ScenarioRunner";
        const string VirtualEditorDescTypeName = "Unity.Multiplayer.PlayMode.Editor.VirtualEditorInstanceDescription";
        const string LocalDescTypeName = "Unity.Multiplayer.PlayMode.Editor.LocalInstanceDescription";

        const BindingFlags InstanceFlags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        const BindingFlags StaticFlags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;

        public const string DefaultScenarioNote =
            "No Play Mode Scenario asset is assigned to PlayModeManager. Create one with editor.playmode create_scenario " +
            "and assign it with editor.playmode set_active_scenario before editor.playmode start_scenario, or starting will just enter ordinary Play Mode.";

        static Type s_ScenarioConfig;
        static Type s_PlayModeConfiguration;
        static Type s_PlayModeManager;
        static Type s_ConfigUtils;
        static Type s_ScenarioRunner;
        static Type s_VirtualDesc;
        static Type s_LocalDesc;

        static Type ScenarioConfigType => Require(ref s_ScenarioConfig, ScenarioConfigTypeName);
        static Type PlayModeConfigurationType => Require(ref s_PlayModeConfiguration, PlayModeConfigurationTypeName);
        static Type PlayModeManagerType => Require(ref s_PlayModeManager, PlayModeManagerTypeName);
        static Type ConfigUtilsType => Require(ref s_ConfigUtils, PlayModeConfigUtilsTypeName);

        // ---- public surface used by the commands -------------------------------------------------

        public static List<UnityEngine.Object> FindScenarioAssets()
        {
            var result = new List<UnityEngine.Object>();
            var scenarioType = ScenarioConfigType;
            var baseType = PlayModeConfigurationType;

            foreach (var guid in AssetDatabase.FindAssets("t:" + PlayModeConfigurationTypeName))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var obj = AssetDatabase.LoadAssetAtPath(path, baseType);
                if (obj != null && scenarioType.IsInstanceOfType(obj))
                    result.Add(obj);
            }

            return result;
        }

        public static UnityEngine.Object LoadScenarioAsset(string path, out string error)
        {
            error = null;
            var obj = AssetDatabase.LoadAssetAtPath(path, PlayModeConfigurationType);
            if (obj != null && ScenarioConfigType.IsInstanceOfType(obj))
                return obj;

            error = $"'{path}' is not a Play Mode Scenario asset (or does not exist). List the ones that are with editor.playmode list_scenarios.";
            return null;
        }

        public static UnityEngine.Object GetActiveConfig()
        {
            var pm = PlayModeManagerInstance();
            var prop = PropDeep(pm.GetType(), "ActivePlayModeConfig", InstanceFlags);
            return prop?.GetValue(pm) as UnityEngine.Object;
        }

        public static UnityEngine.Object GetActiveScenarioAsset()
        {
            var config = GetActiveConfig();
            return config != null && ScenarioConfigType.IsInstanceOfType(config) ? config : null;
        }

        public static void SetActiveConfig(UnityEngine.Object config)
        {
            var pm = PlayModeManagerInstance();
            var prop = PropDeep(pm.GetType(), "ActivePlayModeConfig", InstanceFlags);
            if (prop == null || !prop.CanWrite)
                throw new InvalidOperationException("PlayModeManager.ActivePlayModeConfig setter not found.");

            try
            {
                prop.SetValue(pm, config);
            }
            catch (TargetInvocationException tie)
            {
                throw new InvalidOperationException(tie.InnerException?.Message ?? tie.Message);
            }
        }

        public static string CurrentPlayModeState()
        {
            var pm = PlayModeManagerInstance();
            var prop = PropDeep(pm.GetType(), "CurrentState", InstanceFlags);
            return prop?.GetValue(pm)?.ToString() ?? "unknown";
        }

        public static string ScenarioState()
        {
            // Prefer the fine-grained ScenarioRunner state (Idle/Running/Completed/Aborted/Failed);
            // fall back to the coarse PlayModeManager state if the runner API is unavailable.
            try
            {
                var runner = Require(ref s_ScenarioRunner, ScenarioRunnerTypeName);
                var getStatus = MethodDeep(runner, "GetScenarioStatus", StaticFlags, Type.EmptyTypes);
                var status = getStatus?.Invoke(null, null);
                if (status != null)
                {
                    var statusType = status.GetType();
                    var stateProp = PropDeep(statusType, "State", InstanceFlags);
                    if (stateProp != null)
                        return stateProp.GetValue(status)?.ToString() ?? "unknown";

                    var stateField = FieldDeep(statusType, "State", InstanceFlags);
                    if (stateField != null)
                        return stateField.GetValue(status)?.ToString() ?? "unknown";
                }
            }
            catch
            {
                // fall through to the coarse state
            }

            return CurrentPlayModeState();
        }

        public static void Start() => InvokeManager("Start");

        public static void Stop() => InvokeManager("Stop");

        public static UnityEngine.Object CreateScenario(string name, out string error)
        {
            error = null;
            var create = MethodDeep(ConfigUtilsType, "CreatePlayModeConfig", StaticFlags,
                new[] { typeof(string), typeof(Type) });
            if (create == null)
            {
                error = "PlayModeConfigurationUtils.CreatePlayModeConfig(string, Type) not found.";
                return null;
            }

            try
            {
                return create.Invoke(null, new object[] { name, ScenarioConfigType }) as UnityEngine.Object;
            }
            catch (TargetInvocationException tie)
            {
                error = tie.InnerException?.Message ?? tie.Message;
                return null;
            }
        }

        public static List<ScenarioPlayer> DescribePlayers(UnityEngine.Object scenario, out string error)
        {
            error = null;
            var players = new List<ScenarioPlayer>();
            if (scenario == null)
                return players;

            var getAll = MethodDeep(ScenarioConfigType, "GetAllInstances", InstanceFlags, Type.EmptyTypes);
            if (getAll == null)
            {
                error = "ScenarioConfig.GetAllInstances() not found.";
                return null;
            }

            if (!(getAll.Invoke(scenario, null) is IEnumerable items))
                return players;

            foreach (var item in items)
            {
                if (item == null)
                    continue;

                var name = ReadName(item);
                players.Add(new ScenarioPlayer
                {
                    Id = name,        // 6.3 identifies instances by Name (no GUID)
                    Name = name,
                    Type = TypeKey(item),
                    Controller = item.GetType().Name,
                    RunMode = PropDeep(item.GetType(), "RunModeState", InstanceFlags)?.GetValue(item)?.ToString()
                });
            }

            return players;
        }

        public static UnityEngine.Object ResolveScenario(string path, out string error)
        {
            if (!string.IsNullOrEmpty(path))
                return LoadScenarioAsset(path, out error);

            var active = GetActiveScenarioAsset();
            if (active == null)
            {
                error = $"No 'path' given and no scenario asset is active. {DefaultScenarioNote}";
                return null;
            }

            error = null;
            return active;
        }

        public static string AddInstance(UnityEngine.Object scenario, string typeKey, string name, out string error)
        {
            error = null;

            string listField;
            Type descType;
            switch ((typeKey ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "clone":
                case "virtual":
                case "virtualeditor":
                    listField = "m_EditorInstances";
                    descType = Require(ref s_VirtualDesc, VirtualEditorDescTypeName);
                    break;
                case "local":
                case "player":
                case "localplayer":
                    listField = "m_LocalInstances";
                    descType = Require(ref s_LocalDesc, LocalDescTypeName);
                    break;
                case "main":
                case "editor":
                case "maineditor":
                    error = "The Main Editor is added automatically and cannot be added as a player.";
                    return null;
                default:
                    error = $"Unknown player type '{typeKey}'. Use 'clone' or 'local'.";
                    return null;
            }

            var field = FieldDeep(ScenarioConfigType, listField, InstanceFlags);
            if (field == null || !(field.GetValue(scenario) is IList list))
            {
                error = $"ScenarioConfig instance list '{listField}' not found.";
                return null;
            }

            object desc;
            try
            {
                desc = Activator.CreateInstance(descType, nonPublic: true);
            }
            catch (Exception e)
            {
                error = $"Failed to construct {descType.Name}: {e.Message}";
                return null;
            }

            var effectiveName = string.IsNullOrWhiteSpace(name) ? DefaultName(typeKey, list.Count) : name;
            FieldDeep(descType, "Name", InstanceFlags)?.SetValue(desc, effectiveName);

            list.Add(desc);

            if (listField == "m_EditorInstances")
                FieldDeep(ScenarioConfigType, "m_EnableEditors", InstanceFlags)?.SetValue(scenario, true);

            EditorUtility.SetDirty(scenario);
            AssetDatabase.SaveAssets();

            // MPPM's ScenarioConfig.OnValidate() resequences editor-instance names ("Player 2",
            // "Player 3", ...) from their position and discards the Name set above. It normally runs on
            // a later editor tick, so returning effectiveName here would hand back an id that
            // list_players/remove_player never see. Reimport first so the freshly constructed
            // description's fields are deserialized (invoking OnValidate before that NREs on its
            // still-null graph fields), then run OnValidate now to assign the canonical name and return
            // the value that actually persisted.
            var assetPath = AssetDatabase.GetAssetPath(scenario);
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
            var reloaded = AssetDatabase.LoadMainAssetAtPath(assetPath);
            MethodDeep(ScenarioConfigType, "OnValidate", InstanceFlags, Type.EmptyTypes)?.Invoke(reloaded, null);

            var reloadedList = FieldDeep(ScenarioConfigType, listField, InstanceFlags)?.GetValue(reloaded) as IList;
            var persistedName = (reloadedList != null && reloadedList.Count > 0)
                ? ReadName(reloadedList[reloadedList.Count - 1])
                : null;
            return persistedName ?? effectiveName;
        }

        public static bool RemoveInstance(UnityEngine.Object scenario, string id, out string error)
        {
            error = null;

            foreach (var listField in new[] { "m_EditorInstances", "m_LocalInstances", "m_RemoteInstances" })
            {
                var field = FieldDeep(ScenarioConfigType, listField, InstanceFlags);
                if (!(field?.GetValue(scenario) is IList list))
                    continue;

                for (int i = 0; i < list.Count; i++)
                {
                    if (list[i] == null)
                        continue;

                    if (string.Equals(ReadName(list[i]), id, StringComparison.OrdinalIgnoreCase))
                    {
                        list.RemoveAt(i);
                        EditorUtility.SetDirty(scenario);
                        AssetDatabase.SaveAssets();
                        return true;
                    }
                }
            }

            error = $"No player with id/name '{id}' in this scenario. List them with editor.playmode list_players.";
            return false;
        }

        // ---- internals ---------------------------------------------------------------------------

        static object PlayModeManagerInstance()
        {
            var prop = PropDeep(PlayModeManagerType, "instance", StaticFlags);
            var pm = prop?.GetValue(null);
            if (pm == null)
                throw new InvalidOperationException("PlayModeManager.instance is unavailable.");
            return pm;
        }

        static void InvokeManager(string method)
        {
            var pm = PlayModeManagerInstance();
            var m = MethodDeep(pm.GetType(), method, InstanceFlags, Type.EmptyTypes);
            if (m == null)
                throw new InvalidOperationException($"PlayModeManager.{method}() not found.");

            try
            {
                m.Invoke(pm, null);
            }
            catch (TargetInvocationException tie)
            {
                throw new InvalidOperationException(tie.InnerException?.Message ?? tie.Message);
            }
        }

        static string ReadName(object instance)
        {
            return FieldDeep(instance.GetType(), "Name", InstanceFlags)?.GetValue(instance) as string;
        }

        static string TypeKey(object item)
        {
            var typeName = PropDeep(item.GetType(), "InstanceTypeName", InstanceFlags)?.GetValue(item)?.ToString();
            switch (typeName)
            {
                case "MainEditor": return MppmInstanceInfo.RoleMain; // "main"
                case "VirtualEditor": return "clone";
                case "Local": return "local";
                case "Remote": return "remote";
                default: return typeName?.ToLowerInvariant() ?? item.GetType().Name;
            }
        }

        static string DefaultName(string typeKey, int index)
        {
            var prefix = typeKey.Trim().ToLowerInvariant().StartsWith("local") || typeKey == "player"
                ? "LocalPlayer"
                : "Player";
            return $"{prefix} {index + 1}";
        }

        static Type Require(ref Type cache, string fullName)
        {
            if (cache == null)
            {
                cache = FindType(fullName);
                if (cache == null)
                    throw new InvalidOperationException($"MPPM type '{fullName}' not found. Is Multiplayer Play Mode installed and active?");
            }
            return cache;
        }

        static Type FindType(string fullName)
        {
            foreach (var assembly in PipelineUtils.GetLoadedAssemblies())
            {
                var candidate = assembly.GetType(fullName, false);
                if (candidate != null)
                    return candidate;
            }
            return null;
        }

        static PropertyInfo PropDeep(Type type, string name, BindingFlags flags)
        {
            for (var current = type; current != null; current = current.BaseType)
            {
                var property = current.GetProperty(name, flags | BindingFlags.DeclaredOnly);
                if (property != null)
                    return property;
            }
            return null;
        }

        static FieldInfo FieldDeep(Type type, string name, BindingFlags flags)
        {
            for (var current = type; current != null; current = current.BaseType)
            {
                var field = current.GetField(name, flags | BindingFlags.DeclaredOnly);
                if (field != null)
                    return field;
            }
            return null;
        }

        static MethodInfo MethodDeep(Type type, string name, BindingFlags flags, Type[] argumentTypes)
        {
            for (var current = type; current != null; current = current.BaseType)
            {
                var method = argumentTypes == null
                    ? current.GetMethod(name, flags | BindingFlags.DeclaredOnly)
                    : current.GetMethod(name, flags | BindingFlags.DeclaredOnly, null, argumentTypes, null);
                if (method != null)
                    return method;
            }
            return null;
        }
    }
}
#endif
