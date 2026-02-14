#if FANTASY_NET
using System;
using Fantasy.Network.Interface;
namespace Fantasy.Network;

internal sealed class ProcessSessionInfo(Session session, AClientNetwork aClientNetwork) : IDisposable
{
    public readonly Session Session = session;
    public readonly AClientNetwork AClientNetwork = aClientNetwork;

    public void Dispose()
    {
        Session.Dispose();
        AClientNetwork?.Dispose();
    }
}
#endif
