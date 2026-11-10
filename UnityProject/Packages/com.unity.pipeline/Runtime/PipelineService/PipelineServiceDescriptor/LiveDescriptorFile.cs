using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Unity.Pipeline
{
    /// <summary>
    /// Keeps a descriptor file in line with an endpoints registry, from construction until
    /// <see cref="Dispose"/>.
    /// </summary>
    /// <remarks>
    /// <b>Lifespan.</b> The file exists only while an endpoint is registered: it is rewritten on every
    /// registry change and heartbeat, and deleted when the set empties and on <see cref="Dispose"/>.
    /// Deleting leaves alone a file whose <c>pid</c> is another process that is still running: two
    /// Players started from the same folder share one path, and the one without a server must not
    /// withdraw the other's.
    ///
    /// <b>Content.</b> The host's identity fields, plus:
    /// <code>
    /// {
    ///   ...identity fields...,
    ///   "port": 52814,
    ///   "lastHeartbeat": "2026-10-02T10:05:00Z",
    ///   "evalToken": "...",
    ///   "endpoints": [
    ///     { "type": "http", "port": "52814" }
    ///   ]
    /// }
    /// </code>
    /// <c>endpoints</c> lists the registry in order, each entry's fields beside its <c>type</c>; clients
    /// use the first type they support. <c>port</c> is the first <c>http</c> endpoint's, for clients
    /// that predate <c>endpoints</c>.
    ///
    /// <b>Heartbeat.</b> <c>lastHeartbeat</c> and <c>evalToken</c> are refreshed on a background timer.
    /// Clients treat a heartbeat older than 60 s as an instance that is gone.
    ///
    /// <b>Writes.</b> Serialized, in place, through <see cref="SecureFile"/>. Failures are logged, never thrown.
    /// </remarks>
    class LiveDescriptorFile : IDisposable
    {
        /// <summary>A quarter of the clients' 60 s staleness threshold.</summary>
        public static readonly TimeSpan DefaultHeartbeatInterval = TimeSpan.FromSeconds(15);

        /// <summary>
        /// Starts publishing, replacing any file already at <paramref name="filePath"/>, or deleting it
        /// if nothing is registered and the process it names is gone.
        /// </summary>
        /// <param name="filePath">Where to write the descriptor.</param>
        /// <param name="identity">The host's fixed fields, copied.</param>
        /// <param name="registry">The endpoints to publish. Not owned.</param>
        /// <param name="getEvalToken">The current eval token. Called from any thread.</param>
        /// <param name="heartbeatInterval">How often to refresh the heartbeat.</param>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        /// <exception cref="ArgumentException"><paramref name="filePath"/> is empty or whitespace.</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="heartbeatInterval"/> is zero or negative.</exception>
        public LiveDescriptorFile(
            string filePath,
            JObject identity,
            IEndpointRegistrationService registry,
            Func<string> getEvalToken,
            TimeSpan heartbeatInterval)
            : this(filePath, identity, registry, getEvalToken, heartbeatInterval, IsProcessRunning)
        {
        }

        /// <summary>As the public constructor, with the process liveness check replaced, for tests.</summary>
        /// <param name="isProcessRunning">Whether the process with the given ID is running.</param>
        internal LiveDescriptorFile(
            string filePath,
            JObject identity,
            IEndpointRegistrationService registry,
            Func<string> getEvalToken,
            TimeSpan heartbeatInterval,
            Func<int, bool> isProcessRunning)
        {
            if (filePath == null)
                throw new ArgumentNullException(nameof(filePath));
            if (string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentException("Descriptor file path must not be empty or whitespace.", nameof(filePath));
            if (heartbeatInterval <= TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(heartbeatInterval), heartbeatInterval, "Heartbeat interval must be positive.");

            m_FilePath = filePath;
            m_Identity = (JObject)(identity ?? throw new ArgumentNullException(nameof(identity))).DeepClone();
            m_Registry = registry ?? throw new ArgumentNullException(nameof(registry));
            m_GetEvalToken = getEvalToken ?? throw new ArgumentNullException(nameof(getEvalToken));
            m_IsProcessRunning = isProcessRunning ?? throw new ArgumentNullException(nameof(isProcessRunning));

            // Subscribe before the first read, so no change is missed in between.
            m_Registry.OnChange += OnEndpointsChanged;
            OnEndpointsChanged();

            m_HeartbeatTimer = new Timer(_ => OnHeartbeat(), null, heartbeatInterval, heartbeatInterval);
        }

        /// <summary>Stops publishing and deletes the file, unless another running process has taken it over. Nothing writes it afterwards.</summary>
        public void Dispose()
        {
            lock (m_Lock)
            {
                if (m_Disposed)
                    return;
                m_Disposed = true;
                DeleteUnlessLive();
            }

            m_Registry.OnChange -= OnEndpointsChanged;
            m_HeartbeatTimer.Dispose();
        }

        /// <summary>The descriptor's JSON. Pure, so the format can be tested on its own.</summary>
        /// <param name="identity">The host's fixed fields. Not modified.</param>
        /// <param name="endpoints">The registered endpoints, in order.</param>
        /// <param name="evalToken">The current eval token.</param>
        /// <param name="now">The heartbeat time (UTC).</param>
        /// <returns>A new object.</returns>
        internal static JObject BuildDescriptor(
            JObject identity,
            IReadOnlyList<PipelineServiceEndpoint> endpoints,
            string evalToken,
            DateTime now)
        {
            var descriptor = (JObject)identity.DeepClone();
            descriptor["port"] = GetLegacyPort(endpoints);
            descriptor["lastHeartbeat"] = now;
            descriptor["evalToken"] = evalToken;

            var entries = new JArray();
            foreach (var endpoint in endpoints)
            {
                var entry = new JObject { ["type"] = endpoint.GetConnectionType() };
                foreach (var field in endpoint.GetFields())
                    entry[field.Key] = field.Value;
                entries.Add(entry);
            }
            descriptor["endpoints"] = entries;
            return descriptor;
        }

        #region Implementation details

        readonly string m_FilePath;
        readonly JObject m_Identity;
        readonly IEndpointRegistrationService m_Registry;
        readonly Func<string> m_GetEvalToken;
        readonly Func<int, bool> m_IsProcessRunning;
        readonly Timer m_HeartbeatTimer;

        // Serializes all state changes and file operations.
        readonly object m_Lock = new object();

        IReadOnlyList<PipelineServiceEndpoint> m_Endpoints = Array.Empty<PipelineServiceEndpoint>();

        bool m_Disposed;

        void OnEndpointsChanged()
        {
            lock (m_Lock)
            {
                if (m_Disposed)
                    return;

                // Read under the lock, so the last write has the latest set.
                m_Endpoints = m_Registry.GetEndpoints();
                if (m_Endpoints.Count == 0)
                    DeleteUnlessLive();
                else
                    WriteFile();
            }
        }

        void OnHeartbeat()
        {
            lock (m_Lock)
            {
                if (m_Disposed || m_Endpoints.Count == 0)
                    return;
                WriteFile();
            }
        }

        void WriteFile()
        {
            try
            {
                var created = !File.Exists(m_FilePath);
                var descriptor = BuildDescriptor(m_Identity, m_Endpoints, m_GetEvalToken(), DateTime.UtcNow);
                SecureFile.WriteAllText(m_FilePath, descriptor.ToString(Formatting.Indented));

                // connectivity.md points Player users to this line.
                if (created)
                    System.Console.WriteLine($"Pipeline: Descriptor written to {m_FilePath}");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"Pipeline: Failed to write descriptor '{m_FilePath}': {ex.Message}");
            }
        }

        // Deletes the file unless it names another process that is still running. A file whose pid
        // can't be read is left alone too: it may be another instance's, caught mid-write.
        void DeleteUnlessLive()
        {
            int pid;
            try
            {
                if (!File.Exists(m_FilePath))
                    return;
                var filePid = JObject.Parse(File.ReadAllText(m_FilePath))["pid"];
                if (filePid == null || filePid.Type != JTokenType.Integer)
                    return;
                pid = (int)filePid;
            }
            catch (Exception)
            {
                return;
            }

            var ownPid = m_Identity["pid"];
            var isOwn = ownPid != null && ownPid.Type == JTokenType.Integer && (int)ownPid == pid;
            if (isOwn || !m_IsProcessRunning(pid))
                SecureFile.TryDelete(m_FilePath);
        }

        // A process this one can't inspect counts as running, so its file is never deleted.
        static bool IsProcessRunning(int pid)
        {
            try
            {
                using (Process.GetProcessById(pid))
                    return true;
            }
            catch (ArgumentException)
            {
                return false;
            }
            catch (Exception)
            {
                return true;
            }
        }

        // For clients that predate "endpoints": the first http endpoint's port, or 0.
        static int GetLegacyPort(IReadOnlyList<PipelineServiceEndpoint> endpoints)
        {
            foreach (var endpoint in endpoints)
            {
                if (endpoint.GetConnectionType() != "http")
                    continue;
                return endpoint.GetFields().TryGetValue("port", out var value) && int.TryParse(value, out var port)
                    ? port
                    : 0;
            }
            return 0;
        }

        #endregion
    }
}
