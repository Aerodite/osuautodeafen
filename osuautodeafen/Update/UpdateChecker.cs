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
    public const string CurrentVersion = "1.1.3";

    public const string CurrentVersionNumeric = "113";

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
            Serilog.Log.Warning("Update check skipped: Velopack not in use.");
            return;
        }

        UpdateInfo? updateInfo = await Mgr.CheckForUpdatesAsync();
        if (updateInfo == null)
        {
            Serilog.Log.Information("No updates available.");
            return;
        }

        UpdateInfo = updateInfo;

        Serilog.Log.Information("Update available.");
        await Mgr.DownloadUpdatesAsync(UpdateInfo);
    }

    /// <summary>
    ///     Displays the update notification bar and initializes progress bar
    /// </summary>
    public async Task ShowUpdateNotification()
    {
        Serilog.Log.Debug("Showing Update Notification");

        _updateNotificationBarButton.IsVisible = true;
        _updateProgressBar.Value = 0;
        _updateProgressBar.Foreground = Brushes.Green;
    }
}