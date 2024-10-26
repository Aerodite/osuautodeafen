using System;
using System.Collections.Generic;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace osuautodeafen.cs;

public class BreakPeriod
{
    private static readonly object ConnectionLock = new();

    private readonly string _osuFilePath;
    private readonly Timer _reconnectTimer;
    private readonly TosuApi _tosuApi;
    private MainWindow _mainWindow;
    private ClientWebSocket? _webSocket;

    public BreakPeriod(TosuApi tosuApi)
    {
        Console.WriteLine("Initializing BreakPeriod...");
        _osuFilePath = tosuApi.GetOsuFilePath();
        _tosuApi = tosuApi;
        _webSocket = new ClientWebSocket();
        _reconnectTimer = new Timer(ReconnectTimerCallback, null, Timeout.Infinite, 300000);

        //_ = ConnectAsync();
    }

    public int Start { get; set; }
    public int End { get; set; }

    private void Dispose()
    {
        _reconnectTimer?.Dispose();
        if (_webSocket.State == WebSocketState.Open)
            try
            {
                _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", CancellationToken.None)
                    .GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Exception while closing WebSocket: {ex.Message}");
            }

        _webSocket.Dispose();
    }

    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        Console.WriteLine("Connecting to WebSocket2...");
        while (!cancellationToken.IsCancellationRequested)
            if (_webSocket == null || _webSocket.State != WebSocketState.Open)
                try
                {
                    _webSocket?.Dispose();
                    _webSocket = new ClientWebSocket();
                    var uri = new Uri("ws://127.0.0.1:24050/websocket/v2/files/beatmap/" + _osuFilePath);
                    Console.WriteLine("Connecting to WebSocket2...bf");
                    await _webSocket.ConnectAsync(uri, cancellationToken);
                    Console.WriteLine("Connected to WebSocket2.");
                    _reconnectTimer.Change(300000, Timeout.Infinite);
                    await GetBreakPeriodsAsync();
                    break;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Failed to connect: {ex.Message}. Retrying in 2 seconds...");
                    await Task.Delay(2000, cancellationToken);
                }
            else
                break;
    }

    private async void ReconnectTimerCallback(object state)
    {
        if (_webSocket != null && _webSocket.State == WebSocketState.Open)
            try
            {
                await _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Reconnecting", CancellationToken.None);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error closing WebSocket: {ex.Message}");
            }

        _webSocket?.Dispose();
        _webSocket = new ClientWebSocket();

        try
        {
            Console.WriteLine("Attempting to reconnect...");
            await ConnectAsync();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to reconnect: {ex.Message}");
        }
    }

    public async Task<List<BreakPeriod>> GetBreakPeriodsAsync()
    {
        Console.WriteLine("Getting break periods...");
        var breakPeriods = new List<BreakPeriod>();
        var inEventsSection = false;

        try
        {
            await ConnectAsync();

            var buffer = new byte[4096];
            WebSocketReceiveResult result;
            var stringBuilder = new StringBuilder();

            do
            {
                result = await _webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
                stringBuilder.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
                Console.WriteLine($"Received {result.Count} bytes");
            } while (!result.EndOfMessage);

            var json = stringBuilder.ToString();
            Console.WriteLine("Received data from WebSocket: " + json);

            using (var reader = new StringReader(json))
            {
                string line;
                while ((line = await reader.ReadLineAsync()) != null)
                {
                    Console.WriteLine("Reading line: " + line);
                    if (line.Trim() == "[Events]")
                    {
                        inEventsSection = true;
                        Console.WriteLine("Entered [Events] section");
                        continue;
                    }

                    if (inEventsSection)
                    {
                        if (line.StartsWith("//")) continue; // Skip comments
                        if (line.StartsWith("[") && line.EndsWith("]")) break; // End of [Events] section

                        var parts = line.Split(',');
                        if (parts.Length >= 3 && parts[0] == "2")
                            if (int.TryParse(parts[1], out var start) && int.TryParse(parts[2], out var end))
                            {
                                breakPeriods.Add(new BreakPeriod(_tosuApi) { Start = start, End = end });
                                Console.WriteLine($"Break period found: {start} - {end}");
                            }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"An error occurred: {ex.Message}");
        }

        return breakPeriods;
    }
}