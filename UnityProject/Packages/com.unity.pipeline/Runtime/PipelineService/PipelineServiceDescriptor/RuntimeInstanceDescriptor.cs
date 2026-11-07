using System;
using Newtonsoft.Json;
using UnityEngine;
using Unity.Pipeline.Config;
using System.IO;
using Unity.Pipeline.Security;
#if UNITY_6000_5_OR_NEWER
using Unity.Scripting.LifecycleManagement;
#endif

namespace Unity.Pipeline.Models
{
    /// <summary>
    /// Instance descriptor for runtime Unity Player builds with Pipeline server.
    /// Different from EditorInstanceDescriptor to reflect runtime context and security.
    /// </summary>
    [Serializable]
#if UNITY_6000_5_OR_NEWER
    [NoAutoStaticsCleanup]
#endif
    class RuntimeInstanceDescriptor
    {
        private const string RuntimeDescriptorFileName = ".unity-pipeline-runtime-port";

        /// <summary>
        /// Process ID of the Unity Player application
        /// </summary>
        [JsonProperty("pid")]
        public int Pid { get; set; }

        /// <summary>
        /// HTTP port the pipeline server is listening on (7900-7999 range)
        /// </summary>
        [JsonProperty("port")]
        public int Port { get; set; }

        /// <summary>
        /// Unity runtime platform (Windows, macOS, Linux, etc.)
        /// </summary>
        [JsonProperty("platform")]
        public string Platform { get; set; }

        /// <summary>
        /// Unity Player version string
        /// </summary>
        [JsonProperty("unityVersion")]
        public string UnityVersion { get; set; }

        /// <summary>
        /// Unique build identifier
        /// </summary>
        [JsonProperty("buildGuid")]
        public string BuildGuid { get; set; }

        /// <summary>
        /// When the runtime instance was started
        /// </summary>
        [JsonProperty("startedAt")]
        public DateTime StartedAt { get; set; }

        /// <summary>
        /// Last heartbeat timestamp
        /// </summary>
        [JsonProperty("lastHeartbeat")]
        public DateTime LastHeartbeat { get; set; }

        /// <summary>
        /// Working directory where the application is running
        /// </summary>
        [JsonProperty("workingDirectory")]
        public string WorkingDirectory { get; set; }

        /// <summary>
        /// Security token used to authorize requests to this runtime server.
        /// </summary>
        [JsonProperty("evalToken")]
        public string EvalToken { get; set; }

        /// <summary>
        /// Optional wire features this server understands (e.g. <c>exec.argv</c>), mirrored from
        /// <see cref="BasePipelineServer.Capabilities"/> so the two carriers cannot drift.
        ///
        /// Carried in the descriptor because every client already reads this file locally to
        /// obtain the eval token, so negotiation costs no extra request. A descriptor with NO
        /// capabilities key at all comes from a server too old to have the field, and a client
        /// must then assume it supports none of these features.
        /// </summary>
        [JsonProperty("capabilities", NullValueHandling = NullValueHandling.Ignore)]
        public string[] Capabilities { get; set; }

        /// <summary>
        /// Create runtime instance descriptor for current Player application.
        /// </summary>
        /// <param name="port">Port the runtime server is listening on.</param>
        /// <param name="config">The runtime pipeline configuration (currently unused, reserved for future fields).</param>
        /// <returns>A populated descriptor for this Player instance.</returns>
        public static RuntimeInstanceDescriptor CreateCurrent(int port, RuntimePipelineConfig config)
        {
            try
            {
                var pid = System.Diagnostics.Process.GetCurrentProcess().Id;
                var unityVersion = Application.unityVersion;
                var platform = Application.platform.ToString();
                var buildGuid = Application.buildGUID;
                var workingDir = Directory.GetCurrentDirectory();

                var token = SecurityTokenManager.GetOrCreateToken();

                return new RuntimeInstanceDescriptor
                {
                    Capabilities = BasePipelineServer.Capabilities,
                    Pid = pid,
                    Port = port,
                    Platform = platform,
                    UnityVersion = unityVersion,
                    BuildGuid = buildGuid,
                    StartedAt = DateTime.UtcNow,
                    LastHeartbeat = DateTime.UtcNow,
                    WorkingDirectory = workingDir,
                    EvalToken = token
                };
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError($"Pipeline: CreateCurrent failed: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Read runtime instance descriptor from working directory.
        /// </summary>
        /// <returns>The descriptor, or null if missing/corrupted.</returns>
        public static RuntimeInstanceDescriptor ReadFromWorkingDirectory()
        {
            var filePath = GetDescriptorFilePath();

            if (!File.Exists(filePath))
                return null;

            try
            {
                var json = File.ReadAllText(filePath);
                return JsonConvert.DeserializeObject<RuntimeInstanceDescriptor>(json);
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogWarning($"Pipeline: Failed to read runtime descriptor: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Test-only override of the detected platform, so the Android branch below is reachable
        /// without a real device. Null (the default) uses the real <see cref="Application.platform"/>.
        /// </summary>
        internal static RuntimePlatform? OverridePlatform;

        internal static string GetDescriptorFilePath()
        {
            var platform = OverridePlatform ?? Application.platform;

            // On Android, Application.dataPath points inside the read-only, system-owned APK
            // install directory (e.g. ".../base.apk") — the app cannot write there. persistentDataPath
            // is the one location Unity guarantees is writable on Android (UUM-151968).
            var workingDir = platform == RuntimePlatform.Android
                ? Application.persistentDataPath
                : new FileInfo($"{Application.dataPath}/..").FullName;

            return Path.Combine(workingDir, RuntimeDescriptorFileName);
        }
    }
}