using System;
using System.IO;

namespace osuautodeafen.cs;

public class GetLowResBackground
{
    private readonly TosuApi _tosuApi;

    public GetLowResBackground(TosuApi tosuApi)
    {
        _tosuApi = tosuApi;
    }

    public string? GetBeatmapId()
    {
        var osuFilePath = _tosuApi.GetOsuFilePath();
        if (string.IsNullOrEmpty(osuFilePath))
        {
            Console.WriteLine("[ERROR] osuFilePath is null or empty");
            return null;
        }

        var beatmapId = osuFilePath.Split(' ')[0];
        Console.WriteLine($"Beatmap ID: {beatmapId}");
        return beatmapId;
    }

    public string? GetLowResBitmapPath()
    {
        var osuFolderPath = _tosuApi.GetGameDirectory();
        if (string.IsNullOrEmpty(osuFolderPath))
        {
            Console.WriteLine("osuFolderPath is null or empty");
            return null;
        }

        Console.WriteLine($"Osu Folder Path: {osuFolderPath}");

        var beatmapId = GetBeatmapId();
        if (string.IsNullOrEmpty(beatmapId))
        {
            Console.WriteLine("[ERROR] beatmapId is null or empty");
            return null;
        }

        var path1 = Path.Combine(osuFolderPath, "Data", "bt", beatmapId + ".jpg");
        var path2 = Path.Combine(osuFolderPath, "Data", "bt", beatmapId + "l.jpg");

        Console.WriteLine($"Path 1: {path1}");
        Console.WriteLine($"Path 2: {path2}");

        if (File.Exists(path2))
        {
            Console.WriteLine($"Path exists: {path2}");
            return path2;
        }
        else if (File.Exists(path1))
        {
            Console.WriteLine($"Path exists: {path1}");
            return path1;
        }
        else
        {
            //just return the normal background (really only affects lazer)
            Console.WriteLine("No path exists, just using high res background");
            string _backgroundPath = _tosuApi.GetBackgroundPath();
            return _backgroundPath;
        }

        Console.WriteLine("No path exists");
        return null;
    }
}