using System.Collections.Generic;

namespace Unity.Pipeline.Commands
{
    /// <summary>
    /// Runs commands with their bound arguments one at a time, in arrival order (EXEC-001), each
    /// on the main thread when it needs it; whoever queued one holds its execution. Doesn't decide
    /// whether a command may run. An immediate command (<see cref="CommandInfo.Immediate"/>) waits
    /// for none of them and runs at once.
    /// </summary>
    /// <remarks>
    /// Thread-safe. What a command returns is written to its request's Out, as JSON shaped by the
    /// executor's options among its arguments, and why it failed to its Error.
    /// </remarks>
    interface ICommandQueue
    {
        /// <summary>
        /// Queues <paramref name="command"/> with <paramref name="arguments"/> to run once every
        /// execution before it has ended, or at once for an immediate command, and returns its
        /// execution at once.
        /// </summary>
        /// <param name="parameters">The arguments as the executor bound them, by parameter name.</param>
        /// <param name="arguments">One value per parameter of the command, converted to its type.</param>
        /// <exception cref="TooManyCommandsException">Too many commands wait or run.</exception>
        public CommandExecution Enqueue(CommandInfo command, IReadOnlyDictionary<string, object> parameters,
            IReadOnlyList<object> arguments, CommandRequest request);
    }
}
