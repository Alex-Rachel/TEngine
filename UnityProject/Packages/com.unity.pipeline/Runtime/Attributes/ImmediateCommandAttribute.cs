using System;

namespace Unity.Pipeline.Commands
{
    /// <summary>
    /// Marks a <see cref="CliGroupCommandAttribute"/> command that runs at once, beside whatever runs,
    /// rather than waiting its turn behind the commands before it. Only for a command that reads and
    /// changes nothing, such as a status, so a client can watch the Editor while another command
    /// holds it.
    /// </summary>
    /// <remarks>
    /// An attribute of its own rather than a property of <see cref="CliGroupCommandAttribute"/>: a
    /// named attribute argument must be public, and this stays internal so only the package decides
    /// which commands run outside the queue.
    /// </remarks>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
    internal sealed class ImmediateCommandAttribute : Attribute
    {
    }
}
