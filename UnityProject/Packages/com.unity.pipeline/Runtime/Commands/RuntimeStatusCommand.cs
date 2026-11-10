using System;
using System.Collections.Generic;
using Unity.Pipeline;
using Unity.Pipeline.Commands;
using Unity.Pipeline.Runtime.Telemetry;
using UnityEngine;
using UnityEngine.Profiling;

namespace Unity.Pipeline.Runtime.Commands
{
    /// <summary>
    /// Runtime status information for Unity Player builds.
    /// Provides Player-appropriate status without Editor-specific APIs.
    /// </summary>
    static class RuntimeStatusCommand
    {
        const string Group = "runtime";

        [CliGroupCommand(Group, "Get comprehensive runtime application status", MainThreadRequired = true, Tags = new[] { "runtime" })]
        public static RuntimeStatusResponse Status()
        {
            try
            {
                return new RuntimeStatusResponse
                {
                    // Application info
                    UnityVersion = Application.unityVersion,
                    Platform = Application.platform.ToString(),
                    BuildGuid = Application.buildGUID,
                    Version = Application.version,

                    // Runtime state
                    IsPlaying = Application.isPlaying,
                    TargetFrameRate = Application.targetFrameRate,
                    TimeScale = Time.timeScale,
                    RealTimeSinceStartup = Time.realtimeSinceStartup,

                    // Performance info
                    FrameCount = Time.frameCount,
                    LoadedLevelName = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name,
                    LoadedLevelBuildIndex = UnityEngine.SceneManagement.SceneManager.GetActiveScene().buildIndex,

                    // Live performance telemetry (fps, frame time, memory, profiler counters)
                    Performance = BuildPerformanceTelemetry(),

                    // The driver's actual state and in-effect config — not just what Project
                    // Settings currently says, which can differ (live JSON in the Editor vs. the
                    // snapshot frozen into the Player at build time).
                    PipelineDriver = BuildDriverStatus(),

                    // Timestamp
                    Timestamp = DateTime.UtcNow
                };
            }
            catch (Exception ex)
            {
                Debug.LogError($"Pipeline: Runtime status command failed: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Null when no driver was ever bootstrapped (e.g. the Pipeline is not built into this
        /// build), otherwise the driver's actual running state and the config values presently
        /// governing it.
        /// maxWorkItemsPerFrame is read via CurrentMaxWorkItemsPerFrame rather than the config
        /// directly, since that field alone can be tuned live after the driver started.
        /// </summary>
        private static RuntimePipelineDriverStatus BuildDriverStatus()
        {
            var driver = RuntimePipelineBootstrap.Instance;
            if (driver == null)
                return null;

            var config = driver.Config;
            return new RuntimePipelineDriverStatus
            {
                IsServerRunning = driver.IsServerRunning,
                ActualPort = driver.ActualPort,
                EnableAuditLogging = config != null && config.enableAuditLogging,
                AutoStart = config != null && config.autoStart,
                MaxWorkItemsPerFrame = driver.CurrentMaxWorkItemsPerFrame
            };
        }

        /// <summary>
        /// Assemble the live performance section from two independent sources:
        /// the per-frame <see cref="FrameStatsSampler"/> (fps / frame time / profiler counters), which only
        /// exists while a <see cref="RuntimePipelineDriver"/> is feeding it, and the Profiler memory APIs,
        /// which can be read on demand at any time. Frame-stat fields stay at their defaults (and
        /// <see cref="RuntimePerformanceTelemetry.FrameStatsAvailable"/> stays false) when no sampler is
        /// running — e.g. in EditMode tests — so a missing sampler degrades gracefully rather than throwing.
        /// </summary>
        private static RuntimePerformanceTelemetry BuildPerformanceTelemetry()
        {
            var perf = new RuntimePerformanceTelemetry();

            var sampler = FrameStatsSampler.Shared;
            if (sampler != null)
            {
                var snap = sampler.GetSnapshot();
                perf.FrameStatsAvailable = snap.Available;
                perf.SampleWindow = snap.SampleWindow;
                perf.Fps = snap.Fps;
                perf.AverageFrameTimeMs = snap.AverageFrameTimeMs;
                perf.MinFrameTimeMs = snap.MinFrameTimeMs;
                perf.MaxFrameTimeMs = snap.MaxFrameTimeMs;
                perf.LastFrameTimeMs = snap.LastFrameTimeMs;
                perf.GpuTimingAvailable = snap.GpuTimingAvailable;
                perf.CpuFrameTimeMs = snap.CpuFrameTimeMs;
                perf.GpuFrameTimeMs = snap.GpuFrameTimeMs;
                perf.Counters = snap.Counters;
            }
            else
            {
                perf.Counters = new Dictionary<string, double>();
            }

            // Memory is sampled directly (no per-frame accumulation needed). These return 0 in a
            // non-development build where the profiler is stripped, which is reported faithfully.
            perf.TotalAllocatedMemory = Profiler.GetTotalAllocatedMemoryLong();
            perf.TotalReservedMemory = Profiler.GetTotalReservedMemoryLong();
            perf.MonoUsedSize = Profiler.GetMonoUsedSizeLong();
            perf.MonoHeapSize = Profiler.GetMonoHeapSizeLong();
            perf.GcMemory = GC.GetTotalMemory(false);

            return perf;
        }
    }

    /// <summary>
    /// Response model for runtime status information.
    /// </summary>
    [Serializable]
    class RuntimeStatusResponse
    {
        // Application Information
        public string UnityVersion { get; set; }
        public string Platform { get; set; }
        public string BuildGuid { get; set; }
        public string Version { get; set; }
        
        // Runtime State
        public bool IsPlaying { get; set; }
        public int TargetFrameRate { get; set; }
        public float TimeScale { get; set; }
        public float RealTimeSinceStartup { get; set; }

        // Performance Information
        public int FrameCount { get; set; }
        public string LoadedLevelName { get; set; }
        public int LoadedLevelBuildIndex { get; set; }

        // Live performance telemetry
        public RuntimePerformanceTelemetry Performance { get; set; }

        // The runtime Pipeline driver's actual state, or null if none was ever bootstrapped.
        public RuntimePipelineDriverStatus PipelineDriver { get; set; }

        // Metadata
        public DateTime Timestamp { get; set; }
    }

    /// <summary>
    /// The runtime Pipeline driver's actual state: whether its server is running, the port it's
    /// actually on, and the config values presently governing it. Distinct from what Project
    /// Settings > Pipeline > Runtime currently shows — that's live-editable in the Editor, while a
    /// running driver is working from whatever it was initialized with (a Player's is additionally
    /// frozen at build time). This is the authoritative "what's actually happening right now" view.
    /// </summary>
    [Serializable]
    class RuntimePipelineDriverStatus
    {
        public bool IsServerRunning { get; set; }
        public int ActualPort { get; set; }
        public bool EnableAuditLogging { get; set; }
        public bool AutoStart { get; set; }

        /// <summary>The value actually governing the dispatcher right now — see RuntimePipelineDriver.CurrentMaxWorkItemsPerFrame.</summary>
        public int MaxWorkItemsPerFrame { get; set; }
    }

    /// <summary>
    /// Live performance telemetry for a running Player: rolling fps / frame-time statistics, optional
    /// CPU/GPU frame timing, memory usage, and a map of profiler counters. Frame-stat and counter fields
    /// are populated from the per-frame <see cref="FrameStatsSampler"/>; memory fields are read on demand.
    /// </summary>
    [Serializable]
    class RuntimePerformanceTelemetry
    {
        // Frame timing — sampled over a rolling window by the RuntimePipelineDriver.
        /// <summary>False when no sampler is running; the fps / frame-time fields below are then meaningless.</summary>
        public bool FrameStatsAvailable { get; set; }
        public int SampleWindow { get; set; }
        public float Fps { get; set; }
        public float AverageFrameTimeMs { get; set; }
        public float MinFrameTimeMs { get; set; }
        public float MaxFrameTimeMs { get; set; }
        public float LastFrameTimeMs { get; set; }

        // CPU/GPU frame timing via FrameTimingManager (not available on every platform).
        public bool GpuTimingAvailable { get; set; }
        public double CpuFrameTimeMs { get; set; }
        public double GpuFrameTimeMs { get; set; }

        // Memory (bytes) via the Profiler API; 0 when the profiler is stripped (non-development build).
        public long TotalAllocatedMemory { get; set; }
        public long TotalReservedMemory { get; set; }
        public long MonoUsedSize { get; set; }
        public long MonoHeapSize { get; set; }
        public long GcMemory { get; set; }

        // Profiler counters by reported name (e.g. DrawCalls, Triangles, GcAllocInFrame).
        public Dictionary<string, double> Counters { get; set; }
    }
}