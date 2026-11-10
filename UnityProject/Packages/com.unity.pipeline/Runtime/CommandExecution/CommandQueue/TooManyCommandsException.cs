using System;

namespace Unity.Pipeline.Commands
{
    /// <summary>Too many commands wait or run to queue another; retrying later can succeed.</summary>
    sealed class TooManyCommandsException : Exception
    {
        /// <summary>The full name of the command that wasn't queued.</summary>
        public string Command { get; }

        /// <summary>How many commands may wait or run at once.</summary>
        public int Limit { get; }

        public TooManyCommandsException(string command, int limit, string message)
            : base(message)
        {
            Command = command;
            Limit = limit;
        }
    }
}
