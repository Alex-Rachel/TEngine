#if !FANTASY_WEBGL
using System;
using KCP;

#pragma warning disable CS1591
namespace Fantasy.Network.KCP
{
    public class KCPSettings
    {
        public int Mtu { get; private set; }
        public int SendWindowSize { get; private set; }
        public int ReceiveWindowSize { get; private set; }
        public int MaxSendWindowSize { get; private set; }

        public static KCPSettings Create(NetworkTarget networkTarget)
        {
            var settings = new KCPSettings();
            
            switch (networkTarget)
            {
                case NetworkTarget.Outer:
                {
                    // 外网设置470的原因:
                    // 1、mtu设置过大有可能路由器过滤掉
                    // 2、降低 mtu 到 470，同样数据虽然会发更多的包，但是小包在路由层优先级更高
                    settings.Mtu = 470;
#if FANTASY_NET
                    settings.SendWindowSize = 8192;
                    settings.ReceiveWindowSize = 8192;
                    settings.MaxSendWindowSize = 8192 * 8192 * 7;
#endif
#if FANTASY_UNITY || FANTASY_CONSOLE
                    settings.SendWindowSize = 2048;
                    settings.ReceiveWindowSize = 2048;
                    settings.MaxSendWindowSize = 2048 * 2048 * 7;
#endif
                    
                    break;
                }
#if FANTASY_NET
                 case NetworkTarget.Inner:
                {
                    // 内网设置1400的原因
                    // 1、一般都是同一台服务器来运行多个进程来处理
                    // 2、内网每个进程跟其他进程只有一个通道进行发送、所以发送的数量会比较大
                    // 3、如果不把窗口设置大点、会出现消息滞后。
                    // 4、因为内网发送的可不只是外网转发数据、还有可能是其他进程的通讯
                    settings.Mtu = 1200;
                    settings.SendWindowSize = 8192;
                    settings.ReceiveWindowSize = 8192;
                    settings.MaxSendWindowSize = 8192 * 8192 * 7;
                    break;
                }   
#endif
                default:
                {
                    throw new NotSupportedException($"KCPServerNetwork NotSupported NetworkType:{networkTarget}");
                }
            }

            return settings;
        }
    }

    internal static class KCPFactory
    {
        public const int FANTASY_KCP_RESERVED_HEAD = 5;

        public static Kcp Create(NetworkTarget networkTarget, uint conv, KcpCallback output, out KCPSettings kcpSettings)
        {
            var kcp = new Kcp(conv, output, FANTASY_KCP_RESERVED_HEAD);
            kcpSettings = KCPSettings.Create(networkTarget);
            kcp.SetNoDelay(1, 5, 2, 1);
            kcp.SetWindowSize(kcpSettings.SendWindowSize, kcpSettings.ReceiveWindowSize);
            kcp.SetMtu(kcpSettings.Mtu);
            kcp.SetMinrto(30);
            return kcp;
        }

        public static Kcp Create(KCPSettings kcpSettings, uint conv, KcpCallback output)
        {
            var kcp = new Kcp(conv, output, FANTASY_KCP_RESERVED_HEAD);
            kcp.SetNoDelay(1, 5, 2, 1);
            kcp.SetWindowSize(kcpSettings.SendWindowSize, kcpSettings.ReceiveWindowSize);
            kcp.SetMtu(kcpSettings.Mtu);
            kcp.SetMinrto(30);
            return kcp;
        }
    }
}
#endif