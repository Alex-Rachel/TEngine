using System;
using Newtonsoft.Json.Linq;
using Unity.Pipeline.Models;
using Unity.Pipeline.Security;
using UnityEngine;
#if UNITY_6000_5_OR_NEWER
using Unity.Scripting.LifecycleManagement;
#endif

namespace Unity.Pipeline
{
    /// <summary>
    /// Publishes this instance's descriptor file (Editor or Player) from the endpoints registered in
    /// <see cref="PipelineEndpoints"/>. Self-contained: nothing calls it. The file itself is a
    /// <see cref="LiveDescriptorFile"/>.
    /// </summary>
    /// <remarks>
    /// <b>Lifespan.</b> Publishes from code load to code unload; never in a secondary Editor process
    /// (e.g. an asset import worker). A leftover file is replaced when publishing starts, or deleted if
    /// the process it names is gone.
    ///
    /// <b>Location.</b> <see cref="EditorInstanceDescriptor.GetDescriptorFilePath"/> in the Editor,
    /// <see cref="RuntimeInstanceDescriptor.GetDescriptorFilePath"/> in a Player.
    /// </remarks>
#if UNITY_6000_5_OR_NEWER
    // Stopped explicitly on code unload; an automatic reset would leave the timer running.
    [NoAutoStaticsCleanup]
#endif
    static partial class PipelineDescriptorPublisher
    {
#if UNITY_6000_5_OR_NEWER
        [OnCodeLoaded]
        static void OnCodeLoaded() => Start();

        [OnCodeUnloading]
        static void OnCodeUnloading() => Stop();
#elif UNITY_EDITOR
        [UnityEditor.InitializeOnLoadMethod]
        static void OnEditorLoaded()
        {
            UnityEditor.AssemblyReloadEvents.beforeAssemblyReload += Stop;
            UnityEditor.EditorApplication.quitting += Stop;
            Start();
        }
#else
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void OnPlayerLoaded()
        {
            Application.quitting += Stop;
            Start();
        }
#endif

        static readonly object s_Lock = new object();

        static LiveDescriptorFile s_File;

        static void Start()
        {
            try
            {
#if UNITY_EDITOR
                if (!EditorProcessInfo.IsMainProcess())
                    return;
#endif
                // CreateCurrent warms the eval token on the main thread; the heartbeat reads it off it.
#if UNITY_EDITOR
                var descriptor = EditorInstanceDescriptor.CreateCurrent(port: 0, automated: EditorProcessInfo.IsAutomated(Environment.GetCommandLineArgs()));
                var filePath = EditorInstanceDescriptor.GetDescriptorFilePath(descriptor.ProjectPath);
#else
                var descriptor = RuntimeInstanceDescriptor.CreateCurrent(port: 0, config: null);
                var filePath = RuntimeInstanceDescriptor.GetDescriptorFilePath();
#endif
                var identity = JObject.FromObject(descriptor);

                lock (s_Lock)
                {
                    // Dispose first: the new one writes the same file as soon as it's constructed.
                    s_File?.Dispose();
                    s_File = null;
                    s_File = new LiveDescriptorFile(
                        filePath,
                        identity,
                        PipelineEndpoints.GetRegistrationService(),
                        SecurityTokenManager.GetOrCreateToken,
                        LiveDescriptorFile.DefaultHeartbeatInterval);
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"Pipeline: Failed to start publishing the descriptor: {ex.Message}");
            }
        }

        static void Stop()
        {
            lock (s_Lock)
            {
                s_File?.Dispose();
                s_File = null;
            }
        }
    }
}
