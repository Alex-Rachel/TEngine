using System;

namespace Unity.Pipeline.Commands
{
    /// <summary>
    /// An argument of a command the executor found can't convert to its parameter's type, or a
    /// required one is missing.
    /// </summary>
    sealed class ParameterValidationException : ArgumentException
    {
        /// <summary>The full name of the command whose arguments were refused.</summary>
        public string Command { get; }

        public ParameterValidationException(string command, string message)
            : base(message)
        {
            Command = command;
        }
    }
}
