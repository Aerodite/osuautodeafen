using System;
using System.Threading.Tasks;
using Serilog;
using Velopack;
using Velopack.Sources;

namespace osuautodeafen.Update;

public sealed class UpdateChecker
{
    public const string CurrentVersion = "1.1.5";
    public const string CurrentVersionNumeric = "115";

    private static readonly GithubSource UpdateSource = new(
        "https://github.com/Aerodite/osuautodeafen",
        null,
        false);

    private readonly UpdateManager _updateManager = new(UpdateSource);
    public string? NewestAvailableVersion { get; private set; }

    public bool IsInstalled => _updateManager.IsInstalled;

    public UpdateInfo? UpdateInfo { get; private set; }

    public async Task CheckForUpdatesAsync()
    {
        UpdateInfo = null;
        NewestAvailableVersion = null;

        if (!_updateManager.IsInstalled)
        {
            Log.Warning("Update check skipped: Velopack not in use.");
            return;
        }

        UpdateInfo = await _updateManager.CheckForUpdatesAsync();

        if (UpdateInfo is null)
        {
            Log.Information("No updates available.");
            return;
        }

        NewestAvailableVersion = UpdateInfo.TargetFullRelease.Version.ToString();

        Log.Information("Update available: {Version}", NewestAvailableVersion);
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