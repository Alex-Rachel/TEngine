using System;
#if UNITY_EDITOR
using System.IO;
#endif
using UnityEngine;
#if UNITY_6000_5_OR_NEWER
using Unity.Scripting.LifecycleManagement;
#endif

namespace Unity.Pipeline.Config
{
    /// <summary>
    /// Configuration for Pipeline server functionality in Unity Player builds. Authored settings
    /// live in a JSON file under ProjectSettings/ (see <see cref="Save"/>/<see cref="Load"/>) —
    /// never as an asset under Assets/, so nothing about this package appears in the user's
    /// Project window. At real Player build time, PipelineRuntimeBuildProcessor bakes the current
    /// settings into a transient Resources asset (deleted again after the build) so the Player
    /// can find them via Resources.Load.
    /// </summary>
#if UNITY_6000_5_OR_NEWER
    [NoAutoStaticsCleanup]
#endif
    public class RuntimePipelineConfig : ScriptableObject
    {
        /// <summary>Listen port. 0 auto-assigns from 7900-7999.</summary>
        [Header("Server")]
        [Tooltip("HTTP port for Pipeline server. Use 0 for auto-assignment from range 7900-7999.")]
        public int port = 0;

        /// <summary>Log remote requests for auditing.</summary>
        [Header("Runtime Behavior")]
        [Tooltip("Enable detailed logging of all remote requests for security auditing.")]
        public bool enableAuditLogging = true;

        /// <summary>Start the server automatically when the Player boots (or Play Mode is entered).</summary>
        [Tooltip("Start the server automatically when the Player boots (or Play Mode is entered).")]
        public bool autoStart = true;

        /// <summary>Dispatcher work items processed per frame.</summary>
        [Tooltip("Maximum work items the dispatcher processes per frame to maintain performance.")]
        [Range(1, 50)]
        public int maxWorkItemsPerFrame = 10;

        /// <summary>Format of the authored settings file; every Save writes the current one.</summary>
        internal const int CurrentSettingsVersion = 2;

        [SerializeField, HideInInspector]
        internal int settingsVersion = CurrentSettingsVersion;

        // Plain int field (not [Range]) so the Inspector shows a text field rather than an
        // unusable 0-65535 slider; the bound is still enforced here, same pattern as
        // EditorPipelineConfig.OnValidate's m_WatchdogIntervalSeconds clamp.
        private void OnValidate()
        {
            port = Mathf.Clamp(port, 0, 65535);
        }

        /// <summary>Resource name the build-time-generated transient asset is saved under for <see cref="Load"/> to find in a Player build.</summary>
        public const string ResourceName = "RuntimePipelineConfig";

#if UNITY_EDITOR
        /// <summary>Project-relative path of the authored settings file (never under Assets/).</summary>
        private const string SettingsFilePath = "ProjectSettings/Packages/com.unity.pipeline/RuntimePipelineConfig.json";
#endif

        /// <summary>
        /// Load the current settings, or null if none exist yet. In the Editor (including Play
        /// Mode) this reads the authored ProjectSettings/ JSON file directly; in a real Player
        /// build it reads the build-time-generated transient Resources asset.
        /// </summary>
        /// <returns>The current settings, or null if none exist yet.</returns>
        public static RuntimePipelineConfig Load()
        {
#if UNITY_EDITOR
            var path = GetAbsoluteSettingsPath();
            if (!File.Exists(path))
                return null;

            var config = CreateInstance<RuntimePipelineConfig>();
            try
            {
                JsonUtility.FromJsonOverwrite(File.ReadAllText(path), config);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"Pipeline: could not read runtime settings at '{path}' ({ex.Message}); using no config. Delete the file to reset.");
                DestroyImmediate(config);
                return null;
            }
            return config;
#else
            return Resources.Load<RuntimePipelineConfig>(ResourceName);
#endif
        }

#if UNITY_EDITOR
        private static int s_LiveMaxWorkItemsPerFrameCache = 10;
        private static DateTime s_LiveMaxWorkItemsPerFrameCacheFileTime;
        private static int s_LiveMaxWorkItemsPerFrameCacheSaveGeneration = -1;

        // Bumped by every in-process Save(). File timestamps can't be trusted to distinguish two
        // rapid saves: besides Windows' ~15ms clock-tick granularity, some filesystems record
        // mtime at whole-second (or coarser) resolution, so nudging the stamp forward by a
        // millisecond can round right back to the same value Save() started from. The counter is
        // exact regardless of the filesystem, and covers every in-process writer (the settings
        // page, the inspector, and remote set_runtime_pipeline_settings calls all run in this
        // process). The file-time check stays alongside it to still pick up a settings file
        // replaced by something outside this process.
        private static int s_SaveGeneration;

        // Whether the settings file was there at the last filesystem check. The idle fast path in
        // GetLiveMaxWorkItemsPerFrame answers without touching the disk, so "no file yet, use the
        // caller's fallback" has to be remembered rather than re-probed on every call.
        private static bool s_LiveSettingsFileExists;

        // Environment.TickCount at the last filesystem check, and whether one has happened at all.
        // Unsigned arithmetic on the difference keeps the interval comparison correct across the
        // ~49-day rollover of TickCount. Environment.TickCount rather than a Unity time API so the
        // poll carries no main-thread requirement of its own.
        private static int s_LastLiveFileCheckTick;
        private static bool s_HasCheckedLiveSettingsFile;

        // How stale an out-of-process settings-file replacement may be before the poll notices it.
        // In-process writers never wait for this: they bump s_SaveGeneration, which the fast path
        // tests on every single call.
        private const int LiveFileCheckIntervalMs = 500;

        /// <summary>
        /// Cheap, per-frame-callable read of just maxWorkItemsPerFrame from the authored settings
        /// file, for RuntimePipelineDriver.Update() to poll without paying Load()'s full
        /// CreateInstance+JSON-parse cost every frame. Re-parses only when Save() has run
        /// in-process since the last poll, so a Project Settings edit (including while in Play
        /// Mode) takes effect on the very next frame; a file whose last-write time moved for some
        /// other reason — replaced by a process other than this one — is picked up at the next
        /// filesystem check, within <see cref="LiveFileCheckIntervalMs"/>. An idle frame costs two
        /// integer compares and nothing else — no
        /// allocation and no filesystem call: this runs inside MonoBehaviour.Update, where it was
        /// the package's only source of per-frame managed garbage. Editor-only: a Player's config
        /// is frozen at build time, so there is nothing to poll for.
        /// </summary>
        public static int GetLiveMaxWorkItemsPerFrame(int fallback)
        {
            // Idle fast path. Every in-process writer (the settings page, the inspector, remote
            // set_runtime_pipeline_settings, and the test-only file helpers) bumps s_SaveGeneration,
            // so all of them are still picked up on the very next call. Only a settings file
            // replaced by something outside this process has to wait for the re-check interval.
            if (s_HasCheckedLiveSettingsFile &&
                s_SaveGeneration == s_LiveMaxWorkItemsPerFrameCacheSaveGeneration &&
                unchecked((uint)(Environment.TickCount - s_LastLiveFileCheckTick)) < LiveFileCheckIntervalMs)
            {
                return s_LiveSettingsFileExists ? s_LiveMaxWorkItemsPerFrameCache : fallback;
            }

            var path = GetAbsoluteSettingsPath();
            s_LastLiveFileCheckTick = Environment.TickCount;
            s_HasCheckedLiveSettingsFile = true;
            s_LiveSettingsFileExists = File.Exists(path);

            if (!s_LiveSettingsFileExists)
            {
                // Clear the timestamp key so a file that reappears is always re-read, even if it
                // carries the mtime of the one read last (an out-of-process restore of an identical
                // file). A real file's last-write time is never DateTime.MinValue, so the sentinel
                // cannot collide with a genuine stamp.
                s_LiveMaxWorkItemsPerFrameCacheFileTime = default;
                s_LiveMaxWorkItemsPerFrameCacheSaveGeneration = s_SaveGeneration;
                return fallback;
            }

            var writeTime = File.GetLastWriteTimeUtc(path);
            if (writeTime != s_LiveMaxWorkItemsPerFrameCacheFileTime ||
                s_SaveGeneration != s_LiveMaxWorkItemsPerFrameCacheSaveGeneration)
            {
                var config = Load();
                if (config != null)
                {
                    s_LiveMaxWorkItemsPerFrameCache = config.maxWorkItemsPerFrame;
                    DestroyImmediate(config);
                }
                s_LiveMaxWorkItemsPerFrameCacheFileTime = writeTime;
            }
            s_LiveMaxWorkItemsPerFrameCacheSaveGeneration = s_SaveGeneration;

            return s_LiveMaxWorkItemsPerFrameCache;
        }

        /// <summary>Persist this instance's current field values to the ProjectSettings/ JSON file.</summary>
        public void Save()
        {
            var path = GetAbsoluteSettingsPath();
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var previousWrite = File.Exists(path) ? File.GetLastWriteTimeUtc(path) : DateTime.MinValue;
            File.WriteAllText(path, JsonUtility.ToJson(this, true));
            s_SaveGeneration++;

            // The settings page (GetSettingsFileWriteTimeUtc) still detects changes by last-write
            // time, so keep nudging the stamp forward on a same-tick rewrite for its benefit.
            if (File.GetLastWriteTimeUtc(path) <= previousWrite)
                File.SetLastWriteTimeUtc(path, previousWrite.AddMilliseconds(1));
        }

        /// <summary>
        /// Last-write-time of the authored settings file (UTC), or DateTime.MinValue if it doesn't
        /// exist yet. Lets a long-lived caller (the Project Settings page) detect that the file
        /// changed underneath it — e.g. a build's "Disable Pipeline" security-dialog choice, or a
        /// set_runtime_pipeline_settings CLI call — without reloading from disk on every frame.
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

            InvalidateLiveCacheAfterTestFileSwap();
        }

        /// <summary>
        /// Move the authored settings file aside, if one exists, so a test run can start from a
        /// clean slate without destroying it. Returns the backup path to pass to
        /// <see cref="RestoreSettingsFileForTesting"/>, or null if there was no file to back up.
        /// For test fixture setup only.
        /// </summary>
        public static string BackUpSettingsFileForTesting()
        {
            // The real path, never a fixture's redirect: this guards the developer's own file, and
            // following a redirect left installed by a failed fixture would park that file under the
            // test name instead of restoring it.
            var path = GetRealSettingsPath();
            var backupPath = path + ".test-backup";

            // A run that dies before teardown leaves the developer's real file parked at the backup
            // path. Recover it instead of deleting it: with nothing at the normal path it is simply
            // moved back, so this run backs it up properly and teardown restores it. With a file
            // present we cannot tell a fresh edit from test residue, so the older one is preserved
            // under a unique name and the developer is told where it went. Either way, no authored
            // configuration is destroyed and none is left stranded at the backup path.
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
                        $"Runtime Pipeline: an interrupted test run had left settings at " +
                        $"'{backupPath}'. Preserved as '{rescued}' rather than overwritten.");
                }
            }

            InvalidateLiveCacheAfterTestFileSwap();

            if (!File.Exists(path))
                return null;

            File.Move(path, backupPath);
            return backupPath;
        }

        /// <summary>
        /// Restore a settings file backed up by <see cref="BackUpSettingsFileForTesting"/>,
        /// discarding whatever the test run left in its place. Pass null (no prior backup) to
        /// just delete a test-created file, leaving no settings file behind. For test fixture
        /// teardown only.
        /// </summary>
        public static void RestoreSettingsFileForTesting(string backupPath)
        {
            // The real path — see BackUpSettingsFileForTesting.
            var path = GetRealSettingsPath();
            if (File.Exists(path))
                File.Delete(path);

            if (backupPath != null)
                File.Move(backupPath, path);

            InvalidateLiveCacheAfterTestFileSwap();
        }

        /// <summary>
        /// Swapping the settings file underneath a test changes the effective configuration, but
        /// neither cache key in <see cref="GetLiveMaxWorkItemsPerFrame"/> necessarily moves:
        /// <see cref="File.Move(string,string)"/> carries the backup's original last-write time
        /// across, so if that matches the timestamp the test's own <see cref="Save"/> left behind —
        /// two writes inside one tick on a coarse-granularity filesystem — the poll would keep
        /// serving the test's value over the restored one. Bumping the generation is exact
        /// regardless of filesystem timestamp resolution.
        ///
        /// It is also what makes the poll's idle fast path safe for tests: that path answers from
        /// the cache without re-probing the filesystem for up to
        /// <see cref="LiveFileCheckIntervalMs"/>, so a file moved aside or deleted between two
        /// tests has to announce itself through the generation rather than be discovered by a
        /// stat. Every helper here calls this, including
        /// <see cref="BackUpSettingsFileForTesting"/>.
        /// </summary>
        private static void InvalidateLiveCacheAfterTestFileSwap() => s_SaveGeneration++;

        private static string s_TestSettingsPath;

        /// <summary>
        /// Point every read and write at a test-only file beside the real one, so a fixture never
        /// touches the developer's authored settings and cannot be tripped by one that already
        /// exists. Returns the redirected path. Pair with
        /// <see cref="EndSettingsFileRedirectForTesting"/> in teardown. For tests only.
        /// </summary>
        internal static string RedirectSettingsFileForTesting(string fileName)
        {
            s_TestSettingsPath = Path.Combine(Path.GetDirectoryName(GetRealSettingsPath()), fileName);
            InvalidateLiveCacheAfterTestFileSwap();
            return s_TestSettingsPath;
        }

        /// <summary>Undo <see cref="RedirectSettingsFileForTesting"/>. For tests only.</summary>
        internal static void EndSettingsFileRedirectForTesting()
        {
            s_TestSettingsPath = null;
            InvalidateLiveCacheAfterTestFileSwap();
        }

        private static string s_AbsoluteSettingsPath;

        internal static string GetAbsoluteSettingsPath()
        {
            // Application.dataPath is ".../Assets"; ProjectSettings is a sibling directory. Both are
            // fixed for the lifetime of the process, so the combined path is built once and reused.
            // It used to be rebuilt per call, which cost three string allocations —
            // Application.dataPath itself, GetDirectoryName, Combine — on a path that
            // GetLiveMaxWorkItemsPerFrame asks for on every frame.
            return s_TestSettingsPath ?? GetRealSettingsPath();
        }

        /// <summary>The project's own settings file, ignoring any test redirect.</summary>
        private static string GetRealSettingsPath() =>
            s_AbsoluteSettingsPath ??= Path.Combine(Path.GetDirectoryName(Application.dataPath), SettingsFilePath);
#endif

        /// <summary>
        /// Validate the configuration for correctness.
        /// Called by build processor to ensure safe deployment.
        /// </summary>
        /// <returns>The validation result.</returns>
        public ValidationResult Validate()
        {
            // Port validation
            if (port != 0 && (port < 7900 || port > 7999))
            {
                return ValidationResult.Warning(
                    $"Port {port} is outside recommended runtime range 7900-7999. May conflict with Editor instances.");
            }

            return ValidationResult.Success("Configuration is valid");
        }
    }

    /// <summary>
    /// Result from configuration validation.
    /// </summary>
    [Serializable]
    public class ValidationResult
    {
        /// <summary>False only for <see cref="Error"/> results.</summary>
        public bool IsValid { get; set; }
        /// <summary>"success", "warning", or "error".</summary>
        public string Level { get; set; } // "success", "warning", "error"
        /// <summary>Human-readable validation message.</summary>
        public string Message { get; set; }

        /// <summary>Create a successful validation result.</summary>
        /// <param name="message">Human-readable message.</param>
        /// <returns>A valid, "success"-level result.</returns>
        public static ValidationResult Success(string message = "Valid") =>
            new ValidationResult { IsValid = true, Level = "success", Message = message };

        /// <summary>Create a valid-but-noteworthy validation result.</summary>
        /// <param name="message">Human-readable message.</param>
        /// <returns>A valid, "warning"-level result.</returns>
        public static ValidationResult Warning(string message) =>
            new ValidationResult { IsValid = true, Level = "warning", Message = message };

        /// <summary>Create a failed validation result.</summary>
        /// <param name="message">Human-readable message.</param>
        /// <returns>An invalid, "error"-level result.</returns>
        public static ValidationResult Error(string message) =>
            new ValidationResult { IsValid = false, Level = "error", Message = message };
    }
}
