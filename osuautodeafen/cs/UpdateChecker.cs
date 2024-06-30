using System;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Styling;
using Newtonsoft.Json.Linq;
using osuautodeafen;

public class UpdateChecker
{
    private static readonly HttpClient client = new HttpClient();
    private const string currentVersion = "1.0.0";
    private static bool updateChecked = false;

    public delegate void UpdateAvailableHandler(string latestVersion, string latestReleaseUrl);

    public static event UpdateAvailableHandler OnUpdateAvailable;

    public static async Task CheckForUpdates()
    {
        if (updateChecked)
        {
            return;
        }

        var url = $"https://api.github.com/repos/Aerodite/osuautodeafen/releases";
        client.DefaultRequestHeaders.Add("User-Agent", "C# App");

        try
        {
            var response = await client.GetStringAsync(url);
            var releases = JArray.Parse(response);
            if (releases.Count > 0)
            {
                var latestRelease = releases[0];
                var latestVersion = latestRelease["tag_name"].ToString();

                if (Version.Parse(latestVersion) > Version.Parse(currentVersion))
                {
                    var latestReleaseUrl = latestRelease["html_url"].ToString();
                    OnUpdateAvailable?.Invoke(latestVersion, latestReleaseUrl);
                }
                else if (Version.Parse(latestVersion) == Version.Parse(currentVersion))
                {
                    Console.WriteLine("You are using the latest version.");
                }
                else
                {
                    Console.WriteLine("No releases found.");
                }
            }

            updateChecked = true;
        }
        catch (HttpRequestException ex)
        {
            if (ex.StatusCode == HttpStatusCode.Forbidden)
            {
                Console.WriteLine("Rate limit exceeded. Please wait and try again later.");
            }
            else
            {
                Console.WriteLine($"An error occurred: {ex.Message}");
            }
        }
        catch (Exception e)
        {
            Console.WriteLine(e.Message);
        }
    }
}