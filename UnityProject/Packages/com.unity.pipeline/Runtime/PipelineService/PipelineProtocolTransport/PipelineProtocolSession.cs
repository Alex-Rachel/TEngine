using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Unity.Pipeline.Commands;
using Unity.Pipeline.Security;
using UnityEngine;
using FrameType = Unity.Pipeline.PipelineProtocol.FrameType;

namespace Unity.Pipeline
{
    /// <summary>
    /// Serves one connection in the Pipeline protocol (see <see cref="PipelineProtocol"/>): checks
    /// the signature and the token, runs the request's command through the executor, streams its
    /// in, out and err as frames, and ends with its exit code. Closes the connection without an
    /// answer on anything that isn't the protocol or has the wrong token.
    /// </summary>
    sealed class PipelineProtocolSession
    {
        /// <summary>
        /// How long a client has to send its signature, token and request: a connection that sends
        /// nothing must not stay open.
        /// </summary>
        static readonly TimeSpan s_HandshakeTimeout = TimeSpan.FromSeconds(5);

        /// <summary>
        /// The largest Request body it reads and discards to say it is too large, rather than closing:
        /// as much as the HTTP API's exec accepts.
        /// </summary>
        internal const long MaxSkippedRequestSize = 32L * 1024 * 1024;

        /// <summary>How often the running command's progress is read, and sent when it changed.</summary>
        static readonly TimeSpan s_ProgressInterval = TimeSpan.FromMilliseconds(250);

        /// <summary>How long it waits after Exit for the client to close its end, before closing anyway.</summary>
        static readonly TimeSpan s_ClientCloseTimeout = TimeSpan.FromSeconds(1);

        // The Metadata keys the session reads.
        const string ClientSourceKey = "client.source";
        const string ClientNameKey = "client.name";
        const string ClientInvocationKey = "client.invocation";
        const string ClientCallerKey = "client.caller";
        const string ClientSkillKey = "client.skill";
        const string TimeoutKey = "timeout";
        const string StdoutTtyKey = "stdout.tty";
        const string TerminalWidthKey = "terminal.width";

        readonly Socket m_Socket;
        readonly Stream m_Stream;
        readonly ICommandExecutor m_Executor;
        readonly Func<string> m_GetToken;
        readonly IReadOnlyDictionary<string, string> m_ObsoleteCommandNames;

        PipelineProtocolSession(Socket socket, ICommandExecutor executor, Func<string> getToken,
            IReadOnlyDictionary<string, string> obsoleteCommandNames)
        {
            m_Socket = socket;
            m_Stream = new NetworkStream(socket, ownsSocket: true);
            m_Executor = executor;
            m_GetToken = getToken;
            m_ObsoleteCommandNames = obsoleteCommandNames;
        }

        /// <summary>
        /// Serves <paramref name="socket"/> until its command ends or the client goes away, and closes
        /// it. Never throws.
        /// </summary>
        /// <param name="getToken">The token clients must send; read once per connection.</param>
        /// <param name="obsoleteCommandNames">
        /// Old command name to the full name that replaced it, for a request naming one; null for none.
        /// </param>
        /// <param name="stopping">
        /// Closes the connection when the transport stops, which cancels its command.
        /// </param>
        public static async Task ServeAsync(Socket socket, ICommandExecutor executor, Func<string> getToken,
            IReadOnlyDictionary<string, string> obsoleteCommandNames, CancellationToken stopping)
        {
            try
            {
                // Inside the try: the stream throws for a socket the client already reset.
                var session = new PipelineProtocolSession(socket, executor, getToken, obsoleteCommandNames);

                // NetworkStream doesn't honour cancellation on every runtime; closing the socket ends
                // any read or write in flight.
                using (stopping.Register(session.Close))
                    await session.ServeAsync();
            }
            catch (Exception ex) when (IsDisconnect(ex))
            {
                // The client went away or broke the protocol; there is no one left to answer.
            }
            catch (Exception ex)
            {
                Debug.LogError($"Pipeline: The Pipeline protocol transport failed serving a connection: {ex}");
            }
            finally
            {
                // The socket rather than the session, which may not exist.
                Close(socket);
            }
        }

        async Task ServeAsync()
        {
            Handshake request;
            using (var handshake = new CancellationTokenSource(s_HandshakeTimeout))
            using (handshake.Token.Register(Close))
            {
                request = await HandshakeAsync();
            }

            if (request == null)
                return;

            if (request.SkippedRequestSize > 0)
            {
                await RefuseAsync(ExitCode.InvalidUsage, DescribeTooLarge(request.SkippedRequestSize));
                return;
            }

            await RunAsync(request.CommandLine, request.Metadata);
        }

        /// <summary>What the handshake read: a request to run, or one too large to read.</summary>
        sealed class Handshake
        {
            public List<string> CommandLine;

            /// <summary>Empty when the client sent none.</summary>
            public Dictionary<string, string> Metadata;

            /// <summary>
            /// The size of a Request body larger than a frame, which was read and discarded so the
            /// client can read why; 0 otherwise.
            /// </summary>
            public long SkippedRequestSize;
        }

        /// <returns>What the client asked for; null when the connection should close unanswered.</returns>
        async Task<Handshake> HandshakeAsync()
        {
            var signature = await PipelineProtocol.ReadSignatureAsync(m_Stream, CancellationToken.None);
            if (!PipelineProtocol.TryParseSignature(signature, out var clientVersion))
                return null;

            // Sent before reading the token: a client may wait for it to check the version first.
            var own = PipelineProtocol.CreateSignature();
            await m_Stream.WriteAsync(own, 0, own.Length);
            if (clientVersion != PipelineProtocol.Version)
                return null;

            var auth = await PipelineProtocol.ReadFrameAsync(m_Stream, PipelineProtocol.MaxTokenSize, CancellationToken.None);
            if (auth?.Type != FrameType.Auth
                || !PipelineProtocol.TryDecodeToken(auth.Value.Body, out var token)
                || !SecurityTokenManager.ConstantTimeEquals(token, m_GetToken()))
                return null;

            // Headers first: a Request too large for a frame is answered rather than closed.
            var metadata = new Dictionary<string, string>(StringComparer.Ordinal);
            var next = await PipelineProtocol.ReadFrameHeaderAsync(m_Stream, CancellationToken.None);
            if (next?.Type == FrameType.Metadata)
            {
                if (next.Value.BodySize > PipelineProtocol.MaxMetadataSize)
                    return null;
                var body = await PipelineProtocol.ReadFrameBodyAsync(m_Stream, (int)next.Value.BodySize, CancellationToken.None);
                if (!PipelineProtocol.TryDecodeKeyValues(body, out metadata))
                    return null;
                next = await PipelineProtocol.ReadFrameHeaderAsync(m_Stream, CancellationToken.None);
            }

            if (next?.Type != FrameType.Request)
                return null;

            var requestSize = next.Value.BodySize;
            if (requestSize > PipelineProtocol.MaxBodySize)
            {
                // A command line too long, not another protocol: read past it, so the answer isn't
                // lost to a reset from closing on unread bytes. Past the bound it isn't a command line.
                if (requestSize > MaxSkippedRequestSize)
                    return null;
                await PipelineProtocol.SkipFrameBodyAsync(m_Stream, requestSize, CancellationToken.None);
                return new Handshake { SkippedRequestSize = requestSize };
            }

            var request = await PipelineProtocol.ReadFrameBodyAsync(m_Stream, (int)requestSize, CancellationToken.None);
            if (!PipelineProtocol.TryDecodeRequest(request, out var commandLine))
                return null;

            return new Handshake { CommandLine = commandLine, Metadata = metadata };
        }

        /// <summary>Why a Request too large for a frame wasn't run, for err.</summary>
        static string DescribeTooLarge(long requestSize)
        {
            const double mebibyte = 1024 * 1024;
            return string.Format(CultureInfo.InvariantCulture,
                "The command line is {0:0.##} MiB; a request can carry at most {1:0.##} MiB.",
                requestSize / mebibyte, PipelineProtocol.MaxBodySize / mebibyte);
        }

        async Task RunAsync(List<string> commandLine, Dictionary<string, string> metadata)
        {
            if (commandLine.Count == 0 || string.IsNullOrWhiteSpace(commandLine[0]))
            {
                await RefuseAsync(ExitCode.InvalidUsage, "No command given.");
                return;
            }

            if (!TryReadMetadata(metadata, out var timeout, out var requester, out var outIsTerminal, out var terminalWidth,
                    out var metadataError))
            {
                await RefuseAsync(ExitCode.InvalidUsage, metadataError);
                return;
            }

            var output = new FrameOutput();
            var input = new FrameInput();
            var request = new CommandRequest(commandLine[0], commandLine.GetRange(1, commandLine.Count - 1), input,
                output.Out, output.Error)
            {
                Timeout = timeout,
                Requester = requester,
                FormatOutput = outIsTerminal.HasValue,
                OutIsTerminal = outIsTerminal == true,
                TerminalWidth = terminalWidth,
            };

            CommandExecution execution;
            try
            {
                execution = m_Executor.Execute(request);
            }
            catch (Exception ex)
            {
                await RefuseAsync(RefusalResult(ex), DescribeRefusal(ex));
                return;
            }

            var sending = output.SendAsync(m_Stream, CancellationToken.None);
            var receiving = ReceiveAsync(input, execution);

            ExitCode result;
            using (var ended = new CancellationTokenSource())
            // A read of In doesn't check for cancellation, so a command blocked in one, even on the
            // main thread, would never see a signal: ending the input lets the read return.
            using (execution.CancellationToken.Register(input.End))
            {
                var reporting = ReportProgressAsync(execution, output, ended.Token);
                try
                {
                    result = await execution;
                }
                finally
                {
                    ended.Cancel();
                    await reporting;
                    // A command abandoned past its timeout may still be reading.
                    input.End();
                    output.Complete();
                    // Awaited even when the command faults, so a failed send isn't left unobserved.
                    await sending;
                }
            }

            await PipelineProtocol.WriteFrameAsync(m_Stream, FrameType.Exit,
                PipelineProtocol.EncodeExitCode((int)result), CancellationToken.None);

            await EndAsync(receiving);
        }

        /// <summary>
        /// Ends the connection once Exit is written. Closing the socket with the client's frames
        /// unread resets it on Windows, discarding what the client hasn't read yet, Exit included.
        /// Ends the sending side instead, and lets <paramref name="receiving"/> see the client close
        /// before the socket does.
        /// </summary>
        async Task EndAsync(Task receiving)
        {
            m_Socket.Shutdown(SocketShutdown.Send);
            await Task.WhenAny(receiving, Task.Delay(s_ClientCloseTimeout));
        }

        /// <summary>
        /// Reads the Metadata keys the session uses, all optional: who sent the request, how long
        /// its command may take, and where the client shows its output.
        /// </summary>
        /// <param name="outIsTerminal">Whether the client's stdout is a terminal; null when not sent.</param>
        /// <param name="terminalWidth">The client's terminal width in columns; null when not sent.</param>
        /// <param name="error">Why a value can't be read, for the client; null otherwise.</param>
        /// <returns>False when a value can't be read.</returns>
        static bool TryReadMetadata(Dictionary<string, string> metadata, out TimeSpan? timeout, out Requester requester,
            out bool? outIsTerminal, out int? terminalWidth, out string error)
        {
            timeout = null;
            requester = default;
            outIsTerminal = null;
            terminalWidth = null;
            error = null;

            if (metadata.TryGetValue(StdoutTtyKey, out var ttyText))
            {
                if (ttyText != "0" && ttyText != "1")
                {
                    error = $"The request's {StdoutTtyKey} must be 1 or 0; got '{ttyText}'.";
                    return false;
                }
                outIsTerminal = ttyText == "1";
            }

            if (metadata.TryGetValue(TerminalWidthKey, out var widthText))
            {
                if (!int.TryParse(widthText, NumberStyles.None, CultureInfo.InvariantCulture, out var width) || width <= 0)
                {
                    error = $"The request's {TerminalWidthKey} must be a positive whole number of columns; got '{widthText}'.";
                    return false;
                }
                terminalWidth = width;
            }

            if (metadata.TryGetValue(TimeoutKey, out var timeoutText))
            {
                // Milliseconds, as the HTTP API's timeout; one past its range is unbounded there too.
                if (!long.TryParse(timeoutText, NumberStyles.None, CultureInfo.InvariantCulture, out var milliseconds)
                    || milliseconds <= 0)
                {
                    error = $"The request's {TimeoutKey} must be a positive whole number of milliseconds; got '{timeoutText}'.";
                    return false;
                }
                timeout = PipelineTimeout.FromRequest((int)Math.Min(milliseconds, PipelineTimeout.UnboundedRequestMs));
            }

            long? invocation = null;
            if (metadata.TryGetValue(ClientInvocationKey, out var invocationText))
            {
                if (!long.TryParse(invocationText, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var id))
                {
                    error = $"The request's {ClientInvocationKey} must be a whole number; got '{invocationText}'.";
                    return false;
                }
                invocation = id;
            }

            metadata.TryGetValue(ClientSourceKey, out var source);
            metadata.TryGetValue(ClientNameKey, out var name);
            metadata.TryGetValue(ClientCallerKey, out var caller);
            metadata.TryGetValue(ClientSkillKey, out var skill);
            requester = new Requester(source, name, invocation, caller, skill);
            return true;
        }

        /// <summary>
        /// Sends the command's progress as Progress frames while it runs, each time it changed: what
        /// it reports through CliProgress, or the Editor's own progress until it does. Clears it when
        /// the command stops reporting before it ends; Exit clears it at the end. Never throws.
        /// </summary>
        /// <param name="ended">Cancelled once the command ended.</param>
        static async Task ReportProgressAsync(CommandExecution execution, FrameOutput output, CancellationToken ended)
        {
            // What the client shows: nothing, until a frame says otherwise.
            var shown = Array.Empty<byte>();
            try
            {
                while (!ended.IsCancellationRequested)
                {
                    await Task.Delay(s_ProgressInterval, ended);

                    var body = EncodeProgress(execution.Progress);
                    if (body == null || body.AsSpan().SequenceEqual(shown))
                        continue;
                    output.ReportProgress(body);
                    shown = body;
                }
            }
            catch (OperationCanceledException)
            {
                // The command ended.
            }
            catch (Exception ex)
            {
                Debug.LogError($"Pipeline: The Pipeline protocol transport failed reporting a command's progress: {ex}");
            }
        }

        /// <summary>A Progress body for <paramref name="progress"/>; empty for none, null when it doesn't fit a frame.</summary>
        static byte[] EncodeProgress(CommandProgress progress)
        {
            if (progress == null)
                return Array.Empty<byte>();

            var pairs = new List<KeyValuePair<string, string>>(5);
            void Add(string key, string value)
            {
                // A value can't hold NUL; anything else a command reports is shown as it is.
                if (value != null)
                    pairs.Add(new KeyValuePair<string, string>(key, value.Replace("\0", string.Empty)));
            }

            Add("title", progress.Title);
            Add("info", progress.Info);
            Add("current", progress.Current?.ToString(CultureInfo.InvariantCulture));
            Add("total", progress.Total?.ToString(CultureInfo.InvariantCulture));
            Add("progress", FormatFraction(progress.Fraction));

            var body = PipelineProtocol.EncodeKeyValues(pairs);
            return body.Length <= PipelineProtocol.MaxBodySize ? body : null;
        }

        /// <summary>
        /// <paramref name="fraction"/> as a protocol number, which has no exponent (<c>R</c> writes
        /// 0.00001 as <c>1E-05</c>); null for none, or for one that isn't a number.
        /// </summary>
        internal static string FormatFraction(double? fraction)
        {
            if (fraction == null || double.IsNaN(fraction.Value) || double.IsInfinity(fraction.Value))
                return null;
            return fraction.Value.ToString("0.###############", CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Reads the client's Stdin and Signal frames while the command runs. A signal, or the client
        /// going away, cancels the command; frames of a type this version doesn't know are skipped.
        /// </summary>
        async Task ReceiveAsync(FrameInput input, CommandExecution execution)
        {
            try
            {
                while (true)
                {
                    var frame = await PipelineProtocol.ReadFrameAsync(m_Stream, PipelineProtocol.MaxBodySize, CancellationToken.None);
                    if (frame == null)
                        break;

                    switch (frame.Value.Type)
                    {
                        case FrameType.Stdin:
                            if (frame.Value.Body.Length == 0)
                                input.End();
                            else if (!input.Append(frame.Value.Body))
                            {
                                // More input than the command reads: closing, rather than holding it
                                // all, cancels the command below.
                                Close();
                                return;
                            }
                            break;
                        case FrameType.Signal:
                            if (frame.Value.Body.Length == 1
                                && (frame.Value.Body[0] == (byte)PipelineProtocol.Signal.Interrupt
                                    || frame.Value.Body[0] == (byte)PipelineProtocol.Signal.Terminate))
                                execution.Cancel();
                            break;
                    }
                }
            }
            catch (InvalidDataException)
            {
                // A frame empty, too large or cut short: the protocol asks to close at once, rather
                // than once the command, now cancelled, gets round to ending.
                Close();
            }
            catch (Exception ex) when (IsDisconnect(ex))
            {
                // The connection broke or closed under the read, as it does once the command ended.
            }
            catch (Exception ex)
            {
                Debug.LogError($"Pipeline: The Pipeline protocol transport failed reading a client's input: {ex}");
            }
            finally
            {
                input.End();
                // Does nothing once the command ended.
                execution.Cancel();
            }
        }

        /// <summary>Answers a command that didn't run: why on err, then its exit code.</summary>
        async Task RefuseAsync(ExitCode result, string message)
        {
            if (!string.IsNullOrEmpty(message))
            {
                var body = Encoding.UTF8.GetBytes(message.EndsWith("\n") ? message : message + "\n");
                for (var offset = 0; offset < body.Length; offset += PipelineProtocol.MaxBodySize)
                {
                    var count = Math.Min(PipelineProtocol.MaxBodySize, body.Length - offset);
                    await PipelineProtocol.WriteFrameAsync(m_Stream, FrameType.Stderr, body, offset, count, CancellationToken.None);
                }
            }

            await PipelineProtocol.WriteFrameAsync(m_Stream, FrameType.Exit, PipelineProtocol.EncodeExitCode((int)result),
                CancellationToken.None);

            // The client may have sent Stdin or Signal frames behind its request.
            await EndAsync(DiscardInputAsync());
        }

        /// <summary>Reads and drops the client's frames until it closes, for a command that didn't run.</summary>
        async Task DiscardInputAsync()
        {
            try
            {
                while (await PipelineProtocol.ReadFrameAsync(m_Stream, PipelineProtocol.MaxBodySize, CancellationToken.None) != null)
                {
                }
            }
            catch (Exception)
            {
                // A frame the protocol can't read, or the socket closed under the read: nothing left
                // worth waiting for.
            }
        }

        /// <summary>
        /// Why the executor refused a request, as text for err: all the HTTP API's envelope says,
        /// apart from the error's class, which the exit code carries. The command's usage after
        /// arguments that don't fit it, the open dialogs after a refusal they caused, and the new
        /// name of a command that was renamed.
        /// </summary>
        string DescribeRefusal(Exception exception)
        {
            switch (exception)
            {
                case CommandNotFoundException notFound:
                    return ObsoleteCommandNameMessage.ForMiss(notFound.CommandName, m_ObsoleteCommandNames) ?? notFound.Message;
                // The message is the problems, through DescribeArgProblems.
                case CommandArgumentsException arguments:
                    return arguments.Message + "\n\n" + DescribeUsage(arguments.CommandInfo);
                case CommandUnavailableException unavailable when unavailable.Dialogs.Count > 0:
                    var text = new StringBuilder(unavailable.Message).Append("\n\nOpen dialogs:");
                    foreach (var dialog in unavailable.Dialogs)
                    {
                        text.Append("\n  ").Append(BasePipelineServer.FormatDialogSummary(dialog));
                        if (dialog.Buttons != null && dialog.Buttons.Length > 0)
                            text.Append(" [").Append(string.Join(", ", dialog.Buttons)).Append(']');
                    }
                    return text.ToString();
                default:
                    return exception.Message;
            }
        }

        /// <summary>
        /// The command's usage line and its parameters, each with its type, description, and whether
        /// it is required or its default: the catalog entry <c>/api/exec</c> attaches to an argument
        /// error, as text.
        /// </summary>
        internal static string DescribeUsage(CommandInfo command)
        {
            var text = new StringBuilder("Usage: ").Append(command.FullName);
            foreach (var parameter in command.Parameters)
            {
                var flag = IsBoolean(parameter) ? $"--{parameter.Name}" : $"--{parameter.Name} <{TypeName(parameter)}>";
                text.Append(' ').Append(parameter.Required ? flag : $"[{flag}]");
            }

            if (command.Parameters.Count == 0)
                return text.ToString();

            text.Append("\n\nParameters:");
            var width = command.Parameters.Max(p => p.Name.Length + TypeName(p).Length);
            foreach (var parameter in command.Parameters)
            {
                var name = $"--{parameter.Name} <{TypeName(parameter)}>";
                text.Append("\n  ").Append(name.PadRight(width + 5));
                text.Append(' ').Append(parameter.Description);
                if (parameter.Required)
                    text.Append(" (required)");
                else if (parameter.DefaultValue != null)
                    text.Append(" (default: ").Append(Convert.ToString(parameter.DefaultValue, CultureInfo.InvariantCulture)).Append(')');
            }
            return text.ToString();
        }

        static Type UnderlyingType(CommandParameterInfo parameter) =>
            Nullable.GetUnderlyingType(parameter.ParameterType) ?? parameter.ParameterType;

        static string TypeName(CommandParameterInfo parameter) => UnderlyingType(parameter).Name;

        /// <summary>Whether the parameter is a flag, which a command line may give without a value.</summary>
        static bool IsBoolean(CommandParameterInfo parameter) => UnderlyingType(parameter) == typeof(bool);

        /// <summary>The exit code of a refusal the executor threw before anything ran.</summary>
        static ExitCode RefusalResult(Exception exception)
        {
            switch (exception)
            {
                case CommandNotFoundException _:
                    return ExitCode.NotFound;
                // Arguments that don't bind or convert.
                case CommandArgumentsException _:
                case ArgumentException _:
                    return ExitCode.InvalidUsage;
                case CommandUnavailableException unavailable:
                    return unavailable.Reason == CommandUnavailableReason.BlockedByDialog
                        ? ExitCode.BlockedByDialog
                        : ExitCode.Unavailable;
                case TooManyCommandsException _:
                    return ExitCode.Unavailable;
                default:
                    return ExitCode.Failed;
            }
        }

        /// <summary>
        /// Whether <paramref name="exception"/> is the connection closing or breaking, or the client
        /// breaking the protocol: expected at any point, and nothing to log.
        /// </summary>
        static bool IsDisconnect(Exception exception)
        {
            return exception is IOException
                || exception is SocketException
                || exception is ObjectDisposedException
                || exception is InvalidDataException
                || exception is OperationCanceledException;
        }

        void Close() => Close(m_Socket);

        static void Close(Socket socket)
        {
            try
            {
                socket.Close();
            }
            catch (Exception)
            {
                // Already closed.
            }
        }
    }
}
