#if FANTASY_NET || FANTASY_CONSOLE
using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO.Pipelines;
using System.Net.WebSockets;
using System.Threading;
using Fantasy.Async;
using Fantasy.Helper;
using Fantasy.Network.Interface;
using Fantasy.PacketParser;
using Fantasy.Serialize;
// ReSharper disable ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member
#pragma warning disable CS8603 // Possible null reference return.
#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
#pragma warning disable CS8625 // Cannot convert null literal to non-nullable reference type.
namespace Fantasy.Network.WebSocket
{
    public sealed class WebSocketClientNetwork : AClientNetwork
    {
        private bool _isSending;
        private bool _isInnerDispose;
        private long _connectTimeoutId;
        private bool _connectDisconnectEvent = true;
        private ClientWebSocket _clientWebSocket;
        private ReadOnlyMemoryPacketParser _packetParser;
        private readonly Pipe _pipe = new Pipe();
        private readonly Queue<MemoryStreamBuffer> _sendBuffers = new Queue<MemoryStreamBuffer>();
        private readonly CancellationTokenSource _cancellationTokenSource = new CancellationTokenSource();

        private Action _onConnectFail;
        private Action _onConnectComplete;
        private Action _onConnectDisconnect;

        public void Initialize(NetworkTarget networkTarget)
        {
            base.Initialize(NetworkType.Client, NetworkProtocolType.WebSocket, networkTarget);
            _packetParser = PacketParserFactory.CreateClientReadOnlyMemoryPacket(this);
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

                if (_clientWebSocket.State == WebSocketState.Open || _clientWebSocket.State == WebSocketState.CloseReceived)
                {
                    _clientWebSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Client Closing", CancellationToken.None).GetAwaiter().GetResult();
                }

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

                ClearConnectTimeout();
                
                if (_connectDisconnectEvent)
                {
                    _onConnectDisconnect?.Invoke();
                }
                
                _packetParser.Dispose();
                _packetParser = null;
                _isSending = false;
                
                _clientWebSocket.Dispose();
                _clientWebSocket = null;
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

        public override Session Connect(string remoteAddress, Action onConnectComplete, Action onConnectFail, Action onConnectDisconnect, bool isHttps, int connectTimeout = 5000)
        {
            if (IsInit)
            {
                throw new NotSupportedException(
                    $"WebSocketClientNetwork Id:{Id} Has already been initialized. If you want to call Connect again, please re instantiate it.");
            }

            IsInit = true;
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

            _clientWebSocket = new ClientWebSocket();
            var webSocketAddress = WebSocketHelper.GetWebSocketAddress(remoteAddress, isHttps);

            try
            {
                _clientWebSocket.ConnectAsync(new Uri(webSocketAddress), _cancellationTokenSource.Token).Wait();

                if (_cancellationTokenSource.IsCancellationRequested)
                {
                    return null;
                }
            }
            catch (WebSocketException wse)
            {
                Log.Error($"WebSocket error: {wse.Message}");
                _connectDisconnectEvent = false;
                _onConnectFail?.Invoke();
                Dispose();
                return null;
            }
            catch (Exception e)
            {
                Log.Error($"An error occurred: {e.Message}");
                _connectDisconnectEvent = false;
                _onConnectFail?.Invoke();
                Dispose();
                return null;
            }

            ClearConnectTimeout();
            ReadPipeDataAsync().Coroutine();
            ReceiveSocketAsync().Coroutine();
            _onConnectComplete?.Invoke();
            Session = Session.Create(this, null);
            return Session;
        }

        #region ReceiveSocket

        private async FTask ReceiveSocketAsync()
        {
            while (!_cancellationTokenSource.IsCancellationRequested)
            {
                try
                {
                    var memory = _pipe.Writer.GetMemory(8192);
                    // 这里接收的数据不一定是一个完整的包。如果大于8192就会分成多个包。
                    var receiveResult = await _clientWebSocket.ReceiveAsync(memory, _cancellationTokenSource.Token);
                    // 服务器发送了关闭帧，客户端需要响应关闭帧
                    if (receiveResult.MessageType == WebSocketMessageType.Close)
                    {
                        if (_clientWebSocket.State == WebSocketState.CloseReceived)
                        {
                            await _clientWebSocket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "Response Closure",
                                CancellationToken.None);
                        }
                        Dispose();
                        return;
                    }

                    var count = receiveResult.Count;

                    if (count > 0)
                    {
                        await PipeWriterFlushAsync(count);
                    }
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
                // 这个先暂时注释掉，因为有些时候会出现WebSocketException
                // 因为会出现这个挥手的错误，下个版本处理一下。
                // The remote party closed the WebSocket connection without completing the close handshake.
                // catch (WebSocketException wse)
                // {
                //     Log.Error($"WebSocket error: {wse.Message}");
                //     Dispose();
                //     break;
                // }
                catch (Exception e)
                {
                    Log.Error(e);
                }
            }

            await _pipe.Writer.CompleteAsync();
        }

        private async FTask PipeWriterFlushAsync(int count)
        {
            _pipe.Writer.Advance(count);
            await _pipe.Writer.FlushAsync();
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
                Send().Coroutine();
            }
        }
        
        private async FTask Send()
        {
            if (_isSending || IsDisposed)
            {
                return;
            }
            
            _isSending = true;
            
            while (_isSending)
            {
                if (!_sendBuffers.TryDequeue(out var memoryStream))
                {
                    _isSending = false;
                    return;
                }

                await _clientWebSocket.SendAsync(new ArraySegment<byte>(memoryStream.GetBuffer(), 0, (int)memoryStream.Position), WebSocketMessageType.Binary, true, _cancellationTokenSource.Token);
                
                if (memoryStream.MemoryStreamBufferSource == MemoryStreamBufferSource.Pack)
                {
                    MemoryStreamBufferPool.ReturnMemoryStream(memoryStream);
                }
            }
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