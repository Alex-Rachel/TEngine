#if !FANTASY_WEBGL
using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipelines;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Threading;
using Fantasy.Async;
using Fantasy.Helper;
using Fantasy.Network.Interface;
using Fantasy.PacketParser;
using Fantasy.Serialize;
// ReSharper disable ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
#pragma warning disable CS8602 // Dereference of a possibly null reference.
#pragma warning disable CS8625 // Cannot convert null literal to non-nullable reference type.
#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member

#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
#pragma warning disable CS8604 // Possible null reference argument.
#pragma warning disable CS8600 // Converting null literal or possible null value to non-nullable type.
#pragma warning disable CS8622 // Nullability of reference types in type of parameter doesn't match the target delegate (possibly because of nullability attributes).

namespace Fantasy.Network.TCP
{
    public sealed class TCPClientNetwork : AClientNetwork
    {
        private bool _isSending;
        private bool _isInnerDispose;
        private long _connectTimeoutId;
        private bool _connectDisconnectEvent = true;
        private Socket _socket;
        private IPEndPoint _remoteEndPoint;
        private SocketAsyncEventArgs _sendArgs;
        private ReadOnlyMemoryPacketParser _packetParser;
        private readonly Pipe _pipe = new Pipe();
        private readonly Queue<MemoryStreamBuffer> _sendBuffers = new Queue<MemoryStreamBuffer>();
        private readonly CancellationTokenSource _cancellationTokenSource = new CancellationTokenSource();
        
        private Action _onConnectFail;
        private Action _onConnectComplete;
        private Action _onConnectDisconnect;
        
        public uint ChannelId { get; private set; }

        public void Initialize(NetworkTarget networkTarget)
        {
            base.Initialize(NetworkType.Client, NetworkProtocolType.TCP, networkTarget);
        }
        
        public override void Dispose()
        {
            if (IsDisposed || _isInnerDispose)
            {
                return;
            }

            try
            {
                _isSending = false;
                _isInnerDispose = true;
                ClearConnectTimeout();

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

                if (_connectDisconnectEvent)
                {
                    _onConnectDisconnect?.Invoke();
                }

                if (_socket.Connected)
                {
                    _socket.Close();
                    _socket = null;
                }

                _sendBuffers.Clear();
                _packetParser?.Dispose();
                ChannelId = 0;
                _sendArgs = null;
                _connectDisconnectEvent = true;
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

        /// <summary>
        /// 连接到远程服务器。
        /// </summary>
        /// <param name="remoteAddress">远程服务器的终端点。</param>
        /// <param name="onConnectComplete">连接成功时的回调。</param>
        /// <param name="onConnectFail">连接失败时的回调。</param>
        /// <param name="onConnectDisconnect">连接断开时的回调。</param>
        /// <param name="isHttps"></param>
        /// <param name="connectTimeout">连接超时时间，单位：毫秒。</param>
        /// <returns>连接的会话。</returns>
        public override Session Connect(string remoteAddress, Action onConnectComplete, Action onConnectFail, Action onConnectDisconnect, bool isHttps, int connectTimeout = 5000)
        {
            // 如果已经初始化过一次，抛出异常，要求重新实例化
            
            if (IsInit)
            {
                throw new NotSupportedException("TCPClientNetwork Has already been initialized. If you want to call Connect again, please re instantiate it.");
            }
            
            IsInit = true;
            _isSending = false;
            _onConnectFail = onConnectFail;
            _onConnectComplete = onConnectComplete;
            _onConnectDisconnect = onConnectDisconnect;
            // 设置连接超时定时器
            _connectTimeoutId = Scene.TimerComponent.Net.OnceTimer(connectTimeout, () =>
            {
                _connectDisconnectEvent = false;
                _onConnectFail?.Invoke();
                Dispose();
            });
            _packetParser = PacketParserFactory.CreateClientReadOnlyMemoryPacket(this);
            _remoteEndPoint = NetworkHelper.GetIPEndPoint(remoteAddress);
            _socket = new Socket(_remoteEndPoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
            _socket.NoDelay = true;
            _socket.SetSocketBufferToOsLimit();
            _sendArgs = new SocketAsyncEventArgs();
            _sendArgs.Completed += OnSendCompleted;
            var outArgs = new SocketAsyncEventArgs
            {
                RemoteEndPoint = _remoteEndPoint
            };
            outArgs.Completed += OnConnectSocketCompleted;
           
            if (!_socket.ConnectAsync(outArgs))
            {
                OnReceiveSocketComplete();
            }
            
            Session = Session.Create(this, _remoteEndPoint);
            return Session;
        }

        private void OnConnectSocketCompleted(object sender, SocketAsyncEventArgs asyncEventArgs)
        {
            if (_cancellationTokenSource.IsCancellationRequested)
            {
                return;
            }
            
            if (asyncEventArgs.LastOperation == SocketAsyncOperation.Connect)
            {
                if (asyncEventArgs.SocketError == SocketError.Success)
                {
                    Scene.ThreadSynchronizationContext.Post(OnReceiveSocketComplete);
                }
                else
                {
                    Scene.ThreadSynchronizationContext.Post(() =>
                    {
                        _connectDisconnectEvent = false;
                        _onConnectFail?.Invoke();
                        Dispose();
                    });
                }
            }
        }
        
        private void OnReceiveSocketComplete()
        {
            ClearConnectTimeout();
            _onConnectComplete?.Invoke();
            ReadPipeDataAsync().Coroutine();
            ReceiveSocketAsync().Coroutine();
        }

        #region ReceiveSocket

        private async FTask ReceiveSocketAsync()
        {
            while (!_cancellationTokenSource.IsCancellationRequested)
            {
                try
                {
                    var count = 0;
                    var memory = _pipe.Writer.GetMemory(8192);
#if UNITY_2021
                    // Unity2021.3.14f有个恶心的问题，使用ReceiveAsync会导致memory不能正确写入
                    // 所有只能使用ReceiveFromAsync来接收消息，但ReceiveFromAsync只有一个接受ArraySegment的接口。
                    MemoryMarshal.TryGetArray(memory, out ArraySegment<byte> arraySegment);
                    var result = await _socket.ReceiveFromAsync(arraySegment, SocketFlags.None, _remoteEndPoint);
                    count = result.ReceivedBytes;
#else
                    count = await _socket.ReceiveAsync(memory, SocketFlags.None, _cancellationTokenSource.Token);
#endif
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
                Log.Warning(e.Message);
                Dispose();
            }
            catch (Exception e)
            {
                Log.Error(e);
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
                MemoryStreamBufferPool.ReturnMemoryStream(memoryStream);
            }
        }

        private void OnSendCompleted(object sender, SocketAsyncEventArgs asyncEventArgs)
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
        
        public override void RemoveChannel(uint channelId)
        {
            Dispose();
        }
        
        private void ClearConnectTimeout()
        {
            if (_connectTimeoutId == 0)
            {
                return;
            }

            Scene?.TimerComponent?.Net?.Remove(ref _connectTimeoutId);
        }
    }
}
#endif