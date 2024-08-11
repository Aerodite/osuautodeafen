using System;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;

namespace osuautodeafen.cs.Screen
{
    public class ScreenBlankerForm : IDisposable
    {
        public bool IsScreenBlanked { get; private set; }
        private readonly Window _mainWindow;
        private ScreenBlankerWindow[]? _blankingWindows;

        public ScreenBlankerForm(Window mainWindow)
        {
            Console.WriteLine(@"Initializing ScreenBlankerForm...");
            _mainWindow = mainWindow;
            InitializeBlankingWindows();
            _mainWindow.Closed += (sender, e) => Dispose();
        }

        public static Task<ScreenBlankerForm> CreateAsync(Window mainWindow)
        {
            return Task.FromResult(new ScreenBlankerForm(mainWindow));
        }

        private void InitializeBlankingWindows()
        {
            var screens = _mainWindow.Screens.All.Where(screen => !screen.IsPrimary).ToList();
            _blankingWindows = new ScreenBlankerWindow[screens.Count];

            for (int i = 0; i < screens.Count; i++)
            {
                var screen = screens[i];
                var pixelBounds = ScreenBlankerHelper.GetPixelBounds(screen);
                var window = new ScreenBlankerWindow($"ScreenBlankerWindow_{i}", pixelBounds, screen.Scaling);
                _blankingWindows[i] = window;
            }
        }

        public async Task BlankScreensAsync()
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                Console.WriteLine(@"Blanking screens...");
                if (_blankingWindows == null)
                {
                    InitializeBlankingWindows();
                }

                if (_blankingWindows != null)
                    foreach (var window in _blankingWindows)
                    {
                        window.Opacity = 1;
                    }

                IsScreenBlanked = true;
            });
        }

        public async Task UnblankScreensAsync()
        {
            await Dispatcher.UIThread.InvokeAsync(async () =>
            {
                Console.WriteLine(@"Unblanking screens...");
                if (_blankingWindows != null)
                {
                    foreach (var window in _blankingWindows)
                    {
                        window.Opacity = 0;
                    }
                }
                IsScreenBlanked = false;
            });
        }

        public void Dispose()
        {
            if (_blankingWindows != null)
            {
                foreach (var window in _blankingWindows)
                {
                    window.Close();
                }
                _blankingWindows = null;
            }
        }
    }
}