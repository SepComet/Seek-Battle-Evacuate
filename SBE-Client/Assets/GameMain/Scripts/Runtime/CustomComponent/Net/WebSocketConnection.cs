using System;
using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;

namespace SepCore.Net
{
    /// <summary>
    /// 基于 System.Net.WebSockets.ClientWebSocket 的连接实现（Windows 编辑器/独立平台可用）。
    /// 收发在后台线程执行，消息与断线事件经 <see cref="Pump"/> 转发到主线程。
    /// </summary>
    public sealed class WebSocketConnection : IServerConnection, IDisposable
    {
        private ClientWebSocket _socket;
        private CancellationTokenSource _cts;
        private Thread _receiveThread;
        private Thread _sendThread;

        private readonly ConcurrentQueue<string> _inbound = new ConcurrentQueue<string>();
        private readonly ConcurrentQueue<string> _outbound = new ConcurrentQueue<string>();
        private readonly AutoResetEvent _sendSignal = new AutoResetEvent(false);
        private readonly StringBuilder _reassembly = new StringBuilder();
        private volatile bool _disconnectRaised;

        public bool IsConnected => _socket != null && _socket.State == WebSocketState.Open;

        public event Action<string> MessageReceived;
        public event Action Disconnected;

        public async UniTask<bool> ConnectAsync(string uri)
        {
            Close();

            // 注意：Unity 的 Mono 档没有 KeepAliveInterval（.NET Core API）；
            // 局域网场景下依赖 TCP 本身保活，服务器侧也未设空闲超时。
            ClientWebSocket socket = new ClientWebSocket();
            _cts = new CancellationTokenSource();

            try
            {
                await socket.ConnectAsync(new Uri(uri), _cts.Token);
            }
            catch (Exception)
            {
                socket.Dispose();
                _cts.Dispose();
                _cts = null;
                return false;
            }

            _socket = socket;
            _disconnectRaised = false;

            _receiveThread = new Thread(ReceiveLoop) { IsBackground = true };
            _receiveThread.Start();
            _sendThread = new Thread(SendLoop) { IsBackground = true };
            _sendThread.Start();
            return true;
        }

        public void Close()
        {
            if (_cts != null)
            {
                try
                {
                    _cts.Cancel();
                }
                catch (ObjectDisposedException)
                {
                }
            }

            if (_socket != null)
            {
                try
                {
                    if (_socket.State == WebSocketState.Open)
                    {
                        _socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "client closing", CancellationToken.None)
                            .ContinueWith(_ => { });
                    }
                }
                catch (Exception)
                {
                }
            }

            _socket = null;
            _cts = null;
            _sendSignal.Set();
        }

        public bool TrySend(string json)
        {
            if (!IsConnected)
            {
                return false;
            }

            _outbound.Enqueue(json);
            _sendSignal.Set();
            return true;
        }

        /// <summary>
        /// 主线程泵送：把后台线程收到的消息与断线事件转发到主线程回调。
        /// </summary>
        public void Pump()
        {
            while (_inbound.TryDequeue(out string message))
            {
                MessageReceived?.Invoke(message);
            }

            if (!_disconnectRaised && _socket != null && _socket.State != WebSocketState.Open)
            {
                _disconnectRaised = true;
                Disconnected?.Invoke();
            }
        }

        private async void ReceiveLoop()
        {
            ClientWebSocket socket = _socket;
            CancellationToken token = _cts.Token;
            byte[] buffer = new byte[16 * 1024];
            _reassembly.Clear();

            try
            {
                while (!token.IsCancellationRequested && socket.State == WebSocketState.Open)
                {
                    WebSocketReceiveResult result =
                        await socket.ReceiveAsync(new ArraySegment<byte>(buffer), token);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        break;
                    }

                    _reassembly.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
                    if (result.EndOfMessage)
                    {
                        _inbound.Enqueue(_reassembly.ToString());
                        _reassembly.Clear();
                    }
                }
            }
            catch (Exception)
            {
                // 断线由 Pump 依据 State 统一上报
            }
        }

        private void SendLoop()
        {
            CancellationToken token = _cts != null ? _cts.Token : CancellationToken.None;
            while (!token.IsCancellationRequested)
            {
                _sendSignal.WaitOne(1000);
                while (_outbound.TryDequeue(out string json))
                {
                    ClientWebSocket socket = _socket;
                    if (socket == null || socket.State != WebSocketState.Open)
                    {
                        return;
                    }

                    try
                    {
                        byte[] bytes = Encoding.UTF8.GetBytes(json);
                        socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, token)
                            .Wait();
                    }
                    catch (Exception)
                    {
                        return;
                    }
                }
            }
        }

        public void Dispose()
        {
            Close();
            _sendSignal.Dispose();
        }
    }
}
