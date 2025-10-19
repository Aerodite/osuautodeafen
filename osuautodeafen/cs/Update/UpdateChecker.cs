using System;
using System.Threading.Tasks;
using Velopack;
using Velopack.Sources;

namespace osuautodeafen.cs.Update;

public class UpdateChecker
{
    /// <summary>
    ///    The current version of osuautodeafen
    /// </summary>
    public const string CurrentVersion = "1.1.1";

    private static readonly GithubSource UpdateSource = new("https://github.com/Aerodite/osuautodeafen",
        null, false);

    public readonly UpdateManager Mgr = new(UpdateSource);
    public UpdateInfo? UpdateInfo;

    /// <summary>
    ///     Checks for updates and downloads them if a new version is available
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