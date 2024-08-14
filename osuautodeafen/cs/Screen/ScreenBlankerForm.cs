using System;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;

namespace osuautodeafen.cs.Screen;

public class ScreenBlankerForm : IDisposable
{
    private readonly Window _mainWindow;
    private ScreenBlankerWindow[]? _blankingWindows;
    private DispatcherTimer _focusCheckTimer;
    private bool _isInitialized;
    private bool _isOsuFocused;
    private readonly Deafen _deafen;
    private bool screenBlankEnabled;

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
            //uh this is really an rng number
            //original value 1250, currently 400 because
            //i dont like waiting to interact with monitors
            Interval = TimeSpan.FromMilliseconds(400)
        };
        _focusCheckTimer.Tick += (sender, e) => CheckOsuFocus();
        _focusCheckTimer.Start();
    }

    public static Task<ScreenBlankerForm> CreateAsync(Window mainWindow)
    {
        return Task.FromResult(new ScreenBlankerForm(mainWindow));
    }

    private void InitializeBlankingWindows()
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
            _blankingWindows[i] = window;
        }

        _isInitialized = true;
    }

    private bool CheckOsuFocus()
    {
        var focusedProcess = GetFocusedProcess();
        if (focusedProcess != null && focusedProcess.ProcessName == "osu!")
        {
            if (!_isOsuFocused)
            {
                _isOsuFocused = true;
                SetBlankingWindowsTopmost(true);
            }

            return true;
        }

        if (_isOsuFocused)
        {
            _isOsuFocused = false;
            SetBlankingWindowsTopmost(false, true);
        }

        return false;
    }

    private void SetBlankingWindowsTopmost(bool topmost, bool bottommost = false)
    {
        //screenBlankEnabled = _deafen.screenBlankEnabled;
        if (_blankingWindows != null)
            foreach (var window in _blankingWindows)
                //if (screenBlankEnabled)
                //{
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
               // }
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