using System;
using System.Diagnostics;
using Microsoft.Win32;

namespace osuautodeafen.cs;

public abstract class TosuLauncher
{
    public static bool IsTosuRunning { get; set; } = false;

    //<summary>
    // ensures that tosu is running, if not, it will attempt to start it
    //</summary>
    public static void EnsureTosuRunning()
    {
        string tosuPath = GetTosuPathFromRegistry();

        if (string.IsNullOrEmpty(tosuPath))
        {
            Console.WriteLine("Tosu path could not be determined from the registry.");
            return;
        }

        tosuPath = tosuPath.Replace(".FriendlyAppName", "");

        var tosuProcesses = Process.GetProcessesByName("tosu");
        if (tosuProcesses.Length == 0)
        {
            Console.WriteLine("Tosu is not running. Attempting to start Tosu...");
            IsTosuRunning = false;
            try
            {
                Process.Start(tosuPath);
                Console.WriteLine("Tosu started successfully.");
                IsTosuRunning = true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to start Tosu: {ex.Message}");
            }
        }
        else
        {
            Console.WriteLine("Tosu is already running.");
            IsTosuRunning = true;
        }
    }

    //<remarks>
    // for the record this might be a bit fucky if for whatever reason tosu changes it's value. but oh well
    //</remarks>
    private static string GetTosuPathFromRegistry()
    {
        const string keyPath = @"SOFTWARE\Classes\Local Settings\Software\Microsoft\Windows\Shell\MuiCache";
        string tosuPath = null;

        using (RegistryKey key = Registry.CurrentUser.OpenSubKey(keyPath))
        {
            if (key != null)
            {
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
        }

        return tosuPath;
    }
}