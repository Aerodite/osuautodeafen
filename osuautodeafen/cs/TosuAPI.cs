using System;
using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Timers;

namespace osuautodeafen
{
    public class TosuAPI : IDisposable
    {
        private string _errorMessage = "";
        public event Action<double> MessageReceived;

        private HttpClient _httpClient;
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
        public event Action<int> StateChanged;

        public TosuAPI()
        {
            _httpClient = new HttpClient();
            _timer = new Timer(500);
            _timer.Elapsed += (sender, e) => ConnectAsync();
            _timer.Start();
        }

        public string GetErrorMessage()
        {
            return _errorMessage;
        }

        public async Task<string> ConnectAsync()
        {
            Debug.WriteLine("Attempting to connect...");
            _errorMessage = "";
            try
            {
                var response = await _httpClient.GetAsync("http://127.0.0.1:24050/json");
                var content = await response.Content.ReadAsStringAsync();
                var jsonDocument = JsonDocument.Parse(content);

                Background background = new Background();
                string? fullBackgroundDirectory = background.GetFullBackgroundDirectory(content);

                if (jsonDocument.RootElement.TryGetProperty("error", out JsonElement errorElement) && errorElement.GetString() == "not_ready")
                {
                    _errorMessage = "osu! is not running!";
                    return content;
                }
                if (jsonDocument.RootElement.TryGetProperty("menu", out JsonElement menuElement))
                {
                    if (menuElement.TryGetProperty("pp", out JsonElement ppElement))
                    {
                        if (ppElement.TryGetProperty("100", out JsonElement maxPPElement))
                        {
                            if (maxPPElement.ValueKind == JsonValueKind.Number)
                            {
                                _maxPP = maxPPElement.GetDouble();
                            }
                        }
                    }

                    if (menuElement.TryGetProperty("bm", out JsonElement bmElement))
                    {
                        if (bmElement.TryGetProperty("time", out JsonElement timeElement))
                        {
                            if(timeElement.TryGetProperty("firstObj", out JsonElement firstObjElement))
                            {
                                double firstObj = firstObjElement.GetDouble();
                                _firstObj = firstObj;
                            }
                            if (timeElement.TryGetProperty("current", out JsonElement currentElement))
                            {
                                double current = currentElement.GetDouble();
                                _current = current;
                            }
                            if (timeElement.TryGetProperty("full", out JsonElement fullElement))
                            {
                                double full = fullElement.GetDouble();
                                _full = full;
                            }
                        }
                        if (bmElement.TryGetProperty("stats", out JsonElement statsElement))
                        {
                            if (statsElement.TryGetProperty("fullSR", out JsonElement fullSRElement))
                            {
                                _fullSR = fullSRElement.GetDouble();
                            }
                        }
                        if (bmElement.TryGetProperty("rankedStatus", out JsonElement rankedStatusElement))
                        {
                            if (rankedStatusElement.ValueKind == JsonValueKind.Number)
                            {
                                _rankedStatus = rankedStatusElement.GetDouble();
                            }
                        }
                    }

                    if (jsonDocument.RootElement.TryGetProperty("gameplay", out JsonElement gameplayElement))
                    {
                        if (gameplayElement.TryGetProperty("combo", out JsonElement comboElement))
                        {
                            if (comboElement.TryGetProperty("current", out JsonElement currentComboElement))
                            {
                                _combo = currentComboElement.GetDouble();
                            }
                            if (comboElement.TryGetProperty("max", out JsonElement maxComboElement))
                            {
                                _maxCombo = maxComboElement.GetDouble();
                            }
                        }

                        if (gameplayElement.TryGetProperty("hits", out JsonElement hitsElement))
                        {
                            if (hitsElement.ValueKind == JsonValueKind.Object && hitsElement.TryGetProperty("0", out JsonElement missElement))
                            {
                                if (missElement.ValueKind == JsonValueKind.Number)
                                {
                                    _missCount = missElement.GetDouble();
                                    if (_missCount > 0)
                                    {
                                        Console.WriteLine($"Miss count: {_missCount}");
                                    }
                                }
                            }
                            if (hitsElement.ValueKind == JsonValueKind.Object && hitsElement.TryGetProperty("sliderBreaks", out JsonElement sbElement))
                            {
                                if (sbElement.ValueKind == JsonValueKind.Number)
                                {
                                    _sbCount = sbElement.GetDouble();
                                    if (_sbCount > 0)
                                    {
                                        Console.WriteLine($"Slider break count: {_sbCount}");
                                    }
                                }
                            }
                        }
                    }

                    if (jsonDocument.RootElement.TryGetProperty("userProfile", out JsonElement userProfileElement))
                    {
                        if (userProfileElement.TryGetProperty("rawBanchoStatus", out JsonElement rawBanchoStatusElement))
                        {
                            int rawBanchoStatus = rawBanchoStatusElement.GetInt32();

                            StateChanged?.Invoke(rawBanchoStatus);

                            if (rawBanchoStatus == 2)
                            {

                            }
                        }
                    }
                }
                return content;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"An error occurred: {ex.Message}");
                _errorMessage = $"An error occurred (is tosu running?): {ex.Message}";
            }
            return null;
        }

        public double GetCompletionPercentage()
        {
            if (_current < _firstObj)
            {
                _completionPercentage = 0;
            }
            else
            {
                double _totalObjTime = _full - _firstObj;
                double _relativeCurrentTime = _current - _firstObj;
                _completionPercentage = (_relativeCurrentTime / _totalObjTime) * 100;
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


        public void Dispose()
        {
            Console.WriteLine("Application is closing...");
            _timer.Dispose();
            _httpClient.Dispose();
        }
    }
}