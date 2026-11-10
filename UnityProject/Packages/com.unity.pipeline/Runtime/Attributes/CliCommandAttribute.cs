using System;

namespace Unity.Pipeline.Commands
{
    /// <summary>
    /// Marks a static method as a CLI command, invoked as <c>unity command &lt;group&gt; &lt;name&gt;</c>.
    /// The group and name are given separately, as one dotted name, or inferred from the method.
    /// A command in the <c>global</c> group is also invoked by its name alone (<c>unity command &lt;name&gt;</c>).
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
    public class CliCommandAttribute : Attribute
    {
        /// <summary>
        /// Group the command belongs to, as <c>.</c>-separated segments (e.g. <c>animation.animator</c>),
        /// <c>global</c> for an undotted name, or null when the group is inferred from the method's
        /// namespace and class.
        /// </summary>
        public string Group { get; }

        /// <summary>
        /// Name of the command within its <see cref="Group"/>, or null when it is derived from the
        /// method name.
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// Human-readable description of what the command does.
        /// Used in CLI help text and command discovery.
        /// </summary>
        public string Description { get; }

        /// <summary>
        /// Whether this command requires Unity main thread execution.
        /// Default: true (most Unity APIs require main thread).
        /// </summary>
        public bool MainThreadRequired { get; set; } = true;

        /// <summary>
        /// Optional hierarchical tags used to browse commands across groups (e.g. "runtime" for the
        /// Player command surface). Path-style: a '/' separates tag from subtag (e.g. "assets/import").
        /// Default: empty (untagged).
        /// </summary>
        public string[] Tags { get; set; } = Array.Empty<string>();

        /// <summary>
        /// Create a CLI command attribute for a command in <paramref name="group"/>.
        /// </summary>
        /// <param name="group">Group the command belongs to</param>
        /// <param name="name">Name of the command within its group</param>
        /// <param name="description">Human-readable description</param>
        /// <example>
        /// <para>A command invoked as <c>unity command lighting bake</c>:</para>
        /// <code>[CliCommand("lighting", "bake", "Bake every lightmap")]</code>
        /// </example>
        public CliCommandAttribute(string group, string name, string description)
        {
            Group = group ?? throw new ArgumentNullException(nameof(group));
            Name = name ?? throw new ArgumentNullException(nameof(name));
            Description = description ?? throw new ArgumentNullException(nameof(description));
        }

        /// <summary>
        /// Create a CLI command attribute from a full name. The text before the last <c>.</c> is the
        /// group and the rest is the name; a name without a <c>.</c> is in the <c>global</c> group.
        /// </summary>
        /// <param name="name">The command's name, optionally prefixed with its group</param>
        /// <param name="description">Human-readable description</param>
        /// <example>
        /// <para>A command invoked as <c>unity command animation.skeleton set_bone</c>:</para>
        /// <code>[CliCommand("animation.skeleton.set_bone", "Set a bone")]</code>
        /// </example>
        public CliCommandAttribute(string name, string description)
            : this(GroupOf(name), NameOf(name), description)
        {
        }

        /// <summary>
        /// Create a CLI command attribute whose group and name are inferred from the method: the group
        /// is the declaring type's namespace and class, lowercased, and the name is derived from the
        /// method name (<c>CreateController</c> becomes <c>create_controller</c>).
        /// </summary>
        /// <param name="description">Human-readable description</param>
        /// <example>
        /// <para>On <c>Bake()</c> in <c>My.Tools.Lighting</c>, a command invoked as <c>unity command my.tools.lighting bake</c>:</para>
        /// <code>[CliCommand("Bake every lightmap")]</code>
        /// </example>
        public CliCommandAttribute(string description)
        {
            Description = description ?? throw new ArgumentNullException(nameof(description));
        }

        static string GroupOf(string name)
        {
            var lastDot = name?.LastIndexOf('.') ?? -1;
            return lastDot < 0 ? "global" : name.Substring(0, lastDot);
        }

        static string NameOf(string name)
        {
            var lastDot = name?.LastIndexOf('.') ?? -1;
            return lastDot < 0 ? name : name.Substring(lastDot + 1);
        }
    }
}
