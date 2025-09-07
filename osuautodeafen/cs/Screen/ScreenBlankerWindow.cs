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
        SystemDecorations = SystemDecorations.None;
        TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };
        Opacity = 0; // Start with opacity 0
        IsVisible = true;
        Show(); // Ensure the window is created

        object? platformImpl = GetType().GetProperty("PlatformImpl", BindingFlags.NonPublic | BindingFlags.Instance)
            ?.GetValue(this);
        IntPtr? handle = platformImpl?.GetType().GetProperty("Handle", BindingFlags.NonPublic | BindingFlags.Instance)
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

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);
}