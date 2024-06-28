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
        public event Action<int> StateChanged;

        public TosuAPI()
        {
            Debug.WriteLine("Application is starting up...");
            _httpClient = new HttpClient();
            _timer = new Timer(250);
            _timer.Elapsed += (sender, e) => ConnectAsync();
            _timer.Start();
        }

        public string GetErrorMessage()
        {
            return _errorMessage;
        }

       private async Task ConnectAsync()
{
    Debug.WriteLine("Attempting to connect...");
    _errorMessage = "";
    try
    {
        var response = await _httpClient.GetAsync("http://127.0.0.1:24050/json");
        var content = await response.Content.ReadAsStringAsync();
        var jsonDocument = JsonDocument.Parse(content);

        if (jsonDocument.RootElement.TryGetProperty("error", out JsonElement errorElement) && errorElement.GetString() == "not_ready")
        {
            _errorMessage = "osu! is not running!";
            return;
        }

        if (jsonDocument.RootElement.TryGetProperty("menu", out JsonElement menuElement))
        {
            if (menuElement.TryGetProperty("bm", out JsonElement bmElement))
            {
                if (bmElement.TryGetProperty("time", out JsonElement timeElement))
                {
                    if (timeElement.TryGetProperty("current", out JsonElement currentElement) &&
                        timeElement.TryGetProperty("full", out JsonElement fullElement))
                    {
                        // Calculate the completion percentage
                        double current = currentElement.GetDouble();
                        double full = fullElement.GetDouble();
                        _completionPercentage = (current / full) * 100; // Store the completion percentage
                    }
                }

                if (bmElement.TryGetProperty("stats", out JsonElement statsElement))
                {
                    if (statsElement.TryGetProperty("fullSR", out JsonElement fullSRElement))
                    {
                        // Extract the fullSR value
                        double fullSR = fullSRElement.GetDouble();
                        // Store the fullSR value

                    }
                }

                if (bmElement.TryGetProperty("pp", out JsonElement ppElement))
                {
                    if (ppElement.TryGetProperty("100", out JsonElement pp100Element))
                    {
                        // Extract the 100pp value
                        double pp100 = pp100Element.GetDouble();
                        // Store the 100pp value
                    }
                }
            }

            if (menuElement.TryGetProperty("state", out JsonElement stateElement))
            {
                int state = stateElement.GetInt32();

                StateChanged?.Invoke(state);

                if (state == 2)
                {
                    //debugging purposes, exact double value of percentage.
                    //MessageReceived?.Invoke(_completionPercentage);
                }
            }
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"An error occurred: {ex.Message}");
        _errorMessage = $"An error occurred (is tosu running?): {ex.Message}";
    }
}

        public double GetCompletionPercentage()
        {
            return _completionPercentage;
        }

        public void Dispose()
        {
            Console.WriteLine("Application is closing...");
            _timer.Dispose();
            _httpClient.Dispose();
        }
    }
}