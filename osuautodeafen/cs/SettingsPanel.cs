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
    private double _minCompletionPercentage = 75; // Default value
    public int minPP { get; set; }

    public float minSR { get; set; }

    public static readonly AvaloniaProperty<double> PPThresholdProperty = AvaloniaProperty.Register<SettingsPanel, double>("PPThreshold", 0);
    public static readonly AvaloniaProperty<double> SRThresholdProperty = AvaloniaProperty.Register<SettingsPanel, double>("SRThreshold", 0);

    public double SRThreshold
    {
        get { return (double)GetValue(SRThresholdProperty); }
        set { SetValue(SRThresholdProperty, value); }
    }

    public double PPThreshold
    {
        get { return (double)(GetValue(PPThresholdProperty) ?? 0); }
        set { SetValue(PPThresholdProperty, value); }
    }

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
                            case "SRThreshold":
                                SRThreshold = value;
                                break;
                            case "PPThreshold":
                                PPThreshold = value;
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
                writer.WriteLine($"SRThreshold = {SRThreshold}");
                writer.WriteLine($"PPThreshold = {PPThreshold}");
            }
        }
    }
    public void ChangeMinCompletionPercentage(double newPercentage)
    {
        MinCompletionPercentage = newPercentage;
        _deafen.UpdateMinCompletionPercentage(MinCompletionPercentage);

        string settingsFilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "osuautodeafen", "settings.txt");

        // Create a dictionary with the settings
        var settings = new Dictionary<string, double>
        {
            { "MinCompletionPercentage", MinCompletionPercentage },
            { "SRThreshold", SRThreshold },
            { "PPThreshold", PPThreshold }
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