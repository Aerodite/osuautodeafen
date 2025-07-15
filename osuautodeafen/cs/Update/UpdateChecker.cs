using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace osuautodeafen.cs;

public class UpdateChecker
{
    public delegate void UpdateAvailableHandler(string latestVersion, string latestReleaseUrl);

    public const string currentVersion = "1.0.8";
    private static readonly HttpClient client = new();
    private static DateTime? lastSuccessfulCheck;
    private static readonly TimeSpan cacheDuration = TimeSpan.FromMinutes(1);

    private static UpdateChecker _instance;
    private static readonly object _lock = new();
    private bool _hasCheckedVersion = false;
    public bool updateFound = true;

    private UpdateChecker()
    {
    }

    public string? latestVersion { get; set; }
    public string? LatestVersion { get; }

    public static event UpdateAvailableHandler? OnUpdateAvailable;

    public static UpdateChecker GetInstance()
    {
        if (_instance == null)
            lock (_lock)
            {
                if (_instance == null) _instance = new UpdateChecker();
            }

        return _instance;
    }

    public async Task<bool> FetchLatestVersionAsync()
    {
        var url = "https://api.github.com/repos/Aerodite/osuautodeafen/releases";

        try
        {
            if (!client.DefaultRequestHeaders.Contains("User-Agent"))
                client.DefaultRequestHeaders.Add("User-Agent", "C# App");

            var response = await client.GetAsync(url);
            Console.WriteLine($"GitHub Response Code: {response.StatusCode}");
            if (!response.IsSuccessStatusCode)
            {
                if (response.StatusCode == HttpStatusCode.Forbidden)
                    Console.WriteLine("Rate limit exceeded. Please wait and try again later.");
                else
                    Console.WriteLine($"HTTP error: {response.StatusCode}");
                return false;
            }

            var content = await response.Content.ReadAsStringAsync();
            var releases = JArray.Parse(content);
            if (releases.Count > 0)
            {
                var latestRelease = releases[0];
                latestVersion = latestRelease["tag_name"]?.ToString().TrimStart('v');
                lastSuccessfulCheck = DateTime.Now;
                Console.WriteLine($"Latest version available: {latestVersion}");
                return true;
            }

            Console.WriteLine("No releases found.");
            return false;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"An error occurred: {ex.Message}");
            return false;
        }
    }

    public static event Action<bool>? UpdateCheckCompleted;

    public async Task CheckForUpdates()
    {
        Console.WriteLine("Checking for updates...");

        var url = "https://api.github.com/repos/Aerodite/osuautodeafen/releases";
        client.DefaultRequestHeaders.Add("User-Agent", "C# App");

        try
        {
            var response = await client.GetStringAsync(url);
            var releases = JArray.Parse(response);
            if (releases.Count > 0)
            {
                var latestRelease = releases[0];
                var latestVersion = latestRelease["tag_name"]?.ToString().TrimStart('v'); // remove the "v" prefix

                if (Version.Parse(latestVersion) > Version.Parse(currentVersion))
                {
                    var latestReleaseUrl = latestRelease["html_url"]?.ToString();
                    latestRelease = latestRelease["name"]?.ToString();
                    Console.WriteLine($"Update available: {latestVersion}");
                    updateFound = true;
                    if (latestReleaseUrl != null) OnUpdateAvailable?.Invoke(latestVersion, latestReleaseUrl);
                    UpdateCheckCompleted?.Invoke(updateFound);
                }
                else if (Version.Parse(latestVersion) == Version.Parse(currentVersion))
                {
                    latestRelease = latestRelease["name"]?.ToString();
                    Console.WriteLine("You are using the latest version.");
                }
                else
                {
                    latestRelease = "No releases found (???)";
                    Console.WriteLine("No releases found.");
                }
            }
        }
        catch (HttpRequestException ex)
        {
            if (ex.StatusCode == HttpStatusCode.Forbidden)
                Console.WriteLine("Rate limit exceeded");
            else
                Console.WriteLine($"An error occurred: {ex.Message}");
        }
        catch (Exception e)
        {
            Console.WriteLine(e.Message);
        }
    }

    public static class UpdateEvents
    {
        public static event Action UpdateFoundEvent;

        public static void OnUpdateFound()
        {
            UpdateFoundEvent?.Invoke();
        }
    }
}