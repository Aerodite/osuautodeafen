using System;
using System.IO;
using System.Runtime.InteropServices;

namespace osuautodeafen.cs;

public class GetLowResBackground
{
    private readonly TosuApi _tosuApi;

    public GetLowResBackground(TosuApi tosuApi)
    {
        _tosuApi = tosuApi;
    }

    public string? GetLowResBitmapPath()
    {
        var osuFolderPath = _tosuApi.GetGameDirectory();

        // on Linux, map Wine's D:\ to the real osu! folder
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) && osuFolderPath == "D:\\")
        {
            var home = Environment.GetEnvironmentVariable("HOME") ?? "";
            osuFolderPath = Path.Combine(home, ".local", "share", "osu-wine", "osu!");
        }

        if (string.IsNullOrEmpty(osuFolderPath))
        {
            Console.WriteLine("osuFolderPath is null or empty");
            return null;
        }

        Console.WriteLine($"Osu Folder Path: {osuFolderPath}");

        var beatmapId = _tosuApi.GetBeatmapId().ToString();
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

        if (File.Exists(path1))
        {
            Console.WriteLine($"Path exists: {path1}");
            return path1;
        }

        //just return the normal background (really only affects lazer)
        Console.WriteLine("No path exists, just using high res background");
        var _backgroundPath = _tosuApi.GetBackgroundPath();
        return _backgroundPath;
    }
}