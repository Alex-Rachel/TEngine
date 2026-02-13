#if FANTASY_NET
using System;
using System.Net;
using System.Runtime.InteropServices;
using Fantasy.Network.Interface;
using Fantasy.PacketParser;
using Fantasy.Serialize;
using KCP;
// ReSharper disable ParameterHidesMember
#pragma warning disable CS8625 // Cannot convert null literal to non-nullable reference type.

#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member

namespace Fantasy.Network.KCP
{
    /// <summary>
    /// KCP 服务器网络通道，用于处理服务器与客户端之间的数据通信。
    /// </summary>
    internal class KCPServerNetworkChannel : ANetworkServerChannel
    {
        private bool _isInnerDispose;
        private readonly int _maxSndWnd;
        private KCPServerNetwork _kcpServerNetwork;
        private readonly BufferPacketParser _packetParser;
        private readonly byte[] _receiveBuffer = new byte[ProgramDefine.MaxMessageSize + 20];
        public Kcp Kcp { get; private set; }
        public uint ChannelId { get; private set; }

        public KCPServerNetworkChannel(KCPServerNetwork network, uint channelId, IPEndPoint ipEndPoint) : base(network, channelId, ipEndPoint)
        {
            _kcpServerNetwork = network;
            ChannelId = channelId;
            _maxSndWnd = network.Settings.MaxSendWindowSize;
            Kcp = KCPFactory.Create(network.Settings, ChannelId, KcpSpanCallback);
            _packetParser = PacketParserFactory.CreateServerBufferPacket(network);
        }

        public override void Dispose()
        {
            if (IsDisposed || _isInnerDispose)
            {
                return;
            }

            try
            {
                _isInnerDispose = true;
                _kcpServerNetwork.RemoveChannel(Id);
                IsDisposed = true;
                Kcp.Dispose();
                Kcp = null;
                ChannelId = 0;
                _kcpServerNetwork = null;
            }
            catch (Exception e)
            {
               Log.Error(e);
            }
            finally
            {
                base.Dispose();
            }
        }

        public void Input(ReadOnlyMemory<byte> buffer)
        {
            Kcp.Input(buffer.Span);
            _kcpServerNetwork.AddUpdateChannel(ChannelId, 0);

            while (!IsDisposed)
            {
                try
                {
                    var peekSize = Kcp.PeekSize();

                    if (peekSize < 0)
                    {
                        return;
                    }

                    var receiveCount = Kcp.Receive(_receiveBuffer.AsSpan(0, peekSize));

                    if (receiveCount != peekSize)
                    {
                        return;
                    }

                    if (!_packetParser.UnPack(_receiveBuffer, ref receiveCount, out var packInfo))
                    {
                        continue;
                    }

                    Session.Receive(packInfo);
                }
                catch (ScanException e)
                {
                    Log.Debug($"RemoteAddress:{RemoteEndPoint} \n{e}");
                    Dispose();
                }
                catch (Exception e)
                {
                    Log.Error(e);
                }
            }
        }

        public override void Send(uint rpcId, long address, MemoryStreamBuffer memoryStream, IMessage message, Type messageType)
        {
            if (IsDisposed)
            {
                return;
            }

            if (Kcp.WaitSendCount > _maxSndWnd)
            {
                // 检查等待发送的消息，如果超出两倍窗口大小，KCP作者给的建议是要断开连接
                Log.Warning($"ERR_KcpWaitSendSizeTooLarge {Kcp.WaitSendCount} > {_maxSndWnd}");
                Dispose();
                return;
            }

            var buffer = _packetParser.Pack(ref rpcId, ref address, memoryStream, message, messageType);
            Kcp.Send(buffer.GetBuffer().AsSpan(0, (int)buffer.Position));
            
            if (buffer.MemoryStreamBufferSource == MemoryStreamBufferSource.Pack)
            {
                _kcpServerNetwork.MemoryStreamBufferPool.ReturnMemoryStream(buffer);
            }
            
            _kcpServerNetwork.AddUpdateChannel(ChannelId, 0);
        }

        private const byte KcpHeaderReceiveData = (byte)KcpHeader.ReceiveData;

        private unsafe void KcpSpanCallback(byte[] buffer, int count)
        {
            if (IsDisposed)
            {
                return;
            }

            try
            {
                if (count == 0)
                {
                    throw new Exception("KcpOutput count 0");
                }
                
                var channelId = ChannelId;
                var bufferSpan = buffer.AsSpan();
                bufferSpan[0] = KcpHeaderReceiveData;
                MemoryMarshal.Write(bufferSpan[1..], in channelId);
                
                _kcpServerNetwork.SendAsync(buffer, 0, count + 5, RemoteEndPoint);
            }
            catch (Exception e)
            {
                Log.Error(e);
            }
        }
    }
}
#endif