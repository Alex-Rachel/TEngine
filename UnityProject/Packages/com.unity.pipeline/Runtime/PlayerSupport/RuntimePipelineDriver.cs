using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Unity.Pipeline.Config;
using Unity.Pipeline.Commands;
using Unity.Pipeline.MethodReload;
using Unity.Pipeline.Runtime.Telemetry;
using UnityEngine;
#if UNITY_6000_5_OR_NEWER
using Unity.Scripting.LifecycleManagement;
#endif
#if (UNITY_EDITOR || ENABLE_PROFILER || ENABLE_RUNTIME_PIPELINE)
using UnityEngine.Networking.PlayerConnection;
#endif

namespace Unity.Pipeline
{
    /// <summary>
    /// Drives the runtime Pipeline server. Created exactly once, in code, by
    /// <see cref="RuntimePipelineBootstrap"/> — never authored in a scene. Hidden from Add
    /// Component (nothing to configure via the Inspector; see <see cref="Initialize"/> instead)
    /// and guarded against stacking multiple instances on one GameObject, in case it's ever added
    /// manually or via a script despite that.
    /// </summary>
    [AddComponentMenu("")]
    [DisallowMultipleComponent]
#if UNITY_6000_5_OR_NEWER
    [NoAutoStaticsCleanup]
#endif
    public class RuntimePipelineDriver : MonoBehaviour
    {
        // Not yet exposed via RuntimePipelineConfig/the runtime settings page — always on. Wire up
        // through m_Config alongside the other Runtime Behavior settings if a project needs to
        // disable the overlay.
        private const bool ShowReloadOverlay = true;

        private RuntimePipelineServer m_Server;
        private RuntimePipelineConfig m_Config;
        private IReadOnlyList<string> m_AllowedReloadRoots = Array.Empty<string>();

        // Public half of the project's push-signing key. Baked at build time into the
        // RuntimePipelineBuildInfo asset (from Library/Pipeline/signing.key in the editor) and
        // supplied via Initialize — a player build rejects every method-reload push that is not signed
        // by the matching private key; empty means "reject all pushes" (strict policy, no fallback).
        private string m_PushPublicKey = "";
        private bool m_OwnsSampler;

        // Restored by StopServer so Play Mode exit doesn't leave the user's setting overridden.
        private bool m_PreviousRunInBackground;

        // True only between something overriding Application.runInBackground (StartServer, or
        // DrainPendingReloads reacting to a method-reload push that arrived with no HTTP server
        // running) and StopServer restoring it. Both writers check this before saving
        // m_PreviousRunInBackground, so whichever gets there first "owns" the saved value — the
        // other must not clobber it with the already-overridden "true". Gating the restore on this
        // (rather than m_Server.IsRunning) also means it still happens even if the listener already
        // died on its own before StopServer runs — otherwise runInBackground would stay stuck on,
        // reproducing a narrower version of the bug this ownership move fixed in the first place.
        private bool m_RunInBackgroundOverridden;

        /// <summary>Get the runtime server instance if it's running.</summary>
        public RuntimePipelineServer Server => m_Server;

        /// <summary>Whether the server is currently running.</summary>
        public bool IsServerRunning => m_Server != null && m_Server.IsRunning;

        /// <summary>Get the actual port the server is running on.</summary>
        public int ActualPort => m_Server?.Port ?? 0;

        /// <summary>The configuration this driver was initialized with.</summary>
        public RuntimePipelineConfig Config => m_Config;

        /// <summary>Absolute roots this build may method reload source files from. Baked at build time.</summary>
        public IReadOnlyList<string> AllowedReloadRoots => m_AllowedReloadRoots;

        /// <summary>
        /// Supply configuration and baked method-reload roots. Must be called once, immediately after
        /// AddComponent (before the end of the current frame, i.e. before Start runs).
        /// </summary>
        /// <param name="config">The configuration to run with.</param>
        /// <param name="allowedReloadRoots">Absolute roots this build may method reload source files from.</param>
        /// <param name="pushPublicKey">Public half of the push-signing key pushes must be signed with; empty rejects all pushes.</param>
        public void Initialize(RuntimePipelineConfig config, IReadOnlyList<string> allowedReloadRoots,
            string pushPublicKey = "")
        {
            m_Config = config;
            m_AllowedReloadRoots = allowedReloadRoots ?? Array.Empty<string>();
            m_PushPublicKey = pushPublicKey ?? "";
        }

        /// <summary>Public half of the project push-signing key. Baked at build time.</summary>
        public string PushPublicKey => m_PushPublicKey;

        void Awake()
        {
            // Persistent across scene loads; exactly one of these ever exists (created by
            // RuntimePipelineBootstrap), so there is no duplicate-instance case to defend against.
            // DontDestroyOnLoad only functions (and is only legal to call) in Play Mode; invoking it
            // from an Edit Mode context (e.g. an EditMode test, or this driver created via eval)
            // throws, which would otherwise abort the rest of Awake below.
            if (Application.isPlaying)
                DontDestroyOnLoad(gameObject);

            // Set up command discovery (the server owns its own dispatcher, initialized on Start).
            CommandRegistry.SetDiscovery(null); // Triggers reflection-based discovery

            // Auto-discover and register reloadable methods. Method reload requires this driver to
            // be present (it owns the communication workflow), so discovery lives here rather than in
            // each gameplay script's Awake.
            RegisterDiscoveredMethodReloadMethods();

            // Own the process-wide frame-stats sampler. A Player has no profiler window or
            // EditorApplication.update to drive sampling, so the driver feeds it from Update (below) and
            // the runtime_status telemetry command reads FrameStatsSampler.Shared. Created here (rather than
            // on server start) so fps history is already warm by the time an agent connects. Only claim
            // ownership if nothing already created one — a second, transient driver (e.g. a test creating
            // its own driver alongside one RuntimeInitializeOnLoadMethod already created) must not steal or
            // later tear down a sibling driver's sampler.
            if (FrameStatsSampler.Shared == null)
            {
                FrameStatsSampler.Shared = new FrameStatsSampler();
                m_OwnsSampler = true;
            }
        }

        void Start()
        {
            if (m_Config != null && m_Config.autoStart)
            {
                StartServer();
            }

#if (UNITY_EDITOR || ENABLE_PROFILER || ENABLE_RUNTIME_PIPELINE)
            // Independent of the HTTP server: method-reload IL pushed from the editor arrives over
            // PlayerConnection (the Profiler's channel — no open port needed, tunnels over USB).
            RegisterReloadReceiver();

            if (ShowReloadOverlay && GetComponent<MethodReloadStatusOverlay>() == null)
                gameObject.AddComponent<MethodReloadStatusOverlay>();
#endif
        }

        // Guards the scan and its "already ran" latch. Discovery can now be reached from two
        // directions at once — eagerly from Awake / the bootstrap on the main thread, and on demand
        // from the reload path, which compiles on a background thread — and the registry's
        // per-id method lists are not themselves thread-safe to append to.
        //
        // Like the registry it describes, the latch is cleared only by a domain reload, never on
        // Play Mode entry (see MethodReloadRegistry's remarks) — a latch that outlived the registry
        // would claim targets are registered after they had been wiped. The lock object only has
        // to exist; it carries no state across sessions.
        private static readonly object s_DiscoveryLock = new object();
        private static bool s_Discovered;

        /// <summary>
        /// Register the project's reload targets unless a scan already did it this session.
        ///
        /// Called from the reload path, so a reload finds its targets whether or not anything
        /// discovered them first. Eager discovery only happens on Play Mode entry (Awake, or
        /// RuntimePipelineBootstrap when it creates no driver), and an editor reload does not
        /// require Play Mode: without this, applying a reload from Edit Mode found an empty
        /// registry and reported every target as missing.
        ///
        /// Deliberately does NOT consult <c>Application.isPlaying</c> to skip Edit Mode — reloads
        /// compile on a background thread, where that property throws. Whether a reload makes sense
        /// in the current play state is the caller's question to ask, on the main thread.
        /// </summary>
        internal static void EnsureDiscoveredMethodReloadMethods()
        {
            lock (s_DiscoveryLock)
            {
                if (s_Discovered)
                    return;

                DiscoverAndRegisterMethodReloadMethods();
            }
        }

        /// <summary>
        /// Scan loaded user assemblies for [MethodReload] methods and register them as reload
        /// targets, whether or not a scan already ran. Runs from Awake, and from
        /// RuntimePipelineBootstrap in the Editor when the runtime server is disabled and no driver
        /// is created — both want the targets ready before the first pushed reload can land, rather
        /// than on first use. Re-running is harmless:
        /// <see cref="MethodReloadRegistry.RegisterReloadableMethod"/> ignores a method it already holds.
        /// </summary>
        internal static void RegisterDiscoveredMethodReloadMethods()
        {
            lock (s_DiscoveryLock)
                DiscoverAndRegisterMethodReloadMethods();
        }

        /// <summary>Forget that the scan ran, so the next <see cref="EnsureDiscoveredMethodReloadMethods"/>
        /// repeats it. Called when the registry is cleared, which would otherwise leave the latch
        /// claiming targets are registered when they are not.</summary>
        internal static void ResetMethodReloadDiscoveryLatch()
        {
            lock (s_DiscoveryLock)
                s_Discovered = false;
        }

        /// <summary>The scan itself. Callers hold <see cref="s_DiscoveryLock"/>.</summary>
        private static void DiscoverAndRegisterMethodReloadMethods()
        {
            int registered = 0;
            int unreadable = 0;

            foreach (var method in GetMethodReloadCandidateMethods())
            {
                // Reading the attribute resolves the method's signature, which throws when an
                // assembly it references cannot be loaded. Skip that one method rather than let it
                // abort the scan, and with it the bootstrap, on every Play Mode entry.
                MethodReloadAttribute reloadable;
                try
                {
                    reloadable = method.GetCustomAttribute<MethodReloadAttribute>();
                }
                catch (Exception ex) when (ex is TypeLoadException || ex is FileNotFoundException || ex is FileLoadException)
                {
                    unreadable++;
                    continue;
                }

                if (reloadable == null)
                    continue;

                // The weaver keys dispatch on TypeName.MethodName, so register with the
                // default id (ignore any custom Id) to match.
                MethodReloadRegistry.RegisterReloadableMethod(
                    method, new MethodReloadAttribute { RequireMainThread = reloadable.RequireMainThread });
                registered++;
            }

            s_Discovered = true;

            // The count only matters once someone tries to reload, so it joins the first-reload
            // summary instead of the startup noise (this runs on every Play Mode entry). That holds
            // for zero too: the summary prints on the first reload ATTEMPT, so a project that never
            // reloads stays quiet, and one that does sees the stripping/attribute hint right next to
            // the "target not registered" reasons.
            MethodReloadRegistry.NoteForFirstReload($"{registered} reloadable method(s) registered" +
                (registered == 0
                    ? " (if methods were expected: are the [MethodReload] attributes in this build, and did the owning assembly survive stripping?)"
                    : "") +
                (unreadable > 0
                    ? $"; {unreadable} method(s) skipped because a referenced assembly could not be loaded"
                    : ""));
        }

        private static IEnumerable<MethodInfo> GetMethodReloadCandidateMethods()
        {
#if UNITY_EDITOR
            return UnityEditor.TypeCache.GetMethodsWithAttribute<MethodReloadAttribute>();
#else
            return GetMethodReloadCandidateMethodsViaReflection();
#endif
        }

        private static IEnumerable<MethodInfo> GetMethodReloadCandidateMethodsViaReflection()
        {
            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic
                | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

            foreach (var assembly in PipelineUtils.GetLoadedAssemblies())
            {
                if (!ShouldScanForMethodReload(assembly))
                    continue;

                Type[] types;
                try
                {
                    types = assembly.GetTypes();
                }
                catch (ReflectionTypeLoadException ex)
                {
                    types = ex.Types; // best effort: keep the types that did load
                }

                foreach (var type in types)
                {
                    if (type == null)
                        continue;

                    MethodInfo[] methods;
                    try
                    {
                        methods = type.GetMethods(flags);
                    }
                    catch (Exception ex) when (ex is TypeLoadException || ex is FileNotFoundException || ex is FileLoadException)
                    {
                        continue; // same best effort as GetTypes above
                    }

                    foreach (var method in methods)
                        yield return method;
                }
            }
        }

        /// <summary>Skip engine/framework assemblies that cannot contain user method-reload methods.</summary>
        private static bool ShouldScanForMethodReload(Assembly assembly)
        {
            if (assembly.IsDynamic)
                return false;

            var name = assembly.GetName().Name;
            if (string.IsNullOrEmpty(name))
                return false;

            foreach (var prefix in s_MethodReloadSkipPrefixes)
            {
                if (name.StartsWith(prefix, StringComparison.Ordinal))
                    return false;
            }

            return true;
        }

        private static readonly string[] s_MethodReloadSkipPrefixes =
        {
            "System", "mscorlib", "netstandard", "Microsoft", "Mono.", "nunit",
            "UnityEngine", "UnityEditor", "Unity.Collections", "Unity.Burst",
            "Unity.Mathematics", "Newtonsoft", "log4net", "ICSharpCode",
        };

        /// <summary>
        /// The maxWorkItemsPerFrame value actually governing the dispatcher right now — live from
        /// the settings file in the Editor, frozen to the build-time value in a Player. Exposed
        /// (rather than reading m_Config.maxWorkItemsPerFrame directly) so status reporting and
        /// Update() can never disagree about which value is actually in effect.
        /// </summary>
        public int CurrentMaxWorkItemsPerFrame
        {
            get
            {
                var fallback = m_Config != null ? m_Config.maxWorkItemsPerFrame : 10;
#if UNITY_EDITOR
                return RuntimePipelineConfig.GetLiveMaxWorkItemsPerFrame(fallback);
#else
                return fallback;
#endif
            }
        }

        void Update()
        {
            // Feed the frame-stats sampler once per frame on the main thread. Uses unscaled delta so
            // reported fps reflects real frame pacing regardless of Time.timeScale.
            FrameStatsSampler.Shared?.Sample(Time.unscaledDeltaTime);

            // Pump this server's own dispatcher for main-thread operations (player builds have no
            // EditorApplication.update to auto-pump it).
            m_Server?.Dispatcher.ProcessWorkQueue(CurrentMaxWorkItemsPerFrame);

            // Drive the watchdog (player builds have no EditorApplication.update). No-op unless the
            // server's watchdog is enabled and armed.
            m_Server?.WatchdogTick();

#if (UNITY_EDITOR || ENABLE_PROFILER || ENABLE_RUNTIME_PIPELINE)
            DrainPendingReloads();
#endif
        }

        void OnApplicationQuit()
        {
            StopServer();
        }

        void OnDestroy()
        {
            StopServer();

            if (RuntimePipelineBootstrap.Instance == this)
                RuntimePipelineBootstrap.Instance = null;

            // Only the driver that actually created the shared sampler tears it down — see Awake().
            if (m_OwnsSampler)
            {
                FrameStatsSampler.Shared?.Dispose();
                FrameStatsSampler.Shared = null;
            }
#if (UNITY_EDITOR || ENABLE_PROFILER || ENABLE_RUNTIME_PIPELINE)
            UnregisterReloadReceiver();
#endif
        }

#if (UNITY_EDITOR || ENABLE_PROFILER || ENABLE_RUNTIME_PIPELINE)
        // ---- PlayerConnection method-reload receiver (editor pushes compiled override IL to this player) ----

        // Pushes arrive on the connection message pump (not guaranteed main thread); enqueue here and
        // apply in Update() where the interpreter + Unity APIs are safe to touch.
        private readonly List<byte[]> m_PendingReloads = new List<byte[]>();
        private bool m_ReloadReceiverRegistered;

        // Push-signing session state. The nonce is fresh per receiver lifetime and announced to the
        // editor via HandshakeMsg; every accepted push must be signed over it (see PushEnvelope).
        private byte[] m_PushSessionNonce;
        private PushVerifier m_PushVerifier;

        private void RegisterReloadReceiver()
        {
            try
            {
                PlayerConnection.instance.Register(PipelineMethodReloadConnect.ApplyReloadMsg, OnApplyReloadMessage);
                PlayerConnection.instance.Register(PipelineMethodReloadConnect.ReloadPendingMsg, OnReloadPendingMessage);
                PlayerConnection.instance.Register(PipelineMethodReloadConnect.ReloadFailedMsg, OnReloadFailedMessage);
                PlayerConnection.instance.Register(PipelineMethodReloadConnect.RequestHandshakeMsg, OnRequestHandshakeMessage);
                PlayerConnection.instance.RegisterConnection(OnPushPeerConnected);
                m_ReloadReceiverRegistered = true;

                m_PushSessionNonce = PushEnvelope.CreateNonce();
#if UNITY_EDITOR
                // Editor play mode is not a network surface for pushes (they originate in this same
                // process, and remote access goes through the token-authenticated HTTP server), so
                // signing is not enforced here — see DrainPendingReloads.
                MethodReloadRegistry.NoteForFirstReload("PlayerConnection receiver ready (editor play mode, push signing not enforced)");
#else
                if (!string.IsNullOrEmpty(m_PushPublicKey))
                {
                    m_PushVerifier = new PushVerifier(m_PushPublicKey, m_PushSessionNonce);
                    var fingerprint = PushEnvelope.Fingerprint(m_PushPublicKey);
                    if (PushEnvelope.VerifySelfTest(m_PushPublicKey, out var selfTestError))
                        Debug.Log($"Pipeline: PlayerConnection method-reload receiver ready — signed pushes only (key {fingerprint}, verify self-test OK). Push IL from the editor via '{CommandNames.ScriptMethodreloadReloadPlayer}'.");
                    else
                        Debug.LogError($"Pipeline: push-signature verify self-test FAILED on this platform ({selfTestError}) — every push will be rejected as CryptoUnavailable.");
                }
                else
                {
                    // Strict policy: a receiver without a baked key accepts nothing. The only way to
                    // get here is a build whose RuntimePipelineBuildInfo carries no key (e.g. an older
                    // build, or one produced without the build processor running).
                    Debug.LogWarning("Pipeline: method-reload push receiver has NO baked signing key — all pushes will be rejected. " +
                        "Rebuild with the Pipeline build processor enabled (it generates the key and bakes the public half into RuntimePipelineBuildInfo).");
                }
#endif
                SendPushHandshake(); // the editor may already be connected (e.g. autoconnect raced us)
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"Pipeline: PlayerConnection register failed: {ex.Message}");
            }
        }

        private void UnregisterReloadReceiver()
        {
            if (!m_ReloadReceiverRegistered) return;
            try
            {
                PlayerConnection.instance.Unregister(PipelineMethodReloadConnect.ApplyReloadMsg, OnApplyReloadMessage);
                PlayerConnection.instance.Unregister(PipelineMethodReloadConnect.ReloadPendingMsg, OnReloadPendingMessage);
                PlayerConnection.instance.Unregister(PipelineMethodReloadConnect.ReloadFailedMsg, OnReloadFailedMessage);
                PlayerConnection.instance.Unregister(PipelineMethodReloadConnect.RequestHandshakeMsg, OnRequestHandshakeMessage);
                PlayerConnection.instance.UnregisterConnection(OnPushPeerConnected);
            }
            catch { }
            m_ReloadReceiverRegistered = false;
        }

        /// <summary>Announce this session (protocol version, expected key fingerprint, session nonce)
        /// to the editor. Sent on receiver start, on every new connection, and on RequestHandshakeMsg.</summary>
        private void SendPushHandshake()
        {
            try
            {
                var fingerprint = string.IsNullOrEmpty(m_PushPublicKey) ? "" : PushEnvelope.Fingerprint(m_PushPublicKey);
                PlayerConnection.instance.Send(PipelineMethodReloadConnect.HandshakeMsg,
                    PipelineMethodReloadConnect.EncodeHandshake(PushEnvelope.ProtocolVersion, fingerprint, m_PushSessionNonce));
            }
            catch { /* no connection yet — the connect callback will re-announce */ }
        }

        // Both may fire on the connection thread; SendPushHandshake only touches immutable session state.
        private void OnRequestHandshakeMessage(MessageEventArgs args) => SendPushHandshake();
        private void OnPushPeerConnected(int playerId) => SendPushHandshake();

        private void OnApplyReloadMessage(MessageEventArgs args)
        {
            if (args?.data == null) return;
            lock (m_PendingReloads) m_PendingReloads.Add(args.data);
        }

        // Connection-thread handlers: MethodReloadActivity is thread-safe, no marshalling needed.
        private void OnReloadPendingMessage(MessageEventArgs args) =>
            MethodReloadActivity.ReportCompileStarted(PipelineMethodReloadConnect.DecodeText(args?.data));

        private void OnReloadFailedMessage(MessageEventArgs args) =>
            MethodReloadActivity.ReportFailed(PipelineMethodReloadConnect.DecodeText(args?.data));

        private void DrainPendingReloads()
        {
            byte[][] batch;
            lock (m_PendingReloads)
            {
                if (m_PendingReloads.Count == 0) return;
                batch = m_PendingReloads.ToArray();
                m_PendingReloads.Clear();
            }

            // A player that received a method-reload push must keep ticking while unfocused, otherwise
            // Update() (which drains + applies these pushes, and runs the reloaded code) pauses the
            // moment the window loses focus. Force it on once, on first receipt — reusing StartServer/
            // StopServer's own save/restore bookkeeping (rather than writing Application.runInBackground
            // directly) so OnDestroy's StopServer() still restores the user's original value even when
            // the HTTP server itself was never started (PlayerConnection needs no open port).
            if (!m_RunInBackgroundOverridden)
            {
                m_PreviousRunInBackground = Application.runInBackground;
                Application.runInBackground = true;
                m_RunInBackgroundOverridden = true;
                Debug.Log("Pipeline: forced Application.runInBackground = true (method-reload push received)");
            }

            foreach (var data in batch)
            {
                bool ok = false;
                string summary;
                try
                {
                    // ---- authentication gate (before anything touches the bytes) ----
                    byte[] payload = null;
                    string rejectAck = null;   // generic wire ack; the device log carries the detail
#if UNITY_EDITOR
                    // Editor play mode: strip a signed envelope without verification (no key is baked
                    // in play mode), accept legacy raw payloads as-is. Not a security gate — pushes to
                    // the editor originate from the editor itself.
                    if (PushEnvelope.LooksLikeEnvelope(data))
                    {
                        if (!PushEnvelope.TryParse(data, out _, out _, out payload))
                            rejectAck = "malformed method-reload push";
                    }
                    else
                    {
                        payload = data;
                    }
#else
                    if (m_PushVerifier == null)
                    {
                        LogPushReject("no signing key baked in this build — rebuild with the Pipeline build processor enabled");
                        rejectAck = "push rejected (authentication)";
                    }
                    else
                    {
                        var status = m_PushVerifier.Verify(data, out payload);
                        if (status != PushVerifyStatus.Ok)
                        {
                            // Q10 diagnostics: a legacy (pre-signing) editor sends a raw payload, which
                            // is the one failure the sender can actually fix without touching the build.
                            if (status == PushVerifyStatus.Malformed && !PushEnvelope.LooksLikeEnvelope(data))
                            {
                                LogPushReject("unsigned legacy push — the sending editor's com.unity.pipeline package predates push signing");
                                rejectAck = "push rejected: unsigned legacy push — update the com.unity.pipeline package in the editor";
                            }
                            else
                            {
                                LogPushReject($"{status} (session nonce/counter and signature details are in this device log only)");
                                rejectAck = "push rejected (authentication)";
                            }
                        }
                    }
#endif
                    if (rejectAck != null)
                    {
                        summary = rejectAck;
                    }
                    else if (!PipelineMethodReloadConnect.TryDecode(payload, out var typeName, out var methodNames, out var il))
                    {
                        summary = "malformed method-reload push";
                    }
                    else
                    {
                        var registered = Unity.Pipeline.Compilation.InterpreterMethodReloadExecutor.Register(
                            il, typeName, methodNames, out var skipped, out var warnings);
                        MethodReloadRegistry.InvokeReloadCallbacks(registered);
                        // Full warning text (unbound host members) goes to the device log; the ack
                        // summary the editor shows carries just the count so it stays one line.
                        foreach (var w in warnings)
                            Debug.LogWarning($"Pipeline: method-reload {typeName} — {w}");
                        // Same for per-method skip reasons: the summary carries only the count, but a
                        // method that didn't apply (e.g. SetupUI referencing a member the running
                        // build stripped) is only diagnosable if the reason reaches the device log —
                        // "skipped 1" alone gives the user nothing to act on.
                        foreach (var s in skipped)
                            Debug.LogWarning($"Pipeline: method-reload {typeName} — skipped {s}");
                        ok = registered.Count > 0;
                        summary = ok
                            ? $"applied {registered.Count} method(s) on {typeName}" +
                                (skipped.Count > 0 ? $"; skipped {skipped.Count}" : "") +
                                (warnings.Count > 0 ? $"; {warnings.Count} binding warning(s)" : "")
                            : $"no methods applied on {typeName}" +
                                (skipped.Count > 0 ? $": {string.Join("; ", skipped)}" : "");
                    }
                }
                catch (Exception ex)
                {
                    // The interpreter binds via reflection, so the real cause (e.g.
                    // ExecutionEngineException from a generic instantiation IL2CPP never
                    // AOT-compiled) arrives wrapped in TargetInvocationException, whose Message is
                    // just "Exception has been thrown by the target of an invocation." Unwrap for
                    // the ack the editor shows; keep the full chain + stack in the device log.
                    var root = ex;
                    while (root is System.Reflection.TargetInvocationException && root.InnerException != null)
                        root = root.InnerException;
                    Debug.LogException(ex);
                    summary = $"apply failed: {root.GetType().Name}: {root.Message}";
                }

                Debug.Log($"Pipeline: method-reload push — {summary}");
                if (ok) MethodReloadActivity.ReportApplied(summary);
                else MethodReloadActivity.ReportFailed(summary);
                try
                {
                    PlayerConnection.instance.Send(
                        PipelineMethodReloadConnect.ResultMsg, PipelineMethodReloadConnect.EncodeResult(ok, summary));
                }
                catch { }
            }
        }

        // Reject-log rate limiting: full detail for the first few failures, then one summary line per
        // minute — an attacker probing the port must not be able to spam the device log into
        // uselessness, but "signature failures are happening" stays visible (it IS the intrusion signal).
        private const int RejectLogBurst = 10;
        private const float RejectWindowSeconds = 60f;
        private int m_RejectsInWindow;
        private float m_RejectWindowStart = float.NegativeInfinity;
        private int m_SuppressedRejects;
        private float m_LastRejectSummaryTime = float.NegativeInfinity;

        private void LogPushReject(string detail)
        {
            float now = Time.realtimeSinceStartup;
            if (now - m_RejectWindowStart > RejectWindowSeconds)
            {
                m_RejectWindowStart = now;
                m_RejectsInWindow = 0;
            }

            m_RejectsInWindow++;
            if (m_RejectsInWindow <= RejectLogBurst)
            {
                Debug.LogWarning($"Pipeline: method-reload push rejected — {detail}");
                return;
            }

            m_SuppressedRejects++;
            if (now - m_LastRejectSummaryTime >= RejectWindowSeconds)
            {
                Debug.LogWarning($"Pipeline: {m_SuppressedRejects} further method-reload push(es) rejected in the last minute " +
                    $"(possible probing on this network); most recent: {detail}");
                m_LastRejectSummaryTime = now;
                m_SuppressedRejects = 0;
            }
        }
#endif

        /// <summary>Manually start the Pipeline server.</summary>
        public void StartServer()
        {
            if (m_Server != null && m_Server.IsRunning)
            {
                Debug.LogWarning("Pipeline: Server already started or initialization attempted.");
                return;
            }

            try
            {
                System.Console.WriteLine("Pipeline: Starting runtime server from RuntimePipelineDriver...");

                if (m_Config == null)
                {
                    Debug.LogWarning("Pipeline: Runtime server has no configuration to start from");
                    return;
                }

                var validation = m_Config.Validate();
                if (!validation.IsValid)
                {
                    Debug.LogError($"Pipeline: Invalid runtime configuration: {validation.Message}");
                    return;
                }

                if (validation.Level == "warning")
                {
                    Debug.LogWarning($"Pipeline: Runtime configuration warning: {validation.Message}");
                }

                m_Server = new RuntimePipelineServer(m_Config);
                m_Server.Start(m_Config.port);

                if (m_Server.IsRunning)
                {
                    // Keeps Update -> Dispatcher.ProcessWorkQueue/WatchdogTick running while unfocused.
                    // Guarded on m_RunInBackgroundOverridden (rather than unconditional) because
                    // DrainPendingReloads can already have overridden it — a method-reload push over
                    // PlayerConnection needs no HTTP server, so it can arrive and force this on before
                    // StartServer ever runs. Saving unconditionally here would clobber the real
                    // original value with the already-overridden "true", and StopServer would then
                    // restore to "true" instead of the user's setting.
                    if (!m_RunInBackgroundOverridden)
                    {
                        m_PreviousRunInBackground = Application.runInBackground;
                        Application.runInBackground = true;
                        m_RunInBackgroundOverridden = true;
                    }

                    // The method-reload registry marshals main-thread overrides through a dispatcher
                    // when reloaded code is invoked from a background thread at runtime. Inject
                    // this server's dispatcher here (a player has exactly one driver), so editor and
                    // per-test servers never clobber the global registry's dispatcher.
                    MethodReloadRegistry.Dispatcher = m_Server.Dispatcher;

                    // Publish the build-baked allowed roots so reload commands can validate that
                    // incoming files are inside the project before compiling and injecting them.
                    MethodReloadRegistry.AllowedReloadRoots = m_AllowedReloadRoots;

                    System.Console.WriteLine($"Pipeline: Runtime server started successfully on port {m_Server.Port}");
                }
                else
                {
                    Debug.LogError("Pipeline: Failed to start runtime server");
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"Pipeline: Runtime initialization failed: {ex.Message}");
                Debug.LogError($"Pipeline: Stack trace: {ex.StackTrace}");
            }
        }

        /// <summary>Manually stop the Pipeline server.</summary>
        public void StopServer()
        {
            try
            {
                if (m_Server != null && m_Server.IsRunning)
                {
                    System.Console.WriteLine("Pipeline: Stopping runtime server...");
                    // Drop the registry's reference to this server's (about to be shut down) dispatcher.
                    if (MethodReloadRegistry.Dispatcher == m_Server.Dispatcher)
                        MethodReloadRegistry.Dispatcher = null;
                    m_Server.Stop(); // Stop() shuts down the server's own dispatcher.
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"Pipeline: Runtime shutdown error: {ex.Message}");
            }

            // Independent of whether the listener was still alive above: if we overrode
            // runInBackground, restore it regardless, so a listener that already died on its own
            // (e.g. the watchdog never got to it) can't leave the setting stuck on.
            if (m_RunInBackgroundOverridden)
            {
                Application.runInBackground = m_PreviousRunInBackground;
                m_RunInBackgroundOverridden = false;
            }
        }

        /// <summary>Validate the current configuration.</summary>
        /// <returns>The validation result.</returns>
        public ValidationResult ValidateConfiguration()
        {
            return m_Config != null ? m_Config.Validate() : ValidationResult.Error("No RuntimePipelineConfig assigned");
        }
    }
}
