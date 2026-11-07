using System;
using System.Collections.Generic;
using Unity.Pipeline.Models;

namespace Unity.Pipeline.Commands
{
    /// <summary>
    /// A request's arguments don't fit its command's parameters: one problem per argument, all of
    /// them, so a client fixes them at once.
    /// </summary>
    sealed class CommandArgumentsException : Exception
    {
        /// <summary>The full name of the command the arguments were for.</summary>
        public string Command => CommandInfo.FullName;

        /// <summary>What is wrong with the arguments.</summary>
        public IReadOnlyList<ArgProblem> Problems { get; }

        /// <summary>The command the arguments were for.</summary>
        internal CommandInfo CommandInfo { get; }

        public CommandArgumentsException(CommandInfo command, IReadOnlyList<ArgProblem> problems, string message)
            : base(message)
        {
            CommandInfo = command ?? throw new ArgumentNullException(nameof(command));
            Problems = problems;
        }
    }
}
