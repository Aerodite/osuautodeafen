using System;
using System.Diagnostics;
using Microsoft.Win32;

public class TosuLauncher
{
    public static bool isTosuRunning = false;

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
            isTosuRunning = false;
            try
            {
                Process.Start(tosuPath);
                Console.WriteLine("Tosu started successfully.");
                isTosuRunning = true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to start Tosu: {ex.Message}");
            }
        }
        else
        {
            Console.WriteLine("Tosu is already running.");
            isTosuRunning = true;
        }
    }

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