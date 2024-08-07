using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Timers;
using System.Windows.Input;
using Avalonia.Threading;

namespace osuautodeafen.cs;

public sealed class SharedViewModel : INotifyPropertyChanged
{
    private readonly UpdateChecker _updateChecker = UpdateChecker.GetInstance();
    private bool _canUpdateSettings = true;
    private MainWindow.HotKey _deafenKeybind;

    private string _deafenKeybindDisplay;
    public Timer _debounceTimer;
    private bool _isBackgroundEnabled;

    private bool _isBlankScreenEnabled;

    private bool _isBlurEffectEnabled;
    public bool _isFCRequired;

    private bool _isKeybindCaptureFlyoutOpen;
    private bool _isParallaxEnabled;
    private int _minCompletionPercentage;
    private int _performancePoints;
    private int _starRating;

    private bool _UndeafenAfterMiss;

    private string _updateStatusMessage;

    private string _updateUrl = "https://github.com/Aerodite/osuautodeafen/releases/latest";

    public SharedViewModel()
    {
        _debounceTimer = new Timer(2000);
        _debounceTimer.Elapsed += (sender, e) => _canUpdateSettings = true;
        _debounceTimer.AutoReset = false;
        OpenUpdateUrlCommand = new RelayCommand(OpenUpdateUrl);
        Task.Run(InitializeAsync);
    }


    public string CurrentAppVersion => $"Current Version: v{UpdateChecker.currentVersion}";

    //<remarks>
    // this file might be the worst organized file in this entire app but most of everything depends on it.
    // TODO: rewrite basically this entire file
    //</remarks>

    public bool IsParallaxEnabled
    {
        get => _isParallaxEnabled;
        set
        {
            if (_isParallaxEnabled != value)
            {
                _isParallaxEnabled = value;
                OnPropertyChanged();

                var settingsFilePath =
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "osuautodeafen",
                        "settings.txt");

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

    public bool IsKeybindCaptureFlyoutOpen
    {
        get => _isKeybindCaptureFlyoutOpen;
        set
        {
            if (_isKeybindCaptureFlyoutOpen != value)
            {
                _isKeybindCaptureFlyoutOpen = value;
                OnPropertyChanged();
            }
        }
    }

    public string DeafenKeybindDisplay
    {
        get => _deafenKeybindDisplay;
        set
        {
            if (_deafenKeybindDisplay != value)
            {
                _deafenKeybindDisplay = value;
                OnPropertyChanged();
            }
        }
    }

    public bool IsFCRequired
    {
        get => _isFCRequired;
        set
        {
            if (!_canUpdateSettings) return;
            if (_isFCRequired != value)
            {
                _isFCRequired = value;
                OnPropertyChanged();

                var settingsFilePath =
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "osuautodeafen",
                        "settings.txt");

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

                // Use FileStream with FileShare.ReadWrite
                using (var fileStream = new FileStream(settingsFilePath, FileMode.OpenOrCreate, FileAccess.ReadWrite,
                           FileShare.ReadWrite))
                using (var writer = new StreamWriter(fileStream))
                {
                    foreach (var line in lines) writer.WriteLine(line);
                }
            }
        }
    }

    public bool UndeafenAfterMiss
    {
        get => _UndeafenAfterMiss;
        set
        {
            if (_UndeafenAfterMiss != value)
            {
                _UndeafenAfterMiss = value;
                OnPropertyChanged();

                var settingsFilePath =
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "osuautodeafen",
                        "settings.txt");

                var lines = File.ReadAllLines(settingsFilePath);

                var index = Array.FindIndex(lines, line => line.StartsWith("UndeafenAfterMiss"));

                if (index != -1)
                {
                    lines[index] = $"UndeafenAfterMiss={value}";
                }
                else
                {
                    var newLines = new List<string>(lines) { $"UndeafenAfterMiss={value}" };
                    lines = newLines.ToArray();
                }

                File.WriteAllLines(settingsFilePath, lines);
            }
        }
    }

    public bool IsBlankScreenEnabled
    {
        get => _isBlankScreenEnabled;
        set
        {
            if (_isBlankScreenEnabled != value)
            {
                _isBlankScreenEnabled = value;
                OnPropertyChanged();

                var settingsFilePath =
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "osuautodeafen",
                        "settings.txt");

                var lines = File.ReadAllLines(settingsFilePath);

                var index = Array.FindIndex(lines, line => line.StartsWith("IsBlankScreenEnabled"));

                if (index != -1)
                {
                    lines[index] = $"IsBlankScreenEnabled={value}";
                }
                else
                {
                    var newLines = new List<string>(lines) { $"IsBlankScreenEnabled={value}" };
                    lines = newLines.ToArray();
                }

                File.WriteAllLines(settingsFilePath, lines);
            }
        }
    }

    public bool IsBlurEffectEnabled
    {
        get => _isBlurEffectEnabled;
        set
        {
            if (_isBlurEffectEnabled != value)
            {
                _isBlurEffectEnabled = value;
                OnPropertyChanged();

                var settingsFilePath =
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "osuautodeafen",
                        "settings.txt");

                var lines = File.ReadAllLines(settingsFilePath);

                var index = Array.FindIndex(lines, line => line.StartsWith("IsBlurEffectEnabled"));

                if (index != -1)
                {
                    lines[index] = $"IsBlurEffectEnabled={value}";
                }
                else
                {
                    var newLines = new List<string>(lines) { $"IsBlurEffectEnabled={value}" };
                    lines = newLines.ToArray();
                }

                File.WriteAllLines(settingsFilePath, lines);
            }
        }
    }

    public bool IsBackgroundEnabled
    {
        get => _isBackgroundEnabled;
        set
        {
            if (_isBackgroundEnabled != value)
            {
                _isBackgroundEnabled = value;
                OnPropertyChanged();

                var settingsFilePath =
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "osuautodeafen",
                        "settings.txt");

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
        get => _deafenKeybind;
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
        get => _minCompletionPercentage;
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
        get => _starRating;
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
        get => _performancePoints;
        set
        {
            if (_performancePoints != value)
            {
                _performancePoints = value;
                OnPropertyChanged();
            }
        }
    }

    public string UpdateUrl
    {
        get => _updateUrl;
        set
        {
            if (_updateUrl != value)
            {
                _updateUrl = value;
                OnPropertyChanged();
            }
        }
    }

    public ICommand OpenUpdateUrlCommand { get; private set; }

    public string UpdateStatusMessage
    {
        get => _updateStatusMessage;
        set
        {
            if (_updateStatusMessage != value)
            {
                _updateStatusMessage = value;
                OnPropertyChanged();
            }
        }
    }

    public event PropertyChangedEventHandler PropertyChanged;

    public void MainWindowViewModel()
    {
        UpdateChecker.OnUpdateAvailable += UpdateChecker_OnUpdateAvailable;
        UpdateChecker.UpdateCheckCompleted += UpdateChecker_UpdateCheckCompleted;
    }

    public async Task InitializeAsync()
    {
        await _updateChecker.FetchLatestVersionAsync();
        UpdateChecker.OnUpdateAvailable += UpdateChecker_OnUpdateAvailable;
        UpdateChecker.UpdateCheckCompleted += UpdateChecker_UpdateCheckCompleted;
    }

    private void UpdateChecker_UpdateCheckCompleted(bool updateFound)
    {
        CheckAndUpdateStatusMessage();
    }

    private void UpdateChecker_OnUpdateAvailable(string? latestVersion, string latestReleaseUrl)
    {
        _updateChecker.latestVersion = latestVersion;
        CheckAndUpdateStatusMessage();
    }

    public void UpdateUndeafenAfterMiss()
    {
        var settingsFilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "osuautodeafen", "settings.txt");
        if (File.Exists(settingsFilePath))
            foreach (var line in File.ReadLines(settingsFilePath))
            {
                var settings = line.Split('=');
                if (settings.Length == 2 && settings[0].Trim() == "UndeafenAfterMiss")
                {
                    UndeafenAfterMiss = bool.Parse(settings[1].Trim());
                    Console.WriteLine($"Updated UndeafenAfterMiss to {UndeafenAfterMiss}");
                    break;
                }
            }
        else
            Console.WriteLine("Settings file does not exist");
    }

    public event Action BackgroundEnabledChanged;

    public void UpdateIsFCRequired()
    {
        if (!_canUpdateSettings) return;
        var settingsFilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "osuautodeafen", "settings.txt");
        if (File.Exists(settingsFilePath))
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
        else
            Console.WriteLine("Settings file does not exist");
    }

    public void CheckAndUpdateStatusMessage()
    {
        var currentVersionObj = new Version(UpdateChecker.currentVersion);
        Version latestVersionObj;

        if (string.IsNullOrEmpty(_updateChecker.latestVersion) ||
            !Version.TryParse(_updateChecker.latestVersion, out latestVersionObj))
        {
            Console.WriteLine("Invalid or missing latest version. Unable to compare versions.");
            return;
        }

        Console.WriteLine($"Current Version: {currentVersionObj}, Latest Version: {latestVersionObj}");

        string message;
        string url;

        if (currentVersionObj < latestVersionObj)
        {
            message = "A new update is available!" + $"\n(v{latestVersionObj})";
        }
        else
        {
            message = "No updates available";
            url = null;
        }

        Dispatcher.UIThread.InvokeAsync(() => { UpdateStatusMessage = message; });
    }

    private void OpenUpdateUrl()
    {
        if (!string.IsNullOrEmpty(UpdateUrl))
            Process.Start(new ProcessStartInfo
            {
                FileName = UpdateUrl,
                UseShellExecute = true
            });
    }

    private void OnPropertyChanged([CallerMemberName] string propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}