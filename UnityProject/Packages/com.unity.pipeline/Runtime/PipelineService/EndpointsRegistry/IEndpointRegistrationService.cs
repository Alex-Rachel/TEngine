using System;
using System.Collections.Generic;

namespace Unity.Pipeline
{
    /// <summary>
    /// The set of endpoints this instance advertises to clients. Transports register their endpoint
    /// while listening; publishers subscribe to <see cref="OnChange"/> and read <see cref="GetEndpoints"/>.
    /// </summary>
    /// <remarks>
    /// Ordered by registration, which is the clients' order of preference. Thread-safe.
    /// </remarks>
    interface IEndpointRegistrationService
    {
        /// <summary>Raised after each change to the set.</summary>
        /// <remarks>
        /// Raised on the thread that made the change, outside the registry's lock. Other changes may
        /// land concurrently, so handlers read <see cref="GetEndpoints"/> rather than track changes.
        /// Not raised on subscription. Handler exceptions are logged, not propagated.
        /// </remarks>
        public event Action OnChange;

        /// <returns>A snapshot of the registered endpoints, in registration order.</returns>
        public IReadOnlyList<PipelineServiceEndpoint> GetEndpoints();

        /// <summary>Adds an endpoint after those already registered.</summary>
        /// <param name="endpoint">The endpoint; keep it to unregister.</param>
        /// <exception cref="ArgumentNullException"><paramref name="endpoint"/> is null.</exception>
        /// <exception cref="InvalidOperationException"><paramref name="endpoint"/> is already registered.</exception>
        public void RegisterEndpoint(PipelineServiceEndpoint endpoint);

        /// <summary>
        /// Removes an endpoint. Does nothing if it isn't registered.
        /// </summary>
        /// <param name="endpoint">The instance passed to <see cref="RegisterEndpoint"/>.</param>
        /// <exception cref="ArgumentNullException"><paramref name="endpoint"/> is null.</exception>
        public void UnregisterEndpoint(PipelineServiceEndpoint endpoint);
    }
}
