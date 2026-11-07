namespace Unity.Pipeline.Commands
{
    /// <summary>
    /// Runs the commands clients request, independent of transport. A request that can't run
    /// throws before anything runs: Execute finds the command, binds its arguments and refuses
    /// one that can't run now before it returns, so a refusal throws from the call itself, and
    /// no execution exists.
    /// </summary>
    /// <remarks>
    /// Thread-safe, and every member answers at once from any thread, even while a modal dialog
    /// blocks the main thread. What a command produced goes to the request's Out, and why it
    /// failed to its Error; its result says only how it ended.
    /// </remarks>
    interface ICommandExecutor
    {
        /// <summary>
        /// Queues the request's command to run once the commands before it have ended, or runs it
        /// at once when it is immediate, and returns its execution at once. Awaiting the execution completes with how it ended, on
        /// the kind of thread that awaits, main or background; a command that needs the main
        /// thread moves there itself.
        /// </summary>
        /// <exception cref="CommandNotFoundException">The request names no command.</exception>
        /// <exception cref="CommandArgumentsException">Its arguments don't bind to the command's parameters.</exception>
        /// <exception cref="System.ArgumentException">
        /// A required argument is missing, or one can't convert to its parameter's type: a
        /// <see cref="ParameterValidationException"/>, naming the command, once the command was found.
        /// </exception>
        /// <exception cref="CommandUnavailableException">The command can't run now.</exception>
        /// <exception cref="TooManyCommandsException">Too many commands wait or run.</exception>
        public CommandExecution Execute(CommandRequest request);
    }
}
