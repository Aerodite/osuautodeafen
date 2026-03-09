using System.IO;
using System.Reflection;
using Avalonia.Controls;
using Avalonia.Threading;

public static class AppIconSetter
{
    private static Stream? _previousIconStream;

    public static void SetIcon(Window window, string iconResourceName)
    {
        Stream? iconStream = LoadEmbeddedResource(iconResourceName);
        if (iconStream != null)
            Dispatcher.UIThread.InvokeAsync(() =>
            {
                // Dispose previous stream if exists
                _previousIconStream?.Dispose();
                window.Icon = new WindowIcon(iconStream);
                _previousIconStream = iconStream;
            });
    }

    private static Stream? LoadEmbeddedResource(string resourceName)
    {
        Assembly assembly = typeof(AppIconSetter).Assembly;
        return assembly.GetManifestResourceStream(resourceName);
    }
}