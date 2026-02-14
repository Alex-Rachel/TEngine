using System;
using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;

namespace Fantasy.Helper
{
    /// <summary>
    /// WebSocket帮助类
    /// </summary>
    public static partial class WebSocketHelper
    {
        /// <summary>
        /// 根据字符串获取WebSocket的连接地址
        /// </summary>
        /// <param name="address">目标服务器地址格式为:127.0.0.1:2000 或 [::1]:2000</param>
        /// <param name="isHttps">目标服务器是否为加密连接也就是https</param>
        /// <returns></returns>
        /// <exception cref="FormatException"></exception>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static string GetWebSocketAddress(string address, bool isHttps)
        {
            if (string.IsNullOrEmpty(address))
            {
                throw new FormatException("Address cannot be null or empty");
            }

            ReadOnlySpan<char> span = address.AsSpan();
            ReadOnlySpan<char> ipSpan;
            ReadOnlySpan<char> portSpan;
            bool isIPv6 = false;
            
            // 检查是否是IPv6格式 [host]:port
            if (span[0] == '[')
            {
                var endBracketIndex = span.IndexOf(']');
                if (endBracketIndex == -1)
                {
                    throw new FormatException("Invalid IPv6 format: missing closing bracket");
                }
                
                ipSpan = span.Slice(1, endBracketIndex - 1);
                
                if (endBracketIndex + 1 >= span.Length || span[endBracketIndex + 1] != ':')
                {
                    throw new FormatException("Invalid format: expected ':' after IPv6 address");
                }
                
                portSpan = span.Slice(endBracketIndex + 2);
                isIPv6 = true;
            }
            else
            {
                // IPv4格式或不带方括号的地址
                var lastColonIndex = span.LastIndexOf(':');
                if (lastColonIndex == -1)
                {
                    throw new FormatException("Invalid format: missing port separator ':'");
                }
                
                ipSpan = span.Slice(0, lastColonIndex);
                portSpan = span.Slice(lastColonIndex + 1);
            }
            
            if (!int.TryParse(portSpan, out var port) || port < 0 || port > 65535)
            {
                throw new FormatException("Invalid port number");
            }
            
            // WebSocket URI中IPv6地址需要方括号(RFC 3986)
            var protocol = isHttps ? "wss://" : "ws://";
            
            // 如果不是IPv6或者已经检测到是IPv6，需要进一步确认
            if (!isIPv6 && IPAddress.TryParse(ipSpan, out var ip))
            {
                isIPv6 = ip.AddressFamily == AddressFamily.InterNetworkV6;
            }
            
            if (isIPv6)
            {
                // IPv6格式: ws://[::1]:8080 或 wss://[::1]:8080
                // 将ipSpan转为string再拼接（Span不能作为泛型参数）
                return $"{protocol}[{ipSpan.ToString()}]:{portSpan.ToString()}";
            }
            
            // IPv4格式: ws://192.168.1.1:8080
            return $"{protocol}{ipSpan.ToString()}:{portSpan.ToString()}";
        }
    }
}
