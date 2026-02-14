using System;
using System.Net;
using Fantasy.Entitas;
using Fantasy.Helper;
using Fantasy.Network.Interface;
#if !FANTASY_WEBGL
using Fantasy.Network.TCP;
using Fantasy.Network.KCP;
#endif
#if FANTASY_NET
using Fantasy.Network.HTTP;
#endif
using Fantasy.Network.WebSocket;

#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member

namespace Fantasy.Network
{
    internal static class NetworkProtocolFactory
    {
#if FANTASY_NET
        public static ANetwork CreateServer(Scene scene, NetworkProtocolType protocolType, NetworkTarget networkTarget, string bindIp, int port)
        {
            switch (protocolType)
            {
                case NetworkProtocolType.TCP:
                {
                    var network = Entity.Create<TCPServerNetwork>(scene, false, false);
                    var address = NetworkHelper.ToIPEndPoint(bindIp, port);
                    network.Initialize(networkTarget, address);
                    return network;
                }
                case NetworkProtocolType.KCP:
                {
                    var network = Entity.Create<KCPServerNetwork>(scene, false, true);
                    var address = NetworkHelper.ToIPEndPoint(bindIp, port);
                    network.Initialize(networkTarget, address);
                    return network;
                }
                case NetworkProtocolType.WebSocket:
                {
                    var network = Entity.Create<WebSocketServerNetwork>(scene, false, true);
                    network.Initialize(networkTarget, port);
                    return network;
                }
                case NetworkProtocolType.HTTP:
                {
                    var network = Entity.Create<HTTPServerNetwork>(scene, false, true);
                    network.Initialize(networkTarget, bindIp, port);
                    return network;
                }
                default:
                {
                    throw new NotSupportedException($"Unsupported NetworkProtocolType:{protocolType}");
                }
            }
        }
#endif
        public static AClientNetwork CreateClient(Scene scene, NetworkProtocolType protocolType, NetworkTarget networkTarget)
        {
#if !FANTASY_WEBGL
            switch (protocolType)
            {
                case NetworkProtocolType.TCP:
                {
                    var network = Entity.Create<TCPClientNetwork>(scene, false, false);
                    network.Initialize(networkTarget);
                    return network;
                }
                case NetworkProtocolType.KCP:
                {
                    var network = Entity.Create<KCPClientNetwork>(scene, false, true);
                    network.Initialize(networkTarget);
                    return network;
                }
                case NetworkProtocolType.WebSocket:
                {
                    var network = Entity.Create<WebSocketClientNetwork>(scene, false, true);
                    network.Initialize(networkTarget);
                    return network;
                }
                default:
                {
                    throw new NotSupportedException($"Unsupported NetworkProtocolType:{protocolType}");
                }
            }
#else
            // Webgl平台只能用这个协议。
            var network = Entity.Create<WebSocketClientNetwork>(scene, false, true);
            network.Initialize(networkTarget);
            return network;
#endif
        }
    }
}