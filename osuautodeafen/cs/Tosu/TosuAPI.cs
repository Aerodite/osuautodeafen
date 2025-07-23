using System;
using System.Collections.Generic;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using osuautodeafen.cs.StrainGraph;

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
    private int _beatmapId;
    private int _beatmapSetId;
    private BreakPeriod _breakPeriod = null!;
    private string _client;
    private double _combo;
    private double _completionPercentage;
    private double _current;
    private double _currentPP;
    private double _DTRate;
    private double _firstObj;
    private double _full;
    private string? _fullPath;
    private double _fullSR;
    private string? _gameDirectory;
    private JsonElement _graphData;
    private bool _isBreakPeriod;
    public bool _isKiai;
    private string? _lastBeatmapChecksum = "abcdefghijklmnop";
    private int _lastBeatmapId = -1;
    private double? _lastBpm;
    private double? _lastCompletionPercentage;
    private bool _lastKiaiValue;
    private string _lastModNames = "";
    private double _lastModNumber = -1;
    private double _maxCombo;
    private double _maxPP;
    private double _missCount;
    private string? _modNames;
    private int _modNumber;
    private double _oldRateAdjustRate;
    private string? _osuFilePath = "";
    private int _previousState = -10;
    private double _rankedStatus;
    private int _rawBanchoStatus = -1;
    private double _rawCompletionPercentage;
    private double _sbCount;
    private string _server;
    private string? _settingsSongsDirectory;
    private string? _songFilePath;
    private ClientWebSocket _webSocket;
    private string? beatmapArtist;
    private string beatmapChecksum;
    private string? beatmapTitle;
    private double? realtimeBpm;

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

    public bool? isWebsocketConnected => _webSocket.State == WebSocketState.Open;


    private GraphData Graph { get; } = null!;

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

    public event EventHandler? HasKiaiChanged;

    public event Action? BeatmapChanged;

    public event Action? HasModsChanged;

    public event Action? HasBPMChanged;

    public event Action? HasRateChanged;

    public event Action? HasStateChanged;

    public event Action? HasPercentageChanged;

    public event Action<GraphData>? GraphDataUpdated;


    public event Action<int>? StateChanged;

    //<summary>
    // callback for the reconnect timer
    // this will attempt to reconnect to the websocket if the connection is lost
    //</summary>
    private async void ReconnectTimerCallback(object? state)
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

    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        Console.WriteLine("Connecting to WebSocket...");
        var counterPath = "osuautodeafen";
        var uriWithParam = $"{WebSocketUri}?l={Uri.EscapeDataString(counterPath)}";

        while (!cancellationToken.IsCancellationRequested)
        {
            if (_webSocket != null && _webSocket.State == WebSocketState.Open)
                return;

            try
            {
                _webSocket?.Dispose();
                _webSocket = new ClientWebSocket();
                await _webSocket.ConnectAsync(new Uri(uriWithParam), cancellationToken);
                Console.WriteLine("Connected to WebSocket.");
                _reconnectTimer.Change(300000, Timeout.Infinite);
                await ReceiveAsync();
                return;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to connect: {ex.Message}. Retrying in 2 seconds...");
                TosuLauncher.EnsureTosuRunning();
                await Task.Delay(2000, cancellationToken);
            }
        }
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
                            if (status.TryGetProperty("number", out var statusNumber))
                                _rankedStatus = statusNumber.GetDouble();

                        if (beatmap.TryGetProperty("id", out var beatmapId))
                            _beatmapId = beatmapId.GetInt32();
                        if (beatmap.TryGetProperty("set", out var beatmapSetId))
                            _beatmapSetId = beatmapSetId.GetInt32();
                        if (beatmap.TryGetProperty("checksum", out var checksum))
                            beatmapChecksum = checksum.GetString() ?? throw new InvalidOperationException();
                        if (beatmap.TryGetProperty("stats", out var beatmapStatistics) &&
                            beatmapStatistics.TryGetProperty("bpm", out var bpm) &&
                            bpm.TryGetProperty("realtime", out var realtime))
                        {
                            if (realtime.ValueKind == JsonValueKind.Number)
                                realtimeBpm = realtime.GetDouble();
                            else if (realtime.ValueKind == JsonValueKind.String)
                                if (double.TryParse(realtime.GetString(), out var bpmValue))
                                    realtimeBpm = bpmValue;
                                else
                                    realtimeBpm = 0;
                        }

                        if (beatmap.TryGetProperty("isBreak", out var isBreak)) _isBreakPeriod = isBreak.GetBoolean();
                        if (beatmap.TryGetProperty("isKiai", out var isKiai)) _isKiai = isKiai.GetBoolean();
                        if (beatmap.TryGetProperty("title", out var title))
                            beatmapTitle = title.GetString();
                        if (beatmap.TryGetProperty("artistUnicode", out var artist))
                            beatmapArtist = artist.GetString();

                        if (beatmap.TryGetProperty("stats", out var stats))
                            if (stats.TryGetProperty("maxCombo", out var maxCombo))
                                _maxCombo = maxCombo.GetDouble();
                    }

                    if (root.TryGetProperty("play", out var play))
                    {
                        if (play.TryGetProperty("combo", out var combo))
                            if (combo.TryGetProperty("current", out var currentCombo))
                                _combo = currentCombo.GetDouble();
                        if (play.TryGetProperty("pp", out var pp))
                            if (pp.TryGetProperty("current", out var currentPP))
                                _currentPP = currentPP.GetDouble();
                        //if (combo.TryGetProperty("max", out var maxCombo)) _maxCombo = maxCombo.GetDouble();
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
                            if (mods.TryGetProperty("number", out var modNumber)) _modNumber = modNumber.GetInt32();
                        }


                        if (root.TryGetProperty("performance", out var performance))
                            if (performance.TryGetProperty("graph", out var graphs))
                            {
                                ParseGraphData(graphs);
                                _graphData = graphs;
                            }

                        if (root.TryGetProperty("server", out var server))
                            _server = server.GetString();
                        if (root.TryGetProperty("client", out var client))
                            _client = client.GetString();
                        if (performance.TryGetProperty("accuracy", out var accuracy))
                            if (accuracy.TryGetProperty("100", out var ssElement))
                            {
                                var ss = ssElement.GetDouble();
                                _maxPP = ss;
                            }

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
                }
                catch (JsonReaderException ex)
                {
                    Console.WriteLine($@"Failed to parse JSON: {ex.Message}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($@"An error occurred in the TosuAPI: {ex.Message}");
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

    public double GetCurrentPP()
    {
        if (_currentPP < 0)
            return 0;
        return _currentPP;
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

        //Console.WriteLine($"Completion Percentage: {_completionPercentage}");
        return _completionPercentage;
    }

    public string GetServer()
    {
        return _server ?? "Unknown Server";
    }

    public string GetClient()
    {
        return _client ?? "Unknown Client";
    }

    public int GetCurrentTime()
    {
        if (_current < _firstObj)
            return 0;
        if (_current > _full)
            return (int)_full;
        return (int)_current;
    }

    public int GetFullTime()
    {
        if (_full < _firstObj)
            return 0;
        return (int)_full;
    }


    public double GetFullSR()
    {
        return _fullSR;
    }

    public double GetCurrentBpm()
    {
        if (realtimeBpm.HasValue)
            return realtimeBpm.Value;
        return 0;
    }

    public string? GetBeatmapTitle()
    {
        return beatmapTitle;
    }

    public string? GetBeatmapArtist()
    {
        return beatmapArtist;
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
        if (_modNames == null || _modNames.Length == 0)
        {
            _modNames = "NM";
            return _modNames;
        }

        return _modNames;
    }

    // This probably shouldn't be used, instead i'd lean towards using GetRateAdjustRate()
    // Which justs return 1.00 if DT or NC aren't selected.
    [Obsolete]
    public bool? IsDTSelected()
    {
        GetSelectedMods();
        if (_modNames != null)
            if (_modNames.Contains("DT") || _modNames.Contains("NC"))
                return true;

        return false;
    }

    public double GetRateAdjustRate()
    {
        return _DTRate;
    }

    public int GetRawBanchoStatus()
    {
        return _rawBanchoStatus;
    }

    public int GetBeatmapId()
    {
        return _beatmapId;
    }

    public int GetBeatmapSetId()
    {
        return _beatmapSetId;
    }

    public int GetModNumber()
    {
        return _modNumber;
    }

    // Alternative method to GetBeatmapId()
    // (probably for use in the case of an unsubmitted map, as they have an ID of 0 in the Tosu API)
    public string GetBeatmapChecksum()
    {
        return beatmapChecksum;
    }

    public bool IsBreakPeriod()
    {
        return _isBreakPeriod;
    }

    public bool IsKiai()
    {
        return _isKiai;
    }

    /*
     This isn't really needed thanks to GetBeatmapId(),
     which should be used instead of recalculating the whole graph again to see if the map changed
     (thanks tosu for finally implementing that in the api :D)
    */
    [Obsolete]
    private bool HasGraphDataChanged(GraphData newGraph)
    {
        // Checks if the X-axis of the graph has changed at all,
        // if so, return true, else return false.

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

    public void CheckForBeatmapChange()
    {
        var checksum = GetBeatmapChecksum();
        if (string.IsNullOrEmpty(checksum) || checksum == _lastBeatmapChecksum)
            return;
        _lastBeatmapChecksum = checksum;
        var handler = BeatmapChanged;
        handler?.Invoke();
    }

    public void CheckForStateChange()
    {
        if (_rawBanchoStatus == _previousState)
            return;
        _previousState = _rawBanchoStatus;
        var handler = HasStateChanged;
        handler?.Invoke();
        StateChanged?.Invoke(_rawBanchoStatus);
    }

    public void CheckForKiaiChange()
    {
        if (_isKiai == _lastKiaiValue)
            return;
        _lastKiaiValue = _isKiai;
        var handler = HasKiaiChanged;
        handler?.Invoke(this, EventArgs.Empty);
    }

    public void CheckForPercentageChange()
    {
        var percentage = GetCompletionPercentage();
        if (_lastCompletionPercentage.HasValue && percentage == _lastCompletionPercentage.Value)
            return;
        _lastCompletionPercentage = percentage;
        HasPercentageChanged?.Invoke();
    }

    // This is exclusively used for the Background toggle, because it can't exactly check
    // for a different checksum if its not checking for beatmaps in the first place 🤯
    public void ForceBeatmapChange()
    {
        _lastBeatmapChecksum = "abcdefghijklmnop";
        BeatmapChanged?.Invoke();
    }

    public void CheckForModChange()
    {
        if (_modNames == _lastModNames)
            return;
        _lastModNames = _modNames ?? string.Empty;
        var handler = HasModsChanged;
        handler?.Invoke();
        Console.WriteLine($"Mods changed to: {_modNames}");
    }

    public void CheckForRateAdjustChange()
    {
        var rate = GetRateAdjustRate();
        if (rate == _oldRateAdjustRate)
            return;
        _oldRateAdjustRate = rate;
        var handler = HasRateChanged;
        handler?.Invoke();
    }

    public void CheckForBPMChange()
    {
        var bpm = GetCurrentBpm();
        if (_lastBpm.HasValue && bpm == _lastBpm.Value) return;
        _lastBpm = bpm;
        var handler = HasBPMChanged;
        handler?.Invoke();
        Console.WriteLine($"BPM changed to: {bpm}");
    }

    public GraphData? GetGraphData()
    {
        try
        {
            return ParseGraphData(_graphData);
        }
        catch
        {
            return null;
        }
    }

    public bool IsFullCombo()
    {
        // if there are any misses or slider breaks, return false
        if (GetMissCount() > 0 || GetSBCount() > 0) return false;
        // if there are no misses and no slider breaks, return true
        return true;
    }

    public void RaiseKiaiChanged()
    {
        HasKiaiChanged?.Invoke(this, EventArgs.Empty);
    }

    private GraphData? ParseGraphData(JsonElement graphElement)
    {
        try
        {
            var newGraph = new GraphData
            {
                Series = new List<Series>(),
                XAxis = new List<double>()
            };

            if (graphElement.TryGetProperty("series", out var seriesArray))
            {
                var seriesCount = seriesArray.GetArrayLength();
                newGraph.Series = new List<Series>(seriesCount);

                foreach (var seriesElement in seriesArray.EnumerateArray())
                {
                    var seriesName = seriesElement.GetProperty("name").GetString();
                    if (seriesName == "flashlight" || seriesName == "aimNoSliders") continue;

                    if (seriesElement.TryGetProperty("data", out var dataArray))
                    {
                        var dataCount = dataArray.GetArrayLength();
                        var data = new List<double>(dataCount);

                        foreach (var dataElement in dataArray.EnumerateArray())
                            if (dataElement.ValueKind == JsonValueKind.Number)
                                data.Add(dataElement.GetDouble());

                        newGraph.Series.Add(new Series
                        {
                            Name = seriesName,
                            Data = data
                        });
                    }
                }
            }

            if (graphElement.TryGetProperty("xaxis", out var xAxisArray))
            {
                var xCount = xAxisArray.GetArrayLength();
                newGraph.XAxis = new List<double>(xCount);

                foreach (var xElement in xAxisArray.EnumerateArray())
                    if (xElement.ValueKind == JsonValueKind.Number)
                        newGraph.XAxis.Add(xElement.GetDouble());
            }

            return newGraph;
        }
        catch
        {
            return null;
        }
    }
}