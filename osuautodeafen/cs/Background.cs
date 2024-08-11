using System;
using System.Text.Json;

namespace osuautodeafen;

public class Background
{
    public string? GetFullBackgroundDirectory(string? json)
    {
        Console.WriteLine("getting full background directory");
        using (var document = JsonDocument.Parse(json))
        {
            var settingsSongsDirectory = string.Empty;
            var fullPath = string.Empty;

            if (document.RootElement.TryGetProperty("folders", out var folders) &&
                folders.TryGetProperty("songs", out var songs))
                settingsSongsDirectory = songs.GetString();

            if (document.RootElement.TryGetProperty("directPath", out var directPath) &&
                directPath.TryGetProperty("beatmapBackground", out var beatmapBackground))
                fullPath = beatmapBackground.GetString();
            var combinedPath = settingsSongsDirectory + "\\" + fullPath;
            //Console.WriteLine(Path.Combine(settingsSongsDirectory, fullPath));
            return combinedPath;
        }
    }
}