#if UNITY_6000_5_OR_NEWER && MPPM_AVAILABLE
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Unity.Pipeline.Commands;
using Unity.Pipeline.Editor.Authoring;
using Unity.Pipeline.Models;
using Unity.PlayMode.Editor;
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
            var scenarios = PlayModeScenarioReflection.FindScenarioAssets()
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

            var state = PlayModeScenarioManager.State;
            if (state != PlayModeScenarioState.Idle)
                throw new InvalidOperationException($"Cannot change the active scenario while state is '{state}'. Stop it first (editor.playmode stop_scenario).");

            var scenario = PlayModeScenarioReflection.LoadScenarioAsset(path, out var loadError);
            if (scenario == null)
                throw new InvalidOperationException(loadError ?? $"No Play Mode Scenario asset at '{path}'.");

            PlayModeScenarioManager.ActiveScenario = scenario;

            return new
            {
                active = new { name = scenario.name, path = AssetDatabase.GetAssetPath(scenario) },
                state = PlayModeScenarioReflection.GetState()
            };
        }

        [CliGroupCommand(Group,
            "Start the active Play Mode Scenario (enters Play Mode with its players)",
            Tags = new[] { "editor/playmode" })]
        public static object StartScenario()
        {
            var active = PlayModeScenarioReflection.GetActiveScenarioAsset();
            if (active == null)
                throw new InvalidOperationException($"No scenario asset is active. {PlayModeScenarioReflection.DefaultScenarioNote}");

            PlayModeScenarioManager.Start();

            return new
            {
                started = true,
                active = new { name = active.name, path = AssetDatabase.GetAssetPath(active) },
                state = PlayModeScenarioReflection.GetState(),
                message = "Scenario starting. Player editors launch asynchronously; poll editor.playmode get_scenario_status or editor.playmode list_instances."
            };
        }

        [CliGroupCommand(Group,
            "Stop the active/running Play Mode Scenario",
            Tags = new[] { "editor/playmode" })]
        public static object StopScenario()
        {
            PlayModeScenarioManager.Stop();

            return new
            {
                stopped = true,
                state = PlayModeScenarioReflection.GetState(),
                note = "State settles asynchronously; poll editor.playmode get_scenario_status for the final state."
            };
        }

        [CliGroupCommand(Group,
            "Live state of the active/running Play Mode Scenario: its state plus one entry per player the scenario declares, each with its run state and, once its Editor is up, the port and project path to reach it.",
            MainThreadRequired = true,
            Tags = new[] { "editor/playmode" })]
        public static object GetScenarioStatus()
        {
            var active = PlayModeScenarioReflection.GetActiveScenarioAsset();
            var declared = PlayModeScenarioReflection.DescribePlayers(active, out var playerError);
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
                state = PlayModeScenarioReflection.GetState(),
                playMode = EditorApplication.isPaused ? "paused" : (EditorApplication.isPlaying ? "playing" : "stopped"),
                active = active == null ? null : new { name = active.name, path = AssetDatabase.GetAssetPath(active) },
                isDefaultScenario = active == null,
                players,
                playerError = playerError ?? mppmError,
                note = active == null ? PlayModeScenarioReflection.DefaultScenarioNote : null
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
            PlayModeScenario scenario;
            if (path == null)
            {
                scenario = PlayModeScenarioReflection.GetActiveScenarioAsset();
            }
            else
            {
                scenario = PlayModeScenarioReflection.LoadScenarioAsset(path, out var loadError);
                if (scenario == null)
                    throw new InvalidOperationException(loadError ?? $"No Play Mode Scenario asset at '{path}'.");
            }

            var declared = PlayModeScenarioReflection.DescribePlayers(scenario, out var playerError);

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
                note = scenario == null ? PlayModeScenarioReflection.DefaultScenarioNote : null
            };
        }

        [CliGroupCommand(Group,
            "Create a new orchestrated Play Mode Scenario asset (auto-adds the Main Editor player). The path must end in '.asset' — Unity does not import a scenario from any other extension." + ScenarioCreateNotes.DescriptionSuffix,
            Tags = new[] { "editor/playmode" })]
        public static object CreateScenario(
            [CliArg("name", "Scenario name (also the asset file name).", Required = true)] string name,
            [CliArg("path", "Optional asset path under Assets/; must end in '.asset'. Defaults to <authoring root>/Settings/PlayMode/<name>.asset, which is Assets/Settings/PlayMode/<name>.asset unless the authoring root is changed.")] string path = null)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("name is required.");

            if (name.IndexOfAny(System.IO.Path.GetInvalidFileNameChars()) >= 0 || name.IndexOf('\\') >= 0)
                throw new ArgumentException($"Scenario name '{name}' contains characters that are not valid in a file name (for example '/' or '\\'). It becomes the asset file name, so pick a plain file name.");

            var created = PlayModeScenarioReflection.CreateScenarioAsset(name, path, out var error);
            if (created == null)
                throw new InvalidOperationException(error);

            var createdPath = AssetDatabase.GetAssetPath(created);
            var active = PlayModeScenarioReflection.GetActiveScenarioAsset() == created;
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
            "Add a player to a scenario asset. type: clone | local. A clone takes the next Multiplayer Play Mode player slot and its name (Player 2, Player 3, Player 4); slot 0 (Player 1) is the Main Editor, so a scenario holds at most 3 clones.",
            Tags = new[] { "editor/playmode" })]
        public static object AddPlayer(
            [CliArg("path", "Scenario asset path (defaults to the active scenario).")] string path = null,
            [CliArg("type", "Player type: 'clone' (virtual player) or 'local' (built player).", Required = true)] string type = null,
            [CliArg("name", "Name for a local player (optional). Ignored for a clone, which Multiplayer Play Mode names by its player slot.")] string name = null)
        {
            var state = PlayModeScenarioManager.State;
            if (state != PlayModeScenarioState.Idle)
                throw new InvalidOperationException($"Cannot add a player while state is '{state}'. Stop it first (editor.playmode stop_scenario).");

            var scenario = PlayModeScenarioReflection.ResolveScenario(path, out var resolveError);
            if (scenario == null)
                throw new InvalidOperationException(resolveError);

            var resolvedPath = AssetDatabase.GetAssetPath(scenario);
            var id = PlayModeScenarioReflection.AddInstance(scenario, type, name, out var addedName, out var error);
            if (id == null)
                throw new InvalidOperationException(error);

            var nameIgnored = !string.IsNullOrEmpty(name) && !string.Equals(name, addedName, StringComparison.Ordinal);
            return new
            {
                added = true,
                id,
                type,
                name = addedName,
                path = resolvedPath,
                note = nameIgnored
                    ? $"Multiplayer Play Mode names clones by their player slot, so --name '{name}' was not used. This clone is '{addedName}'."
                    : null
            };
        }

        [CliGroupCommand(Group,
            "Remove a scenario player by its id (GUID). Destructive: requires confirm=true (use dry-run to preview).",
            Tags = new[] { "editor/playmode" })]
        public static object RemovePlayer(
            [CliArg("id", "Player id (GUID) to remove.", Required = true)] string id,
            [CliArg("path", "Scenario asset path (defaults to the active scenario).")] string path = null,
            [CliArg("confirm", "Must be true to actually remove the player. Without it the command refuses (destructive guard).")] bool confirm = false,
            [CliArg("dry-run", "If true, report the player that would be removed without changing the scenario.")] bool dryRun = false)
        {
            var state = PlayModeScenarioManager.State;
            if (state != PlayModeScenarioState.Idle)
                throw new InvalidOperationException($"Cannot remove a player while state is '{state}'. Stop it first (editor.playmode stop_scenario).");

            var scenario = PlayModeScenarioReflection.ResolveScenario(path, out var resolveError);
            if (scenario == null)
                throw new InvalidOperationException(resolveError);

            var scenarioPath = AssetDatabase.GetAssetPath(scenario);
            if (dryRun)
            {
                var player = PlayModeScenarioReflection.DescribePlayers(scenario, out var describeError)?
                    .FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));
                if (player == null)
                    throw new InvalidOperationException(describeError ?? $"No player with id '{id}' in '{scenarioPath}'. List them with editor.playmode list_players.");

                return new { status = "dry_run", wouldRemove = new { id = player.Id, name = player.Name, type = player.Type }, path = scenarioPath };
            }

            if (!confirm)
                throw new ArgumentException($"Refusing to remove player '{id}' from '{scenarioPath}'. Pass confirm=true to remove it (rewrites the scenario asset, not undoable via Unity's Undo), or dry-run=true to preview.");

            if (!PlayModeScenarioReflection.RemoveInstance(scenario, id, out var error))
                throw new InvalidOperationException(error);

            return new { removed = true, id, path = scenarioPath };
        }
    }

    internal static class PlayModeScenarioReflection
    {
        private const string OrchestratedScenarioTypeName = "Unity.Multiplayer.PlayMode.Editor.OrchestratedScenario";
        private const string CloneControllerTypeName = "Unity.Multiplayer.PlayMode.Editor.CloneEditorController";
        private const string LocalPlayerControllerTypeName = "Unity.Multiplayer.PlayMode.Editor.LocalPlayerController";
        private const string MainEditorControllerTypeName = "Unity.Multiplayer.PlayMode.Editor.MainEditorController";

        private const BindingFlags MemberFlags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

        public const string ScenarioAssetExtension = ".asset";

        public const string DefaultScenarioNote =
            "PlayModeScenarioManager.ActiveScenario falls back to a non-persistent DefaultScenario (main Editor only, no clones) when no scenario asset is assigned. " +
            "Assign one with editor.playmode set_active_scenario before editor.playmode start_scenario, or starting will just enter ordinary single-Editor Play Mode.";

        public static string GetState() => PlayModeScenarioManager.State.ToString();

        public static PlayModeScenario GetActiveScenario() => PlayModeScenarioManager.ActiveScenario;

        public static PlayModeScenario GetActiveScenarioAsset()
        {
            var active = GetActiveScenario();
            return active != null && EditorUtility.IsPersistent(active) ? active : null;
        }

        public static PlayModeScenario ResolveScenario(string path, out string error)
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

        public static List<ScenarioPlayer> DescribePlayers(PlayModeScenario scenario, out string error)
        {
            error = null;
            if (scenario == null)
                return new List<ScenarioPlayer>();

            if (!TryGetSettings(scenario, out _, out var boxedSettings, out var settingsStructType, out error))
                return null;

            var getAll = FindPlayerItemsMethod(settingsStructType);
            if (getAll == null)
            {
                error = $"Scenario settings method '{string.Join("' or '", PlayerItemsMethodNames)}' not found.";
                return null;
            }

            var items = getAll.Invoke(boxedSettings, null) as System.Collections.IEnumerable;
            if (items == null)
                return new List<ScenarioPlayer>();

            var described = new List<ScenarioPlayer>();
            foreach (var item in items)
            {
                if (item == null)
                    continue;

                var itemType = item.GetType();
                var controllerType = itemType.GetMethod("GetInstanceType", MemberFlags)?.Invoke(item, null) as Type;
                described.Add(new ScenarioPlayer
                {
                    Id = itemType.GetMethod("GetId", MemberFlags)?.Invoke(item, null)?.ToString(),
                    Name = itemType.GetMethod("GetName", MemberFlags)?.Invoke(item, null) as string,
                    Type = ControllerTypeToKey(controllerType),
                    Controller = controllerType?.Name,
                    RunMode = itemType.GetMethod("GetRunMode", MemberFlags)?.Invoke(item, null)?.ToString()
                });
            }

            return described;
        }

        // The settings method that lists a scenario's players. Every 6000.5+ release names it
        // GetAllInstanceItems; one 6000.7 alpha renamed it GetAllControllerItems before MPPM renamed it back.
        internal static readonly string[] PlayerItemsMethodNames = { "GetAllInstanceItems", "GetAllControllerItems" };

        internal static MethodInfo FindPlayerItemsMethod(Type settingsStructType) =>
            PlayerItemsMethodNames
                .Select(name => settingsStructType.GetMethod(name, MemberFlags, null, Type.EmptyTypes, null))
                .FirstOrDefault(method => method != null);

        public static PlayModeScenario LoadScenarioAsset(string path, out string error)
        {
            error = null;

            var scenario = AssetDatabase.LoadAssetAtPath<PlayModeScenario>(path);
            if (scenario != null)
                return scenario;

            if (System.IO.File.Exists(path) && !ValidateScenarioAssetPath(path.Replace('\\', '/'), out var pathError))
                error = pathError;
            else
                error = $"'{path}' is not a Play Mode Scenario asset (or does not exist). List the ones that are with editor.playmode list_scenarios.";

            return null;
        }

        public static List<PlayModeScenario> FindScenarioAssets()
        {
            var paths = new HashSet<string>();
            foreach (var filter in new[] { "t:PlayModeScenario", "t:OrchestratedScenario" })
            {
                foreach (var guid in AssetDatabase.FindAssets(filter))
                    paths.Add(AssetDatabase.GUIDToAssetPath(guid));
            }

            var result = new List<PlayModeScenario>();
            foreach (var path in paths)
            {
                var scenario = AssetDatabase.LoadAssetAtPath<PlayModeScenario>(path);
                if (scenario != null)
                    result.Add(scenario);
            }

            return result;
        }

        public static PlayModeScenario CreateScenarioAsset(string name, string path, out string error)
        {
            error = null;

            var orchestratedType = FindType(OrchestratedScenarioTypeName);
            if (orchestratedType == null)
            {
                error = $"'{OrchestratedScenarioTypeName}' not found. Is Multiplayer Play Mode installed?";
                return null;
            }

            if (string.IsNullOrEmpty(path))
                path = ProjectPaths.AuthoringRoot + "/Settings/PlayMode/" + name + ScenarioAssetExtension;

            path = path.Replace('\\', '/');

            if (!ValidateScenarioAssetPath(path, out error))
                return null;

            var dir = System.IO.Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
            {
                System.IO.Directory.CreateDirectory(dir);
                AssetDatabase.Refresh();
            }

            var scenario = ScriptableObject.CreateInstance(orchestratedType) as PlayModeScenario;
            if (scenario == null)
            {
                error = "Failed to instantiate OrchestratedScenario.";
                return null;
            }
            scenario.name = name;

            if (AssetDatabase.LoadMainAssetAtPath(path) != null || System.IO.File.Exists(path))
            {
                error = $"A scenario already exists at '{path}'. Choose a different name or delete the existing asset first; " +
                        $"create refuses rather than silently writing a differently-named copy.";
                UnityEngine.Object.DestroyImmediate(scenario);
                return null;
            }

            AssetDatabase.CreateAsset(scenario, path);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            var imported = AssetDatabase.LoadAssetAtPath<PlayModeScenario>(path);
            if (imported == null)
            {
                error = $"Unity wrote '{path}' but did not import it as a Play Mode Scenario. " +
                        $"Delete the file and retry; if it persists, check the Editor console for an AssetDatabase error.";
                return null;
            }

            return imported;
        }

        public static bool ValidateScenarioAssetPath(string path, out string error)
        {
            error = null;

            if (!path.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
            {
                error = $"'{path}' is outside the Assets folder. Scenario assets must be created under 'Assets/'.";
                return false;
            }

            if (ProjectPaths.Resolve(path, out var confinementError) == null)
            {
                error = confinementError;
                return false;
            }

            var extension = System.IO.Path.GetExtension(path);
            if (string.Equals(extension, ScenarioAssetExtension, StringComparison.OrdinalIgnoreCase))
                return true;

            var corrected = System.IO.Path.ChangeExtension(path, ScenarioAssetExtension).Replace('\\', '/');
            error = string.IsNullOrEmpty(extension)
                ? $"'{path}' has no file extension. Play Mode Scenario assets must end in '{ScenarioAssetExtension}' — use '{corrected}'."
                : $"'{path}' ends in '{extension}'. Unity only imports a Play Mode Scenario from a '{ScenarioAssetExtension}' file; " +
                  $"any other extension is written to disk but imported as a DefaultAsset, leaving the scenario invisible to every other scenario command. Use '{corrected}'.";
            return false;
        }

        // MPPM launches clone n (1-3) as virtual player slot n and names it "Player n+1"; slot 0 is the
        // Main Editor. It only re-sequences clones when it loads the scenario asset, so a clone written
        // with the default slot 0 never launches until a reload. Mirrors OrchestratedScenario's
        // k_MaxCloneEditorInstances, which is the same on every 6000.5+ release.
        internal const int MaxCloneEditorInstances = 3;

        public static string AddInstance(PlayModeScenario scenario, string typeKey, string name, out string error) =>
            AddInstance(scenario, typeKey, name, out _, out error);

        public static string AddInstance(PlayModeScenario scenario, string typeKey, string name, out string addedName, out string error)
        {
            addedName = null;
            if (!TryResolveControllerType(typeKey, out var controllerType, out error))
                return null;

            if (!TryGetSettings(scenario, out var settingsField, out var boxedSettings, out var settingsStructType, out error))
                return null;

            var instanceSettingsType = controllerType.GetNestedType("InstanceSettings", BindingFlags.Public | BindingFlags.NonPublic);
            if (instanceSettingsType == null)
            {
                error = $"'InstanceSettings' nested type not found on {controllerType.Name}.";
                return null;
            }

            var defaults = Activator.CreateInstance(instanceSettingsType);

            addedName = name ?? string.Empty;
            if (controllerType.FullName == CloneControllerTypeName
                && !TryAssignCloneSlot(scenario, instanceSettingsType, defaults, out addedName, out error))
                return null;

            MethodInfo addInstance = null;
            foreach (var method in settingsStructType.GetMethods(BindingFlags.Public | BindingFlags.Instance))
            {
                if (method.Name != "AddInstance" || !method.IsGenericMethodDefinition)
                    continue;
                if (method.GetGenericArguments().Length != 2)
                    continue;

                var methodParameters = method.GetParameters();
                if (methodParameters.Length == 2 && methodParameters[0].ParameterType == typeof(string))
                {
                    addInstance = method;
                    break;
                }
            }
            if (addInstance == null)
            {
                error = "AddInstance<TController,TSettings>(string, TSettings) overload not found.";
                return null;
            }

            object id;
            try
            {
                id = addInstance
                    .MakeGenericMethod(controllerType, instanceSettingsType)
                    .Invoke(boxedSettings, new[] { (object)addedName, defaults });
            }
            catch (TargetInvocationException tie)
            {
                error = tie.InnerException?.Message ?? tie.Message;
                return null;
            }

            settingsField.SetValue(scenario, boxedSettings);
            MakeSettingsConsistent(scenario);
            EditorUtility.SetDirty(scenario);
            AssetDatabase.SaveAssets();

            var idString = id?.ToString();
            var added = DescribePlayers(scenario, out _)?.FirstOrDefault(p => p.Id == idString);
            if (added?.Name != null)
                addedName = added.Name;

            return idString;
        }

        // Runs MPPM's own clean-up, which re-sequences clone slots and names in list order. MPPM only
        // runs it when the asset loads, so without this a clone removed from the middle leaves the
        // others on stale slots until the next reload. Present on every 6000.5+ release; if a future
        // MPPM drops it, AddInstance's explicit slot still covers a scenario with no gaps.
        private static void MakeSettingsConsistent(PlayModeScenario scenario) =>
            scenario.GetType()
                .GetMethod("MakeSettingsConsistent", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null)
                ?.Invoke(scenario, null);

        // Gives a new clone the next free slot and the name MPPM shows for it, or refuses a clone past
        // MPPM's limit. Clones fill slots in list order, so the new one, appended last, takes the next.
        private static bool TryAssignCloneSlot(
            PlayModeScenario scenario, Type instanceSettingsType, object settings, out string cloneName, out string error)
        {
            cloneName = null;

            var players = DescribePlayers(scenario, out error);
            if (players == null)
                return false;

            var existingClones = players.Count(p => p.Type == MppmInstanceInfo.RoleClone);
            if (existingClones >= MaxCloneEditorInstances)
            {
                error = $"The scenario already has {existingClones} clone players, the most Multiplayer Play Mode can launch. " +
                        "Remove one with editor.playmode remove_player, or add a built player with --type local.";
                return false;
            }

            var slotField = instanceSettingsType.GetField("PlayerInstanceIndex", BindingFlags.Public | BindingFlags.Instance);
            if (slotField == null || slotField.FieldType != typeof(int))
            {
                error = $"'{instanceSettingsType.FullName}.PlayerInstanceIndex' not found, so the clone's player slot can't be set.";
                return false;
            }

            var slot = existingClones + 1;
            slotField.SetValue(settings, slot);
            cloneName = $"Player {slot + 1}";
            return true;
        }

        public static bool RemoveInstance(PlayModeScenario scenario, string idString, out string error)
        {
            if (!TryGetSettings(scenario, out var settingsField, out var boxedSettings, out var settingsStructType, out error))
                return false;

            var findIndex = settingsStructType.GetMethod("FindInstanceIndexById", MemberFlags);
            var removeAt = settingsStructType.GetMethod("RemoveInstanceAt", MemberFlags);
            if (findIndex == null || removeAt == null)
            {
                error = "Scenario settings remove API (FindInstanceIndexById/RemoveInstanceAt) not found.";
                return false;
            }

            var parameterType = findIndex.GetParameters()[0].ParameterType;
            if (parameterType != typeof(UnityEngine.GUID))
            {
                error = $"Scenario settings FindInstanceIndexById expects a {parameterType.FullName}, which this version of the Pipeline package cannot supply.";
                return false;
            }

            if (!UnityEngine.GUID.TryParse(idString, out var guid))
            {
                error = $"'{idString}' is not a valid player id.";
                return false;
            }

            int index;
            try
            {
                index = (int)findIndex.Invoke(boxedSettings, new object[] { guid });
            }
            catch (TargetInvocationException)
            {
                error = $"No player with id '{idString}' in this scenario.";
                return false;
            }

            if (index < 0)
            {
                error = $"No player with id '{idString}' in this scenario. List the ids with editor.playmode list_players.";
                return false;
            }

            try
            {
                removeAt.Invoke(boxedSettings, new object[] { index });
            }
            catch (TargetInvocationException tie)
            {
                error = tie.InnerException?.Message ?? tie.Message;
                return false;
            }

            settingsField.SetValue(scenario, boxedSettings);
            MakeSettingsConsistent(scenario);
            EditorUtility.SetDirty(scenario);
            AssetDatabase.SaveAssets();
            return true;
        }

        private static bool TryGetSettings(
            PlayModeScenario scenario,
            out FieldInfo settingsField,
            out object boxedSettings,
            out Type settingsStructType,
            out string error)
        {
            settingsField = null;
            boxedSettings = null;
            settingsStructType = null;
            error = null;

            if (scenario == null)
            {
                error = "No scenario asset supplied.";
                return false;
            }

            settingsField = scenario.GetType().GetField("m_Settings", MemberFlags);
            if (settingsField == null)
            {
                error = "Scenario settings field 'm_Settings' not found.";
                return false;
            }

            settingsStructType = settingsField.FieldType;
            boxedSettings = settingsField.GetValue(scenario);
            return true;
        }

        private static string ControllerTypeToKey(Type controllerType)
        {
            if (controllerType == null)
                return null;

            if (controllerType.FullName == CloneControllerTypeName)
                return "clone";
            if (controllerType.FullName == LocalPlayerControllerTypeName)
                return "local";
            if (controllerType.FullName == MainEditorControllerTypeName)
                return "main";

            return controllerType.Name;
        }

        private static bool TryResolveControllerType(string typeKey, out Type controllerType, out string error)
        {
            controllerType = null;
            error = null;

            string fullName;
            switch ((typeKey ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "clone":
                case "virtual":
                case "cloneeditor":
                    fullName = CloneControllerTypeName;
                    break;
                case "local":
                case "player":
                case "localplayer":
                    fullName = LocalPlayerControllerTypeName;
                    break;
                case "main":
                case "editor":
                case "maineditor":
                    error = "The Main Editor is added automatically and cannot be added as a player.";
                    return false;
                default:
                    error = $"Unknown player type '{typeKey}'. Use 'clone' or 'local'.";
                    return false;
            }

            controllerType = FindType(fullName);
            if (controllerType == null)
            {
                error = $"Controller type '{fullName}' not found. Is Multiplayer Play Mode installed?";
                return false;
            }

            return true;
        }

        private static Type FindType(string fullName)
        {
            foreach (var assembly in PipelineUtils.GetLoadedAssemblies())
            {
                var candidate = assembly.GetType(fullName, false);
                if (candidate != null)
                    return candidate;
            }

            return null;
        }
    }
}
#endif
