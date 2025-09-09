using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using osuautodeafen.cs.Settings;
using osuautodeafen.cs.Settings.Presets;

namespace osuautodeafen.cs;

public sealed class SharedViewModel : INotifyPropertyChanged
{
    private readonly bool _canUpdateSettings = true;

    private readonly SettingsHandler _settingsHandler;

    private readonly TosuApi _tosuApi;
    
    public bool CanCreatePreset => !PresetExistsForCurrentChecksum;
    
    private SolidColorBrush _averageColorBrush = new(Colors.Gray);

    private double _blurRadius;

    private string _beatmapName;
    
    private string _fullBeatmapName;

    private string _beatmapDifficulty;

    private double _completionPercentage;
    private MainWindow.HotKey? _deafenKeybind;

    private string _deafenKeybindDisplay;

    private bool _isBackgroundEnabled;

    private bool _IsBreakUndeafenToggleEnabled;

    private bool _isFCRequired;

    private bool _isKeybindCaptureFlyoutOpen;

    private bool _IsKiaiEffectEnabled;

    private bool _isParallaxEnabled;

    private bool _isSliderTooltipOpen;
    private bool _isUpdateReady;
    private double _minCompletionPercentage;

    private Bitmap? _modifiedLogoImage;
    private int _performancePoints;

    private double _sliderTooltipOffsetX;
    private double _starRating;

    private string _statusMessage;

    private bool _undeafenAfterMiss;
    private IBrush _updateBarBackground = Brushes.Gray;

    private int _updateProgress;

    private string _updateStatusMessage;
    
    public ObservableCollection<PresetInfo> Presets { get; } = new();


    private string _updateUrl = "https://github.com/Aerodite/osuautodeafen/releases/latest";

    public SharedViewModel(TosuApi tosuApi)
    {
        _settingsHandler = new SettingsHandler();
        OpenUpdateUrlCommand = new RelayCommand(OpenUpdateUrl);
        Task.Run(InitializeAsync);
        _tosuApi = tosuApi;
        Task.Run(UpdateCompletionPercentageAsync);
    }
    
    public void RefreshPresets()
    {
        Presets.Clear();
        foreach (var preset in PresetManager.LoadAllPresets())
            Presets.Add(preset);
    }

    public int BlurPercent => (int)Math.Round(BlurRadius / 20.0 * 100);

    public int UpdateProgress
    {
        get => _updateProgress;
        set
        {
            if (_updateProgress != value)
            {
                _updateProgress = value;
                OnPropertyChanged();
                IsUpdateReady = _updateProgress >= 100;
                UpdateBarBackground = IsUpdateReady ? Brushes.Green : Brushes.Gray;
            }
        }
    }
    public string BeatmapName
    {
        get => _beatmapName;
        set
        {
            if (_beatmapName != value)
            {
                _beatmapName = value;
                OnPropertyChanged();
            }
        }
    }
    
    public string FullBeatmapName
    {
        get => _fullBeatmapName;
        set
        {
            if (_fullBeatmapName != value)
            {
                _fullBeatmapName = value;
                OnPropertyChanged();
            }
        }
    }
    
    public bool PresetExistsForCurrentChecksum
    {
        get => _presetExistsForCurrentChecksum;
        set
        {
            if (_presetExistsForCurrentChecksum != value)
            {
                _presetExistsForCurrentChecksum = value;
                OnPropertyChanged(nameof(PresetExistsForCurrentChecksum));
                OnPropertyChanged(nameof(CanCreatePreset));
            }
        }
    }
    private bool _presetExistsForCurrentChecksum;
    
    public string BeatmapDifficulty
    {
        get => _beatmapDifficulty;
        set
        {
            if (_beatmapDifficulty != value)
            {
                _beatmapDifficulty = value;
                OnPropertyChanged();
            }
        }
    }
    
    public string BeatmapDifficultyBrackets => $"[{BeatmapDifficulty}]";

    public bool IsUpdateReady
    {
        get => _isUpdateReady;
        set
        {
            if (_isUpdateReady != value)
            {
                _isUpdateReady = value;
                OnPropertyChanged();
            }
        }
    }

    public IBrush UpdateBarBackground
    {
        get => _updateBarBackground;
        set
        {
            if (_updateBarBackground != value)
            {
                _updateBarBackground = value;
                OnPropertyChanged();
            }
        }
    }


    public string CurrentAppVersion => $"Current Version: v{UpdateChecker.CurrentVersion}";

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
                bool wasDisabled = !_isBackgroundEnabled;
                _isBackgroundEnabled = value;
                OnPropertyChanged();
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
                _tosuApi.RaiseKiaiChanged();
            }
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

    public MainWindow.HotKey? DeafenKeybind
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

    public double MinCompletionPercentage
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

    public double StarRating
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

    public double BlurRadius
    {
        get => _blurRadius;
        set
        {
            if (_blurRadius != value)
            {
                _blurRadius = value;
                OnPropertyChanged();
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

    public object MinPPValue => _tosuApi.GetMaxPP();

    public object MinSRValue => _tosuApi.GetFullSR();

    public event PropertyChangedEventHandler PropertyChanged;

    public void UpdateMinPPValue()
    {
        OnPropertyChanged(nameof(MinPPValue));
    }

    public void UpdateMinSRValue()
    {
        OnPropertyChanged(nameof(MinSRValue));
    }

    private async Task UpdateCompletionPercentageAsync()
    {
        while (true)
        {
            double newCompletionPercentage = _tosuApi.GetCompletionPercentage();
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
        Version currentVersionObj = new(UpdateChecker.CurrentVersion);
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


    public void OnPropertyChanged([CallerMemberName] string propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}