using System;
using System.IO;
using Fantasy.Async;
using Fantasy.Network;
using Fantasy.Network.Interface;
using Fantasy.PacketParser.Interface;
#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member
// ReSharper disable UnassignedField.Global
#pragma warning disable CS8600 // Converting null literal or possible null value to non-nullable type.

namespace Fantasy.Scheduler
{
    public abstract class ANetworkMessageScheduler
    {
        protected readonly Scene Scene;
        protected readonly MessageDispatcherComponent MessageDispatcherComponent;
#if FANTASY_NET
        protected readonly NetworkMessagingComponent NetworkMessagingComponent;
#endif
        protected ANetworkMessageScheduler(Scene scene)
        {
            Scene = scene;
            MessageDispatcherComponent = scene.MessageDispatcherComponent;
#if FANTASY_NET
            NetworkMessagingComponent = scene.NetworkMessagingComponent;
#endif
        }
        public abstract FTask Scheduler(Session session, APackInfo packInfo);
    }
}