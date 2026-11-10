using System;
using System.IO;
using Unity.Pipeline.Commands;
using Unity.Pipeline.Models;
using UnityEngine;

namespace Unity.Pipeline.Editor
{
    /// <summary>
    /// Configuration and control surface for the live editor pipeline server. This is settings,
    /// NOT the server's owner — <see cref="PipelineServerStartup"/> owns the server instance (a
    /// static owner survives domain reloads cleanly, whereas a ScriptableObject's lifetime does not
    /// track the server across editor events). The owner reads this config when starting; the
    /// settings page drives Start/Stop through the owner and shows live status.
    ///
    /// Authored settings live in a JSON file under UserSettings/ (see <see cref="Save"/> /
    /// <see cref="Load"/>) — never as an asset under Assets/, which would ship the package's own
    /// configuration inside the user's build, and not under ProjectSettings/ either: they are
    /// per-user, and the Run In Background override record below changes with Play sessions and
    /// editor.playmode set_run_in_background, which kept the file in version control's way
    /// (AUTHAPI-119). Edited from Project Settings > Pipeline > Editor
    /// (<see cref="EditorPipelineSettingsProvider"/>).
    ///
    /// The file is optional: without it the owner uses the defaults below.
    /// </summary>
    class EditorPipelineConfig : ScriptableObject
    {
        // Each of these is used twice: as the field initializer below, and as the fallback wherever
        // a caller reads a setting with no settings file authored (Load returns null then). Those two
        // have to agree — otherwise merely authoring a file, without editing anything in it, would
        // change how the server starts. Naming the value once makes that true by construction
        // instead of leaving it to a test to notice the day they drift apart.
        internal const bool DefaultHttpEnabled = true;
        internal const ushort DefaultPort = 0;
        internal const bool DefaultAutoStart = true;
        internal const bool DefaultWatchdogEnabled = true;
        internal const int DefaultWatchdogIntervalSeconds = 5;
        internal const bool DefaultLogRequestsResponses = false;
        internal const bool DefaultEvalTelemetryEnabled = true;
        internal const bool DefaultStoreEvalSource = false;
        internal const bool DefaultAllowBrowserClients = false;
        internal const bool DefaultMethodReloadRepushOnConnect = true;
        internal const bool DefaultRunInBackgroundInPlayMode = false;

        // The HTTP-only settings (m_HttpEnabled, m_Port, m_AllowBrowserClients, the watchdog and
        // request logging) are drawn by EditorPipelineConfigEditor's "HTTP Service" foldout, not the
        // generic field loop.
        [Tooltip("Serve the HTTP API. When off, clients reach the editor through the Pipeline " +
                 "protocol transport only. Applies on next start.")]
        [SerializeField] private bool m_HttpEnabled = DefaultHttpEnabled;

        [Tooltip("HTTP port for the editor server. 0 = auto-assign from the 7800-7849 range. Applies on next start.")]
        [SerializeField] private ushort m_Port = DefaultPort;

        [Tooltip("Start the server automatically when the editor loads. Applies on next editor load.")]
        [SerializeField] private bool m_AutoStart = DefaultAutoStart;

        [Tooltip("Self-heal: if the HTTP listener dies without a Stop(), re-open it on a timer. " +
                 "Keeps auto-tick on so the editor keeps ticking while unfocused (required for the watchdog).")]
        [SerializeField] private bool m_WatchdogEnabled = DefaultWatchdogEnabled;

        [Tooltip("How often the watchdog checks the listener, between 1 and 60 seconds.")]
        [SerializeField] private int m_WatchdogIntervalSeconds = DefaultWatchdogIntervalSeconds;

        [Tooltip("Log every command request/response (raw JSON) handled by the editor server to " +
                 "<project>/Logs/pipeline.log. Editor only; applies live.")]
        [SerializeField] private bool m_LogRequestsResponses = DefaultLogRequestsResponses;

        [Tooltip("Record local eval-usage telemetry (fingerprints + shape, no raw source) to " +
                 "<project>/Library/Pipeline/eval-usage.jsonl. Read it back with the '" + CommandNames.DiagnosticsReportEvals + "' " +
                 "command. Local-only; no data leaves the machine. Applies live.")]
        [SerializeField] private bool m_EvalTelemetryEnabled = DefaultEvalTelemetryEnabled;

        [Tooltip("Also store the raw eval source in each eval-usage telemetry record. Off by default " +
                 "(privacy-first) — enable only for local debugging. Applies live.")]
        [SerializeField] private bool m_StoreEvalSource = DefaultStoreEvalSource;

        [Tooltip("Accept requests from a client running in a sandboxed browser frame (Origin: null), " +
                 "such as a plugin hosted inside a web application. An ordinary web page is refused " +
                 "either way, and a sandboxed one still needs the bearer token. Applies on next start.")]
        [SerializeField] private bool m_AllowBrowserClients = DefaultAllowBrowserClients;

        // Method Reload Watch config, drawn by a dedicated inspector section (not DrawDefaultInspector).
        [HideInInspector, SerializeField, UnityEngine.Serialization.FormerlySerializedAs("m_CodeReloadRepushOnConnect"),
         UnityEngine.Serialization.FormerlySerializedAs("m_HotReloadRepushOnConnect")]
        private bool m_MethodReloadRepushOnConnect = DefaultMethodReloadRepushOnConnect;

        // Drawn explicitly by EditorPipelineConfigEditor.DrawConfigFields with its own GUIContent
        // (not DrawDefaultInspector) so its Inspector label reads "Ensure editor runs in background
        // in PlayMode" — [InspectorName] only retitles enum values, not fields.
        [SerializeField] private bool m_RunInBackgroundInPlayMode = DefaultRunInBackgroundInPlayMode;

        // Run In Background override record (CLI-1432): hidden bookkeeping, never user-editable
        // directly. Toggling m_RunInBackgroundInPlayMode above, or calling
        // editor.playmode set_run_in_background, changes what these SHOULD be; only
        // PipelineServerStartup.ReconcileRunInBackground actually writes them, so the record and the
        // live Application.runInBackground value never drift apart.
        [HideInInspector, SerializeField] private bool m_RunInBackgroundOverridden;
        [HideInInspector, SerializeField] private bool m_RunInBackgroundOriginal;
        [HideInInspector, SerializeField] private bool m_RunInBackgroundCommandOverride;

        /// <summary>Serve the HTTP API. Applies on next start.</summary>
        public bool HttpEnabled => m_HttpEnabled;
        /// <summary>HTTP port for the editor server. 0 = auto-assign. Applies on next start.</summary>
        public int Port => m_Port;
        /// <summary>Start the server automatically when the editor loads.</summary>
        public bool AutoStart => m_AutoStart;
        /// <summary>Self-heal: re-open the listener on a timer if it dies without a Stop().</summary>
        public bool WatchdogEnabled => m_WatchdogEnabled;
        /// <summary>How often the watchdog checks the listener, in seconds.</summary>
        public int WatchdogIntervalSeconds => m_WatchdogIntervalSeconds;
        /// <summary>Log every command request/response to Logs/pipeline.log.</summary>
        public bool LogRequestsResponses => m_LogRequestsResponses;
        public bool EvalTelemetryEnabled => m_EvalTelemetryEnabled;
        public bool StoreEvalSource => m_StoreEvalSource;
        public bool AllowBrowserClients => m_AllowBrowserClients;

        /// <summary>Player-target watches: re-push the watched method-reload state to any player that
        /// connects mid-watch, so a restarted player catches up without waiting for the next save.</summary>
        internal bool MethodReloadRepushOnConnect => m_MethodReloadRepushOnConnect;

        /// <summary>
        /// Force the project's Run In Background Player Setting on while the Editor is in Play mode,
        /// restoring it in Edit mode (CLI-1432). Off by default. A build always uses the project's
        /// own value regardless of this setting or of any active <c>editor.playmode set_run_in_background</c>
        /// override — see <see cref="BuildProcessors.PipelineRuntimeBuildProcessor"/>.
        /// </summary>
        public bool RunInBackgroundInPlayMode => m_RunInBackgroundInPlayMode;

        /// <summary>
        /// Whether Application.runInBackground is currently overridden by this package (CLI-1432) and
        /// not yet restored — from a Play session driven by <see cref="RunInBackgroundInPlayMode"/>,
        /// from <see cref="RunInBackgroundCommandOverride"/>, or both.
        /// </summary>
        internal bool RunInBackgroundOverridden => m_RunInBackgroundOverridden;

        /// <summary>
        /// The project's own Run In Background value from just before the override began. Only
        /// meaningful while <see cref="RunInBackgroundOverridden"/> is true.
        /// </summary>
        internal bool RunInBackgroundOriginal => m_RunInBackgroundOriginal;

        /// <summary>
        /// Whether <c>editor.playmode set_run_in_background(enabled: true)</c> is in effect. Lasts until the
        /// command is called again with <c>enabled: false</c>, or until the Editor quits: quitting puts
        /// the project's own value back (AUTHAPI-119), and the next load reads that as the override ending.
        /// </summary>
        internal bool RunInBackgroundCommandOverride => m_RunInBackgroundCommandOverride;

        /// <summary>
        /// Record that the Run In Background override is now active or inactive, and what the
        /// project's own value was when it began. In-memory only — the caller owns persistence.
        /// Called only by <c>PipelineServerStartup.ReconcileRunInBackground</c>.
        /// </summary>
        internal void SetRunInBackgroundOverrideRecord(bool overridden, bool original)
        {
            m_RunInBackgroundOverridden = overridden;
            m_RunInBackgroundOriginal = original;
        }

        /// <summary>
        /// Record whether <c>editor.playmode set_run_in_background</c>'s standing override is in effect.
        /// In-memory only — the caller owns persistence.
        /// </summary>
        internal void SetRunInBackgroundCommandOverride(bool value) => m_RunInBackgroundCommandOverride = value;

        /// <summary>Whether the live server (owned by PipelineServerStartup) is actually running.</summary>
        public bool IsServerRunning => PipelineServerStartup.Server != null && PipelineServerStartup.Server.IsRunning;

        /// <summary>The port the live server is actually listening on, or 0 when stopped.</summary>
        public int ActualPort => PipelineServerStartup.Server?.Port ?? 0;

        /// <summary>Start the live server (delegates to the static owner, which reads this config).</summary>
        public void StartServer() => PipelineServerStartup.EnsureServerStarted();

        /// <summary>Stop the live server.</summary>
        public void StopServer() => PipelineServerStartup.StopServer();

        /// <summary>Restart the live server.</summary>
        public void RestartServer() => PipelineServerStartup.RestartServer();

        /// <summary>Project-relative path of the authored settings file (never under Assets/).</summary>
        private const string SettingsFilePath = "UserSettings/Packages/com.unity.pipeline/EditorPipelineConfig.json";

        /// <summary>Where the settings file lived before AUTHAPI-119. Moved to <see cref="SettingsFilePath"/> when found.</summary>
        private const string LegacySettingsFilePath = "ProjectSettings/Packages/com.unity.pipeline/EditorPipelineConfig.json";

        /// <summary>
        /// Load the authored settings, or null if none have been written yet (callers fall back to
        /// the defaults above). No caching — this is only read at start, at inspect, and on the
        /// settings page, never per frame.
        ///
        /// Returns a fresh instance each call, not a shared asset, so <b>the caller owns it and must
        /// DestroyImmediate it</b>. Same contract as RuntimePipelineConfig.Load.
        /// </summary>
        /// <returns>The authored settings, or null if none exist.</returns>
        public static EditorPipelineConfig Load()
        {
            MigrateLegacySettingsFile();
            var path = GetAbsoluteSettingsPath();
            if (!File.Exists(path))
            {
                // The legacy file could not be moved (locked, say): read it where it is.
                path = GetAbsolutePath(LegacySettingsFilePath);
                if (!File.Exists(path))
                    return null;
            }

            var config = CreateInstance<EditorPipelineConfig>();
            try
            {
                JsonUtility.FromJsonOverwrite(File.ReadAllText(path), config);
                config.Sanitize();
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"Pipeline: could not read editor settings at '{path}' ({ex.Message}); using defaults. Delete the file to reset.");
                DestroyImmediate(config);
                return null;
            }
            return config;
        }

        /// <summary>
        /// Persist this instance's current field values to the UserSettings/ JSON file, leaving a
        /// write time strictly later than the one already there.
        ///
        /// That guarantee is load-bearing: <see cref="EditorPipelineSettingsProvider"/> detects an
        /// out-of-band write purely by comparing <see cref="GetSettingsFileWriteTimeUtc"/>, and a
        /// rewrite within the same filesystem timestamp tick would otherwise leave the stamp
        /// unchanged — Windows' clock ticks about every 15ms, and some filesystems record mtime at
        /// whole-second resolution. An open settings page would then keep a stale instance whose
        /// next edit overwrites what was just written (reachable through the leftover-asset notice's
        /// copy button, or a second editor on the same project). Same nudge as
        /// RuntimePipelineConfig.Save.
        /// </summary>
        public void Save()
        {
            MigrateLegacySettingsFile();
            var path = GetAbsoluteSettingsPath();
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var previousWrite = File.Exists(path) ? File.GetLastWriteTimeUtc(path) : DateTime.MinValue;
            File.WriteAllText(path, JsonUtility.ToJson(this, true));

            if (File.GetLastWriteTimeUtc(path) <= previousWrite)
                File.SetLastWriteTimeUtc(path, previousWrite.AddMilliseconds(1));
        }

        /// <summary>
        /// Last-write-time of the authored settings file (UTC), or DateTime.MinValue if it doesn't
        /// exist yet. Lets the settings page detect that the file changed underneath it — someone
        /// editing it by hand, or a second editor instance sharing the project — without reloading
        /// from disk on every repaint.
        /// </summary>
        public static DateTime GetSettingsFileWriteTimeUtc()
        {
            var path = GetAbsoluteSettingsPath();
            return File.Exists(path) ? File.GetLastWriteTimeUtc(path) : DateTime.MinValue;
        }

        /// <summary>Delete the authored settings file, if any. For test cleanup only.</summary>
        public static void DeleteSettingsFileForTesting()
        {
            var path = GetAbsoluteSettingsPath();
            if (File.Exists(path))
                File.Delete(path);
        }

        /// <summary>
        /// Move the authored settings file aside, if one exists, so a test run can start from a
        /// clean slate without destroying it. Returns the backup path to pass to
        /// <see cref="RestoreSettingsFileForTesting"/>, or null if there was no file to back up.
        /// A legacy ProjectSettings/ file is parked as well, since the first Load() of the run would
        /// otherwise copy it back in under the tests' feet. For test fixture setup only.
        /// </summary>
        public static string BackUpSettingsFileForTesting()
        {
            ParkLegacySettingsFileForTesting();
            var path = GetAbsoluteSettingsPath();
            var backupPath = path + ".test-backup";

            // An interrupted run leaves the developer's real file parked at the backup path.
            // Recover it rather than delete it; see RuntimePipelineConfig for the full reasoning.
            if (File.Exists(backupPath))
            {
                if (!File.Exists(path))
                {
                    File.Move(backupPath, path);
                }
                else
                {
                    var rescued = $"{backupPath}-{DateTime.UtcNow:yyyyMMddHHmmssfff}";
                    File.Move(backupPath, rescued);
                    Debug.LogWarning(
                        $"Editor Pipeline: an interrupted test run had left settings at " +
                        $"'{backupPath}'. Preserved as '{rescued}' rather than overwritten.");
                }
            }

            if (!File.Exists(path))
                return null;

            File.Move(path, backupPath);
            return backupPath;
        }

        /// <summary>
        /// Restore a settings file backed up by <see cref="BackUpSettingsFileForTesting"/>,
        /// discarding whatever the test run left in its place. Pass null (no prior backup) to just
        /// delete a test-created file, leaving no settings file behind. For test fixture teardown only.
        /// </summary>
        public static void RestoreSettingsFileForTesting(string backupPath)
        {
            var path = GetAbsoluteSettingsPath();
            if (File.Exists(path))
                File.Delete(path);

            if (backupPath != null)
                File.Move(backupPath, path);

            UnparkLegacySettingsFileForTesting();
        }

        private static string LegacyTestBackupPath => GetAbsolutePath(LegacySettingsFilePath) + ".test-backup";

        private static void ParkLegacySettingsFileForTesting()
        {
            var legacyPath = GetAbsolutePath(LegacySettingsFilePath);

            // An interrupted run leaves the committed legacy file parked. Put it back, unless a
            // legacy file exists again, which the developer restored or edited since: keep both.
            if (File.Exists(LegacyTestBackupPath))
            {
                if (!File.Exists(legacyPath))
                {
                    File.Move(LegacyTestBackupPath, legacyPath);
                }
                else
                {
                    var rescued = $"{LegacyTestBackupPath}-{DateTime.UtcNow:yyyyMMddHHmmssfff}";
                    File.Move(LegacyTestBackupPath, rescued);
                    Debug.LogWarning(
                        $"Editor Pipeline: an interrupted test run had left legacy settings at " +
                        $"'{LegacyTestBackupPath}'. Preserved as '{rescued}' rather than overwritten.");
                }
            }

            if (File.Exists(legacyPath))
                File.Move(legacyPath, LegacyTestBackupPath);
        }

        /// <summary>
        /// Teardown: put back the legacy file <see cref="ParkLegacySettingsFileForTesting"/> moved
        /// aside, replacing any legacy file the test run itself left in its place.
        /// </summary>
        private static void UnparkLegacySettingsFileForTesting()
        {
            if (!File.Exists(LegacyTestBackupPath))
                return;

            var legacyPath = GetAbsolutePath(LegacySettingsFilePath);
            if (File.Exists(legacyPath))
                File.Delete(legacyPath);
            File.Move(LegacyTestBackupPath, legacyPath);
        }

        /// <summary>
        /// Move a settings file left under ProjectSettings/ by an earlier version to UserSettings/,
        /// while UserSettings/ has none yet. The package owns this file, not the user, so it moves
        /// silently. If the move fails, the file stays where it is and <see cref="Load"/> reads it
        /// from there, until a later call manages the move.
        /// </summary>
        private static void MigrateLegacySettingsFile()
        {
            var path = GetAbsoluteSettingsPath();
            var legacyPath = GetAbsolutePath(LegacySettingsFilePath);
            if (File.Exists(path) || !File.Exists(legacyPath))
                return;

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.Move(legacyPath, path);
                // A file in a Perforce workspace that is not checked out is read-only, and a move keeps
                // the attribute: the settings file has to stay writable for Save().
                File.SetAttributes(path, File.GetAttributes(path) & ~FileAttributes.ReadOnly);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        private static string GetAbsoluteSettingsPath() => GetAbsolutePath(SettingsFilePath);

        private static string GetAbsolutePath(string projectRelativePath)
        {
            // Application.dataPath is ".../Assets"; UserSettings and ProjectSettings are siblings.
            var projectRoot = Path.GetDirectoryName(Application.dataPath);
            var settingsRoot = GetSettingsProjectRoot(projectRoot, EditorProcessInfo.IsVirtualProjectClone());
            return Path.Combine(settingsRoot, projectRelativePath);
        }

        /// <summary>
        /// The project whose settings this Editor uses. A Multiplayer Play Mode virtual player is a
        /// project of its own under <c>&lt;mainProject&gt;/Library/VP/&lt;id&gt;</c>, and keeps using the
        /// main project's settings, as it did when they lived in the ProjectSettings/ it shares.
        /// </summary>
        internal static string GetSettingsProjectRoot(string projectRoot, bool isVirtualProjectClone)
        {
            if (!isVirtualProjectClone)
                return projectRoot;

            var virtualProjectsDir = Path.GetDirectoryName(projectRoot);
            var mainProjectRoot = Path.GetDirectoryName(Path.GetDirectoryName(virtualProjectsDir));
            if (mainProjectRoot == null)
                return projectRoot;

            var isUnderMainProject = string.Equals(
                Path.GetFullPath(EditorInstanceDescriptor.GetVirtualProjectsDir(mainProjectRoot)),
                Path.GetFullPath(virtualProjectsDir),
                StringComparison.OrdinalIgnoreCase);
            return isUnderMainProject ? mainProjectRoot : projectRoot;
        }

        private void OnValidate() => Sanitize();

        /// <summary>
        /// Clamp fields to their valid ranges. Needs calling from both <see cref="OnValidate"/>
        /// (which covers settings-page edits, raised by the inspector's
        /// ApplyModifiedPropertiesWithoutUndo) and <see cref="Load"/>, because
        /// JsonUtility.FromJsonOverwrite raises no Unity callbacks and the settings file is
        /// hand-editable text. A 0 watchdog interval is the one that bites: WatchdogTick throttles
        /// on `elapsed &lt; WatchdogIntervalSeconds`, so it would stop throttling and re-open a
        /// dead listener every editor update.
        /// </summary>
        private void Sanitize()
        {
            m_WatchdogIntervalSeconds = Mathf.Clamp(m_WatchdogIntervalSeconds, 1, 60);
        }
    }
}
