using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace osuautodeafen.cs;

public class TosuApi : IDisposable
{
    private const string WebSocketUri = "ws://127.0.0.1:24050/websocket/v2";
    private static readonly object ConnectionLock = new();
    private readonly List<byte> _dynamicBuffer;
    private readonly string _errorMessage = "";
    private readonly StringBuilder _messageAccumulator = new();
    private readonly Timer _reconnectTimer;
    private readonly Timer _timer;
    private BreakPeriod _breakPeriod;
    private double _combo;
    private double _completionPercentage;
    private double _current;
    private double _firstObj;
    private double _full;
    private string? _fullPath;
    private double _fullSR;
    private string? _gameDirectory;
    private double _maxCombo;
    private double _maxPP;
    private double _missCount;
    private string? _osuFilePath = "";
    private double _rankedStatus;
    private int _rawBanchoStatus = -1; // Default to -1 to indicate uninitialized
    private double _sbCount;
    private string? _settingsSongsDirectory;
    private ClientWebSocket _webSocket;

    public TosuApi()
    {
        _timer = new Timer(async _ => await ConnectAsync(), null, Timeout.Infinite, Timeout.Infinite);
        _webSocket = new ClientWebSocket();
        _dynamicBuffer = new List<byte>();
        _reconnectTimer = new Timer(ReconnectTimerCallback, null, Timeout.Infinite, 300000);
        TosuLauncher.EnsureTosuRunning();
        lock (ConnectionLock)
        {
            _ = ConnectAsync();
        }
    }


    public GraphData Graph { get; private set; }

    //<summary>
    // Closes and tidies up for websocket closure
    //</summary>
    public void Dispose()
    {
        _reconnectTimer?.Dispose();
        _timer?.Dispose();
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

    public event Action<GraphData>? GraphDataUpdated;


    public event Action<int>? StateChanged;

    //<summary>
    // callback for the reconnect timer
    // this will attempt to reconnect to the websocket if the connection is lost
    //</summary>
    private async void ReconnectTimerCallback(object state)
    {
        if (_rawBanchoStatus != 2)
        {
            if (_webSocket.State == WebSocketState.Open)
                try
                {
                    await _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Reconnecting",
                        CancellationToken.None);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($@"Error closing WebSocket: {ex.Message}");
                }

            _webSocket?.Dispose();
            _webSocket = new ClientWebSocket();

            try
            {
                Console.WriteLine(@"Attempting to reconnect...");
                await ConnectAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($@"Failed to reconnect: {ex.Message}");
            }
        }
    }

    public string GetErrorMessage()
    {
        return _errorMessage;
    }

    //<summary>
    // attempts to connect to the websocket along with assuring Tosu is running
    //</summary>
    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        Console.WriteLine(@"Connecting to WebSocket...");
        while (!cancellationToken.IsCancellationRequested)
            if (_webSocket == null || _webSocket.State != WebSocketState.Open)
                try
                {
                    _webSocket?.Dispose();
                    _webSocket = new ClientWebSocket();
                    await _webSocket.ConnectAsync(new Uri(WebSocketUri), cancellationToken);
                    Console.WriteLine(@"Connected to WebSocket.");
                    // this reconnects every 5 minutes incase tosu does that dumb stuff where it
                    // doesn't want to refresh data anymore (which is quite frankly annoying as fuck).
                    _reconnectTimer.Change(300000, Timeout.Infinite);
                    await ReceiveAsync();
                    break;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($@"Failed to connect: {ex.Message}. Retrying in 2 seconds...");
                    await Task.Delay(2000, cancellationToken);
                    TosuLauncher.EnsureTosuRunning();
                }
            else
                break;
    }

    //<summary>
    // Receives data from the WebSocket and parses it
    //</summary>
    public async Task<JsonDocument?> ReceiveAsync()
    {
        const int bufferSize = 4096;
        var buffer = new byte[bufferSize];
        var memoryBuffer = new Memory<byte>(buffer);
        ValueWebSocketReceiveResult result;

        while (_webSocket.State == WebSocketState.Open)
        {
            _dynamicBuffer.Clear(); // Ensure the buffer is clear before receiving new data
            do
            {
                result = await _webSocket.ReceiveAsync(memoryBuffer, CancellationToken.None);
                _dynamicBuffer.AddRange(new ArraySegment<byte>(buffer, 0, result.Count));
            } while (!result.EndOfMessage);

            if (result.EndOfMessage)
            {
                try
                {
                    _messageAccumulator.Append(Encoding.UTF8.GetString(_dynamicBuffer.ToArray(), 0,
                        _dynamicBuffer.Count));
                    var completeMessage = _messageAccumulator.ToString();
                    var root = JsonDocument.Parse(completeMessage).RootElement;
                    var jsonString =
                        await Task.FromResult(
                            "{ \"key\": \"value\" }");

                    ////////////////////////////////////////////////////////////////////
                    if (root.TryGetProperty("stats", out var stats))
                        if (stats.TryGetProperty("pp", out var pp))
                            if (pp.TryGetProperty("fc", out var fc))
                                _maxPP = fc.GetDouble();

                    if (root.TryGetProperty("beatmap", out var beatmap))
                    {
                        if (beatmap.TryGetProperty("time", out var time))
                        {
                            if (time.TryGetProperty("live", out var live)) _current = live.GetDouble();

                            if (time.TryGetProperty("firstObject", out var firstObject))
                                _firstObj = firstObject.GetDouble();

                            if (time.TryGetProperty("lastObject", out var lastObject)) _full = lastObject.GetDouble();
                        }

                        if (beatmap.TryGetProperty("stats", out var bmstats))
                            if (bmstats.TryGetProperty("stars", out var stars))
                                if (stars.TryGetProperty("total", out var totalSR))
                                    _fullSR = totalSR.GetDouble();

                        if (beatmap.TryGetProperty("status", out var status))
                            if (beatmap.TryGetProperty("number", out var statusNumber))
                                _rankedStatus = statusNumber.GetDouble();
                    }

                    if (root.TryGetProperty("play", out var play))
                    {
                        if (play.TryGetProperty("combo", out var combo))
                        {
                            if (combo.TryGetProperty("current", out var currentCombo))
                                _combo = currentCombo.GetDouble();

                            if (combo.TryGetProperty("max", out var maxCombo)) _maxCombo = maxCombo.GetDouble();
                        }

                        if (play.TryGetProperty("hits", out var hits))
                        {
                            if (hits.ValueKind == JsonValueKind.Object &&
                                hits.TryGetProperty("0", out var missElement))
                                if (missElement.ValueKind == JsonValueKind.Number)
                                {
                                    _missCount = missElement.GetDouble();
                                    if (_missCount > 0)
                                    {
                                        //Console.WriteLine($"Miss count: {_missCount}");
                                    }
                                }

                            if (hits.ValueKind == JsonValueKind.Object &&
                                hits.TryGetProperty("sliderBreaks", out var sbElement))
                                if (sbElement.ValueKind == JsonValueKind.Number)
                                {
                                    _sbCount = sbElement.GetDouble();
                                    if (_sbCount > 0)
                                    {
                                        //Console.WriteLine($"Slider break count: {_sbCount}");
                                    }
                                }
                        }
                    }

                    if (root.TryGetProperty("performance", out var performance))
                        if (performance.TryGetProperty("graph", out var graphs))
                            ParseGraphData(graphs);

                    if (root.TryGetProperty("profile", out var profile))
                        if (profile.TryGetProperty("banchoStatus", out var banchoStatus))
                            if (banchoStatus.TryGetProperty("number", out var banchoStatusNumber))
                            {
                                //using tosu beta b0bf580 for lazer this does not return the correct status
                                //hoping is fixed later by tosu devs
                                //if not we might just want to return local status as well?
                                //which would be possible by grabbing profile > userStatus > number
                                //instead of profile > banchoStatus > number)
                                var rawBanchoStatus = banchoStatusNumber.GetInt32();
                                StateChanged?.Invoke(rawBanchoStatus);
                                _rawBanchoStatus = rawBanchoStatus;
                                if (rawBanchoStatus == 2)
                                {
                                }
                            }

                    if (root.TryGetProperty("folders", out var folders) &&
                        folders.TryGetProperty("songs", out var songs))
                        _settingsSongsDirectory = songs.GetString();
                    folders.TryGetProperty("game", out var game);
                    _gameDirectory = game.GetString();

                    if (root.TryGetProperty("directPath", out var directPath) &&
                        directPath.TryGetProperty("beatmapBackground", out var beatmapBackground))
                        _fullPath = beatmapBackground.GetString();
                    var combinedPath = _settingsSongsDirectory + "\\" + _fullPath;

                    if (directPath.TryGetProperty("beatmapFile", out var beatmapFile))
                        _osuFilePath = beatmapFile.GetString();

                    var jsonDocument = JsonDocument.Parse(jsonString);
                    var rootElement = jsonDocument.RootElement;
                }
                catch (JsonReaderException ex)
                {
                    Console.WriteLine($@"Failed to parse JSON: {ex.Message}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($@"An error occurred: {ex.Message}");
                }
                finally
                {
                    _messageAccumulator.Clear();
                }
                /////////////////////////////////////////////////////////////////

                if (result.MessageType == WebSocketMessageType.Close)
                    await _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, string.Empty,
                        CancellationToken.None);
                _dynamicBuffer.Clear();
            }
        }

        return null;
    }

    public double GetCompletionPercentage()
    {
        if (_full == 0)
            //Console.WriteLine("completion percent = Undefined (division by zero)");
            return double.NaN;

        if (_current < _firstObj)
        {
            //Console.WriteLine("completion percent = 0");
            _completionPercentage = 0;
        }
        else
        {
            _completionPercentage = (_current - _firstObj) / _full * 100;
            _completionPercentage = Math.Round(_completionPercentage, 2);
            //Console.WriteLine($"completion percent = {_completionPercentage}%");
        }

        return _completionPercentage;
    }


    public double GetFullSR()
    {
        return _fullSR;
    }

    public string? GetOsuFilePath()
    {
        return _osuFilePath;
    }

    public double GetMaxPP()
    {
        return _maxPP;
    }

    public double GetCombo()
    {
        return _combo;
    }

    public double GetMaxCombo()
    {
        return _maxCombo;
    }

    public double GetMissCount()
    {
        return _missCount;
    }

    public double GetSBCount()
    {
        return _sbCount;
    }

    public double GetRankedStatus()
    {
        return _rankedStatus;
    }

    public string GetBackgroundPath()
    {
        return _settingsSongsDirectory + "\\" + _fullPath;
    }

    public string? GetGameDirectory()
    {
        return _gameDirectory;
    }

    private bool IsYAxisChanged(List<Series> newSeries)
    {
        if (Graph?.Series == null) return true;

        if (Graph.Series.Count != newSeries.Count) return true;

        for (var i = 0; i < Graph.Series.Count; i++)
        {
            var currentSeries = Graph.Series[i];
            var newSeriesData = newSeries[i].Data;

            if (currentSeries.Data.Count != newSeriesData.Count) return true;

            for (var j = 0; j < currentSeries.Data.Count; j++)
                if (Math.Abs(currentSeries.Data[j] - newSeriesData[j]) > 0.01)
                    return true;
        }

        return false;
    }

    //<summary>
    // organizes the array data from tosu
    // also this was a massive nightmare :)
    //</summary>
    private void ParseGraphData(JsonElement graphElement)
    {
        try
        {
            var newGraph = new GraphData
            {
                Series = new List<Series>(),
                XAxis = new List<int>()
            };

            if (graphElement.TryGetProperty("series", out var seriesArray))
                foreach (var seriesElement in seriesArray.EnumerateArray())
                {
                    var seriesName = seriesElement.GetProperty("name").GetString();
                    if (seriesName == "flashlight" || seriesName == "aimNoSliders") continue;

                    var series = new Series
                    {
                        Name = seriesName,
                        Data = new List<double>()
                    };

                    if (seriesElement.TryGetProperty("data", out var dataArray))
                    {
                        foreach (var dataElement in dataArray.EnumerateArray())
                            if (dataElement.ValueKind == JsonValueKind.Number)
                            {
                                var value = dataElement.GetDouble();
                                if (Math.Abs(value - (-100)) > 0.01) series.Data.Add(value);
                            }
                    }
                    else
                    {
                        Console.WriteLine(@"Data property not found in series element.");
                    }

                    newGraph.Series.Add(series);
                }
            else
                Console.WriteLine(@"Series property not found in graph element.");

            if (graphElement.TryGetProperty("xaxis", out var xAxisArray))
            {
                foreach (var xElement in xAxisArray.EnumerateArray())
                    if (xElement.ValueKind == JsonValueKind.Number)
                    {
                        var xValue = xElement.GetInt32();
                        newGraph.XAxis.Add(xValue);
                    }
            }
            else
            {
                Console.WriteLine(@"X-Axis property not found in graph element.");
            }

            // Combine channels that represent the beatmap's difficulty
            var data = new double[newGraph.XAxis.Count];
            foreach (var series in newGraph.Series)
                for (var i = 0; i < data.Length && i < series.Data.Count; i++)
                    data[i] += series.Data[i];

            // Count up samples that don't represent intro, breaks, and outro sections
            var percent = data.Max() / 100;
            for (var i = 0; i < data.Length; i++)
            {
                data[i] = Math.Max(0, data[i]);
                if (data[i] > percent)
                {
                }
            }

            if (IsYAxisChanged(newGraph.Series))
            {
                Graph = newGraph;
                GraphDataUpdated?.Invoke(Graph);
            }
            else if (MainWindow.isCompPctLostFocus)
            {
                Graph = newGraph;
                GraphDataUpdated?.Invoke(Graph);
                MainWindow.isCompPctLostFocus = false;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($@"An error occurred while parsing graph data: {ex.Message}");
        }
    }

    private double[] FastSmooth(double[] data, double windowWidth, int smoothness)
    {
        var windowSize = (int)Math.Ceiling(windowWidth * smoothness);
        var smoothedData = new double[data.Length];

        for (var i = 0; i < data.Length; i++)
        {
            var start = Math.Max(0, i - windowSize / 2);
            var end = Math.Min(data.Length - 1, i + windowSize / 2);
            double sum = 0;
            var count = 0;

            for (var j = start; j <= end; j++)
            {
                sum += data[j];
                count++;
            }

            smoothedData[i] = sum / count;
        }

        return smoothedData;
    }
}