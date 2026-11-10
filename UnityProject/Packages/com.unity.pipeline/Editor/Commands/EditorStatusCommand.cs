using System;
using System.IO;
using Unity.Pipeline.Commands;
using Unity.Pipeline.Models;
using UnityEditor;
using UnityEngine;

namespace Unity.Pipeline.Editor.Commands
{
    /// <summary>
    /// Command to get detailed Editor status information.
    /// Returns rich Editor state including compilation, play mode, and project details.
    /// Accessible via "unity request editor_status" and "/api/editor_status" endpoint.
    /// </summary>
    static class EditorStatusCommand
    {
        const string Group = "editor";

        [CliGroupCommand(Group, "Get detailed Unity Editor status and state information", Tags = new[] { "editor" })]
        [ImmediateCommand]
        public static StatusResponse Status()
        {
            // On the main thread here, so take a fresh sample rather than one up to an update old.
            EditorPipelineServer.SamplePlayerLoop();
            var playerLoop = EditorPipelineServer.ReadPlayerLoop();

            return new StatusResponse
            {
                Status = GetOverallStatus(),
                Compiling = EditorApplication.isCompiling,
                DomainReloadInProgress = EditorApplication.isUpdating,
                CodeLoadCount = PipelineServerStartup.CodeLoadCount,
                AutoRefresh = GetAutoRefreshMode(),
                PlayMode = GetPlayModeStatus(),
                FrameCount = playerLoop.Playing ? playerLoop.FrameCount : (int?)null,
                PlayerLoopTicking = playerLoop.Playing ? playerLoop.Ticking : (bool?)null,
                RunInBackground = Application.runInBackground,
                LastHeartbeat = DateTime.UtcNow,
                ProjectPath = Path.GetDirectoryName(Application.dataPath),
                UnityVersion = Application.unityVersion,
                Role = MppmInstanceInfo.Role,
                InstanceName = MppmInstanceInfo.InstanceName
            };
        }

        /// <summary>
        /// Get overall Editor status based on current state.
        /// </summary>
        private static string GetOverallStatus()
        {
            if (EditorApplication.isCompiling)
                return "compiling";

            if (EditorApplication.isUpdating)
                return "reloading";

            if (EditorApplication.isPlaying || EditorApplication.isPaused)
                return "playing";

            return "ready";
        }

        /// <summary>
        /// Get the Auto Refresh preference (Preferences > Asset Pipeline). Reads the live value, so
        /// it reflects the method reload watch turning Auto Refresh off while it runs.
        /// </summary>
        internal static string GetAutoRefreshMode()
        {
            switch (EditorPrefs.GetInt(EditorMethodReloadWatcher.AutoRefreshModePref, 1))
            {
                case 0:
                    return "disabled";
                case 2:
                    return "enabled_outside_play_mode";
                default:
                    return "enabled";
            }
        }

        /// <summary>
        /// Get current play mode status.
        /// </summary>
        private static string GetPlayModeStatus()
        {
            if (EditorApplication.isPaused)
                return "paused";

            if (EditorApplication.isPlaying)
                return "playing";

            return "stopped";
        }
    }
}