namespace Unity.Pipeline.Commands
{
    /// <summary>
    /// A command's outcome, as the exit code the Pipeline protocol's exit frame carries. The codes
    /// are the unity CLI's own exit codes, which the CLI exits with as they are, so their numbers and
    /// meanings are those of the CLI's exit-code table: a new code is a row there first. What the
    /// command produced goes to the request's Out, and why it failed to its Error, never into its result.
    /// </summary>
    enum ExitCode
    {
        Success = 0,
        /// <summary>
        /// The request is wrong: no command, arguments that don't fit the command, as the server
        /// found or the command once it ran, or a request value the server can't read.
        /// </summary>
        InvalidUsage = 2,
        Failed = 6,
        NotFound = 13,
        /// <summary>The request's timeout ran out, while it waited or while it ran.</summary>
        TimedOut = 14,
        /// <summary>
        /// Can't run now: settling, compiling, reloading, Safe Mode, or too many commands wait or
        /// run. Retrying later can succeed.
        /// </summary>
        Unavailable = 17,
        /// <summary>
        /// Can't run now: a modal dialog holds the main thread. Apart from <see cref="Unavailable"/>
        /// because it doesn't clear on its own: someone has to dismiss the dialog.
        /// </summary>
        BlockedByDialog = 18,
        Cancelled = 130
    }
}
