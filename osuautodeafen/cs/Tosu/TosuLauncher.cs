using System;
using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace osuautodeafen.cs;

public class TosuLauncher
{
    //<summary>
    // ensures that tosu is running, if not, it will attempt to start it
    //</summary>
    public static void EnsureTosuRunning()
    {
        var tosuPath = GetTosuPath();
        if (string.IsNullOrEmpty(tosuPath))
        {
            Console.WriteLine("Tosu path could not be determined.");
            return;
        }

        if (!IsTosuRunning())
        {
            Console.WriteLine("Tosu is not running. Attempting to start Tosu...");
            try
            {
                Process.Start(tosuPath);
                Console.WriteLine("Tosu started successfully.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to start Tosu: {ex.Message}");
            }
        }
        else
        {
            Console.WriteLine("Tosu is already running.");
        }
    }

    //<remarks>
    // for the record this might be a bit fucky if for whatever reason tosu changes it's value. but oh well
    //</remarks>
    private static string GetTosuPathFromRegistry()
    {
        const string keyPath = @"SOFTWARE\Classes\Local Settings\Software\Microsoft\Windows\Shell\MuiCache";
        string tosuPath = null;

        using (var key = Registry.CurrentUser.OpenSubKey(keyPath))
        {
            if (key != null)
                foreach (var valueName in key.GetValueNames())
                {
                    var value = key.GetValue(valueName) as string;
                    if (value != null && value.Contains("osu! memory reader, built in typescript"))
                    {
                        tosuPath = valueName;
                        break;
                    }
                }
        }

        return tosuPath;
    }

    public static bool IsTosuRunning()
    {
        var processes = Process.GetProcessesByName("tosu");
        foreach (var proc in processes)
            try
            {
                if (!proc.HasExited)
                    return true;
            }
            catch
            {
                // ignore processes that can't be accessed
            }

        return false;
    }

    public static string GetTosuPath()
    {
        const string keyPath = @"SOFTWARE\tosu";
        using (var key = Registry.LocalMachine.OpenSubKey(keyPath))
        {
            var path = key?.GetValue("InstallPath") as string;
            if (!string.IsNullOrEmpty(path))
                return path;
        }

        var processes = Process.GetProcessesByName("tosu");
        foreach (var proc in processes)
            try
            {
                var path = proc.MainModule?.FileName;
                if (!string.IsNullOrEmpty(path))
                    return path;
            }
            catch
            {
            }

        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var possiblePath = Path.Combine(programFiles, "Tosu", "tosu.exe");
        if (File.Exists(possiblePath))
            return possiblePath;

        return string.Empty;
    }
}