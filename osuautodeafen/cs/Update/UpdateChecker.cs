using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Media;
using Velopack;
using Velopack.Sources;

namespace osuautodeafen.cs.Update;

public class UpdateChecker
{
    /// <summary>
    ///     The current version of osuautodeafen
    /// </summary>
    public const string CurrentVersion = "1.1.2";
    public const string CurrentVersionNumeric = "112";

    private static readonly GithubSource UpdateSource = new("https://github.com/Aerodite/osuautodeafen",
        null, false);

    private readonly Button? _updateNotificationBarButton;
    private readonly ProgressBar? _updateProgressBar;

    public readonly UpdateManager Mgr = new(UpdateSource);
    public UpdateInfo? UpdateInfo;

    public UpdateChecker(Button? notificationBar, ProgressBar? progressBar)
    {
        _updateNotificationBarButton = notificationBar;
        _updateProgressBar = progressBar;
    }

    private bool ShouldShowChangelog(string lastSeen)
    {
        return lastSeen != CurrentVersion;
    }

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

    /// <summary>
    ///     Displays the update notification bar and initializes progress bar
    /// </summary>
    public async Task ShowUpdateNotification()
    {
        Console.WriteLine("Showing Update Notification");

        _updateNotificationBarButton.IsVisible = true;
        _updateProgressBar.Value = 0;
        _updateProgressBar.Foreground = Brushes.Green;
    }
}