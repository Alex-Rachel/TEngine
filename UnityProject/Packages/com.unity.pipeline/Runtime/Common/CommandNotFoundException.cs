using System;

namespace Unity.Pipeline
{
    /// <summary>
    /// Thrown when a request names a command the registry does not hold. /api/exec answers it
    /// with "Command Not Found"; any other <see cref="InvalidOperationException"/> is a command
    /// that ran and failed. It derives from <see cref="InvalidOperationException"/> so a caller
    /// that only catches that type keeps seeing it.
    /// </summary>
    internal sealed class CommandNotFoundException : InvalidOperationException
    {
        /// <summary>The name that matched no registered command.</summary>
        public string CommandName { get; }

        public CommandNotFoundException(string commandName, string message)
            : base(message)
        {
            CommandName = commandName;
        }
    }
}
