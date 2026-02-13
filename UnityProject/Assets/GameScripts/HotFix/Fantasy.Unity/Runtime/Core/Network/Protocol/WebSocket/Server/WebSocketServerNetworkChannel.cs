#if FANTASY_NET
using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO.Pipelines;
using System.Net;
using System.Net.WebSockets;
using System.Threading;
using Fantasy.Async;
using Fantasy.Network.Interface;
using Fantasy.PacketParser;
using Fantasy.Serialize;
#pragma warning disable CS8602 // Dereference of a possibly null reference.
#pragma warning disable CS8600 // Converting null literal or possible null value to non-nullable type.
#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member

namespace Fantasy.Network.WebSocket;

public sealed class WebSocketServerNetworkChannel : ANetworkServerChannel
{
    private bool _isSending;
    private bool _isInnerDispose;
    private readonly Pipe _pipe = new Pipe();
    private readonly System.Net.WebSockets.WebSocket _webSocket;
    private readonly WebSocketServerNetwork _network;
    private readonly ReadOnlyMemoryPacketParser _packetParser;
    private readonly Queue<MemoryStreamBuffer> _sendBuffers = new Queue<MemoryStreamBuffer>();
    private readonly CancellationTokenSource _cancellationTokenSource = new CancellationTokenSource();
    
    public WebSocketServerNetworkChannel(ANetwork network, uint id, HttpListenerWebSocketContext httpListenerWebSocketContext, IPEndPoint remoteEndPoint) : base(network, id, remoteEndPoint)
    {
        _network = (WebSocketServerNetwork)network;
        _webSocket = httpListenerWebSocketContext.WebSocket;
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
        
        if (_webSocket.State == WebSocketState.Open || _webSocket.State == WebSocketState.CloseReceived)
        {
            try
            {
                _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Normal Closure",
                    CancellationToken.None).GetAwaiter().GetResult();
            }
            catch (Exception)
            {
                // 关闭过程中的异常可以忽略
            }
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
        
        _sendBuffers.Clear();
        _network.RemoveChannel(Id);
        _webSocket.Dispose();
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
                // 这里接收的数据不一定是一个完整的包。如果大于8192就会分成多个包。
                var receiveResult = await _webSocket.ReceiveAsync(memory, _cancellationTokenSource.Token);
                // 客户端发送了关闭帧，服务器需要响应关闭帧
                if (receiveResult.MessageType == WebSocketMessageType.Close)
                {
                    if (_webSocket.State == WebSocketState.CloseReceived)
                    {
                        await _webSocket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "Response Closure",
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
            catch (WebSocketException)
            {
                // Log.Error($"WebSocket error: {wse.Message}");
                Dispose();
                break;
            }
            catch (Exception e)
            {
                Log.Error(e);
            }
        }

        await _pipe.Writer.CompleteAsync();
        
        // 接收循环结束，自动释放连接
        Dispose();
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

            await _webSocket.SendAsync(new ArraySegment<byte>(memoryStream.GetBuffer(), 0, (int)memoryStream.Position), WebSocketMessageType.Binary, true, _cancellationTokenSource.Token);
                
            if (memoryStream.MemoryStreamBufferSource == MemoryStreamBufferSource.Pack)
            {
                _network.MemoryStreamBufferPool.ReturnMemoryStream(memoryStream);
            }
        }
    }

    #endregion
}
#endif