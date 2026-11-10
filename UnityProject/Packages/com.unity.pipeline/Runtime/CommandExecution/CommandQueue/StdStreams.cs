using System;
using System.IO;

namespace Unity.Pipeline.Commands
{
    /// <summary>
    /// A command's standard streams, as a process has them: what the client sends it, where its
    /// output goes, and where its errors go.
    /// </summary>
    /// <remarks>
    /// Out takes output that isn't a result, such as text the command lays out itself. A result is
    /// written through <see cref="CommandContext.Result"/>, never both in one command.
    /// </remarks>
    sealed class StdStreams
    {
        /// <param name="request">The request whose streams these are.</param>
        public StdStreams(CommandRequest request)
        {
            if (request == null)
                throw new ArgumentNullException(nameof(request));

            In = request.In;
            Out = request.Out;
            Error = request.Error;
            OutIsTerminal = request.OutIsTerminal;
        }

        /// <summary>What the client sends the command.</summary>
        public TextReader In { get; }

        /// <summary>Takes the command's output.</summary>
        public TextWriter Out { get; }

        /// <summary>Takes why the command failed.</summary>
        public TextWriter Error { get; }

        /// <summary>Whether the client's Out is a terminal, so output may be colored.</summary>
        public bool OutIsTerminal { get; }
    }
}
