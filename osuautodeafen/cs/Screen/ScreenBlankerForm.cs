using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;
using osuautodeafen.cs.Screen;

public class ScreenBlankerForm : IDisposable
{
    private readonly Window _mainWindow;
    private ScreenBlankerWindow[]? _blankingWindows;
    private bool _isHandlingFocusChange;
    private bool _isInitialized;
    private bool _isOsuFocused;
    private DateTime _lastFocusChangeTime;
    private DateTime _lastMouseEventTime = DateTime.MinValue;

    public ScreenBlankerForm(Window mainWindow)
    {
        Console.WriteLine(@"Initializing ScreenBlankerForm...");
        _mainWindow = mainWindow;

        _mainWindow.Closed += (sender, e) => Dispose();
    }

    public bool IsScreenBlanked { get; private set; }

    public void Dispose()
    {
        if (_blankingWindows != null)
        {
            foreach (var window in _blankingWindows)
                window.Close();
            _blankingWindows = null;
        }
    }

    public static Task<ScreenBlankerForm> CreateAsync(Window mainWindow)
    {
        return Task.FromResult(new ScreenBlankerForm(mainWindow));
    }

    public void InitializeBlankingWindows()
    {
        if (_isInitialized) return;
        var screens = _mainWindow.Screens.All.Where(screen => !screen.IsPrimary).ToList();
        _blankingWindows = new ScreenBlankerWindow[screens.Count];

        for (var i = 0; i < screens.Count; i++)
        {
            var screen = screens[i];
            var pixelBounds = ScreenBlankerHelper.GetPixelBounds(screen);
            var window = new ScreenBlankerWindow($"ScreenBlankerWindow_{i}", pixelBounds, screen.Scaling)
            {
                IsVisible = false,
                IsHitTestVisible = false,
                Focusable = false
            };
            _blankingWindows[i] = window;
        }

        _isInitialized = true;
    }

    public void SetBlankingWindowsTopmost(bool topmost, bool bottommost = false)
    {
        if (_blankingWindows == null) return;

        foreach (var window in _blankingWindows)
            if (bottommost)
            {
                window.Topmost = false;
                window.IsVisible = false;
            }
            else if (topmost && !window.IsVisible)
            {
                window.Topmost = true;
                window.IsVisible = true;
            }
    }

    public async Task BlankScreensAsync()
    {
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            Console.WriteLine(@"Blanking screens...");
            if (_blankingWindows != null)
                foreach (var window in _blankingWindows)
                    window.Opacity = 1;
            IsScreenBlanked = true;
        });
    }

    public async Task UnblankScreensAsync()
    {
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            Console.WriteLine(@"Unblanking screens...");
            if (_blankingWindows != null)
                foreach (var window in _blankingWindows)
                    window.Opacity = 0;
            IsScreenBlanked = false;
        });
    }

    private async Task CheckMouseClickOutsideOsuAsync()
    {
        var focusedProcess = GetFocusedProcess();
        if (focusedProcess is { ProcessName: not "osu!" })
            await Dispatcher.UIThread.InvokeAsync(() => SetBlankingWindowsTopmost(false, true));
    }

    private bool CheckOsuFocus()
    {
        if (_isHandlingFocusChange || (DateTime.Now - _lastFocusChangeTime).TotalMilliseconds < 200)
            return _isOsuFocused;

        _isHandlingFocusChange = true;

        var focusedProcess = GetFocusedProcess();
        if (focusedProcess != null && focusedProcess.ProcessName is "osu!")
        {
            if (!_isOsuFocused)
            {
                _isOsuFocused = true;
                Dispatcher.UIThread.InvokeAsync(() => SetBlankingWindowsTopmost(true)).Wait();
            }
        }
        else
        {
            if (_isOsuFocused)
            {
                _isOsuFocused = false;
                Dispatcher.UIThread.InvokeAsync(() => SetBlankingWindowsTopmost(false, true)).Wait();
            }
        }

        _lastFocusChangeTime = DateTime.Now;
        _isHandlingFocusChange = false;

        return _isOsuFocused;
    }

    private Process? GetFocusedProcess()
    {
        // var hwnd = GetForegroundWindow();
        // if (hwnd == IntPtr.Zero) return null;
        //
        // GetWindowThreadProcessId(hwnd, out var pid);
        // return Process.GetProcessById((int)pid)
        //;
        return null;
    }
}