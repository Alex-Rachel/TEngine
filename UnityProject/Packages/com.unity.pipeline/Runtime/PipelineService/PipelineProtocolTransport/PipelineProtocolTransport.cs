using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Unity.Pipeline.Commands;
using UnityEngine;

namespace Unity.Pipeline
{
    /// <summary>
    /// The Pipeline protocol transport: listens on a loopback TCP port of its own and serves each
    /// connection on its own, in the Pipeline protocol (see <see cref="PipelineProtocolSession"/>),
    /// on background threads, never the main thread. Registers a <c>pipeline</c> endpoint with its
    /// port while started. Commands still run one at a time: the executor queues them.
    /// </summary>
    /// <remarks>
    /// Independent of the engine's native endpoint, which speaks the same protocol on a local IPC
    /// endpoint of its own; the two may run at once. Thread-safe.
    /// </remarks>
    sealed class PipelineProtocolTransport
    {
        /// <summary>The connection type of the endpoint it advertises.</summary>
        public const string ConnectionType = "pipeline";

        /// <summary>How many connections may wait to be accepted.</summary>
        const int Backlog = 16;

        readonly object m_Lock = new object();
        readonly ICommandExecutor m_Executor;
        readonly Func<string> m_GetToken;
        readonly IReadOnlyDictionary<string, string> m_ObsoleteCommandNames;
        readonly bool m_AdvertisesEndpoint;

        Socket m_Listener;
        CancellationTokenSource m_Stopping;
        PipelineServiceEndpoint m_Endpoint;

        // Unregister from the same registry, even if PipelineEndpoints has released it since.
        IEndpointRegistrationService m_EndpointRegistry;

        /// <param name="executor">Runs the commands its clients request.</param>
        /// <param name="getToken">The token clients must send; called once per connection, on its thread.</param>
        /// <param name="advertisesEndpoint">False for a test transport, so it doesn't show up beside the live one.</param>
        /// <param name="obsoleteCommandNames">
        /// Old command name to the full name that replaced it, named when a request uses one, as
        /// the HTTP server's are; null for none.
        /// </param>
        public PipelineProtocolTransport(ICommandExecutor executor, Func<string> getToken, bool advertisesEndpoint = true,
            IReadOnlyDictionary<string, string> obsoleteCommandNames = null)
        {
            m_Executor = executor ?? throw new ArgumentNullException(nameof(executor));
            m_GetToken = getToken ?? throw new ArgumentNullException(nameof(getToken));
            m_ObsoleteCommandNames = obsoleteCommandNames;
            m_AdvertisesEndpoint = advertisesEndpoint;
        }

        /// <summary>Whether it accepts connections.</summary>
        public bool IsRunning
        {
            get
            {
                lock (m_Lock)
                    return m_Listener != null;
            }
        }

        /// <summary>The port it accepts connections on; 0 when stopped.</summary>
        public int Port
        {
            get
            {
                lock (m_Lock)
                    return m_Listener?.LocalEndPoint is IPEndPoint endPoint ? endPoint.Port : 0;
            }
        }

        /// <summary>Opens a listening TCP socket on the IPv4 loopback, and only there.</summary>
        static Socket OpenLoopbackListener(int port)
        {
            var listener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            try
            {
                listener.ExclusiveAddressUse = true;
                listener.Bind(new IPEndPoint(IPAddress.Loopback, port));
                listener.Listen(Backlog);
                return listener;
            }
            catch
            {
                listener.Close();
                throw;
            }
        }

        /// <summary>
        /// Starts listening on the loopback and accepting connections, and advertises its endpoint.
        /// </summary>
        /// <param name="port">The port, or 0 for any free one.</param>
        /// <exception cref="InvalidOperationException">It already runs.</exception>
        /// <exception cref="SocketException">The port is taken.</exception>
        public void Start(int port = 0)
        {
            CancellationToken stopping;
            Socket listener;
            int boundPort;
            lock (m_Lock)
            {
                if (m_Listener != null)
                    throw new InvalidOperationException("The Pipeline protocol transport already runs.");

                listener = OpenLoopbackListener(port);
                // Read here: a Stop may close the listener as soon as the lock is released.
                boundPort = ((IPEndPoint)listener.LocalEndPoint).Port;
                m_Listener = listener;
                m_Stopping = new CancellationTokenSource();
                stopping = m_Stopping.Token;
            }

            if (m_AdvertisesEndpoint)
            {
                IEndpointRegistrationService registry;
                PipelineServiceEndpoint endpoint;
                try
                {
                    registry = PipelineEndpoints.GetRegistrationService();
                    endpoint = new PipelineServiceEndpoint(ConnectionType, new Dictionary<string, string>
                    {
                        ["port"] = boundPort.ToString(CultureInfo.InvariantCulture)
                    });
                    registry.RegisterEndpoint(endpoint);
                }
                catch
                {
                    // Only this listener: a Stop and another Start may have run since.
                    Stop(listener);
                    throw;
                }

                bool stillRunning;
                lock (m_Lock)
                {
                    stillRunning = m_Listener == listener;
                    if (stillRunning)
                    {
                        m_EndpointRegistry = registry;
                        m_Endpoint = endpoint;
                    }
                }

                // A Stop ran while it registered, and found no endpoint to withdraw.
                if (!stillRunning)
                {
                    registry.UnregisterEndpoint(endpoint);
                    return;
                }
            }

            _ = Task.Run(() => AcceptAsync(listener, stopping));
        }

        /// <summary>
        /// Stops accepting connections, closes the listening socket, withdraws its endpoint, and
        /// closes every connection it serves, which cancels their commands. Does nothing when stopped.
        /// </summary>
        /// <remarks>
        /// Doesn't wait for its sessions to end: their commands finish cancelling on the executor
        /// after it returns.
        /// </remarks>
        public void Stop() => Stop(null);

        /// <param name="only">Stops only while it runs this listener; null for any.</param>
        void Stop(Socket only)
        {
            Socket listener;
            CancellationTokenSource stopping;
            IEndpointRegistrationService registry;
            PipelineServiceEndpoint endpoint;
            lock (m_Lock)
            {
                if (m_Listener == null || (only != null && m_Listener != only))
                    return;

                listener = m_Listener;
                stopping = m_Stopping;
                registry = m_EndpointRegistry;
                endpoint = m_Endpoint;
                m_Listener = null;
                m_Stopping = null;
                m_EndpointRegistry = null;
                m_Endpoint = null;
            }

            registry?.UnregisterEndpoint(endpoint);
            // Cancelled before the socket closes, so the accept loop reads the close as a stop.
            // Never disposed: sessions may still register on its token, and it has no timer.
            stopping.Cancel();
            listener.Close();
        }

        async Task AcceptAsync(Socket listener, CancellationToken stopping)
        {
            var failures = 0;
            while (!stopping.IsCancellationRequested)
            {
                Socket connection;
                try
                {
                    connection = await listener.AcceptAsync();
                    failures = 0;
                }
                catch (Exception ex) when (stopping.IsCancellationRequested || ex is ObjectDisposedException)
                {
                    // Stopped: the listening socket closed under the accept.
                    return;
                }
                catch (Exception ex)
                {
                    // One connection failed before it was accepted, such as a client that gave up.
                    // Any other failure is retried too, rather than ending the loop: the endpoint
                    // stays advertised while it runs, so a loop that ended would leave clients
                    // connecting to a port nothing accepts on. This is why it has no watchdog.
                    Debug.LogWarning($"Pipeline: The Pipeline protocol transport failed to accept a connection: {ex.Message}");

                    // Backs off so a failure that persists, such as running out of file
                    // descriptors, doesn't spin the loop.
                    failures++;
                    try
                    {
                        await Task.Delay(AcceptRetryDelay(failures), stopping);
                    }
                    catch (OperationCanceledException)
                    {
                        return;
                    }
                    continue;
                }

                try
                {
                    connection.NoDelay = true;
                }
                catch (Exception ex) when (ex is SocketException || ex is ObjectDisposedException)
                {
                    // The client reset the connection before it was served; macOS fails the option then.
                    connection.Close();
                    continue;
                }

                _ = PipelineProtocolSession.ServeAsync(connection, m_Executor, m_GetToken, m_ObsoleteCommandNames, stopping);
            }
        }

        /// <summary>How long to wait before the next accept after <paramref name="failures"/> failures in a row.</summary>
        static TimeSpan AcceptRetryDelay(int failures)
        {
            // 50 ms, doubling, up to a second.
            return TimeSpan.FromMilliseconds(Math.Min(50 << Math.Min(failures - 1, 5), 1000));
        }
    }
}
