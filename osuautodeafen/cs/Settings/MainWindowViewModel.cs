using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using osuautodeafen.cs.Settings;
using osuautodeafen.cs.Settings.Presets;
using osuautodeafen.cs.Tooltips;
using osuautodeafen.cs.Tosu;
using osuautodeafen.cs.Update;

namespace osuautodeafen.cs;

public sealed class SharedViewModel : INotifyPropertyChanged
{
    private readonly bool _canUpdateSettings = true;

    private readonly SettingsHandler _settingsHandler;

    private readonly TooltipManager _tooltipManager;

    private readonly TosuApi _tosuApi;

    private SolidColorBrush _averageColorBrush = new(Colors.Gray);

    private string _beatmapDifficulty;

    private string _beatmapName;

    private double _blurRadius;

    private double _completionPercentage;
    private MainWindow.HotKey? _deafenKeybind;

    private string _deafenKeybindDisplay;

    private string _fullBeatmapName;

    private bool _isBackgroundEnabled;

    private bool _IsBreakUndeafenToggleEnabled;

    private bool _isFCRequired;

    private bool _isKeybindCaptureFlyoutOpen;

    private bool _IsKiaiEffectEnabled;

    private bool _isParallaxEnabled;

    private bool _isSliderTooltipOpen;
    private bool _isUpdateReady;

    private string _keybindPrompt = "Press any key(s) for the keybind...";
    private double _minCompletionPercentage;

    private Bitmap? _modifiedLogoImage;
    private int _performancePoints;
    private bool _presetExistsForCurrentChecksum;

    private double _sliderTooltipOffsetX;
    private double _starRating;

    private string _statusMessage;

    private bool _undeafenAfterMiss;
    private IBrush _updateBarBackground = Brushes.Gray;

    private int _updateProgress;

    private string _updateStatusMessage;

    private string _updateUrl = "https://github.com/Aerodite/osuautodeafen/releases/latest";

    public SharedViewModel(TosuApi tosuApi, TooltipManager tooltipManager)
    {
        _settingsHandler = new SettingsHandler();
        OpenUpdateUrlCommand = new RelayCommand(OpenUpdateUrl);
        Task.Run(InitializeAsync);
        _tosuApi = tosuApi;
        _tooltipManager = tooltipManager;
        Task.Run(UpdateCompletionPercentageAsync);

        if (Presets != null)
            Presets.CollectionChanged += (s, e) => OnPropertyChanged(nameof(HasAnyPresets));
    }

    public bool CanCreatePreset => !PresetExistsForCurrentChecksum;

    public bool HasAnyPresets => Presets != null && Presets.Any();
    public bool HasAnyPresetsNotCurrent => Presets != null && Presets.Any(p => !p.IsCurrentPreset);
    public ObservableCollection<PresetInfo>? Presets { get; } = [];

    public IEnumerable<PresetInfo> VisiblePresets =>
        Presets?.Where(p => !p.IsCurrentPreset) ?? [];

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
                OnPropertyChanged();
                OnPropertyChanged(nameof(CanCreatePreset));
            }
        }
    }

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

    public string BeatmapDifficultyBrackets
    {
        get
        {
            const int maxLength = 25;
            string value = BeatmapDifficulty ?? string.Empty;
            if (value.Length > maxLength)
                value = value.Substring(0, maxLength) + "...";
            return $"[{value}]";
        }
    }

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
                OnPropertyChanged(nameof(AverageColorBrushDim));
                OnPropertyChanged(nameof(AverageColorBrushDimmer));
                OnPropertyChanged(nameof(AverageColorBrushDark));
                OnPropertyChanged(nameof(AverageColorBrushLight));
            }
        }
    }

    public SolidColorBrush AverageColorBrushDim
    {
        get
        {
            Color color = _averageColorBrush.Color;
            Color lessVibrantColor = DesaturateAndLightenColorHsl(color, 0.75f, 0.85f);
            return new SolidColorBrush(lessVibrantColor);
        }
    }

    public SolidColorBrush AverageColorBrushDimmer
    {
        get
        {
            Color color = _averageColorBrush.Color;
            Color lessVibrantColor = DesaturateAndLightenColorHsl(color, 0.8f, 0.35f);
            return new SolidColorBrush(lessVibrantColor);
        }
    }

    public SolidColorBrush AverageColorBrushDark
    {
        get
        {
            Color color = _averageColorBrush.Color;
            Color lessVibrantColor = DesaturateAndLightenColorHsl(color, 0.75f, 0.25f);
            return new SolidColorBrush(lessVibrantColor);
        }
    }

    public SolidColorBrush AverageColorBrushDarker
    {
        get
        {
            Color color = _averageColorBrush.Color;
            Color lessVibrantColor = DesaturateAndLightenColorHsl(color, 0.85f, 0.12f);
            return new SolidColorBrush(lessVibrantColor);
        }
    }

    public SolidColorBrush AverageColorBrushLight
    {
        get
        {
            Color color = _averageColorBrush.Color;
            Color lessVibrantColor = DesaturateAndLightenColorHsl(color, 0.15f, 0.60f);
            return new SolidColorBrush(lessVibrantColor);
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
                _tooltipManager.UpdateTooltipText("" + (value ? "Disable" : "Enable") + " Beatmap Background", true);
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
                _tooltipManager.UpdateTooltipText("" + (value ? "Disable" : "Enable") + " Parallax Effect", true);
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
                _tooltipManager.UpdateTooltipText("" + (value ? "Disable" : "Enable") + " Undeafening during breaks",
                    true);
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
                _tooltipManager.UpdateTooltipText("" + (value ? "Disable" : "Enable") + " Kiai Effect", true);
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
                _tooltipManager.UpdateTooltipText("" + (value ? "Disable" : "Enable") + " Undeafening after a miss",
                    true);
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
                _tooltipManager.UpdateTooltipText("" + (value ? "Disable" : "Enable") + " FC Requirement", true);
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

    public string KeybindPrompt
    {
        get => _keybindPrompt;
        set
        {
            _keybindPrompt = value;
            OnPropertyChanged();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public void RefreshPresets()
    {
        Presets?.Clear();
        foreach (PresetInfo preset in PresetManager.LoadAllPresets())
        {
            preset.PropertyChanged += Preset_PropertyChanged;
            Presets?.Add(preset);
        }

        foreach (PresetInfo preset in Presets ?? Enumerable.Empty<PresetInfo>())
        {
            preset.IsCurrentPreset = preset.Checksum == _tosuApi.GetBeatmapChecksum();
            Console.WriteLine($"Preset {preset.BeatmapName} IsCurrentPreset: {preset.IsCurrentPreset}");
        }

        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasAnyPresetsNotCurrent)));
    }

    private void Preset_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PresetInfo.IsCurrentPreset))
            OnPropertyChanged(nameof(HasAnyPresetsNotCurrent));
    }

    private static Color DesaturateAndLightenColorHsl(Color color, float saturationFactor, float lightnessFactor)
    {
        // convert rgb to hsl
        float red = color.R / 255f;
        float green = color.G / 255f;
        float blue = color.B / 255f;

        float maxComponent = Math.Max(red, Math.Max(green, blue));
        float minComponent = Math.Min(red, Math.Min(green, blue));
        float hue, saturation;

        float lightness = (maxComponent + minComponent) / 2f;

        if (maxComponent == minComponent)
        {
            hue = 0f;
            saturation = 0f;
        }
        else
        {
            float delta = maxComponent - minComponent;

            saturation = lightness > 0.5f
                ? delta / (2f - maxComponent - minComponent)
                : delta / (maxComponent + minComponent);

            if (maxComponent == red)
                hue = (green - blue) / delta + (green < blue ? 6f : 0f);
            else if (maxComponent == green)
                hue = (blue - red) / delta + 2f;
            else
                hue = (red - green) / delta + 4f;

            hue /= 6f;
        }

        // desaturate and lighten
        saturation *= saturationFactor;
        lightness = Math.Min(lightness * lightnessFactor, 1f);

        // convert the hsl back to rgb
        float q = lightness < 0.5f
            ? lightness * (1f + saturation)
            : lightness + saturation - lightness * saturation;
        float p = 2f * lightness - q;

        float[] tempHues = [hue + 1f / 3f, hue, hue - 1f / 3f];
        float[] rgbComponents = new float[3];

        for (int i = 0; i < 3; i++)
        {
            float tempHue = tempHues[i];
            if (tempHue < 0f) tempHue += 1f;
            if (tempHue > 1f) tempHue -= 1f;

            switch (tempHue)
            {
                case < 1f / 6f:
                    rgbComponents[i] = p + (q - p) * 6f * tempHue;
                    break;
                case < 1f / 2f:
                    rgbComponents[i] = q;
                    break;
                case < 2f / 3f:
                    rgbComponents[i] = p + (q - p) * (2f / 3f - tempHue) * 6f;
                    break;
                default:
                    rgbComponents[i] = p;
                    break;
            }
        }

        return Color.FromArgb(
            color.A,
            (byte)(rgbComponents[0] * 255),
            (byte)(rgbComponents[1] * 255),
            (byte)(rgbComponents[2] * 255)
        );
    }

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

    private Task InitializeAsync()
    {
        return Task.CompletedTask;
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


    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}