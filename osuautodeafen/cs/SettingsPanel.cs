using System;
using System.ComponentModel;
using System.IO;
using Avalonia.Controls;

namespace osuautodeafen.cs;

public class SettingsPanel : Control
{
    private static readonly string SettingsFilePath =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "osuautodeafen",
            "settings.txt");

    public static readonly object Lock = new();

    private double _minCompletionPercentage = 60;
    private double _performancePoints;
    private double _starRating;

    public SettingsPanel()
    {
        LoadSettings();
    }

    public double MinCompletionPercentage
    {
        get => _minCompletionPercentage;
        set
        {
            if (_minCompletionPercentage == value) return;
            _minCompletionPercentage = value;
            OnPropertyChanged(nameof(MinCompletionPercentage));
            SaveSettings();
        }
    }

    public double StarRating
    {
        get => _starRating;
        set
        {
            if (_starRating == value) return;
            _starRating = value;
            OnPropertyChanged(nameof(StarRating));
            SaveSettings();
        }
    }

    public double PerformancePoints
    {
        get => _performancePoints;
        set
        {
            if (_performancePoints == value) return;
            _performancePoints = value;
            OnPropertyChanged(nameof(PerformancePoints));
            SaveSettings();
        }
    }

    public new event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private void LoadSettings()
    {
        if (!File.Exists(SettingsFilePath))
        {
            /*
        Create the settings file and entries for pp, sr, and mcp because turns out I broke
        the settings logic somehow 💀
        */
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsFilePath)!);
            File.Create(SettingsFilePath).Close();
            using var streamWriter = new StreamWriter(SettingsFilePath);
            streamWriter.Write("MinCompletionPercentage=60\nStarRating=0\nPerformancePoints=0");
        }

        try
        {
            using var streamReader = new StreamReader(SettingsFilePath);
            string? line;
            while ((line = streamReader.ReadLine()) != null)
            {
                var parts = line.Split('=');
                if (parts.Length != 2 || !double.TryParse(parts[1], out var value)) continue;
                switch (parts[0].Trim())
                {
                    case "MinCompletionPercentage":
                        MinCompletionPercentage = value;
                        break;
                    case "StarRating":
                        StarRating = value;
                        break;
                    case "PerformancePoints":
                        PerformancePoints = value;
                        break;
                }
            }
        }
        catch (IOException ex)
        {
            Console.WriteLine($"An error occurred while loading settings: {ex.Message}");
        }
    }

    public void SaveSettings()
    {
        lock (Lock)
        {
            using var fileStream = new FileStream(SettingsFilePath, FileMode.Create, FileAccess.Write, FileShare.None);
            using var streamWriter = new StreamWriter(fileStream);
            streamWriter.Write(
                $"MinCompletionPercentage={MinCompletionPercentage}\nStarRating={StarRating}\nPerformancePoints={PerformancePoints}");
        }
    }
}