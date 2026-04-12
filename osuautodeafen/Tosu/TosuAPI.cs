using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.WebSockets;
using System.Reactive.Subjects;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Input;
using Newtonsoft.Json;
using osuautodeafen.Settings;
using osuautodeafen.StrainGraph;

namespace osuautodeafen.Tosu;

public class TosuApi : IDisposable
{
    private static readonly Lock ConnectionLock = new();
    private readonly SemaphoreSlim _connectLock = new(1, 1);
    private readonly List<byte> _dynamicBuffer;
    private readonly string _errorMessage = "";
    private readonly StringBuilder _messageAccumulator = new();
    private readonly Timer? _timer;
    private string? _modNames;
    private ClientWebSocket _webSocket;
    public TosuState? LatestState { get; private set; }
    public sealed record GraphPoint(double Value);

    public sealed record GraphSeries(
        string Name,
        IReadOnlyList<double>? Data
    );

    public sealed record GraphDataModel(
        IReadOnlyList<GraphSeries> Series,
        IReadOnlyList<double>? XAxis
    );
    public sealed record TosuState(
        int BeatmapId,
        int BeatmapSetId,
        string BeatmapChecksum,
        string? BeatmapTitle,
        string? BeatmapArtist,
        string? BeatmapDifficulty,
        string? BeatmapMapper,
        double CurrentTime,
        double FirstObjectTime,
        double FullTime,
        double CompletionPercentage,
        double StarRating,
        double RankedStatus,
        double MaxCombo,
        double CurrentBpm,
        double Rate,
        double Combo,
        double MaxPlayCombo,
        double CurrentPP,
        double MaxPP,
        double MissCount,
        double SliderBreakCount,
        string? ModNames,
        int ModNumber,
        bool IsBreak,
        bool IsKiai,
        bool IsFailed,
        bool IsPaused,
        int RawLazerBanchoStatus,
        int RawBanchoStatus,
        string? Client,
        string? Server,
        string? K1Bind,
        string? K2Bind,
        string? SongsDirectory,
        string? GameDirectory,
        string? BeatmapFilePath,
        string? BeatmapBackgroundPath,
        GraphDataModel GraphData
    );
    
    private readonly Subject<TosuState> _stateStream = new();

    public IObservable<TosuState> StateStream => _stateStream;

    public TosuApi()
    {
        _webSocket = new ClientWebSocket();
        _dynamicBuffer = new List<byte>();
        _ = InitializeConnectionAsync();
    }

    public bool? IsWebsocketConnected => _webSocket.State == WebSocketState.Open;

    private GraphData Graph { get; } = null!;

    /// <summary>
    ///     Disposes the WebSocket and timers when done
    /// </summary>
    public void Dispose()
    {
        _timer?.Dispose();
        if (_webSocket.State == WebSocketState.Open)
            try
            {
                _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", CancellationToken.None)
                    .GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                Serilog.Log.Error("Exception while closing WebSocket: {ExMessage}", ex.Message);
            }

        _webSocket.Dispose();
    }

    /// <summary>
    ///  Attempts connection to the Tosu websocket and handles reconnects gracefully
    /// </summary>
    private async Task InitializeConnectionAsync(CancellationToken cancellationToken = default)
    {
        var delay = TimeSpan.FromSeconds(1);
        var maxDelay = TimeSpan.FromSeconds(16);

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await ConnectAsync(cancellationToken);
                delay = TimeSpan.FromSeconds(1);

                await ReceiveAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                Serilog.Log.Error("Tosu connection lost. Attempting reconnect in " + delay + "s");

                await Task.Delay(delay, cancellationToken);

                delay = TimeSpan.FromSeconds(Math.Min(delay.TotalSeconds * 2, maxDelay.TotalSeconds));
            }
        }
    }

    /// <summary>
    ///     Connects to the Tosu WebSocket API
    /// </summary>
    /// <param name="cancellationToken"></param>
    private async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        await _connectLock.WaitAsync(cancellationToken);

        try
        {
            SettingsHandler settings = new();

            string ip = settings.tosuApiIp ?? "";
            string port = settings.tosuApiPort ?? "";
            string counterPath = "osuautodeafen";

            string uri = $"ws://{ip}:{port}/websocket/v2?l={Uri.EscapeDataString(counterPath)}";

            _webSocket?.Dispose();
            _webSocket = new ClientWebSocket();

            Serilog.Log.Information("Connecting to Tosu: {Uri}", uri);

            await _webSocket.ConnectAsync(new Uri(uri), cancellationToken);

            Serilog.Log.Information("Connected to Tosu WebSocket");
        }
        finally
        {
            _connectLock.Release();
        }
    }

    /// <summary>
    ///     Receives messages from the WebSocket and processes them into variables that can be used anywhere
    /// </summary>
    /// <returns></returns>
    /// <exception cref="InvalidOperationException"></exception>
    public async Task ReceiveAsync(CancellationToken cancellationToken)
    {
        const int bufferSize = 4096;
        var buffer = new byte[bufferSize];

        while (_webSocket.State == WebSocketState.Open &&
               !cancellationToken.IsCancellationRequested)
        {
            _dynamicBuffer.Clear();

            WebSocketReceiveResult result;

            do
            {
                result = await _webSocket.ReceiveAsync(
                    new ArraySegment<byte>(buffer),
                    cancellationToken);

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    Serilog.Log.Warning("WebSocket closed by server");
                    return;
                }

                _dynamicBuffer.AddRange(buffer.Take(result.Count));

            } while (!result.EndOfMessage);

            string json = Encoding.UTF8.GetString(
                _dynamicBuffer.ToArray(),
                0,
                _dynamicBuffer.Count);

            if (string.IsNullOrWhiteSpace(json))
                continue;

            try
            {
                JsonElement root = JsonDocument.Parse(json).RootElement;
            
                int beatmapId = 0;
                int beatmapSetId = 0;
                string beatmapChecksum = "";
                string? title = null;
                string? artist = null;
                string? difficulty = null;
                string? mapper = null;

                double currentTime = 0;
                double firstObject = 0;
                double fullTime = 0;

                double starRating = 0;
                double rankedStatus = 0;
                double maxCombo = 0;

                double realtimeBpm = 0;

                bool isBreak = false;
                bool isKiai = false;
            
                double combo = 0;
                double maxPlayCombo = 0;
                double currentPP = 0;
                double maxPP = 0;
                double missCount = 0;
                double sliderBreaks = 0;

                string? modNames = null;
                int modNumber = 0;
                double rate = 1;

                bool hasFailed = false;
                bool isPaused = false;

                int rawLazerBanchoStatus = 0;
                int rawBanchoStatus = 0;

                string? client = null;
                string? server = null;

                string? k1 = null;
                string? k2 = null;

                string? songs = null;
                string? gameDir = null;
                string? beatmapFile = null;
                string? beatmapBg = null;

                GraphDataModel graphData = new GraphDataModel(Array.Empty<GraphSeries>(), Array.Empty<double>());

                if (root.TryGetProperty("beatmap", out var beatmapProperty))
                {
                    if (beatmapProperty.TryGetProperty("time", out var timeProperty))
                    {
                        if (timeProperty.TryGetProperty("live", out var currentTimeProperty))
                            currentTime = currentTimeProperty.GetDouble();

                        if (timeProperty.TryGetProperty("firstObject", out var firstObjectProperty))
                            firstObject = firstObjectProperty.GetDouble();

                        if (timeProperty.TryGetProperty("lastObject", out var lastObjectProperty))
                            fullTime = lastObjectProperty.GetDouble();
                    }

                    if (beatmapProperty.TryGetProperty("stats", out var stats))
                    {
                        if (stats.TryGetProperty("stars", out var starRatingProperty) &&
                            starRatingProperty.TryGetProperty("total", out var totalSrProperty))
                            starRating = totalSrProperty.GetDouble();

                        if (stats.TryGetProperty("maxCombo", out var maxComboProperty))
                            maxCombo = maxComboProperty.GetDouble();

                        if (stats.TryGetProperty("bpm", out var bpmProperty) &&
                            bpmProperty.TryGetProperty("realtime", out var currentBpmProperty))
                        {
                            realtimeBpm = currentBpmProperty.ValueKind == JsonValueKind.Number
                                ? currentBpmProperty.GetDouble()
                                : double.TryParse(currentBpmProperty.GetString(), out var v) ? v : 0;
                        }
                    }

                    if (beatmapProperty.TryGetProperty("status", out var beatmapStatus) &&
                        beatmapStatus.TryGetProperty("number", out var rankedStatusProperty))
                        rankedStatus = rankedStatusProperty.GetDouble();

                    if (beatmapProperty.TryGetProperty("id", out var beatmapIdProperty))
                        beatmapId = beatmapIdProperty.GetInt32();

                    if (beatmapProperty.TryGetProperty("set", out var beatmapSetProperty))
                        beatmapSetId = beatmapSetProperty.GetInt32();

                    if (beatmapProperty.TryGetProperty("checksum", out var checksumProperty))
                        beatmapChecksum = checksumProperty.GetString() ?? "";

                    if (beatmapProperty.TryGetProperty("isBreak", out var isCurrentlyBreakProperty))
                        isBreak = isCurrentlyBreakProperty.GetBoolean();

                    if (beatmapProperty.TryGetProperty("isKiai", out var isCurrentlyKiaiProperty))
                        isKiai = isCurrentlyKiaiProperty.GetBoolean();

                    if (beatmapProperty.TryGetProperty("title", out var titleProperty))
                        title = titleProperty.GetString();

                    if (beatmapProperty.TryGetProperty("artistUnicode", out var artistProperty))
                        artist = artistProperty.GetString();

                    if (beatmapProperty.TryGetProperty("version", out var mapVersionProperty))
                        difficulty = mapVersionProperty.GetString();

                    if (beatmapProperty.TryGetProperty("mapper", out var mapperProperty))
                        mapper = mapperProperty.GetString();
                }

                if (root.TryGetProperty("play", out var currentPlayProperty))
                {
                    if (currentPlayProperty.TryGetProperty("combo", out var comboProperty))
                    {
                        if (comboProperty.TryGetProperty("current", out var currrentComboProperty))
                            combo = currrentComboProperty.GetDouble();

                        if (comboProperty.TryGetProperty("max", out var maxComboProperty))
                            maxPlayCombo = maxComboProperty.GetDouble();
                    }

                    if (currentPlayProperty.TryGetProperty("pp", out var ppProperty) &&
                        ppProperty.TryGetProperty("current", out var currentPpProperty))
                        currentPP = currentPpProperty.GetDouble();

                    if (currentPlayProperty.TryGetProperty("hits", out var hitProperty))
                    {
                        if (hitProperty.TryGetProperty("0", out var missCountProperty))
                            missCount = missCountProperty.GetDouble();

                        if (hitProperty.TryGetProperty("sliderBreaks", out var sliderBreakProperty))
                            sliderBreaks = sliderBreakProperty.GetDouble();
                    }

                    if (currentPlayProperty.TryGetProperty("mods", out var modsProperty))
                    {
                        if (modsProperty.TryGetProperty("name", out var modNameProperty))
                            modNames = modNameProperty.GetString();

                        if (modsProperty.TryGetProperty("rate", out var rateProperty))
                            rate = rateProperty.GetDouble();

                        if (modsProperty.TryGetProperty("number", out var modNumberProperty))
                            modNumber = modNumberProperty.GetInt32();
                    }

                    if (currentPlayProperty.TryGetProperty("failed", out var failedProperty))
                        hasFailed = failedProperty.GetBoolean();
                }

                if (root.TryGetProperty("state", out var stateProperty) &&
                    stateProperty.TryGetProperty("number", out var lazerBanchoStateProperty))
                    rawLazerBanchoStatus = lazerBanchoStateProperty.GetInt32();

                if (root.TryGetProperty("server", out var serverProperty))
                    server = serverProperty.GetString();

                if (root.TryGetProperty("client", out var clientProperty))
                    client = clientProperty.GetString();

                if (root.TryGetProperty("profile", out var profileProperty) &&
                    profileProperty.TryGetProperty("banchoStatus", out var banchoStatusProperty) &&
                    banchoStatusProperty.TryGetProperty("number", out var banchoStatusNumProperty))
                    rawBanchoStatus = banchoStatusNumProperty.GetInt32();

                if (root.TryGetProperty("folders", out var folders) &&
                    folders.TryGetProperty("songs", out var songsFolderProperty))
                    songs = songsFolderProperty.GetString();

                if (folders.TryGetProperty("game", out var osuDirectoryProperty))
                    gameDir = osuDirectoryProperty.GetString();

                if (root.TryGetProperty("directPath", out var dp) &&
                    dp.TryGetProperty("beatmapBackground", out var bgProperty))
                    beatmapBg = bgProperty.GetString();

                if (dp.TryGetProperty("beatmapFile", out var beatmapFileProperty))
                    beatmapFile = beatmapFileProperty.GetString();

                if (root.TryGetProperty("performance", out var perf) &&
                    perf.TryGetProperty("graph", out var graphProperty))
                {
                    graphData = ParseGraph(graphProperty);
                }

                var tosuState = new TosuState(
                    beatmapId,
                    beatmapSetId,
                    beatmapChecksum,
                    title,
                    artist,
                    difficulty,
                    mapper,
                    currentTime,
                    firstObject,
                    fullTime,
                    0,
                    starRating,
                    rankedStatus,
                    maxCombo,
                    realtimeBpm,
                    rate,
                    combo,
                    maxPlayCombo,
                    currentPP,
                    maxPP,
                    missCount,
                    sliderBreaks,
                    modNames,
                    modNumber,
                    isBreak,
                    isKiai,
                    hasFailed,
                    isPaused,
                    rawLazerBanchoStatus,
                    rawBanchoStatus,
                    client,
                    server,
                    k1,
                    k2,
                    songs,
                    gameDir,
                    beatmapFile,
                    beatmapBg,
                    graphData
                );
                _stateStream.OnNext(tosuState);
                LatestState = tosuState;
            }
            catch (Exception ex)
            {
                Serilog.Log.Error($"Tosu parse error: {ex.Message}");
            }
            finally
            {
                _messageAccumulator.Clear();
            }

            if (result.MessageType == WebSocketMessageType.Close)
            {
                await _webSocket.CloseAsync(
                    WebSocketCloseStatus.NormalClosure,
                    string.Empty,
                    CancellationToken.None);
            }

            _dynamicBuffer.Clear();
        }
    }

    /// <summary>
    ///     Gets the current pp value
    /// </summary>
    /// <returns>
    ///     pp value as a double
    /// </returns>
    public double GetCurrentPP()
    {
        var state = LatestState;
        if (state == null || state.CurrentPP < 0)
            return 0;

        return state.CurrentPP;
    }
    
    /// <summary>
    ///     Gets the current percentage completed of the beatmap
    /// </summary>
    /// <returns>
    ///     double in the form of 0.* to 100
    /// </returns>
    public double GetCompletionPercentage()
    {
        var state = LatestState;
        if (state == null || state.FullTime == 0)
            return double.NaN;

        double current = state.CurrentTime;
        double first = state.FirstObjectTime;
        double full = state.FullTime;

        if (current < first)
            return 0;

        if (current > full)
            return 100;

        return (current - first) / (full - first) * 100;
    }
    
    /// <summary>
    ///     Gets the current percentage of the song playback
    /// </summary>
    /// <returns>
    ///     double in the form of 0.* to 100
    /// </returns>
    public double GetSongProgress()
    {
        var state = LatestState;
        if (state == null || state.FullTime == 0)
            return double.NaN;

        if (state.CurrentTime > state.FullTime)
            return 100;

        return state.CurrentTime / state.FullTime * 100;
    }
    
    public int GetCurrentSongProgress()
    {
        var state = LatestState;
        if (state == null)
            return 0;

        if (state.CurrentTime > state.FullTime)
            return (int)state.FullTime;

        return (int)state.CurrentTime;
    }
    
    /// <summary>
    ///     Gets the server name from tosu (e.g. "Bancho", "Akatsuki", "Gatari", etc.)
    /// </summary>
    /// <returns>
    ///     Server as a string
    /// </returns>
    /// <remarks>
    ///     Info only seen in the debug console (Ctrl + D)
    /// </remarks>
    public string GetServer()
    {
        return LatestState?.Server ?? "Unknown Server";
    }
    
    /// <summary>
    ///     Gets the client name from tosu (lazer or stable)
    /// </summary>
    /// <returns>
    ///     Client as a string
    /// </returns>
    /// <remarks>
    ///     Info only seen on the debug console (Ctrl + D)
    /// </remarks>
    public string GetClient()
    {
        return LatestState?.Client ?? "Unknown Client";
    }
    
    /// <summary>
    ///     Gets the current progress of the beatmap in milliseconds
    /// </summary>
    /// <returns></returns>
    public int GetCurrentTime()
    {
        var state = LatestState;
        if (state == null)
            return 0;

        if (state.CurrentTime < state.FirstObjectTime)
            return 0;

        if (state.CurrentTime > state.FullTime)
            return (int)state.FullTime;

        return (int)state.CurrentTime;
    }
    
    /// <summary>
    ///     Gets the full length of the beatmap in milliseconds
    /// </summary>
    /// <remarks>
    ///     as far as I can tell this seems to be as accurate as I can get,
    ///     because this uses both the mp3 time and first object to determine the beatmap length.
    /// </remarks>
    public int GetFullTime()
    {
        var state = LatestState;
        if (state == null)
            return 0;

        if (state.FullTime < state.FirstObjectTime)
            return 0;

        return (int)state.FullTime;
    }
    
    /// <summary>
    ///     Get full star rating of the beatmap including mods
    /// </summary>
    /// <returns></returns>
    public double GetFullSR()
    {
        return LatestState?.StarRating ?? 0;
    }
    
    /// <summary>
    /// Gets BPM of the map at current point in time
    /// </summary>
    /// <returns></returns>
    public double GetCurrentBpm()
    {
        return LatestState?.CurrentBpm ?? 0;
    }
    
    /// <summary>
    ///     Checks if the current play is paused
    /// </summary>
    public bool IsPaused()
    {
        return LatestState?.IsPaused ?? false;
    }
    
    /// <summary>
    ///     Checks if the current play has failed
    /// </summary>
    /// <returns></returns>
    public bool HasFailed()
    {
        return LatestState?.IsFailed ?? false;
    }

    /// <summary>
    ///     Obtains the title of the current beatmap
    /// </summary>
    /// <returns></returns>
    public string? GetBeatmapTitle()
    {
        return LatestState?.BeatmapTitle;
    }

    /// <summary>
    ///     Obtains the artist of the current beatmap
    /// </summary>
    /// <returns></returns>
    public string? GetBeatmapArtist()
    {
        return LatestState?.BeatmapArtist;
    }

    public string GetBeatmapDifficulty()
    {
        return (LatestState?.BeatmapDifficulty ?? "Unknown Difficulty").TrimEnd();
    }

    public IEnumerable<Key> GetOsuKeybinds()
    {
        if (Enum.TryParse(LatestState?.K1Bind, out Key key1))
            yield return key1;
        if (Enum.TryParse(LatestState?.K2Bind, out Key key2))
            yield return key2;
    }

    /// <summary>
    ///     Obtains the osu! file path of the current beatmap
    /// </summary>
    /// <returns></returns>
    public string? GetOsuFilePath()
    {
        return LatestState?.BeatmapFilePath;
    }

    /// <summary>
    ///     Obtains the full file path of the current beatmap
    /// </summary>
    /// <returns></returns>
    public string? GetFullFilePath()
    {
        if (LatestState?.BeatmapFilePath == null)
            return null;

        string osuSongsFolder;

        if ((OperatingSystem.IsLinux() || OperatingSystem.IsMacOS()) &&
            LatestState?.Client == "stable")
        {
            string home = Environment.GetEnvironmentVariable("HOME") ?? "";
            osuSongsFolder = Path.Combine(home, ".local", "share", "osu-wine", "osu!", "Songs");
        }
        else
        {
            osuSongsFolder = LatestState?.SongsDirectory ?? "";
        }

        string? normalizedFilePath = LatestState?.BeatmapFilePath
            .TrimStart('\\', '/')
            .Replace('\\', Path.DirectorySeparatorChar)
            .Replace('/', Path.DirectorySeparatorChar);

        if (normalizedFilePath != null)
        {
            string fullPath = Path.Combine(osuSongsFolder, normalizedFilePath);

            return File.Exists(fullPath) ? Path.GetFullPath(fullPath) : null;
        }

        return "";
    }

    /// <summary>
    ///     Obtains the maximum pp value of the current beatmap with selected mods
    /// </summary>
    /// <returns></returns>
    public double? GetMaxPP()
    {
        return LatestState?.MaxPP;
    }

    /// <summary>
    ///     Obtains the current combo of the play
    /// </summary>
    /// <returns></returns>
    public double? GetCombo()
    {
        return LatestState?.Combo;
    }

    /// <summary>
    ///     Obtains the maximum achievable combo of the current beatmap
    /// </summary>
    /// <returns></returns>
    public double? GetMaxCombo()
    {
        return LatestState?.MaxCombo;
    }

    /// <summary>
    ///     Obtains the maximum combo the player has achieved in this current play
    /// </summary>
    /// <remarks>
    ///     Literally the only reason this is here is to avoid that "Deafen Debounce" issue that has been prevalent for a while
    /// </remarks>
    public double? GetMaxPlayCombo()
    {
        return LatestState?.MaxPlayCombo;
    }

    /// <summary>
    ///     Obtains the current miss count of the play
    /// </summary>
    /// <returns></returns>
    public double? GetMissCount()
    {
        return LatestState?.MissCount;
    }

    /// <summary>
    ///     Obtains the current slider break count of the player
    /// </summary>
    /// <returns></returns>
    public double? GetSBCount()
    {
        return LatestState?.SliderBreakCount;
    }

    /// <summary>
    ///     Obtains the status of the current beatmap
    /// </summary>
    /// <returns></returns>
    public double? GetRankedStatus()
    {
        return LatestState?.RankedStatus;
    }

    /// <summary>
    ///     Obtains the background path of the current beatmap
    /// </summary>
    /// <returns></returns>
    public string? GetBackgroundPath()
    {
        if (LatestState == null)
            return null;

        string songsDir = LatestState.SongsDirectory ?? "";
        string fullPath = LatestState.BeatmapBackgroundPath ?? "";

        if (string.IsNullOrWhiteSpace(fullPath))
            return null;

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            string home = Environment.GetEnvironmentVariable("HOME") ?? "";

            if (songsDir == "Songs" || string.IsNullOrWhiteSpace(songsDir))
            {
                songsDir = Path.Combine(
                    home,
                    ".local",
                    "share",
                    "osu-wine",
                    "osu!",
                    "Songs");
            }
        }

        return Path.GetFullPath(Path.Combine(
            songsDir,
            fullPath.Replace("\\", Path.DirectorySeparatorChar.ToString())
        ));
    }

    /// <summary>
    ///     Obtains the game directory of the current osu! installation
    /// </summary>
    /// <returns></returns>
    public string? GetGameDirectory()
    {
        return LatestState?.GameDirectory;
    }

    /// <summary>
    ///     Gets the selected mods as a string (e.g. "HDDTHR")
    /// </summary>
    public string? GetSelectedMods()
    {
        if (string.IsNullOrEmpty(LatestState?.ModNames))
        {
            _modNames = "NM";
        }
        _modNames = LatestState?.ModNames;

        return _modNames;
    }

    /// <summary>
    ///     Gets the rate (0.50-2.00) from mod select
    /// </summary>
    /// <returns></returns>
    public double GetRateAdjustRate()
    {
        return LatestState?.Rate ?? 1.00;
    }

    /// <summary>
    ///     Gets the raw bancho status
    /// </summary>
    /// <returns></returns>
    public int GetRawBanchoStatus()
    {
        return LatestState?.RawBanchoStatus ?? -1;
    }

    public int GetLazerRawBanchoStatus()
    {
        return LatestState?.RawLazerBanchoStatus ?? -1;
    }

    /// <summary>
    ///     Gets the beatmap ID of the current beatmap
    /// </summary>
    /// <returns></returns>
    public int? GetBeatmapId()
    {
        return LatestState?.BeatmapId;
    }

    /// <summary>
    ///     Gets the beatmap set ID of the current beatmap
    /// </summary>
    /// <returns></returns>
    public int? GetBeatmapSetId()
    {
        return LatestState?.BeatmapSetId;
    }

    /// <summary>
    ///     Gets the beatmap mapper of the current beatmap
    /// </summary>
    /// <returns></returns>
    public string GetBeatmapMapper()
    {
        return LatestState?.BeatmapMapper ?? "Unknown Mapper";
    }

    /// <summary>
    ///     Gets the mod number of the current selected mods
    /// </summary>
    /// <returns></returns>
    public int? GetModNumber()
    {
        return LatestState?.ModNumber;
    }

    /// <summary>
    ///     Gets the beatmap checksum of the current beatmap (Alternative method to GetBeatmapId())
    /// </summary>
    /// <returns></returns>
    public string? GetBeatmapChecksum()
    {
        return LatestState?.BeatmapChecksum;
    }

    /// <summary>
    ///     Checks if the current time is in a break period
    /// </summary>
    /// <returns></returns>
    public bool IsBreakPeriod()
    {
        return LatestState?.IsBreak ?? false;
    }

    /// <summary>
    ///     Checks if the current time is in kiai time
    /// </summary>
    /// <returns></returns>
    public bool? IsKiai()
    {
        return LatestState?.IsKiai;
    }

    /// <summary>
    ///     Checks if the current play is a full combo (no misses or slider breaks)
    /// </summary>
    /// <returns></returns>
    public bool IsHoldingFullCombo()
    {
        // if there are any misses or slider breaks, return false
        if (LatestState?.MissCount > 0 || LatestState?.SliderBreakCount > 0) 
            return false;
        // if there are no misses and no slider breaks, return true
        return true;
    }
    
    private static GraphDataModel ParseGraph(JsonElement graph)
    {
        if (graph.ValueKind != JsonValueKind.Object)
            return new GraphDataModel([], []);
        
        var seriesList = new List<GraphSeries>();

        if (graph.TryGetProperty("series", out var seriesJson) &&
            seriesJson.ValueKind == JsonValueKind.Array)
        {
            foreach (var series in seriesJson.EnumerateArray())
            {
                string name = series.TryGetProperty("name", out var n)
                    ? n.GetString() ?? ""
                    : "";
                
                if (name == "flashlight" || name == "aimNoSliders")
                    continue;

                var data = new List<double>();

                if (series.TryGetProperty("data", out var dataArray) &&
                    dataArray.ValueKind == JsonValueKind.Array)
                {
                    foreach (var v in dataArray.EnumerateArray())
                    {
                        if (v.ValueKind == JsonValueKind.Number)
                            data.Add(v.GetDouble());
                    }
                }

                seriesList.Add(new GraphSeries(name, data));
            }
        }
        
        var xAxis = new List<double>();

        if (graph.TryGetProperty("xaxis", out var xAxisArray) &&
            xAxisArray.ValueKind == JsonValueKind.Array)
        {
            foreach (var x in xAxisArray.EnumerateArray())
            {
                if (x.ValueKind == JsonValueKind.Number)
                    xAxis.Add(x.GetDouble());
            }
        }

        return new GraphDataModel(seriesList, xAxis);
    }
}