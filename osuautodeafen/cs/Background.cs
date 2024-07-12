using System;
using System.IO;
using System.Text.Json;

namespace osuautodeafen
{
    public class Background
    {
        public string? GetFullBackgroundDirectory(string? json)
        {
            Console.WriteLine("getting full background directory");
            using (JsonDocument document = JsonDocument.Parse(json))
            {
                string settingsSongsDirectory = string.Empty;
                string fullPath = string.Empty;

                if (document.RootElement.TryGetProperty("folders", out var folders) &&
                    folders.TryGetProperty("songs", out var songs))
                {
                    settingsSongsDirectory = songs.GetString();
                }

                if (document.RootElement.TryGetProperty("directPath", out var directPath) &&
                    directPath.TryGetProperty("beatmapBackground", out var beatmapBackground))
                {
                    fullPath = beatmapBackground.GetString();
                }
                string combinedPath = settingsSongsDirectory + "\\" + fullPath;
                //Console.WriteLine(Path.Combine(settingsSongsDirectory, fullPath));
                return combinedPath;
            }
        }
    }
}