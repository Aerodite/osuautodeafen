using System;
using System.Threading.Tasks;
using osuautodeafen;
using Velopack;
using Velopack.Sources;

public class UpdateChecker
{
    public const string CurrentVersion = "1.1.0";

    private static readonly GithubSource updateSource = new("https://github.com/Aerodite/osuautodeafen",
        null, false);

    private MainWindow _mainWindow;
    public UpdateManager Mgr = new(updateSource);
    public UpdateInfo? UpdateInfo;

    /// <summary>
    /// Initializes a new instance of the <see cref="UpdateChecker"/> class.
    /// </summary>
    public async Task CheckForUpdatesAsync()
    {
        if (!Mgr.IsInstalled)
        {
            Console.WriteLine("Update check skipped: application is not installed.");
            return;
        }

        UpdateInfo? updateInfo = await Mgr.CheckForUpdatesAsync();
        if (updateInfo == null)
        {
            Console.WriteLine("No updates available.");
            return;
        }

        UpdateInfo = updateInfo;

        Console.WriteLine("Update available.");
        await Mgr.DownloadUpdatesAsync(UpdateInfo);
    }
}