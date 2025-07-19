using Avalonia.Controls;
using Avalonia.Threading;

namespace osuautodeafen.cs.Logo;

public class AppIconSetter
{
    public static void SetIcon(Window window, string iconResourceName)
    {
        var iconStream = LoadEmbeddedResource(iconResourceName);
        if (iconStream != null)
        {
            Dispatcher.UIThread.InvokeAsync(() =>
            {
                window.Icon = new WindowIcon(iconStream);
            });
        }
    }

    private static System.IO.Stream? LoadEmbeddedResource(string resourceName)
    {
        var assembly = typeof(AppIconSetter).Assembly;
        return assembly.GetManifestResourceStream(resourceName);
    }
}