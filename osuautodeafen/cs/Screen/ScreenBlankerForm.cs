using System;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
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

        if (OperatingSystem.IsWindows())
        {
            // This should probably be moved to be called only when the toggle is enabled, and vice-versa
            InitializeBlankingWindows();

            _winEventDelegate = WinEventProc;
            _winEventHook = SetWinEventHook(
                EVENT_SYSTEM_FOREGROUND,
                EVENT_SYSTEM_FOREGROUND,
                IntPtr.Zero,
                _winEventDelegate,
                0,
                0,
                WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS
            );
        }
        else
        {
            Console.WriteLine("Screen blanking is only supported on Windows.");
        }

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

        if (_winEventHook != IntPtr.Zero) UnhookWinEvent(_winEventHook);

        if (_mouseHook != IntPtr.Zero) UnhookWindowsHookEx(_mouseHook);
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

        //_mouseProc = MouseHookCallback;
        //_mouseHook = SetWindowsHookEx(WH_MOUSE_LL, _mouseProc, IntPtr.Zero, 0);
    }

    private IntPtr MouseHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        // this creates major major major cursor lag for god knows what reason. this function
        // would be ideal to check intent but for now mouse clicks are the best we can do
        if (nCode >= 0 && (wParam == WM_LBUTTONDOWN || wParam == WM_RBUTTONDOWN))
        {
            var currentMouseEventTime = DateTime.Now;
            if ((currentMouseEventTime - _lastMouseEventTime).TotalMilliseconds > 50)
            {
                _lastMouseEventTime = currentMouseEventTime;
                Task.Run(CheckMouseClickOutsideOsuAsync);
            }
        }

        return CallNextHookEx(_mouseHook, nCode, wParam, lParam);
    }

    private async Task CheckMouseClickOutsideOsuAsync()
    {
        var focusedProcess = GetFocusedProcess();
        if (focusedProcess is { ProcessName: not "osu!" })
            await Dispatcher.UIThread.InvokeAsync(() => SetBlankingWindowsTopmost(false, true));
    }

    private async void WinEventProc(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild,
        uint dwEventThread, uint dwmsEventTime)
    {
        if (eventType == EVENT_SYSTEM_FOREGROUND) await Task.Run(CheckOsuFocus);
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

    private Process? GetFocusedProcess()
    {
        var hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero) return null;

        GetWindowThreadProcessId(hwnd, out var pid);
        return Process.GetProcessById((int)pid);
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
#if WINDOWS

    private readonly IntPtr _winEventHook;
    private readonly WinEventDelegate _winEventDelegate;
    private IntPtr _mouseHook;
    private LowLevelMouseProc _mouseProc;

    private const uint EVENT_SYSTEM_FOREGROUND = 0x0003;
    private const uint WINEVENT_OUTOFCONTEXT = 0x0000;
    private const uint WINEVENT_SKIPOWNPROCESS = 0x0002;
    private const int WH_MOUSE_LL = 14;
    private const int WM_LBUTTONDOWN = 0x0201;
    private const int WM_RBUTTONDOWN = 0x0204;

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    private static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr hmodWinEventProc,
        WinEventDelegate lpfnWinEventProc, uint idProcess, uint idThread, uint dwFlags);

    [DllImport("user32.dll")]
    private static extern bool UnhookWinEvent(IntPtr hWinEventHook);

    private delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    private delegate void WinEventDelegate(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild,
        uint dwEventThread, uint dwmsEventTime);
#endif
}