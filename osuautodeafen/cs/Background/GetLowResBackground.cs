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

    /// <summary>
    ///     Attempts to get the path to the low resolution background image for the current beatmap
    ///     (The one used in the beatmap carousel on stable, if on lazer, it just ignores it and uses the regular image)
    /// </summary>
    /// <returns></returns>
    public string? GetLowResBitmapPath()
    {
        string? osuFolderPath = _tosuApi.GetGameDirectory();

        // on Linux, map Wine's D:\ to the real osu! folder
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) && osuFolderPath == "D:\\")
        {
            string home = Environment.GetEnvironmentVariable("HOME") ?? "";
            osuFolderPath = Path.Combine(home, ".local", "share", "osu-wine", "osu!");
        }

        if (string.IsNullOrEmpty(osuFolderPath))
        {
            Console.WriteLine("osuFolderPath is null or empty");
            return null;
        }

        Console.WriteLine($"Osu Folder Path: {osuFolderPath}");

        string beatmapId = _tosuApi.GetBeatmapId().ToString();
        if (string.IsNullOrEmpty(beatmapId))
        {
            Console.WriteLine("[ERROR] beatmapId is null or empty");
            return null;
        }

        string path1 = Path.Combine(osuFolderPath, "Data", "bt", beatmapId + ".jpg");
        string path2 = Path.Combine(osuFolderPath, "Data", "bt", beatmapId + "l.jpg");

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
        string _backgroundPath = _tosuApi.GetBackgroundPath();
        return _backgroundPath;
    }
}