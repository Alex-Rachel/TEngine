using System;
using Fantasy.Network;
using Fantasy.Network.Interface;
using Fantasy.PacketParser.Interface;

// ReSharper disable PossibleNullReferenceException
#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member
#pragma warning disable CS8602 // Dereference of a possibly null reference.
#pragma warning disable CS8600 // Converting null literal or possible null value to non-nullable type.
#pragma warning disable CS8603 // Possible null reference return.
namespace Fantasy.PacketParser
{
    internal static class PacketParserFactory
    {
#if FANTASY_NET
        internal static ReadOnlyMemoryPacketParser CreateServerReadOnlyMemoryPacket(ANetwork network)
        {
            ReadOnlyMemoryPacketParser readOnlyMemoryPacketParser = null;
            
            switch (network.NetworkTarget)
            {
                case NetworkTarget.Inner:
                {
                    readOnlyMemoryPacketParser = new InnerReadOnlyMemoryPacketParser();
                    break;
                }
                case NetworkTarget.Outer:
                {
                    readOnlyMemoryPacketParser = new OuterReadOnlyMemoryPacketParser();
                    break;
                }
            }
            
            readOnlyMemoryPacketParser.Scene = network.Scene;
            readOnlyMemoryPacketParser.Network = network;
            readOnlyMemoryPacketParser.MessageDispatcherComponent = network.Scene.MessageDispatcherComponent;
            return readOnlyMemoryPacketParser;
        }

        public static BufferPacketParser CreateServerBufferPacket(ANetwork network)
        {
            BufferPacketParser bufferPacketParser = null;
            
            switch (network.NetworkTarget)
            {
                case NetworkTarget.Inner:
                {
                    bufferPacketParser = new InnerBufferPacketParser();
                    break;
                }
                case NetworkTarget.Outer:
                {
                    bufferPacketParser = new OuterBufferPacketParser();
                    break;
                }
            }
            
            bufferPacketParser.Scene = network.Scene;
            bufferPacketParser.Network = network;
            bufferPacketParser.MessageDispatcherComponent = network.Scene.MessageDispatcherComponent;
            return bufferPacketParser;
        }
#endif
        internal static ReadOnlyMemoryPacketParser CreateClientReadOnlyMemoryPacket(ANetwork network)
        {
            ReadOnlyMemoryPacketParser readOnlyMemoryPacketParser = null;

            switch (network.NetworkTarget)
            {
#if FANTASY_NET
                case NetworkTarget.Inner:
                {
                    readOnlyMemoryPacketParser = new InnerReadOnlyMemoryPacketParser();
                    break;
                }
#endif
                case NetworkTarget.Outer:
                {
                    readOnlyMemoryPacketParser = new OuterReadOnlyMemoryPacketParser();
                    break;
                }
            }

            readOnlyMemoryPacketParser.Scene = network.Scene;
            readOnlyMemoryPacketParser.Network = network;
            readOnlyMemoryPacketParser.MessageDispatcherComponent = network.Scene.MessageDispatcherComponent;
            return readOnlyMemoryPacketParser;
        }

#if !FANTASY_WEBGL
        public static BufferPacketParser CreateClientBufferPacket(ANetwork network)
        {
            BufferPacketParser bufferPacketParser = null;

            switch (network.NetworkTarget)
            {
#if FANTASY_NET
                case NetworkTarget.Inner:
                {
                    bufferPacketParser = new InnerBufferPacketParser();
                    break;
                }
#endif
                case NetworkTarget.Outer:
                {
                    bufferPacketParser = new OuterBufferPacketParser();
                    break;
                }
            }

            bufferPacketParser.Scene = network.Scene;
            bufferPacketParser.Network = network;
            bufferPacketParser.MessageDispatcherComponent = network.Scene.MessageDispatcherComponent;
            return bufferPacketParser;
        }
#endif
        public static T CreateClient<T>(ANetwork network) where T : APacketParser
        {
            var packetParserType = typeof(T);
            
            switch (network.NetworkTarget)
            {
#if FANTASY_NET
                case NetworkTarget.Inner:
                {
                    APacketParser innerPacketParser = null;

                    if (packetParserType == typeof(ReadOnlyMemoryPacketParser))
                    {
                        innerPacketParser = new InnerReadOnlyMemoryPacketParser();
                    }
                    else if (packetParserType == typeof(BufferPacketParser))
                    {
                        innerPacketParser = new InnerBufferPacketParser();
                    }
                    // else if(packetParserType == typeof(CircularBufferPacketParser))
                    // {
                    //     innerPacketParser = new InnerCircularBufferPacketParser();
                    // }

                    innerPacketParser.Scene = network.Scene;
                    innerPacketParser.Network = network;
                    innerPacketParser.MessageDispatcherComponent = network.Scene.MessageDispatcherComponent;
                    return (T)innerPacketParser;
                }
#endif
                case NetworkTarget.Outer:
                {
                    APacketParser outerPacketParser = null;

                    if (packetParserType == typeof(ReadOnlyMemoryPacketParser))
                    {
                        outerPacketParser = new OuterReadOnlyMemoryPacketParser();
                    }
                    else if (packetParserType == typeof(BufferPacketParser))
                    {
#if FANTASY_WEBGL
                        outerPacketParser = new OuterWebglBufferPacketParser();
#else
                        outerPacketParser = new OuterBufferPacketParser();
#endif
                    }
                    outerPacketParser.Scene = network.Scene;
                    outerPacketParser.Network = network;
                    outerPacketParser.MessageDispatcherComponent = network.Scene.MessageDispatcherComponent;
                    return (T)outerPacketParser;
                }
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }
    }
}