#if UNITY_6000_5_OR_NEWER
using Unity.Scripting.LifecycleManagement;
#endif

namespace Unity.Pipeline
{
    /// <summary>
    /// Provides a global pipeline endpoints registry.
    /// The service is created at application initialization and released at application closure.
    /// In the Editor, the lifespan of the service matches the lifespan of the application domain,
    /// so it's released before assembly reloads and lazily re-created (empty) after a reload. This means
    /// that endpoints need to be re-registered on code initialization after a domain reload.
    /// </summary>
#if UNITY_6000_5_OR_NEWER
    // Released explicitly on code unload instead.
    [NoAutoStaticsCleanup]
#endif
    static partial class PipelineEndpoints
    {
        /// <summary>The registration service for this instance, created on first use. Thread-safe.</summary>
        /// <remarks>Don't keep it past a domain reload.</remarks>
        /// <returns>The service, never null.</returns>
        static public IEndpointRegistrationService GetRegistrationService() => GetOrCreateService();

        #region Implementation details

        static readonly object s_Lock = new object();

        static IEndpointRegistrationService m_Service;

#if UNITY_6000_5_OR_NEWER
        [OnCodeUnloading]
        static void OnCodeUnloading() => Release();
#elif UNITY_EDITOR
        static bool s_ReleaseHooked;
#endif

        static IEndpointRegistrationService GetOrCreateService()
        {
            lock (s_Lock)
            {
                if (m_Service != null)
                    return m_Service;

                m_Service = new EndpointsRegistry();
#if !UNITY_6000_5_OR_NEWER && UNITY_EDITOR
                // No [OnCodeUnloading] before 6000.5. A Player keeps the service for its whole life.
                if (!s_ReleaseHooked)
                {
                    s_ReleaseHooked = true;
                    UnityEditor.AssemblyReloadEvents.beforeAssemblyReload += Release;
                    UnityEditor.EditorApplication.quitting += Release;
                }
#endif
                return m_Service;
            }
        }

        static void Release()
        {
            lock (s_Lock)
                m_Service = null;
        }

        #endregion
    }
}
