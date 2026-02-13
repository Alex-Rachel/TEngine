#if FANTASY_NET
using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO.Pipelines;
using System.Net.Sockets;
using System.Threading;
using Fantasy.Async;
using Fantasy.Network.Interface;
using Fantasy.PacketParser;
using Fantasy.Serialize;
// ReSharper disable ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
#pragma warning disable CS8625 // Cannot convert null literal to non-nullable reference type.
#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member

#pragma warning disable CS8600 // Converting null literal or possible null value to non-nullable type.
#pragma warning disable CS8602 // Dereference of a possibly null reference.

#pragma warning disable CS8604 // Possible null reference argument.
#pragma warning disable CS8622 // Nullability of reference types in type of parameter doesn't match the target delegate (possibly because of nullability attributes).

namespace Fantasy.Network.TCP
{
    public sealed class TCPServerNetworkChannel : ANetworkServerChannel
    {
        private bool _isSending;
        private bool _isInnerDispose;
        private readonly Socket _socket;
        private readonly ANetwork _network;
        private readonly Pipe _pipe = new Pipe();
        private readonly SocketAsyncEventArgs _sendArgs;
        private readonly ReadOnlyMemoryPacketParser _packetParser;
        private readonly Queue<MemoryStreamBuffer> _sendBuffers = new Queue<MemoryStreamBuffer>();
        private readonly CancellationTokenSource _cancellationTokenSource = new CancellationTokenSource();

        public TCPServerNetworkChannel(ANetwork network, Socket socket, uint id) : base(network, id, socket.RemoteEndPoint)
        {
            _socket = socket;
            _network = network;
            _socket.NoDelay = true;
            _sendArgs = new SocketAsyncEventArgs();
            _sendArgs.Completed += OnSendCompletedHandler;
            _packetParser = PacketParserFactory.CreateServerReadOnlyMemoryPacket(network);
            ReadPipeDataAsync().Coroutine();
            ReceiveSocketAsync().Coroutine();
        }

        public override void Dispose()
        {
            if (IsDisposed || _isInnerDispose)
            {
                return;
            }

            _isInnerDispose = true;
            _network.RemoveChannel(Id);
            
            if (!_cancellationTokenSource.IsCancellationRequested)
            {
                try
                {
                    _cancellationTokenSource.Cancel();
                }
                catch (OperationCanceledException)
                {
                    // 通常情况下，此处的异常可以忽略
                }
            }
            
            if (_socket != null)
            {
                _socket.Shutdown(SocketShutdown.Both);
                _socket.Close();
            }

            _sendBuffers.Clear();
            _packetParser.Dispose();
            _isSending = false;
            base.Dispose();
        }

        #region ReceiveSocket

        private async FTask ReceiveSocketAsync()
        {
            while (!_cancellationTokenSource.IsCancellationRequested)
            {
                try
                {
                    var memory = _pipe.Writer.GetMemory(8192);
                    var count = await _socket.ReceiveAsync(memory, SocketFlags.None, _cancellationTokenSource.Token);
                    
                    if (count == 0)
                    {
                        Dispose();
                        return;
                    }
                    
                    _pipe.Writer.Advance(count);
                    await _pipe.Writer.FlushAsync();
                }
                catch (SocketException)
                {
                    Dispose();
                    break;
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (ObjectDisposedException)
                {
                    Dispose();
                    break;
                }
                catch (Exception ex)
                {
                    Log.Error($"Unexpected exception: {ex.Message}");
                }
            }

            await _pipe.Writer.CompleteAsync();
        }

        #endregion

        #region ReceivePipeData

        private async FTask ReadPipeDataAsync()
        {
            var pipeReader = _pipe.Reader;
            while (!_cancellationTokenSource.IsCancellationRequested)
            {
                ReadResult result = default;

                try
                {
                    result = await pipeReader.ReadAsync(_cancellationTokenSource.Token);
                }
                catch (OperationCanceledException)
                {
                    // 出现这个异常表示取消了_cancellationTokenSource。一般Channel断开会取消。
                    break;
                }

                var buffer = result.Buffer;
                var consumed = buffer.Start;
                var examined = buffer.End;

                while (TryReadMessage(ref buffer, out var message))
                {
                    ReceiveData(ref message);
                    consumed = buffer.Start;
                }

                if (result.IsCompleted)
                {
                    break;
                }

                pipeReader.AdvanceTo(consumed, examined);
            }

            await pipeReader.CompleteAsync();
        }

        private bool TryReadMessage(ref ReadOnlySequence<byte> buffer, out ReadOnlyMemory<byte> message)
        {
            if (buffer.Length == 0)
            {
                message = default;
                return false;
            }

            message = buffer.First;

            if (message.Length == 0)
            {
                message = default;
                return false;
            }

            buffer = buffer.Slice(message.Length);
            return true;
        }

        private void ReceiveData(ref ReadOnlyMemory<byte> buffer)
        {
            try
            {
                while (_packetParser.UnPack(ref buffer, out var packInfo))
                {
                    if (_cancellationTokenSource.IsCancellationRequested)
                    {
                        return;
                    }

                    Session.Receive(packInfo);
                }
            }
            catch (ScanException e)
            {
                Log.Warning($"RemoteAddress:{RemoteEndPoint} \n{e}");
                Dispose();
            }
            catch (Exception e)
            {
                Log.Error($"RemoteAddress:{RemoteEndPoint} \n{e}");
                Dispose();
            }
        }

        #endregion

        #region Send

        public override void Send(uint rpcId, long address, MemoryStreamBuffer memoryStream, IMessage message, Type messageType)
        {
            _sendBuffers.Enqueue(_packetParser.Pack(ref rpcId, ref address, memoryStream, message, messageType));

            if (!_isSending)
            {
                Send();
            }
        }
        
        private void Send()
        {
            if (_isSending || IsDisposed)
            {
                return;
            }
            
            _isSending = true;
            
            while (_sendBuffers.Count > 0)
            {
                var memoryStreamBuffer = _sendBuffers.Dequeue();
                _sendArgs.UserToken = memoryStreamBuffer;
                _sendArgs.SetBuffer(new ArraySegment<byte>(memoryStreamBuffer.GetBuffer(), 0, (int)memoryStreamBuffer.Position));

                try
                {
                    if (_socket.SendAsync(_sendArgs))
                    {
                        return;
                    }

                    ReturnMemoryStream(memoryStreamBuffer);
                }
                catch
                {
                    _isSending = false;
                    return;
                }
            }
            
            _isSending = false;
        }
        
        private void ReturnMemoryStream(MemoryStreamBuffer memoryStream)
        {
            if (memoryStream.MemoryStreamBufferSource == MemoryStreamBufferSource.Pack)
            {
                _network.MemoryStreamBufferPool.ReturnMemoryStream(memoryStream);
            }
        }
        
        private void OnSendCompletedHandler(object sender, SocketAsyncEventArgs asyncEventArgs)
        {
            if (asyncEventArgs.SocketError != SocketError.Success || asyncEventArgs.BytesTransferred == 0)
            {
                _isSending = false;
                return;
            }

            var memoryStreamBuffer = (MemoryStreamBuffer)asyncEventArgs.UserToken;
            
            Scene.ThreadSynchronizationContext.Post(() =>
            {
                ReturnMemoryStream(memoryStreamBuffer);
                
                if (_sendBuffers.Count > 0)
                {
                    Send();
                }
                else
                {
                    _isSending = false;
                }
            });
        }

        #endregion
    }
}
#endif