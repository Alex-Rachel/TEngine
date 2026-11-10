using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Newtonsoft.Json;
using UnityEngine;
#if UNITY_6000_5_OR_NEWER
using Unity.Scripting.LifecycleManagement;
#endif

#if UNITY_EDITOR
using System.Threading.Tasks;
#endif

namespace Unity.Pipeline.Telemetry
{
    /// <summary>
    /// One appended line of eval-usage telemetry. Serialized as a single compact JSON object per line
    /// (JSONL). The raw <see cref="Source"/> is omitted unless the local <see cref="EvalUsageTelemetry.StoreSource"/>
    /// opt-in is set, so by default only shape/fingerprint data is persisted (AUTHAPI-29).
    /// </summary>
    [Serializable]
    class EvalUsageRecord
    {
        [JsonProperty("time")]
        public string Time { get; set; }

        /// <summary>The command that ran the source, as a user types it: "script eval" or "script eval_file".</summary>
        [JsonProperty("command")]
        public string Command { get; set; }

        [JsonProperty("success")]
        public bool Success { get; set; }

        /// <summary>Short error category on failure (e.g. "Compilation Failed"); omitted on success.</summary>
        [JsonProperty("error", NullValueHandling = NullValueHandling.Ignore)]
        public string Error { get; set; }

        /// <summary>Character length of the eval body.</summary>
        [JsonProperty("payloadLength")]
        public int PayloadLength { get; set; }

        /// <summary>Newline-delimited line count of the eval body.</summary>
        [JsonProperty("lineCount")]
        public int LineCount { get; set; }

        [JsonProperty("executionTimeMs")]
        public long ExecutionTimeMs { get; set; }

        /// <summary>"single-expression" | "statements" — see <see cref="EvalClassification"/>.</summary>
        [JsonProperty("classification")]
        public string Classification { get; set; }

        /// <summary>Top-level API member-access paths extracted from the syntax tree.</summary>
        [JsonProperty("fingerprints")]
        public List<string> Fingerprints { get; set; } = new List<string>();

        /// <summary>Raw eval source. Present ONLY when the explicit opt-in is set; omitted otherwise.</summary>
        [JsonProperty("source", NullValueHandling = NullValueHandling.Ignore)]
        public string Source { get; set; }
    }

    /// <summary>
    /// Local-first, privacy-first instrumentation of <c>eval</c>/<c>eval_file</c> (AUTHAPI-29). Per
    /// invocation it appends one <see cref="EvalUsageRecord"/> to
    /// <c>&lt;project&gt;/Library/Pipeline/eval-usage.jsonl</c>. There is NO off-machine transmission:
    /// disabling <see cref="Enabled"/> stops even local recording, which satisfies "analytics opt-out
    /// fully disables any off-machine transmission" trivially — nothing ever leaves the machine.
    ///
    /// RECORDING IS EDITOR-ONLY. Eval telemetry exists to steer the editor command-surface migration,
    /// so <see cref="Record"/>/<see cref="RecordInBackground"/> compile to no-ops outside the editor
    /// (<c>#if UNITY_EDITOR</c>). This also keeps player builds from writing a "Library/Pipeline"
    /// folder next to <c>Application.dataPath</c> (the APK path on Android, inside the .app bundle on
    /// macOS) — a location players may not even be able to write. Release players cannot eval at all
    /// (the compiler stub blocks it); development players can, but are deliberately not instrumented.
    /// The class itself stays compiled in players so shared code links, and the read side returns
    /// empty results there.
    ///
    /// The production hook is fire-and-forget: <see cref="RecordInBackground"/> snapshots the eval
    /// outcome plus the current configuration on the calling (main) thread and runs the Roslyn parse,
    /// fingerprint extraction, and file append on a thread-pool task, so recording adds no latency to
    /// the eval response.
    ///
    /// Modelled on <c>PipelineTransactionLog</c>: a lock serialises the append (evals can overlap on
    /// the /api/progress path) and the reader takes the same lock, all I/O is best-effort and swallows
    /// errors so telemetry never breaks a command, and a directory-parameterised seam makes it
    /// hermetically testable. Unlike the debug transaction log, the JSONL is meant to ACCUMULATE
    /// across sessions so the ranked report has data, so it is bounded by a byte-size cap with
    /// rotation rather than reset per session; <see cref="ReadRecords"/> merges the rotated ".old"
    /// file back in, bounding readable history to roughly 2× <see cref="MaxFileBytes"/>.
    /// </summary>
#if UNITY_6000_5_OR_NEWER
    [NoAutoStaticsCleanup]
#endif
    static class EvalUsageTelemetry
    {
        /// <summary>
        /// Byte-size cap for the active JSONL before it rotates to the ".old" file (default 5 MB).
        /// A settable field (not a const) so tests can force the size-cap rotation path cheaply.
        /// </summary>
        public static long MaxFileBytes = 5L * 1024 * 1024;

        /// <summary>
        /// Upper bound, in characters, on the raw source persisted per record when the
        /// <see cref="StoreSource"/> opt-in is set (64 K). Longer bodies are cut at this bound and
        /// <see cref="SourceTruncationMarker"/> is appended so truncation is explicit;
        /// <see cref="EvalUsageRecord.PayloadLength"/> still reports the full original length.
        /// </summary>
        public const int MaxStoredSourceChars = 64 * 1024;

        /// <summary>Marker appended to a stored <see cref="EvalUsageRecord.Source"/> that was truncated.</summary>
        public const string SourceTruncationMarker = "…[truncated]";

        private const string FileName = "eval-usage.jsonl";
        private const string OldFileName = "eval-usage.old.jsonl";

        /// <summary>Master switch. When false, <see cref="Record"/> is a no-op (no local write, no transmission).</summary>
        public static bool Enabled = true;

        /// <summary>Opt-in: persist the raw eval source in each record. Off by default (privacy-first).</summary>
        public static bool StoreSource = false;

        /// <summary>
        /// Overrides the storage directory. Null uses <c>&lt;project&gt;/Library/Pipeline</c>. Tests set
        /// this to a temp directory so they never touch the real project telemetry file.
        /// </summary>
        public static string OverrideDirectory;

        private static readonly object s_AppendGate = new object();

        /// <summary>
        /// Number of background records queued by <see cref="RecordInBackground"/> and not yet
        /// flushed. Explicitly initialised: in player builds the recording path compiles away and the
        /// field is only ever read (by <see cref="WaitForPendingRecords"/>), which would otherwise
        /// raise CS0649.
        /// </summary>
        private static int s_PendingRecords = 0;

#if UNITY_EDITOR
        /// <summary>Serialises background writes in submission order (see <see cref="RecordInBackground"/>).
        /// Editor-only like the recording path itself: Task is not even imported in player builds.</summary>
        private static readonly object s_WriteChainGate = new object();
        private static Task s_WriteChain = Task.CompletedTask;
#endif

        /// <summary>Main-thread-resolved cache of the default (dataPath-derived) storage directory.</summary>
        private static string s_CachedDefaultDirectory;

        /// <summary>
        /// Record one eval invocation synchronously on the calling thread. Editor-only: compiles to a
        /// no-op in player builds (see class doc). Best-effort: any failure (parse, I/O) is swallowed
        /// with a warning so instrumentation can never break the eval it is measuring. The production
        /// eval hook uses <see cref="RecordInBackground"/> so the eval response is never delayed; this
        /// synchronous seam remains for tests and direct callers that need the write completed on
        /// return.
        /// </summary>
        public static void Record(string command, string code, bool success, string error, long executionTimeMs)
        {
#if UNITY_EDITOR
            if (!Enabled)
                return;

            TryWriteRecord(ResolveDirectory(), DateTime.UtcNow, command, code, success, error, executionTimeMs, StoreSource);
#endif
        }

        /// <summary>
        /// Record one eval invocation without blocking the calling thread. Editor-only: compiles to a
        /// no-op in player builds (see class doc). Everything the write needs — the eval primitives
        /// plus a snapshot of the current <see cref="Enabled"/>/<see cref="StoreSource"/> configuration
        /// and the resolved directory (which touches Unity API and must be read on the main thread) —
        /// is captured here on the calling thread; the Roslyn parse, fingerprint extraction, and
        /// lock-serialised append then run on a thread-pool task, so telemetry adds zero latency to
        /// the eval response. Failures on the background task are swallowed with a warning
        /// (<c>Debug.LogWarning</c> is thread-safe). A domain reload can at worst abandon one in-flight
        /// record; its partial trailing line is skipped by <see cref="ReadRecords"/>.
        /// </summary>
        public static void RecordInBackground(string command, string code, bool success, string error, long executionTimeMs)
        {
#if UNITY_EDITOR
            if (!Enabled)
                return;

            // Snapshot on the calling thread: config statics can be flipped live by the inspector,
            // and ResolveDirectory may touch Application.dataPath (main-thread-only Unity API).
            var directory = ResolveDirectory();
            var storeSource = StoreSource;
            var timeUtc = DateTime.UtcNow;

            Interlocked.Increment(ref s_PendingRecords);
            // Chain onto the previous write instead of racing independent Task.Runs: back-to-back
            // evals (batch, tight agent loops) must land in submission order — ReadRecords
            // documents the log as chronological — and the chain also means at most one telemetry
            // write competes with the pool at a time.
            lock (s_WriteChainGate)
            {
                s_WriteChain = s_WriteChain.ContinueWith(_ =>
                {
                    try
                    {
                        TryWriteRecord(directory, timeUtc, command, code, success, error, executionTimeMs, storeSource);
                    }
                    finally
                    {
                        Interlocked.Decrement(ref s_PendingRecords);
                    }
                }, System.Threading.CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
            }
#endif
        }

        /// <summary>
        /// Block until every background record queued by <see cref="RecordInBackground"/> has been
        /// flushed, or <paramref name="timeout"/> (default 5 s) elapses (returns false on timeout). Recording is
        /// fire-and-forget, so tests (and teardown paths that delete the storage directory) drain the
        /// queue with this before asserting on / removing the JSONL.
        /// </summary>
        public static bool WaitForPendingRecords(TimeSpan? timeout = null)
        {
            var limit = timeout ?? TimeSpan.FromSeconds(5);
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            while (Volatile.Read(ref s_PendingRecords) > 0)
            {
                if (stopwatch.Elapsed >= limit)
                    return false;
                Thread.Sleep(1);
            }
            return true;
        }

#if UNITY_EDITOR
        /// <summary>
        /// <see cref="WriteRecord"/>, with the shared best-effort guard: telemetry must never affect
        /// eval behavior (or crash a background task), so any failure (parse, I/O) is swallowed with
        /// a warning instead of propagating. Shared by <see cref="Record"/>'s synchronous call and
        /// <see cref="RecordInBackground"/>'s queued continuation.
        /// </summary>
        private static void TryWriteRecord(
            string directory,
            DateTime timeUtc,
            string command,
            string code,
            bool success,
            string error,
            long executionTimeMs,
            bool storeSource)
        {
            try
            {
                WriteRecord(directory, timeUtc, command, code, success, error, executionTimeMs, storeSource);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"Pipeline eval telemetry write failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Build and append one record from an already-captured snapshot. Runs on whichever thread the
        /// caller chose; deliberately touches no Unity API (the fingerprinter is pure Roslyn and the
        /// directory was resolved by the caller), so it is safe on a thread-pool task.
        /// </summary>
        private static void WriteRecord(
            string directory,
            DateTime timeUtc,
            string command,
            string code,
            bool success,
            string error,
            long executionTimeMs,
            bool storeSource)
        {
            var analysis = EvalUsageFingerprinter.Analyze(code);
            var record = new EvalUsageRecord
            {
                Time = timeUtc.ToString("o"),
                Command = command,
                Success = success,
                Error = success ? null : error,
                PayloadLength = code?.Length ?? 0,
                LineCount = CountLines(code),
                ExecutionTimeMs = executionTimeMs,
                Classification = analysis.Classification,
                Fingerprints = new List<string>(analysis.Fingerprints),
                Source = storeSource ? BoundStoredSource(code) : null
            };

            Append(directory, record);
        }

        /// <summary>
        /// Bound the opt-in raw source at <see cref="MaxStoredSourceChars"/>, appending
        /// <see cref="SourceTruncationMarker"/> when cut, so one giant eval_file body can't balloon
        /// a single record (the JSONL size cap only rotates between appends).
        /// </summary>
        private static string BoundStoredSource(string code)
        {
            if (string.IsNullOrEmpty(code) || code.Length <= MaxStoredSourceChars)
                return code;
            return code.Substring(0, MaxStoredSourceChars) + SourceTruncationMarker;
        }
#endif

        /// <summary>
        /// Append one record to the JSONL in an explicit directory. Testable seam behind
        /// <see cref="Record"/>. Rotates first if the active file has reached the size cap, so each
        /// file stays bounded to roughly <see cref="MaxFileBytes"/> plus one record.
        /// </summary>
        public static void Append(string directory, EvalUsageRecord record)
        {
            lock (s_AppendGate)
            {
                var root = Path.GetFullPath(directory);
                Directory.CreateDirectory(root);
                var path = DataFilePath(root, FileName);

                if (File.Exists(path) && new FileInfo(path).Length >= MaxFileBytes)
                    Rotate(root);

                File.AppendAllText(path, JsonConvert.SerializeObject(record, Formatting.None) + "\n");
            }
        }

        /// <summary>
        /// Canonical absolute path of one of this class's fixed data files inside the telemetry
        /// directory, with containment re-checked after normalization. The directory is the only
        /// dynamic path segment (a local editor setting or the test override — never protocol
        /// input) and the file names are compile-time constants, so this is an explicit invariant
        /// at the IO boundary: any input that would resolve outside its canonical directory is
        /// rejected instead of opened.
        /// </summary>
        private static string DataFilePath(string directory, string fileName)
        {
            if (!TryResolveDataFilePath(directory, fileName, out var full))
                throw new InvalidOperationException(
                    $"Telemetry data file path '{full}' escapes its directory '{directory}'.");
            return full;
        }

        /// <summary>
        /// The single canonicalize-and-contain implementation behind both the throwing
        /// <see cref="DataFilePath"/> (writer sites) and the silently-skipping reader — one place
        /// to fix normalization, per review.
        /// </summary>
        private static bool TryResolveDataFilePath(string directory, string fileName, out string fullPath)
        {
            var root = Path.GetFullPath(directory);
            fullPath = Path.GetFullPath(Path.Combine(root, fileName));
            var rootWithSeparator = root.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)
                ? root
                : root + Path.DirectorySeparatorChar;
            return fullPath.StartsWith(rootWithSeparator, StringComparison.Ordinal);
        }

        /// <summary>
        /// Move the active JSONL to the ".old" backup, replacing any prior backup, so the active file
        /// starts fresh. No-op when there is no active file. Exposed as the testable rotation seam.
        /// Takes the append gate so a rotation can never interleave with a concurrent append or read
        /// (the lock is re-entrant, so the rotate-inside-<see cref="Append"/> path is unaffected).
        /// </summary>
        public static void Rotate(string directory)
        {
            lock (s_AppendGate)
            {
                var root = Path.GetFullPath(directory);
                Directory.CreateDirectory(root);
                var path = DataFilePath(root, FileName);
                var oldPath = DataFilePath(root, OldFileName);
                RotatingFileBackup.RotateToBackup(path, oldPath);
            }
        }

        /// <summary>
        /// Read back every record from <paramref name="directory"/>: the rotated
        /// <c>eval-usage.old.jsonl</c> first (when present), then the active file, so the report sees
        /// the full retained history — bounded to roughly 2× <see cref="MaxFileBytes"/> — in
        /// chronological order. Malformed lines are skipped so a partial trailing write can never
        /// break the report.
        ///
        /// Concurrency: takes the same gate as <see cref="Append"/>/<see cref="Rotate"/> (this class
        /// is the only writer, and appends now happen on background tasks), and additionally opens
        /// each file tolerantly — <c>FileShare.ReadWrite</c>, with I/O errors caught — so a reader
        /// racing an out-of-process writer, or a file swapped away between the existence check and the
        /// open (the rotation TOCTOU), degrades to "return what parsed" instead of throwing.
        /// </summary>
        public static IReadOnlyList<EvalUsageRecord> ReadRecords(string directory)
        {
            var records = new List<EvalUsageRecord>();
            lock (s_AppendGate)
            {
                ReadFileInto(directory, OldFileName, records);
                ReadFileInto(directory, FileName, records);
            }
            return records;
        }

        /// <summary>
        /// Best-effort JSONL parse of one fixed-name data file into <paramref name="records"/>; see
        /// <see cref="ReadRecords"/>. The canonicalize-and-contain check is repeated inline here (not
        /// only in <see cref="DataFilePath"/>) so the validated path and the open are in the same
        /// method — the same invariant, kept visible at the IO site.
        /// </summary>
        private static void ReadFileInto(string directory, string fileName, List<EvalUsageRecord> records)
        {
            // Same canonicalize-and-contain invariant as the writer sites, via the one shared
            // implementation; a reader skips silently where a writer throws.
            if (!TryResolveDataFilePath(directory, fileName, out var path))
                return; // outside the telemetry directory — unreachable for the constant file names

            try
            {
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (var reader = new StreamReader(stream))
                {
                    string line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        if (string.IsNullOrWhiteSpace(line))
                            continue;
                        try
                        {
                            var record = JsonConvert.DeserializeObject<EvalUsageRecord>(line);
                            if (record != null)
                                records.Add(record);
                        }
                        catch (JsonException)
                        {
                            // Skip a malformed / partially written line; keep the rest of the log usable.
                        }
                    }
                }
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                // Covers FileNotFound/DirectoryNotFound (nothing recorded yet, or the file was rotated
                // away mid-read), sharing violations from external processes, and ACL/AV denials
                // (UnauthorizedAccessException does NOT derive from IOException): telemetry read-back
                // is best-effort, so return whatever parsed instead of throwing.
            }
        }

        /// <summary>
        /// Resolve the storage directory: the override if set, else &lt;project&gt;/Library/Pipeline.
        /// The default derives from <c>Application.dataPath</c> (a main-thread Unity API), so the
        /// first non-overridden call must happen on the main thread — the editor startup path primes
        /// it — after which the cached value is safe from any thread (e.g. the off-main-thread
        /// <c>report_evals</c> command).
        /// </summary>
        public static string ResolveDirectory()
        {
            if (!string.IsNullOrEmpty(OverrideDirectory))
                return OverrideDirectory;

            if (s_CachedDefaultDirectory == null)
            {
#if UNITY_EDITOR
                var projectRoot = Path.GetDirectoryName(Application.dataPath);
                s_CachedDefaultDirectory = Path.Combine(projectRoot, "Library", "Pipeline");
#else
                // Player builds: recording is compiled out and nothing is ever written, but
                // report_evals (MainThreadRequired=false) can reach this off the main thread
                // before any main-thread code primed the cache — and Application.dataPath is
                // main-thread-only. A stable process-temp location keeps the read path safe and
                // empty, so the command returns the documented zeroed report instead of throwing.
                s_CachedDefaultDirectory = Path.Combine(Path.GetTempPath(), "UnityPipelineEvalUsage");
#endif
            }
            return s_CachedDefaultDirectory;
        }

        private static int CountLines(string code)
        {
            if (string.IsNullOrEmpty(code))
                return 0;

            var lines = 1;
            for (var i = 0; i < code.Length; i++)
            {
                var c = code[i];
                if (c == '\n')
                    lines++;
                else if (c == '\r' && (i + 1 >= code.Length || code[i + 1] != '\n'))
                    lines++; // lone \r (classic Mac) is a line break; \r\n already counts via \n
            }
            return lines;
        }
    }
}
