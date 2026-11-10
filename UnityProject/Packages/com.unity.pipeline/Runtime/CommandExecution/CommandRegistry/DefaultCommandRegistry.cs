using System;

namespace Unity.Pipeline.Commands
{
    /// <summary>
    /// <see cref="ICommandRegistry"/> over this domain's <see cref="CommandRegistry"/>, which holds
    /// the commands. Get it from <see cref="CommandRegistry.GetRegistry"/>.
    /// </summary>
    class DefaultCommandRegistry : ICommandRegistry
    {
        /// <inheritdoc/>
        public CommandIndex Index => CommandRegistry.Index;

        /// <inheritdoc/>
        public void Register(string group, string name, string description, Delegate handler,
            bool mainThreadRequired, string[] tags)
        {
            CommandRegistry.RegisterCommand(group, name, description, handler, mainThreadRequired, tags);
        }

        /// <inheritdoc/>
        public bool Unregister(string group, string name)
        {
            return CommandRegistry.UnregisterCommand(group, name);
        }
    }
}
