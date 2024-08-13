using Avalonia;

namespace osuautodeafen.cs.Screen;

public static class ScreenBlankerHelper
{
    public static PixelRect GetPixelBounds(Avalonia.Platform.Screen screen)
    {
        return new PixelRect(
            (int)(screen.Bounds.X * screen.Scaling),
            (int)(screen.Bounds.Y * screen.Scaling),
            (int)(screen.Bounds.Width * screen.Scaling),
            (int)(screen.Bounds.Height * screen.Scaling)
        );
    }
}