using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace osuautodeafen.cs;

public class TosuLauncher
{
    /// <summary>
    ///     Ensures that Tosu is running. If it is not running, attempts to start it
    /// </summary>
    public static void EnsureTosuRunning()
    {
        string tosuPath = GetTosuPath();
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

    /// <summary>
    ///     Attempts to retrieve the Tosu installation path from the Windows Registry
    /// </summary>
    /// <returns></returns>
    private static string GetTosuPathFromRegistry()
    {
        const string keyPath = @"SOFTWARE\Classes\Local Settings\Software\Microsoft\Windows\Shell\MuiCache";
        string tosuPath = null;

        using (RegistryKey? key = Registry.CurrentUser.OpenSubKey(keyPath))
        {
            if (key != null)
                foreach (string valueName in key.GetValueNames())
                {
                    string? value = key.GetValue(valueName) as string;
                    if (value != null && value.Contains("osu! memory reader, built in typescript"))
                    {
                        tosuPath = valueName;
                        break;
                    }
                }
        }

        return tosuPath;
    }

    /// <summary>
    ///     Checks if Tosu is currently running
    /// </summary>
    /// <returns></returns>
    public static bool IsTosuRunning()
    {
        var processes = Process.GetProcessesByName("tosu");
        foreach (Process proc in processes)
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

    /// <summary>
    ///     Gets the installation path of Tosu from the Registry
    /// </summary>
    /// <returns></returns>
    public static string GetTosuPath()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) return string.Empty;

        const string keyPath = @"SOFTWARE\tosu";
        using (RegistryKey? key = Registry.LocalMachine.OpenSubKey(keyPath))
        {
            string? path = key?.GetValue("InstallPath") as string;
            if (!string.IsNullOrEmpty(path))
                return path;
        }

        var processes = Process.GetProcessesByName("tosu");
        foreach (Process proc in processes)
            try
            {
                string? path = proc.MainModule?.FileName;
                if (!string.IsNullOrEmpty(path))
                    return path;
            }
            catch
            {
            }

        string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        string possiblePath = Path.Combine(programFiles, "Tosu", "tosu.exe");
        if (File.Exists(possiblePath))
            return possiblePath;

        return string.Empty;
    }
}