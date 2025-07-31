using System;
using System.Runtime.InteropServices;
using Avalonia.Controls;

public static class TaskbarIconChanger
{
    private const int WM_SETICON = 0x0080;
    private const int ICON_SMALL = 0;
    private const int ICON_BIG = 1;
    private const int IMAGE_ICON = 1;
    private const int LR_LOADFROMFILE = 0x00000010;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr LoadImage(IntPtr hInstance, string lpFilename, uint uType, int cxDesired,
        int cyDesired, uint fuLoad);

    public static void SetTaskbarIcon(Window window, string imagePath)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return;

        var handle = window.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        if (handle == IntPtr.Zero)
            throw new InvalidOperationException("Could not get native window handle.");

        // Load icons for both sizes
        var hIconSmall = LoadImage(IntPtr.Zero, imagePath, IMAGE_ICON, 16, 16, LR_LOADFROMFILE);
        var hIconBig = LoadImage(IntPtr.Zero, imagePath, IMAGE_ICON, 32, 32, LR_LOADFROMFILE);

        // Set small icon (titlebar)
        SendMessage(handle, WM_SETICON, ICON_SMALL, hIconSmall);
        // Set big icon (taskbar)
        SendMessage(handle, WM_SETICON, ICON_BIG, hIconBig);
    }
}