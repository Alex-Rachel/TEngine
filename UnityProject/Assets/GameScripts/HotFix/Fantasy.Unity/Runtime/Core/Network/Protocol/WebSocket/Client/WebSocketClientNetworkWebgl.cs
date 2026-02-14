#if !FANTASY_NET && !FANTASY_CONSOLE
using System;
using System.Collections.Generic;
using System.IO;
using Fantasy.Helper;
using Fantasy.Network.Interface;
using Fantasy.PacketParser;
using Fantasy.Serialize;
using UnityWebSocket;

namespace Fantasy.Network.WebSocket
{
    // 因为webgl的限制、注定这个要是在游戏主线程里。所以这个库不会再其他线程执行的。
    // WebGL:在WebGL环境下运行
    // 另外不是运行在WebGL环境下，也没必要使用WebSocket协议了。完全可以使用TCP或KCP运行。同样也不会有那个队列产生的GC。
    public class WebSocketClientNetwork : AClientNetwork
    {
        private UnityWebSocket.WebSocket _webSocket;
        private bool _isInnerDispose;
        private bool _isConnected;
        private long _connectTimeoutId;
        private bool _connectDisconnectEvent = true;
        private BufferPacketParser _packetParser; 
        private readonly Queue<MemoryStreamBuffer> _messageCache = new Queue<MemoryStreamBuffer>();
        
        private Action _onConnectFail;
        private Action _onConnectComplete;
        private Action _onConnectDisconnect;
        
        public void Initialize(NetworkTarget networkTarget)
        {
            base.Initialize(NetworkType.Client, NetworkProtocolType.WebSocket, networkTarget);
            _packetParser = PacketParserFactory.CreateClient<BufferPacketParser>(this);
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
                ClearConnectTimeout();
            
                if (_webSocket != null && _webSocket.ReadyState != WebSocketState.Closed)
                {
                    _webSocket.CloseAsync();
                }
            
                _packetParser.Dispose();
                _messageCache.Clear();
                
                if (_connectDisconnectEvent)
                {
                    _onConnectDisconnect?.Invoke();
                }
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
            // 如果已经初始化过一次，抛出异常，要求重新实例化
            
            if (IsInit)
            {
                throw new NotSupportedException($"WebSocketClientNetwork Id:{Id} Has already been initialized. If you want to call Connect again, please re instantiate it.");
            }
            
            IsInit = true;
            _onConnectFail = onConnectFail;
            _onConnectComplete = onConnectComplete;
            _onConnectDisconnect = onConnectDisconnect;
            _connectTimeoutId = Scene.TimerComponent.Net.OnceTimer(connectTimeout, () =>
            {
                _connectDisconnectEvent = false;
                _onConnectFail?.Invoke();
                Dispose();
            });
            var webSocketAddress = WebSocketHelper.GetWebSocketAddress(remoteAddress, isHttps);
            _webSocket = new UnityWebSocket.WebSocket(webSocketAddress);
            _webSocket.OnOpen += OnNetworkConnectComplete;
            _webSocket.OnMessage += OnReceiveComplete;
            _webSocket.OnClose += (sender, args) =>
            {
                Dispose();
            };
            _webSocket.ConnectAsync();
            Session = Session.Create(this, null);
            return Session;
        }

        private void OnNetworkConnectComplete(object sender, OpenEventArgs e)
        {
            if (IsDisposed)
            {
                return;
            }
            
            _isConnected = true;
            ClearConnectTimeout();
            _onConnectComplete?.Invoke();

            while (_messageCache.TryDequeue(out var memoryStream))
            {
                Send(memoryStream);
            }
        }

        #region Receive

        private void OnReceiveComplete(object sender, MessageEventArgs e)
        {
            try
            {
                // WebSocket 协议已经在协议层面处理了消息的边界问题，因此不需要额外的粘包处理逻辑。
                // 所以如果解包的时候出现任何错误只能是恶意攻击造成的。
                var rawDataLength = e.RawData.Length;
                _packetParser.UnPack(e.RawData, ref rawDataLength, out var packInfo);
                Session.Receive(packInfo);
            }
            catch (ScanException ex)
            {
                Log.Warning($"{ex}");
                Dispose();
            }
            catch (Exception ex)
            {
                Log.Error($"{ex}");
                Dispose();
            }
        }

        #endregion

        #region Send

        public override void Send(uint rpcId, long address, MemoryStreamBuffer memoryStream, IMessage message, Type messageType)
        {
            if (IsDisposed)
            {
                return;
            }

            var buffer = _packetParser.Pack(ref rpcId, ref address, memoryStream, message, messageType);
            
            if (!_isConnected)
            {
                _messageCache.Enqueue(buffer);
                return;
            }

            Send(buffer);
        }
        
        private void Send(MemoryStreamBuffer memoryStream)
        {
            _webSocket.SendAsync(memoryStream.GetBuffer(), 0, (int)memoryStream.Position);
#if !UNITY_EDITOR && UNITY_WEBGL
            if (memoryStream.MemoryStreamBufferSource == MemoryStreamBufferSource.Pack)
            {
                MemoryStreamBufferPool.ReturnMemoryStream(memoryStream);
            }
#endif
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