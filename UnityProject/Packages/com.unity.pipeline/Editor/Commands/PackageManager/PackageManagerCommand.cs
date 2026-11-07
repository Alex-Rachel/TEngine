using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Unity.Pipeline.Commands;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEngine;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;
#if UNITY_6000_5_OR_NEWER
using Unity.Scripting.LifecycleManagement;
#endif

namespace Unity.Pipeline.Editor.Commands.PackageManager
{
    /// <summary>
    /// UPM package management over the Client API (CLI-203): list / search / add / remove / resolve.
    ///
    /// Threading: UPM <see cref="Client"/> operations are asynchronous (the <see cref="Request"/> only
    /// progresses while the editor ticks) and their members are main-thread only. Commands that wait on a
    /// request therefore run off the main thread (<c>MainThreadRequired = false</c>) and marshal every
    /// UPM touch back onto the main thread via the server dispatcher (<see cref="RunOnMain"/>), so they
    /// can block without freezing the editor.
    ///
    /// Command surface:
    ///  - <c>package_list</c> (installed scope) reads the resolved set inline; <c>available</c>/<c>all</c>
    ///    and <c>package_search</c> query the registry and wait for it — all return <b>synchronously</b>.
    ///  - <c>package_add</c>/<c>package_remove</c> mutate the manifest and trigger a <b>domain reload</b>
    ///    that tears down the in-flight request and the HTTP connection. They are <b>dual-mode</b>:
    ///    <list type="bullet">
    ///    <item><description><b>async (default)</b> — kick the op off, return <c>in_progress</c> immediately, and let
    ///    an <see cref="EditorApplication.update"/> poller finalize a Temp status file. Poll
    ///    <c>package_status</c> until <c>completed</c>/<c>failed</c>, then <c>recompile_status</c>. This
    ///    keeps the (single-request) server responsive and survives the reload.</description></item>
    ///    <item><description><b>synchronous</b> (<c>wait=true</c>) — block until the request completes and return the
    ///    full result. The result is captured in one main-thread hop the moment the request completes
    ///    (before the compile-triggered reload). Reliable for add; for remove the reload can fire fast
    ///    enough to drop the reply — the status file still settles, so <c>package_status</c> confirms it.</description></item>
    ///    </list>
    ///    Both write the status file, so a lost reply (or a reload mid-wait) is recoverable via
    ///    <c>package_status</c>; <see cref="RecoverInterruptedOperation"/> settles an <c>in_progress</c>
    ///    file on the next load.
    ///  - <c>package_resolve</c> records its status too, so <c>package_status</c> validates it.
    ///
    /// Mutating commands (add / remove) follow the shared <c>confirm</c>/<c>dry-run</c> convention.
    /// UPM operations are not part of Unity's Undo.
    /// </summary>
    [InitializeOnLoad]
#if UNITY_6000_5_OR_NEWER
    [NoAutoStaticsCleanup]
#endif
    static class PackageManagerCommand
    {
        const string Group = "package";
        const string StatusFile = "Temp/pipeline_package_status.json";

        const int PollIntervalMs = 50;

        static readonly object s_Lock = new object();

        // The in-flight mutating request (add/remove) and how to project its result. s_InProgress is a
        // plain flag (read off-thread by IsBusy) so the busy check never touches main-thread-only Request
        // members. Both are reset by a domain reload; RecoverInterruptedOperation settles the status file.
        static volatile bool s_InProgress;
        static Request s_Request;
        static string s_Operation;
        static string s_Argument;
        static Func<Request, PackageSummary> s_ReadPackage;

        static PackageManagerCommand()
        {
            RecoverInterruptedOperation();
        }

        // ---- List / search (synchronous) -------------------------------------------------------

        [CliGroupCommand(Group,
            "List packages by scope: installed (default) | available (registry) | all (both). Returns the full " +
            "result synchronously — available/all block until the registry query completes.",
            MainThreadRequired = false,
            Tags = new[] { "packages" })]
        public static object List(
            [CliArg("scope", "Which packages to list: installed (default) | available | all.")] string scope = "installed",
            [CliArg("include-indirect", "Include indirect (transitive) installed dependencies (applies to scope=installed/all).")]
            bool includeIndirect = true,
            [CliArg("offline", "For available/all: query the local cache instead of the registry.")] bool offline = false,
            [CliArg("timeout", "Seconds to wait for the registry (available/all).")] double timeoutInSeconds = 60)
        {
            var start = System.Diagnostics.Stopwatch.GetTimestamp();
            var timeout = PipelineTimeout.FromSeconds(timeoutInSeconds);
            switch ((scope ?? "installed").Trim().ToLowerInvariant())
            {
                case "installed":
                    return RunOnMain(() => ListInstalled(includeIndirect), timeout, start);
                case "available":
                    return ListFromRegistry("available", includeIndirect, offline, timeout, start);
                case "all":
                    return ListFromRegistry("all", includeIndirect, offline, timeout, start);
                default:
                    return new PackageListResponse
                    {
                        Success = false,
                        Scope = scope,
                        Message = $"Unknown scope '{scope}'. Use installed | available | all."
                    };
            }
        }

        /// <summary>
        /// Installed scope: <see cref="PackageInfo.GetAllRegisteredPackages"/> reflects the resolved
        /// set without a registry round-trip. Runs on the main thread (via <see cref="RunOnMain"/>).
        /// </summary>
        static PackageListResponse ListInstalled(bool includeIndirect)
        {
            var summaries = new List<PackageSummary>();
            foreach (var p in PackageInfo.GetAllRegisteredPackages())
            {
                if (includeIndirect || p.isDirectDependency)
                    summaries.Add(Map(p, isInstalled: true));
            }

            return new PackageListResponse
            {
                Success = true,
                Scope = "installed",
                Count = summaries.Count,
                Packages = summaries,
                Manifest = SafeReadManifest(),
                Message = $"{summaries.Count} installed package(s)."
            };
        }

        /// <summary>
        /// available / all scope: these need the registry, so a <c>Client.SearchAll</c> request is
        /// started on the main thread and awaited here (on the command's background thread). For
        /// <c>all</c>, the installed set is merged in (and marked) so callers see one unified list.
        /// </summary>
        static object ListFromRegistry(string scope, bool includeIndirect, bool offline, TimeSpan timeout, long start)
        {
            var request = RunOnMain(() => Client.SearchAll(offline), timeout, start);

            if (!WaitForCompletion(request, timeout, start, out var waitError))
                return new PackageListResponse { Success = false, Scope = scope, Message = waitError };

            return RunOnMain<object>(() =>
            {
                if (request.Status != StatusCode.Success)
                    return new PackageListResponse
                    {
                        Success = false,
                        Scope = scope,
                        Message = $"Registry query failed: {request.Error?.message ?? "unknown error"}"
                    };

                var packages = BuildScopedList(scope, request.Result, includeIndirect);
                return new PackageListResponse
                {
                    Success = true,
                    Scope = scope,
                    Count = packages.Count,
                    Packages = packages,
                    Manifest = SafeReadManifest(),
                    Message = $"{packages.Count} package(s)."
                };
            }, timeout, start);
        }

        /// <summary>
        /// Combine the registry search result with the installed set per <paramref name="scope"/>:
        /// <c>available</c> returns every registry package (flagged whether it is installed);
        /// <c>all</c> returns installed packages (resolved info) plus the registry packages that are
        /// not installed.
        /// </summary>
        static List<PackageSummary> BuildScopedList(string scope, PackageInfo[] available, bool includeIndirect)
        {
            var installed = new Dictionary<string, PackageInfo>();
            foreach (var p in PackageInfo.GetAllRegisteredPackages())
                installed[p.name] = p;

            var result = new List<PackageSummary>();

            if (scope == "available")
            {
                foreach (var p in available ?? Array.Empty<PackageInfo>())
                    result.Add(Map(p, isInstalled: installed.ContainsKey(p.name)));
                return result;
            }

            // scope == "all": installed entries first (with their resolved info), then registry-only ones.
            var seen = new HashSet<string>();
            foreach (var entry in installed.Values)
            {
                if (!includeIndirect && !entry.isDirectDependency)
                    continue;
                result.Add(Map(entry, isInstalled: true));
                seen.Add(entry.name);
            }
            foreach (var p in available ?? Array.Empty<PackageInfo>())
            {
                if (!seen.Contains(p.name))
                    result.Add(Map(p, isInstalled: false));
            }
            return result;
        }

        [CliGroupCommand(Group,
            "Search packages available in the registry. Provide a name (e.g. com.unity.foo) or omit to list all. " +
            "Returns the full result synchronously (blocks until the registry query completes).",
            MainThreadRequired = false,
            Tags = new[] { "packages" })]
        public static object Search(
            [CliArg("query", "Package name to search for. Omit/empty to list all available packages.")] string query = "",
            [CliArg("offline", "Search the local cache only.")] bool offline = false,
            [CliArg("timeout", "Seconds to wait for the registry.")] double timeoutInSeconds = 60)
        {
            var start = System.Diagnostics.Stopwatch.GetTimestamp();
            var timeout = PipelineTimeout.FromSeconds(timeoutInSeconds);
            var trimmed = (query ?? string.Empty).Trim();

            var request = RunOnMain(() => string.IsNullOrEmpty(trimmed)
                ? Client.SearchAll(offline)
                : Client.Search(trimmed, offline), timeout, start);

            if (!WaitForCompletion(request, timeout, start, out var waitError))
                return new PackageSearchResponse { Success = false, Query = trimmed, Message = waitError };

            return RunOnMain<object>(() =>
            {
                if (request.Status != StatusCode.Success)
                    return new PackageSearchResponse
                    {
                        Success = false,
                        Query = trimmed,
                        Message = $"Search failed: {request.Error?.message ?? "unknown error"}"
                    };

                var packages = MapMarkingInstalled(request.Result);
                return new PackageSearchResponse
                {
                    Success = true,
                    Query = trimmed,
                    Count = packages.Count,
                    Packages = packages,
                    Message = $"{packages.Count} package(s)."
                };
            }, timeout, start);
        }

        // ---- Mutating operations (dual-mode, CAT-2509 gated) -----------------------------------

        [CliGroupCommand(Group,
            "Add a UPM package by name@version, git URL, or 'file:' local path. Async by default (returns " +
            "in_progress; poll " + CommandNames.PackageStatus + "); pass wait=true to block until added. A recompile/domain reload " +
            "follows — poll " + CommandNames.ScriptRecompileStatus + ". Requires confirm=true; use dry-run to preview.",
            MainThreadRequired = false,
            Tags = new[] { "packages" })]
        public static object Add(
            [CliArg("name", "Package to add: 'com.unity.foo@1.2.3', a git URL, or 'file:../Path'.", Required = true)] string identifier = "",
            [CliArg("confirm", "Apply the change. Without it the call is refused.")] bool confirm = false,
            [CliArg("dry-run", "Preview the change without applying it.")] bool dryRun = false,
            [CliArg("wait", "Block until the operation completes and return the result (synchronous). Default: return immediately and poll " + CommandNames.PackageStatus + ".")] bool wait = false,
            [CliArg("timeout", "Seconds to wait when wait=true.")] double timeoutInSeconds = 300)
        {
            if (!PackageIdentifier.TryParse(identifier, out var parsed, out var parseError))
                return PackageMutationResponse.Failed("add", identifier, parseError);

            if (IsBusy())
                return PackageMutationResponse.Busy("add");

            return ExecuteMutation(
                operation: "add",
                commandName: CommandNames.PackageAdd,
                argument: parsed.Identifier,
                planText: $"Add package {parsed.Description} [{parsed.Kind}]",
                confirm: confirm,
                dryRun: dryRun,
                wait: wait,
                timeout: PipelineTimeout.FromSeconds(timeoutInSeconds),
                startTimestamp: System.Diagnostics.Stopwatch.GetTimestamp(),
                start: () => Client.Add(parsed.Identifier),
                readPackage: req =>
                {
                    var added = (req as AddRequest)?.Result;
                    return added != null ? Map(added, isInstalled: true) : null;
                });
        }

        [CliGroupCommand(Group,
            "Remove a UPM package by name. Async by default (returns in_progress; poll " + CommandNames.PackageStatus + "); pass " +
            "wait=true to block until removed. A recompile/domain reload follows — poll " + CommandNames.ScriptRecompileStatus + ". " +
            "Requires confirm=true; use dry-run to preview.",
            MainThreadRequired = false,
            Tags = new[] { "packages" })]
        public static object Remove(
            [CliArg("name", "Package name to remove (e.g. com.unity.foo).", Required = true)] string name = "",
            [CliArg("confirm", "Apply the change. Without it the call is refused.")] bool confirm = false,
            [CliArg("dry-run", "Preview the change without applying it.")] bool dryRun = false,
            [CliArg("wait", "Block until the operation completes and return the result (synchronous). Default: return immediately and poll " + CommandNames.PackageStatus + ".")] bool wait = false,
            [CliArg("timeout", "Seconds to wait when wait=true.")] double timeoutInSeconds = 300)
        {
            if (string.IsNullOrWhiteSpace(name))
                return PackageMutationResponse.Failed("remove", name, "A package name is required.");

            var packageName = name.Trim();

            if (IsBusy())
                return PackageMutationResponse.Busy("remove");

            return ExecuteMutation(
                operation: "remove",
                commandName: CommandNames.PackageRemove,
                argument: packageName,
                planText: $"Remove package '{packageName}'",
                confirm: confirm,
                dryRun: dryRun,
                wait: wait,
                timeout: PipelineTimeout.FromSeconds(timeoutInSeconds),
                startTimestamp: System.Diagnostics.Stopwatch.GetTimestamp(),
                start: () => Client.Remove(packageName),
                readPackage: null);
        }

        [CliGroupCommand(Group,
            "Resolve/refresh packages from the manifest (re-fetch and re-link). May trigger a recompile/domain " +
            "reload — poll " + CommandNames.ScriptRecompileStatus + ". Its outcome is recorded for " + CommandNames.PackageStatus + ".",
            MainThreadRequired = true,
            Tags = new[] { "packages" })]
        public static object Resolve()
        {
            WriteStatus(new PackageStatus
            {
                Status = "in_progress",
                Operation = "resolve",
                StartedAt = NowIso(),
                Message = "Resolving packages..."
            });

            try
            {
                // Client.Resolve() is fire-and-forget (returns void) and schedules a resolve on a later
                // tick, so the response flushes before any reload — no request to wait on.
                Client.Resolve();
            }
            catch (Exception ex)
            {
                var failed = new PackageStatus
                {
                    Status = "failed",
                    Operation = "resolve",
                    Success = false,
                    Error = ex.Message,
                    Manifest = SafeReadManifest(),
                    CompletedAt = NowIso(),
                    Message = $"Resolve failed: {ex.Message}"
                };
                WriteStatus(failed);
                return PackageMutationResponse.Failed("resolve", null, failed.Error);
            }

            var done = new PackageStatus
            {
                Status = "completed",
                Operation = "resolve",
                Success = true,
                RequiresRecompile = true,
                Manifest = SafeReadManifest(),
                CompletedAt = NowIso(),
                Message = "Resolve requested. If assemblies changed, a domain reload follows — poll " + CommandNames.ScriptRecompileStatus + "."
            };
            WriteStatus(done);
            return ToMutationResponse(done, plan: null);
        }

        // ---- Status ----------------------------------------------------------------------------

        [CliGroupCommand(Group,
            "Status of the last async package operation (add/remove/resolve): idle | in_progress | completed | " +
            "failed, with the added package, manifest, and any error.",
            MainThreadRequired = false,
            Tags = new[] { "packages" })]
        public static JToken Status()
        {
            return StatusFileReader.ReadOrIdle(StatusFile);
        }

        // ---- Mutation execution ----------------------------------------------------------------

        /// <summary>
        /// Gate (confirm / dry-run) and kick off the mutating op, then
        /// either return <c>in_progress</c> (async, default) or block until completion (<paramref name="wait"/>).
        /// Either way the operation is tracked and a status file is written, so the result is recoverable
        /// via <c>package_status</c> even if the domain reload severs a synchronous reply.
        /// </summary>
        static object ExecuteMutation(
            string operation, string commandName, string argument, string planText,
            bool confirm, bool dryRun, bool wait, TimeSpan timeout, long startTimestamp,
            Func<Request> start, Func<Request, PackageSummary> readPackage)
        {
            if (dryRun)
                return PackageMutationResponse.DryRunPreview(operation, argument, planText, $"Dry run — {planText}");
            if (!confirm)
                return PackageMutationResponse.Rejected(operation, argument,
                    "Refused: this changes project packages. Re-run with confirm=true to apply, or dry-run=true to preview.");

            // UPM operations are not part of Unity's Undo system, so there is no undo scope here.
            var request = RunOnMain(() =>
            {
                var r = start();
                // async mode finalizes via the update-loop poller; sync mode finalizes itself.
                BeginTracking(operation, argument, r, readPackage, subscribePoll: !wait);
                return r;
            }, timeout, startTimestamp);

            if (request == null)
                return PackageMutationResponse.Failed(operation, argument, "Operation was confirmed but failed to start.");

            if (!wait)
                return PackageMutationResponse.InProgress(operation, argument, planText);

            // Synchronous: poll until complete, capturing status + result atomically on the main thread
            // (a reload can only fire between ticks, so the snapshot can't be interleaved).
            while (true)
            {
                var status = RunOnMain(TryFinalize, timeout, startTimestamp);
                if (status != null)
                    return ToMutationResponse(status, planText);

                if (TimedOut(timeout, startTimestamp))
                {
                    // Hand off to the update-loop poller so package_status still settles after we bail.
                    RunOnMain<object>(() => { SubscribePoll(); return null; }, timeout, startTimestamp);
                    return PackageMutationResponse.Failed(operation, argument,
                        $"Timed out after {PipelineTimeout.Describe(timeout)}; the operation may still be in progress — poll {CommandNames.PackageStatus}.");
                }

                Thread.Sleep(PollIntervalMs);
            }
        }

        /// <summary>Record the in-flight op, write the in_progress status, and (async only) start polling.</summary>
        static void BeginTracking(string operation, string argument, Request request,
            Func<Request, PackageSummary> readPackage, bool subscribePoll)
        {
            lock (s_Lock)
            {
                s_Request = request;
                s_Operation = operation;
                s_Argument = argument;
                s_ReadPackage = readPackage;
                s_InProgress = true;
            }

            WriteStatus(new PackageStatus
            {
                Status = "in_progress",
                Operation = operation,
                Argument = argument,
                StartedAt = NowIso(),
                Message = $"{operation} in progress. Poll {CommandNames.PackageStatus}."
            });

            if (subscribePoll)
                SubscribePoll();
        }

        static void SubscribePoll()
        {
            EditorApplication.update -= Poll;
            EditorApplication.update += Poll;
        }

        /// <summary>Update-loop poller for async mode (and a timed-out sync wait): finalize when done.</summary>
        static void Poll() => TryFinalize();

        /// <summary>
        /// If the tracked request has completed, write its final status, clear tracking, and return the
        /// status; otherwise return null. Main-thread only (reads Request members); guarded so the sync
        /// waiter and the update-loop poller can't double-finalize.
        /// </summary>
        static PackageStatus TryFinalize()
        {
            lock (s_Lock)
            {
                var request = s_Request;
                if (request == null)
                {
                    EditorApplication.update -= Poll;
                    return null;
                }
                if (!request.IsCompleted)
                    return null;

                var status = BuildStatus(s_Operation, s_Argument, request, s_ReadPackage);
                WriteStatus(status);

                s_Request = null;
                s_Operation = null;
                s_Argument = null;
                s_ReadPackage = null;
                s_InProgress = false;
                EditorApplication.update -= Poll;
                return status;
            }
        }

        static PackageStatus BuildStatus(string operation, string argument, Request request,
            Func<Request, PackageSummary> readPackage)
        {
            var status = new PackageStatus
            {
                Operation = operation,
                Argument = argument,
                CompletedAt = NowIso(),
                Manifest = SafeReadManifest()
            };

            if (request.Status == StatusCode.Success)
            {
                status.Status = "completed";
                status.Success = true;
                status.RequiresRecompile = true;
                try { status.Package = readPackage?.Invoke(request); }
                catch (Exception ex) { Debug.LogWarning($"[pipeline] package result projection failed: {ex.Message}"); }
                status.Message = $"{operation} completed.";
            }
            else
            {
                status.Status = "failed";
                status.Success = false;
                status.Error = request.Error?.message ?? "Unknown package manager error.";
                status.Message = $"{operation} failed: {status.Error}";
            }

            return status;
        }

        static PackageMutationResponse ToMutationResponse(PackageStatus s, string plan) => new PackageMutationResponse
        {
            Success = s.Success,
            Operation = s.Operation,
            Argument = s.Argument,
            Status = s.Status,
            Applied = s.Success,
            Plan = plan,
            Package = s.Package,
            Manifest = s.Manifest,
            RequiresRecompile = s.RequiresRecompile,
            Message = s.Message
        };

        /// <summary>
        /// On load, settle a status file left at "in_progress" by a domain reload (the expected outcome of
        /// a successful add/remove). The manifest change that triggered the reload has taken effect, so
        /// the op is recorded completed; the follow-on recompile is observed via recompile_status.
        /// </summary>
        static void RecoverInterruptedOperation()
        {
            try
            {
                if (!File.Exists(StatusFile))
                    return;

                var status = JsonConvert.DeserializeObject<PackageStatus>(File.ReadAllText(StatusFile));
                if (status == null || status.Status != "in_progress")
                    return;

                status.Status = "completed";
                status.Success = true;
                status.RequiresRecompile = true;
                status.CompletedAt = NowIso();
                status.Manifest = SafeReadManifest();
                status.Message = $"{status.Operation} completed (domain reload). Manifest updated; poll {CommandNames.ScriptRecompileStatus} for the recompile.";
                WriteStatus(status);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[pipeline] package status recovery failed: {ex.Message}");
            }
        }

        // ---- Helpers ---------------------------------------------------------------------------

        static bool IsBusy() => s_InProgress;

        /// <summary>
        /// Block until <paramref name="request"/> completes, polling its (main-thread-only) state via
        /// <see cref="RunOnMain"/> while this thread sleeps between checks. Returns false with
        /// <paramref name="error"/> set on timeout. Used by the read-only registry queries.
        /// </summary>
        static bool WaitForCompletion(Request request, TimeSpan timeout, long startTimestamp, out string error)
        {
            error = null;
            while (!RunOnMain(() => request.IsCompleted, timeout, startTimestamp))
            {
                if (TimedOut(timeout, startTimestamp))
                {
                    error = $"Timed out after {PipelineTimeout.Describe(timeout)} waiting for the package manager.";
                    return false;
                }
                Thread.Sleep(PollIntervalMs);
            }
            return true;
        }

        /// <summary>True once a wait that started at <paramref name="startTimestamp"/> should stop and report its timeout.</summary>
        static bool TimedOut(TimeSpan timeout, long startTimestamp) =>
            PipelineTimeout.Remaining(PipelineTimeout.StopBefore(timeout), startTimestamp) == TimeSpan.Zero;

        /// <summary>
        /// Run <paramref name="fn"/> on the Unity main thread. When invoked off-thread (the normal case
        /// for the waiting commands), it marshals through the live server's dispatcher, waiting at most
        /// what is left of <paramref name="timeout"/> since <paramref name="startTimestamp"/>; when
        /// already on the main thread (e.g. a direct in-process/test call, or no server running) it
        /// runs inline.
        /// </summary>
        static T RunOnMain<T>(Func<T> fn, TimeSpan timeout, long startTimestamp)
        {
            var dispatcher = PipelineServerStartup.Server?.Dispatcher;
            if (dispatcher != null && dispatcher.IsInitialized && !dispatcher.IsMainThread())
                return dispatcher.Invoke(fn, PipelineTimeout.Remaining(timeout, startTimestamp));
            return fn();
        }

        static Dictionary<string, string> SafeReadManifest() =>
            PackageManifest.TryRead(out var deps, out _) ? deps : null;

        static PackageSummary Map(PackageInfo p, bool isInstalled = false) => new PackageSummary
        {
            Name = p.name,
            Version = p.version,
            DisplayName = p.displayName,
            Source = p.source.ToString(),
            ResolvedPath = p.resolvedPath,
            IsDirectDependency = p.isDirectDependency,
            IsInstalled = isInstalled
        };

        /// <summary>Map registry results, flagging which are currently installed.</summary>
        static List<PackageSummary> MapMarkingInstalled(IEnumerable<PackageInfo> packages)
        {
            var installed = new HashSet<string>();
            foreach (var p in PackageInfo.GetAllRegisteredPackages())
                installed.Add(p.name);

            var list = new List<PackageSummary>();
            if (packages != null)
                foreach (var p in packages)
                    list.Add(Map(p, installed.Contains(p.name)));
            return list;
        }

        static string NowIso() => DateTime.UtcNow.ToString("o");

        static void WriteStatus(PackageStatus status)
        {
            try
            {
                File.WriteAllText(StatusFile, JsonConvert.SerializeObject(status));
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Package] Failed to write status file: {ex.Message}");
            }
        }
    }
}
