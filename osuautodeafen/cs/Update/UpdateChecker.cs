using System;
using System.Threading.Tasks;
using osuautodeafen;
using Velopack;
using Velopack.Sources;

public class UpdateChecker
{
    public static string currentVersion = "1.0.8";
    private MainWindow _mainWindow;
    private static GithubSource updateSource = new GithubSource("https://github.com/Aerodite/osuautodeafen",
        null, false);
    public UpdateManager mgr = new UpdateManager(updateSource);
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