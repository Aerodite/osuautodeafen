using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Media;
using Serilog;
using Velopack;
using Velopack.Sources;

namespace osuautodeafen.Update;

public sealed class UpdateChecker
{
    public const string CurrentVersion = "1.1.3";
    public const string CurrentVersionNumeric = "113";
    
    private static readonly GithubSource UpdateSource = new(
        "https://github.com/Aerodite/osuautodeafen",
        null,
        false);
    
    public bool IsInstalled => _updateManager.IsInstalled;
    
    private readonly UpdateManager _updateManager = new(UpdateSource);
    private readonly Button? _notificationButton;
    private readonly ProgressBar? _progressBar;
    
    public UpdateInfo? UpdateInfo { get; private set; }

    public UpdateChecker(
        Button? notificationButton,
        ProgressBar? progressBar)
    {
        _notificationButton = notificationButton;
        _progressBar = progressBar;
    }

    public async Task CheckForUpdatesAsync()
    {
        if (!_updateManager.IsInstalled)
        {
            Log.Warning("Update check skipped: Velopack not in use.");
            return;
        }

        UpdateInfo? updateInfo =
            await _updateManager.CheckForUpdatesAsync();

        if (updateInfo is null)
        {
            Log.Information("No updates available.");
            return;
        }

        UpdateInfo = updateInfo;

        Log.Information(
            "Update available: {Version}",
            updateInfo.TargetFullRelease.Version);

        await _updateManager.DownloadUpdatesAsync(updateInfo);
    }

    public void ShowUpdateNotification()
    {
        if (_notificationButton is null ||
            _progressBar is null)
        {
            Log.Warning(
                "Cannot show update notification: UI controls are unavailable.");
            return;
        }

        Log.Debug("Showing update notification.");

        _notificationButton.IsVisible = true;

        _progressBar.Value = 0;
        _progressBar.Foreground = Brushes.Green;
    }
    
    public Task DownloadUpdateAsync(Action<int>? progress = null)
    {
        if (UpdateInfo is null)
            return Task.CompletedTask;

        return _updateManager.DownloadUpdatesAsync(UpdateInfo, progress);
    }

    public void ApplyUpdateAndRestart()
    {
        if (UpdateInfo is null)
            return;

        _updateManager.ApplyUpdatesAndRestart(UpdateInfo);
    }
}