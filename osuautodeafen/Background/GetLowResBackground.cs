using System;
using System.IO;
using System.Runtime.InteropServices;
using osuautodeafen.Tosu;

namespace osuautodeafen.Background;

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
            Serilog.Log.Debug("osuFolderPath is null or empty");
            return null;
        }

        Serilog.Log.Debug($"osu! folder path: {osuFolderPath}");

        string beatmapId = _tosuApi.GetBeatmapId().ToString();
        if (string.IsNullOrEmpty(beatmapId))
        {
            Serilog.Log.Warning("beatmapId is null or empty");
            return null;
        }

        string path1 = Path.Combine(osuFolderPath, "Data", "bt", beatmapId + ".jpg");
        string path2 = Path.Combine(osuFolderPath, "Data", "bt", beatmapId + "l.jpg");

        Serilog.Log.Debug("Path 1: {Path1}", path1);
        Serilog.Log.Debug("Path 2: {Path2}", path2);

        if (File.Exists(path2))
        {
            Serilog.Log.Debug("Path exists: {Path2}", path2);
            return path2;
        }

        if (File.Exists(path1))
        {
            Serilog.Log.Debug("Path exists: {Path1}", path1);
            return path1;
        }

        //just return the normal background (really only affects lazer)
        Serilog.Log.Debug("No path exists, just using high res background");
        string backgroundPath = _tosuApi.GetBackgroundPath();
        return backgroundPath;
    }
}