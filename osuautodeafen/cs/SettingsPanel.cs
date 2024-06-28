using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using osuautodeafen;

public class SettingsPanel : Control
{
    private double _minCompletionPercentage = 75;

    public double MinCompletionPercentage
    {
        get { return _minCompletionPercentage; }
        set
        {
            if (_minCompletionPercentage != value)
            {

            }
        }
    }

    private TosuAPI _tosuAPI;
    private Deafen _deafen;

    public SettingsPanel(TosuAPI tosuAPI, Deafen deafen)
    {
        _tosuAPI = tosuAPI;
        _deafen = deafen;

        string settingsFilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "osuautodeafen", "settings.txt");
        if (File.Exists(settingsFilePath))
        {
            using (var reader = new StreamReader(settingsFilePath))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    var parts = line.Split('=');
                    if (parts.Length == 2 && double.TryParse(parts[1], out double value))
                    {
                        switch (parts[0].Trim())
                        {
                            case "MinCompletionPercentage":
                                MinCompletionPercentage = value;
                                break;
                        }
                    }
                }
            }
        }
        else
        {
            Directory.CreateDirectory(Path.GetDirectoryName(settingsFilePath));
            using (var writer = new StreamWriter(settingsFilePath))
            {
                writer.WriteLine($"MinCompletionPercentage = {MinCompletionPercentage}");
            }
        }
    }
    public void ChangeMinCompletionPercentage(double newPercentage)
    {
        MinCompletionPercentage = newPercentage;

        string settingsFilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "osuautodeafen", "settings.txt");

        var settings = new Dictionary<string, double>
        {
            { "MinCompletionPercentage", MinCompletionPercentage },
        };

        // Write the settings to the file
        using (var writer = new StreamWriter(settingsFilePath))
        {
            foreach (var setting in settings)
            {
                writer.WriteLine($"{setting.Key} = {setting.Value}");
            }
        }
    }
}