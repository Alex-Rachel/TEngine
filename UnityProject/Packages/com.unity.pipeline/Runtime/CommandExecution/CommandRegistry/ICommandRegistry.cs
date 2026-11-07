using System;

namespace Unity.Pipeline.Commands
{
    /// <summary>
    /// The commands there are: those declared in code with <c>[CliGroupCommand]</c> and those registered
    /// at runtime. <see cref="CommandRegistry.GetRegistry"/> returns this domain's.
    /// </summary>
    /// <remarks>Thread-safe.</remarks>
    interface ICommandRegistry
    {
        /// <summary>
        /// A snapshot of every command, indexed by full name and group. Take it once per request
        /// and read everything from it, so a registration landing in between can't mix two states.
        /// </summary>
        public CommandIndex Index { get; }

        /// <summary>Registers a command at runtime, without a <c>[CliGroupCommand]</c> attribute.</summary>
        /// <inheritdoc cref="CommandRegistry.RegisterCommand"/>
        public void Register(string group, string name, string description, Delegate handler,
            bool mainThreadRequired, string[] tags);

        /// <summary>Removes a command registered with <see cref="Register"/>.</summary>
        /// <inheritdoc cref="CommandRegistry.UnregisterCommand"/>
        public bool Unregister(string group, string name);
    }
}
