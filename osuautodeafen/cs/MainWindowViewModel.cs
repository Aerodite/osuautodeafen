using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using osuautodeafen;

public class SharedViewModel : INotifyPropertyChanged
{
    private int _minCompletionPercentage;
    private int _starRating;
    private int _performancePoints;
    private bool _isParallaxEnabled;
    private bool _isBackgroundEnabled;
     private MainWindow.HotKey _deafenKeybind;
    public bool _isFCRequired;
    public bool IsParallaxEnabled
    {
        get { return _isParallaxEnabled; }
        set
        {
            if (_isParallaxEnabled != value)
            {
                _isParallaxEnabled = value;
                OnPropertyChanged();

                string settingsFilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "osuautodeafen", "settings.txt");

                var lines = File.ReadAllLines(settingsFilePath);

                var index = Array.FindIndex(lines, line => line.StartsWith("IsParallaxEnabled"));

                if (index != -1)
                {
                    lines[index] = $"IsParallaxEnabled={value}";
                }
                else
                {
                    var newLines = new List<string>(lines) { $"IsParallaxEnabled={value}" };
                    lines = newLines.ToArray();
                }

                File.WriteAllLines(settingsFilePath, lines);
            }
        }
    }

    public bool IsFCRequired
    {
        get { return _isFCRequired; }
        set
        {
            if (_isFCRequired != value)
            {
                _isFCRequired = value;
                OnPropertyChanged();

                string settingsFilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "osuautodeafen", "settings.txt");

                var lines = File.ReadAllLines(settingsFilePath);

                var index = Array.FindIndex(lines, line => line.StartsWith("IsFCRequired"));

                if (index != -1)
                {
                    lines[index] = $"IsFCRequired={value}";
                }
                else
                {
                    var newLines = new List<string>(lines) { $"IsFCRequired={value}" };
                    lines = newLines.ToArray();
                }
                File.WriteAllLines(settingsFilePath, lines);
            }
        }
    }
    public event Action BackgroundEnabledChanged;

    public bool IsBackgroundEnabled
    {
        get { return _isBackgroundEnabled; }
        set
        {
            if (_isBackgroundEnabled != value)
            {
                _isBackgroundEnabled = value;
                OnPropertyChanged();

                string settingsFilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "osuautodeafen", "settings.txt");

                var lines = File.ReadAllLines(settingsFilePath);

                var index = Array.FindIndex(lines, line => line.StartsWith("IsBackgroundEnabled"));

                if (index != -1)
                {
                    lines[index] = $"IsBackgroundEnabled={value}";
                }
                else
                {
                    var newLines = new List<string>(lines) { $"IsBackgroundEnabled={value}" };
                    lines = newLines.ToArray();
                }

                File.WriteAllLines(settingsFilePath, lines);

                BackgroundEnabledChanged?.Invoke();
            }
        }
    }

    public MainWindow.HotKey DeafenKeybind
    {
        get { return _deafenKeybind; }
        set
        {
            if (_deafenKeybind != value)
            {
                _deafenKeybind = value;
                OnPropertyChanged();
            }
        }
    }

    public int MinCompletionPercentage
    {
        get { return _minCompletionPercentage; }
        set
        {
            if (_minCompletionPercentage != value)
            {
                _minCompletionPercentage = value;
                OnPropertyChanged();
            }
        }
    }

    public int StarRating
    {
        get { return _starRating; }
        set
        {
            if (_starRating != value)
            {
                _starRating = value;
                OnPropertyChanged();
            }
        }
    }

    public int PerformancePoints
    {
        get { return _performancePoints; }
        set
        {
            if (_performancePoints != value)
            {
                _performancePoints = value;
                OnPropertyChanged();
            }
        }
    }

    public void UpdateIsFCRequired()
    {
        string settingsFilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "osuautodeafen", "settings.txt");
        if (File.Exists(settingsFilePath))
        {
            foreach (var line in File.ReadLines(settingsFilePath))
            {
                var settings = line.Split('=');
                if (settings.Length == 2 && settings[0].Trim() == "IsFCRequired")
                {
                    IsFCRequired = bool.Parse(settings[1].Trim());
                    Console.WriteLine($"Updated IsFCRequired to {IsFCRequired}");
                    break;
                }
            }
        }
        else
        {
            Console.WriteLine("Settings file does not exist");
        }
    }
    public event PropertyChangedEventHandler PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}