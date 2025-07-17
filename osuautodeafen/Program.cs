using System;
using Avalonia;
using Velopack;

namespace osuautodeafen;

internal class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        VelopackApp.Build()
            .OnFirstRun(v =>
            {
                // open a window to show the user that the app is being initialized
                var initWindow = new MainWindow
                {
                    Title = "hi",
                    Width = 400,
                    Height = 200,
                    Content = "hi"
                };
                initWindow.Show();
            })
            .Run();
        BuildAvaloniaApp()
            .StartWithClassicDesktopLifetime(args);
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
    {
        return AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .UseSkia()
            .WithInterFont();
    }
}