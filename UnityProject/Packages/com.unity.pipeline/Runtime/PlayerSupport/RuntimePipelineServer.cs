using System;
using Unity.Pipeline.Config;
using Unity.Pipeline.Security;
using Unity.Pipeline.Models;
using UnityEngine;

namespace Unity.Pipeline
{
    /// <summary>The Player-side <see cref="BasePipelineServer"/>, owned by a <see cref="RuntimePipelineDriver"/>.</summary>
    public class RuntimePipelineServer : BasePipelineServer
    {
        private RuntimePipelineConfig m_Config;

        /// <summary>Create a server bound to the given configuration.</summary>
        /// <param name="config">The runtime pipeline configuration.</param>
        public RuntimePipelineServer(RuntimePipelineConfig config)
        {
            m_Config = config;
        }

        /// <summary>Runtime servers use ports 7900-7949 (test runtime servers use 7950-7999).</summary>
        /// <returns>The inclusive port range to try when binding the listener.</returns>
        protected override (int basePort, int maxPort) GetPortRange()
        {
            return (7900, 7949); // Runtime production (test runtime servers use 7950-7999)
        }

        /// <summary>Build the Player-specific status/heartbeat payload for /api/status.</summary>
        /// <returns>The status payload.</returns>
        protected override object GetServerStatus()
        {
            return new
            {
                status = IsEndpointRegistered ? "ready" : "error",
                // The response time; see StatusResponse.LastHeartbeat.
                lastHeartbeat = DateTime.UtcNow,
                capabilities = Capabilities
            };
        }

        /// <summary>The bearer token clients must present to authenticate requests.</summary>
        /// <returns>The current token.</returns>
        protected override string GetToken()
        {
            return SecurityTokenManager.GetOrCreateToken();
        }
    }
}
