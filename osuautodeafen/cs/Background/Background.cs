using System;
using System.Text.Json;

namespace osuautodeafen;

public class Background
{
    /// <summary>
    ///     Gets the full background directory from the provided JSON string.
    /// </summary>
    /// <param name="json"></param>
    /// <returns></returns>
    public string? GetFullBackgroundDirectory(string? json)
    {
        Console.WriteLine("getting full background directory");
        using (JsonDocument document = JsonDocument.Parse(json))
        {
            string? settingsSongsDirectory = string.Empty;
            string? fullPath = string.Empty;

            if (document.RootElement.TryGetProperty("folders", out JsonElement folders) &&
                folders.TryGetProperty("songs", out JsonElement songs))
                settingsSongsDirectory = songs.GetString();

            if (document.RootElement.TryGetProperty("directPath", out JsonElement directPath) &&
                directPath.TryGetProperty("beatmapBackground", out JsonElement beatmapBackground))
                fullPath = beatmapBackground.GetString();
            string combinedPath = settingsSongsDirectory + "\\" + fullPath;
            //Console.WriteLine(Path.Combine(settingsSongsDirectory, fullPath));
            return combinedPath;
        }
    }
}