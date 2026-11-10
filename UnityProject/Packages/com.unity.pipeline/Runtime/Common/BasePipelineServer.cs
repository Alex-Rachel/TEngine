using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Unity.Pipeline.Models;
using Unity.Pipeline.Commands;
using Unity.Pipeline.Security;
using Unity.Pipeline.Threading;
using UnityEngine;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Unity.Pipeline
{
    /// <summary>
    /// HTTP server that enables CLI tools to execute commands in Unity Editor.
    /// Represents an Instance that can serve as a Pipeline Element for automation.
    /// </summary>
    public abstract class BasePipelineServer
    {
        private const long DefaultMaxRequestBodyBytes = 1 * 1024 * 1024;

        /// <summary>
        /// Maximum accepted request body size. Larger requests are rejected with 413, bounding the memory
        /// one remote request can force the server to buffer. Settable so a server whose clients
        /// legitimately post larger documents can raise it without every other server paying for it.
        /// </summary>
        public long MaxRequestBodyBytes { get; set; } = DefaultMaxRequestBodyBytes;

        /// <summary>
        /// Upper bound on how many bytes we read-and-discard from an over-limit request body before
        /// sending the 413. HttpListener resets the TCP connection if we close the response while the
        /// client is still uploading, and the client then sees a connection error instead of the 413.
        /// Draining lets the client finish so it can read the response. Kept comfortably above
        /// <see cref="MaxRequestBodyBytes"/> to cover clients only slightly over the cap, while still
        /// bounding the work a single abusive request can force (a larger body simply resets).
        /// </summary>
        private const long MaxDrainBytes = 8 * 1024 * 1024;

        /// <summary>
        /// Maximum accepted body size for /api/job/cancel (4 KiB). The body is just an
        /// <c>{"id": "..."}</c> object, so it needs far less headroom than <see cref="MaxRequestBodyBytes"/>.
        /// </summary>
        private const long MaxCancelBodyBytes = 4 * 1024;

        private bool m_IsRunning;
        private int m_Port;

        private HttpListener m_HttpListener;
        private readonly Dispatcher m_Dispatcher = new Dispatcher();

        /// <summary>The progress of the command this server's queue runs now (see CliProgress).</summary>
        internal CliProgressState Progress => Queue.Progress;

        /// <summary>This server's own modal-dialog state (see EditorDialogStateMirror, Task 7).</summary>
        internal DialogStateTracker Dialogs { get; } = new DialogStateTracker();

        /// <summary>
        /// The detached jobs (CLI-335) this server ran for <c>/api/exec</c> with <c>"job": true</c>,
        /// by id: each one's execution and what it wrote, kept after it ends so a client that
        /// timed out or disconnected can still collect it with <c>/api/job</c>. In-memory by design:
        /// a domain reload discards them, as it does every other C# static.
        /// </summary>
        readonly ConcurrentDictionary<CommandExecutionId, Job> m_Jobs = new ConcurrentDictionary<CommandExecutionId, Job>();

        /// <summary>Ended jobs kept beyond this count are pruned oldest-first.</summary>
        const int MaxRetainedEndedJobs = 100;

        /// <summary>Ended jobs older than this are pruned regardless of count.</summary>
        static readonly TimeSpan s_JobRetention = TimeSpan.FromHours(1);


        /// <summary>
        /// Runs this server's commands one at a time on its dispatcher. The status routes use it
        /// directly; everything else goes through <see cref="Executor"/>.
        /// </summary>
        internal DefaultCommandQueue Queue { get; }

        /// <summary>
        /// Runs the commands this server's requests ask for, through <see cref="Queue"/>, and
        /// decides whether a command can run now.
        /// </summary>
        internal ICommandExecutor Executor { get; }

        private bool m_WatchdogEnabled;
        private bool m_WatchdogArmed;
        private DateTime m_LastWatchdogCheck;

        /// <summary>
        /// This server's own main-thread dispatcher. Each server instance owns one (no global
        /// singleton), so tests that start their own server never affect any other server's
        /// dispatch. Pump it via ProcessWorkQueue from the main thread (auto-pumped on
        /// EditorApplication.update in the editor; pumped by RuntimePipelineDriver.Update in a
        /// player; pumped explicitly by tests).
        /// </summary>
        public Dispatcher Dispatcher => m_Dispatcher;

        /// <summary>Creates the server, with its executor over this domain's command registry.</summary>
        protected BasePipelineServer()
        {
            DefaultCommandExecutor executor = null;
            Queue = new DefaultCommandQueue(m_Dispatcher, info => executor.ReportEnded(info));
            Executor = executor = CreateExecutor(CommandRegistry.GetRegistry(), Queue);
        }

        /// <summary>
        /// Creates this server's executor, whose rules decide what may run. Called from the
        /// constructor, so an override uses only what the base class has set up, such as
        /// <see cref="Dispatcher"/> and <see cref="Dialogs"/>. Default: an executor that refuses
        /// nothing, as a Player's.
        /// </summary>
        internal virtual DefaultCommandExecutor CreateExecutor(ICommandRegistry registry, ICommandQueue queue)
        {
            return new DefaultCommandExecutor(registry, queue);
        }

        /// <summary>
        /// Whether the server is running AND its HTTP listener is actually listening. More accurate
        /// than the internal running flag alone: returns false if the listener was stopped/disposed
        /// (e.g. by a domain reload) even when the flag is stale-true. Cheap and non-blocking.
        ///
        /// A self-HTTP probe was considered for "does it actually respond" but rejected: the server
        /// processes requests strictly one-at-a-time (HandleRequests awaits ProcessRequest), so a
        /// self-probe deadlocks if issued from within a handler and false-negatives whenever a
        /// request is in flight — unsafe for a watchdog. IsListening reliably catches the realistic
        /// failure (listener stopped by a domain reload), which is what we need.
        /// </summary>
        public bool IsRunning => m_IsRunning && (!m_ServesHttp || (m_HttpListener != null && m_HttpListener.IsListening));

        /// <summary>
        /// Whether <see cref="Start"/> opens the HTTP listener. When false the server still starts
        /// its dispatcher and executor, for another transport to serve, but listens on no port,
        /// advertises no <c>http</c> endpoint and arms no watchdog. Default true; read at start.
        /// </summary>
        internal bool HttpEnabled { get; set; } = true;

        // HttpEnabled as of the last Start, so editing the property mid-run changes nothing.
        private bool m_ServesHttp;

        /// <summary>
        /// When enabled, the server periodically checks its own HTTP listener and re-opens it in
        /// place if it died without going through <see cref="Stop"/> (e.g. an unexpected listener
        /// fault). The server instance survives such failures — only the listener dies — so it can
        /// self-heal without an external restart. Default OFF: transient/test servers must not
        /// watchdog. The editor owner enables it for the live server.
        ///
        /// Setting this while the server is running arms/disarms the watchdog immediately; otherwise
        /// it takes effect on the next <see cref="Start"/>. Domain reloads are NOT handled here (the
        /// instance is torn down and recreated by [InitializeOnLoad]); the watchdog only revives a
        /// listener that died while the instance is still alive.
        /// </summary>
        public bool WatchdogEnabled
        {
            get => m_WatchdogEnabled;
            set
            {
                if (m_WatchdogEnabled == value)
                    return;
                m_WatchdogEnabled = value;
                if (!m_IsRunning)
                    return;
                if (value)
                    ArmWatchdog();
                else
                    DisarmWatchdog();
            }
        }

        /// <summary>
        /// How often the watchdog checks the listener (seconds). Default 5.
        /// </summary>
        public double WatchdogIntervalSeconds { get; set; } = 5.0;

        /// <summary>
        /// Port number the server is listening on. 0 if not running.
        /// Range: 7800-7899 for Editor, 7900-7999 for Runtime (avoids unity-tools port range 37800-37899).
        /// </summary>
        public int Port => m_Port;

        /// <summary>UTC time this server instance started listening.</summary>
        public DateTime StartedAt => m_StartedAt;

        private DateTime m_StartedAt;

        /// <summary>
        /// Whether this server registers its <c>http</c> endpoint in <see cref="PipelineEndpoints"/>
        /// while running. Test servers return false so they don't show up beside the live server.
        /// </summary>
        protected virtual bool AdvertisesEndpoint => true;

        /// <summary>
        /// Whether this server's endpoint is currently registered in <see cref="PipelineEndpoints"/>:
        /// true from a successful <see cref="Start"/> until <see cref="Stop"/> (or a failed start)
        /// unregisters it. Always false when <see cref="AdvertisesEndpoint"/> is false. Subclasses
        /// report /api/status "ready" vs. "error" from it.
        /// </summary>
        protected bool IsEndpointRegistered => m_Endpoint != null;

        private PipelineServiceEndpoint m_Endpoint;

        // Unregister from the same registry, even if PipelineEndpoints has released it since.
        private IEndpointRegistrationService m_EndpointRegistry;

        /// <summary>
        /// Whether requests from a sandboxed browser frame (Origin: null) are accepted. Off by default;
        /// an ordinary web page is refused either way, and a sandboxed one still needs the bearer token,
        /// which lives in a file no browser can read. Such a frame may also address the server as
        /// localhost. Read by <see cref="Start"/>, so set it before <see cref="Start"/>.
        /// </summary>
        public bool AllowSandboxedBrowserClients
        {
            get => m_AllowSandboxedBrowserClients;
            set => m_AllowSandboxedBrowserClients = value;
        }

        // Read on the listener thread, so volatile: a value set on the main thread must be seen there.
        private volatile bool m_AllowSandboxedBrowserClients;

        /// <summary>The only Origin a sandboxed frame reports; a real page sends its own origin.</summary>
        private const string SandboxedOrigin = "null";

        /// <summary>
        /// The one host this server binds and answers to. Single-sourced because it is both the
        /// listener prefix (which decides the socket address) and the only accepted Host header.
        /// </summary>
        private const string LoopbackHost = "127.0.0.1";

        /// <summary>The host a sandboxed browser frame dials, accepted only from such a frame.</summary>
        private const string LocalhostName = "localhost";

        /// <summary>How long <see cref="Start"/> waits for "localhost" to resolve on the main thread.</summary>
        private static readonly TimeSpan LocalhostResolveTimeout = TimeSpan.FromSeconds(1);

        /// <summary>
        /// Whether listeners also register a localhost prefix. Decided once per <see cref="Start"/>:
        /// the port probe and the watchdog re-open build listeners too, and must not repeat the DNS
        /// lookup or its warning.
        /// </summary>
        private bool m_ServesLocalhost;

        /// <summary>Build the host-specific payload for <c>/api/status</c> (Editor vs. Player fields differ).</summary>
        /// <returns>The status payload.</returns>
        protected abstract object GetServerStatus();

        /// <summary>
        /// The Editor's state, for <c>/api/editor_status</c>. A Player has none, and the route
        /// answers its failure.
        /// </summary>
        /// <exception cref="NotSupportedException">This server is not an Editor's.</exception>
        internal virtual Task<StatusResponse> GetEditorStatus()
        {
            throw new NotSupportedException("Only an Editor reports its status.");
        }

        /// <summary>
        /// The state of the asynchronous test run, for <c>/api/test-status</c>. A Player runs no
        /// tests, and the route answers its failure.
        /// </summary>
        /// <exception cref="NotSupportedException">This server is not an Editor's.</exception>
        internal virtual object GetTestStatus()
        {
            throw new NotSupportedException("Only an Editor runs tests.");
        }

        /// <summary>
        /// Answers a command the executor refused as though it ran, for a server that has a result
        /// to give in its place. Called on the HTTP thread. None by default.
        /// </summary>
        /// <param name="refusal">Why the command can't run now.</param>
        /// <param name="answer">The result to answer with.</param>
        /// <returns>False to answer the refusal itself.</returns>
        internal virtual bool TryAnswerRefusal(CommandUnavailableException refusal, out object answer)
        {
            answer = null;
            return false;
        }

        /// <summary>The bearer token clients must present to authenticate requests.</summary>
        /// <returns>The current token.</returns>
        protected abstract string GetToken();

        /// <summary>
        /// Optional wire features every server understands, advertised on /api/status and in the
        /// port descriptor. A Player server advertises exactly these; the Editor server advertises
        /// <see cref="EditorCapabilities"/>.
        ///
        /// One array per server feeds both carriers so they cannot drift: a server that claims a
        /// capability it lacks is worse than one that claims nothing, because a client's fallback
        /// is keyed on the claim. Absence of the key entirely means the server is too old to have
        /// it, and clients must then assume no raw command-line support.
        ///
        /// <c>exec.commandTimeouts</c>: a request without a <c>timeout</c> gets the command's own
        /// default, and the server always answers (504 <c>TIMEOUT</c>) when a timeout runs out.
        /// </summary>
        internal static readonly string[] Capabilities = { "exec.argv", "exec.commandLine", "exec.commandTimeouts" };

        /// <summary>The capability of a server that answers <c>/api/commands?detail=obsolete_names</c>.</summary>
        internal const string ObsoleteNamesCapability = "commands.obsoleteNames";

        /// <summary>
        /// The capability of a server whose <c>pipeline</c> endpoint speaks the Pipeline protocol
        /// with the unity CLI's exit codes. An Editor of 0.9.0-exp.1 lists that endpoint but answers
        /// with earlier codes, so a client keys the transport on this, not on the endpoint. Not
        /// <c>transport.pipeline</c>: unity CLI 1.0.0-beta.15 keys on that name and reads the
        /// codes from 100 up, so it stays on HTTP.
        /// </summary>
        internal const string PipelineTransportCapability = "transport.pipelineProtocol";

        /// <summary>
        /// The capability of a server whose Pipeline protocol transport formats and streams a
        /// command's result for a client that sends <c>stdout.tty</c>.
        /// </summary>
        internal const string PipelineTerminalCapability = "pipeline.terminal";

        /// <summary>
        /// <see cref="Capabilities"/> plus what only the Editor serves: the obsolete command names,
        /// which ship as a package file a Player does not carry, and the Pipeline protocol transport.
        /// </summary>
        internal static readonly string[] EditorCapabilities =
            Capabilities.Append(ObsoleteNamesCapability).Append(PipelineTransportCapability)
                .Append(PipelineTerminalCapability).ToArray();

        /// <summary>
        /// Old command name to the full name that replaced it, served by
        /// <c>/api/commands?detail=obsolete_names</c>. Null on a server that does not serve them,
        /// which then refuses that detail value like an older package does.
        /// </summary>
        protected virtual IReadOnlyDictionary<string, string> ObsoleteCommandNames => null;

        /// <summary>Hook invoked once the listener is accepting requests. No-op by default.</summary>
        protected virtual void ServerStarted()
        {

        }

        /// <summary>Hook invoked once the listener has stopped accepting requests. No-op by default.</summary>
        protected virtual void ServerStopped()
        {

        }

        /// <summary>
        /// This server's current auth token. Exposed to the test assembly (via InternalsVisibleTo)
        /// so the test client can authenticate without re-deriving the token.
        /// </summary>
        internal string Token => GetToken();

        /// <summary>
        /// Whether <see cref="Port"/> was picked from this server's range rather than requested by
        /// the caller. Recorded at bind time, so it still describes the running listener after the
        /// settings asset is edited.
        /// </summary>
        internal bool PortAutoAssigned { get; private set; }

        /// <summary>
        /// Port that auto-assignment tries before it scans the range, so a restarted server can keep
        /// the port it had. Ignored when outside <see cref="GetPortRange"/> or not free.
        /// </summary>
        internal int PreferredPort { get; set; }

        /// <summary>
        /// Start the HTTP server on the specified port or auto-assign from range.
        /// </summary>
        /// <param name="port">Port to bind to, or 0 for auto-assignment from server-specific range</param>
        public void Start(int port = 0)
        {
            if (m_IsRunning)
                return;

            m_ServesHttp = HttpEnabled;
            m_ServesLocalhost = m_ServesHttp && ShouldServeLocalhost();
            m_Port = !m_ServesHttp ? 0 : port == 0 ? FindAvailablePort() : port;
            PortAutoAssigned = m_ServesHttp && port == 0;

            try
            {
                // Initialize this server's own dispatcher (captures the main thread; Start() is
                // always called from the main thread).
                m_Dispatcher.Initialize();

                // Warm the token cache on the main thread before the listener accepts requests:
                // per-request auth runs on background threads and the Editor token is SessionState-
                // backed (main-thread-only). Re-runs after every domain reload via [InitializeOnLoad].
                SecurityTokenManager.GetOrCreateToken();

                // Mark running before opening the listener so HandleRequests' loop guard stays true
                // as soon as it starts on the threadpool.
                m_IsRunning = true;
                if (m_ServesHttp)
                {
                    OpenListener();
                    System.Console.WriteLine($"Start HTTP server: port:{m_Port}");
                }

                m_StartedAt = DateTime.UtcNow;
                if (m_ServesHttp && AdvertisesEndpoint)
                    RegisterEndpoint();

                ArmWatchdog();

                ServerStarted();
            }
            catch (Exception)
            {
                m_IsRunning = false;
                // Don't leave a failed start advertised.
                UnregisterEndpoint();
                m_HttpListener?.Stop();
                m_HttpListener = null;
                throw;
            }
        }

        /// <summary>
        /// Create the HTTP listener, bind it, and start the request-handling loop. Extracted from
        /// <see cref="Start"/> so the watchdog can re-open a dead listener in place without
        /// re-running the rest of startup (dispatcher init, descriptor). Caller sets m_IsRunning.
        /// </summary>
        private void OpenListener()
        {
            m_HttpListener = new HttpListener();
            AddLoopbackPrefixes(m_HttpListener, m_Port);
            m_HttpListener.Start();

            // Start request handling
            _ = Task.Run(HandleRequests);
        }

        /// <summary>
        /// Bind the IPv4 loopback literal, and only that. The prefix host is what picks the socket
        /// address: Unity's Mono ships HttpListener as managed code over a plain socket, where "+"
        /// and "*" both resolve to IPAddress.Any — a bind on every interface, with no URL ACL
        /// behind it — and a hostname is resolved through DNS, falling back to IPAddress.Any when
        /// resolution fails. An IP literal is the only spelling that cannot widen.
        ///
        /// The cost is that clients address the server as 127.0.0.1 and nothing else: a request
        /// spelling the host any other way is refused. Mono's listener did that itself, by matching
        /// Host literally against the prefix; CoreCLR's listener on Windows does not, which is why
        /// <see cref="IsHostAllowed"/> now makes the check explicitly rather than relying on it.
        ///
        /// IPv4 only: Mono mis-parses a bracketed "[::1]" host, so the IPv6 loopback cannot be
        /// served whichever prefix is registered.
        ///
        /// The one exception is <see cref="AllowSandboxedBrowserClients"/>: a browser frame dials
        /// "localhost", and a managed listener refuses that Host unless a matching prefix exists. The
        /// prefix is registered only when "localhost" resolves to loopback addresses alone, so it
        /// cannot widen the socket binding. It is never registered over http.sys (see
        /// <see cref="ListenerMatchesHostAgainstPrefixes"/>).
        /// </summary>
        private void AddLoopbackPrefixes(HttpListener listener, int port)
        {
            listener.Prefixes.Add(BaseUrlFor(port));

            if (m_ServesLocalhost)
                listener.Prefixes.Add($"http://{LocalhostName}:{port}/");
        }

        private bool ShouldServeLocalhost()
        {
            if (!AllowSandboxedBrowserClients || !ListenerMatchesHostAgainstPrefixes)
                return false;

            if (ResolvesToLoopbackOnly(LocalhostName, LocalhostResolveTimeout))
                return true;

            Debug.LogWarning($"Pipeline: '{LocalhostName}' did not resolve to the loopback only, so browser "
                + $"clients must address the server as {LoopbackHost}.");
            return false;
        }

        /// <summary>
        /// Whether the listener refuses a Host that matches no registered prefix. Mono's managed
        /// listener does, and so does the managed one CoreCLR uses on macOS and Linux. CoreCLR on
        /// Windows runs over http.sys instead, which already serves "Host: localhost" through the
        /// 127.0.0.1 prefix, and listens on every interface for a hostname prefix.
        /// </summary>
        private static readonly bool ListenerMatchesHostAgainstPrefixes =
            Type.GetType("Mono.Runtime") != null
            || !System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(
                System.Runtime.InteropServices.OSPlatform.Windows);

        /// <summary>
        /// Whether <paramref name="host"/> resolves, within <paramref name="timeout"/>, to loopback
        /// addresses alone. A failed or slower lookup counts as no: DNS has no timeout of its own.
        /// </summary>
        internal static bool ResolvesToLoopbackOnly(string host, TimeSpan timeout)
        {
            try
            {
                var lookup = Dns.GetHostAddressesAsync(host);
                if (!lookup.Wait(timeout))
                    return false;

                var addresses = lookup.Result;
                return addresses.Length > 0 && addresses.All(IPAddress.IsLoopback);
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// The base URL of a server on <paramref name="port"/>, and the one source the listener
        /// prefix is registered from. Anything in this package that addresses a server goes through
        /// here, because <see cref="IsHostAllowed"/> accepts only this host: a caller that spelled
        /// it its own way would be answered 400 the moment the two drifted apart.
        /// </summary>
        internal static string BaseUrlFor(int port) => $"http://{LoopbackHost}:{port}/";

        /// <summary>The endpoint this server advertises while running. Called once per start.</summary>
        /// <remarks>Internal, not protected: <see cref="PipelineServiceEndpoint"/> is internal.</remarks>
        /// <returns>An <c>http</c> endpoint with this server's <c>port</c>.</returns>
        internal virtual PipelineServiceEndpoint CreateEndpoint()
        {
            return new PipelineServiceEndpoint("http", new Dictionary<string, string>
            {
                ["port"] = m_Port.ToString(System.Globalization.CultureInfo.InvariantCulture)
            });
        }

        private void RegisterEndpoint()
        {
            m_EndpointRegistry = PipelineEndpoints.GetRegistrationService();
            m_Endpoint = CreateEndpoint();
            m_EndpointRegistry.RegisterEndpoint(m_Endpoint);
        }

        private void UnregisterEndpoint()
        {
            if (m_Endpoint == null)
                return;
            m_EndpointRegistry.UnregisterEndpoint(m_Endpoint);
            m_Endpoint = null;
            m_EndpointRegistry = null;
        }

        /// <summary>
        /// Stop the HTTP server and clean up resources.
        /// </summary>
        public void Stop()
        {
            if (!m_IsRunning)
                return;

            m_IsRunning = false;

            DisarmWatchdog();
            System.Console.WriteLine("Pipeline Server stopped");

            try
            {
                UnregisterEndpoint();

                // Shut the dispatcher down BEFORE Close() below, not after: a MainThreadRequired
                // command still parked in Dispatcher.Invoke's polling loop has no pump left to wait
                // for once Stop() (running on the main thread) starts tearing things down, so it
                // would otherwise just sit there until Close() disposes its context out from under
                // it. Shutdown() resolves each queued item with OperationCanceledException
                // immediately, so Invoke() unblocks right away and HandleExecRequest gets a chance
                // to write a real "shutting down" response — best-effort, since it still races
                // Close() below, but no worse than before and it starts that race far sooner.
                m_Dispatcher.Shutdown();

                // Close() alone also stops the listener (same pattern as WatchdogTick's re-open
                // below). A separate preceding Stop() call left a window where the listener was
                // "stopped" but not yet disposed: HandleRequests' accept loop, running on another
                // thread, could call GetContextAsync() in that gap and hit Mono's
                // InvalidOperationException("Please, call Start...") instead of the
                // ObjectDisposedException/HttpListenerException it already handles as shutdown.
                m_HttpListener?.Close();

                ServerStopped();
            }
            catch
            {
                // Ignore cleanup errors
            }
            finally
            {
                m_HttpListener = null;
            }
            // Note: Keep port value for diagnostic purposes
        }

        /// <summary>
        /// Arm the watchdog (no-op if disabled or already armed). In the editor the tick rides
        /// EditorApplication.update; in a player it is driven by RuntimePipelineDriver.Update via
        /// <see cref="WatchdogTick"/>.
        /// </summary>
        private void ArmWatchdog()
        {
            if (!m_WatchdogEnabled || m_WatchdogArmed || !m_ServesHttp)
                return;

            m_WatchdogArmed = true;
            m_LastWatchdogCheck = DateTime.UtcNow;
#if UNITY_EDITOR
            UnityEditor.EditorApplication.update += WatchdogTick;
#endif
        }

        private void DisarmWatchdog()
        {
            if (!m_WatchdogArmed)
                return;

            m_WatchdogArmed = false;
#if UNITY_EDITOR
            UnityEditor.EditorApplication.update -= WatchdogTick;
#endif
        }

        /// <summary>
        /// Watchdog heartbeat: throttled to <see cref="WatchdogIntervalSeconds"/>, re-opens the HTTP
        /// listener in place if it died while the server is still meant to be running. Safe to call
        /// every frame. In the editor it is subscribed to EditorApplication.update by ArmWatchdog;
        /// in a player RuntimePipelineDriver.Update calls it.
        /// </summary>
        public void WatchdogTick()
        {
            if (!m_WatchdogArmed)
                return;

#if UNITY_EDITOR
            // Don't fight domain reloads / asset updates — the listener is expected to be torn down
            // then, and [InitializeOnLoad] recreates the server afterwards.
            if (UnityEditor.EditorApplication.isCompiling || UnityEditor.EditorApplication.isUpdating)
                return;
#endif

            var now = DateTime.UtcNow;
            if ((now - m_LastWatchdogCheck).TotalSeconds < WatchdogIntervalSeconds)
                return;
            m_LastWatchdogCheck = now;

            if (m_HttpListener != null && m_HttpListener.IsListening)
                return; // healthy

            try
            {
                // Dispose the dead listener so it releases the port binding before we re-bind a new
                // one on the same port (a stopped-but-not-closed listener can keep the port held).
                try { m_HttpListener?.Close(); } catch { }
                m_HttpListener = null;

                // The instance is alive and the watchdog is armed → the server is meant to be
                // listening. Re-open the listener in place.
                m_IsRunning = true;
                OpenListener();
                Debug.Log($"Pipeline watchdog re-opened HTTP listener on port {m_Port}");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"Pipeline watchdog failed to re-open listener on port {m_Port}: {ex.Message}");
            }
        }

        /// <summary>
        /// Handle incoming HTTP requests with routing to appropriate endpoints.
        /// </summary>
        private async Task HandleRequests()
        {
            while (m_IsRunning && m_HttpListener != null && m_HttpListener.IsListening)
            {
                try
                {
                    var context = await m_HttpListener.GetContextAsync();
                    // Process detached so the accept loop keeps serving while a long command
                    // holds its /api/exec connection open. Without this, ANY in-flight exec
                    // blocked every other request — including /api/status probes and the
                    // /api/progress polls that exist precisely for that situation (CLI-488;
                    // the "editor is unresponsive until the command finishes" report in
                    // CLI-335). Command execution itself stays strictly serialized via
                    // the command queue, so exec ordering semantics are unchanged.
                    _ = ProcessRequestDetached(context);
                }
                catch (ObjectDisposedException)
                {
                    // Listener was stopped, exit gracefully
                    break;
                }
                catch (HttpListenerException)
                {
                    // Listener was stopped, exit gracefully
                    break;
                }
                catch (Exception ex)
                {
                    Debug.LogError(ex);
                }
            }
            m_IsRunning = false;
        }

        /// <summary>
        /// Run ProcessRequest without the accept loop awaiting it; a per-request failure is
        /// logged and must never tear down the listener loop.
        ///
        /// The loopback check sits here rather than in the overridable <see cref="ProcessRequest"/>
        /// so a subclass cannot drop it. The socket is already bound to the loopback address (see
        /// <see cref="AddLoopbackPrefixes"/>), so this is a second line of defence: it covers a
        /// widened prefix and a hop that re-sources a connection as loopback.
        /// </summary>
        private async Task ProcessRequestDetached(HttpListenerContext context)
        {
            try
            {
                var remoteAddress = context.Request.RemoteEndPoint?.Address;
                if (remoteAddress == null || !IPAddress.IsLoopback(remoteAddress))
                {
                    await SendStatusResponse(
                        context.Response, 403, "Forbidden", "Only loopback connections are allowed");
                    return;
                }

                await ProcessRequest(context);
            }
            catch (Exception ex) when (!m_IsRunning && (ex is ObjectDisposedException || ex is HttpListenerException))
            {
                // Stop() sets m_IsRunning = false BEFORE closing the listener, so this combination
                // (disposed/listener exception + already stopping) means Close() tore down this
                // context while a detached task (this one) was still mid-processing — an expected
                // shutdown race, not a bug. Log at info level rather than swallowing outright: still
                // discoverable if it starts happening far more than a stop-time race would explain,
                // but won't fail whichever test happens to be running when this task resumes.
                // Guarding on m_IsRunning keeps a genuine ObjectDisposedException/HttpListenerException
                // from a command handler mid-execution (not a shutdown) loud, as Debug.LogError below.
                Debug.Log($"Pipeline: request handling ended by server shutdown: {ex.GetType().Name}: {ex.Message}");
            }
            catch (Exception ex)
            {
                Debug.LogError(ex);
            }
        }

        /// <summary>
        /// Process individual HTTP request and route to appropriate handler.
        /// </summary>
        /// <param name="context">The incoming HTTP request/response context.</param>
        /// <returns>A task that completes once the response has been written.</returns>
        protected virtual async Task ProcessRequest(HttpListenerContext context)
        {
            var request = context.Request;
            var response = context.Response;

            try
            {
                // Refuse anything not addressed to this server's own endpoint, before any other
                // handling. This is deliberately not redundant with the Origin gate below: in a DNS
                // rebinding attack the attacker's page has had its own domain re-pointed at
                // 127.0.0.1, so the browser believes that domain IS this server and sends the
                // request as same-origin — with no Origin header for the gate to catch. What it
                // cannot suppress is Host, which still names the attacker's domain. Pinning Host to
                // the single endpoint the listener is registered for is the check that sees it.
                if (!IsHostAllowed(request))
                {
                    await SendStatusResponse(response, 400, "Bad Request",
                        $"Unexpected Host header; this server is addressed as {LoopbackHost}:{m_Port}");
                    return;
                }

                // Reject browser-originated requests. Legitimate non-browser clients (CLI, CI) never
                // send an Origin header; emitting no CORS headers and refusing any request that
                // carries one prevents a website in the developer's browser from reaching this
                // local server (and short-circuits CORS preflights). A server that opts into
                // AllowSandboxedBrowserClients additionally admits Origin: null.
                var origin = request.Headers["Origin"];
                if (!string.IsNullOrEmpty(origin))
                {
                    if (!AllowSandboxedBrowserClients || origin != SandboxedOrigin)
                    {
                        await SendStatusResponse(response, 403, "Forbidden", "Cross-origin requests are not allowed");
                        return;
                    }

                    // "*" rather than echoing "null": the gate above already decides who is served, no
                    // credentials are involved, and it is better supported for an opaque origin.
                    response.AddHeader("Access-Control-Allow-Origin", "*");
                    response.AddHeader("Vary", "Origin");
                }

                // Preflight is answered before auth by necessity: the browser sends OPTIONS with no
                // Authorization header, so requiring the token here would fail every real request.
                if (request.HttpMethod == "OPTIONS")
                {
                    if (string.IsNullOrEmpty(origin))
                    {
                        await SendStatusResponse(response, 405, "Method Not Allowed", "OPTIONS is only used for CORS preflight");
                        return;
                    }

                    response.AddHeader("Access-Control-Allow-Methods", "GET, POST, OPTIONS");
                    response.AddHeader("Access-Control-Allow-Headers", "Authorization, Content-Type");
                    response.AddHeader("Access-Control-Max-Age", "600");
                    // Chrome's Private Network Access: a page on the public internet reaching a loopback
                    // address must be granted it explicitly, or the preflight fails.
                    if (!string.IsNullOrEmpty(request.Headers["Access-Control-Request-Private-Network"]))
                        response.AddHeader("Access-Control-Allow-Private-Network", "true");
                    response.StatusCode = 204;
                    response.ContentLength64 = 0;
                    response.OutputStream.Close();
                    return;
                }

                // Authenticate every request with the bearer token before routing.
                if (!IsAuthorized(request))
                {
                    await SendStatusResponse(response, 401, "Unauthorized", "Missing or invalid authentication token");
                    return;
                }

                // Route to appropriate endpoint
                var path = request.Url.AbsolutePath.ToLowerInvariant();

                switch (path)
                {
                    case "/api/status":
                        await HandleStatusRequest(response);
                        break;
                    case "/api/editor_status":
                        await HandleEditorStatusRequest(response);
                        break;
                    case "/api/commands":
                        await HandleCommandsRequest(request, response);
                        break;
                    case "/api/exec":
                        if (request.HttpMethod == "POST")
                            await HandleExecRequest(request, response);
                        else
                            await HandleMethodNotAllowed(response, "POST");
                        break;
                    case "/api/test-status":
                        await HandleTestStatusRequest(response);
                        break;
                    case "/api/progress":
                        await HandleProgressRequest(request, response);
                        break;
                    case "/api/dialog":
                        await HandleDialogRequest(response);
                        break;
                    case "/api/job":
                        if (request.HttpMethod == "GET")
                            await HandleJobStatusRequest(request, response);
                        else
                            await HandleMethodNotAllowed(response, "GET");
                        break;
                    case "/api/job/cancel":
                        if (request.HttpMethod == "POST")
                            await HandleJobCancelRequest(request, response);
                        else
                            await HandleMethodNotAllowed(response, "POST");
                        break;
                    default:
                        await HandleNotFound(response);
                        break;
                }
            }
            catch (Exception e)
            {
                // A silent 500 leaves the client with nothing to go on; name the request and the fault.
                Debug.LogError($"Pipeline: unhandled error serving {request.HttpMethod} "
                    + $"{request.Url?.AbsolutePath}: {e}");

                // Ensure response is always closed
                try
                {
                    if (response.OutputStream.CanWrite)
                    {
                        response.StatusCode = 500;
                        response.OutputStream.Close();
                    }
                }
                catch { }
            }
        }

        /// <summary>
        /// Validate the request's bearer token against this server's token.
        /// </summary>
        /// <summary>
        /// Whether the request's Host header names this server's own endpoint — <c>127.0.0.1</c> on
        /// the port it listens on — and nothing else.
        ///
        /// Until Unity 7 this was enforced for us, by accident: Unity's Mono ships HttpListener as
        /// managed code that matches a request's Host <em>literally</em> against each registered
        /// prefix, and the only prefix registered is the IPv4 loopback literal, so the listener
        /// answered 400 to anything else before our code ever saw it. Under CoreCLR that no longer
        /// holds everywhere: on Windows, "Host: localhost" reached this handler and was served (the
        /// 7000.0 win10 job got a 200). The check is therefore ours to make, which is better
        /// regardless: a wrong Host is refused on every runtime, and the rule is stated in one place
        /// rather than inherited from an implementation detail.
        ///
        /// It is not the only layer. The managed listener CoreCLR uses on macOS and Linux still turns
        /// away a Host that matches no prefix before this runs, with a 404 rather than this 400. So
        /// the code a refused client sees varies by platform, but the refusal does not.
        ///
        /// Strict on purpose. Everything in this package addresses a server through
        /// <see cref="BaseUrlFor"/>, so the one spelling that has to work is the one it produces;
        /// admitting "localhost" as well would widen the surface for no caller's benefit. The one
        /// caller that needs it is a sandboxed browser frame, so "localhost" is accepted only with
        /// <see cref="AllowSandboxedBrowserClients"/> on and an Origin of "null". A rebinding
        /// request still names the attacker's domain, so this admits nothing the Origin gate
        /// would not.
        /// </summary>
        private bool IsHostAllowed(HttpListenerRequest request)
        {
            // Read the raw header rather than UserHostName, which some implementations synthesize
            // from the local endpoint when the header is absent — that would turn a missing Host
            // into a passing one.
            var host = request.Headers["Host"];

            // HTTP/1.1 requires Host. Nothing this package ships omits it.
            if (string.IsNullOrEmpty(host))
                return false;

            // Last colon, not the first: only the IPv4 literal is ever bound, so there is no
            // bracketed IPv6 form to parse, but splitting from the right is correct either way.
            var separator = host.LastIndexOf(':');
            var name = separator >= 0 ? host.Substring(0, separator) : host;
            if (name != LoopbackHost && !IsSandboxedBrowserAtLocalhost(request, name))
                return false;

            // A Host with no port means the scheme's default. Spelled out rather than waved through
            // so it cannot match a server listening on some other port.
            if (separator < 0)
                return m_Port == 80;

            return int.TryParse(host.Substring(separator + 1), out var port) && port == m_Port;
        }

        private bool IsSandboxedBrowserAtLocalhost(HttpListenerRequest request, string hostName)
        {
            return AllowSandboxedBrowserClients
                && string.Equals(hostName, LocalhostName, StringComparison.OrdinalIgnoreCase)
                && request.Headers["Origin"] == SandboxedOrigin;
        }

        private bool IsAuthorized(HttpListenerRequest request)
        {
            var header = request.Headers["Authorization"];
            if (string.IsNullOrEmpty(header))
                return false;

            const string prefix = "Bearer ";
            if (!header.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return false;

            var token = header.Substring(prefix.Length).Trim();
            return SecurityTokenManager.ConstantTimeEquals(token, GetToken());
        }

        /// <summary>
        /// Send a structured JSON error response with an explicit HTTP status code.
        /// </summary>
        private async Task SendStatusResponse(HttpListenerResponse response, int statusCode, string error, string details)
        {
            try
            {
                response.StatusCode = statusCode;
                response.ContentType = "application/json";
                var json = JsonConvert.SerializeObject(BaseResponse.Failure(error, details), Formatting.Indented);
                var buffer = Encoding.UTF8.GetBytes(json);
                response.ContentLength64 = buffer.Length;

                await response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
                response.OutputStream.Close();
            }
            catch
            {
                try { response.OutputStream.Close(); } catch { }
            }
        }

        /// <summary>
        /// Parse the <c>omit_nulls</c> query parameter (snake_case like the other query
        /// parameters, e.g. group_by) — the GET-endpoint equivalent of exec's <c>omitNulls</c>
        /// JSON body flag, for /api/job, /api/job/cancel, and /api/progress. Accepted values:
        /// "true"/"false", case-insensitive; absent means false. Any other value (e.g.
        /// <c>omit_nulls=1</c>) is NOT silently coerced to false: it yields false plus a
        /// <paramref name="warning"/> the caller surfaces in the response's "warnings" array, so
        /// an agent learns the accepted spelling instead of guessing why nulls are still present.
        /// (Exec body requests don't need this: JSON deserialization coerces their boolean.)
        /// </summary>
        private static bool ParseOmitNulls(HttpListenerRequest request, out string warning)
        {
            warning = null;
            var raw = request.QueryString["omit_nulls"];
            if (raw == null)
            {
                // A bare "?omit_nulls" (no '=') is not the same as absent: .NET files valueless
                // query tokens under the null key, so look there to warn instead of silently
                // treating an intended opt-in as false.
                var valueless = request.QueryString.GetValues(null);
                if (valueless != null && Array.IndexOf(valueless, "omit_nulls") >= 0)
                    warning = "omit_nulls given without a value ignored; use omit_nulls=true";
                return false;
            }
            if (string.Equals(raw, "true", StringComparison.OrdinalIgnoreCase))
                return true;
            if (string.Equals(raw, "false", StringComparison.OrdinalIgnoreCase))
                return false;

            warning = $"omit_nulls='{raw}' ignored; expected 'true' or 'false'";
            return false;
        }

        /// <summary>
        /// Serialize <paramref name="payload"/> and send it as the JSON body. Null fields are
        /// INCLUDED by default — on /api/job and /api/progress every field is payload (there is
        /// no envelope/payload split like /api/exec's), and an absent key is indistinguishable
        /// from a nonexistent or misspelled one. Callers pass <paramref name="omitNulls"/> from
        /// the endpoint's <c>omit_nulls</c> query parameter (see <see cref="ParseOmitNulls"/>)
        /// to opt out (AUTHAPI-21).
        /// Shares <see cref="SendStatusResponse"/>'s write guard: a failure while writing
        /// (e.g. the client disconnected mid-response) is swallowed here instead of propagating
        /// into the caller's own catch block, which would otherwise attempt a second response on
        /// the now-dead connection.
        /// </summary>
        private async Task SendJsonResponse(HttpListenerResponse response, int statusCode, object payload, bool omitNulls = false)
        {
            try
            {
                var json = JsonConvert.SerializeObject(payload,
                    new JsonSerializerSettings
                    {
                        NullValueHandling = omitNulls ? NullValueHandling.Ignore : NullValueHandling.Include
                    });

                response.StatusCode = statusCode;
                response.ContentType = "application/json";
                var buffer = Encoding.UTF8.GetBytes(json);
                response.ContentLength64 = buffer.Length;

                await response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
                response.OutputStream.Close();
            }
            catch
            {
                try { response.OutputStream.Close(); } catch { }
            }
        }

        /// <summary>
        /// Handle /api/status endpoint - returns basic server health information.
        /// No Editor API access required, always fast response.
        /// </summary>
        private async Task HandleStatusRequest(HttpListenerResponse response)
        {
            try
            {
                var basicStatus = GetServerStatus();
                var json = JsonConvert.SerializeObject(basicStatus, Formatting.Indented);

                response.StatusCode = 200;
                response.ContentType = "application/json";

                var buffer = Encoding.UTF8.GetBytes(json);
                response.ContentLength64 = buffer.Length;

                await response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
                response.OutputStream.Close();
            }
            catch (Exception ex)
            {
                Debug.LogError($"HandleStatusRequest failed: {ex.Message}");
                await SendErrorResponse(response, "Status Error", $"Failed to get status: {ex.Message}");
            }
        }

        /// <summary>
        /// Handle /api/editor_status endpoint - returns detailed Editor state, read by
        /// <see cref="GetEditorStatus"/>.
        /// </summary>
        private async Task HandleEditorStatusRequest(HttpListenerResponse response)
        {
            try
            {
                var editorStatus = await GetEditorStatus();

                // Update with server-specific information
                UpdateStatusWithServerInfo(editorStatus);

                var json = JsonConvert.SerializeObject(editorStatus, Formatting.Indented);

                response.StatusCode = 200;
                response.ContentType = "application/json";

                var buffer = Encoding.UTF8.GetBytes(json);
                response.ContentLength64 = buffer.Length;

                await response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
                response.OutputStream.Close();
            }
            catch (Exception ex)
            {
                var errorMessage = !string.IsNullOrEmpty(ex.Message) ? ex.Message : "[EMPTY EXCEPTION MESSAGE]";
                Debug.LogError($"HandleEditorStatusRequest failed:");
                Debug.LogError($"  Exception Type: {ex.GetType().FullName}");
                Debug.LogError($"  Message: '{errorMessage}'");
                Debug.LogError($"  Full Exception: {ex}");

                await SendErrorResponse(response, "Editor Status Error", $"Failed to get editor status: {errorMessage}");
            }
        }

        /// <summary>
        /// Update StatusResponse with server-specific information.
        /// Used by /api/editor_status to add server metadata to the Editor's state.
        /// </summary>
        private void UpdateStatusWithServerInfo(StatusResponse editorStatus)
        {
            // The response time; see StatusResponse.LastHeartbeat.
            editorStatus.LastHeartbeat = DateTime.UtcNow;
        }

        /// <summary>
        /// Handle /api/commands endpoint - returns the available CLI commands, built by
        /// <see cref="BuildCommandCatalog"/> from the query string.
        /// </summary>
        private async Task HandleCommandsRequest(HttpListenerRequest request, HttpListenerResponse response)
        {
            try
            {
                Dictionary<string, object> catalog;
                try
                {
                    catalog = BuildCommandCatalog(name => request.QueryString[name], ObsoleteCommandNames, BuildServerBlock());
                }
                catch (ArgumentException ex)
                {
                    await SendStatusResponse(response, 400, "Invalid Request", ex.Message);
                    return;
                }

                await SendJsonPayload(response, catalog);
            }
            catch (Exception ex)
            {
                Debug.LogError($"Failed to handle /api/commands request: {ex.Message}");

                // Return error response
                response.StatusCode = 500;
                response.ContentType = "application/json";

                var errorResponse = new { error = "Internal server error", message = ex.Message };
                var errorJson = JsonConvert.SerializeObject(errorResponse);
                var errorBuffer = Encoding.UTF8.GetBytes(errorJson);
                response.ContentLength64 = errorBuffer.Length;

                await response.OutputStream.WriteAsync(errorBuffer, 0, errorBuffer.Length);
                response.OutputStream.Close();
            }
        }

        /// <summary>
        /// The registry's catalog, as /api/commands serves it and the pipeline list_commands
        /// command returns it.
        ///
        /// Optional query parameters (filters combine with AND):
        ///  - detail: 'full' (default) includes parameters and the generated JSON schema;
        ///    'compact' returns a lightweight index (command, name, group, description, tags, package) so a
        ///    client can cheaply browse, then fetch full detail only for the commands it
        ///    intends to invoke; 'tags' returns a 'tags' array of {tag, count} INSTEAD of
        ///    'commands' — the entry point to browsing, so a client needs no catalog of its
        ///    own. Each count covers the tag's whole subtree, so it is exactly what
        ///    'tag=<that tag>' returns. 'groups' returns a 'groups' array of {group, count} the
        ///    same way, one row per group including parents that hold only subgroups; each
        ///    count is exactly what 'group=<that group>' returns. 'obsolete_names', on a server
        ///    that overrides <see cref="ObsoleteCommandNames"/>, returns an 'obsoleteNames' array of
        ///    {oldName, command}: each flat name a command had before groups, and its full name now.
        ///  - query: case-insensitive substring match on full name, description, or any tag.
        ///  - group: scope to a group and its subgroups ('build' matches 'build' and
        ///    'build.target' but not 'buildx').
        ///  - include_tests: 'true' also lists the commands the test assemblies declare, which
        ///    are hidden by default (they stay executable either way).
        ///  - tag: scope to a tag subtree via segment-aware prefix match ('assets' matches
        ///    'assets' and 'assets/import' but not 'assetsx').
        ///  - group_by: 'flat' (default) returns a 'commands' array; 'package', 'tag' and
        ///    'group' return a 'groups' array instead ('tag' and 'group' are nested trees
        ///    mirroring tag/subtag and group.subgroup; untagged or ungrouped commands land in a
        ///    node with an empty tag or group).
        ///  - sort: 'name' (default, by full name) or 'package' (originating package, full name as tiebreak);
        ///    order: 'asc' (default) or 'desc'. Sorting applies to the flat list before
        ///    pagination and grouping.
        ///  - offset / limit: paginate the filtered, sorted flat list (applied before
        ///    grouping, so pages are deterministic); 'total' reports the match count before
        ///    pagination while 'count' is the number of commands actually returned.
        /// </summary>
        /// <param name="option">The value of each option above by its name, or null when it isn't given.</param>
        /// <param name="obsoleteNames">The old command names for detail=obsolete_names; null when there are none to serve.</param>
        /// <param name="server">The responding server's identity, under 'server'; omitted when null.</param>
        /// <exception cref="ArgumentException">An option's value is unknown, or can't be combined with another's.</exception>
        internal static Dictionary<string, object> BuildCommandCatalog(Func<string, string> option,
            IReadOnlyDictionary<string, string> obsoleteNames, object server)
        {
            var detail = option("detail") ?? "full";
            bool fullDetail = false;
            var tagIndex = false;
            var groupIndex = false;
            var obsoleteIndex = false;
            if (string.Equals(detail, "full", StringComparison.OrdinalIgnoreCase))
                fullDetail = true;
            else if (string.Equals(detail, "compact", StringComparison.OrdinalIgnoreCase))
                fullDetail = false;
            else if (string.Equals(detail, "tags", StringComparison.OrdinalIgnoreCase))
                tagIndex = true;
            else if (string.Equals(detail, "groups", StringComparison.OrdinalIgnoreCase))
                groupIndex = true;
            else if (obsoleteNames != null && string.Equals(detail, "obsolete_names", StringComparison.OrdinalIgnoreCase))
                obsoleteIndex = true;
            else
            {
                var accepted = obsoleteNames != null ? "compact, full, tags, groups, obsolete_names" : "compact, full, tags, groups";
                throw new ArgumentException($"Unknown detail value '{detail}'. Accepted values: {accepted}");
            }

            var groupBy = option("group_by") ?? "flat";
            var groupByPackage = string.Equals(groupBy, "package", StringComparison.OrdinalIgnoreCase);
            var groupByTag = string.Equals(groupBy, "tag", StringComparison.OrdinalIgnoreCase);
            var groupByGroup = string.Equals(groupBy, "group", StringComparison.OrdinalIgnoreCase);
            if (!groupByPackage && !groupByTag && !groupByGroup && !string.Equals(groupBy, "flat", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException($"Unknown group_by value '{groupBy}'. Accepted values: flat, package, tag, group");

            // Grouping an index is meaningless — its rows ARE the tags or groups — so refuse
            // the combination rather than silently ignoring one of the two.
            if ((tagIndex || groupIndex) && (groupByPackage || groupByTag || groupByGroup))
            {
                var kind = tagIndex ? "tag" : "group";
                throw new ArgumentException($"detail={kind}s cannot be combined with group_by='{groupBy}'. The {kind} index is "
                    + $"already one row per {kind}; use group_by with detail=compact or detail=full.");
            }

            var sort = option("sort") ?? "name";
            var sortByPackage = string.Equals(sort, "package", StringComparison.OrdinalIgnoreCase);
            if (!sortByPackage && !string.Equals(sort, "name", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException($"Unknown sort value '{sort}'. Accepted values: name, package");

            // A tag belongs to no single package, so there is nothing for a package sort to
            // order a tag index by — refuse rather than silently falling back to name order.
            if ((tagIndex || groupIndex) && sortByPackage)
            {
                var kind = tagIndex ? "tag" : "group";
                throw new ArgumentException($"detail={kind}s cannot be combined with sort=package. A {kind} is not owned by a "
                    + "package; use sort=name, or sort=package with detail=compact or detail=full.");
            }

            // An obsolete name is not a command: it has no package, tag or group to order,
            // group or scope it by.
            if (obsoleteIndex && (groupByPackage || groupByTag || groupByGroup || sortByPackage
                    || option("tag") != null || option("group") != null))
            {
                throw new ArgumentException("detail=obsolete_names cannot be combined with group_by, sort=package, tag or group. "
                    + "Narrow it with query; order, offset and limit apply.");
            }

            var order = option("order") ?? "asc";
            var descending = string.Equals(order, "desc", StringComparison.OrdinalIgnoreCase);
            if (!descending && !string.Equals(order, "asc", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException($"Unknown order value '{order}'. Accepted values: asc, desc");

            var offset = 0;
            if (option("offset") != null
                && (!int.TryParse(option("offset"), out offset) || offset < 0))
                throw new ArgumentException($"Invalid offset value '{option("offset")}'. Expected a non-negative integer");

            int? limit = null;
            if (option("limit") != null)
            {
                if (!int.TryParse(option("limit"), out var parsedLimit) || parsedLimit < 0)
                    throw new ArgumentException($"Invalid limit value '{option("limit")}'. Expected a non-negative integer");
                limit = parsedLimit;
            }

            var query = option("query");
            if (obsoleteIndex)
                return BuildObsoleteNamesIndex(obsoleteNames, query, descending, offset, limit, server);

            var tag = option("tag");
            var group = option("group");
            var includeTests = string.Equals(option("include_tests"), "true", StringComparison.OrdinalIgnoreCase);

            var filtered = CommandRegistry.DiscoverCommands()
                .Where(c => (includeTests || !IsTestCommand(c))
                    && MatchesQuery(c, query) && MatchesTagSubtree(c, tag) && MatchesGroupSubtree(c, group));
            // The tag index is a different projection of the same filtered set: one row per
            // distinct tag, so pagination and sorting apply to TAGS rather than to commands.
            if (tagIndex)
                return BuildTagIndex(filtered.ToList(), descending, offset, limit, server);
            if (groupIndex)
                return BuildGroupIndex(filtered.ToList(), group, descending, offset, limit, server);

            var matching = SortCommands(filtered, sortByPackage, descending).ToList();

            // Paginate the flat, sorted list before any grouping so pages are deterministic.
            IEnumerable<CommandInfo> window = matching.Skip(offset);
            if (limit.HasValue)
                window = window.Take(limit.Value);
            var page = window.ToList();

            Func<CommandInfo, object> project = c =>
                fullDetail ? BuildFullCommandResponse(c) : BuildCompactCommandResponse(c);

            var responseData = new Dictionary<string, object>();
            if (groupByPackage)
                responseData["groups"] = BuildPackageGroups(page, project);
            else if (groupByTag)
                responseData["groups"] = BuildTagTree(page, project);
            else if (groupByGroup)
                responseData["groups"] = BuildGroupTree(page, project);
            else
                responseData["commands"] = page.Select(project).ToList();
            responseData["count"] = page.Count;
            responseData["total"] = matching.Count;
            responseData["offset"] = offset;
            responseData["limit"] = limit;
            if (server != null)
                responseData["server"] = server;
            return responseData;
        }

        /// <summary>
        /// The responding server's identity, carried by every /api/commands response.
        /// </summary>
        private object BuildServerBlock()
        {
            return new
            {
                version = "0.0.1", // TODO: Get from package.json
                port = m_Port,
                startTime = StartedAt
            };
        }

        /// <summary>
        /// Serialize a 200 response body and write it out.
        /// </summary>
        private static async Task SendJsonPayload(HttpListenerResponse response, object payload)
        {
            var json = JsonConvert.SerializeObject(payload, Formatting.Indented);

            response.StatusCode = 200;
            response.ContentType = "application/json";

            var buffer = Encoding.UTF8.GetBytes(json);
            response.ContentLength64 = buffer.Length;

            await response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
            response.OutputStream.Close();
        }

        /// <summary>
        /// The catalog for detail=obsolete_names: one {oldName, command} row per old command name, ordered
        /// by old name. <paramref name="query"/> keeps the rows whose old or full name contains it.
        /// </summary>
        private static Dictionary<string, object> BuildObsoleteNamesIndex(IReadOnlyDictionary<string, string> obsoleteNames,
            string query, bool descending, int offset, int? limit, object server)
        {
            bool Matches(string name) => string.IsNullOrEmpty(query) || name.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;

            IEnumerable<KeyValuePair<string, string>> ordered = obsoleteNames
                .Where(pair => Matches(pair.Key) || Matches(pair.Value))
                .OrderBy(pair => pair.Key, StringComparer.Ordinal);
            if (descending)
                ordered = ordered.Reverse();
            var all = ordered.ToList();

            IEnumerable<KeyValuePair<string, string>> window = all.Skip(offset);
            if (limit.HasValue)
                window = window.Take(limit.Value);
            var page = window.ToList();

            var responseData = new Dictionary<string, object>
            {
                ["obsoleteNames"] = page.Select(pair => new { oldName = pair.Key, command = pair.Value }).ToList(),
                ["count"] = page.Count,
                ["total"] = all.Count,
                ["offset"] = offset,
                ["limit"] = limit
            };
            if (server != null)
                responseData["server"] = server;
            return responseData;
        }

        /// <summary>
        /// The catalog for detail=tags: one row per distinct tag carried by <paramref name="commands"/>,
        /// each with the number of commands in that tag's subtree — so a row's count is exactly
        /// what a follow-up 'tag=&lt;that tag&gt;' request returns.
        /// </summary>
        private static Dictionary<string, object> BuildTagIndex(List<CommandInfo> commands, bool descending, int offset,
            int? limit, object server)
        {
            // Ordinal ordering: tags are validated lowercase '/'-separated paths, so this is both
            // deterministic and the order a client can reproduce without knowing a culture.
            var distinct = new SortedSet<string>(StringComparer.Ordinal);
            foreach (var command in commands)
            {
                foreach (var tag in command.Tags)
                {
                    if (!string.IsNullOrEmpty(tag))
                        distinct.Add(tag);
                }
            }

            IEnumerable<string> ordered = distinct;
            if (descending)
                ordered = ordered.Reverse();
            var allTags = ordered.ToList();

            IEnumerable<string> window = allTags.Skip(offset);
            if (limit.HasValue)
                window = window.Take(limit.Value);
            var page = window.ToList();

            var responseData = new Dictionary<string, object>
            {
                ["tags"] = page
                    .Select(tag => new
                    {
                        tag,
                        count = commands.Count(c => MatchesTagSubtree(c, tag))
                    })
                    .ToList(),
                ["count"] = page.Count,
                ["total"] = allTags.Count,
                ["offset"] = offset,
                ["limit"] = limit
            };
            if (server != null)
                responseData["server"] = server;
            return responseData;
        }

        /// <summary>
        /// The catalog for detail=groups: one row per group the filtered commands define, parents that hold
        /// only subgroups included (<c>animation</c> for <c>animation.clip</c>), each with the number
        /// of commands in its subtree — exactly what a follow-up 'group=&lt;that group&gt;' returns.
        /// </summary>
        private static Dictionary<string, object> BuildGroupIndex(List<CommandInfo> commands, string scope, bool descending,
            int offset, int? limit, object server)
        {
            // Under a group filter, the parents above it are not part of the answer.
            bool InScope(string group) => string.IsNullOrEmpty(scope) || CommandInfo.IsInGroupSubtree(group, scope);
            var distinct = new SortedSet<string>(StringComparer.Ordinal);
            foreach (var command in commands)
            {
                for (var group = command.Group; !string.IsNullOrEmpty(group);
                     group = CommandInfo.ParentGroup(group))
                {
                    if (!InScope(group) || !distinct.Add(group))
                        break;
                }
            }

            IEnumerable<string> ordered = distinct;
            if (descending)
                ordered = ordered.Reverse();
            var allGroups = ordered.ToList();

            IEnumerable<string> window = allGroups.Skip(offset);
            if (limit.HasValue)
                window = window.Take(limit.Value);
            var page = window.ToList();

            var responseData = new Dictionary<string, object>
            {
                ["groups"] = page
                    .Select(group => new
                    {
                        group,
                        count = commands.Count(c => MatchesGroupSubtree(c, group))
                    })
                    .ToList(),
                ["count"] = page.Count,
                ["total"] = allGroups.Count,
                ["offset"] = offset,
                ["limit"] = limit
            };
            if (server != null)
                responseData["server"] = server;
            return responseData;
        }

        /// <summary>
        /// Build the lightweight index entry for a single command (detail=compact).
        /// </summary>
        private static object BuildCompactCommandResponse(CommandInfo command)
        {
            return new
            {
                command = command.FullName,
                name = command.Name,
                group = command.Group,
                description = command.Description,
                tags = command.Tags,
                package = command.Package
            };
        }

        /// <summary>
        /// Build the complete JSON response object for a single command (detail=full).
        /// </summary>
        private static object BuildFullCommandResponse(CommandInfo command)
        {
            var entry = BuildCommandEntry(command);
            entry["schema"] = JsonSchemaGenerator.GenerateCommandSchema(command);
            return entry;
        }

        /// <summary>
        /// One command's catalog entry: everything /api/commands serves for it except the
        /// generated JSON <c>schema</c>.
        ///
        /// Shared by the catalog and by an argument-error envelope, so a client's entry renderer
        /// consumes both unchanged and the two cannot drift. The error path takes this directly
        /// rather than building the full response and dropping the schema, because generating and
        /// serializing a schema nobody reads is pure waste on the interactive retry path.
        /// </summary>
        private static JObject BuildCommandEntry(CommandInfo command)
        {
            return JObject.FromObject(new
            {
                command = command.FullName,
                name = command.Name,
                group = command.Group,
                description = command.Description,
                tags = command.Tags,
                package = command.Package,
                mainThreadRequired = command.MainThreadRequired,
                parameters = command.Parameters.Select(p => new
                {
                    name = p.Name,
                    description = p.Description,
                    type = p.ParameterType.Name,
                    required = p.Required,
                    defaultValue = p.DefaultValue
                }).ToList()
            });
        }

        /// <summary>
        /// Attaches the bound-parameter echo for raw-form requests, and only for those, leaving
        /// the structured path's envelope unchanged. Such a reply also always names the command it
        /// ran (<see cref="CommandExecutionResponse.EchoCommand"/>).
        ///
        /// The values are exactly what the binder stored, each with its JSON type, so the echo cannot
        /// disagree with what the command executes with. A client that does not bind locally has no
        /// other way to report what the command received.
        /// </summary>
        private static CommandExecutionResponse WithBoundParameters(
            CommandExecutionResponse response, bool rawForm, IReadOnlyDictionary<string, object> bound)
        {
            if (rawForm)
            {
                response.BoundParameters = bound == null ? new JObject() : (JObject)ToJson(bound);
                response.EchoCommand = true;
            }
            return response;
        }

        /// <summary>
        /// A bound value as JSON: a dictionary as an object, a list as an array, anything else as
        /// the value it is. Built directly rather than through a serializer, whose settings a
        /// project can change.
        /// </summary>
        private static JToken ToJson(object value)
        {
            switch (value)
            {
                case IReadOnlyDictionary<string, object> values:
                    var obj = new JObject();
                    foreach (var pair in values)
                        obj[pair.Key] = ToJson(pair.Value);
                    return obj;
                case IReadOnlyList<object> items:
                    var array = new JArray();
                    foreach (var item in items)
                        array.Add(ToJson(item));
                    return array;
                default:
                    return new JValue(value);
            }
        }

        /// <summary>
        /// Renders argument problems as English prose for <c>errorDetails</c>.
        ///
        /// This is the fallback, not the primary channel: a client that understands
        /// <c>argProblems</c> renders and localizes them itself. This text serves curl, MCP, and
        /// any client that meets a <c>kind</c> it does not recognize, so it must stay useful on
        /// its own.
        /// </summary>
        /// <param name="structured">The problems are about the keys of a structured <c>parameters</c>
        /// object rather than flags, so names are quoted as keys: a client that sent JSON cannot pass
        /// the <c>--name</c> a flag spelling would suggest.</param>
        internal static string DescribeArgProblems(CommandInfo command, List<ArgProblem> problems, bool structured = false)
        {
            if (problems == null || problems.Count == 0)
                return "Invalid arguments";

            var parts = new List<string>(problems.Count);
            foreach (var problem in problems)
            {
                switch (problem.Kind)
                {
                    case ArgProblemKind.EmptyName:
                        parts.Add($"'{problem.Token}' is not a valid flag: the name is empty.");
                        break;
                    case ArgProblemKind.EmptyValue:
                        parts.Add($"--{problem.Name} needs a value.");
                        break;
                    case ArgProblemKind.SingleDash:
                        parts.Add($"'{problem.Token}' is not a valid flag: single-dash flags are not supported. Use a double dash, or pass it after -- to send it as a value.");
                        break;
                    case ArgProblemKind.Duplicate:
                        parts.Add($"--{problem.Name} was given more than once.");
                        break;
                    case ArgProblemKind.UnknownName:
                        parts.Add(structured
                            ? problem.Suggestion != null
                                ? $"{command.FullName} has no parameter '{problem.Name}'. Did you mean '{problem.Suggestion}'?"
                                : $"{command.FullName} has no parameter '{problem.Name}'. Valid parameters: {DescribeParameterNames(command, string.Empty)}."
                            : problem.Suggestion != null
                                ? $"{command.FullName} has no parameter --{problem.Name}. Did you mean --{problem.Suggestion}?"
                                : $"{command.FullName} has no parameter --{problem.Name}. Valid parameters: {DescribeParameterNames(command, "--")}.");
                        break;
                    case ArgProblemKind.BareAssignment:
                        parts.Add($"'{problem.Token}' looks like a flag written without dashes. Use --name value instead.");
                        break;
                    case ArgProblemKind.ExcessPositional:
                        parts.Add($"{command.FullName} takes {problem.Capacity} argument(s) but {problem.Given} were given (starting at '{problem.Token}').");
                        break;
                    case ArgProblemKind.PositionalConflict:
                        parts.Add($"--{problem.Name} is already set by an explicit flag, so there is no slot for '{problem.Token}'.");
                        break;
                    case ArgProblemKind.TypeMismatch:
                        parts.Add($"--{problem.Name} expects {problem.ExpectedType}, but got '{problem.Token}'.{DescribeValidValues(command, problem.Name)}");
                        break;
                    default:
                        parts.Add($"{problem.Kind}: {problem.Token ?? problem.Name}");
                        break;
                }
            }

            return string.Join(" ", parts);
        }

        /// <summary>Every declared parameter of the command, each after <paramref name="prefix"/>, or "(none)".</summary>
        private static string DescribeParameterNames(CommandInfo command, string prefix)
        {
            if (command.Parameters.Count == 0)
                return "(none)";
            return string.Join(", ", command.Parameters.Select(p => prefix + p.Name));
        }

        /// <summary>
        /// For an enum parameter, the legal names, following the same enumerate-the-valid-set
        /// convention used for unknown command names and query values. Empty for every other type.
        /// </summary>
        private static string DescribeValidValues(CommandInfo command, string parameterName)
        {
            foreach (var parameter in command.Parameters)
            {
                if (!string.Equals(parameter.Name, parameterName, StringComparison.OrdinalIgnoreCase))
                    continue;
                var type = Nullable.GetUnderlyingType(parameter.ParameterType) ?? parameter.ParameterType;
                if (type.IsEnum)
                    return " Valid values: " + string.Join(", ", Enum.GetNames(type)) + ".";
                break;
            }
            return string.Empty;
        }

        /// <summary>
        /// Order the filtered command list: by full name, or by originating package with full name as
        /// tiebreak. Both keys follow the requested direction.
        /// </summary>
        private static IEnumerable<CommandInfo> SortCommands(IEnumerable<CommandInfo> commands, bool byPackage, bool descending)
        {
            if (byPackage)
            {
                var byPkg = descending
                    ? commands.OrderByDescending(c => c.Package ?? string.Empty, StringComparer.Ordinal)
                    : commands.OrderBy(c => c.Package ?? string.Empty, StringComparer.Ordinal);
                return descending
                    ? byPkg.ThenByDescending(c => c.FullName, StringComparer.Ordinal)
                    : byPkg.ThenBy(c => c.FullName, StringComparer.Ordinal);
            }
            return descending
                ? commands.OrderByDescending(c => c.FullName, StringComparer.Ordinal)
                : commands.OrderBy(c => c.FullName, StringComparer.Ordinal);
        }

        /// <summary>
        /// Case-insensitive substring match on the command's full name (so its group and name), its
        /// description, or any tag. A null/empty query matches everything.
        /// </summary>
        private static bool MatchesQuery(CommandInfo command, string query)
        {
            if (string.IsNullOrEmpty(query))
                return true;
            return command.FullName.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0
                || command.Description.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0
                || command.Tags.Any(t => t.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        /// <summary>
        /// Segment-aware tag subtree match: 'assets' matches tags 'assets' and 'assets/import'
        /// but not 'assetsx'. A null/empty tag matches everything.
        /// </summary>
        private static bool MatchesTagSubtree(CommandInfo command, string tag)
        {
            return command.HasTagInSubtree(tag);
        }

        /// <summary>
        /// Whether a test assembly declares the command: the fixtures this package's own tests
        /// register. Recognised by assembly, so no group name is reserved for them.
        /// </summary>
        internal static bool IsTestCommand(CommandInfo command)
        {
            return command.Package != null
                && (command.Package.StartsWith("Unity.Pipeline.Tests", StringComparison.Ordinal)
                    || command.Package.StartsWith("Unity.Pipeline.Samples.Tests", StringComparison.Ordinal));
        }

        /// <summary>
        /// Segment-aware group subtree match: 'build' matches groups 'build' and 'build.target'
        /// but not 'buildx'. A null/empty group matches everything.
        /// </summary>
        private static bool MatchesGroupSubtree(CommandInfo command, string group)
        {
            if (string.IsNullOrEmpty(group))
                return true;
            return CommandInfo.IsInGroupSubtree(command.Group, group);
        }

        /// <summary>
        /// Group a page of commands by originating package, sorted by package name.
        /// </summary>
        private static List<object> BuildPackageGroups(List<CommandInfo> commands, Func<CommandInfo, object> project)
        {
            return commands
                .GroupBy(c => c.Package ?? string.Empty)
                .OrderBy(g => g.Key, StringComparer.Ordinal)
                .Select(g => (object)new
                {
                    package = g.Key,
                    count = g.Count(),
                    commands = g.Select(project).ToList()
                })
                .ToList();
        }

        /// <summary>
        /// Node of the group_by=tag and group_by=group trees. In the tag tree a command appears in
        /// the node of each tag it carries; untagged commands land in a top-level node with an
        /// empty tag. In the group tree it appears once, in its group's node.
        /// </summary>
        private class TagTreeNode
        {
            public readonly List<CommandInfo> Commands = new List<CommandInfo>();
            public readonly SortedDictionary<string, TagTreeNode> Children =
                new SortedDictionary<string, TagTreeNode>(StringComparer.Ordinal);
        }

        /// <summary>
        /// Build the nested group_by=tag tree mirroring the tag/subtag hierarchy. Each node's
        /// 'count' covers its whole subtree (a command tagged with several tags under the same
        /// node counts once per tag entry).
        /// </summary>
        private static List<object> BuildTagTree(List<CommandInfo> commands, Func<CommandInfo, object> project)
        {
            var root = new TagTreeNode();
            foreach (var command in commands)
            {
                if (command.Tags.Count == 0)
                {
                    GetOrAddChild(root, string.Empty).Commands.Add(command);
                    continue;
                }
                foreach (var tag in command.Tags)
                {
                    var node = root;
                    foreach (var segment in tag.Split('/'))
                        node = GetOrAddChild(node, segment);
                    node.Commands.Add(command);
                }
            }
            return root.Children
                .Select(kv => ToTagGroup(kv.Key, kv.Value, project))
                .ToList();
        }

        /// <summary>
        /// Build the nested group_by=group tree mirroring group.subgroup. A command has one group,
        /// so it appears exactly once; commands without a group land in a top-level node with an
        /// empty group. Each node's 'count' covers its whole subtree.
        /// </summary>
        private static List<object> BuildGroupTree(List<CommandInfo> commands, Func<CommandInfo, object> project)
        {
            var root = new TagTreeNode();
            foreach (var command in commands)
            {
                var node = command.Group == null ? GetOrAddChild(root, string.Empty) : root;
                if (command.Group != null)
                {
                    foreach (var segment in command.Group.Split('.'))
                        node = GetOrAddChild(node, segment);
                }
                node.Commands.Add(command);
            }
            return root.Children
                .Select(kv => ToGroupNode(kv.Key, kv.Value, project))
                .ToList();
        }

        private static object ToGroupNode(string path, TagTreeNode node, Func<CommandInfo, object> project)
        {
            return new
            {
                group = path,
                count = SubtreeCommandCount(node),
                commands = node.Commands.Select(project).ToList(),
                children = node.Children
                    .Select(kv => ToGroupNode(path.Length == 0 ? kv.Key : path + "." + kv.Key, kv.Value, project))
                    .ToList()
            };
        }

        private static TagTreeNode GetOrAddChild(TagTreeNode node, string key)
        {
            if (!node.Children.TryGetValue(key, out var child))
            {
                child = new TagTreeNode();
                node.Children[key] = child;
            }
            return child;
        }

        private static object ToTagGroup(string path, TagTreeNode node, Func<CommandInfo, object> project)
        {
            return new
            {
                tag = path,
                count = SubtreeCommandCount(node),
                commands = node.Commands.Select(project).ToList(),
                children = node.Children
                    .Select(kv => ToTagGroup(path.Length == 0 ? kv.Key : path + "/" + kv.Key, kv.Value, project))
                    .ToList()
            };
        }

        private static int SubtreeCommandCount(TagTreeNode node)
        {
            return node.Commands.Count + node.Children.Values.Sum(SubtreeCommandCount);
        }

        /// <summary>
        /// Handle 404 Not Found responses.
        /// </summary>
        private async Task HandleNotFound(HttpListenerResponse response)
        {
            response.StatusCode = 404;
            response.ContentType = "text/plain";

            var responseText = "Not Found";
            var buffer = Encoding.UTF8.GetBytes(responseText);
            response.ContentLength64 = buffer.Length;

            await response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
            response.OutputStream.Close();
        }

        /// <summary>
        /// Handle 405 Method Not Allowed responses.
        /// </summary>
        private async Task HandleMethodNotAllowed(HttpListenerResponse response, string allowedMethod)
        {
            await SendErrorResponse(response, "Method Not Allowed",
                $"This endpoint only supports {allowedMethod} requests", statusCode: 405);
        }

        /// <summary>
        /// Handle /api/test-status endpoint - returns status of async test execution, read by
        /// <see cref="GetTestStatus"/>.
        /// </summary>
        private async Task HandleTestStatusRequest(HttpListenerResponse response)
        {
            try
            {
                var jsonResponse = JsonConvert.SerializeObject(GetTestStatus());

                response.StatusCode = 200;
                response.ContentType = "application/json";

                var buffer = Encoding.UTF8.GetBytes(jsonResponse);
                response.ContentLength64 = buffer.Length;

                await response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
                response.OutputStream.Close();
            }
            catch (Exception ex)
            {
                Debug.LogError($"HandleTestStatusRequest failed: {ex.Message}");
                await SendErrorResponse(response, "Test Status Error", $"Failed to get test status: {ex.Message}");
            }
        }

        /// <summary>
        /// Project progress to the <c>{title,info,current,total,pct}</c> wire shape shared by
        /// /api/progress and a running job's <c>progress</c> field — kept as one helper so the two
        /// can't drift out of sync with each other or the documented contract.
        /// </summary>
        internal static object BuildProgressPayload(CommandProgress progress)
        {
            if (progress == null)
                return null;

            return new
            {
                title = progress.Title,
                info = progress.Info,
                current = progress.Current,
                total = progress.Total,
                pct = progress.Fraction
            };
        }

        /// <summary>A detached job: its execution, and what it wrote — its output and why it failed.</summary>
        private sealed class Job
        {
            public CommandExecution Execution;
            public readonly StringWriter Out = new StringWriter();
            public readonly StringWriter Error = new StringWriter();
        }

        /// <summary>Forgets the ended jobs beyond the retained count, oldest first, and those that expired.</summary>
        private void PruneJobs()
        {
            var now = DateTime.UtcNow;
            var ended = m_Jobs.Values
                .Where(job => job.Execution.State == CommandExecutionState.Ended)
                .OrderBy(job => job.Execution.EndedAt)
                .ToList();

            var excess = ended.Count - MaxRetainedEndedJobs;
            for (var i = 0; i < ended.Count; i++)
            {
                var execution = ended[i].Execution;
                if (i < excess || now - execution.EndedAt > s_JobRetention)
                    m_Jobs.TryRemove(execution.Id, out _);
            }
        }

        /// <summary>Finds a job by the id a client holds, pruning the ones that aged out first.</summary>
        private bool TryGetJob(string id, out Job job)
        {
            PruneJobs();
            job = null;
            return CommandExecutionId.TryParse(id, out var executionId) && m_Jobs.TryGetValue(executionId, out job);
        }

        /// <summary>Test hook: forget every job.</summary>
        internal void ResetJobs()
        {
            m_Jobs.Clear();
        }

        /// <summary>
        /// Serialize one job to the wire shape shared by the job endpoints. An ended job completed
        /// when its command returned (its output is its result, a returned failure included),
        /// failed when it threw or returned a failure that fails the envelope (its output is then
        /// still its result), and was canceled when its cancellation took effect.
        /// </summary>
        private object BuildJobResponse(Job job, bool omitNulls, string warning = null)
        {
            var execution = job.Execution;
            // State first: every other field is set before it moves past it.
            var state = execution.State;

            string wireState;
            object result = null;
            string error = null;
            string errorDetails = null;
            string errorCode = null;
            switch (state)
            {
                case CommandExecutionState.Queued:
                    wireState = "queued";
                    break;
                case CommandExecutionState.Running:
                    wireState = "running";
                    break;
                default:
                    var written = job.Out.ToString();
                    if (execution.Result == ExitCode.Cancelled)
                    {
                        wireState = "canceled";
                    }
                    else if (!string.IsNullOrEmpty(written) && execution.ReturnedFailure is { } failure)
                    {
                        // Answered like the synchronous reply: failed, with the command's payload.
                        wireState = "failed";
                        error = failure.Error;
                        errorDetails = failure.ErrorDetails;
                        errorCode = failure.ErrorCode;
                        result = ExecResponseSerializer.ResultFromOutput(written, omitNulls);
                    }
                    else if (!string.IsNullOrEmpty(written))
                    {
                        wireState = "completed";
                        // A null result as null, which the reply's settings keep or drop as they
                        // always did.
                        result = written == "null" ? null : ExecResponseSerializer.ResultFromOutput(written, omitNulls);
                    }
                    else
                    {
                        wireState = "failed";
                        error = execution.Result == ExitCode.InvalidUsage
                            ? "Parameter Validation Failed"
                            : "Command Execution Failed";
                        errorDetails = ErrorText(job.Error);
                    }
                    break;
            }

            return new
            {
                jobId = execution.Id.ToString(),
                command = execution.Command,
                state = wireState,
                cancellationRequested = execution.CancellationRequested,
                enqueuedAt = execution.EnqueuedAt,
                startedAt = execution.StartedAt,
                completedAt = execution.EndedAt,
                result,
                error,
                errorDetails,
                errorCode,
                progress = state == CommandExecutionState.Running ? BuildProgressPayload(execution.Progress) : null,
                // Corrective guidance for the caller (e.g. an unparseable omit_nulls value);
                // null when there is nothing to say.
                warnings = warning == null ? null : new[] { warning }
            };
        }

        /// <summary>What a command wrote to its Error, without the line break that ends it; null when nothing.</summary>
        private static string ErrorText(StringWriter error)
        {
            var text = error?.ToString().TrimEnd('\r', '\n');
            return string.IsNullOrEmpty(text) ? null : text;
        }

        /// <summary>
        /// Handle GET /api/job?id=… — a detached job's state, progress, and (once terminal)
        /// its retained result (CLI-335 poll/reattach). Served on the listener thread.
        /// </summary>
        private async Task HandleJobStatusRequest(HttpListenerRequest request, HttpListenerResponse response)
        {
            try
            {
                var id = request.QueryString["id"];
                if (string.IsNullOrEmpty(id))
                {
                    await SendStatusResponse(response, 400, "Bad Request", "Query parameter 'id' is required");
                    return;
                }
                if (!TryGetJob(id, out var job))
                {
                    await SendStatusResponse(response, 404, "Job Not Found", $"No job with id '{id}' (jobs do not survive domain reloads and are pruned after retention)");
                    return;
                }

                var omitNulls = ParseOmitNulls(request, out var omitNullsWarning);
                await SendJsonResponse(response, 200, BuildJobResponse(job, omitNulls, omitNullsWarning), omitNulls);
            }
            catch (Exception ex)
            {
                Debug.LogError($"HandleJobStatusRequest failed: {ex.Message}");
                await SendErrorResponse(response, "Job Status Error", $"Failed to get job status: {ex.Message}");
            }
        }

        /// <summary>
        /// Handle POST /api/job/cancel — request cancellation of a detached job (CLI-335).
        /// A queued job never starts; a running job gets the cooperative
        /// PipelineCancellation flag (synchronous code cannot be aborted from outside).
        /// </summary>
        private async Task HandleJobCancelRequest(HttpListenerRequest request, HttpListenerResponse response)
        {
            try
            {
                if (request.ContentLength64 > MaxCancelBodyBytes)
                {
                    await DrainRequestBody(request, MaxDrainBytes);
                    await SendStatusResponse(response, 413, "Payload Too Large",
                        $"Request body exceeds the maximum allowed size of {MaxCancelBodyBytes} bytes");
                    return;
                }

                string body;
                try
                {
                    using (var limited = new MaxLengthStream(request.InputStream, MaxCancelBodyBytes, leaveOpen: true))
                    using (var reader = new StreamReader(limited))
                    {
                        body = await reader.ReadToEndAsync();
                    }
                }
                catch (RequestTooLargeException ex)
                {
                    await DrainRequestBody(request, MaxDrainBytes);
                    await SendStatusResponse(response, 413, "Payload Too Large", ex.Message);
                    return;
                }
                finally
                {
                    request.InputStream.Dispose();
                }

                string id = null;
                try
                {
                    var parsed = JObject.Parse(string.IsNullOrEmpty(body) ? "{}" : body);
                    id = parsed["id"]?.ToString();
                }
                catch (JsonException)
                {
                    // Fall through to the missing-id error below.
                }

                if (string.IsNullOrEmpty(id))
                {
                    await SendStatusResponse(response, 400, "Bad Request", "Request body must be JSON with an 'id' field");
                    return;
                }
                if (!TryGetJob(id, out var job))
                {
                    await SendStatusResponse(response, 404, "Job Not Found", $"No job with id '{id}'");
                    return;
                }

                // A job that already ended can't be cancelled; it is answered as it ended.
                job.Execution.Cancel();

                var omitNulls = ParseOmitNulls(request, out var omitNullsWarning);
                await SendJsonResponse(response, 200, BuildJobResponse(job, omitNulls, omitNullsWarning), omitNulls);
            }
            catch (Exception ex)
            {
                Debug.LogError($"HandleJobCancelRequest failed: {ex.Message}");
                await SendErrorResponse(response, "Job Cancel Error", $"Failed to cancel job: {ex.Message}");
            }
        }

        /// <summary>
        /// Handle /api/progress endpoint — the currently executing command's task progress
        /// (CLI-488): the server side of the CLI's terminal progress bars, the pipeline
        /// equivalent of EditorUtility.DisplayProgressBar.
        ///
        /// Served entirely on the listener thread from CliProgress's lock-protected snapshot —
        /// never marshaled to the main thread — so it stays responsive while a long synchronous
        /// command has the main thread blocked (exactly when progress matters most).
        ///
        /// Contract (all progress fields optional; pct is 0–1; an idle server returns
        /// <c>{"active":false,"progress":null}</c>):
        /// <code>{"active":true,"progress":{"title":"…","info":"…","current":42,"total":100,"pct":0.42}}</code>
        /// </summary>
        private async Task HandleProgressRequest(HttpListenerRequest request, HttpListenerResponse response)
        {
            try
            {
                var active = Progress.IsActive;
                var progress = active ? BuildProgressPayload(CommandProgress.From(Progress.Current)) : null;

                var omitNulls = ParseOmitNulls(request, out var omitNullsWarning);
                await SendJsonResponse(response, 200, new
                {
                    active,
                    progress,
                    // Corrective guidance for the caller (e.g. an unparseable omit_nulls value);
                    // null when there is nothing to say.
                    warnings = omitNullsWarning == null ? null : new[] { omitNullsWarning }
                }, omitNulls);
            }
            catch (Exception ex)
            {
                Debug.LogError($"HandleProgressRequest failed: {ex.Message}");
                await SendErrorResponse(response, "Progress Error", $"Failed to get progress: {ex.Message}");
            }
        }

        /// <summary>
        /// Handle /api/dialog endpoint — currently-open modal dialogs, mirroring /api/progress's
        /// shape and thread-safety: served entirely from DialogStateTracker's lock-protected
        /// state, never marshaled to the main thread, so it answers even while a synchronous
        /// command has the main thread blocked inside a modal dialog (exactly when it matters
        /// most).
        ///
        /// Contract: {"active":true,"dialogs":[{"id","source","title","message","level","buttons","openedAt","dismissedAt"}]}
        ///
        /// "active": false means no dialog of a COVERED kind is open, not that the Editor isn't
        /// blocked by some other popup — coverage is mechanism-based (see
        /// UnityEditor.EditorDialogEvents' remarks in trunk): native message boxes and EditorWindow
        /// modals are covered; OS file/folder pickers and any other dialog mechanism are not. A
        /// caller still needs a fallback signal (e.g. a command timeout) for the uncovered cases.
        /// </summary>
        private async Task HandleDialogRequest(HttpListenerResponse response)
        {
            try
            {
                var dialogs = Dialogs.CurrentlyOpen;
                await SendJsonResponse(response, 200, new
                {
                    active = dialogs.Count > 0,
                    dialogs = dialogs.Select(BuildDialogPayload).ToList()
                });
            }
            catch (Exception ex)
            {
                Debug.LogError($"HandleDialogRequest failed: {ex.Message}");
                await SendErrorResponse(response, "Dialog Error", $"Failed to get dialog state: {ex.Message}");
            }
        }

        /// <summary>
        /// Shared wire projection of a DialogInfo, used by /api/dialog, dialogsDuringExecution, the
        /// busy-gate, and (internal, via InternalsVisibleTo) EditorPipelineServer's /api/status.
        /// </summary>
        internal static object BuildDialogPayload(DialogInfo info) => new
        {
            id = info.Id,
            source = info.Source,
            title = info.Title,
            message = info.Message,
            level = info.Level,
            buttons = info.Buttons,
            openedAt = info.OpenedAtUtc,
            dismissedAt = info.DismissedAtUtc
        };

        /// <summary>Human-readable one-line summary of a dialog for the busy-gate message. Message/Level are null for a ManagedCustomWindow source, so both are omitted when absent.</summary>
        internal static string FormatDialogSummary(DialogInfo info)
        {
            var levelPrefix = string.IsNullOrEmpty(info.Level) ? "" : $"[{info.Level}] ";
            var messageSuffix = string.IsNullOrEmpty(info.Message) ? "" : $": {info.Message}";
            return $"{levelPrefix}{info.Title}{messageSuffix}";
        }

        /// <summary>
        /// Handle /api/exec endpoint - execute CLI commands with parameters.
        /// </summary>
        private async Task HandleExecRequest(HttpListenerRequest request, HttpListenerResponse response)
        {
            var cmd = "";
            // Stay false for pre-parse failures (413/empty/invalid JSON), which cannot honor the
            // request's flags; set from the request as soon as it deserializes — before structure
            // validation, so a verbose request gets a verbose validation failure. (AUTHAPI-21)
            var verbose = false;
            var omitNulls = false;
            // Whether the request named its command on a command line (argv/commandLine). Such a
            // reply always carries the resolved command; see EchoIfRaw.
            var rawForm = false;
            string requestBody = null;
            // Hoisted so the catch blocks below can report the caller context the request sent.
            CommandExecutionRequest commandRequest = null;

            // Marks a reply to a raw request so it carries `command` in every mode, not only
            // verbose: the caller named the command by position and learns here what it resolved to.
            // Structured requests keep the lean envelope.
            CommandExecutionResponse EchoIfRaw(CommandExecutionResponse reply)
            {
                reply.EchoCommand = rawForm;
                return reply;
            }

            try
            {
                // Reject oversized bodies up front via Content-Length (cheap, before reading anything).
                if (request.ContentLength64 > MaxRequestBodyBytes)
                {
                    // Drain the body first so the client can read the 413 (see DrainRequestBody).
                    await DrainRequestBody(request, MaxDrainBytes);
                    await SendExecResponse(response, 413,
                        BaseResponse.Failure("Payload Too Large",
                            $"Request body exceeds the maximum allowed size of {MaxRequestBodyBytes} bytes"), null);
                    return;
                }

                // Read request body, enforcing the same cap while reading in case Content-Length is
                // absent or untruthful (e.g. chunked transfer-encoding).
                try
                {
                    // leaveOpen: on an over-limit read we still need the raw InputStream open to drain
                    // the unsent remainder before responding, so it must outlive the reader/wrapper.
                    using (var limited = new MaxLengthStream(request.InputStream, MaxRequestBodyBytes, leaveOpen: true))
                    using (var reader = new StreamReader(limited))
                    {
                        requestBody = await reader.ReadToEndAsync();
                    }
                }
                catch (RequestTooLargeException ex)
                {
                    // Drain the body first so the client can read the 413 (see DrainRequestBody).
                    await DrainRequestBody(request, MaxDrainBytes);
                    await SendExecResponse(response, 413,
                        BaseResponse.Failure("Payload Too Large", ex.Message), null);
                    return;
                }
                finally
                {
                    // MaxLengthStream left the InputStream open; dispose it now that any read/drain is done.
                    request.InputStream.Dispose();
                }

                if (string.IsNullOrEmpty(requestBody))
                {
                    await SendExecResponse(response, 400,
                        BaseResponse.Failure("Bad Request", "Request body is required"), requestBody);
                    return;
                }

                // Parse command request
                try
                {
                    commandRequest = JsonConvert.DeserializeObject<CommandExecutionRequest>(requestBody);
                }
                catch (JsonException ex)
                {
                    await SendExecResponse(response, 400,
                        BaseResponse.Failure("Invalid JSON", $"Failed to parse request body: {ex.Message}"), requestBody);
                    return;
                }

                // A body of literal JSON "null" parses without error but deserializes to null —
                // reject it here so everything below can rely on a non-null request (previously
                // this slid through the null-conditional validation and NRE'd at the Command read).
                if (commandRequest == null)
                {
                    await SendExecResponse(response, 400,
                        BaseResponse.Failure("Invalid Request", "Request body must be a JSON object"), requestBody);
                    return;
                }

                // Honor the reply-shape flags from here on: the request has deserialized, so even
                // the structure-validation failure below can respect them.
                verbose = commandRequest.Verbose;
                omitNulls = commandRequest.OmitNulls;
                rawForm = commandRequest.IsRawForm;

                // Validate request structure
                var requestValidationError = commandRequest.Validate();
                if (!string.IsNullOrEmpty(requestValidationError))
                {
                    await SendExecResponse(response, 400,
                        BaseResponse.Failure("Invalid Request", requestValidationError), requestBody, verbose, omitNulls);
                    return;
                }

                // Raw command-line forms. Tokenize `commandLine`, or take `argv` as-is (it is
                // already split; re-splitting would corrupt any value containing a space). The
                // tokens go to the executor as the command request's command and arguments, and
                // it resolves the leading ones; a run and a job take the same tokens.
                //
                // Placed after Validate() so transport concerns do not depend on the command
                // registry, after the reply-shape flags are captured so a malformed line still
                // honours verbose/omitNulls, and before ResolveCommand so an unknown raw command
                // name produces the same Command Not Found envelope as the structured path.
                List<string> rawTokens = null;
                if (commandRequest.IsRawForm)
                {
                    if (commandRequest.Argv != null)
                    {
                        rawTokens = commandRequest.Argv;
                    }
                    else if (!CommandLineTokenizer.TryTokenize(commandRequest.CommandLine, out rawTokens, out var tokenizeError))
                    {
                        // Reported as a malformed request shape, not as an argument error: no
                        // command was resolved, so there is no schema and no argProblems to carry.
                        // INVALID_COMMAND_ARGS promises a client both of those.
                        await SendExecResponse(response, 400,
                            BaseResponse.Failure("Invalid Request", tokenizeError),
                            requestBody, verbose, omitNulls);
                        return;
                    }

                    // A blank first token is as malformed as no token at all, and it is reachable:
                    // a commandLine of `"" --message hi` is a NONBLANK source string, so Validate()
                    // passes, yet it tokenizes to an empty command name. Left unchecked that became
                    // a Command Not Found, telling the caller an empty command is merely
                    // unavailable. `argv` with an empty first element is already rejected by
                    // Validate(), so both raw forms answer the same way here.
                    if (rawTokens.Count == 0 || string.IsNullOrWhiteSpace(rawTokens[0]))
                    {
                        await SendExecResponse(response, 400,
                            BaseResponse.Failure("Invalid Request", "Command name is required"),
                            requestBody, verbose, omitNulls);
                        return;
                    }

                    // Hand the executor the tokens, so it resolves and binds one shape whichever
                    // raw form the request used.
                    commandRequest.Argv = rawTokens;

                    // A group named with nothing after it is answered with its listing, by the
                    // executor, at once: a listing is not a job. Refused with an error code, so a
                    // client does not read it as a server too old for raw requests.
                    var group = commandRequest.Job ? TryResolveRawCommand(rawTokens, CommandRegistry.Index).ListedGroup : null;
                    if (group != null)
                    {
                        var notACommand = EchoIfRaw(CommandExecutionResponse.CmdFailure(group, "Invalid Request",
                            $"'{group}' is a group, not a command, so it cannot run as a job. " +
                            $"Name a command in it, as '{group} <name>'."));
                        notACommand.ErrorCode = GroupNotACommandErrorCode;
                        await SendExecResponse(response, 400, notACommand, requestBody, verbose, omitNulls);
                        return;
                    }

                    cmd = rawTokens[0];
                }
                else
                {
                    cmd = commandRequest.Command;
                }

                // The executor finds the command, binds its arguments and refuses one that can't
                // run now, all before anything runs or a job is created, and throws each refusal
                // for the catches below to answer. A command line goes to it as argv; a structured
                // request's parameters as their JSON text, bound by name as they are. The
                // executor's options lead the arguments, asking for what the command returns as
                // the answer will carry it.
                CommandRequest NewRequest(TextWriter output, TextWriter error, params string[] options)
                {
                    var arguments = new List<string>(options);
                    if (rawTokens != null)
                        arguments.AddRange(rawTokens.GetRange(1, rawTokens.Count - 1));
                    return new CommandRequest(cmd, arguments, null, output, error)
                    {
                        Timeout = PipelineTimeout.FromRequest(commandRequest.Timeout),
                        Requester = new Requester(commandRequest.ClientSource, commandRequest.ClientName, commandRequest.CommandId,
                            commandRequest.Caller, commandRequest.Skill),
                        ParametersJson = rawTokens == null ? (commandRequest.Parameters ?? new JObject()).ToString(Formatting.None) : null,
                    };
                }

                // Detached job (CLI-335): reply with a job id immediately and run the command
                // in the background — the client polls GET /api/job?id=… to reattach and
                // collect the result after its own HTTP timeout would have expired. The job and
                // what it writes are kept here for that poll.
                if (commandRequest.Job)
                {
                    var job = new Job();
                    // A job's result has no envelope: every field is payload, a nested response's
                    // metadata included, so it is written verbose with its nulls kept until a poll
                    // asks to omit them.
                    var executionRequest = NewRequest(job.Out, job.Error, CommandRequest.VerboseOption);
                    // A job runs until it ends, however long its request allowed: its client
                    // collects it later rather than waiting on it.
                    executionRequest.Timeout = Timeout.InfiniteTimeSpan;

                    PruneJobs();
                    job.Execution = Executor.Execute(executionRequest);
                    m_Jobs[job.Execution.Id] = job;

                    // Standard exec envelope; the job handle is the command's "result".
                    await SendExecResponse(response, 200,
                        WithBoundParameters(
                            CommandExecutionResponse.CmdSuccess(job.Execution.Command,
                                new { jobId = job.Execution.Id.ToString(), state = "queued" }), rawForm,
                            job.Execution.Parameters),
                        requestBody, verbose, omitNulls, commandRequest);
                    return;
                }

                // One command at a time, exactly as when the accept loop was serial.
                var output = new StringWriter();
                var error = new StringWriter();
                var options = new List<string>(2);
                if (verbose)
                    options.Add(CommandRequest.VerboseOption);
                if (omitNulls)
                    options.Add(CommandRequest.OmitNullsOption);
                var execution = Executor.Execute(NewRequest(output, error, options.ToArray()));
                var result = await execution;
                cmd = execution.Command;

                if (execution.ListedGroup != null)
                {
                    // A listing binds nothing, but it is a raw-form success, and every raw-form
                    // success echoes its bound parameters: a client reads a missing echo as a
                    // server too old to parse command lines. Not a command's transaction.
                    var listing = WithBoundParameters(CommandExecutionResponse.CmdSuccess(cmd, new JRaw(output.ToString())),
                        rawForm, null);
                    listing.ListedGroup = execution.ListedGroup;
                    await SendExecResponse(response, 200, listing, requestBody, verbose, omitNulls);
                    return;
                }

                // A command that returned answers with what it returned, a returned failure
                // included; one that threw answers with why. The output is already the JSON this
                // reply's flags give the result, so it is embedded as it is; a null result as
                // null, which the envelope's own settings keep or drop as they always did.
                var written = output.ToString();
                if (written.Length > 0 && execution.ReturnedFailure is { } failure)
                {
                    // A returned failure that asks to fail the envelope: the reply fails with its
                    // status and category, and still carries what the command returned.
                    var failed = CommandExecutionResponse.CmdFailure(cmd, failure.Error, failure.ErrorDetails);
                    failed.ErrorCode = failure.ErrorCode;
                    failed.Result = new JRaw(written);
                    failed.KeepsResultOnFailure = true;
                    await SendExecResponse(response, failure.FailureStatusCode.Value,
                        WithBoundParameters(failed, rawForm, execution.Parameters),
                        requestBody, verbose, omitNulls, commandRequest);
                    return;
                }

                if (written.Length > 0)
                {
                    var returned = written == "null" ? null : new JRaw(written);
                    await SendExecResponse(response, 200,
                        WithBoundParameters(CommandExecutionResponse.CmdSuccess(cmd, returned), rawForm,
                            execution.Parameters),
                        requestBody, verbose, omitNulls, commandRequest);
                    return;
                }

                var details = ErrorText(error);
                switch (result)
                {
                    case ExitCode.TimedOut:
                        await SendExecResponse(response, 504,
                            EchoIfRaw(CommandExecutionResponse.CmdTimeout(cmd, details)),
                            requestBody, verbose, omitNulls, commandRequest);
                        break;
                    case ExitCode.InvalidUsage:
                        await SendExecResponse(response, 400,
                            EchoIfRaw(CommandExecutionResponse.CmdFailure(cmd, "Parameter Validation Failed", details)),
                            requestBody, verbose, omitNulls, commandRequest);
                        break;
                    case ExitCode.Cancelled:
                        await SendExecResponse(response, 400,
                            EchoIfRaw(CommandExecutionResponse.CmdFailure(cmd, "Command Cancelled",
                                details ?? "The command was cancelled before it ended.")),
                            requestBody, verbose, omitNulls, commandRequest);
                        break;
                    default:
                        await SendExecResponse(response, 400,
                            EchoIfRaw(CommandExecutionResponse.CmdFailure(cmd, "Command Execution Failed", details)),
                            requestBody, verbose, omitNulls, commandRequest);
                        break;
                }
            }
            catch (CommandArgumentsException ex)
            {
                await SendExecResponse(response, 400,
                    EchoIfRaw(CommandExecutionResponse.CmdInvalidArgs(ex.Command, ex.Message, ex.Problems.ToList(),
                        BuildCommandEntry(ex.CommandInfo))),
                    requestBody, verbose, omitNulls, commandRequest);
            }
            catch (CommandUnavailableException ex)
            {
                // A result the server gives in the command's place, for a job too: nothing ran,
                // so nothing was queued.
                if (TryAnswerRefusal(ex, out var answer))
                {
                    // Echoes no arguments: the only command answered this way, editor status,
                    // takes none, and a command line with any is refused before it gets here.
                    await SendExecResponse(response, 200,
                        WithBoundParameters(CommandExecutionResponse.CmdSuccess(ex.Command, answer), rawForm,
                            null),
                        requestBody, verbose, omitNulls, commandRequest);
                    return;
                }

                // A 503 with a retryable envelope, distinguishable from a genuine command failure
                // (AUTHAPI-35). The busy reply follows the request's reply-shape flags like every
                // other branch (AUTHAPI-21 x AUTHAPI-35).
                var busyResponse = EchoIfRaw(CommandExecutionResponse.CmdBusy(ex.Command, ex.Message,
                    ex.Reason == CommandUnavailableReason.BlockedByDialog ? "blocked_by_dialog" : "settling"));
                if (ex.Dialogs.Count > 0)
                    busyResponse.Dialogs = ex.Dialogs.Select(BuildDialogPayload).ToList();
                await SendExecResponse(response, 503, busyResponse, requestBody, verbose, omitNulls, commandRequest);
            }
            catch (TooManyCommandsException ex)
            {
                // Labelled as it always was for a job, which released clients branch on.
                var error = commandRequest?.Job == true ? "Too Many Queued Jobs" : "Too Many Queued Commands";
                await SendExecResponse(response, 429,
                    EchoIfRaw(CommandExecutionResponse.CmdFailure(ex.Command, error, ex.Message)),
                    requestBody, verbose, omitNulls, commandRequest);
            }
            catch (ArgumentException ex)
            {
                // Parameter validation errors
                await SendExecResponse(response, 400,
                    EchoIfRaw(CommandExecutionResponse.CmdFailure((ex as ParameterValidationException)?.Command ?? cmd,
                        "Parameter Validation Failed", ex.Message)), requestBody, verbose, omitNulls, commandRequest);
            }
            catch (CommandNotFoundException ex)
            {
                // Command not found errors. The exception names what was looked up, in the CLI
                // form the caller typed: for `<group> <name>` that is both tokens, not just the
                // group.
                await SendExecResponse(response, 400,
                    EchoIfRaw(CommandExecutionResponse.CmdFailure(ex.CommandName ?? cmd, "Command Not Found",
                        ObsoleteCommandNameMessage.ForMiss(ex.CommandName, ObsoleteCommandNames) ?? ex.Message)), requestBody, verbose, omitNulls, commandRequest);
            }
            catch (Exception ex)
            {
                Debug.LogError($"Failed to handle /api/exec request: {ex.Message}");
                await SendExecResponse(response, 400,
                    EchoIfRaw(CommandExecutionResponse.CmdFailure(cmd, "Internal Server Error", ex.Message)), requestBody, verbose, omitNulls, commandRequest);
            }
        }

        /// <summary>
        /// Read and discard the remaining request body, up to <paramref name="maxDrainBytes"/> bytes.
        /// When we reject a request early (e.g. 413) the client may still be uploading; if we close the
        /// response while unread data is in flight, HttpListener resets the TCP connection and the client
        /// sees a connection error instead of our HTTP response. Draining lets the upload complete so the
        /// response is delivered. Best-effort and bounded: a body larger than the budget stops draining
        /// (and may reset), which is acceptable for an abusive request.
        /// </summary>
        private static async Task DrainRequestBody(HttpListenerRequest request, long maxDrainBytes)
        {
            try
            {
                var input = request.InputStream;
                var buffer = new byte[16 * 1024];
                long drained = 0;
                while (drained < maxDrainBytes)
                {
                    var read = await input.ReadAsync(buffer, 0, buffer.Length);
                    if (read <= 0)
                        break;
                    drained += read;
                }
            }
            catch
            {
                // Best-effort: if the client already closed the connection or the stream is gone,
                // there is nothing left to drain and the response send below will handle the rest.
            }
        }

        /// <summary>
        /// Send error response with structured JSON format.
        /// </summary>
        private async Task SendErrorResponse(HttpListenerResponse response, string error, string details = null, int statusCode = 400)
        {
            try
            {
                var errorResponse = BaseResponse.Failure(error, details);
                await SendResponse(response, statusCode, errorResponse);
            }
            catch
            {
                // Ignore errors in error handling
            }
        }

        private async Task SendResponse(HttpListenerResponse response, int statusCode, BaseResponse pipelineResponse)
        {
            response.StatusCode = statusCode;
            response.ContentType = "application/json";
            var json = JsonConvert.SerializeObject(pipelineResponse, Formatting.Indented);
            var buffer = Encoding.UTF8.GetBytes(json);
            response.ContentLength64 = buffer.Length;

            await response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
            response.OutputStream.Close();
        }

        /// <summary>
        /// Send an /api/exec response and report the finished interaction to
        /// <see cref="OnCommandDone"/>. Single send point for the exec handler so every branch
        /// (success and error) is captured uniformly.
        ///
        /// The report carries the transaction only; the executor reports the command, if one ran.
        /// </summary>
        /// <param name="request">The request as it deserialized, for its caller context; null
        /// before it did.</param>
        private async Task SendExecResponse(HttpListenerResponse response, int statusCode,
                                            BaseResponse body, string requestJson, bool verbose = false, bool omitNulls = false,
                                            CommandExecutionRequest request = null)
        {
            response.StatusCode = statusCode;
            response.ContentType = "application/json";
            // Lean, compact serialization by default; the request's `verbose` flag opts into the
            // full envelope and `omitNulls` drops payload nulls. Single contract shared with the
            // byte-size regression test. (AUTHAPI-21)
            var json = ExecResponseSerializer.Serialize(body, verbose, omitNulls);
            var buffer = Encoding.UTF8.GetBytes(json);
            response.ContentLength64 = buffer.Length;

            await response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
            response.OutputStream.Close();

            OnCommandDone(CommandExecutionInfo.ForTransaction(request, requestJson, json));
        }

        /// <summary>
        /// Hook invoked once per finished /api/exec interaction: the Editor server writes the
        /// transaction log here. No-op by default. The executor reports each command that ran,
        /// a detached job's included, once it ends.
        ///
        /// Runs on the background HTTP thread, so an implementation must either stick to
        /// thread-safe work or marshal to the main thread itself. It must also never throw —
        /// an exception here would surface as a failed request for work the caller never asked for.
        /// </summary>
        /// <param name="info">The finished transaction, with the caller context its request sent.</param>
        internal virtual void OnCommandDone(in CommandExecutionInfo info) { }

        /// <summary>
        /// Convert a single JSON parameter token to the command's parameter type.
        ///
        /// Agents and the CLI frequently pass structured parameters (e.g. <c>float[]</c> position,
        /// <c>JObject</c> properties) as a JSON-ENCODED STRING — e.g. position <c>"[0,0,0]"</c> or
        /// properties <c>"{\"m_Mass\":0.17}"</c>. Newtonsoft's <c>JValue(string).ToObject(float[])</c>
        /// (and likewise for <c>JObject</c>) returns null, so the parameter silently drops out:
        /// set_transform applies nothing and set_component_properties fails Required-parameter
        /// validation (CLI-219 / CLI-220). To fix this generally — without special-casing any command —
        /// when the token is a string but the target type is NOT string AND the trimmed string starts
        /// with '{' or '[', we re-parse it as a JSON document before converting.
        ///
        /// The '{'/'[' guard is deliberate and narrow: ordinary string params (and ObjectRef string
        /// handles like "/Player", "Assets/Foo.prefab", "guid:..." — none of which start with '{'/'[')
        /// fall straight through to <c>token.ToObject</c>, so the class-level
        /// <see cref="Unity.Pipeline.Models.ObjectRefConverter"/> and plain-string parameters keep
        /// working unchanged. A re-parse that fails falls through to the normal conversion path (the
        /// caller's try/catch then handles any remaining failure).
        /// </summary>
        /// <remarks>
        /// Internal rather than private so <see cref="CommandLineBinder"/> can DRY-RUN the exact
        /// converter the executor will use. Binding against a different conversion would let a
        /// command line pass validation and then fail during execution.
        /// </remarks>
        internal static object ConvertParameterToken(Newtonsoft.Json.Linq.JToken token, System.Type targetType)
        {
            if (token == null)
                return null;

            if (token.Type == Newtonsoft.Json.Linq.JTokenType.String
                && targetType != typeof(string)
                && !targetType.IsPrimitive
                && !targetType.IsEnum)
            {
                var s = token.Value<string>();
                if (!string.IsNullOrWhiteSpace(s))
                {
                    var t = s.TrimStart();
                    if (t.Length > 0 && (t[0] == '{' || t[0] == '['))
                    {
                        try { return ToParameterObject(Newtonsoft.Json.Linq.JToken.Parse(s), targetType); }
                        catch (Newtonsoft.Json.JsonException) { /* fall through to the direct conversion */ }
                    }
                }
            }

            return ToParameterObject(token, targetType);
        }

        private static object ToParameterObject(Newtonsoft.Json.Linq.JToken token, System.Type targetType)
        {
            if (!StructuredInputSerializer.Applies(targetType))
                return token.ToObject(targetType);

            StructuredInputSerializer.RejectUnknownKeys(token, targetType);
            return token.ToObject(targetType, StructuredInputSerializer.Instance);
        }

        /// <summary>
        /// Extract command parameters from JSON request and convert to appropriate types.
        /// <para><paramref name="conversionError"/> reports arguments that could not be converted to
        /// the parameter's type. Substituting the default there gives the caller a different result
        /// than it asked for with no way to detect it — an ignored <c>limit</c> returns more than was
        /// asked, an ignored <c>timeout</c> waits longer than allowed. The slot still gets the default
        /// so the array stays well-formed, but the callers reject the request.</para>
        /// </summary>
        internal static object[] ExtractCommandParameters(CommandInfo command, Newtonsoft.Json.Linq.JObject parametersJson, out string conversionError)
        {
            StringBuilder conversionErrors = null;
            var parameterValues = new object[command.Parameters.Count];

            for (int i = 0; i < command.Parameters.Count; i++)
            {
                var paramInfo = command.Parameters[i];
                var paramName = paramInfo.Name;

                // Try to get value from JSON parameters
                object jsonValue = null;
                if (parametersJson != null && parametersJson.ContainsKey(paramName))
                {
                    var token = parametersJson[paramName];
                    try
                    {
                        jsonValue = ConvertParameterToken(token, paramInfo.ParameterType);

                        // A converter can DECLINE a token by returning null instead of throwing —
                        // ObjectRefConverter does exactly that for unsupported kinds, so
                        // {"parent": true} would otherwise read as an omitted argument and the
                        // command would run against the default. Anything that converts to null
                        // without being one of the intentional null forms is a conversion failure.
                        if (jsonValue == null && !IsIntentionalNullToken(token))
                        {
                            RecordConversionError(ref conversionErrors, paramName, paramInfo,
                                $"unsupported JSON value ({token.Type})");
                        }
                    }
                    catch (Exception ex)
                    {
                        // Record it rather than logging and moving on: a console warning is invisible
                        // to the HTTP caller, which is the only party that can act on it.
                        RecordConversionError(ref conversionErrors, paramName, paramInfo, ex.Message);
                    }
                }

                // Use provided value or default value
                if (jsonValue != null)
                {
                    parameterValues[i] = jsonValue;
                }
                else if (paramInfo.DefaultValue != null)
                {
                    parameterValues[i] = paramInfo.DefaultValue;
                }
                else
                {
                    // Use type's default value for value types, null for reference types
                    parameterValues[i] = paramInfo.ParameterType.IsValueType
                        ? Activator.CreateInstance(paramInfo.ParameterType)
                        : null;
                }
            }

            conversionError = conversionErrors?.ToString();
            return parameterValues;
        }

        /// <summary>
        /// The token forms a converter may legitimately turn into null, as opposed to declining a
        /// value it cannot represent: an explicit JSON null, and an empty or whitespace-only string.
        /// The latter is a documented contract, not a quirk — <c>ObjectRefConverter.FromString</c>
        /// returns null for it, and <c>set_parent</c> advertises "Omit (or empty) to move the object
        /// to the scene root", so <c>{"parent":""}</c> must keep detaching rather than 400.
        /// </summary>
        private static bool IsIntentionalNullToken(JToken token)
        {
            if (token == null || token.Type == JTokenType.Null)
                return true;

            return token.Type == JTokenType.String && string.IsNullOrWhiteSpace(token.Value<string>());
        }

        /// <summary>Appends one parameter-conversion failure to the accumulated report.</summary>
        private static void RecordConversionError(
            ref StringBuilder errors, string paramName, CommandParameterInfo paramInfo, string detail)
        {
            if (errors == null)
                errors = new StringBuilder();
            else
                errors.Append("; ");
            errors.Append(
                $"Parameter '{paramName}' could not be converted to {paramInfo.ParameterType.Name}: {detail}");
        }

        /// <summary>
        /// The one wording for a missing required parameter. Shared by the two validators below so
        /// the structured and raw request forms cannot diagnose the same mistake differently.
        /// </summary>
        private static string MissingRequiredParameterMessage(string parameterName)
        {
            return $"Required parameter '{parameterName}' is missing or empty";
        }

        /// <summary>
        /// Validates required parameters against a BOUND parameter object, before anything runs.
        ///
        /// <see cref="ValidateCommandParameters"/> applies the same rule to already-extracted CLR
        /// values, but it runs inside command execution — which for a detached job is after the job
        /// id has already been handed to the client. A raw submission missing a required parameter
        /// would be acknowledged as accepted and only fail later, in the background. Checking the
        /// bound object first lets that request be refused up front, with the same envelope the
        /// synchronous path produces.
        ///
        /// The rule itself is shared, not reimplemented: both this and the extracted-value check
        /// normalize to a CLR value and call <see cref="IsMissingRequiredValue"/>, so the raw and
        /// structured paths cannot drift on what counts as missing.
        /// </summary>
        internal static string ValidateRequiredParametersBound(CommandInfo command, JObject parameters)
        {
            for (var i = 0; i < command.Parameters.Count; i++)
            {
                var paramInfo = command.Parameters[i];
                if (!paramInfo.Required)
                    continue;

                if (IsMissingRequiredValue(AsRequiredValue(parameters?[paramInfo.Name])))
                {
                    return MissingRequiredParameterMessage(paramInfo.Name);
                }
            }

            return null;
        }

        /// <summary>
        /// The one rule for "this required parameter was not supplied": absent, null, or the empty
        /// string. Both the bound-JSON and extracted-CLR checks normalize to a CLR value and share
        /// this, so a future change to what counts as missing cannot be applied to one path and
        /// forgotten on the other.
        /// </summary>
        private static bool IsMissingRequiredValue(object value)
        {
            return value == null || (value is string text && string.IsNullOrEmpty(text));
        }

        /// <summary>
        /// A bound JSON value as the CLR value <see cref="IsMissingRequiredValue"/> expects. A JSON
        /// null is a CLR null; a JSON string is its text; anything else is present by definition
        /// and stands in for itself.
        /// </summary>
        private static object AsRequiredValue(JToken value)
        {
            if (value == null || value.Type == JTokenType.Null)
                return null;
            return value.Type == JTokenType.String ? value.Value<string>() : (object)value;
        }

        /// <summary>
        /// Validate that all required command parameters are provided.
        /// </summary>
        internal static string ValidateCommandParameters(CommandInfo command, object[] parameters)
        {
            for (int i = 0; i < command.Parameters.Count; i++)
            {
                var paramInfo = command.Parameters[i];

                if (paramInfo.Required && IsMissingRequiredValue(parameters[i]))
                {
                    return MissingRequiredParameterMessage(paramInfo.Name);
                }
            }

            return null; // No validation errors
        }

        /// <summary>
        /// What a command that matched nothing was looked up by: the name the caller typed (what an
        /// error shows) and the commands a suggestion may come from. Carries no logging, so a caller
        /// that must stay quiet (a batch dry run) can build the message itself.
        /// </summary>
        internal sealed class RawCommandMiss
        {
            /// <summary>What the caller typed, as <c>group name</c> (<c>editor nope</c>) or a single token, for messages and the reply's <c>command</c> echo.</summary>
            public string TypedName { get; }

            /// <summary>The commands a suggestion or prefix match is drawn from.</summary>
            public IReadOnlyList<CommandInfo> Candidates { get; }

            /// <summary>The commands that share <see cref="TypedName"/> as their name, in several groups; empty unless the name is ambiguous.</summary>
            public IReadOnlyList<CommandInfo> SharingTheName { get; }

            /// <param name="typedName">What the caller typed.</param>
            /// <param name="candidates">The commands a suggestion is drawn from.</param>
            /// <param name="named">The commands named <paramref name="typedName"/> (<see cref="CommandIndex.CommandsNamed"/>); two or more make the miss an ambiguous name.</param>
            public RawCommandMiss(string typedName, IEnumerable<CommandInfo> candidates, IReadOnlyList<CommandInfo> named = null)
            {
                TypedName = typedName;
                Candidates = candidates.ToList();
                SharingTheName = named != null && named.Count > 1 ? named : Array.Empty<CommandInfo>();
            }

            /// <summary>
            /// The Command Not Found message for this miss: the commands sharing the name when it is
            /// ambiguous, else <see cref="CommandNotFoundMessage(string, IEnumerable{string})"/>.
            /// </summary>
            public string Message => SharingTheName.Count > 0
                ? $"No command named '{TypedName}' is available. '{TypedName}' names several commands; run one of: " +
                  string.Join(", ", SharingTheName.Select(c => c.FullName).OrderBy(n => n, StringComparer.Ordinal)) + "."
                : CommandNotFoundMessage(TypedName, Candidates.Select(c => c.FullName));
        }

        /// <summary>
        /// What a raw request's leading tokens name: a command and the index of its first
        /// argument, a group to list, or nothing (<see cref="Miss"/>).
        /// </summary>
        internal readonly struct RawCommandResolution
        {
            public readonly CommandInfo Command;
            public readonly int ArgumentsStart;
            public readonly string ListedGroup;

            /// <summary>Set when the tokens name no command and no group; every other field is then empty.</summary>
            public readonly RawCommandMiss Miss;

            RawCommandResolution(CommandInfo command, int argumentsStart, string listedGroup, RawCommandMiss miss)
            {
                Command = command;
                ArgumentsStart = argumentsStart;
                ListedGroup = listedGroup;
                Miss = miss;
            }

            public static RawCommandResolution ForCommand(CommandInfo command, int argumentsStart) =>
                new RawCommandResolution(command, argumentsStart, null, null);

            public static RawCommandResolution ForGroup(string group) =>
                new RawCommandResolution(null, 0, group, null);

            public static RawCommandResolution ForMiss(RawCommandMiss miss) =>
                new RawCommandResolution(null, 0, null, miss);
        }

        /// <summary>
        /// Resolve the leading tokens of a raw request (<c>argv</c> or <c>commandLine</c>), logging
        /// and throwing the standard Command Not Found error when they name nothing. Groups are
        /// checked first: see <see cref="TryResolveRawCommand"/>.
        /// </summary>
        /// <exception cref="CommandNotFoundException">The tokens name no command and no group.</exception>
        internal static RawCommandResolution ResolveRawCommand(IReadOnlyList<string> tokens, CommandIndex index)
        {
            var resolution = TryResolveRawCommand(tokens, index);
            if (resolution.Miss != null)
                throw CommandNotFound(resolution.Miss);
            return resolution;
        }

        /// <summary>
        /// The resolution core shared by /api/exec (which logs a miss, see
        /// <see cref="ResolveRawCommand"/>) and the batch command (which must stay quiet): never
        /// logs and never throws. Groups are checked first: when token 0 is a group and token 1 a
        /// name in it, that command is named. Otherwise token 0 is the name of a command in
        /// <see cref="CommandInfo.GlobalGroup"/> or, when it is not a group, the one-word spelling
        /// of a full name (<see cref="CommandInfo.GetIdFromName"/>), accepted without being shown
        /// anywhere, or a command's name alone when no other command has it; a name several
        /// commands share is a miss that names them. Only then is a group with nothing after it listed, so a global command named
        /// like a group runs instead of answering with a listing. A group is one token: <c>animation animator add_state</c> is
        /// not <c>animation.animator add_state</c>. A miss carries what was looked up.
        ///
        /// <para>A shell-quoted name (<c>unity command "test run"</c>) arrives as one token holding
        /// two words. It names the command whose full name those words are, and its arguments
        /// start at the next token; any other quoted token keeps its old meaning, so its miss
        /// still names what was typed.</para>
        /// </summary>
        internal static RawCommandResolution TryResolveRawCommand(IReadOnlyList<string> tokens, CommandIndex index)
        {
            var first = tokens[0];
            var words = first?.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
            if (words != null && words.Length == 2
                && index.TryGetCommand(CommandInfo.ComposeFullName(words[0], words[1]), out var quoted))
                return RawCommandResolution.ForCommand(quoted, 1);

            var isGroup = index.IsGroup(first);
            string fullName = null;
            if (isGroup && tokens.Count > 1)
            {
                fullName = CommandInfo.ComposeFullName(first, tokens[1]);
                if (index.TryGetCommand(fullName, out var inGroup))
                    return RawCommandResolution.ForCommand(inGroup, 2);
            }

            if ((!isGroup && index.TryGetCommandByOneWord(first, out var command)) || index.TryGetGlobalCommand(first, out command)
                || (!isGroup && index.TryGetCommandByUniqueName(first, out command)))
                return RawCommandResolution.ForCommand(command, 1);

            if (!isGroup)
                return RawCommandResolution.ForMiss(new RawCommandMiss(first, index.Commands, index.CommandsNamed(first)));

            if (fullName == null)
                return RawCommandResolution.ForGroup(first);

            // Suggest from the group's own subtree, so `animation animator` points at the
            // animation.animator commands.
            return RawCommandResolution.ForMiss(new RawCommandMiss(fullName, index.CommandsUnder(first)));
        }

        /// <summary>The error code of a job submission that names a group instead of a command.</summary>
        internal const string GroupNotACommandErrorCode = "GROUP_NOT_A_COMMAND";

        /// <summary>The listing <c>/api/exec</c> returns for a group named with nothing after it.</summary>
        internal static GroupListing BuildGroupListing(string group, CommandIndex index)
        {
            return new GroupListing
            {
                Group = group,
                Subgroups = index.SubgroupsOf(group).OrderBy(g => g, StringComparer.Ordinal).ToList(),
                Commands = index.CommandsIn(group)
                    .OrderBy(c => c.FullName, StringComparer.Ordinal)
                    .Select(c => new GroupListingCommand { Command = c.FullName, Name = c.Name, Description = c.Description })
                    .ToList(),
            };
        }

        /// <summary>Log and build the standard Command Not Found error for a miss.</summary>
        internal static CommandNotFoundException CommandNotFound(RawCommandMiss miss)
        {
            var errorMessage = miss.Message;
            Debug.LogError($"ExecuteCommandByName: {errorMessage}");
            return new CommandNotFoundException(miss.TypedName, errorMessage);
        }

        /// <summary>Most commands a Command Not Found message names before it summarises the rest.</summary>
        internal const int MaxPrefixMatchesListed = 5;

        /// <summary>Where a Command Not Found message with no suggestion sends the caller.</summary>
        internal const string ListCommandsHint = "Run 'unity command' to list the commands this instance exposes.";

        /// <summary>
        /// The Command Not Found message: the name that missed plus either the command that was
        /// probably meant (a typo) or, failing that, the commands that start with what was typed
        /// (a partial name such as <c>editor</c> + <c>sta</c>), capped at <see cref="MaxPrefixMatchesListed"/>.
        /// When there is neither, it points at <c>unity command</c> (<see cref="ListCommandsHint"/>).
        /// It deliberately never lists the whole registry — that is hundreds of names, and it
        /// flooded both the CLI and the Editor console on a single typo (UUM-154017). The full
        /// catalog is served by /api/commands.
        ///
        /// <para>A typo is matched against full names (<c>group name</c>); a prefix is matched in the
        /// one-word spelling, so a partial name finds its commands whichever separator was typed.
        /// What the message shows is always the typed name and the full name of every suggested
        /// command.</para>
        /// </summary>
        /// <param name="typedName">What was typed, shown in the message.</param>
        /// <param name="available">The full name of every candidate.</param>
        internal static string CommandNotFoundMessage(string typedName, IEnumerable<string> available)
        {
            typedName = typedName ?? string.Empty;
            var candidates = available.ToList();
            var message = $"No command named '{typedName}' is available.";
            // With nothing to suggest, point at the listing instead (the hint #130 introduced).
            var withListingHint = message + " " + ListCommandsHint;

            var suggestion = CommandLineBinder.SuggestClosestName(typedName, candidates);
            if (suggestion != null)
                return $"{message} Did you mean '{suggestion}'?";

            // An empty name is a prefix of everything; listing for it would be the old flood.
            if (typedName.Length == 0)
                return withListingHint;

            // Compared in one-word spelling on both sides, so a space and a dot are the same
            // separator and `animation animator` finds the animation.animator commands.
            var typedPrefix = typedName.Replace(' ', '.');
            var prefixMatches = candidates
                .Where(c => CommandInfo.GetIdFromName(c).StartsWith(typedPrefix, StringComparison.OrdinalIgnoreCase))
                .OrderBy(c => c, StringComparer.Ordinal)
                .ToList();
            if (prefixMatches.Count == 0)
                return withListingHint;

            var listed = string.Join(", ", prefixMatches.Take(MaxPrefixMatchesListed));
            var remaining = prefixMatches.Count - MaxPrefixMatchesListed;
            var more = remaining > 0 ? $", … ({remaining} more)" : string.Empty;
            return $"{message} Commands starting with '{typedName}': {listed}{more}.";
        }


        /// <summary>
        /// Get the port range for this server type.
        /// Editor servers use 7800-7899, Runtime servers use 7900-7999.
        /// </summary>
        /// <returns>The inclusive port range to try when binding the listener.</returns>
        protected virtual (int basePort, int maxPort) GetPortRange()
        {
            return (7800, 7849); // Editor production (test editor servers use 7850-7899)
        }

        /// <summary>
        /// Find an available port in the pipeline server range.
        /// </summary>
        private int FindAvailablePort()
        {
            var (basePort, maxPort) = GetPortRange();

            if (PreferredPort >= basePort && PreferredPort <= maxPort && IsPortAvailable(PreferredPort))
                return PreferredPort;

            for (int port = basePort; port <= maxPort; port++)
            {
                if (IsPortAvailable(port))
                {
                    return port;
                }
            }

            throw new InvalidOperationException($"No available ports in range {basePort}-{maxPort}");
        }

        /// <summary>
        /// Check if a specific port is available for binding.
        /// </summary>
        /// <remarks>
        /// A plain socket bind is tried first. Mono shares one socket per port between every
        /// HttpListener in the process, and a failed HttpListener probe on a port another server
        /// in this process is listening on closes that server's socket.
        /// </remarks>
        private bool IsPortAvailable(int port)
        {
            try
            {
                var socket = new System.Net.Sockets.TcpListener(IPAddress.Parse(LoopbackHost), port);
                socket.Start();
                socket.Stop();

                using (var listener = new HttpListener())
                {
                    AddLoopbackPrefixes(listener, port);
                    listener.Start();
                    listener.Stop();
                    return true;
                }
            }
            catch
            {
                return false;
            }
        }
    }
}
