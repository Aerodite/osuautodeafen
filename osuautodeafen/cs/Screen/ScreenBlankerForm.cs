using System;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;
using osuautodeafen;
using osuautodeafen.cs.Screen;

public class ScreenBlankerForm : IDisposable
{
    private readonly Window _mainWindow;
    private ScreenBlankerWindow[]? _blankingWindows;
    private DispatcherTimer _focusCheckTimer;
    private bool _isInitialized;
    private bool _isOsuFocused;
    private readonly Deafen _deafen;
    private bool screenBlankEnabled;
    private bool _isHandlingFocusChange;
    private DateTime _lastFocusChangeTime;
    private bool isScreenBlankEnabled;

    public ScreenBlankerForm(Window mainWindow)
    {
        Console.WriteLine(@"Initializing ScreenBlankerForm...");
        _mainWindow = mainWindow;
        InitializeBlankingWindows();
        InitializeFocusCheckTimer();
        _mainWindow.Closed += (sender, e) => Dispose();
    }

    public bool IsScreenBlanked { get; private set; }

    public void Dispose()
    {
        if (_blankingWindows != null)
        {
            foreach (var window in _blankingWindows) window.Close();
            _blankingWindows = null;
        }
    }

    private void InitializeFocusCheckTimer()
    {
        _focusCheckTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(50)
        };
        _focusCheckTimer.Tick += (sender, e) => CheckOsuFocus();
        _focusCheckTimer.Start();
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
            var window = new ScreenBlankerWindow($"ScreenBlankerWindow_{i}", pixelBounds, screen.Scaling);
            window.IsVisible = false;
            window.Focusable = false;
            _blankingWindows[i] = window;
        }

        _isInitialized = true;
    }

    private bool CheckOsuFocus()
    {
        if(!isScreenBlankEnabled) return false;
        if (_isHandlingFocusChange || (DateTime.Now - _lastFocusChangeTime).TotalMilliseconds < 1500) return false;
        _isHandlingFocusChange = true;

        var focusedProcess = GetFocusedProcess();
        if (focusedProcess != null && focusedProcess.ProcessName == "osu!")
        {
            if (!_isOsuFocused)
            {
                _isOsuFocused = true;
                SetBlankingWindowsTopmost(true);
            }
        }
        else
        {
            if (_isOsuFocused)
            {
                _isOsuFocused = false;
                SetBlankingWindowsTopmost(false, true);
            }
        }

        _lastFocusChangeTime = DateTime.Now;
        _isHandlingFocusChange = false;

        return _isOsuFocused;
    }

    public void SetBlankingWindowsTopmost(bool topmost, bool bottommost = false)
    {
        if (_blankingWindows != null)
            foreach (var window in _blankingWindows)
                if (bottommost)
                {
                    window.Topmost = false;
                    window.IsVisible = false;
                }
                else
                {
                    window.Topmost = topmost;
                    window.IsVisible = true;
                }
    }

    private Process? GetFocusedProcess()
    {
        var hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero) return null;

        GetWindowThreadProcessId(hwnd, out var pid);
        return Process.GetProcessById((int)pid);
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

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
        await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            Console.WriteLine(@"Unblanking screens...");
            if (_blankingWindows != null)
                foreach (var window in _blankingWindows)
                    window.Opacity = 0;
            IsScreenBlanked = false;
        });
    }
}