using System;

namespace Unity.Pipeline.Scripting
{
    /// <summary>
    /// Thrown by <see cref="Wait"/> when what it waits for does not happen in time. The message says
    /// which wait timed out, after how long, and why it waited when the caller gave a reason.
    /// </summary>
    public sealed class WaitTimeoutException : TimeoutException
    {
        /// <summary>Creates the exception with the message describing the wait.</summary>
        /// <param name="message">Which wait timed out, and why it waited.</param>
        public WaitTimeoutException(string message)
            : base(message)
        {
        }
    }
}
