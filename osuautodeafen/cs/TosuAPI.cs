using System;
using System.Collections.Generic;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using JsonException = System.Text.Json.JsonException;

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
    private double _DTRate;
    private double _firstObj;
    private double _full;
    private string? _fullPath;
    private double _fullSR;
    private string? _gameDirectory;
    private double _maxCombo;
    private double _maxPP;
    private double _missCount;
    private string? _modNames;
    private string? _osuFilePath = "";
    private double _rankedStatus;
    private int _rawBanchoStatus = -1; // Default to -1 to indicate uninitialized
    private double _rawCompletionPercentage;
    private double _sbCount;
    private string? _settingsSongsDirectory;
    private string? _songFilePath;
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
                    if (root.TryGetProperty("state", out var state))
                        if (state.TryGetProperty("number", out var number))
                            _rawBanchoStatus = number.GetInt32();
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

                        if (play.TryGetProperty("mods", out var mods))
                        {
                            if (mods.TryGetProperty("name", out var modNames)) _modNames = modNames.GetString();
                            if (mods.TryGetProperty("rate", out var rate)) _DTRate = rate.GetDouble();
                        }
                    }

                    if (root.TryGetProperty("performance", out var performance))
                        if (performance.TryGetProperty("graph", out var graphs))
                            ParseGraphData(graphs);

                    if (root.TryGetProperty("profile", out var profile))
                        if (profile.TryGetProperty("banchoStatus", out var banchoStatus))
                            if (banchoStatus.TryGetProperty("number", out var banchoStatusNumber))
                                //using tosu beta b0bf580 for lazer this does not return the correct status
                                //hoping is fixed later by tosu devs
                                //if not we might just want to return local status as well?
                                //which would be possible by grabbing profile > userStatus > number
                                //instead of profile > banchoStatus > number)
                                //var rawBanchoStatus = banchoStatusNumber.GetInt32();
                                //StateChanged?.Invoke(rawBanchoStatus);
                                //_rawBanchoStatus = rawBanchoStatus;
                                //if (rawBanchoStatus == 2)
                            {
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
            return double.NaN;

        if (_current < _firstObj)
            _completionPercentage = 0;
        else if (_current > _full)
            _completionPercentage = 100;
        else
            _completionPercentage = (_current - _firstObj) / (_full - _firstObj) * 100;

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

    public string? GetFullFilePath()
    {
        return _settingsSongsDirectory + "\\" + _osuFilePath;
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

    public string? GetSelectedMods()
    {
        return _modNames;
    }

    public bool? IsDTSelected()
    {
        GetSelectedMods();
        if (_modNames != null)
            if (_modNames.Contains("DT") || _modNames.Contains("NC"))
                return true;

        return false;
    }

    public double RateAdjustRate()
    {
        return _DTRate;
    }

    public int GetRawBanchoStatus()
    {
        return _rawBanchoStatus;
    }

    private bool HasGraphDataChanged(GraphData newGraph)
    {
        if (Graph?.Series == null) return true;

        if (Graph.Series.Count != newGraph.Series.Count) return true;

        for (var i = 0; i < Graph.Series.Count; i++)
        {
            var currentSeries = Graph.Series[i];
            var newSeries = newGraph.Series[i];

            if (currentSeries.Name != newSeries.Name) return true;

            if (currentSeries.Data.Count != newSeries.Data.Count) return true;

            for (var j = 0; j < currentSeries.Data.Count; j++)
                if (Math.Abs(currentSeries.Data[j] - newSeries.Data[j]) > 0.05)
                    return true;
        }

        if (Graph.XAxis.Count != newGraph.XAxis.Count) return true;

        for (var i = 0; i < Graph.XAxis.Count; i++)
            if (Math.Abs(Graph.XAxis[i] - newGraph.XAxis[i]) > 0.05)
                return true;

        return false;
    }

    private void ParseGraphData(JsonElement graphElement, double? dtRate = null)
    {
        try
        {
            var newGraph = new GraphData
            {
                Series = new List<Series>(),
                XAxis = new List<double>()
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
                        foreach (var dataElement in dataArray.EnumerateArray())
                            if (dataElement.ValueKind == JsonValueKind.Number)
                            {
                                var value = dataElement.GetDouble();
                                series.Data.Add(value);
                            }
                            else
                            {
                                Console.WriteLine($"Invalid data value kind: {dataElement.ValueKind}");
                            }
                    else
                        Console.WriteLine("Data property not found in series element.");

                    newGraph.Series.Add(series);
                }
            else
                Console.WriteLine("Series property not found in graph element.");

            if (graphElement.TryGetProperty("xaxis", out var xAxisArray))
                foreach (var xElement in xAxisArray.EnumerateArray())
                    if (xElement.ValueKind == JsonValueKind.Number)
                    {
                        var xValue = xElement.GetDouble();
                        newGraph.XAxis.Add(xValue);
                    }
                    else
                    {
                        Console.WriteLine($"Invalid x-axis value kind: {xElement.ValueKind}");
                    }
            else
                Console.WriteLine("X-Axis property not found in graph element.");

            var data = new double[newGraph.XAxis.Count];
            foreach (var series in newGraph.Series)
                for (var i = 0; i < data.Length && i < series.Data.Count; i++)
                    data[i] += series.Data[i];

            if (HasGraphDataChanged(newGraph))
            {
                Graph = newGraph;
                GraphDataUpdated?.Invoke(Graph);
            }
        }
        catch (JsonException ex)
        {
            Console.WriteLine($"JSON parsing error: {ex.Message}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"An error occurred while parsing graph data: {ex.Message}");
        }
    }
}