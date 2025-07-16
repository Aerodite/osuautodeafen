using System;
using System.Threading.Tasks;
using Velopack;
using Velopack.Sources;

public class UpdateChecker
{
    public static string currentVersion = "1.0.8";
    public async Task CheckForUpdatesAsync()
    {
        var updateSource = new GithubSource("https://github.com/Aerodite/osuautodeafen", null, false, null);
        var mgr = new UpdateManager(updateSource);

        if (!mgr.IsInstalled)
        {
            Console.WriteLine("Update check skipped: application is not installed.");
            return;
        }

        var updateInfo = await mgr.CheckForUpdatesAsync();
        if (updateInfo == null)
        {
            Console.WriteLine("No updates available.");
            return;
        }

        Console.WriteLine("Update available.");
        await mgr.DownloadUpdatesAsync(updateInfo);
        mgr.ApplyUpdatesAndRestart(updateInfo);
    }
}