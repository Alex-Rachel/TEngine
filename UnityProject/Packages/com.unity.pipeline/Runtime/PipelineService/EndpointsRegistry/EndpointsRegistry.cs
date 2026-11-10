using System;
using System.Collections.Generic;
using UnityEngine;

namespace Unity.Pipeline
{
    /// <summary>In-memory <see cref="IEndpointRegistrationService"/>.</summary>
    class EndpointsRegistry : IEndpointRegistrationService
    {
        /// <inheritdoc/>
        public event Action OnChange
        {
            add { lock (m_Lock) m_OnChange += value; }
            remove { lock (m_Lock) m_OnChange -= value; }
        }

        /// <inheritdoc/>
        public IReadOnlyList<PipelineServiceEndpoint> GetEndpoints()
        {
            lock (m_Lock)
                return m_Endpoints.ToArray();
        }

        /// <inheritdoc/>
        public void RegisterEndpoint(PipelineServiceEndpoint endpoint)
        {
            if (endpoint == null)
                throw new ArgumentNullException(nameof(endpoint));

            lock (m_Lock)
            {
                if (m_Endpoints.Contains(endpoint))
                    throw new InvalidOperationException("This endpoint is already registered.");
                m_Endpoints.Add(endpoint);
            }
            RaiseOnChange();
        }

        /// <inheritdoc/>
        public void UnregisterEndpoint(PipelineServiceEndpoint endpoint)
        {
            if (endpoint == null)
                throw new ArgumentNullException(nameof(endpoint));

            lock (m_Lock)
            {
                if (!m_Endpoints.Remove(endpoint))
                    return;
            }
            RaiseOnChange();
        }

        #region Implementation details

        readonly object m_Lock = new object();

        // Compared by reference: PipelineServiceEndpoint doesn't override Equals.
        readonly List<PipelineServiceEndpoint> m_Endpoints = new List<PipelineServiceEndpoint>();

        Action m_OnChange;

        // Outside m_Lock, so handlers may call back in; one handler throwing doesn't stop the others.
        void RaiseOnChange()
        {
            Action handlers;
            lock (m_Lock)
                handlers = m_OnChange;
            if (handlers == null)
                return;

            foreach (Action handler in handlers.GetInvocationList())
            {
                try
                {
                    handler();
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"Pipeline: An endpoints registry change handler threw: {ex.Message}");
                }
            }
        }

        #endregion
    }
}
