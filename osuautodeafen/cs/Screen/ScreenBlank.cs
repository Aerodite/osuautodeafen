using System;
using System.Threading.Tasks;
using Avalonia.Controls;

namespace osuautodeafen.cs.Screen;

public class ScreenBlanker : IDisposable
{
    private readonly Window _mainWindow;
    private ScreenBlankerForm _screenBlankerForm;

    public ScreenBlanker(Window mainWindow)
    {
        Console.WriteLine("Initializing ScreenBlanker...");
        _mainWindow = mainWindow;
        _mainWindow.Closed += (sender, e) => Dispose();
    }

    public bool IsScreenBlanked => _screenBlankerForm?.IsScreenBlanked ?? false;

    public void Dispose()
    {
        _screenBlankerForm?.Dispose();
        _screenBlankerForm = null;
    }

    public static async Task<ScreenBlanker> CreateAsync(Window mainWindow)
    {
        var screenBlanker = new ScreenBlanker(mainWindow);
        screenBlanker._screenBlankerForm = await ScreenBlankerForm.CreateAsync(mainWindow);
        return screenBlanker;
    }

    public async Task BlankScreensAsync()
    {
        if (_screenBlankerForm != null) await _screenBlankerForm.BlankScreensAsync();
    }

    public async Task UnblankScreensAsync()
    {
        if (_screenBlankerForm != null) await _screenBlankerForm.UnblankScreensAsync();
    }
}