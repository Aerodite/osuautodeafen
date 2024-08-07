using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;

namespace osuautodeafen.cs.Screen;

public class ScreenBlanker
{
    private readonly Window[] _blankingWindows;

    public ScreenBlanker(Window mainWindow)
    {
        var screens = mainWindow.Screens.All.Where(screen => !screen.IsPrimary).ToList();
        _blankingWindows = new Window[screens.Count];

        for (int i = 0; i < screens.Count; i++)
        {
            var screen = screens[i];
            var scaling = screen.Scaling;
            var window = new Window
            {
                Background = Brushes.Black,
                WindowState = WindowState.FullScreen,
                WindowStartupLocation = WindowStartupLocation.Manual,
                Position = new PixelPoint((int)(screen.Bounds.X * scaling), (int)(screen.Bounds.Y * scaling)),
                Width = screen.Bounds.Width * scaling,
                Height = screen.Bounds.Height * scaling,
                ShowInTaskbar = false,
                Topmost = true,
                CanResize = false,
                IsVisible = false
            };
            _blankingWindows[i] = window;
        }
    }

    public void BlankScreens()
    {
        Dispatcher.UIThread.InvokeAsync(() =>
        {
            foreach (var window in _blankingWindows)
            {
                window.Show();
            }
        });
    }

    public void UnblankScreens()
    {
        Dispatcher.UIThread.InvokeAsync(() =>
        {
            foreach (var window in _blankingWindows)
            {
                window.Hide();
            }
        });
    }
}