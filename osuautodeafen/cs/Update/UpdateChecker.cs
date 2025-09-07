using System;
using System.Threading.Tasks;
using osuautodeafen;
using Velopack;
using Velopack.Sources;

public class UpdateChecker
{
    public static string currentVersion = "1.0.9";

    private static readonly GithubSource updateSource = new("https://github.com/Aerodite/osuautodeafen",
        null, false);

    private MainWindow _mainWindow;
    public UpdateManager mgr = new(updateSource);
    public UpdateInfo? UpdateInfo;

    public async Task CheckForUpdatesAsync()
    {
        if (!mgr.IsInstalled)
        {
            Console.WriteLine("Update check skipped: application is not installed.");
            return;
        }

        UpdateInfo? updateInfo = await mgr.CheckForUpdatesAsync();
        if (updateInfo == null)
        {
            Console.WriteLine("No updates available.");
            return;
        }

        UpdateInfo = updateInfo;

        Console.WriteLine("Update available.");
        await mgr.DownloadUpdatesAsync(UpdateInfo);
    }
}