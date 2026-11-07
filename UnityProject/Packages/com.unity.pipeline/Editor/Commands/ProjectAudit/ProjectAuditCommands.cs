using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Unity.Pipeline.Commands;
using Unity.Pipeline.Editor.Authoring;
using UnityEditor;
using UnityEngine;
#if UNITY_6000_5_OR_NEWER
using Unity.Scripting.LifecycleManagement;
#endif

namespace Unity.Pipeline.Editor.Commands.ProjectAudit
{
    /// <summary>
    /// Exposes Unity's Project Auditor static-analysis scan as <c>audit</c> / <c>audit_status</c>:
    /// start a scan, poll it, read a CSV of the reported issues.
    ///
    /// Project Auditor is reached entirely by reflection (as <see cref="FocusEditorCommand"/> reaches
    /// its editor internals): this package must compile and run in editors without Project Auditor, so
    /// it takes no asmdef reference and no package dependency. One reflection path covers both Project
    /// Auditor deployments — the built-in editor module and the com.unity.project-auditor package —
    /// because they share the type name and public surface. They do not share an assembly name, so the
    /// lookup scans loaded assemblies rather than naming one; see <see cref="FindLoadedType"/>.
    ///
    /// <c>AuditAsync</c> is only partly asynchronous: it blocks its caller through assembly
    /// compilation and the first analysis phase, then finishes on its own background thread and fires
    /// <c>OnCompleted</c> there (or synchronously, when Code analysis is out of scope). So
    /// <c>audit</c> only enqueues — an <see cref="EditorApplication.update"/> hook runs the scan on a
    /// later tick — and <c>audit_status</c> answers off the main thread while compilation holds it.
    ///
    /// A resolvable Project Auditor is not necessarily a usable one: as a built-in editor module its
    /// rules ship in the separate com.unity.project-auditor-rules package, and without them it
    /// registers no modules and produces an empty report indistinguishable from a clean project.
    /// Hence the registered-modules check, reported as <c>unavailable</c>.
    /// </summary>
    [InitializeOnLoad]
#if UNITY_6000_5_OR_NEWER
    [NoAutoStaticsCleanup]
#endif
    static class ProjectAuditCommands
    {
        const string Group = "diagnostics";
        const string StatusFile = "Temp/pipeline_audit_status.json";
        const string CsvDirectory = "Temp/pipeline-audit";
        const string RulesPackageName = "com.unity.project-auditor-rules";
        const string AuditorTypeName = "Unity.ProjectAuditor.Editor.ProjectAuditor";

        static readonly object s_Lock = new object();

        // Enqueued by audit(); consumed by the OnUpdate pump on the next main-thread tick.
        static bool s_Pending;
        // True from when the pump starts a scan until OnDone (completion) or a start failure.
        static bool s_Scanning;
        // Category array for AnalysisParams.Categories (element type varies by deployment), or null = all.
        static object s_PendingCategories;
        static string s_ScanId;
        static string s_CsvPath;
        // Last status JSON successfully written; lets audit_status answer while a write is in flight.
        static volatile string s_LastStatusJson;

        static ProjectAuditCommands()
        {
            // A scan cannot survive a domain reload (its background thread and our static state are
            // gone), so flip any dangling "scanning" status to "interrupted" before watching again.
            ReconcileAfterReload();
            EditorApplication.update += OnUpdate;
        }

        [CliGroupCommand(Group, "Run a Project Auditor static-analysis scan. Returns immediately; poll " + CommandNames.DiagnosticsAuditStatus + " until status is 'completed', then read the CSV.", MainThreadRequired = false, Tags = new[] { "observability/audit" })]
        public static object RunAudit(
            [CliArg("categories", "Comma-separated issue categories to scan (e.g. Code,ProjectSetting,Texture). Default: all categories.")] string categories = "",
            [CliArg("output-path", "CSV output path (absolute or relative to the project root). Defaults to Temp/pipeline-audit/<scanId>.csv.")] string output = "")
        {
            if (!TryResolve(out var pa, out var error))
                return new { status = "unavailable", message = error };

            var requested = ParseCategoryNames(categories);
            var validNames = Enum.GetNames(pa.IssueCategoryType);
            var validationError = ValidateCategories(requested, validNames);
            if (validationError != null)
                return new { status = "error", message = validationError };

            lock (s_Lock)
            {
                if (s_Pending || s_Scanning)
                    return new { status = "busy", message = "An audit is already queued or running. Poll " + CommandNames.DiagnosticsAuditStatus + "." };

                s_ScanId = Guid.NewGuid().ToString("N").Substring(0, 8);
                s_CsvPath = ResolveCsvPath(output, s_ScanId);
                s_PendingCategories = BuildCategoriesArray(pa, requested);
                s_Pending = true;

                // Written inside the lock: the pump can take it on the very next frame and write a
                // terminal status synchronously (unavailable, or completed when the scan runs inline).
                // Writing after the release would clobber that, wedging audit_status on "scanning".
                WriteStatus(new AuditStatus { Status = "scanning", ScanId = s_ScanId, CsvPath = s_CsvPath });
            }

            return new { status = "scanning", scanId = s_ScanId, csvPath = s_CsvPath };
        }

        [CliGroupCommand(Group, "Get the status of the last audit: idle | scanning | completed | failed | interrupted | unavailable.", MainThreadRequired = false, Tags = new[] { "observability/audit" })]
        public static JToken AuditStatus()
        {
            if (!TryResolve(out _, out var error))
                return JObject.FromObject(new AuditStatus { Status = "unavailable", Message = error });

            if (!File.Exists(StatusFile))
                return StatusFileReader.Idle;

            // WriteStatus truncates then rewrites, and its writer is usually another thread (the pump,
            // or Project Auditor's background thread) while this read serves an HTTP worker. A read
            // landing mid-write either throws (sharing violation) or returns truncated JSON, so retry,
            // then fall back to the last status we wrote - the file will match it once the write lands.
            for (var attempt = 0; attempt < 3; attempt++)
            {
                if (attempt > 0)
                    Thread.Sleep(20);

                try
                {
                    var json = File.ReadAllText(StatusFile);
                    // WriteStatus writes serialized JSON with nothing after it, so a closing '}' means
                    // this read caught a whole object rather than a half-written one.
                    if (json.Length > 1 && json[json.Length - 1] == '}')
                        return StatusFileReader.Parse(json);
                }
                catch (IOException)
                {
                    // Mid-write collision; retry.
                }
            }

            return StatusFileReader.Parse(s_LastStatusJson ?? File.ReadAllText(StatusFile));
        }

        /// <summary>
        /// Main-thread pump: picks up a queued request and runs the audit. Building AnalysisParams and
        /// calling AuditAsync must happen on the main thread; AuditAsync then blocks here through
        /// compilation and returns once the remaining analysis is handed to its background thread.
        /// </summary>
        static void OnUpdate()
        {
            object categories;
            lock (s_Lock)
            {
                if (!s_Pending || s_Scanning)
                    return;
                s_Pending = false;
                s_Scanning = true;
                categories = s_PendingCategories;
            }

            try
            {
                if (!StartAudit(categories))
                {
                    // RunAudit wrote the terminal status and OnDone will not run: release the guard here.
                    lock (s_Lock) { s_Scanning = false; }
                }
            }
            catch (Exception ex)
            {
                // Only up-front and synchronous failures reach here; once AuditAsync has handed off to
                // its background thread, OnDone owns both the outcome and s_Scanning.
                WriteStatus(new AuditStatus { Status = "failed", ScanId = s_ScanId, Error = ex.Message });
                lock (s_Lock) { s_Scanning = false; }
            }
        }

        /// <summary>
        /// Starts the scan. Returns false when no scan was started, in which case the terminal status
        /// has already been written and no completion callback will fire.
        /// </summary>
        static bool StartAudit(object categories)
        {
            if (!TryResolve(out var pa, out var error))
                throw new InvalidOperationException(error);

            // Constructing ProjectAuditor is what initializes its modules, so this is the earliest
            // point at which the "has usable rules" precondition can be checked.
            var auditor = Activator.CreateInstance(pa.ProjectAuditorType);
            if (HasNoModules(pa, auditor))
            {
                WriteStatus(new AuditStatus
                {
                    Status = "unavailable",
                    ScanId = s_ScanId,
                    Message = "Project Auditor registered no analysis modules, so it cannot analyze this " +
                        $"project. Install the Project Auditor Rules package ({RulesPackageName})."
                });
                return false;
            }

            var analysisParams = Activator.CreateInstance(pa.AnalysisParamsType, new object[] { true });
            if (categories != null)
                pa.CategoriesField.SetValue(analysisParams, categories);

            // Wire OnCompleted, which is an Action<Report>. OnDone takes 'object'; relaxed delegate
            // binding permits it because Report derives from object (reference-type contravariance).
            var actionType = typeof(Action<>).MakeGenericType(pa.ReportType);
            var onDone = Delegate.CreateDelegate(actionType, typeof(ProjectAuditCommands)
                .GetMethod(nameof(OnDone), BindingFlags.NonPublic | BindingFlags.Static));
            pa.OnCompletedField.SetValue(analysisParams, onDone);

            // AuditAsync(AnalysisParams, IProgress); a null progress means no progress and no cancel hook.
            pa.AuditAsyncMethod.Invoke(auditor, new[] { analysisParams, null });
            return true;
        }

        /// <summary>No registered modules means the rules package is missing: a scan would complete
        /// instantly with an empty report. False when the reflected accessor is absent.</summary>
        static bool HasNoModules(ResolvedProjectAuditor pa, object auditor)
        {
            if (pa.GetModulesMethod == null)
                return false;
            return pa.GetModulesMethod.Invoke(auditor, null) is ICollection modules && modules.Count == 0;
        }

        /// <summary>
        /// Completion callback bound to AnalysisParams.OnCompleted. Fires on Project Auditor's
        /// background analysis thread (or synchronously inside AuditAsync when Code analysis is not in
        /// scope). Only does thread-safe work: reads the report and writes the CSV + status files.
        /// </summary>
        static void OnDone(object report)
        {
            try
            {
                if (!TryResolve(out var pa, out var error))
                    throw new InvalidOperationException(error);

                var rows = CollectRows(pa, report);
                WriteCsv(s_CsvPath, rows);
                WriteStatus(new AuditStatus
                {
                    Status = "completed",
                    ScanId = s_ScanId,
                    CsvPath = s_CsvPath,
                    IssueCount = rows.Count
                });
            }
            catch (Exception ex)
            {
                WriteStatus(new AuditStatus { Status = "failed", ScanId = s_ScanId, Error = ex.Message });
            }
            finally
            {
                lock (s_Lock) { s_Scanning = false; }
            }
        }

        /// <summary>
        /// Enumerate the report's issues (ReportItems whose descriptor is valid — i.e. diagnostics, not
        /// raw-inventory insights) and project each to a CSV row. Reflection only; safe off-thread.
        /// </summary>
        static List<string[]> CollectRows(ResolvedProjectAuditor pa, object report)
        {
            var rows = new List<string[]>();
            var all = pa.GetAllIssuesMethod.Invoke(report, null) as IEnumerable;
            if (all == null)
                return rows;

            foreach (var item in all)
            {
                if (item == null || !(bool)pa.IsIssueMethod.Invoke(item, null))
                    continue;

                var descriptor = pa.GetDescriptorMethod.Invoke(pa.IdProperty.GetValue(item), null);
                var line = (int)pa.LineProperty.GetValue(item);

                rows.Add(new[]
                {
                    pa.CategoryProperty.GetValue(item)?.ToString() ?? string.Empty,
                    pa.SeverityProperty.GetValue(item)?.ToString() ?? string.Empty,
                    pa.AreasField.GetValue(descriptor)?.ToString() ?? string.Empty,
                    pa.DescriptionProperty.GetValue(item) as string ?? string.Empty,
                    pa.RelativePathProperty.GetValue(item) as string ?? string.Empty,
                    line > 0 ? line.ToString() : string.Empty,
                    pa.IdField.GetValue(descriptor) as string ?? string.Empty,
                    pa.RecommendationField.GetValue(descriptor) as string ?? string.Empty,
                });
            }

            return rows;
        }

        static readonly string[] k_CsvHeader =
        {
            "Category", "Severity", "Areas", "Description", "RelativePath", "Line", "DescriptorId", "Recommendation"
        };

        static void WriteCsv(string path, List<string[]> rows)
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            var sb = new StringBuilder();
            AppendCsvRow(sb, k_CsvHeader);
            foreach (var row in rows)
                AppendCsvRow(sb, row);

            File.WriteAllText(path, sb.ToString());
        }

        static void AppendCsvRow(StringBuilder sb, string[] fields)
        {
            for (var i = 0; i < fields.Length; i++)
            {
                if (i > 0)
                    sb.Append(',');
                sb.Append(EscapeCsv(fields[i]));
            }
            sb.Append("\r\n");
        }

        /// <summary>RFC 4180 escaping: quote a field containing a comma, quote or newline; double any embedded quotes.</summary>
        internal static string EscapeCsv(string field)
        {
            field = field ?? string.Empty;
            if (field.IndexOfAny(new[] { ',', '"', '\n', '\r' }) < 0)
                return field;
            return "\"" + field.Replace("\"", "\"\"") + "\"";
        }

        /// <summary>
        /// Returns null when every requested category name is valid, otherwise an error message listing
        /// the unknown names and the valid values. Pure (valid names injected) so it needs no Project
        /// Auditor types to test.
        /// </summary>
        internal static string ValidateCategories(IReadOnlyList<string> requested, IReadOnlyCollection<string> validNames)
        {
            if (requested == null || requested.Count == 0)
                return null;

            var unknown = requested
                .Where(r => !validNames.Any(v => string.Equals(v, r, StringComparison.OrdinalIgnoreCase)))
                .ToList();
            if (unknown.Count == 0)
                return null;

            return $"Unknown categor{(unknown.Count == 1 ? "y" : "ies")}: {string.Join(", ", unknown)}. " +
                $"Valid categories: {string.Join(", ", validNames)}.";
        }

        static List<string> ParseCategoryNames(string categories)
        {
            if (string.IsNullOrWhiteSpace(categories))
                return new List<string>();
            return categories.Split(',')
                .Select(c => c.Trim())
                .Where(c => c.Length > 0)
                .ToList();
        }

        /// <summary>Build the array for AnalysisParams.Categories, or null when no filter was requested (= all).</summary>
        static object BuildCategoriesArray(ResolvedProjectAuditor pa, IReadOnlyList<string> requested)
        {
            if (requested.Count == 0)
                return null;
            return BuildCategoryArray(pa.CategoryElementType, pa.CategoryWrapperCtor, pa.IssueCategoryType, requested);
        }

        /// <summary>
        /// Build the typed category array. The element type is a deployment detail — the standalone
        /// package stores the bare enum, the built-in module a serialization wrapper — so it and its
        /// wrapping constructor are passed in, which also makes this testable without Project Auditor.
        /// </summary>
        internal static Array BuildCategoryArray(Type elementType, ConstructorInfo wrapperCtor, Type enumType,
            IReadOnlyList<string> requested)
        {
            var array = Array.CreateInstance(elementType, requested.Count);
            for (var i = 0; i < requested.Count; i++)
            {
                var category = Enum.Parse(enumType, requested[i], ignoreCase: true);
                array.SetValue(wrapperCtor == null ? category : wrapperCtor.Invoke(new[] { category }), i);
            }
            return array;
        }

        static string ResolveCsvPath(string output, string scanId)
        {
            var projectRoot = Path.GetDirectoryName(Application.dataPath);
            if (!string.IsNullOrWhiteSpace(output))
                return ProjectPaths.ResolveFile(output, projectRoot);
            return Path.Combine(projectRoot, CsvDirectory, scanId + ".csv");
        }

        static void ReconcileAfterReload()
        {
            if (!File.Exists(StatusFile))
                return;

            AuditStatus prior;
            try
            {
                prior = JsonConvert.DeserializeObject<AuditStatus>(File.ReadAllText(StatusFile));
            }
            catch
            {
                return;
            }

            if (prior != null && prior.Status == "scanning")
                WriteStatus(new AuditStatus { Status = "interrupted", ScanId = prior.ScanId });
        }

        static void WriteStatus(AuditStatus status)
        {
            var json = JsonConvert.SerializeObject(status);
            try
            {
                File.WriteAllText(StatusFile, json);
                // Cached only after a successful write, so the audit_status fallback can never report a
                // status the file does not hold.
                s_LastStatusJson = json;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ProjectAudit] Failed to write status file: {ex.Message}");
            }
        }

        #region reflection resolution

        static readonly object s_ResolveLock = new object();
        static ResolvedProjectAuditor s_Resolved;

        /// <summary>
        /// Resolve the Project Auditor types/members once (thread-safe: audit_status runs off the main
        /// thread). Returns false with a clear message when Project Auditor is not installed or an
        /// expected member is missing (e.g. an incompatible version).
        /// </summary>
        static bool TryResolve(out ResolvedProjectAuditor pa, out string error)
        {
            lock (s_ResolveLock)
            {
                if (s_Resolved != null)
                {
                    pa = s_Resolved;
                    error = null;
                    return true;
                }

                pa = null;
                error = null;

                var auditorType = FindLoadedType(AuditorTypeName);
                if (auditorType == null)
                {
                    error = $"Project Auditor is not installed in this Editor ({AuditorTypeName} not found).";
                    return false;
                }

                var asm = auditorType.Assembly;
                try
                {
                    var resolved = new ResolvedProjectAuditor(auditorType, asm);
                    s_Resolved = resolved;
                    pa = resolved;
                    return true;
                }
                catch (Exception ex)
                {
                    error = $"Project Auditor is present but its API could not be resolved (version mismatch?): {ex.Message}";
                    return false;
                }
            }
        }

        /// <summary>
        /// Find a type by full name in any loaded assembly. The assembly name cannot be used to narrow
        /// the search: the same type ships in <c>Unity.ProjectAuditor.Editor</c> as a package but in
        /// <c>UnityEditor.ProjectAuditorModule</c> as a built-in editor module (measured on 6000.7), so
        /// an assembly-qualified <see cref="Type.GetType(string)"/> finds only the package deployment.
        /// </summary>
        static Type FindLoadedType(string fullName)
        {
            foreach (var asm in PipelineUtils.GetLoadedAssemblies())
            {
                var type = asm.GetType(fullName, throwOnError: false);
                if (type != null)
                    return type;
            }

            return null;
        }

        #endregion
    }

    /// <summary>
    /// Cached Project Auditor reflection handles. The constructor throws if any expected member is
    /// missing, so <see cref="ProjectAuditCommands"/> can report a clean "unavailable" rather than
    /// failing mid-scan. All members come from the one assembly that declares ProjectAuditor.
    /// </summary>
    sealed class ResolvedProjectAuditor
    {
        public readonly Type ProjectAuditorType;
        public readonly Type AnalysisParamsType;
        public readonly Type ReportType;
        public readonly Type IssueCategoryType;

        public readonly MethodInfo AuditAsyncMethod;
        public readonly FieldInfo CategoriesField;
        // Element type of AnalysisParams.Categories: the bare IssueCategory in the standalone Project
        // Auditor package, a SerializableEnum<IssueCategory> wrapper in the built-in editor module.
        public readonly Type CategoryElementType;
        // Constructor wrapping an IssueCategory into that element type; null when it is the bare enum.
        public readonly ConstructorInfo CategoryWrapperCtor;
        // Optional (internal API): when absent, the registered-modules precondition check is skipped.
        public readonly MethodInfo GetModulesMethod;
        public readonly FieldInfo OnCompletedField;

        public readonly MethodInfo GetAllIssuesMethod;
        public readonly MethodInfo IsIssueMethod;
        public readonly PropertyInfo CategoryProperty;
        public readonly PropertyInfo SeverityProperty;
        public readonly PropertyInfo DescriptionProperty;
        public readonly PropertyInfo RelativePathProperty;
        public readonly PropertyInfo LineProperty;
        public readonly PropertyInfo IdProperty;

        public readonly MethodInfo GetDescriptorMethod;
        public readonly FieldInfo IdField;
        public readonly FieldInfo AreasField;
        public readonly FieldInfo RecommendationField;

        public ResolvedProjectAuditor(Type auditorType, Assembly asm)
        {
            ProjectAuditorType = auditorType;
            AnalysisParamsType = Require(asm, "Unity.ProjectAuditor.Editor.AnalysisParams");
            ReportType = Require(asm, "Unity.ProjectAuditor.Editor.Report");
            IssueCategoryType = Require(asm, "Unity.ProjectAuditor.Editor.IssueCategory");
            var reportItemType = Require(asm, "Unity.ProjectAuditor.Editor.ReportItem");
            var descriptorType = Require(asm, "Unity.ProjectAuditor.Editor.Descriptor");

            AuditAsyncMethod = Require(ProjectAuditorType.GetMethods()
                .FirstOrDefault(m => m.Name == "AuditAsync" && m.GetParameters().Length == 2), "ProjectAuditor.AuditAsync");
            CategoriesField = Require(AnalysisParamsType.GetField("Categories"), "AnalysisParams.Categories");
            CategoryElementType = Require(CategoriesField.FieldType.GetElementType(), "AnalysisParams.Categories element type");
            CategoryWrapperCtor = CategoryElementType == IssueCategoryType
                ? null
                : Require(CategoryElementType.GetConstructor(new[] { IssueCategoryType }),
                    $"{CategoryElementType.Name}(IssueCategory) constructor");
            GetModulesMethod = ProjectAuditorType.GetMethod("GetModules",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null);
            OnCompletedField = Require(AnalysisParamsType.GetField("OnCompleted"), "AnalysisParams.OnCompleted");

            GetAllIssuesMethod = Require(ReportType.GetMethod("GetAllIssues", Type.EmptyTypes), "Report.GetAllIssues");
            IsIssueMethod = Require(reportItemType.GetMethod("IsIssue", Type.EmptyTypes), "ReportItem.IsIssue");
            CategoryProperty = Require(reportItemType.GetProperty("Category"), "ReportItem.Category");
            SeverityProperty = Require(reportItemType.GetProperty("Severity"), "ReportItem.Severity");
            DescriptionProperty = Require(reportItemType.GetProperty("Description"), "ReportItem.Description");
            RelativePathProperty = Require(reportItemType.GetProperty("RelativePath"), "ReportItem.RelativePath");
            LineProperty = Require(reportItemType.GetProperty("Line"), "ReportItem.Line");
            IdProperty = Require(reportItemType.GetProperty("Id"), "ReportItem.Id");

            GetDescriptorMethod = Require(IdProperty.PropertyType.GetMethod("GetDescriptor", Type.EmptyTypes), "DescriptorId.GetDescriptor");
            IdField = Require(descriptorType.GetField("Id"), "Descriptor.Id");
            AreasField = Require(descriptorType.GetField("Areas"), "Descriptor.Areas");
            RecommendationField = Require(descriptorType.GetField("Recommendation"), "Descriptor.Recommendation");
        }

        static Type Require(Assembly asm, string fullName)
        {
            return Require(asm.GetType(fullName, throwOnError: false), fullName);
        }

        static T Require<T>(T member, string name) where T : class
        {
            if (member == null)
                throw new MissingMemberException($"Project Auditor member not found: {name}");
            return member;
        }
    }

    /// <summary>Status/result payload for the <c>audit</c> / <c>audit_status</c> commands.</summary>
    [Serializable]
    class AuditStatus
    {
        [JsonProperty("status")]
        public string Status { get; set; }

        [JsonProperty("scanId", NullValueHandling = NullValueHandling.Ignore)]
        public string ScanId { get; set; }

        [JsonProperty("csvPath", NullValueHandling = NullValueHandling.Ignore)]
        public string CsvPath { get; set; }

        [JsonProperty("issueCount", NullValueHandling = NullValueHandling.Ignore)]
        public int? IssueCount { get; set; }

        [JsonProperty("error", NullValueHandling = NullValueHandling.Ignore)]
        public string Error { get; set; }

        [JsonProperty("message", NullValueHandling = NullValueHandling.Ignore)]
        public string Message { get; set; }
    }
}
