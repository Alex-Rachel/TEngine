using System;
using System.Collections.Generic;
using System.IO;
using System.Diagnostics;
using Newtonsoft.Json;
using UnityEngine;
using Unity.Pipeline.Security;
#if UNITY_6000_5_OR_NEWER
using Unity.Scripting.LifecycleManagement;
#endif

namespace Unity.Pipeline.Models
{
    /// <summary>
    /// The Editor's instance descriptor, read by CLI tools to find running Editor instances, at
    /// Library/Pipeline/.unity-pipeline-port. Written by <see cref="PipelineDescriptorPublisher"/>.
    /// </summary>
    [Serializable]
#if UNITY_6000_5_OR_NEWER
    [NoAutoStaticsCleanup]
#endif
    class EditorInstanceDescriptor
    {
        private const string DescriptorFileName = ".unity-pipeline-port";

        private const string VirtualProjectsDirName = "VP";

        // Multiplayer Play Mode launch-arg flags, kept in sync with
        // Unity.Pipeline.Editor.MppmInstanceInfo (which lives in the Editor assembly).
        private const string CloneFlag = "--virtual-project-clone";
        private const string VpIdFlag = "-vpId";
        private const string NameFlag = "-name";

        /// <summary>
        /// Corrective guidance surfaced via <see cref="Info"/> when the Editor is interactive and
        /// wasn't launched with -automated (UUM-149977). Read by every client before it can even
        /// connect (see <see cref="Info"/>), unlike a console warning which was logged once per
        /// server instance and nobody watches anyway.
        /// </summary>
        private const string AutomationInfoMessage =
            "This editor has not been opened in automated mode and therefore could get stuck on modal dialogs. Human intervention may be required.";

        /// <summary>
        /// Process ID of the Unity Editor
        /// </summary>
        [JsonProperty("pid")]
        public int Pid { get; set; }

        /// <summary>
        /// HTTP port the pipeline server is listening on
        /// </summary>
        [JsonProperty("port")]
        public int Port { get; set; }

        /// <summary>
        /// Full path to the Unity project
        /// </summary>
        [JsonProperty("projectPath")]
        public string ProjectPath { get; set; }

        /// <summary>
        /// Name of the Unity project
        /// </summary>
        [JsonProperty("projectName")]
        public string ProjectName { get; set; }

        /// <summary>
        /// Unity Editor version string
        /// </summary>
        [JsonProperty("unityVersion")]
        public string UnityVersion { get; set; }

        /// <summary>
        /// Editor mode: "editor", "batchmode"
        /// </summary>
        [JsonProperty("mode")]
        public string Mode { get; set; }

        /// <summary>
        /// When the Editor instance was started
        /// </summary>
        [JsonProperty("startedAt")]
        public DateTime StartedAt { get; set; }

        /// <summary>
        /// Last heartbeat timestamp
        /// </summary>
        [JsonProperty("lastHeartbeat")]
        public DateTime LastHeartbeat { get; set; }

        /// <summary>
        /// Security token for code evaluation commands
        /// </summary>
        [JsonProperty("evalToken")]
        public string EvalToken { get; set; }

        /// <summary>
        /// Corrective guidance for the client, e.g. warning that this instance could get stuck on
        /// a modal dialog. Null when there is nothing to say (launched with -automated, or
        /// batchmode — which can't show modals regardless, see <see cref="Mode"/>) — omitted from
        /// the JSON entirely, same convention as <see cref="BaseResponse.Warnings"/>. Read this
        /// before issuing commands: it's the one piece of state every client sees, since reading
        /// the descriptor is a mandatory first step just to connect (UUM-149977). There is no
        /// separate "automated" flag: it would only ever disagree with a null/non-null `Info` for
        /// batchmode instances, and that distinction carries no information — batchmode can't show
        /// modal dialogs either way.
        /// </summary>
        [JsonProperty("info", NullValueHandling = NullValueHandling.Ignore)]
        public string Info { get; set; }

        /// <summary>
        /// Optional wire features this server understands (e.g. <c>exec.argv</c>), mirrored from
        /// <see cref="BasePipelineServer.EditorCapabilities"/> so the two carriers cannot drift.
        ///
        /// Carried in the descriptor because every client already reads this file locally to
        /// obtain the eval token, so negotiation costs no extra request. A descriptor with NO
        /// capabilities key at all comes from a server too old to have the field, and a client
        /// must then assume it supports none of these features.
        /// </summary>
        [JsonProperty("capabilities", NullValueHandling = NullValueHandling.Ignore)]
        public string[] Capabilities { get; set; }

        /// <summary>
        /// Multiplayer Play Mode role of this instance: "main" or "clone". Defaults to "main".
        /// </summary>
        [JsonProperty("role")]
        public string Role { get; set; }

        /// <summary>
        /// Human-readable instance name (e.g. "Main Editor", "Player 2"). Display/selection aid.
        /// </summary>
        [JsonProperty("instanceName")]
        public string InstanceName { get; set; }

        /// <summary>
        /// Multiplayer Play Mode virtual-project identifier for a clone (the <c>-vpId</c> value);
        /// null on the main Editor.
        /// </summary>
        [JsonProperty("vpId")]
        public string VpId { get; set; }

        /// <summary>
        /// The ways to connect, in order of preference, e.g. <c>{ "type": "http", "port": "52814" }</c>.
        /// Clients use the first <c>type</c> they support.
        /// </summary>
        [JsonProperty("endpoints", NullValueHandling = NullValueHandling.Ignore)]
        public List<Dictionary<string, string>> Endpoints { get; set; }

        /// <summary>
        /// Create instance descriptor for current Editor session
        /// </summary>
        /// <param name="port">Port the server is listening on.</param>
        /// <param name="automated">Whether the Editor was launched with -automated.</param>
        /// <returns>A populated descriptor for this Editor session.</returns>
        public static EditorInstanceDescriptor CreateCurrent(int port, bool automated)
        {
            try
            {
                var projectPath = Path.GetDirectoryName(Application.dataPath);
                var projectName = Path.GetFileName(projectPath);
                var pid = Process.GetCurrentProcess().Id;
                var unityVersion = Application.unityVersion;
                var isBatchMode = Application.isBatchMode;

                // Generate eval token at server startup for CLI auto-discovery
                var evalToken = SecurityTokenManager.GetOrCreateToken();

                // Multiplayer Play Mode identity, derived from the launch args so a virtual-player
                // clone publishes its own role/name/vpId instead of looking like the main Editor.
                // Mirrors the arg flags in Unity.Pipeline.Editor.MppmInstanceInfo (kept in sync),
                // resolved here because the descriptor is published from this (Runtime) assembly.
                var args = Environment.GetCommandLineArgs();
                var isClone = HasCommandLineFlag(args, CloneFlag);
                var role = isClone ? "clone" : "main";
                var vpId = isClone ? GetCommandLineValue(args, VpIdFlag) : null;
                var instanceName = isClone
                    ? (GetCommandLineValue(args, NameFlag) ?? "clone")
                    : "Main Editor";

                return new EditorInstanceDescriptor
                {
                    Capabilities = BasePipelineServer.EditorCapabilities,
                    Pid = pid,
                    Port = port,
                    ProjectPath = projectPath,
                    ProjectName = projectName,
                    UnityVersion = unityVersion,
                    Mode = isBatchMode ? "batchmode" : "editor",
                    StartedAt = DateTime.UtcNow,
                    LastHeartbeat = DateTime.UtcNow,
                    EvalToken = evalToken,
                    // Only in an interactive Editor: batchmode (CI, upm-pvp, UTR) can't show modal
                    // popups, so the guidance would be pure noise there.
                    Info = (!automated && !isBatchMode) ? AutomationInfoMessage : null,
                    Role = role,
                    InstanceName = instanceName,
                    VpId = vpId
                };
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError($"CreateCurrent failed: {ex.Message}");
                UnityEngine.Debug.LogError($"Stack trace: {ex.StackTrace}");
                throw;
            }
        }

        /// <summary>
        /// Read instance descriptor from project root
        /// </summary>
        /// <param name="projectPath">Absolute path to the Unity project.</param>
        /// <returns>The descriptor, or null if missing/corrupted.</returns>
        public static EditorInstanceDescriptor ReadFromProjectRoot(string projectPath)
        {
            var filePath = GetDescriptorFilePath(projectPath);

            if (!File.Exists(filePath))
                return null;

            try
            {
                var json = File.ReadAllText(filePath);
                return JsonConvert.DeserializeObject<EditorInstanceDescriptor>(json);
            }
            catch
            {
                // Invalid or corrupted file
                return null;
            }
        }

        /// <summary>
        /// Absolute path to the editor descriptor file for a project: the git-ignored
        /// Library/Pipeline folder under the project root. Kept in one place so discovery code
        /// and tests can't drift from where this class itself reads/writes.
        /// </summary>
        /// <param name="projectPath">Absolute path to the Unity project.</param>
        /// <returns>The descriptor file's absolute path.</returns>
        public static string GetDescriptorFilePath(string projectPath)
        {
            return Path.Combine(projectPath, "Library", "Pipeline", DescriptorFileName);
        }

        /// <summary>
        /// Directory under a project's Library where Multiplayer Play Mode stores virtual player
        /// (clone) sub-projects (<c>&lt;mainProjectPath&gt;/Library/VP</c>).
        /// </summary>
        public static string GetVirtualProjectsDir(string mainProjectPath)
        {
            return Path.Combine(mainProjectPath, "Library", VirtualProjectsDirName);
        }

        private static bool HasCommandLineFlag(string[] args, string flag)
        {
            if (args == null)
                return false;

            foreach (var arg in args)
            {
                if (arg == flag || arg.StartsWith(flag + "=", StringComparison.Ordinal))
                    return true;
            }

            return false;
        }

        private static string GetCommandLineValue(string[] args, string flag)
        {
            if (args == null)
                return null;

            var prefix = flag + "=";
            for (var i = 0; i < args.Length; i++)
            {
                var arg = args[i];
                if (arg.StartsWith(prefix, StringComparison.Ordinal))
                    return arg.Substring(prefix.Length);
                if (arg == flag && i + 1 < args.Length)
                    return args[i + 1];
            }

            return null;
        }

        /// <summary>
        /// Discover the descriptors of every Pipeline instance reachable from a main project: the
        /// main Editor itself plus each Multiplayer Play Mode virtual player clone. Clones are
        /// separate Unity projects under <c>Library/VP/&lt;vpId&gt;</c> with their own Library, so
        /// each publishes its own canonical descriptor at
        /// <c>&lt;cloneProject&gt;/Library/Pipeline/.unity-pipeline-port</c>; this reads the main's
        /// descriptor and scans the VP folder for the clones'. Corrupt or missing files are skipped;
        /// liveness (PID still running) is the caller's concern.
        /// </summary>
        public static List<EditorInstanceDescriptor> DiscoverAll(string mainProjectPath)
        {
            var result = new List<EditorInstanceDescriptor>();

            var main = ReadFromProjectRoot(mainProjectPath);
            if (main != null)
                result.Add(main);

            var vpDir = GetVirtualProjectsDir(mainProjectPath);
            if (Directory.Exists(vpDir))
            {
                foreach (var cloneProject in Directory.GetDirectories(vpDir))
                {
                    var descriptor = ReadFromProjectRoot(cloneProject);
                    if (descriptor != null)
                        result.Add(descriptor);
                }
            }

            return result;
        }
    }
}