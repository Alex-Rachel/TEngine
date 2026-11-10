using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Reflection;

namespace Unity.Pipeline.Commands
{
    /// <summary>
    /// Information about a discovered CLI command.
    /// Contains metadata needed for command execution and CLI help generation.
    /// </summary>
    public class CommandInfo
    {
        /// <summary>
        /// Name of the command within its <see cref="Group"/>.
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// Group the command belongs to, as <c>.</c>-separated segments (e.g. <c>animation.animator</c>),
        /// or null for a command without one.
        /// </summary>
        public string Group { get; }

        /// <summary>
        /// The command as a user types and reads it: <c>group name</c> (<c>editor status</c>), or
        /// just the name when the command has no group. It is the one name of a command: the
        /// registry resolves by it, and every message, hint, help text and payload that names a
        /// command shows it.
        /// </summary>
        public string FullName { get; }

        /// <summary>
        /// Human-readable description of the command.
        /// </summary>
        public string Description { get; }

        /// <summary>
        /// Whether this command requires Unity main thread execution.
        /// </summary>
        public bool MainThreadRequired { get; }

        /// <summary>
        /// Whether the command runs at once, beside whatever runs, rather than waiting its turn in
        /// the queue: a command that only reads, such as a status.
        /// </summary>
        internal bool Immediate { get; }

        /// <summary>
        /// Hierarchical tags used to group and browse commands (path-style, '/'-separated).
        /// Empty for untagged commands; never null.
        /// </summary>
        public IReadOnlyList<string> Tags { get; }

        /// <summary>
        /// Name of the assembly this command originates from, used to group and filter
        /// commands by contributing package/source.
        /// </summary>
        public string Package { get; }

        /// <summary>
        /// Method that implements this command.
        /// </summary>
        public MethodInfo Method { get; } // TODO Maybe look at generating a dynamic Delegate to increase performance of the command call.

        /// <summary>
        /// Parameters that this command accepts.
        /// </summary>
        public IReadOnlyList<CommandParameterInfo> Parameters { get; }

        /// <summary>
        /// Instance to invoke <see cref="Method"/> on, or null when <see cref="Method"/> is static.
        /// Commands declared with a [CliGroupCommand] attribute are always static and leave this null;
        /// a dynamically registered command bound to an instance method carries its receiver here.
        /// </summary>
        public object Target { get; }

        /// <summary>
        /// Create command information from discovery.
        /// </summary>
        /// <param name="name">Name of the command within its group.</param>
        /// <param name="description">Human-readable description of the command.</param>
        /// <param name="mainThreadRequired">Whether this command requires Unity main thread execution.</param>
        /// <param name="method">Method that implements this command.</param>
        /// <param name="parameters">Parameters that this command accepts.</param>
        /// <param name="tags">Hierarchical tags used to group and browse commands.</param>
        /// <param name="package">Name of the assembly this command originates from.</param>
        /// <param name="target">
        /// Instance to invoke <paramref name="method"/> on, or null when it is static. Only a
        /// dynamically registered command bound to an instance method supplies one.
        /// </param>
        /// <param name="group">Group the command belongs to, or null for none.</param>
        public CommandInfo(string name, string description, bool mainThreadRequired,
            MethodInfo method, IReadOnlyList<CommandParameterInfo> parameters,
            IReadOnlyList<string> tags = null, string package = null, object target = null, string group = null)
            : this(name, description, mainThreadRequired, false, method, parameters, tags, package, target, group)
        {
        }

        /// <inheritdoc cref="CommandInfo(string, string, bool, MethodInfo, IReadOnlyList{CommandParameterInfo}, IReadOnlyList{string}, string, object, string)"/>
        /// <param name="immediate">Whether the command runs at once, outside the queue.</param>
        internal CommandInfo(string name, string description, bool mainThreadRequired, bool immediate,
            MethodInfo method, IReadOnlyList<CommandParameterInfo> parameters,
            IReadOnlyList<string> tags = null, string package = null, object target = null, string group = null)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            Group = group;
            FullName = ComposeFullName(group, name);
            Description = description ?? throw new ArgumentNullException(nameof(description));
            Method = method ?? throw new ArgumentNullException(nameof(method));
            Parameters = parameters ?? throw new ArgumentNullException(nameof(parameters));
            MainThreadRequired = mainThreadRequired;
            Immediate = immediate;
            Tags = tags ?? Array.Empty<string>();
            Package = package;
            Target = target;
        }

        /// <summary>
        /// The full name of the command called <paramref name="name"/> in <paramref name="group"/>,
        /// as a user types it: <c>group name</c>, or <paramref name="name"/> alone when there is no
        /// group.
        /// </summary>
        internal static string ComposeFullName(string group, string name)
        {
            return group == null ? name : group + " " + name;
        }

        /// <summary>
        /// The one-word spelling of a full name: the single space before the name becomes a dot
        /// (<c>animation.animator add_state</c> becomes <c>animation.animator.add_state</c>).
        /// Only for the places that need a single token without spaces; it is accepted when a
        /// command is named but never shown. A name holds no space, so the last space is the one
        /// that separates it. Returns <paramref name="fullName"/> unchanged when it holds none.
        /// </summary>
        internal static string GetIdFromName(string fullName)
        {
            var space = fullName.LastIndexOf(' ');
            return space < 0 ? fullName : fullName.Substring(0, space) + "." + fullName.Substring(space + 1);
        }

        /// <summary>
        /// Whether <paramref name="candidate"/> is <paramref name="group"/> or one of its subgroups,
        /// segment-aware: <c>build</c> covers <c>build</c> and <c>build.target</c> but not <c>buildx</c>.
        /// </summary>
        internal static bool IsInGroupSubtree(string candidate, string group)
        {
            return candidate != null && group != null
                && (candidate == group || candidate.StartsWith(group + ".", StringComparison.Ordinal));
        }

        /// <summary>
        /// The group directly above <paramref name="group"/> (<c>animation</c> for
        /// <c>animation.clip</c>), or null for a top-level group.
        /// </summary>
        internal static string ParentGroup(string group)
        {
            var dot = group.LastIndexOf('.');
            return dot < 0 ? null : group.Substring(0, dot);
        }

        /// <summary>
        /// The group of the commands declared with <c>[CliCommand(name, description)]</c>. A command
        /// in it is also found by its name alone.
        /// </summary>
        public const string GlobalGroup = "global";

        /// <summary>
        /// The tag marking the runtime (Player) command surface.
        /// </summary>
        public const string RuntimeTag = "runtime";

        /// <summary>
        /// Whether this command belongs to the runtime (Player) command surface — that is,
        /// whether it carries the <see cref="RuntimeTag"/> tag or one beneath it. Derived from
        /// <see cref="Tags"/>, so the tag is the single source of truth.
        /// </summary>
        public bool RuntimeOnly => HasTagInSubtree(RuntimeTag);

        /// <summary>
        /// Whether the command carries <paramref name="tag"/> or a tag beneath it. The match is
        /// segment-aware: 'assets' matches 'assets' and 'assets/import' but not 'assetsx'. A
        /// null or empty tag matches every command.
        /// </summary>
        /// <param name="tag">Path-style tag to test, e.g. "assets" or "assets/import".</param>
        /// <returns>True when the command sits in that tag's subtree.</returns>
        public bool HasTagInSubtree(string tag)
        {
            if (string.IsNullOrEmpty(tag))
                return true;
            foreach (var candidate in Tags)
            {
                if (candidate != null
                    && (candidate.Equals(tag, StringComparison.OrdinalIgnoreCase)
                        || candidate.StartsWith(tag + "/", StringComparison.OrdinalIgnoreCase)))
                    return true;
            }
            return false;
        }

        /// <summary>A short diagnostic summary of the command.</summary>
        /// <returns>The summary string.</returns>
        public override string ToString()
        {
            return $"{FullName} MainThreadRequired:{MainThreadRequired} Parameters:{Parameters.Count}";
        }
    }
}