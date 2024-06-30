using System;
using System.ComponentModel;
using System.IO;
using Avalonia.Controls;
using osuautodeafen;

public class SettingsPanel : Control
{
    private static readonly string SettingsFilePath =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "osuautodeafen",
            "settings.txt");

    public static readonly object _lock = new object();

    private double _minCompletionPercentage = 75;
    private double _starRating;
    private double _performancePoints;

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

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    public SettingsPanel()
    {
        LoadSettings();
    }

    private void LoadSettings()
    {
        if (!File.Exists(SettingsFilePath)) return;
        try
        {
            using (var streamReader = new StreamReader(SettingsFilePath))
            {
                string line;
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
        }
        catch (IOException ex)
        {
            Console.WriteLine($"An error occurred while loading settings: {ex.Message}");
        }
    }

    public void SaveSettings()
    {
        lock (_lock)
        {
            using var fileStream = new FileStream(SettingsFilePath, FileMode.Create, FileAccess.Write, FileShare.None);
            using var streamWriter = new StreamWriter(fileStream);
            streamWriter.Write($"MinCompletionPercentage={MinCompletionPercentage}\nStarRating={StarRating}\nPerformancePoints={PerformancePoints}");
        }
    }
}