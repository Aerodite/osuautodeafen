using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using osuautodeafen.cs.Settings;

namespace osuautodeafen.cs;

public sealed class SharedViewModel : INotifyPropertyChanged
{
    private readonly bool _canUpdateSettings = true;

    private readonly SettingsHandler _settingsHandler;

    private readonly TosuApi _tosuApi;

    private SolidColorBrush _averageColorBrush = new(Colors.Gray);

    private double _completionPercentage;
    private MainWindow.HotKey? _deafenKeybind;

    private string _deafenKeybindDisplay;

    private bool _isBackgroundEnabled;

    private bool _isBlurEffectEnabled;

    private bool _IsBreakUndeafenToggleEnabled;

    private bool _isFCRequired;

    private bool _isKeybindCaptureFlyoutOpen;

    private bool _IsKiaiEffectEnabled;

    private bool _isParallaxEnabled;

    private bool _isSliderTooltipOpen;
    private int _minCompletionPercentage;

    private Bitmap? _modifiedLogoImage;
    private int _performancePoints;

    private double _sliderTooltipOffsetX;
    private double _starRating;

    private string _statusMessage;

    private bool _undeafenAfterMiss;

    private string _updateStatusMessage;

    private string _updateUrl = "https://github.com/Aerodite/osuautodeafen/releases/latest";

    public SharedViewModel(TosuApi tosuApi)
    {
        _settingsHandler = new SettingsHandler();
        OpenUpdateUrlCommand = new RelayCommand(OpenUpdateUrl);
        Task.Run(InitializeAsync);
        _tosuApi = tosuApi;
        Task.Run(UpdateCompletionPercentageAsync);
    }


    public string CurrentAppVersion => $"Current Version: v{UpdateChecker.currentVersion}";

    //<remarks>
    // this file might be the worst organized file in this entire app but most of everything depends on it.
    // TODO: rewrite basically this entire file
    //</remarks>

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

    public SolidColorBrush AverageColorBrush
    {
        get => _averageColorBrush;
        set
        {
            if (_averageColorBrush != value)
            {
                _averageColorBrush = value;
                OnPropertyChanged();
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
                var wasDisabled = !_isBackgroundEnabled;
                _isBackgroundEnabled = value;
                OnPropertyChanged();
                _settingsHandler?.SaveSetting("UI", "IsBackgroundEnabled", value);
                if (wasDisabled && value) _tosuApi.ForceBeatmapChange();
            }
        }
    }

    public bool IsParallaxEnabled
    {
        get => _isParallaxEnabled;
        set
        {
            if (_isParallaxEnabled != value)
            {
                _isParallaxEnabled = value;
                OnPropertyChanged();
                _settingsHandler?.SaveSetting("UI", "IsParallaxEnabled", value);
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
                _settingsHandler?.SaveSetting("UI", "IsBlurEffectEnabled", value);
            }
        }
    }

    public bool IsBreakUndeafenToggleEnabled
    {
        get => _IsBreakUndeafenToggleEnabled;
        set
        {
            if (_IsBreakUndeafenToggleEnabled != value)
            {
                _IsBreakUndeafenToggleEnabled = value;
                OnPropertyChanged();
                _settingsHandler?.SaveSetting("Behavior", "IsBreakUndeafenToggleEnabled", value);
            }
        }
    }

    public bool IsKiaiEffectEnabled
    {
        get => _IsKiaiEffectEnabled;
        set
        {
            if (_IsKiaiEffectEnabled != value)
            {
                _IsKiaiEffectEnabled = value;
                OnPropertyChanged();
                _settingsHandler?.SaveSetting("UI", "IsKiaiEffectEnabled", value);
                _tosuApi.RaiseKiaiChanged();
            }
        }
    }


    public string StatusMessage
    {
        get => _statusMessage;
        set
        {
            _statusMessage = value;
            OnPropertyChanged();
        }
    }

    public bool UndeafenAfterMiss
    {
        get => _undeafenAfterMiss;
        set
        {
            if (_undeafenAfterMiss != value)
            {
                _undeafenAfterMiss = value;
                OnPropertyChanged();
                _settingsHandler?.SaveSetting("Behavior", "UndeafenAfterMiss", value);
            }
        }
    }

    public bool IsFCRequired
    {
        get => _isFCRequired;
        set
        {
            if (_isFCRequired != value)
            {
                _isFCRequired = value;
                OnPropertyChanged();
                _settingsHandler?.SaveSetting("Behavior", "IsFCRequired", value);
            }
        }
    }

    public MainWindow.HotKey? DeafenKeybind
    {
        get => _deafenKeybind;
        set
        {
            if (_deafenKeybind != value)
            {
                _deafenKeybind = value;
                OnPropertyChanged();
                _settingsHandler?.SaveSetting("Hotkeys", "DeafenKeybind", value?.ToString());
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
                _settingsHandler?.SaveSetting("General", "MinCompletionPercentage", value);
            }
        }
    }

    public double StarRating
    {
        get => _starRating;
        set
        {
            if (_starRating != value)
            {
                _starRating = value;
                OnPropertyChanged();
                _settingsHandler?.SaveSetting("General", "StarRating", value);
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
                _settingsHandler?.SaveSetting("General", "PerformancePoints", value);
            }
        }
    }

    public double CompletionPercentage
    {
        get => _completionPercentage;
        set
        {
            if (Math.Abs(_completionPercentage - value) > 0.01)
            {
                _completionPercentage = value;
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

    public Bitmap? ModifiedLogoImage
    {
        get => _modifiedLogoImage;
        set
        {
            _modifiedLogoImage = value;
            OnPropertyChanged();
        }
    }

    public bool IsSliderTooltipOpen
    {
        get => _isSliderTooltipOpen;
        set
        {
            if (_isSliderTooltipOpen != value)
            {
                _isSliderTooltipOpen = value;
                OnPropertyChanged();
            }
        }
    }

    public double SliderTooltipOffsetX
    {
        get => _sliderTooltipOffsetX;
        set
        {
            if (Math.Abs(_sliderTooltipOffsetX - value) > 0.01)
            {
                _sliderTooltipOffsetX = value;
                OnPropertyChanged();
            }
        }
    }

    public event PropertyChangedEventHandler PropertyChanged;

    private async Task UpdateCompletionPercentageAsync()
    {
        while (true)
        {
            var newCompletionPercentage = _tosuApi.GetCompletionPercentage();
            CompletionPercentage = newCompletionPercentage;
            await Task.Delay(50);
        }
    }

    public async Task InitializeAsync()
    {
    }

    private void UpdateChecker_UpdateCheckCompleted(bool updateFound)
    {
        CheckAndUpdateStatusMessage();
    }

    public void CheckAndUpdateStatusMessage()
    {
        var currentVersionObj = new Version(UpdateChecker.currentVersion);
        Version latestVersionObj;

        string message;
        string url;
        

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