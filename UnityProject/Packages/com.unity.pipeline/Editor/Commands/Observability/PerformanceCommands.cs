using System;
using Newtonsoft.Json;
using Unity.Pipeline.Commands;
using UnityEditor;
using UnityEngine;
using UnityEngine.Profiling;

namespace Unity.Pipeline.Editor.Commands.Observability
{
    /// <summary>
    /// Render statistics for the most recently rendered Editor frame, read from
    /// <see cref="UnityStats"/>. These reflect the last frame the Editor drew (Game/Scene view), not
    /// an averaged measurement — treat them as a snapshot.
    /// </summary>
    [Serializable]
    class RenderStats
    {
        /// <summary>Draw calls issued for the last rendered frame.</summary>
        [JsonProperty("drawCalls")]
        public int DrawCalls { get; set; }

        /// <summary>Total batches (dynamic + static + instanced) for the last rendered frame.</summary>
        [JsonProperty("batches")]
        public int Batches { get; set; }

        /// <summary>SetPass calls (shader pass switches) for the last rendered frame.</summary>
        [JsonProperty("setPassCalls")]
        public int SetPassCalls { get; set; }

        /// <summary>Triangles submitted for the last rendered frame.</summary>
        [JsonProperty("triangles")]
        public int Triangles { get; set; }

        /// <summary>Vertices submitted for the last rendered frame.</summary>
        [JsonProperty("vertices")]
        public int Vertices { get; set; }
    }

    /// <summary>
    /// Process memory usage read from <see cref="Profiler"/>. Values are in bytes and are always
    /// available at runtime regardless of whether the Profiler window is open.
    /// </summary>
    [Serializable]
    class MemoryStats
    {
        /// <summary>Total memory currently allocated by Unity (bytes).</summary>
        [JsonProperty("totalAllocatedBytes")]
        public long TotalAllocatedBytes { get; set; }

        /// <summary>Total memory reserved by Unity (bytes).</summary>
        [JsonProperty("totalReservedBytes")]
        public long TotalReservedBytes { get; set; }

        /// <summary>Mono managed heap memory in use (bytes).</summary>
        [JsonProperty("monoUsedBytes")]
        public long MonoUsedBytes { get; set; }

        /// <summary>Mono managed heap size reserved (bytes).</summary>
        [JsonProperty("monoHeapBytes")]
        public long MonoHeapBytes { get; set; }
    }

    /// <summary>
    /// Latest CPU/GPU frame timing from <see cref="FrameTimingManager"/>. The Frame Timing Manager
    /// must have a timing captured for <see cref="Available"/> to be true; when unavailable the
    /// timing fields are left at zero.
    /// </summary>
    [Serializable]
    class FrameTimingStats
    {
        /// <summary>True when a frame timing was captured and the milliseconds fields are meaningful.</summary>
        [JsonProperty("available")]
        public bool Available { get; set; }

        /// <summary>Total CPU frame time in milliseconds.</summary>
        [JsonProperty("cpuFrameTimeMs")]
        public double CpuFrameTimeMs { get; set; }

        /// <summary>Total GPU frame time in milliseconds.</summary>
        [JsonProperty("gpuFrameTimeMs")]
        public double GpuFrameTimeMs { get; set; }

        /// <summary>CPU main-thread frame time in milliseconds.</summary>
        [JsonProperty("cpuMainThreadFrameTimeMs")]
        public double CpuMainThreadFrameTimeMs { get; set; }
    }

    /// <summary>
    /// Composite performance snapshot returned by <c>get_performance_stats</c>: render counters,
    /// process memory, and the latest frame timing.
    /// </summary>
    [Serializable]
    class PerformanceStats
    {
        /// <summary>Render counters for the last rendered Editor frame.</summary>
        [JsonProperty("render")]
        public RenderStats Render { get; set; }

        /// <summary>Process memory usage at capture time.</summary>
        [JsonProperty("memory")]
        public MemoryStats Memory { get; set; }

        /// <summary>Latest CPU/GPU frame timing, if one was captured.</summary>
        [JsonProperty("frameTiming")]
        public FrameTimingStats FrameTiming { get; set; }
    }

    /// <summary>
    /// Read-only telemetry command (CLI-198) that snapshots render, memory, and frame-timing stats so
    /// an agent can reason about Editor performance without opening the Profiler or Stats overlay.
    /// </summary>
    static class PerformanceCommands
    {
        const string Group = "diagnostics";

        [CliGroupCommand(Group, "Read render, memory, and frame-timing stats (structured, read-only).", Tags = new[] { "observability/performance" })]
        public static PerformanceStats GetPerformance()
        {
            return new PerformanceStats
            {
                Render = new RenderStats
                {
                    // UnityStats reflects the last frame rendered by the Editor (Game/Scene view).
                    DrawCalls = UnityStats.drawCalls,
                    Batches = UnityStats.dynamicBatches + UnityStats.staticBatches + UnityStats.instancedBatches,
                    SetPassCalls = UnityStats.setPassCalls,
                    Triangles = UnityStats.triangles,
                    Vertices = UnityStats.vertices
                },
                Memory = new MemoryStats
                {
                    TotalAllocatedBytes = Profiler.GetTotalAllocatedMemoryLong(),
                    TotalReservedBytes = Profiler.GetTotalReservedMemoryLong(),
                    MonoUsedBytes = Profiler.GetMonoUsedSizeLong(),
                    MonoHeapBytes = Profiler.GetMonoHeapSizeLong()
                },
                FrameTiming = CaptureFrameTiming()
            };
        }

        /// <summary>
        /// Capture the latest CPU/GPU frame timing. The Frame Timing Manager only yields data once a
        /// timing has been captured (and platform support exists), so any failure or empty result is
        /// reported as <see cref="FrameTimingStats.Available"/> = false rather than throwing.
        /// </summary>
        private static FrameTimingStats CaptureFrameTiming()
        {
            try
            {
                FrameTimingManager.CaptureFrameTimings();

                var timings = new FrameTiming[1];
                uint received = FrameTimingManager.GetLatestTimings(1, timings);
                if (received > 0)
                {
                    return new FrameTimingStats
                    {
                        Available = true,
                        CpuFrameTimeMs = timings[0].cpuFrameTime,
                        GpuFrameTimeMs = timings[0].gpuFrameTime,
                        CpuMainThreadFrameTimeMs = timings[0].cpuMainThreadFrameTime
                    };
                }
            }
            catch
            {
                // Frame timing is platform-dependent and may be unsupported; report as unavailable.
            }

            return new FrameTimingStats { Available = false };
        }
    }
}
