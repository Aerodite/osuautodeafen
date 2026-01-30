using System;
using System.Net.Http;
using System.Threading.Tasks;
using osuautodeafen.cs.Settings;
using osuautodeafen.cs.Update;
using osuautodeafen.cs.ViewModels;

namespace osuautodeafen.cs.Changelog;

public sealed class ChangelogManager
{
    private readonly HttpClient _http;
    private readonly SettingsHandler _settingsHandler;
    private readonly ChangelogViewModel _changelogViewModel;

    private const string ChangelogUrl =
        "https://i.cdn.aerodite.dev/osuautodeafen/changelog-" + UpdateChecker.CurrentVersionNumeric + ".md";

    public ChangelogManager(
        HttpClient http,
        SettingsHandler settingsHandler,
        ChangelogViewModel changelogViewModel)
    {
        _http = http;
        _settingsHandler = settingsHandler;
        _changelogViewModel = changelogViewModel;
    }

    public async Task TryShowChangelogAsync(string currentVersion)
    {
        try
        {
            if (_settingsHandler.LastSeenVersion == currentVersion)
                return;
            
            VideoPreviewCache.DeleteOldChangelogCaches(currentVersion);

            string markdown = await _http.GetStringAsync(ChangelogUrl);

            _changelogViewModel.LoadFromMarkdown(markdown);
            _changelogViewModel.IsVisible = true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Changelog] Failed to show changelog: {ex}");
        }
    }
    
    public async Task ForceShowChangelogAsync()
    {
        string markdown = await _http.GetStringAsync(ChangelogUrl);
        _changelogViewModel.LoadFromMarkdown(markdown);
        _changelogViewModel.IsVisible = true;
    }
    
    public void DismissChangelog(string currentVersion)
    {
        try
        {
            _settingsHandler.LastSeenVersion = currentVersion;

            _changelogViewModel.Dispose();

            ChangelogDestroyed?.Invoke();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Changelog] Failed to dismiss changelog: {ex}");
        }
    }
    public event Action? ChangelogDestroyed;
}