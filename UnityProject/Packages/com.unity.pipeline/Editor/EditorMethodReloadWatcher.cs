using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unity.Pipeline.Editor.Commands;
using Unity.Pipeline.MethodReload;
using UnityEditor;
using UnityEngine;
#if UNITY_6000_5_OR_NEWER
using Unity.Scripting.LifecycleManagement;
#endif

namespace Unity.Pipeline.Editor
{
    /// <summary>
    /// Watches a file or folder for <c>.cs</c> saves and re-applies the affected <c>[MethodReload]</c>
    /// methods. The target is resolved automatically at each save: any connected development player
    /// wins (broadcast push via <see cref="Commands.PushReloadCommand"/>),
    /// otherwise the reload applies in this editor process via <see cref="InPlaceReloadProcessor"/>.
    /// Both paths run on the interpreter backend. Folder mode watches recursively,
    /// only touching files that opt into method reload; the settings UI always watches the project's
    /// Assets folder (StartWatch itself accepts any file or folder).
    ///
    /// Change detection is event-driven (<see cref="FileSystemWatcher"/>) — polling a whole tree every
    /// editor tick would be prohibitive. The watcher can silently miss saves (macOS kqueue fd
    /// exhaustion, atomic rename-based saves), so a low-rate poll of the <c>.cs</c> files' last-write
    /// times backstops it; both paths feed the same debounce map, so a change seen by both applies once.
    ///
    /// Watches also re-push the watched state to any player that connects mid-watch:
    /// pushed overrides live only in the player's memory, so a restarted player boots the original AOT
    /// code. Standalone <see cref="InitializeOnLoadAttribute"/> owner so the watch survives domain
    /// reloads (state persisted in <see cref="EditorPrefs"/>); the Pipeline settings page only
    /// drives Start/Stop.
    /// </summary>
    [InitializeOnLoad]
#if UNITY_6000_5_OR_NEWER
    [NoAutoStaticsCleanup]
#endif
    static class EditorMethodReloadWatcher
    {
        private const double DebounceSeconds = 0.3;
        // Reconciliation poll period; each poll only stats the watched .cs files, so it stays cheap.
        private const double ReconcileIntervalSeconds = 2.0;
        // PlayerConnection attaches early in player boot but the reload receiver only registers in
        // RuntimePipelineDriver.Start(); an immediate push would go unheard.
        private const double ReconnectPushDelaySeconds = 2.0;

        // Persisted so an active watch survives editor domain reloads.
        private const string PkPath = "Pipeline.MethodReloadWatch.Path";
        private const string PkIsFolder = "Pipeline.MethodReloadWatch.IsFolder";
        // Prior values of the two auto-refresh prefs, persisted while a watch holds them at 0; -1 = we didn't change that key.
        private const string PkPrevAutoRefresh = "Pipeline.MethodReloadWatch.PrevAutoRefresh";
        private const string PkPrevAutoRefreshMode = "Pipeline.MethodReloadWatch.PrevAutoRefreshMode";
        // Unity reads kAutoRefreshMode (0=disabled, 1=enabled, 2=enabled outside play mode) since 2021.2;
        // kAutoRefresh is the pre-2021.2 bool. Set both — writing only the legacy key is a no-op on Unity 6.
        private const string AutoRefreshPref = "kAutoRefresh";
        internal const string AutoRefreshModePref = "kAutoRefreshMode";

        private static bool s_Watching;
        private static string s_WatchPath;
        private static bool s_IsFolder;
        private static int s_PrevAutoRefresh = -1;
        private static int s_PrevAutoRefreshMode = -1;

        private static FileSystemWatcher s_Fsw;
        // Set from the watcher's background thread when it faults (buffer overflow, backend failure);
        // OnEditorUpdate recreates the watcher on the main thread — otherwise it dies silently while
        // IsWatching keeps reporting true.
        private static volatile bool s_WatcherFaulted;
        private static string s_WatcherFaultMessage;
        // FileSystemWatcher fires on background threads → enqueue here, drain on the main thread.
        private static readonly ConcurrentQueue<string> s_Changed = new ConcurrentQueue<string>();
        // Main-thread-only: path → last-seen editor time, for per-file debounce.
        private static readonly Dictionary<string, double> s_Pending = new Dictionary<string, double>();
        // Main-thread-only: path → last-write ticks the watch has accounted for. The reconciliation
        // poll enqueues any .cs whose mtime differs (a save the FileSystemWatcher missed). Entries for
        // deleted files linger until the next Start; bounded by .cs count, harmless.
        private static readonly Dictionary<string, long> s_KnownWriteTimes = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        private static double s_NextReconcile;

        // Re-pushes due to players that connected mid-watch. Main-thread only; entries are
        // (due time, player id).
        private static readonly List<(double due, int playerId)> s_PendingReconnectPushes = new List<(double, int)>();
        private static bool s_ConnectHooked;
        private static bool s_SuppressConnectCallbacks;

        /// <summary>Whether an in-editor watch is currently active.</summary>
        public static bool IsWatching => s_Watching;
        /// <summary>The file or folder being watched, or null when not watching.</summary>
        public static string WatchPath => s_Watching ? s_WatchPath : null;
        /// <summary>Whether the active watch is a folder (recursive) rather than a single file.</summary>
        public static bool IsFolder => s_IsFolder;
        /// <summary>Development players currently connected via PlayerConnection. This decides where a
        /// save applies: any connected player wins (broadcast push), otherwise the reload runs in this
        /// editor process. Initializes the connection so the count is accurate even before a watch
        /// starts (Initialize is idempotent).</summary>
        public static int ConnectedPlayerCount
        {
            get
            {
                try
                {
                    var conn = UnityEditor.Networking.PlayerConnection.EditorConnection.instance;
                    conn.Initialize();
                    return conn.ConnectedPlayers.Count;
                }
                catch { return 0; }
            }
        }

        // Liveness telemetry for the settings inspector: saves applying while the event count stays
        // flat means the FileSystemWatcher is dead and the reconcile poll is carrying the watch.
        // Session-only (reset by domain reloads and Start).
        private static int s_FsEventCount;
        private static long s_LastFsEventTicks;   // DateTime.UtcNow ticks; written on the watcher thread
        private static string s_LastApplyFile;    // main thread only
        private static long s_LastApplyTicks;

        /// <summary>Raw FileSystemWatcher events seen since the watch (re)started this session.</summary>
        public static int FsEventCount => s_FsEventCount;
        /// <summary>UTC time of the last raw watcher event, or null if none yet.</summary>
        public static DateTime? LastFsEventUtc => TicksToUtc(System.Threading.Interlocked.Read(ref s_LastFsEventTicks));
        /// <summary>File name of the last reload attempt (either detection path), or null.</summary>
        public static string LastApplyFile => s_LastApplyFile;
        /// <summary>UTC time of the last reload attempt, or null if none yet.</summary>
        public static DateTime? LastApplyUtc => TicksToUtc(s_LastApplyTicks);

        private static DateTime? TicksToUtc(long ticks) =>
            ticks == 0 ? (DateTime?)null : new DateTime(ticks, DateTimeKind.Utc);

        static EditorMethodReloadWatcher()
        {
            // Editor may not be fully ready at [InitializeOnLoad] time; resume on the next tick.
            EditorApplication.delayCall += ResumeWatchIfPersisted;
            // The FileSystemWatcher is unmanaged-backed; drop it before a domain reload and recreate on resume.
            AssemblyReloadEvents.beforeAssemblyReload += DisposeWatcher;
        }

        /// <summary>Start watching <paramref name="path"/> (a file, or a folder when <paramref name="isFolder"/>).
        /// File mode applies once up front; folder mode only reacts to subsequent saves. No-op-with-error if the
        /// path doesn't exist.</summary>
        public static void StartWatch(string path, bool isFolder)
        {
            if (string.IsNullOrEmpty(path) || (isFolder ? !Directory.Exists(path) : !File.Exists(path)))
            {
                Debug.LogError($"[MethodReloadWatch] Cannot watch — {(isFolder ? "folder" : "file")} not found: {path}");
                return;
            }

            s_WatchPath = Path.GetFullPath(path);
            s_IsFolder = isFolder;
            s_Watching = true;
            s_Pending.Clear();
            while (s_Changed.TryDequeue(out _)) { }
            BaselineWriteTimes();
            var baselines = CaptureMethodBaselines();
            ClearAppliedFilesJournal();
            s_FsEventCount = 0;
            s_LastFsEventTicks = 0;
            s_LastApplyFile = null;
            s_LastApplyTicks = 0;

            // Turn auto-refresh off so saving a watched .cs doesn't domain-reload the editor (which would wipe
            // the registered overrides and interrupt the watch). Restore only what we changed.
            DisableAutoRefresh();

            CreateWatcher();
            EditorApplication.update -= OnEditorUpdate;
            EditorApplication.update += OnEditorUpdate;
            HookPlayerConnect();

            EditorPrefs.SetString(PkPath, s_WatchPath);
            EditorPrefs.SetBool(PkIsFolder, isFolder);
            EditorPrefs.SetInt(PkPrevAutoRefresh, s_PrevAutoRefresh);
            EditorPrefs.SetInt(PkPrevAutoRefreshMode, s_PrevAutoRefreshMode);

            // Single file: apply once up front so its current state is live. Folder: don't bulk-compile the
            // tree — only react to saves.
            if (!isFolder)
                Apply(s_WatchPath);

            Debug.Log($"[MethodReloadWatch] Watching {(isFolder ? "folder " : "")}{s_WatchPath} " +
                $"[{DescribeTarget()}]; auto-refresh off" + (baselines != null ? $"; {baselines}" : "") +
                ". Stop from Project Settings > Pipeline > Editor.");
        }

        /// <summary>Stop the active watch and restore auto-refresh.</summary>
        public static void StopWatch()
        {
            if (!s_Watching) return;
            s_Watching = false;
            DisposeWatcher();
            UnhookPlayerConnect();
            EditorApplication.update -= OnEditorUpdate;
            s_Pending.Clear();
            s_KnownWriteTimes.Clear();
            while (s_Changed.TryDequeue(out _)) { }
            ClearAppliedFilesJournal();
            MethodReloadBaseline.Clear();
            SessionState.EraseString(SkMethodBaselines);

            RestoreAutoRefresh();

            EditorPrefs.DeleteKey(PkPath);
            EditorPrefs.DeleteKey(PkIsFolder);
            EditorPrefs.DeleteKey(PkPrevAutoRefresh);
            EditorPrefs.DeleteKey(PkPrevAutoRefreshMode);
            Debug.Log("[MethodReloadWatch] Watch stopped; auto-refresh restored.");
        }

        private static void ResumeWatchIfPersisted()
        {
            if (s_Watching) return;
            var path = EditorPrefs.GetString(PkPath, null);
            if (string.IsNullOrEmpty(path)) return;

            bool isFolder = EditorPrefs.GetBool(PkIsFolder, false);
            bool exists = isFolder ? Directory.Exists(path) : File.Exists(path);
            if (!exists)
            {
                EditorPrefs.DeleteKey(PkPath);
                return;
            }

            s_WatchPath = path;
            s_IsFolder = isFolder;
            s_PrevAutoRefresh = EditorPrefs.GetInt(PkPrevAutoRefresh, -1);
            s_PrevAutoRefreshMode = EditorPrefs.GetInt(PkPrevAutoRefreshMode, -1);
            s_Watching = true;
            s_Pending.Clear();
            BaselineWriteTimes();

            CreateWatcher();
            EditorApplication.update -= OnEditorUpdate;
            EditorApplication.update += OnEditorUpdate;
            HookPlayerConnect();

            // Method baselines mirror the re-apply split below: an edit-mode reload means a real
            // recompile ran, so disk is the compiled state again — recapture. A play-mode reload
            // recompiled nothing (auto-refresh is off) and disk may already carry edits, so
            // recapturing would bless those edits as "compiled"; restore the watch-start snapshot.
            string baselines = null;
            if (EditorApplication.isPlaying)
                MethodReloadBaseline.Restore(SessionState.GetString(SkMethodBaselines, ""));
            else
                baselines = CaptureMethodBaselines();

            // Edit-mode resume: don't re-apply — the reload came from a real recompile, so the
            // compiled code already matches disk. Play-mode resume (entering play, or a mid-play
            // reload) is different: the reload wiped MethodReloadRegistry's overrides but recompiled
            // nothing (auto-refresh is off), so re-enqueue every file this watch had applied.
            // Routing through s_Pending reuses the debounce, by which time the
            // RuntimePipelineDriver has re-registered the reload targets (it runs in Awake).
            var reapply = ComputeResumeReapplies(EditorApplication.isPlaying, GetJournaledAppliedFiles());
            if (reapply.Count > 0)
            {
                double now = EditorApplication.timeSinceStartup;
                foreach (var f in reapply)
                    s_Pending[f] = now;
            }
            // One line per domain reload: what is watched, plus what this resume does about it.
            Debug.Log($"[MethodReloadWatch] Resumed watching {path} after domain reload" +
                (reapply.Count > 0 ? $"; re-applying {reapply.Count} method-reload override(s) the play-mode reload wiped" : "") +
                (baselines != null ? $"; {baselines}" : "") + ".");
        }

        /// <summary>Set both auto-refresh prefs to 0, remembering each prior value so Stop restores
        /// exactly what the user had (the mode key is an enum — it may have been 2, not 1).</summary>
        private static void DisableAutoRefresh()
        {
            int prev = EditorPrefs.GetInt(AutoRefreshPref, 1);
            s_PrevAutoRefresh = prev == 0 ? -1 : prev;
            if (s_PrevAutoRefresh >= 0)
                EditorPrefs.SetInt(AutoRefreshPref, 0);

            int prevMode = EditorPrefs.GetInt(AutoRefreshModePref, 1);
            s_PrevAutoRefreshMode = prevMode == 0 ? -1 : prevMode;
            if (s_PrevAutoRefreshMode >= 0)
                EditorPrefs.SetInt(AutoRefreshModePref, 0);
        }

        private static void RestoreAutoRefresh()
        {
            if (s_PrevAutoRefresh >= 0)
                EditorPrefs.SetInt(AutoRefreshPref, s_PrevAutoRefresh);
            if (s_PrevAutoRefreshMode >= 0)
                EditorPrefs.SetInt(AutoRefreshModePref, s_PrevAutoRefreshMode);
            s_PrevAutoRefresh = -1;
            s_PrevAutoRefreshMode = -1;
        }

        private static void CreateWatcher()
        {
            DisposeWatcher();
            s_WatcherFaulted = false; // before enabling: a fault firing mid-create must not be wiped
            try
            {
                var dir = s_IsFolder ? s_WatchPath : Path.GetDirectoryName(s_WatchPath);
                s_Fsw = new FileSystemWatcher(dir)
                {
                    Filter = s_IsFolder ? "*.cs" : Path.GetFileName(s_WatchPath),
                    IncludeSubdirectories = s_IsFolder,
                    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName,
                    InternalBufferSize = 64 * 1024,
                };
                s_Fsw.Changed += OnFsEvent;
                s_Fsw.Created += OnFsEvent;
                s_Fsw.Renamed += OnFsEvent;
                s_Fsw.Error += OnFsError;
                s_Fsw.EnableRaisingEvents = true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[MethodReloadWatch] Failed to start file watcher on {s_WatchPath}: {ex.Message}");
            }
        }

        private static void HookPlayerConnect()
        {
            if (s_ConnectHooked) return;
            try
            {
                // RegisterConnection replays the callback synchronously for players that are already
                // connected; suppress those — folder watches deliberately don't bulk-push on start,
                // and an already-running player still has whatever was pushed to it. Only players
                // that connect *after* this (i.e. fresh boots with no overrides) need catching up.
                s_SuppressConnectCallbacks = true;
                UnityEditor.Networking.PlayerConnection.EditorConnection.instance.Initialize();
                UnityEditor.Networking.PlayerConnection.EditorConnection.instance.RegisterConnection(OnPlayerConnected);
                s_ConnectHooked = true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[MethodReloadWatch] Could not hook player connections (reconnect re-push disabled): {ex.Message}");
            }
            finally
            {
                s_SuppressConnectCallbacks = false;
            }
        }

        private static void UnhookPlayerConnect()
        {
            s_PendingReconnectPushes.Clear();
            if (!s_ConnectHooked) return;
            try { UnityEditor.Networking.PlayerConnection.EditorConnection.instance.UnregisterConnection(OnPlayerConnected); }
            catch { /* connection may be gone during teardown */ }
            s_ConnectHooked = false;
        }

        // Main thread (EditorConnection dispatches connection events there).
        private static void OnPlayerConnected(int playerId)
        {
            if (s_SuppressConnectCallbacks || !s_Watching) return;

            // Read the setting at event time so a settings-page toggle applies to the next
            // connection without restarting the watch. No authored settings (CLI-driven watch) =
            // enabled. Load() hands back an instance we own, so destroy it rather than leak one per
            // player connect.
            var cfg = EditorPipelineConfig.Load();
            bool repushOnConnect = cfg?.MethodReloadRepushOnConnect ?? EditorPipelineConfig.DefaultMethodReloadRepushOnConnect;
            if (cfg != null)
                UnityEngine.Object.DestroyImmediate(cfg);

            if (!repushOnConnect)
            {
                // Nothing happens until the next save, so this is first-reload-summary material,
                // not a console entry of its own on every connect.
                MethodReloadRegistry.NoteForFirstReload($"player {playerId} connected, not re-pushed " +
                    "(Re-push On Connect is off in Project Settings > Pipeline > Editor; it catches up on the next save)");
                return;
            }

            s_PendingReconnectPushes.Add((EditorApplication.timeSinceStartup + ReconnectPushDelaySeconds, playerId));
            Debug.Log($"[MethodReloadWatch] Player {playerId} connected — re-pushing watched method-reload state " +
                $"in {ReconnectPushDelaySeconds:0}s (a restarted player has none of the previously pushed overrides).");
        }

        private static void OnFsEvent(object sender, FileSystemEventArgs e)
        {
            System.Threading.Interlocked.Increment(ref s_FsEventCount);
            System.Threading.Interlocked.Exchange(ref s_LastFsEventTicks, DateTime.UtcNow.Ticks);
            s_Changed.Enqueue(e.FullPath);
        }

        // Background thread: only record the fault; the recreate must happen on the main thread.
        private static void OnFsError(object sender, ErrorEventArgs e)
        {
            s_WatcherFaultMessage = e.GetException()?.Message ?? "unknown error";
            s_WatcherFaulted = true;
        }

        private static void DisposeWatcher()
        {
            if (s_Fsw == null) return;
            try { s_Fsw.EnableRaisingEvents = false; s_Fsw.Dispose(); } catch { /* best effort */ }
            s_Fsw = null;
        }

        private static void OnEditorUpdate()
        {
            if (!s_Watching) return;

            if (s_WatcherFaulted)
            {
                s_WatcherFaulted = false;
                Debug.LogWarning($"[MethodReloadWatch] File watcher faulted ({s_WatcherFaultMessage}); " +
                    "recreating it. Changes saved in the meantime may have been missed — save again to re-apply.");
                CreateWatcher();
            }

            double now = EditorApplication.timeSinceStartup;

            for (int i = s_PendingReconnectPushes.Count - 1; i >= 0; i--)
            {
                if (now < s_PendingReconnectPushes[i].due) continue;
                int playerId = s_PendingReconnectPushes[i].playerId;
                s_PendingReconnectPushes.RemoveAt(i);
                PushReloadCommand.RepushAll(s_WatchPath, s_IsFolder, playerId);
            }

            while (s_Changed.TryDequeue(out var p))
                if (!string.IsNullOrEmpty(p) && p.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
                {
                    s_Pending[p] = now; // (re)start this file's debounce window
                    // Stamp the accounted-for mtime so the reconcile poll doesn't re-enqueue a
                    // change the watcher already delivered.
                    try { s_KnownWriteTimes[p] = File.GetLastWriteTimeUtc(p).Ticks; } catch { /* transient */ }
                }

            ReconcilePoll(now);

            if (s_Pending.Count == 0) return;

            List<string> ready = null;
            foreach (var kv in s_Pending)
                if (now - kv.Value >= DebounceSeconds)
                    (ready ??= new List<string>()).Add(kv.Key);
            if (ready == null) return;

            foreach (var p in ready)
            {
                s_Pending.Remove(p);
                if (File.Exists(p))
                    Apply(p);
            }
        }

        /// <summary>Record the current last-write time of every watched .cs so the reconcile poll only
        /// reacts to saves made after the watch (re)started — mirrors "don't re-apply on resume".</summary>
        private static void BaselineWriteTimes()
        {
            s_KnownWriteTimes.Clear();
            try
            {
                if (s_IsFolder)
                    foreach (var f in Directory.EnumerateFiles(s_WatchPath, "*.cs", SearchOption.AllDirectories))
                        s_KnownWriteTimes[f] = File.GetLastWriteTimeUtc(f).Ticks;
                else
                    s_KnownWriteTimes[s_WatchPath] = File.GetLastWriteTimeUtc(s_WatchPath).Ticks;
            }
            catch { /* best effort — the poll fills in files it couldn't stat here */ }
            s_NextReconcile = EditorApplication.timeSinceStartup + ReconcileIntervalSeconds;
        }

        /// <summary>Low-rate safety net for saves the <see cref="FileSystemWatcher"/> missed: stat the
        /// watched .cs files and route any mtime change into the same debounce map the watcher feeds.</summary>
        private static void ReconcilePoll(double now)
        {
            if (now < s_NextReconcile) return;
            s_NextReconcile = now + ReconcileIntervalSeconds;
            try
            {
                if (s_IsFolder)
                {
                    foreach (var f in Directory.EnumerateFiles(s_WatchPath, "*.cs", SearchOption.AllDirectories))
                        CheckWriteTime(f, now);
                }
                else if (File.Exists(s_WatchPath))
                {
                    CheckWriteTime(s_WatchPath, now);
                }
            }
            catch { /* transient IO (file mid-write, folder shuffle) — next poll retries */ }
        }

        private static void CheckWriteTime(string path, double now)
        {
            long ticks = File.GetLastWriteTimeUtc(path).Ticks;
            if (s_KnownWriteTimes.TryGetValue(path, out var known) && known == ticks)
                return;
            s_KnownWriteTimes[path] = ticks;
            s_Pending[path] = now;
        }

        /// <summary>Apply one changed file to the active target. Never throws.</summary>
        private static void Apply(string path)
        {
            // Folder mode: only compile files that actually opt into method reload — an unrelated .cs save in the
            // tree shouldn't recompile or log. (Single-file mode was chosen explicitly, so always apply.)
            if (s_IsFolder && !PushReloadCommand.FileMentionsMethodReload(path))
                return;

            s_LastApplyFile = Path.GetFileName(path);
            s_LastApplyTicks = DateTime.UtcNow.Ticks;

            try
            {
                // Resolve the target per save, not per watch: a connected development player wins
                // (connecting one is a deliberate act, and the in-editor path is a no-op outside
                // play mode anyway), otherwise apply in this editor process. Plugging in or
                // disconnecting a device mid-watch reroutes the next save with no restart.
                if (ConnectedPlayerCount > 0)
                {
                    // PushChanged logs its own summary; -1 broadcasts to every connected player.
                    // Baseline-filtered: methods untouched since the watch started stay compiled
                    // on the device instead of being re-registered as interpreter overrides.
                    PushReloadCommand.PushChanged(path, player: -1);
                    return;
                }

                var result = InPlaceReloadProcessor.ProcessSourceFileOnMainThread(
                    path, assemblyDir: null, pdb: false, useInterpreter: true);
                if (result.Success)
                {
                    // ProcessSourceFileOnMainThread only binds the overrides; firing [OnMethodReloaded]
                    // callbacks on live instances is the caller's job (mirrors MethodReloadCommands.reload_file).
                    // Reverted methods changed behavior too (their override was removed), so they
                    // count as reloaded for the callbacks.
                    MethodReloadRegistry.InvokeReloadCallbacks(
                        result.RevertedMethods.Count == 0
                            ? result.RegisteredMethods
                            : result.RegisteredMethods.Concat(result.RevertedMethods));

                    if (result.AllUpToDate)
                    {
                        Debug.Log($"[MethodReloadWatch] {Path.GetFileName(path)} is up to date — " +
                            $"{result.UpToDateMethods.Count} method(s) already match compiled code" +
                            (result.RevertedMethods.Count > 0
                                ? $"; removed {result.RevertedMethods.Count} stale override(s)" : "") + ".");
                    }
                    // "Compiled but 0 methods bound" looks like success yet applies nothing — surface
                    // the per-method skip reasons (e.g. the runtime Pipeline isn't running, or the
                    // interpreter backend rejecting an unsupported construct) instead of a bare count.
                    else if (result.RegisteredMethods.Count == 0)
                    {
                        var reasons = result.CompilationDiagnostics != null && result.CompilationDiagnostics.Count > 0
                            ? ":\n- " + string.Join("\n- ", result.CompilationDiagnostics)
                            : ". Is the runtime Pipeline driver active (in play mode)?";
                        Debug.LogWarning($"[MethodReloadWatch] Compiled {Path.GetFileName(path)} " +
                            $"but bound 0 method(s){reasons}");
                    }
                    else
                    {
                        // Journal it so the next play-mode domain reload (which wipes the registry
                        // while auto-refresh keeps the compiled code stale) re-applies this file.
                        JournalAppliedFile(path);
                        Debug.Log($"[MethodReloadWatch] Applied {Path.GetFileName(path)}: " +
                            $"{result.RegisteredMethods.Count} method(s)" +
                            (result.UpToDateMethods.Count > 0
                                ? $" ({result.UpToDateMethods.Count} up to date, left compiled)" : "") + ".");
                    }
                }
                else
                {
                    // Surface the actual compiler diagnostics, not just the generic ErrorMessage —
                    // otherwise the watcher path swallows the reason the reload_file command already reports.
                    var details = result.CompilationDiagnostics != null && result.CompilationDiagnostics.Count > 0
                        ? ":\n- " + string.Join("\n- ", result.CompilationDiagnostics)
                        : "";
                    Debug.LogError($"[MethodReloadWatch] Reload failed for {Path.GetFileName(path)}: {result.ErrorMessage}{details}");
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[MethodReloadWatch] Reload error for {Path.GetFileName(path)}: {ex.Message}");
            }
        }

        // Per-method baselines (MethodReloadBaseline) captured when the watch starts, i.e. when disk
        // still matches the compiled code. Reloads diff each save against them and leave untouched
        // methods running compiled instead of re-registering an interpreter override for the whole
        // file. SessionState-backed for the same reason as the applied-files journal: the play-mode
        // domain reload wipes the store while disk keeps the user's edits.
        private const string SkMethodBaselines = "Pipeline.MethodReloadWatch.MethodBaselines";

        /// <summary>Snapshot the compiled method bodies of the watched files. Returns a short clause
        /// for the caller's log line (null when nothing was captured), so a watch start or resume
        /// stays a single console entry.</summary>
        private static string CaptureMethodBaselines()
        {
            MethodReloadBaseline.Clear();
            int captured = 0, stale = 0;
            try
            {
                var files = s_IsFolder
                    ? Directory.EnumerateFiles(s_WatchPath, "*.cs", SearchOption.AllDirectories)
                        .Where(PushReloadCommand.FileMentionsMethodReload)
                    : (IEnumerable<string>)new[] { s_WatchPath };

                foreach (var f in files)
                {
                    // A source newer than its compiled assembly was edited without a recompile —
                    // snapshotting it would bless those edits as "compiled" and silently skip them
                    // on the next save. Leave it baseline-less: every method reloads, like before.
                    if (!CompiledStateMatchesDisk(f))
                    {
                        stale++;
                        continue;
                    }

                    if (MethodReloadBaseline.Capture(f))
                        captured++;
                    // Pushes diff under the player's defines; capture that variant too so a device
                    // save doesn't fall back to reloading everything when #if regions differ.
                    var playerDefines = PushReloadCommand.PlayerDefinesFor(f);
                    if (playerDefines != null)
                        MethodReloadBaseline.Capture(f, playerDefines);
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[MethodReloadWatch] Baseline capture failed ({ex.Message}); " +
                    "saves will reload every [MethodReload] method in a file.");
            }

            SessionState.SetString(SkMethodBaselines, MethodReloadBaseline.Serialize());
            if (captured == 0 && stale == 0)
                return null;
            return $"method baselines captured for {captured} file(s)" +
                (stale > 0 ? $" ({stale} newer than their compiled assembly, unfiltered)" : "") +
                ", so saves only reload methods that differ";
        }

        /// <summary>Whether the compiled assembly owning <paramref name="path"/> is at least as new
        /// as the file — i.e. the source on disk is what the editor is running. Unknown = false.</summary>
        private static bool CompiledStateMatchesDisk(string path)
        {
            try
            {
                var projectRoot = Path.GetFullPath(Path.GetDirectoryName(Application.dataPath));
                var full = Path.GetFullPath(path);
                if (!full.StartsWith(projectRoot, StringComparison.OrdinalIgnoreCase))
                    return false;
                var relative = full.Substring(projectRoot.Length).TrimStart('/', '\\').Replace('\\', '/');
                var asmName = UnityEditor.Compilation.CompilationPipeline.GetAssemblyNameFromScriptPath(relative);
                if (string.IsNullOrEmpty(asmName))
                    return false;
                if (!asmName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                    asmName += ".dll";
                var dll = Path.Combine(projectRoot, "Library", "ScriptAssemblies", asmName);
                return File.Exists(dll) && File.GetLastWriteTimeUtc(dll) >= File.GetLastWriteTimeUtc(full);
            }
            catch
            {
                return false;
            }
        }

        // Applied-files journal: full paths of files whose overrides this watch applied in-editor.
        // SessionState-backed because the overrides it mirrors live only in MethodReloadRegistry's
        // statics — the domain reload triggered by entering play mode wipes them while the watched
        // sources stay un-recompiled (auto-refresh is off), so without this record the next play
        // session silently runs the stale compiled bodies. Session-scoped, like the overrides.
        private const string SkAppliedFiles = "Pipeline.MethodReloadWatch.AppliedFiles";

        /// <summary>Record a file whose overrides were successfully applied in this editor process.</summary>
        internal static void JournalAppliedFile(string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            var files = GetJournaledAppliedFiles();
            foreach (var f in files)
                if (string.Equals(f, path, StringComparison.OrdinalIgnoreCase))
                    return;
            var joined = files.Length == 0 ? path : string.Join("\n", files) + "\n" + path;
            SessionState.SetString(SkAppliedFiles, joined);
        }

        /// <summary>All journaled applied files (empty when none).</summary>
        internal static string[] GetJournaledAppliedFiles()
        {
            var raw = SessionState.GetString(SkAppliedFiles, "");
            return string.IsNullOrEmpty(raw) ? Array.Empty<string>() : raw.Split('\n');
        }

        /// <summary>Forget all journaled applies (watch started/stopped: overrides no longer ours to restore).</summary>
        internal static void ClearAppliedFilesJournal() => SessionState.EraseString(SkAppliedFiles);

        /// <summary>Files to re-apply when a watch resumes after a domain reload. Only in play mode:
        /// there the reload wiped the registry without recompiling the watched sources, so disk and
        /// compiled code have diverged. An edit-mode resume follows a real recompile — current
        /// compiled state already matches disk, so nothing needs re-applying.</summary>
        internal static List<string> ComputeResumeReapplies(bool isPlaying, string[] journaledFiles)
        {
            var result = new List<string>();
            if (!isPlaying || journaledFiles == null) return result;
            foreach (var f in journaledFiles)
                if (!string.IsNullOrEmpty(f) && File.Exists(f))
                    result.Add(f);
            return result;
        }

        private static string DescribeTarget() =>
            "target: connected players when present, else in-editor (always interpreter)";
    }
}
