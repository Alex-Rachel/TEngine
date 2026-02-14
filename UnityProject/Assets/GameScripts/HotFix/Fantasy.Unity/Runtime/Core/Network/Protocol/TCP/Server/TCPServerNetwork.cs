#if FANTASY_NET
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using Fantasy.Helper;
using Fantasy.Network.Interface;
#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member

// ReSharper disable GCSuppressFinalizeForTypeWithoutDestructor
#pragma warning disable CS8622 // Nullability of reference types in type of parameter doesn't match the target delegate (possibly because of nullability attributes).
#pragma warning disable CS8625 // Cannot convert null literal to non-nullable reference type.
#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.

namespace Fantasy.Network.TCP
{
    public sealed class TCPServerNetwork : ANetwork
    {
        private Random _random;
        private Socket _socket;
        private SocketAsyncEventArgs _acceptAsync;
        private readonly Dictionary<uint, INetworkChannel> _connectionChannel = new Dictionary<uint, INetworkChannel>();

        public void Initialize(NetworkTarget networkTarget, IPEndPoint address)
        {
            base.Initialize(NetworkType.Server, NetworkProtocolType.TCP, networkTarget);
            _random = new Random();
            _acceptAsync = new SocketAsyncEventArgs();
            _socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
            _socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, false);

            if (address.AddressFamily == AddressFamily.InterNetworkV6)
            {
                _socket.SetSocketOption(SocketOptionLevel.IPv6, SocketOptionName.IPv6Only, false);
            }

            _socket.Bind(address);
            _socket.Listen(int.MaxValue);
            _socket.SetSocketBufferToOsLimit();
            Log.Info($"SceneConfigId = {Scene.SceneConfigId} networkTarget = {networkTarget.ToString()} TCPServer Listen {address}");
            _acceptAsync.Completed += OnCompleted;
            AcceptAsync();
        }

        public override void Dispose()
        {
            if (IsDisposed)
            {
                return;
            }

            try
            {
                foreach (var networkChannel in _connectionChannel.Values.ToArray())
                {
                    networkChannel.Dispose();
                }

                _connectionChannel.Clear();
                _random = null;
                _socket.Dispose();
                _socket = null;
                _acceptAsync.Dispose();
                _acceptAsync = null;
                GC.SuppressFinalize(this);
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

        private void AcceptAsync()
        {
            _acceptAsync.AcceptSocket = null;

            if (_socket.AcceptAsync(_acceptAsync))
            {
                return;
            }
            
            OnAcceptComplete(_acceptAsync);
        }

        private void OnAcceptComplete(SocketAsyncEventArgs asyncEventArgs)
        {
            if (asyncEventArgs.AcceptSocket == null)
            {
                return;
            }

            if (asyncEventArgs.SocketError != SocketError.Success)
            {
                Log.Error($"Socket Accept Error: {_acceptAsync.SocketError}");
                return;
            }

            try
            {
                uint channelId;
                do
                {
                    channelId = 0xC0000000 | (uint)_random.Next();
                } while (_connectionChannel.ContainsKey(channelId));

                _connectionChannel.Add(channelId, new TCPServerNetworkChannel(this, asyncEventArgs.AcceptSocket, channelId));
            }
            catch (Exception e)
            {
                Log.Error(e);
            }
            finally
            {
                AcceptAsync();
            }
        }

        public override void RemoveChannel(uint channelId)
        {
            if (IsDisposed || !_connectionChannel.Remove(channelId, out var channel))
            {
                return;
            }

            if (channel.IsDisposed)
            {
                return;
            }

            channel.Dispose();
        }

        #region 网络线程（由Socket底层产生的线程）

        private void OnCompleted(object sender, SocketAsyncEventArgs asyncEventArgs)
        {
            switch (asyncEventArgs.LastOperation)
            {
                case SocketAsyncOperation.Accept:
                {
                    try
                    {
                        Scene.ThreadSynchronizationContext.Post(() =>
                        {
                            OnAcceptComplete(asyncEventArgs);
                        });
                    }
                    catch
                    {
                        // ignored
                    }

                    break;
                }
                default:
                {
                    throw new Exception($"Socket Accept Error: {asyncEventArgs.LastOperation}");
                }
            }
        }

        #endregion
    }
}
#endif


