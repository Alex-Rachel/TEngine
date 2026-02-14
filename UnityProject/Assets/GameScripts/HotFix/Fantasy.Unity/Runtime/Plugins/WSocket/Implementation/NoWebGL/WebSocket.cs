#if !NET_LEGACY && (UNITY_EDITOR || !UNITY_WEBGL)
using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Net.WebSockets;
using System.IO;
using System.Collections.Concurrent;

namespace UnityWebSocket
{
    public class WebSocket : IWebSocket
    {
        public string Address { get; private set; }
        public string[] SubProtocols { get; private set; }

        public WebSocketState ReadyState
        {
            get
            {
                if (socket == null)
                    return WebSocketState.Closed;
                switch (socket.State)
                {
                    case System.Net.WebSockets.WebSocketState.Closed:
                    case System.Net.WebSockets.WebSocketState.None:
                        return WebSocketState.Closed;
                    case System.Net.WebSockets.WebSocketState.CloseReceived:
                    case System.Net.WebSockets.WebSocketState.CloseSent:
                        return WebSocketState.Closing;
                    case System.Net.WebSockets.WebSocketState.Connecting:
                        return WebSocketState.Connecting;
                    case System.Net.WebSockets.WebSocketState.Open:
                        return WebSocketState.Open;
                }
                return WebSocketState.Closed;
            }
        }

        public event EventHandler<OpenEventArgs> OnOpen;
        public event EventHandler<CloseEventArgs> OnClose;
        public event EventHandler<ErrorEventArgs> OnError;
        public event EventHandler<MessageEventArgs> OnMessage;

        private ClientWebSocket socket;
        private bool isOpening => socket != null && socket.State == System.Net.WebSockets.WebSocketState.Open;
        private ConcurrentQueue<SendBuffer> sendQueue = new ConcurrentQueue<SendBuffer>();
        private ConcurrentQueue<EventArgs> eventQueue = new ConcurrentQueue<EventArgs>();
        private bool closeProcessing;
        private CancellationTokenSource cts = null;

        #region APIs 
        public WebSocket(string address)
        {
            this.Address = address;
        }

        public WebSocket(string address, string subProtocol)
        {
            this.Address = address;
            this.SubProtocols = new string[] { subProtocol };
        }

        public WebSocket(string address, string[] subProtocols)
        {
            this.Address = address;
            this.SubProtocols = subProtocols;
        }

        public void ConnectAsync()
        {
            if (socket != null)
            {
                HandleError(new Exception("Socket is busy."));
                return;
            }

            WebSocketManager.Instance.Add(this);

            socket = new ClientWebSocket();
            cts = new CancellationTokenSource();

            // support sub protocols
            if (this.SubProtocols != null)
            {
                foreach (var protocol in this.SubProtocols)
                {
                    if (string.IsNullOrEmpty(protocol)) continue;
                    Log($"Add Sub Protocol {protocol}");
                    socket.Options.AddSubProtocol(protocol);
                }
            }

            Task.Run(ConnectTask);
        }

        public void CloseAsync()
        {
            if (!isOpening) return;
            closeProcessing = true;
        }

        public void SendAsync(byte[] data, int offset, int len)
        {
            if (!isOpening) return;
            var buffer = new SendBuffer(data, offset, len, WebSocketMessageType.Binary);
            sendQueue.Enqueue(buffer);
        }

        public void SendAsync(byte[] data)
        {
            if (!isOpening) return;
            var buffer = new SendBuffer(data, 0, data.Length, WebSocketMessageType.Binary);
            sendQueue.Enqueue(buffer);
        }

        public void SendAsync(string text)
        {
            if (!isOpening) return;
            var data = Encoding.UTF8.GetBytes(text);
            var buffer = new SendBuffer(data, 0, data.Length, WebSocketMessageType.Text);
            sendQueue.Enqueue(buffer);
        }
        #endregion

        class SendBuffer
        {
            public int offset;
            public int len;
            public byte[] data;
            public WebSocketMessageType type;
            public SendBuffer(byte[] data, int offset, int len, WebSocketMessageType type)
            {
                this.offset = offset;
                this.len = len;
                this.data = data;
                this.type = type;
            }
        }

        private void CleanSendQueue()
        {
            while (sendQueue.TryDequeue(out var _)) ;
        }

        private void CleanEventQueue()
        {
            while (eventQueue.TryDequeue(out var _)) ;
        }

        private async Task ConnectTask()
        {
            Log("Connect Task Begin ...");

            try
            {
                var uri = new Uri(Address);
                await socket.ConnectAsync(uri, cts.Token);
            }
            catch (Exception e)
            {
                HandleError(e);
                HandleClose((ushort)CloseStatusCode.Abnormal, e.Message);
                return;
            }

            HandleOpen();

            Log("Connect Task Success !");

            StartReceiveTask();
            StartSendTask();
        }

        private async void StartSendTask()
        {
            Log("Send Task Begin ...");

            try
            {
                while (!closeProcessing && socket != null && cts != null && !cts.IsCancellationRequested)
                {
                    while (!closeProcessing && sendQueue.Count > 0 && sendQueue.TryDequeue(out var buffer))
                    {
                        Log($"Send, type: {buffer.type}, size: {buffer.data.Length}, queue left: {sendQueue.Count}");
                        await socket.SendAsync(new ArraySegment<byte>(buffer.data, buffer.offset, buffer.len), buffer.type, true, cts.Token);
                    }
                    Thread.Sleep(3);
                }
                if (closeProcessing && socket != null && cts != null && !cts.IsCancellationRequested)
                {
                    CleanSendQueue();
                    Log($"Close Send Begin ...");
                    await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "Normal Closure", cts.Token);
                    Log($"Close Send Success !");
                }
            }
            catch (Exception e)
            {
                HandleError(e);
            }
            finally
            {
                closeProcessing = false;
            }

            Log("Send Task End !");
        }

        private async void StartReceiveTask()
        {
            Log("Receive Task Begin ...");

            string closeReason = "";
            ushort closeCode = 0;
            bool isClosed = false;
            var segment = new ArraySegment<byte>(new byte[8192]);
            var ms = new MemoryStream();

            try
            {
                while (!isClosed && !cts.IsCancellationRequested)
                {
                    var result = await socket.ReceiveAsync(segment, cts.Token);
                    ms.Write(segment.Array, 0, result.Count);
                    if (!result.EndOfMessage) continue;
                    var data = ms.ToArray();
                    ms.SetLength(0);
                    switch (result.MessageType)
                    {
                        case WebSocketMessageType.Binary:
                            HandleMessage(Opcode.Binary, data);
                            break;
                        case WebSocketMessageType.Text:
                            HandleMessage(Opcode.Text, data);
                            break;
                        case WebSocketMessageType.Close:
                            isClosed = true;
                            closeCode = (ushort)result.CloseStatus;
                            closeReason = result.CloseStatusDescription;
                            break;
                    }
                }
            }
            catch (Exception e)
            {
                HandleError(e);
                closeCode = (ushort)CloseStatusCode.Abnormal;
                closeReason = e.Message;
            }
            finally
            {
                ms.Close();
            }

            HandleClose(closeCode, closeReason);

            Log("Receive Task End !");
        }

        private void SocketDispose()
        {
            Log("Dispose");
            WebSocketManager.Instance.Remove(this);
            CleanSendQueue();
            CleanEventQueue();
            socket.Dispose();
            socket = null;
            cts.Dispose();
            cts = null;
        }

        private void HandleOpen()
        {
            Log("OnOpen");
            eventQueue.Enqueue(new OpenEventArgs());
        }

        private void HandleMessage(Opcode opcode, byte[] rawData)
        {
            Log($"OnMessage, type: {opcode}, size: {rawData.Length}");
            eventQueue.Enqueue(new MessageEventArgs(opcode, rawData));
        }

        private void HandleClose(ushort code, string reason)
        {
            Log($"OnClose, code: {code}, reason: {reason}");
            eventQueue.Enqueue(new CloseEventArgs(code, reason));
        }

        private void HandleError(Exception exception)
        {
            Log("OnError, error: " + exception.Message);
            eventQueue.Enqueue(new ErrorEventArgs(exception.Message));
        }

        internal void Update()
        {
            while (eventQueue.Count > 0 && eventQueue.TryDequeue(out var e))
            {
                if (e is CloseEventArgs)
                {
                    OnClose?.Invoke(this, e as CloseEventArgs);
                    SocketDispose();
                    break;
                }
                else if (e is OpenEventArgs)
                {
                    OnOpen?.Invoke(this, e as OpenEventArgs);
                }
                else if (e is MessageEventArgs)
                {
                    OnMessage?.Invoke(this, e as MessageEventArgs);
                }
                else if (e is ErrorEventArgs)
                {
                    OnError?.Invoke(this, e as ErrorEventArgs);
                }
            }
        }

        internal void Abort()
        {
            Log("Abort");
            if (cts != null)
            {
                cts.Cancel();
            }
        }

        [System.Diagnostics.Conditional("UNITY_WEB_SOCKET_LOG")]
        static void Log(string msg)
        {
            var time = DateTime.Now.ToString("HH:mm:ss.fff");
            var thread = Thread.CurrentThread.ManagedThreadId;
            UnityEngine.Debug.Log($"[{time}][UnityWebSocket][T-{thread:D3}] {msg}");
        }
    }
}
#endif
