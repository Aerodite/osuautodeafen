using System;
using System.Reflection;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace osuautodeafen.cs.Screen;

public class ScreenBlankerWindow : Window
{
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TRANSPARENT = 0x00000020;

    public ScreenBlankerWindow(string name, PixelRect bounds, double scaling)
    {
        Focusable = false;
        Name = name;
        Background = Brushes.Black;
        WindowState = WindowState.FullScreen;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Position = new PixelPoint((int)(bounds.X * scaling), (int)(bounds.Y * scaling));
        Width = bounds.Width * scaling;
        Height = bounds.Height * scaling;
        ShowInTaskbar = false;
        Topmost = true;
        CanResize = false;
        IsVisible = false;
        SystemDecorations = SystemDecorations.None;
        TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };
        Show(); // make sure the window is created

        Opacity = 0; // set to zero so it exists in the background

        var platformImpl = GetType().GetProperty("PlatformImpl", BindingFlags.NonPublic | BindingFlags.Instance)
            ?.GetValue(this);
        var handle = platformImpl?.GetType().GetProperty("Handle", BindingFlags.NonPublic | BindingFlags.Instance)
            ?.GetValue(platformImpl) as IntPtr?;
        if (handle.HasValue)
            SetWindowLong(handle.Value, GWL_EXSTYLE, GetWindowLong(handle.Value, GWL_EXSTYLE) | WS_EX_TRANSPARENT);
    }

    public string Name { get; }

    public sealed override void Hide()
    {
        base.Hide();
    }

    public sealed override void Show()
    {
        base.Show();
    }

    public void Blank()
    {
        Console.WriteLine($@"Blanking window: {Name}");
        Opacity = 1;
    }

    public void Unblank()
    {
        Console.WriteLine($@"Unblanking window: {Name}");
        Opacity = 0;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);
}