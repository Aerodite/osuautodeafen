using System;
using System.Collections.Generic;
using System.IO;
using System.Net.WebSockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using osuautodeafen.cs.Settings;
using osuautodeafen.cs.StrainGraph;

namespace osuautodeafen.cs.Tosu;

public class TosuApi : IDisposable
{
    private static readonly Lock ConnectionLock = new();
    private readonly List<byte> _dynamicBuffer;
    private readonly string _errorMessage = "";
    private readonly StringBuilder _messageAccumulator = new();
    private readonly Timer _reconnectTimer;
    private readonly Timer _timer;
    private string? _beatmapArtist;
    private string _beatmapChecksum;
    private string _beatmapDifficulty;
    private int _beatmapId;
    private string? _beatmapMapper;
    private int _beatmapSetId;
    private string? _beatmapTitle;
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
    private double? _lastBpm;
    private double? _lastCompletionPercentage;
    private bool _lastKiaiValue;
    private string _lastModNames = "";
    private double _maxCombo;
    private double _maxPlayCombo;
    private double _maxPP;
    private double _missCount;
    private string? _modNames;
    private int _modNumber;
    private double _oldRateAdjustRate;
    private string? _osuFilePath = "";
    private int _previousState = -10;
    private double _rankedStatus;
    private int _rawBanchoStatus = -1;
    private double? _realtimeBpm;
    private double _sbCount;
    private string _server;
    private string? _settingsSongsDirectory;
    private ClientWebSocket _webSocket;

    public TosuApi()
    {
        _timer = new Timer(async void (_) =>
        {
            try
            {
                await ConnectAsync();
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("Failed to connect to Tosu WebSocket: ", ex);
            }
        }, null, Timeout.Infinite, Timeout.Infinite);
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

    /// <summary>
    ///     Disposes the WebSocket and timers when done
    /// </summary>
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

    /// <summary>
    ///     Event triggered when the Kiai state changes
    /// </summary>
    public event EventHandler? HasKiaiChanged;

    /// <summary>
    ///     Event triggered when the beatmap changes
    /// </summary>
    public event Action? BeatmapChanged;

    /// <summary>
    ///     Event triggered when the selected mods change (accounts for lazer)
    /// </summary>
    public event Action? HasModsChanged;

    /// <summary>
    ///     Event triggered when the BPM changes (accounts for poly-rhythm)
    /// </summary>
    public event Action? HasBPMChanged;

    /// <summary>
    ///     Event triggered when the rate changes in mod select (0.50-2.00)
    /// </summary>
    /// <remarks>
    ///     I should hope this accounts for Wind Up / Wind Down too but eh haven't tested
    /// </remarks>
    public event Action? HasRateChanged;

    /// <summary>
    ///     Event triggered when the play state changes (e.g. from downloading a map to playing)
    /// </summary>
    public event Action? HasStateChanged;

    /// <summary>
    ///     Event triggered when the map progress percentage changes
    /// </summary>
    public event Action? HasPercentageChanged;

    /// <summary>
    ///     Event triggered when the raw bancho status changes
    /// </summary>
    public event Action<int>? StateChanged;

    /// <summary>
    ///     Called every 5 minutes to attempt to reconnect if the connection is lost / to refresh the connection in the case
    ///     Tosu stops reporting data
    /// </summary>
    /// <param name="state"></param>
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

    /// <summary>
    ///     Returns the last error message encountered
    /// </summary>
    /// <returns></returns>
    public string GetErrorMessage()
    {
        return _errorMessage;
    }

    /// <summary>
    ///     Connects to the Tosu WebSocket API
    /// </summary>
    /// <param name="cancellationToken"></param>
    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        SettingsHandler settings = new();

        string? ip = settings.tosuApiIp;
        string? port = settings.tosuApiPort;
        string counterPath = "osuautodeafen";
        string uriWithParam = $"ws://{ip}:{port}/websocket/v2?l={Uri.EscapeDataString(counterPath)}";
        Console.WriteLine($"WebSocket URI: {uriWithParam}");
        Console.WriteLine($"WebSocket State: {_webSocket.State}");

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

    /// <summary>
    ///     Receives messages from the WebSocket and processes them into variables that can be used anywhere
    /// </summary>
    /// <returns></returns>
    /// <exception cref="InvalidOperationException"></exception>
    public async Task<JsonDocument?> ReceiveAsync()
    {
        const int bufferSize = 4096;
        byte[] buffer = new byte[bufferSize];
        var memoryBuffer = new Memory<byte>(buffer);
        ValueWebSocketReceiveResult result;

        while (_webSocket.State == WebSocketState.Open)
        {
            _dynamicBuffer.Clear();
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
                    string completeMessage = _messageAccumulator.ToString();
                    JsonElement root = JsonDocument.Parse(completeMessage).RootElement;
                    string jsonString =
                        await Task.FromResult(
                            "{ \"key\": \"value\" }");

                    ////////////////////////////////////////////////////////////////////
                    if (root.TryGetProperty("state", out JsonElement state))
                        if (state.TryGetProperty("number", out JsonElement number))
                            _rawBanchoStatus = number.GetInt32();

                    if (root.TryGetProperty("beatmap", out JsonElement beatmap))
                    {
                        if (beatmap.TryGetProperty("time", out JsonElement time))
                        {
                            if (time.TryGetProperty("live", out JsonElement live)) _current = live.GetDouble();

                            if (time.TryGetProperty("firstObject", out JsonElement firstObject))
                                _firstObj = firstObject.GetDouble();

                            if (time.TryGetProperty("lastObject", out JsonElement lastObject))
                                _full = lastObject.GetDouble();
                        }

                        if (beatmap.TryGetProperty("stats", out JsonElement bmstats))
                            if (bmstats.TryGetProperty("stars", out JsonElement stars))
                                if (stars.TryGetProperty("total", out JsonElement totalSR))
                                    _fullSR = totalSR.GetDouble();

                        if (beatmap.TryGetProperty("status", out JsonElement status))
                            if (status.TryGetProperty("number", out JsonElement statusNumber))
                                _rankedStatus = statusNumber.GetDouble();

                        if (beatmap.TryGetProperty("id", out JsonElement beatmapId))
                            _beatmapId = beatmapId.GetInt32();
                        if (beatmap.TryGetProperty("set", out JsonElement beatmapSetId))
                            _beatmapSetId = beatmapSetId.GetInt32();
                        if (beatmap.TryGetProperty("checksum", out JsonElement checksum))
                            _beatmapChecksum = checksum.GetString() ?? throw new InvalidOperationException();
                        if (beatmap.TryGetProperty("stats", out JsonElement beatmapStatistics) &&
                            beatmapStatistics.TryGetProperty("bpm", out JsonElement bpm) &&
                            bpm.TryGetProperty("realtime", out JsonElement realtime))
                        {
                            if (realtime.ValueKind == JsonValueKind.Number)
                                _realtimeBpm = realtime.GetDouble();
                            else if (realtime.ValueKind == JsonValueKind.String)
                                if (double.TryParse(realtime.GetString(), out double bpmValue))
                                    _realtimeBpm = bpmValue;
                                else
                                    _realtimeBpm = 0;
                        }

                        if (beatmap.TryGetProperty("isBreak", out JsonElement isBreak))
                            _isBreakPeriod = isBreak.GetBoolean();
                        if (beatmap.TryGetProperty("isKiai", out JsonElement isKiai)) _isKiai = isKiai.GetBoolean();
                        if (beatmap.TryGetProperty("title", out JsonElement title))
                            _beatmapTitle = title.GetString();
                        if (beatmap.TryGetProperty("artistUnicode", out JsonElement artist))
                            _beatmapArtist = artist.GetString();
                        if (beatmap.TryGetProperty("version", out JsonElement beatmapDifficulty))
                            _beatmapDifficulty = beatmapDifficulty.GetString() ?? throw new InvalidOperationException();
                        if (beatmap.TryGetProperty("mapper", out JsonElement mapper))
                            _beatmapMapper = mapper.GetString();
                        if (beatmap.TryGetProperty("stats", out JsonElement stats))
                            if (stats.TryGetProperty("maxCombo", out JsonElement maxCombo))
                                _maxCombo = maxCombo.GetDouble();
                    }

                    if (root.TryGetProperty("play", out JsonElement play))
                    {
                        if (play.TryGetProperty("combo", out JsonElement combo))
                            if (combo.TryGetProperty("current", out JsonElement currentCombo))
                                _combo = currentCombo.GetDouble();
                        if (combo.TryGetProperty("max", out JsonElement maxComboElement))
                            if (maxComboElement.ValueKind == JsonValueKind.Number)
                                _maxPlayCombo = maxComboElement.GetDouble();
                        if (play.TryGetProperty("pp", out JsonElement pp))
                            if (pp.TryGetProperty("current", out JsonElement currentPP))
                                _currentPP = currentPP.GetDouble();
                        //if (combo.TryGetProperty("max", out var maxCombo)) _maxCombo = maxCombo.GetDouble();
                        if (play.TryGetProperty("hits", out JsonElement hits))
                        {
                            if (hits.ValueKind == JsonValueKind.Object &&
                                hits.TryGetProperty("0", out JsonElement missElement))
                                if (missElement.ValueKind == JsonValueKind.Number)
                                {
                                    _missCount = missElement.GetDouble();
                                    if (_missCount > 0)
                                    {
                                        //Console.WriteLine($"Miss count: {_missCount}");
                                    }
                                }

                            if (hits.ValueKind == JsonValueKind.Object &&
                                hits.TryGetProperty("sliderBreaks", out JsonElement sbElement))
                                if (sbElement.ValueKind == JsonValueKind.Number)
                                {
                                    _sbCount = sbElement.GetDouble();
                                    if (_sbCount > 0)
                                    {
                                        //Console.WriteLine($"Slider break count: {_sbCount}");
                                    }
                                }
                        }

                        if (play.TryGetProperty("mods", out JsonElement mods))
                        {
                            if (mods.TryGetProperty("name", out JsonElement modNames)) _modNames = modNames.GetString();
                            if (mods.TryGetProperty("rate", out JsonElement rate)) _DTRate = rate.GetDouble();
                            if (mods.TryGetProperty("number", out JsonElement modNumber))
                                _modNumber = modNumber.GetInt32();
                        }


                        if (root.TryGetProperty("performance", out JsonElement performance))
                            if (performance.TryGetProperty("graph", out JsonElement graphs))
                            {
                                ParseGraphData(graphs);
                                _graphData = graphs;
                            }

                        if (root.TryGetProperty("server", out JsonElement server))
                            _server = server.GetString();
                        if (root.TryGetProperty("client", out JsonElement client))
                            _client = client.GetString();
                        if (performance.TryGetProperty("accuracy", out JsonElement accuracy))
                            if (accuracy.TryGetProperty("100", out JsonElement ssElement))
                            {
                                double ss = ssElement.GetDouble();
                                _maxPP = ss;
                            }

                        if (root.TryGetProperty("profile", out JsonElement profile))
                            if (profile.TryGetProperty("banchoStatus", out JsonElement banchoStatus))
                                if (banchoStatus.TryGetProperty("number", out JsonElement banchoStatusNumber))
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


                        if (root.TryGetProperty("folders", out JsonElement folders) &&
                            folders.TryGetProperty("songs", out JsonElement songs))
                            _settingsSongsDirectory = songs.GetString();
                        folders.TryGetProperty("game", out JsonElement game);
                        _gameDirectory = game.GetString();

                        if (root.TryGetProperty("directPath", out JsonElement directPath) &&
                            directPath.TryGetProperty("beatmapBackground", out JsonElement beatmapBackground))
                            _fullPath = beatmapBackground.GetString();
                        string combinedPath = _settingsSongsDirectory + "\\" + _fullPath;

                        if (directPath.TryGetProperty("beatmapFile", out JsonElement beatmapFile))
                            _osuFilePath = beatmapFile.GetString();

                        JsonDocument jsonDocument = JsonDocument.Parse(jsonString);
                        JsonElement rootElement = jsonDocument.RootElement;
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

    /// <summary>
    ///     Gets the current pp value
    /// </summary>
    /// <returns>
    ///     pp value as a double
    /// </returns>
    public double GetCurrentPP()
    {
        if (_currentPP < 0)
            return 0;
        return _currentPP;
    }

    /// <summary>
    ///     Gets the current percentage completed of the beatmap
    /// </summary>
    /// <returns>
    ///     double in the form of 0.* to 100
    /// </returns>
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

    /// <summary>
    ///     Gets the server name from tosu (e.g. "Bancho", "Akatsuki", "Gatari", etc.)
    /// </summary>
    /// <returns>
    ///     Server as a string
    /// </returns>
    /// <remarks>
    ///     Info only seen with the debug console (Ctrl + D)
    /// </remarks>
    public string GetServer()
    {
        return _server ?? "Unknown Server";
    }

    /// <summary>
    ///     Gets the client name from tosu (lazer or stable)
    /// </summary>
    /// <returns>
    ///     Client as a string
    /// </returns>
    /// <remarks>
    ///     Info only seen with the debug console (Ctrl + D)
    /// </remarks>
    public string GetClient()
    {
        return _client ?? "Unknown Client";
    }

    /// <summary>
    ///     Obtains the current time progress of the beatmap in milliseconds
    /// </summary>
    /// <returns></returns>
    public int GetCurrentTime()
    {
        if (_current < _firstObj)
            return 0;
        if (_current > _full)
            return (int)_full;
        return (int)_current;
    }

    /// <summary>
    ///     Obtains the full time length of the beatmap in milliseconds
    /// </summary>
    /// <remarks>
    ///     As far as I can tell this seems to be as accurate as I can get,
    ///     because this uses both the mp3 time and first object to determine the beatmap length.
    /// </remarks>
    public int GetFullTime()
    {
        if (_full < _firstObj)
            return 0;
        return (int)_full;
    }

    /// <summary>
    ///     Obtains the full star rating of the beatmap including mods
    /// </summary>
    /// <returns></returns>
    public double GetFullSR()
    {
        return _fullSR;
    }

    /// <summary>
    ///     Obtains the current BPM of the beatmap, accounting for poly-rhythm changes
    /// </summary>
    /// <returns></returns>
    public double GetCurrentBpm()
    {
        if (_realtimeBpm.HasValue)
            return _realtimeBpm.Value;
        return 0;
    }

    /// <summary>
    ///     Obtains the title of the current beatmap
    /// </summary>
    /// <returns></returns>
    public string? GetBeatmapTitle()
    {
        return _beatmapTitle;
    }

    /// <summary>
    ///     Obtains the artist of the current beatmap
    /// </summary>
    /// <returns></returns>
    public string? GetBeatmapArtist()
    {
        return _beatmapArtist;
    }

    public string GetBeatmapDifficulty()
    {
        return (_beatmapDifficulty ?? "Unknown Difficulty").TrimEnd();
    }

    /// <summary>
    ///     Obtains the osu! file path of the current beatmap
    /// </summary>
    /// <returns></returns>
    public string? GetOsuFilePath()
    {
        return _osuFilePath;
    }

    /// <summary>
    ///     Obtains the full file path of the current beatmap
    /// </summary>
    /// <returns></returns>
// C#
    public string? GetFullFilePath()
    {
        if (_settingsSongsDirectory == null || _osuFilePath == null)
            return null;

        char separator = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? '\\' : '/';
        return _settingsSongsDirectory.TrimEnd('\\', '/') + separator + _osuFilePath.TrimStart('\\', '/');
    }

    /// <summary>
    ///     Obtains the maximum pp value of the current beatmap with selected mods
    /// </summary>
    /// <returns></returns>
    public double GetMaxPP()
    {
        return _maxPP;
    }

    /// <summary>
    ///     Obtains the current combo of the player
    /// </summary>
    /// <returns></returns>
    public double GetCombo()
    {
        return _combo;
    }

    /// <summary>
    ///     Obtains the maximum achievable combo of the current beatmap
    /// </summary>
    /// <returns></returns>
    public double GetMaxCombo()
    {
        return _maxCombo;
    }

    /// <summary>
    ///     Obtains the maximum combo the player has achieved in this current play
    /// </summary>
    /// <remarks>
    ///     Literally the only reason this is here is to avoid that "Deafen Debounce" issue that has been prevalent for a while
    /// </remarks>
    public double GetMaxPlayCombo()
    {
        return _maxPlayCombo;
    }

    /// <summary>
    ///     Obtains the current miss count of the player
    /// </summary>
    /// <returns></returns>
    public double GetMissCount()
    {
        return _missCount;
    }

    /// <summary>
    ///     Obtains the current slider break count of the player
    /// </summary>
    /// <returns></returns>
    public double GetSBCount()
    {
        return _sbCount;
    }

    /// <summary>
    ///     Obtains the status of the current beatmap
    /// </summary>
    /// <returns></returns>
    public double GetRankedStatus()
    {
        return _rankedStatus;
    }

    /// <summary>
    ///     Obtains the background path of the current beatmap
    /// </summary>
    /// <returns></returns>
    public string GetBackgroundPath()
    {
        string songsDir = _settingsSongsDirectory ?? "";
        string fullPath = _fullPath ?? "";

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            string home = Environment.GetEnvironmentVariable("HOME") ?? "";
            if (songsDir == "Songs" || string.IsNullOrEmpty(songsDir))
                songsDir = Path.Combine(home, ".local", "share", "osu-wine", "osu!", "Songs");
            return Path.Combine(songsDir, fullPath.Replace("\\", Path.DirectorySeparatorChar.ToString()));
        }

        //windows is way simpler :skull:
        return songsDir + "\\" + fullPath;
    }

    /// <summary>
    ///     Obtains the game directory of the current osu! installation
    /// </summary>
    /// <returns></returns>
    public string? GetGameDirectory()
    {
        return _gameDirectory;
    }

    /// <summary>
    ///     Gets the selected mods as a string (e.g. "HDDTHR")
    /// </summary>
    /// <returns></returns>
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
    /// <summary>
    ///     Checks if DT or NC is selected in the mods list
    /// </summary>
    /// <returns></returns>
    [Obsolete]
    public bool? IsDTSelected()
    {
        GetSelectedMods();
        if (_modNames != null)
            if (_modNames.Contains("DT") || _modNames.Contains("NC"))
                return true;

        return false;
    }

    /// <summary>
    ///     Gets the rate (0.50-2.00) from mod select
    /// </summary>
    /// <returns></returns>
    public double GetRateAdjustRate()
    {
        return _DTRate;
    }

    /// <summary>
    ///     Gets the raw bancho status
    /// </summary>
    /// <returns></returns>
    public int GetRawBanchoStatus()
    {
        return _rawBanchoStatus;
    }

    /// <summary>
    ///     Gets the beatmap ID of the current beatmap
    /// </summary>
    /// <returns></returns>
    public int GetBeatmapId()
    {
        return _beatmapId;
    }

    /// <summary>
    ///     Gets the beatmap set ID of the current beatmap
    /// </summary>
    /// <returns></returns>
    public int GetBeatmapSetId()
    {
        return _beatmapSetId;
    }

    /// <summary>
    ///     Gets the beatmap mapper of the current beatmap
    /// </summary>
    /// <returns></returns>
    public string GetBeatmapMapper()
    {
        return _beatmapMapper ?? "Unknown Mapper";
    }

    /// <summary>
    ///     Gets the mod number of the current selected mods
    /// </summary>
    /// <returns></returns>
    public int GetModNumber()
    {
        return _modNumber;
    }

    /// <summary>
    ///     Gets the beatmap checksum of the current beatmap (Alternative method to GetBeatmapId())
    /// </summary>
    /// <returns></returns>
    public string GetBeatmapChecksum()
    {
        return _beatmapChecksum;
    }

    /// <summary>
    ///     Checks if the current time is in a break period
    /// </summary>
    /// <returns></returns>
    public bool IsBreakPeriod()
    {
        return _isBreakPeriod;
    }

    /// <summary>
    ///     Checks if the current time is in kiai time
    /// </summary>
    /// <returns></returns>
    public bool IsKiai()
    {
        return _isKiai;
    }

    /// <summary>
    ///     Checks for a beatmap change by comparing the current checksum to the last known checksum
    /// </summary>
    public void CheckForBeatmapChange()
    {
        string checksum = GetBeatmapChecksum();
        if (string.IsNullOrEmpty(checksum) || checksum == _lastBeatmapChecksum)
            return;
        _lastBeatmapChecksum = checksum;
        Action? handler = BeatmapChanged;
        handler?.Invoke();
    }

    /// <summary>
    ///     Checks for a state change by comparing the current raw bancho status to the last known status
    /// </summary>
    public void CheckForStateChange()
    {
        if (_rawBanchoStatus == _previousState)
            return;
        _previousState = _rawBanchoStatus;
        Action? handler = HasStateChanged;
        handler?.Invoke();
        StateChanged?.Invoke(_rawBanchoStatus);
    }

    /// <summary>
    ///     Checks for a kiai change by comparing the current kiai state to the last known state
    /// </summary>
    public void CheckForKiaiChange()
    {
        if (_isKiai == _lastKiaiValue)
            return;
        _lastKiaiValue = _isKiai;
        EventHandler? handler = HasKiaiChanged;
        handler?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    ///     Checks for a percentage change by comparing the current percentage to the last known percentage
    /// </summary>
    public void CheckForPercentageChange()
    {
        double percentage = GetCompletionPercentage();
        if (_lastCompletionPercentage.HasValue && percentage == _lastCompletionPercentage.Value)
            return;
        _lastCompletionPercentage = percentage;
        HasPercentageChanged?.Invoke();
    }

    /// <summary>
    ///     Forces a beatmap change event to be triggered
    /// </summary>
    /// <remarks>
    ///     This is exclusively used for the Background toggle at the moment
    /// </remarks>
    public void ForceBeatmapChange()
    {
        _lastBeatmapChecksum = "abcdefghijklmnop";
        BeatmapChanged?.Invoke();
    }

    /// <summary>
    ///     Checks for a mod change by comparing the current mod string to the last known mod string
    /// </summary>
    public void CheckForModChange()
    {
        // Normalize to "NM" if null or empty
        string? currentMods = string.IsNullOrEmpty(_modNames) ? "NM" : _modNames;

        if (currentMods == _lastModNames)
            return;

        _lastModNames = currentMods;
        Action? handler = HasModsChanged;
        handler?.Invoke();
        Console.WriteLine($"Mods changed to: {currentMods}");
    }

    /// <summary>
    ///     Checks for a rate change by comparing the current rate to the last known rate
    /// </summary>
    public void CheckForRateAdjustChange()
    {
        double rate = GetRateAdjustRate();
        if (rate == _oldRateAdjustRate)
            return;
        _oldRateAdjustRate = rate;
        Action? handler = HasRateChanged;
        handler?.Invoke();
    }

    /// <summary>
    ///     Checks for a BPM change by comparing the current BPM to the last known BPM
    /// </summary>
    public void CheckForBPMChange()
    {
        double bpm = GetCurrentBpm();
        if (_lastBpm.HasValue && bpm == _lastBpm.Value) return;
        _lastBpm = bpm;
        Action? handler = HasBPMChanged;
        handler?.Invoke();
        Console.WriteLine($"BPM changed to: {bpm}");
    }

    /// <summary>
    ///     Gets the strain graph data from the API
    /// </summary>
    /// <returns></returns>
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

    /// <summary>
    ///     Checks if the current play is a full combo (no misses or slider breaks)
    /// </summary>
    /// <returns></returns>
    public bool IsFullCombo()
    {
        // if there are any misses or slider breaks, return false
        if (GetMissCount() > 0 || GetSBCount() > 0) return false;
        // if there are no misses and no slider breaks, return true
        return true;
    }

    /// <summary>
    ///     Raises the KiaiChanged event
    /// </summary>
    public void RaiseKiaiChanged()
    {
        HasKiaiChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    ///     Parses the graph data from the JSON element into a GraphData object
    /// </summary>
    /// <param name="graphElement"></param>
    /// <returns></returns>
    private GraphData? ParseGraphData(JsonElement graphElement)
    {
        try
        {
            GraphData newGraph = new()
            {
                Series = new List<Series>(),
                XAxis = new List<double>()
            };

            if (graphElement.TryGetProperty("series", out JsonElement seriesArray))
            {
                int seriesCount = seriesArray.GetArrayLength();
                newGraph.Series = new List<Series>(seriesCount);

                foreach (JsonElement seriesElement in seriesArray.EnumerateArray())
                {
                    string? seriesName = seriesElement.GetProperty("name").GetString();
                    if (seriesName == "flashlight" || seriesName == "aimNoSliders") continue;

                    if (seriesElement.TryGetProperty("data", out JsonElement dataArray))
                    {
                        int dataCount = dataArray.GetArrayLength();
                        var data = new List<double>(dataCount);

                        foreach (JsonElement dataElement in dataArray.EnumerateArray())
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

            if (graphElement.TryGetProperty("xaxis", out JsonElement xAxisArray))
            {
                int xCount = xAxisArray.GetArrayLength();
                newGraph.XAxis = new List<double>(xCount);

                foreach (JsonElement xElement in xAxisArray.EnumerateArray())
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