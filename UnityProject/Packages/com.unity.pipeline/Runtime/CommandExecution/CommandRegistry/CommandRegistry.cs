using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
#if UNITY_6000_5_OR_NEWER
using Unity.Scripting.LifecycleManagement;
#endif

namespace Unity.Pipeline.Commands
{
    /// <summary>
    /// Registry for discovering and managing CLI commands via pluggable discovery mechanism.
    /// Scans for methods marked with [CliGroupCommand] or [CliCommand] attribute across all assemblies.
    /// Based on unity-tools ToolRegistry patterns adapted for Pipeline requirements.
    /// </summary>
    /// <remarks>
    /// [NoAutoStaticsCleanup] on purpose: m_DiscoveryLock and the injected discovery mechanism are
    /// set up once per domain and must not be reset under the running server. The dynamic-command
    /// table is the one static here with a shorter useful life, and PlayModeSessionEnded resets the
    /// part of it that a Play session owns.
    /// </remarks>
#if UNITY_6000_5_OR_NEWER
    [NoAutoStaticsCleanup]
#endif
    public static class CommandRegistry
    {
        private static IReadOnlyList<CommandInfo> m_CachedCommands;
        private static IReadOnlyList<CommandInfo> m_CachedAllCommands;
        // m_CachedAllCommands indexed by full name and group; built and cleared together with it.
        private static CommandIndex m_CachedIndex;
        // Keyed by FullName.
        private static readonly Dictionary<string, CommandInfo> m_DynamicCommands =
            new Dictionary<string, CommandInfo>(StringComparer.Ordinal);
        // Full names in m_DynamicCommands that predate the Play session currently running; null outside
        // one. Lets PlayModeSessionEnded tell a registration made during the session apart from one
        // that was already there. See PlayModeSessionStarted.
        private static HashSet<string> m_PrePlayModeCommands;
        private static ICommandDiscovery m_Discovery;
        private static readonly object m_DiscoveryLock = new object();
        private static readonly ICommandRegistry s_Registry = new DefaultCommandRegistry();

        /// <summary>This registry as an <see cref="ICommandRegistry"/>, never null.</summary>
        internal static ICommandRegistry GetRegistry() => s_Registry;

        /// <summary>
        /// Set the command discovery mechanism.
        /// Editor assembly provides TypeCache-based discovery, Runtime uses reflection fallback.
        /// </summary>
        /// <param name="discovery">The discovery mechanism to use.</param>
        internal static void SetDiscovery(ICommandDiscovery discovery)
        {
            m_Discovery = discovery;
            ClearCache(); // Re-discover with new mechanism
        }

        /// <summary>
        /// Discover every available command: the methods marked with [CliGroupCommand] or [CliCommand], plus any
        /// registered through <see cref="RegisterCommand"/>.
        /// Attribute discovery uses the injected mechanism (TypeCache in Editor, reflection in
        /// Runtime) and is cached until domain reload; the combined result is re-cached whenever
        /// a dynamic command is registered or unregistered.
        /// </summary>
        /// <returns>All discovered commands.</returns>
        public static IEnumerable<CommandInfo> DiscoverCommands()
        {
            var cached = m_CachedAllCommands;
            if (cached != null)
            {
                return cached;
            }

            lock (m_DiscoveryLock)
            {
                EnsureCacheLocked();
                return m_CachedAllCommands;
            }
        }

        /// <summary>
        /// Look a command up by its <see cref="CommandInfo.FullName"/>.
        /// </summary>
        /// <param name="fullName">The command's full name, <c>group name</c>.</param>
        /// <param name="command">The command, or null when no command has that name.</param>
        /// <returns>True when a command has that name.</returns>
        internal static bool TryGetCommand(string fullName, out CommandInfo command)
        {
            return Index.TryGetCommand(fullName, out command);
        }

        /// <summary>
        /// Every available command indexed by full name and group: the same set
        /// <see cref="DiscoverCommands"/> returns. Take it once per request and read everything
        /// from that snapshot, so a registration landing in between cannot mix two states.
        /// </summary>
        internal static CommandIndex Index
        {
            get
            {
                var index = m_CachedIndex;
                if (index != null)
                    return index;

                lock (m_DiscoveryLock)
                {
                    EnsureCacheLocked();
                    return m_CachedIndex;
                }
            }
        }

        /// <summary>
        /// Build whichever caches are missing. The index is assigned before the list, so a reader
        /// that sees the list on the lock-free path of <see cref="DiscoverCommands"/> also finds the
        /// index ready. Caller holds <see cref="m_DiscoveryLock"/>.
        /// </summary>
        private static void EnsureCacheLocked()
        {
            if (m_CachedCommands == null)
            {
                m_CachedCommands = RejectDuplicateNames(DiscoverCommandsInternal()).ToList();
            }

            if (m_CachedAllCommands == null)
            {
                var byName = m_CachedCommands.ToDictionary(c => c.FullName, StringComparer.Ordinal);
                var all = m_CachedCommands;
                if (m_DynamicCommands.Count > 0)
                {
                    var combined = new List<CommandInfo>(m_CachedCommands);
                    foreach (var dynamic in m_DynamicCommands.Values)
                    {
                        // RegisterCommand refuses an attribute command's name, but a re-discovery
                        // (ClearCache, SetDiscovery) can surface one after the fact; the attribute
                        // command is part of the code and wins.
                        if (byName.ContainsKey(dynamic.FullName))
                        {
                            Debug.LogError($"Dynamic command '{dynamic.FullName}' is hidden by a [CliGroupCommand] with the same name.");
                            continue;
                        }
                        byName.Add(dynamic.FullName, dynamic);
                        combined.Add(dynamic);
                    }
                    all = combined;
                }
                m_CachedIndex = new CommandIndex(all);
                m_CachedAllCommands = all;
            }
        }

        /// <summary>
        /// The names of the assemblies built from this package: the <c>name</c> of each asmdef under
        /// <c>packages/com.unity.pipeline</c>, held here and nowhere else. A command declared in one
        /// of them ships with the package; a command declared anywhere else (a project's own
        /// assemblies, and the test assemblies, which are separate packages) does not. Matched by
        /// exact name, never by prefix, so <c>Unity.Pipeline.Tests.Editor</c> is not the package.
        /// </summary>
        internal static readonly IReadOnlyList<string> PackageAssemblyNames = new[]
        {
            "Unity.Pipeline",
            "Unity.Pipeline.Editor",
            "Unity.Pipeline.Attributes",
            "Unity.Pipeline.IlInterpreter",
            "Unity.Pipeline.CodeGen",
        };

        /// <summary>Whether <paramref name="assemblyName"/> is one of the <see cref="PackageAssemblyNames"/>.</summary>
        internal static bool IsPackageAssembly(string assemblyName)
        {
            for (var i = 0; i < PackageAssemblyNames.Count; i++)
            {
                if (string.Equals(PackageAssemblyNames[i], assemblyName, StringComparison.Ordinal))
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Resolve attribute-declared commands that share a full name. Discovery order is not guaranteed,
        /// so keeping "the first" would pick a winner at random, and a package command must not be
        /// switchable off by a project declaring the same name: when exactly one of the declarations
        /// is in one of this package's own assemblies (<see cref="PackageAssemblyNames"/>), that one
        /// is kept and the others are refused. Otherwise (no declaration of the package's, or more
        /// than one) every declaration sharing the name is refused. Either way an error is logged that
        /// names every declaring method, and says which was kept and which were refused. Commands
        /// with their own name are untouched.
        /// </summary>
        internal static IEnumerable<CommandInfo> RejectDuplicateNames(IEnumerable<CommandInfo> commands)
        {
            var result = new List<CommandInfo>();
            foreach (var sameName in commands.GroupBy(c => c.FullName, StringComparer.Ordinal))
            {
                var declared = sameName.ToList();
                if (declared.Count == 1)
                {
                    result.Add(declared[0]);
                    continue;
                }

                var own = declared.Where(c => IsPackageAssembly(c.Package)).ToList();
                var name = declared[0].FullName;
                if (own.Count == 1)
                {
                    var refused = declared.Where(c => c != own[0]).ToList();
                    Debug.LogError($"Command '{name}' is declared {declared.Count} times; the package's own declaration " +
                        $"({DescribeMethod(own[0])}) is kept and the others are refused ({string.Join(", ", refused.Select(DescribeMethod))}).");
                    result.Add(own[0]);
                    continue;
                }

                Debug.LogError($"Command '{name}' is declared {declared.Count} times ({string.Join(", ", declared.Select(DescribeMethod))}); none of them is registered.");
            }
            return result;
        }

        private static string DescribeMethod(CommandInfo command) =>
            $"{command.Method.DeclaringType?.FullName}.{command.Method.Name}";

        /// <summary>
        /// Register a command at runtime, without a [CliGroupCommand] attribute. The resulting command
        /// is indistinguishable from an attribute-declared one: it is listed by <c>/api/commands</c>
        /// and executable through <c>/api/exec</c> under its full name, <c>group name</c>.
        ///
        /// Parameter metadata is read from the handler's own parameters exactly as it is for an
        /// attribute-declared command — a [CliArg] on a parameter supplies its CLI name,
        /// description, and required flag; a parameter without one falls back to its declared name
        /// and default value. Note that a lambda cannot carry [CliArg], so registering a named
        /// method is what yields full metadata.
        ///
        /// Registrations live until they are unregistered or the domain reloads; they survive
        /// <see cref="ClearCache"/> and <see cref="SetDiscovery"/>. One made while the Editor is in
        /// Play Mode is additionally dropped when Play Mode exits — see
        /// <see cref="PlayModeSessionEnded"/>.
        /// </summary>
        /// <param name="group">
        /// Group the command belongs to, as <c>.</c>-separated segments. Any group may be used,
        /// an existing one or a new one.
        /// </param>
        /// <param name="name">Name of the command within its group.</param>
        /// <param name="description">Human-readable description, shown in command listings.</param>
        /// <param name="handler">
        /// Method to invoke. An instance-method delegate keeps its receiver, so the command runs
        /// against that instance; a static one runs with no receiver.
        /// </param>
        /// <param name="mainThreadRequired">Whether the command must run on the Unity main thread.</param>
        /// <param name="tags">
        /// Optional path-style tags describing facets of the command, e.g.
        /// <see cref="CommandInfo.RuntimeTag"/> for the Player command surface.
        /// </param>
        /// <exception cref="ArgumentException">
        /// The group or the name breaks a format rule (see <see cref="FormatError"/>).
        /// </exception>
        /// <exception cref="InvalidOperationException">
        /// A command with the same full name is already registered. Attribute-declared commands can never
        /// be replaced; a dynamic one must be unregistered first.
        /// </exception>
        public static void RegisterCommand(string group, string name, string description, Delegate handler,
            bool mainThreadRequired = true, string[] tags = null)
        {
            if (group == null)
                throw new ArgumentNullException(nameof(group));
            if (name == null)
                throw new ArgumentNullException(nameof(name));
            if (description == null)
                throw new ArgumentNullException(nameof(description));
            if (handler == null)
                throw new ArgumentNullException(nameof(handler));
            var formatError = FormatError(group, name, out var badParameter);
            if (formatError != null)
                throw new ArgumentException(formatError, badParameter);

            var method = handler.Method;
            var command = new CommandInfo(
                name,
                description,
                mainThreadRequired,
                method,
                DiscoverParameters(method).ToList(),
                tags,
                method.DeclaringType?.Assembly.GetName().Name,
                handler.Target,
                group);

            lock (m_DiscoveryLock)
            {
                if (m_DynamicCommands.ContainsKey(command.FullName))
                {
                    throw new InvalidOperationException(
                        $"A command named '{command.FullName}' is already registered dynamically. " +
                        "Unregister it before registering it again.");
                }

                // Anything left matching here is attribute-declared: the dynamic table was just checked.
                if (TryGetCommand(command.FullName, out _))
                {
                    throw new InvalidOperationException(
                        $"A command named '{command.FullName}' is already declared with a [CliGroupCommand] attribute " +
                        "and cannot be replaced.");
                }

                m_DynamicCommands[command.FullName] = command;
                m_CachedAllCommands = null;
                m_CachedIndex = null;
            }
        }

        /// <summary>
        /// Remove a command previously registered with <see cref="RegisterCommand"/>.
        /// Attribute-declared commands are part of the code and are never removable.
        /// </summary>
        /// <param name="group">Group of the command, as it was registered.</param>
        /// <param name="name">Name of the command within its group, as it was registered.</param>
        /// <returns>
        /// True if a dynamic command was removed; false if no command is registered under that
        /// group and name dynamically, including when they belong to an attribute-declared command.
        /// </returns>
        public static bool UnregisterCommand(string group, string name)
        {
            if (group == null)
                throw new ArgumentNullException(nameof(group));
            if (name == null)
                throw new ArgumentNullException(nameof(name));

            lock (m_DiscoveryLock)
            {
                if (!m_DynamicCommands.Remove(CommandInfo.ComposeFullName(group, name)))
                    return false;

                m_CachedAllCommands = null;
                m_CachedIndex = null;
                return true;
            }
        }

        /// <summary>
        /// Note that a Play session is starting, so registrations made from here on can be told
        /// apart from the ones that predate it. Called by the Editor on Play Mode entry, before any
        /// user code runs.
        /// </summary>
        internal static void PlayModeSessionStarted()
        {
            lock (m_DiscoveryLock)
            {
                m_PrePlayModeCommands = new HashSet<string>(m_DynamicCommands.Keys, StringComparer.Ordinal);
            }
        }

        /// <summary>
        /// Drop every dynamic registration made during the Play session that just ended, the way a
        /// domain reload would have. Registrations that predate the session are left alone: nothing
        /// re-runs to restore them (an [InitializeOnLoadMethod] runs once per domain).
        ///
        /// A script registering from Awake or OnEnable re-registers on every Play Mode entry, so
        /// without this the second entry would throw "already registered dynamically" whenever no
        /// domain reload happened in between — and any surviving entry would still hold a delegate
        /// bound to the previous session's destroyed object. That covers Enter Play Mode with domain
        /// reload disabled on any Editor version, and on 6000.5+ the class-wide
        /// [NoAutoStaticsCleanup] means AutoStaticsCleanup does not step in either.
        /// </summary>
        internal static void PlayModeSessionEnded()
        {
            lock (m_DiscoveryLock)
            {
                // No recorded session start (e.g. a domain reload wiped it on the way in, having
                // already cleared the registry): nothing is known to belong to the session, so
                // removing anything would be guesswork.
                if (m_PrePlayModeCommands == null)
                    return;

                foreach (var fullName in m_DynamicCommands.Keys.Where(n => !m_PrePlayModeCommands.Contains(n)).ToList())
                    m_DynamicCommands.Remove(fullName);

                m_PrePlayModeCommands = null;
                m_CachedAllCommands = null;
                m_CachedIndex = null;
            }
        }

        /// <summary>
        /// Clear the discovered-command cache, so the next discovery re-scans for [CliGroupCommand] or [CliCommand]
        /// methods. Called automatically on domain reload, and whenever the discovery mechanism
        /// changes. Commands registered through <see cref="RegisterCommand"/> are not affected.
        /// </summary>
        internal static void ClearCache()
        {
            lock (m_DiscoveryLock)
            {
                m_CachedCommands = null;
                m_CachedAllCommands = null;
                m_CachedIndex = null;
            }
        }

        /// <summary>
        /// Internal implementation of command discovery using injected discovery mechanism.
        /// </summary>
        private static IEnumerable<CommandInfo> DiscoverCommandsInternal()
        {
            var commands = new List<CommandInfo>();

            try
            {
                // Use injected discovery mechanism if available, otherwise fallback to reflection
                IEnumerable<MethodInfo> methods;

                if (m_Discovery != null)
                {
                    methods = m_Discovery.GetMethodsWithAttribute<CliGroupCommandAttribute>()
                        .Union(m_Discovery.GetMethodsWithAttribute<CliCommandAttribute>());
                }
                else
                {
                    // Fallback: Use reflection to scan loaded assemblies
                    methods = GetMethodsWithAttributeViaReflection<CliGroupCommandAttribute>()
                        .Union(GetMethodsWithAttributeViaReflection<CliCommandAttribute>());
                }

                foreach (var method in methods)
                {
                    try
                    {
                        var commandInfo = CreateCommandInfo(method);
                        if (commandInfo != null)
                        {
                            commands.Add(commandInfo);
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning($"Failed to register command from method {method.DeclaringType?.Name}.{method.Name}: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"Failed to discover commands: {ex.Message}");
            }

            return commands;
        }

        /// <summary>
        /// Fallback command discovery using reflection when TypeCache is not available.
        /// </summary>
        private static IEnumerable<MethodInfo> GetMethodsWithAttributeViaReflection<T>() where T : Attribute
        {
            var methods = new List<MethodInfo>();

            try
            {
                var assemblies = PipelineUtils.GetLoadedAssemblies();

                foreach (var assembly in assemblies)
                {
                    // Skip system assemblies for performance
                    if (IsSystemAssembly(assembly))
                        continue;

                    try
                    {
                        foreach (var type in assembly.GetTypes())
                        {
                            foreach (var method in type.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
                            {
                                if (method.GetCustomAttribute<T>() != null)
                                {
                                    methods.Add(method);
                                }
                            }
                        }
                    }
                    catch (ReflectionTypeLoadException)
                    {
                        // Skip assemblies that can't be loaded
                        continue;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"Reflection-based command discovery failed: {ex.Message}");
            }

            return methods;
        }

        /// <summary>
        /// Check if assembly is a system assembly that should be skipped during discovery.
        /// </summary>
        private static bool IsSystemAssembly(Assembly assembly)
        {
            var name = assembly.GetName().Name;
            return name.StartsWith("System.") ||
                   name.StartsWith("Microsoft.") ||
                   name.StartsWith("mscorlib") ||
                   name.StartsWith("netstandard") ||
                   name.Equals("UnityEngine") ||
                   name.Equals("UnityEditor");
        }

        /// <summary>
        /// Create CommandInfo from a method with a CliGroupCommand or CliCommand attribute.
        /// </summary>
        private static CommandInfo CreateCommandInfo(MethodInfo method)
        {
            var grouped = method.GetCustomAttribute<CliGroupCommandAttribute>();
            var cliCommand = method.GetCustomAttribute<CliCommandAttribute>();
            if (grouped == null && cliCommand == null)
                return null;

            // Validate method is static (required for CLI commands).
            // Any static method may be registered regardless of accessibility
            // (public, internal, or private) — invocation goes through MethodInfo.Invoke.
            if (!method.IsStatic)
            {
                Debug.LogWarning($"Command method {method.DeclaringType?.Name}.{method.Name} must be static");
                return null;
            }

            var declarationError = ReadDeclaration(method, grouped, cliCommand, out var group, out var name);
            if (declarationError != null)
            {
                Debug.LogError($"Command {method.DeclaringType?.FullName}.{method.Name} is not registered: {declarationError}");
                return null;
            }

            // Discover parameters
            var parameters = DiscoverParameters(method).ToList();

            return new CommandInfo(
                name,
                grouped?.Description ?? cliCommand.Description,
                grouped?.MainThreadRequired ?? cliCommand.MainThreadRequired,
                grouped != null && method.IsDefined(typeof(ImmediateCommandAttribute), false),
                method,
                parameters,
                grouped != null ? grouped.Tags : cliCommand.Tags,
                method.DeclaringType?.Assembly.GetName().Name,
                group: group
            );
        }

        /// <summary>
        /// The group and name <paramref name="method"/> declares, or why it cannot be registered.
        /// <c>[CliGroupCommand]</c> names the group and derives the name from the method unless it
        /// sets one; <c>[CliCommand]</c> names both, or with only a description takes the group from
        /// the declaring type's namespace and class, lowercased, and derives the name from the method.
        /// A method carries one of the two.
        /// </summary>
        /// <returns>The error, or null when the declaration can be registered.</returns>
        internal static string ReadDeclaration(MethodInfo method, CliGroupCommandAttribute grouped, CliCommandAttribute cliCommand,
            out string group, out string name)
        {
            if (grouped != null && cliCommand != null)
            {
                group = name = null;
                return "it carries both [CliCommand] and [CliGroupCommand]; keep [CliGroupCommand].";
            }

            if (grouped != null)
            {
                group = grouped.Group;
                name = grouped.Name ?? DeriveName(method.Name);
                return FormatError(group, name);
            }

            group = cliCommand.Group ?? method.DeclaringType.FullName.Replace('+', '.').ToLowerInvariant();
            name = cliCommand.Name ?? DeriveName(method.Name);
            var error = FormatError(group, name);
            return error == null ? null
                : $"{error} To place the command in a group, declare it with [CliCommand(group, name, description)].";
        }

        /// <summary>
        /// The command name a method name derives: split at each capital and join with <c>_</c>,
        /// lowercased. An acronym is one word (<c>GetUIElement</c> becomes <c>get_ui_element</c>)
        /// and digits stay with the word before them (<c>Vector3Field</c> becomes <c>vector3_field</c>).
        /// </summary>
        internal static string DeriveName(string methodName)
        {
            var builder = new System.Text.StringBuilder(methodName.Length + 8);
            for (var i = 0; i < methodName.Length; i++)
            {
                var c = methodName[i];
                if (char.IsUpper(c) && i > 0)
                {
                    var previousIsUpper = char.IsUpper(methodName[i - 1]);
                    var nextIsLower = i + 1 < methodName.Length && char.IsLower(methodName[i + 1]);
                    // A new word starts at a capital, except inside an acronym, whose last capital
                    // starts the next word when a lowercase letter follows it (UIElement -> ui_element).
                    if (!previousIsUpper || nextIsLower)
                        builder.Append('_');
                }
                builder.Append(char.ToLowerInvariant(c));
            }
            return builder.ToString();
        }

        /// <summary>
        /// What makes a group and name unparseable, or null when both are fine. These are the
        /// only rules the registry enforces, and only because parsing needs them: a group is one
        /// or more non-empty <c>.</c>-separated segments without whitespace, and a name is
        /// non-empty, without whitespace and without <c>.</c>, so a full name splits
        /// unambiguously at its space and its one-word spelling at its last dot. Which groups, names and verbs to use is the command author's choice.
        /// </summary>
        internal static string FormatError(string group, string name)
        {
            return FormatError(group, name, out _);
        }

        /// <inheritdoc cref="FormatError(string, string)"/>
        /// <param name="parameter">Which argument broke a rule: <c>"group"</c> or <c>"name"</c>; null when both are fine.</param>
        internal static string FormatError(string group, string name, out string parameter)
        {
            parameter = "group";
            if (string.IsNullOrEmpty(group))
                return "the group is empty.";
            if (group.Any(char.IsWhiteSpace))
                return $"the group '{group}' contains whitespace.";
            if (group.Split('.').Any(segment => segment.Length == 0))
                return $"the group '{group}' has an empty segment.";
            parameter = "name";
            if (string.IsNullOrEmpty(name))
                return "the name is empty.";
            if (name.Any(char.IsWhiteSpace))
                return $"the name '{name}' contains whitespace.";
            if (name.Contains("."))
                return $"the name '{name}' contains a '.', which separates the group from the name.";
            parameter = null;
            return null;
        }

        /// <summary>
        /// Discover parameter information from method parameters.
        /// </summary>
        private static IEnumerable<CommandParameterInfo> DiscoverParameters(MethodInfo method)
        {
            foreach (var param in method.GetParameters())
            {
                var argAttr = param.GetCustomAttribute<CliArgAttribute>();

                // Parameters without CliArg attribute get default metadata
                var name = argAttr?.Name ?? param.Name;
                var description = argAttr?.Description ?? $"Parameter: {param.Name}";
                var required = argAttr?.Required ?? !param.HasDefaultValue;
                var defaultValue = param.HasDefaultValue ? param.DefaultValue : argAttr?.DefaultValue;

                yield return new CommandParameterInfo(name, description, required, param.ParameterType, defaultValue);
            }
        }
    }
}