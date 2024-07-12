using System;
using System.Collections.Generic;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace osuautodeafen.cs
{
    public class TosuApi : IDisposable
    {
        private string _errorMessage = "";
        public event Action<double>? MessageReceived;
        private ClientWebSocket _webSocket;
        private List<byte> _dynamicBuffer;
        private const int ChunkSize = 100000;
        private Timer _timer;
        private double _completionPercentage;
        private double _fullSR;
        private double _maxPP;
        private double _combo;
        private double _maxCombo;
        private double _missCount;
        private double _sbCount;
        private double _current;
        private double _full;
        private double _firstObj;
        private double _rankedStatus;
        private string settingsSongsDirectory;
        private string fullPath;
        private StringBuilder messageAccumulator = new StringBuilder();

        public event Action<int>? StateChanged;
        private const string WebSocketUri = "ws://127.0.0.1:24050/websocket/v2";

        public TosuApi()
        {
            _webSocket = new ClientWebSocket();
            _dynamicBuffer = new List<byte>();
            ConnectAsync();
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
                    await ReceiveAsync();
                }
                catch (Exception ex)
                {
                    _errorMessage = $"Failed to connect: {ex.Message}";
                }
            }
        }

        public async Task<JsonDocument?> ReceiveAsync()
        {
            var temporaryBuffer = new byte[ChunkSize];
            while (_webSocket.State == WebSocketState.Open)
            {
                WebSocketReceiveResult result;
                do
                {
                    result = await _webSocket.ReceiveAsync(new ArraySegment<byte>(temporaryBuffer), CancellationToken.None);
                    _dynamicBuffer.AddRange(new ArraySegment<byte>(temporaryBuffer, 0, result.Count));
                }
                while (!result.EndOfMessage);

                if (result.EndOfMessage)
                {
                    try
                    {
                        messageAccumulator.Append(Encoding.UTF8.GetString(_dynamicBuffer.ToArray(), 0, _dynamicBuffer.Count));
                        var completeMessage = messageAccumulator.ToString();
                        var root = JsonDocument.Parse(completeMessage).RootElement;
                        string jsonString =
                            await Task.FromResult(
                                "{ \"key\": \"value\" }");

                        ////////////////////////////////////////////////////////////////////
                        if (root.TryGetProperty("stats", out var stats))
                        {
                            if (stats.TryGetProperty("pp", out var pp))
                            {
                                if (pp.TryGetProperty("fc", out var fc))
                                {
                                    _maxPP = fc.GetDouble();
                                }
                            }
                        }

                        if (root.TryGetProperty("beatmap", out var beatmap))
                        {
                            if (beatmap.TryGetProperty("time", out var time))
                            {
                                if (time.TryGetProperty("live", out var live))
                                {
                                    _current = live.GetDouble();
                                }

                                if (time.TryGetProperty("firstObject", out var firstObject))
                                {
                                    _firstObj = firstObject.GetDouble();
                                }

                                if (time.TryGetProperty("lastObject", out var lastObject))
                                {
                                    _full = lastObject.GetDouble();
                                }
                            }

                            if (beatmap.TryGetProperty("stats", out var bmstats))
                            {
                                if (bmstats.TryGetProperty("stars", out var stars))
                                {
                                    if (stars.TryGetProperty("total", out var totalSR))
                                    {
                                        _fullSR = totalSR.GetDouble();
                                    }
                                }
                            }

                            if (beatmap.TryGetProperty("status", out var status))
                            {
                                if (beatmap.TryGetProperty("number", out var statusNumber))
                                {
                                    _rankedStatus = statusNumber.GetDouble();
                                }
                            }
                        }

                        if (root.TryGetProperty("play", out var play))
                        {
                            if (play.TryGetProperty("combo", out var combo))
                            {
                                if (combo.TryGetProperty("current", out var currentCombo))
                                {
                                    _combo = currentCombo.GetDouble();
                                }

                                if (combo.TryGetProperty("max", out var maxCombo))
                                {
                                    _maxCombo = maxCombo.GetDouble();
                                }
                            }

                            if (play.TryGetProperty("hits", out var hits))
                            {
                                if (hits.ValueKind == JsonValueKind.Object &&
                                    hits.TryGetProperty("0", out var missElement))
                                {
                                    if (missElement.ValueKind == JsonValueKind.Number)
                                    {
                                        _missCount = missElement.GetDouble();
                                        if (_missCount > 0)
                                        {
                                            //Console.WriteLine($"Miss count: {_missCount}");
                                        }
                                    }
                                }

                                if (hits.ValueKind == JsonValueKind.Object &&
                                    hits.TryGetProperty("sliderBreaks", out var sbElement))
                                {
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
                        }

                        if (root.TryGetProperty("profile", out var profile))
                        {
                            if (profile.TryGetProperty("banchoStatus", out var banchoStatus))
                            {
                                if (banchoStatus.TryGetProperty("number", out var banchoStatusNumber))
                                {
                                    int rawBanchoStatus = banchoStatusNumber.GetInt32();
                                    StateChanged?.Invoke(rawBanchoStatus);
                                    if (rawBanchoStatus == 2)
                                    {

                                    }
                                }
                            }
                        }
                        if (root.TryGetProperty("folders", out var folders) &&
                            folders.TryGetProperty("songs", out var songs))
                        {
                            settingsSongsDirectory = songs.GetString();
                        }

                        if (root.TryGetProperty("directPath", out var directPath) &&
                            directPath.TryGetProperty("beatmapBackground", out var beatmapBackground))
                        {
                            fullPath = beatmapBackground.GetString();
                        }
                        string combinedPath = settingsSongsDirectory + "\\" + fullPath;

                    var jsonDocument = JsonDocument.Parse(jsonString);
                    var rootElement = jsonDocument.RootElement;
                    }
                    catch (JsonReaderException ex)
                    {
                        Console.WriteLine($"Failed to parse JSON: {ex.Message}");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"An error occurred: {ex.Message}");
                    }
                    finally
                    {
                        messageAccumulator.Clear();
                    }
                    /////////////////////////////////////////////////////////////////

                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        await _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, string.Empty,
                            CancellationToken.None);
                    }
                    _dynamicBuffer.Clear();
                }
            }
            return null;
        }

        public double GetCompletionPercentage()
        {
            if (_full == 0)
            {
                //Console.WriteLine("completion percent = Undefined (division by zero)");
                return double.NaN;
            }

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
            return settingsSongsDirectory + "\\" + fullPath;
        }


        public void Dispose()
        {
            _timer?.Dispose();
            if (_webSocket != null)
            {
                if (_webSocket.State == WebSocketState.Open)
                {
                    try
                    {
                        _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", CancellationToken.None)
                            .GetAwaiter().GetResult();
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Exception while closing WebSocket: {ex.Message}");
                    }
                }

                _webSocket.Dispose();
            }
        }
    }
}