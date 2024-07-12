using System;
using System.Diagnostics;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;
using Timer = System.Timers.Timer;

namespace osuautodeafen.cs
{
    public class TosuApi : IDisposable
    {
        private string _errorMessage = "";
        public event Action<double>? MessageReceived;
        private ClientWebSocket _webSocket;
        private Timer _timer;
        private const string WebSocketUri = "ws://127.0.0.1:24050/websocket/v2";

        public TosuApi()
        {
            _webSocket = new ClientWebSocket();
            _timer = new Timer(500);
            _timer.Elapsed += async (sender, e) => await ConnectAsync();
            _timer.Start();
        }

        public string GetErrorMessage()
        {
            return _errorMessage;
        }

        public async Task ConnectAsync()
        {
            if (_webSocket.State != WebSocketState.Open)
            {
                try
                {
                    await _webSocket.ConnectAsync(new Uri(WebSocketUri), CancellationToken.None);
                    await ReceiveAsync(); // Start receiving messages
                }
                catch (Exception ex)
                {
                    _errorMessage = $"Failed to connect: {ex.Message}";
                }
            }
        }
        private async Task ReceiveAsync()
        {
            var buffer = new byte[1024 * 4];
            while (_webSocket.State == WebSocketState.Open)
            {
                var result = await _webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
                if (result.MessageType == WebSocketMessageType.Text)
                {
                    var message = Encoding.UTF8.GetString(buffer, 0, result.Count);
                    // Process the message
                }
                else if (result.MessageType == WebSocketMessageType.Close)
                {
                    await _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, string.Empty, CancellationToken.None);
                }
            }
        }

        public void Dispose()
        {
            _timer.Dispose();
            if (_webSocket != null)
            {
                if (_webSocket.State == WebSocketState.Open)
                {
                    _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", CancellationToken.None).Wait();
                }
                _webSocket.Dispose();
            }
        }
    }
}