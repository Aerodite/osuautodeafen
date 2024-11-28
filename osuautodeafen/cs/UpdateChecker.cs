using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace osuautodeafen.cs;

public class UpdateChecker
{
    public delegate void UpdateAvailableHandler(string latestVersion, string latestReleaseUrl);

    public const string currentVersion = "1.0.6";
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
        // this is mainly to stop rate limiting github api
        //TODO: this is kinda unnecessary now so get rid in future
        if (lastSuccessfulCheck.HasValue && DateTime.Now - lastSuccessfulCheck.Value < cacheDuration)
        {
            Console.WriteLine("Using cached version data.");
            return false; // cached data is being used, no new fetch
        }

        var url = "https://api.github.com/repos/Aerodite/osuautodeafen/releases";
        client.DefaultRequestHeaders.Add("User-Agent", "C# App");

        try
        {
            var response = await client.GetStringAsync(url);
            var releases = JArray.Parse(response);
            switch (releases.Count)
            {
                case > 0:
                {
                    var latestRelease = releases[0];
                    latestVersion = latestRelease["tag_name"]?.ToString().TrimStart('v');
                    lastSuccessfulCheck = DateTime.Now;
                    return true;
                }
                default:
                    Console.WriteLine("No releases found.");
                    return false;
            }
        }
        catch (HttpRequestException ex)
        {
            switch (ex.StatusCode)
            {
                case HttpStatusCode.Forbidden:
                    Console.WriteLine("Rate limit exceeded. Please wait and try again later.");
                    break;
                default:
                    Console.WriteLine($"An error occurred: {ex.Message}");
                    break;
            }

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