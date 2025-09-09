using System;
using System.Collections.Generic;
using System.IO;

namespace osuautodeafen.cs.Settings.Presets;

public static class PresetManager
{
    public static List<PresetInfo> LoadAllPresets()
    {
        var presets = new List<PresetInfo>();
        string presetsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "osuautodeafen", "presets");
        if (!Directory.Exists(presetsPath)) return presets;

        int idx = 0;
        foreach (var file in Directory.GetFiles(presetsPath, "*.preset.data"))
        {
            var lines = File.ReadAllLines(file);
            var preset = new PresetInfo { FilePath = file, Index = idx++ };
            foreach (var line in lines)
            {
                var trimmedLine = line.Trim();
                if (string.IsNullOrEmpty(trimmedLine) || !trimmedLine.Contains("=")) continue;

                var parts = trimmedLine.Split('=', 2);
                if (parts.Length != 2) continue;

                var key = parts[0].Trim();
                var value = parts[1].Trim();

                switch (key)
                {
                    case "FullBeatmapName": preset.FullBeatmapName = value; break;
                    case "Artist": preset.Artist = value; break;
                    case "BeatmapName": preset.BeatmapName = value; break;
                    case "BeatmapDifficulty": preset.BeatmapDifficulty = value; break;
                    case "BeatmapID": preset.BeatmapID = value; break;
                    case "RankedStatus": preset.RankedStatus = value; break;
                    case "BackgroundPath": preset.BackgroundPath = value; break;
                    case "Mapper": preset.Mapper = value; break;
                    case "Checksum": preset.Checksum = value; break;
                    case "StarRating": preset.StarRating = value; break;
                    case "AverageColor1": preset.AverageColor1 = value; break;
                    case "AverageColor2": preset.AverageColor2 = value; break;
                    case "AverageColor3": preset.AverageColor3 = value; break;
                }
            }
            presets.Add(preset);
        }
        return presets;
    }
}