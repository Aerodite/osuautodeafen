using Avalonia.Controls;
using Avalonia.Threading;
using System.IO;

public static class AppIconSetter
{
    private static Stream? _previousIconStream;

    public static void SetIcon(Window window, string iconResourceName)
    {
        var iconStream = LoadEmbeddedResource(iconResourceName);
        if (iconStream != null)
        {
            Dispatcher.UIThread.InvokeAsync(() =>
            {
                // Dispose previous stream if exists
                _previousIconStream?.Dispose();
                window.Icon = new WindowIcon(iconStream);
                _previousIconStream = iconStream;
            });
        }
    }

    private static Stream? LoadEmbeddedResource(string resourceName)
    {
        var assembly = typeof(AppIconSetter).Assembly;
        return assembly.GetManifestResourceStream(resourceName);
    }
}