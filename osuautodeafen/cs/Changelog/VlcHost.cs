using LibVLCSharp.Shared;

namespace osuautodeafen.cs.Changelog;

internal static class VlcHost
{
    public static readonly LibVLC Instance = new("--avcodec-hw=none", "--vout=opengl");
}