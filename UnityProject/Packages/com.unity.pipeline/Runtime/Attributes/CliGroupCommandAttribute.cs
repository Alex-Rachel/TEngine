using System;

namespace Unity.Pipeline.Commands
{
    /// <summary>
    /// Attribute to mark static methods as CLI-accessible commands.
    /// Commands with this attribute can be executed remotely via Pipeline Server.
    /// Adapted from unity-tools [Tool] attribute for Pipeline organization requirements.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
    public class CliGroupCommandAttribute : Attribute
    {
        /// <summary>
        /// Group the command belongs to, as <c>.</c>-separated segments (e.g. <c>animation.animator</c>).
        /// The command is invoked as <c>unity command &lt;group&gt; &lt;name&gt;</c>; that
        /// <c>group name</c> is its full name.
        /// </summary>
        public string Group { get; }

        /// <summary>
        /// Name of the command within its <see cref="Group"/>. Optional: when null it is derived
        /// from the method name (<c>CreateController</c> becomes <c>create_controller</c>). Set it
        /// only when the derived name is wrong for the command.
        /// </summary>
        public string Name { get; set; }

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
        /// Create a new CLI command attribute.
        /// </summary>
        /// <param name="group">Group the command belongs to</param>
        /// <param name="description">Human-readable description</param>
        public CliGroupCommandAttribute(string group, string description)
        {
            Group = group ?? throw new ArgumentNullException(nameof(group));
            Description = description ?? throw new ArgumentNullException(nameof(description));
        }
    }
}
