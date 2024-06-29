using System;
using System.IO;
using System.Text.Json;

namespace osuautodeafen
{
    public class Background
    {
        public string? GetFullBackgroundDirectory(string json)
        {
            using (JsonDocument document = JsonDocument.Parse(json))
            {
                string settingsSongsDirectory = document.RootElement.GetProperty("settings").GetProperty("folders").GetProperty("songs").GetString();
                string fullPath = document.RootElement.GetProperty("menu").GetProperty("bm").GetProperty("path").GetProperty("full").GetString();

                Console.WriteLine(Path.Combine(settingsSongsDirectory, fullPath));
                return Path.Combine(settingsSongsDirectory, fullPath);
            }
        }
    }
}