using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Rendering.Composition;
using Avalonia.Styling;
using Avalonia.Threading;
using LiveChartsCore;
using LiveChartsCore.Defaults;
using LiveChartsCore.Drawing;
using LiveChartsCore.Measure;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using osuautodeafen.cs;
using osuautodeafen.cs.Screen;
using SkiaSharp;
using Svg.Skia;
using Animation = Avalonia.Animation.Animation;
using KeyFrame = Avalonia.Animation.KeyFrame;
using Path = System.IO.Path;
using Vector = Avalonia.Vector;

namespace osuautodeafen;

public partial class MainWindow : Window
{
    public static bool isCompPctLostFocus;
    private readonly AnimationManager _animationManager = new();

    //testing out a capacity of 0 for now, this means a ram-usage reduction of 50% 🤯
    private readonly Queue<Bitmap> _bitmapQueue = new(0);
    private readonly Deafen _deafen;
    private readonly DispatcherTimer _disposeTimer;
    private readonly DispatcherTimer _mainTimer;
    private readonly DispatcherTimer _parallaxCheckTimer;
    private readonly TosuApi _tosuApi;

    private readonly UpdateChecker _updateChecker = UpdateChecker.GetInstance();


    private readonly object _updateLogoLock = new();
    private Grid? _blackBackground;
    private Image? _blurredBackground;
    private BreakPeriod _breakPeriod;
    private SKSvg? _cachedLogoSvg;
    private Bitmap _colorChangingImage;

    private string? _currentBackgroundDirectory;

    private double _currentBackgroundOpacity = 1.0;
    private Bitmap? _currentBitmap;
    private KeyModifiers _currentKeyModifiers = KeyModifiers.None;
    private HotKey? _deafenKeybind;
    private LineSeries<ObservablePoint> _deafenMarker;
    private readonly GetLowResBackground? _getLowResBackground;
    private Thread _graphDataThread;
    private bool _hasDisplayed = false;
    private bool _isConstructorFinished;
    private bool _isTransitioning = false;

    private Key _lastKeyPressed = Key.None;
    private DateTime _lastKeyPressTime = DateTime.MinValue;
    private DateTime _lastUpdateCheck = DateTime.MinValue;

    //_lowres
    private Bitmap? _lowResBitmap;
    private double _mouseX;
    private double _mouseY;
    private Image? _normalBackground;
    private SKColor _oldAverageColor = SKColors.Transparent;
    private ScreenBlanker _screenBlanker;
    private ScreenBlankerForm? _screenBlankerForm;
    private DispatcherTimer? _visibilityCheckTimer;
    private double deafenProgressPercentage;
    private double deafenTimestamp;
    private SettingsPanel settingsPanel1;


    //<summary>
    // constructor for the ui and subsequent panels
    //</summary>
    public MainWindow()
    {
        InitializeComponent();

        var settingsPanel = new SettingsPanel();

        var settingsPanel1 = new SettingsPanel();

        LoadSettings();

        Icon = new WindowIcon(LoadEmbeddedResource("osuautodeafen.Resources.oad.ico"));

        _tosuApi = new TosuApi();

        _getLowResBackground = new GetLowResBackground(_tosuApi);

        _breakPeriod = new BreakPeriod(_tosuApi);

        _deafen = new Deafen(_tosuApi, settingsPanel1, new ScreenBlankerForm(this));

        _disposeTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(500)
        };
        _disposeTimer.Tick += DisposeTimer_Tick;

        _parallaxCheckTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };

        _parallaxCheckTimer.Tick += CheckParallaxSetting;
        _parallaxCheckTimer.Start();

        _mainTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(150)
        };
        _mainTimer.Tick += MainTimer_Tick;
        _mainTimer.Start();

        InitializeVisibilityCheckTimer();

        var oldContent = Content;

        Content = null;

        Content = new Grid
        {
            Children =
            {
                new ContentControl { Content = oldContent }
            }
        };

        InitializeViewModel();

        InitializeLogo();

        PointerMoved += OnMouseMove;

        DataContext = ViewModel;

        InitializeGraphDataThread();

        _tosuApi.GraphDataUpdated += OnGraphDataUpdated;

        Series = [];
        XAxes = new Axis[] { new() { LabelsPaint = new SolidColorPaint(SKColors.White) } };
        YAxes = new Axis[] { new() { LabelsPaint = new SolidColorPaint(SKColors.White) } };
        PlotView.Series = Series;
        PlotView.XAxes = XAxes;
        PlotView.YAxes = YAxes;

        var series1 = new StackedAreaSeries<ObservablePoint>
        {
            Values = ChartData.Series1Values,
            Fill = new SolidColorPaint { Color = new SKColor(0xFF, 0x00, 0x00) },
            Stroke = new SolidColorPaint { Color = new SKColor(0xFF, 0x00, 0x00) }
        };

        var series2 = new StackedAreaSeries<ObservablePoint>
        {
            Values = ChartData.Series2Values,
            Fill = new SolidColorPaint { Color = new SKColor(0x00, 0xFF, 0x00) },
            Stroke = new SolidColorPaint { Color = new SKColor(0x00, 0xFF, 0x00) }
        };


        PlotView.Series = new ISeries[] { series1, series2 };


        //dogshit slider logic that i would rather not reimplement

        // var slider = this.FindControl<Slider>("Slider");
        //
        // var sliderManager = new SliderManager(settingsPanel1, slider);

        settingsPanel.Transitions = new Transitions
        {
            new DoubleTransition
            {
                Property = OpacityProperty,
                Duration = TimeSpan.FromSeconds(0.5),
                Easing = new QuarticEaseInOut()
            }
        };

        DataContext = settingsPanel1;
        ExtendClientAreaToDecorationsHint = true;
        ExtendClientAreaTitleBarHeightHint = -1;
        ExtendClientAreaChromeHints = ExtendClientAreaChromeHints.PreferSystemChrome;
        ExtendClientAreaToDecorationsHint = true;
        ExtendClientAreaTitleBarHeightHint = 32;
        ExtendClientAreaChromeHints = ExtendClientAreaChromeHints.PreferSystemChrome;
        Background = Brushes.Black;
        PointerPressed += (sender, e) =>
        {
            var point = e.GetPosition(this);
            const int titleBarHeight = 34; // height of the title bar + an extra 2px of wiggle room
            if (point.Y <= titleBarHeight) BeginMoveDrag(e);
        };

        InitializeKeybindButtonText();
        UpdateDeafenKeybindDisplay();

        CompletionPercentageTextBox.Text = ViewModel.MinCompletionPercentage.ToString();
        StarRatingTextBox.Text = ViewModel.StarRating.ToString();
        PPTextBox.Text = ViewModel.PerformancePoints.ToString();

        BorderBrush = Brushes.Black;
        Width = 600;
        Height = 600;
        CanResize = false;
        Closing += MainWindow_Closing;
        _isConstructorFinished = true;
    }

    private bool BlurEffectUpdate { get; set; }
    private SharedViewModel ViewModel { get; } = new();
    private bool IsBlackBackgroundDisplayed { get; set; }

    public GraphData? Graph { get; set; }
    public ISeries[] Series { get; set; }
    public Axis[] XAxes { get; set; }
    public Axis[] YAxes { get; set; }

    public TimeSpan Interval { get; set; }
    public object? UpdateUrl { get; }


    public HotKey? DeafenKeybind
    {
        get => _deafenKeybind;
        set
        {
            if (_deafenKeybind != value)
            {
                _deafenKeybind = value;
                OnPropertyChanged(nameof(DeafenKeybind));
                var button = this.FindControl<Button>("DeafenKeybindButton");
                if (button != null) button.Content = value.ToString();
            }
        }
    }

    private void InitializeGraphDataThread()
    {
        _graphDataThread = new Thread(GraphDataThreadStart);
        _graphDataThread.IsBackground = true;
        _graphDataThread.Start();
    }

    private async void GraphDataThreadStart()
    {
        while (true)
            if (Graph != null)
                await Dispatcher.UIThread.InvokeAsync(() => UpdateChart(Graph));
    }

    private void OnGraphDataUpdated(GraphData graphData)
    {
        graphData.Series[0].Name = "aim";
        graphData.Series[1].Name = "speed";

        ChartData.Series1Values = graphData.Series[0].Data.Select((value, index) => new ObservablePoint(index, value))
            .ToList();
        ChartData.Series2Values = graphData.Series[1].Data.Select((value, index) => new ObservablePoint(index, value))
            .ToList();

        Dispatcher.UIThread.InvokeAsync(() => UpdateChart(graphData));

        _deafen.MinCompletionPercentage = ViewModel.MinCompletionPercentage;
    }

    // i hope i never have to touch arrays or graphs again due to this function alone.
    private List<ObservablePoint> SmoothData(List<ObservablePoint> data, int windowSize, double smoothingFactor)
    {
        var smoothedData = new List<ObservablePoint>();
        for (var i = 0; i < data.Count; i++)
        {
            double sum = 0;
            var count = 0;
            var adjustedWindowSize = (int)(windowSize * smoothingFactor);
            for (var j = Math.Max(0, i - adjustedWindowSize);
                 j <= Math.Min(data.Count - 1, i + adjustedWindowSize);
                 j++)
            {
                sum += data[j].Y ?? 0.0;
                count++;
            }

            smoothedData.Add(new ObservablePoint(data[i].X, sum / count));
        }

        return smoothedData;
    }

    public void UpdateChart(GraphData graphData)
    {
        var seriesList = new List<ISeries>();
        PlotView.DrawMargin = new Margin(0, 0, 0, 0);

        foreach (var series in graphData.Series)
        {
            var updatedValues = series.Data
                .Select((value, index) => new { value, index })
                .Where(x => x.value != -100)
                .Select(x => new ObservablePoint(x.index, x.value))
                .ToList();

            // Apply smoothing
            var smoothedValues = SmoothData(updatedValues, 10, 0.2);

            var color = series.Name == "aim"
                ? new SKColor(0x00, 0xFF, 0x00, 192)
                : new SKColor(0x00, 0x00, 0xFF, 140); // Adjust transparency
            var name = series.Name == "aim" ? "Aim" : "Speed";

            var lineSeries = new LineSeries<ObservablePoint>
            {
                Values = smoothedValues,
                Fill = new SolidColorPaint { Color = color },
                Stroke = new SolidColorPaint { Color = color },
                Name = name,
                GeometryFill = null,
                GeometryStroke = null,
                LineSmoothness = 1,
                EasingFunction = EasingFunctions.ExponentialOut,
                TooltipLabelFormatter = value => $"{name}: {value}"
            };
            seriesList.Add(lineSeries);
        }

        // round up the last value in the updated x-axis array
        var maxLimit = Math.Ceiling((double)graphData.XAxis.Last() / 1024);

        deafenProgressPercentage = _deafen.MinCompletionPercentage / 128;
        var graphDuration = maxLimit;
        deafenTimestamp = graphDuration * deafenProgressPercentage;

        var maxYValue = graphData.Series.SelectMany(series => series.Data).Max();

        var deafenMarker = new LineSeries<ObservablePoint>
        {
            Values = new List<ObservablePoint>
            {
                new(deafenTimestamp, 0), // bottom-left corner
                new(deafenTimestamp, maxYValue), // top-left corner
                new(maxLimit, maxYValue), // top-right corner
                new(maxLimit, 0), // bottom-right corner
                new(deafenTimestamp, 0) // close the rectangle by returning to the bottom-left corner
            },
            Name = "Deafen Point",
            Stroke = new SolidColorPaint
                { Color = new SKColor(0xFF, 0x00, 0x00, 110), StrokeThickness = 3 }, // 70% opacity red
            GeometryFill = null,
            GeometryStroke = null,
            LineSmoothness = 0
        };

        seriesList.Add(deafenMarker);

        Series = seriesList.ToArray();
        XAxes = new Axis[]
        {
            new()
            {
                LabelsPaint = new SolidColorPaint(SKColors.Transparent),
                MinLimit = 0, // ensure the x-axis starts from 0
                MaxLimit = maxLimit,
                Padding = new Padding(2),
                TextSize = 12
            }
        };
        YAxes = new Axis[]
        {
            new()
            {
                LabelsPaint = new SolidColorPaint(SKColors.Transparent),
                SeparatorsPaint = new SolidColorPaint(SKColors.Transparent)
            }
        };

        PlotView.Series = Series;
        PlotView.XAxes = XAxes;
        PlotView.YAxes = YAxes;
    }


    //initialize the visibility check timer
    private void InitializeVisibilityCheckTimer()
    {
        _visibilityCheckTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(100)
        };
        _visibilityCheckTimer.Tick += VisibilityCheckTimer_Tick;
        _visibilityCheckTimer.Start();
    }

    private void VisibilityCheckTimer_Tick(object? sender, EventArgs e)
    {
        _blurredBackground.IsVisible = ViewModel.IsBlurEffectEnabled;
        _normalBackground.IsVisible = !ViewModel.IsBlurEffectEnabled;
    }

    // show the update notification bar if an update is available
    private async void InitializeViewModel()
    {
        await _updateChecker.FetchLatestVersionAsync();

        var viewModel = new SharedViewModel();
        {
            //UpdateStatusMessage = "v" + UpdateChecker.currentVersion,
        }

        DataContext = ViewModel;
    }

    private async void CheckForUpdatesIfNeeded()
    {
        if ((DateTime.Now - _lastUpdateCheck).TotalMinutes > 20)
        {
            _lastUpdateCheck = DateTime.Now;
            await _updateChecker.FetchLatestVersionAsync();

            if (string.IsNullOrEmpty(_updateChecker.latestVersion))
            {
                Console.WriteLine("Latest version string is null or empty.");
                return;
            }

            var currentVersion = new Version(UpdateChecker.currentVersion);
            var latestVersion = new Version(_updateChecker.latestVersion);

            if (latestVersion > currentVersion) ShowUpdateNotification();
        }
    }

    // grab the keybind from the settings file and update the display
    private void UpdateDeafenKeybindDisplay()
    {
        var currentKeybind = RetrieveKeybindFromSettings();
        DeafenKeybindButton.Content = currentKeybind;
    }

    // save the keybind to the settings file
    private void DeafenKeybindButton_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.IsKeybindCaptureFlyoutOpen = !ViewModel.IsKeybindCaptureFlyoutOpen;
        var flyout = Resources["KeybindCaptureFlyout"] as Flyout;
        if (flyout != null)
        {
            if (ViewModel.IsKeybindCaptureFlyoutOpen)
                flyout.ShowAt(DeafenKeybindButton);
            else
                flyout.Hide();
        }
    }

    // capture the keybind and save it to the settings file
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        if (!ViewModel.IsKeybindCaptureFlyoutOpen) return;

        if (e.Key == Key.NumLock) return;

        Flyout? flyout;
        if (e.Key == Key.Escape)
        {
            ViewModel.IsKeybindCaptureFlyoutOpen = false;
            flyout = Resources["KeybindCaptureFlyout"] as Flyout;
            if (flyout != null) flyout.Hide();
            return;
        }

        var currentTime = DateTime.Now;

        if (e.Key == _lastKeyPressed &&
            (currentTime - _lastKeyPressTime).TotalMilliseconds <
            2500) return; // considered a repeat key press, ignore it

        _lastKeyPressed = e.Key;
        _lastKeyPressTime = currentTime;

        if (IsModifierKey(e.Key)) return;

        // capture the key and its modifiers
        var modifiers = KeyModifiers.None;
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control)) modifiers |= KeyModifiers.Control;
        if (e.KeyModifiers.HasFlag(KeyModifiers.Alt)) modifiers |= KeyModifiers.Alt;
        if (e.KeyModifiers.HasFlag(KeyModifiers.Shift)) modifiers |= KeyModifiers.Shift;

        // create and set the new hotkey
        ViewModel.DeafenKeybind = new HotKey { Key = e.Key, ModifierKeys = modifiers };

        // save the new hotkey to settings
        SaveSettingsToFile(ViewModel.DeafenKeybind.ToString(), "Hotkey");

        e.Handled = true;
    }

    private void InitializeKeybindButtonText()
    {
        var currentKeybind = RetrieveKeybindFromSettings();
        DeafenKeybindButton.Content = currentKeybind;
    }

    private string RetrieveKeybindFromSettings()
    {
        var settingsFilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "osuautodeafen", "settings.txt");
        if (File.Exists(settingsFilePath))
        {
            var lines = File.ReadAllLines(settingsFilePath);
            var keybindLine = lines.FirstOrDefault(line => line.StartsWith("Hotkey="));
            if (keybindLine != null) return keybindLine.Split('=')[1];
        }

        return "Set Keybind";
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    public void ShowUpdateNotification()
    {
        var notificationBar = this.FindControl<Button>("UpdateNotificationBar");
        if (notificationBar != null)
            notificationBar.IsVisible = true;
        else
            Console.WriteLine("Notification bar control not found.");
    }

    private void UpdateNotificationBar_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(ViewModel.UpdateUrl))
            Process.Start(new ProcessStartInfo
            {
                FileName = ViewModel.UpdateUrl,
                UseShellExecute = true
            });
    }

    private void MainTimer_Tick(object? sender, EventArgs? e)
    {
        Dispatcher.UIThread.InvokeAsync(() => UpdateBackground(sender, e));
        Dispatcher.UIThread.InvokeAsync(() => CheckIsFCRequiredSetting(sender, e));
        Dispatcher.UIThread.InvokeAsync(() => CheckBackgroundSetting(sender, e));
        Dispatcher.UIThread.InvokeAsync(() => CheckParallaxSetting(sender, e));
        Dispatcher.UIThread.InvokeAsync(() => UpdateErrorMessage(sender, e));
        Dispatcher.UIThread.InvokeAsync(() => CheckBlurEffectSetting(sender, e));
        Dispatcher.UIThread.InvokeAsync(UpdateDeafenKeybindDisplay);
        Dispatcher.UIThread.InvokeAsync(() => CheckMissUndeafenSetting(sender, e));
        Dispatcher.UIThread.InvokeAsync(CheckForUpdatesIfNeeded);
        Dispatcher.UIThread.InvokeAsync(() => CheckBlankSetting(sender, e));

        var logoImage = this.FindControl<Image>("LogoImage");
        if (logoImage != null)
        {
            logoImage.Source = _colorChangingImage;
            logoImage.IsVisible = true;
        }
    }

    private void CheckIsFCRequiredSetting(object? sender, EventArgs? e)
    {
        {
            var settingsFilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "osuautodeafen", "settings.txt");

            if (File.Exists(settingsFilePath))
            {
                var lines = File.ReadAllLines(settingsFilePath);
                var fcSettingLine = Array.Find(lines, line => line.StartsWith("IsFCRequired"));
                if (fcSettingLine != null)
                {
                    var settings = fcSettingLine.Split('=');
                    if (settings.Length == 2 && bool.TryParse(settings[1], out var parsedisFcRequired))
                    {
                        ViewModel.IsFCRequired = parsedisFcRequired;
                        this.FindControl<CheckBox>("FCToggle")!.IsChecked = parsedisFcRequired;
                    }
                }
                else
                {
                    ViewModel.IsFCRequired = false;
                    SaveSettingsToFile(false, "IsFcRequired");
                    this.FindControl<CheckBox>("FCToggle")!.IsChecked = false;
                }
            }
            else
            {
                Directory.CreateDirectory(Path.GetDirectoryName(settingsFilePath));
                ViewModel.IsFCRequired = false;
                SaveSettingsToFile(false, "IsFCRequired");
                this.FindControl<CheckBox>("FCToggle").IsChecked = false;
            }
        }
    }

    public async void CheckForUpdatesButton_Click(object sender, RoutedEventArgs e)
    {
        await _updateChecker.FetchLatestVersionAsync();

        if (string.IsNullOrEmpty(_updateChecker.latestVersion))
        {
            Console.WriteLine("Latest version string is null or empty.");
            return;
        }

        var currentVersion = new Version(UpdateChecker.currentVersion);
        var latestVersion = new Version(_updateChecker.latestVersion);

        if (latestVersion > currentVersion) ShowUpdateNotification();
        if (string.IsNullOrEmpty(_updateChecker.latestVersion))
            ViewModel.UpdateStatusMessage = "Failed to check for updates.";
        else if (latestVersion > currentVersion)
            ViewModel.UpdateStatusMessage = $"Update available: v{_updateChecker.latestVersion}";
        else
            ViewModel.UpdateStatusMessage = "No updates available.";
    }

    private void LoadSettings()
    {
        var settingsFilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "osuautodeafen", "settings.txt");

        if (!File.Exists(settingsFilePath))
        {
            ViewModel.MinCompletionPercentage = 60;
            ViewModel.StarRating = 0;
            ViewModel.PerformancePoints = 0;
            ViewModel.IsParallaxEnabled = true;
            ViewModel.IsBlurEffectEnabled = true;
            ViewModel.IsFCRequired = true;
            ViewModel.UndeafenAfterMiss = false;
            SaveSettingsToFile();
            return;
        }

        var settingsLines = File.ReadAllLines(settingsFilePath);
        foreach (var line in settingsLines)
        {
            var settings = line.Split('=');
            if (settings.Length != 2) continue;

            switch (settings[0].Trim())
            {
                case "Hotkey":
                    break;
                case "MinCompletionPercentage":
                    if (int.TryParse(settings[1], out var parsedPercentage))
                        ViewModel.MinCompletionPercentage = parsedPercentage;

                    break;
                case "StarRating":
                    if (int.TryParse(settings[1], out var parsedRating)) ViewModel.StarRating = parsedRating;

                    break;
                case "PerformancePoints":
                    if (int.TryParse(settings[1], out var parsedPP)) ViewModel.PerformancePoints = parsedPP;

                    break;
                case "IsParallaxEnabled":
                    if (bool.TryParse(settings[1], out var parsedIsParallaxEnabled))
                        ViewModel.IsParallaxEnabled = parsedIsParallaxEnabled;

                    break;
                case "IsBlurEffectEnabled":
                    if (bool.TryParse(settings[1], out var parsedIsBlurEffectEnabled))
                        ViewModel.IsBlurEffectEnabled = parsedIsBlurEffectEnabled;

                    break;
                case "UndeafenAfterMiss":
                    if (bool.TryParse(settings[1], out var parsedUndeafenAfterMiss))
                        ViewModel.UndeafenAfterMiss = parsedUndeafenAfterMiss;

                    break;
                case "IsBlankScreenEnabled":
                    if (bool.TryParse(settings[1], out var parsedIsBlankScreenEnabled))
                        ViewModel.IsBlankScreenEnabled = parsedIsBlankScreenEnabled;
                    break;
            }
        }
    }

    private void SaveSettingsToFile()
    {
        var settingsFilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "osuautodeafen", "settings.txt");

        Directory.CreateDirectory(Path.GetDirectoryName(settingsFilePath) ?? throw new InvalidOperationException());

        string[] settingsLines =
        {
            $"MinCompletionPercentage={ViewModel.MinCompletionPercentage}",
            $"StarRating={ViewModel.StarRating}",
            $"PerformancePoints={ViewModel.PerformancePoints}",
            $"IsParallaxEnabled={ViewModel.IsParallaxEnabled}",
            $"IsBlurEffectEnabled={ViewModel.IsBlurEffectEnabled}",
            $"Hotkey={ViewModel.DeafenKeybind}",
            $"IsBlankScreenEnabled={ViewModel.IsBlankScreenEnabled}"
        };

        // update updatedeafenkeybinddisplay with hotkey
        UpdateDeafenKeybindDisplay();


        File.WriteAllLines(settingsFilePath, settingsLines);
    }

    private bool IsModifierKey(Key key)
    {
        return key == Key.LeftCtrl || key == Key.RightCtrl ||
               key == Key.LeftAlt || key == Key.RightAlt ||
               key == Key.LeftShift || key == Key.RightShift;
    }

    private void CheckBackgroundSetting(object? sender, EventArgs? e)
    {
        var settingsFilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "osuautodeafen", "settings.txt");

        if (File.Exists(settingsFilePath))
        {
            var lines = File.ReadAllLines(settingsFilePath);
            var backgroundSettingLine = Array.Find(lines, line => line.StartsWith("IsBackgroundEnabled"));
            if (backgroundSettingLine != null)
            {
                var settings = backgroundSettingLine.Split('=');
                if (settings.Length == 2 && bool.TryParse(settings[1], out var parsedIsBackgroundEnabled))
                {
                    ViewModel.IsBackgroundEnabled = parsedIsBackgroundEnabled;
                    this.FindControl<CheckBox>("BackgroundToggle")!.IsChecked = parsedIsBackgroundEnabled;
                }
            }
            else
            {
                ViewModel.IsBackgroundEnabled = true;
                SaveSettingsToFile(true, "IsBackgroundEnabled");
                this.FindControl<CheckBox>("BackgroundToggle")!.IsChecked = true;
            }
        }
        else
        {
            Directory.CreateDirectory(Path.GetDirectoryName(settingsFilePath) ?? throw new InvalidOperationException());
            ViewModel.IsBackgroundEnabled = true;
            SaveSettingsToFile(true, "IsBackgroundEnabled");
            this.FindControl<CheckBox>("BackgroundToggle")!.IsChecked = true;
        }

    }

    private void CheckMissUndeafenSetting(object? sender, EventArgs? e)
    {
        var settingsFilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "osuautodeafen", "settings.txt");

        if (File.Exists(settingsFilePath))
        {
            var lines = File.ReadAllLines(settingsFilePath);
            var missUndeafenSettingLine = Array.Find(lines, line => line.StartsWith("UndeafenAfterMiss"));
            if (missUndeafenSettingLine != null)
            {
                var settings = missUndeafenSettingLine.Split('=');
                if (settings.Length == 2 && bool.TryParse(settings[1], out var parsedUndeafenAfterMiss))
                {
                    ViewModel.UndeafenAfterMiss = parsedUndeafenAfterMiss;
                    this.FindControl<CheckBox>("UndeafenOnMiss")!.IsChecked = parsedUndeafenAfterMiss;
                }
            }
            else
            {
                ViewModel.UndeafenAfterMiss = true;
                SaveSettingsToFile(true, "UnAfterMiss");
                this.FindControl<CheckBox>("UndeafenOnMiss")!.IsChecked = true;
            }
        }
        else
        {
            Directory.CreateDirectory(Path.GetDirectoryName(settingsFilePath) ?? throw new InvalidOperationException());
            ViewModel.UndeafenAfterMiss = true;
            SaveSettingsToFile(true, "UndeafenAfterMiss");
            this.FindControl<CheckBox>("UndeafenOnMiss")!.IsChecked = true;
        }
    }

    private void CheckBlankSetting(object? sender, EventArgs? e)
    {
        var settingsFilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "osuautodeafen", "settings.txt");

        if (File.Exists(settingsFilePath))
        {
            var lines = File.ReadAllLines(settingsFilePath);
            var blankSettingLine = Array.Find(lines, line => line.StartsWith("IsBlankScreenEnabled"));
            if (blankSettingLine != null)
            {
                var settings = blankSettingLine.Split('=');
                if (settings.Length == 2 && bool.TryParse(settings[1], out var parsedIsBlankScreenEnabled))
                {
                    ViewModel.IsBlankScreenEnabled = parsedIsBlankScreenEnabled;
                    this.FindControl<CheckBox>("BlankEffectToggle")!.IsChecked = parsedIsBlankScreenEnabled;
                }
            }
            else
            {
                ViewModel.IsBlankScreenEnabled = true;
                SaveSettingsToFile(true, "IsBlankScreenEnabled");
                this.FindControl<CheckBox>("BlankEffectToggle")!.IsChecked = true;
            }
        }
        else
        {
            Directory.CreateDirectory(Path.GetDirectoryName(settingsFilePath) ?? throw new InvalidOperationException());
            ViewModel.IsBlankScreenEnabled = true;
            SaveSettingsToFile(true, "IsBlankScreenEnabled");
            this.FindControl<CheckBox>("BlankEffectToggle")!.IsChecked = true;
        }
    }

    private void CheckParallaxSetting(object? sender, EventArgs? e)
    {
        var settingsFilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "osuautodeafen", "settings.txt");

        if (File.Exists(settingsFilePath))
        {
            var lines = File.ReadAllLines(settingsFilePath);
            var parallaxSettingLine = Array.Find(lines, line => line.StartsWith("IsParallaxEnabled"));
            if (parallaxSettingLine != null)
            {
                var settings = parallaxSettingLine.Split('=');
                if (settings.Length == 2 && bool.TryParse(settings[1], out var parsedIsParallaxEnabled))
                {
                    ViewModel.IsParallaxEnabled = parsedIsParallaxEnabled;
                    this.FindControl<CheckBox>("ParallaxToggle")!.IsChecked = parsedIsParallaxEnabled;
                }
            }
            else
            {
                ViewModel.IsParallaxEnabled = true;
                SaveSettingsToFile(true, "IsParallaxEnabled");
                this.FindControl<CheckBox>("ParallaxToggle")!.IsChecked = true;
            }
        }
        else
        {
            Directory.CreateDirectory(Path.GetDirectoryName(settingsFilePath) ?? throw new InvalidOperationException());
            ViewModel.IsParallaxEnabled = true;
            SaveSettingsToFile(true, "IsParallaxEnabled");
            this.FindControl<CheckBox>("ParallaxToggle")!.IsChecked = true;
        }
    }

    private void CheckBlurEffectSetting(object? sender, EventArgs? e)
    {
        var settingsFilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "osuautodeafen", "settings.txt");

        var defaultBlurEffectEnabled = true;

        if (File.Exists(settingsFilePath))
            try
            {
                string?[] lines = File.ReadAllLines(settingsFilePath);
                var blurEffectSettingLine =
                    Array.Find(lines, line => line != null && line.StartsWith("IsBlurEffectEnabled"));
                if (blurEffectSettingLine != null)
                {
                    var parts = blurEffectSettingLine.Split('=');
                    if (parts.Length == 2 && bool.TryParse(parts[1], out var parsedIsBlurEffectEnabled))
                        ViewModel.IsBlurEffectEnabled = parsedIsBlurEffectEnabled;
                    else
                        ViewModel.IsBlurEffectEnabled = defaultBlurEffectEnabled;
                }
                else
                {
                    ViewModel.IsBlurEffectEnabled = defaultBlurEffectEnabled;
                }
            }
            catch
            {
                ViewModel.IsBlurEffectEnabled = defaultBlurEffectEnabled;
            }
        else
            ViewModel.IsBlurEffectEnabled = defaultBlurEffectEnabled;

        this.FindControl<CheckBox>("BlurEffectToggle")!.IsChecked = ViewModel.IsBlurEffectEnabled;
    }

    private void DisposeTimer_Tick(object? sender, EventArgs e)
    {
        if (_bitmapQueue.Count > 0) _bitmapQueue.Dequeue().Dispose();
        _disposeTimer.Stop();
    }

    private void CompletionPercentageTextBox_TextInput(object sender, TextInputEventArgs e)
    {
        var regex = new Regex("^[0-9]{1,2}$");
        if (e.Text != null && !regex.IsMatch(e.Text)) e.Handled = true;
    }

    private void CompletionPercentageTextBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (int.TryParse(CompletionPercentageTextBox.Text, out var parsedPercentage))
        {
            if (parsedPercentage >= 0 && parsedPercentage <= 99)
            {
                ViewModel.MinCompletionPercentage = parsedPercentage;
                SaveSettingsToFile(ViewModel.MinCompletionPercentage, "MinCompletionPercentage");
            }
            else
            {
                CompletionPercentageTextBox.Text = ViewModel.MinCompletionPercentage.ToString();
            }
        }
        else
        {
            CompletionPercentageTextBox.Text = ViewModel.MinCompletionPercentage.ToString();
        }

        if (Graph != null) UpdateChart(Graph);
        isCompPctLostFocus = true;
    }

    private void StarRatingTextBox_TextInput(object sender, TextInputEventArgs e)
    {
        var regex = new Regex("^[0-9]{1,2}$");
        if (e.Text != null && !regex.IsMatch(e.Text)) e.Handled = true;
    }

    private void StarRatingTextBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (int.TryParse(StarRatingTextBox.Text, out var parsedRating))
        {
            if (parsedRating >= 0 && parsedRating <= 15)
            {
                ViewModel.StarRating = parsedRating;
                SaveSettingsToFile(ViewModel.StarRating, "StarRating");
            }
            else
            {
                StarRatingTextBox.Text = ViewModel.StarRating.ToString();
            }
        }
        else
        {
            StarRatingTextBox.Text = ViewModel.StarRating.ToString();
        }
    }

    private void PPTextBox_TextInput(object sender, TextInputEventArgs e)
    {
        var regex = new Regex("^[0-9]{1,4}$");
        if (e.Text != null && !regex.IsMatch(e.Text)) e.Handled = true;
    }

    private void PPTextBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (int.TryParse(PPTextBox.Text, out var parsedPP))
        {
            if (parsedPP >= 0 && parsedPP <= 9999)
            {
                ViewModel.PerformancePoints = parsedPP;
                SaveSettingsToFile(ViewModel.PerformancePoints, "PerformancePoints");
            }
            else
            {
                PPTextBox.Text = ViewModel.PerformancePoints.ToString();
            }
        }
        else
        {
            PPTextBox.Text = ViewModel.PerformancePoints.ToString();
        }
    }

    private void SaveSettingsToFile(object value, string settingName)
    {
        var settingsFilePath =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "osuautodeafen",
                "settings.txt");
        try
        {
            var lines = File.ReadAllLines(settingsFilePath);

            var index = Array.FindIndex(lines, line => line.StartsWith(settingName));

            var valueString = value is bool b ? b ? "true" : "false" : value.ToString();

            if (index != -1)
            {
                lines[index] = $"{settingName}={valueString}";
            }
            else
            {
                var newLines = new List<string>(lines) { $"{settingName}={valueString}" };
                lines = newLines.ToArray();
            }

            File.WriteAllLines(settingsFilePath, lines);
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex);
        }
    }

    private void UpdateBackground(object? sender, EventArgs? e)
    {
        if (!ViewModel.IsBackgroundEnabled)
        {
            if (_blurredBackground != null) _blurredBackground.IsVisible = false;
            if (_normalBackground != null) _normalBackground.IsVisible = false;
            _currentBitmap?.Dispose();
            _currentBitmap = null;
        }
        else
        {
            // if the background is enabled, check if a new background needs to be loaded
            var backgroundPath = _tosuApi.GetBackgroundPath();
            
            if (_currentBitmap == null || backgroundPath != _currentBackgroundDirectory)
            {
                if (!File.Exists(backgroundPath))
                {
                    Console.WriteLine($"The file does not exist: {backgroundPath}");
                    DisplayBlackBackground(); // fallback to black background
                    return;
                }

                Bitmap newBitmap;
                try
                {
                    newBitmap = new Bitmap(backgroundPath);
                }
                catch
                {
                    DisplayBlackBackground(); // fallback to black background
                    return;
                }

                _currentBitmap?.Dispose();
                _currentBitmap = newBitmap;
                IsBlackBackgroundDisplayed = false;
                _currentBackgroundDirectory = backgroundPath;
                UpdateUIWithNewBackground(newBitmap);

                UpdateLogoAsync();
            }
            else
            {
                if (_currentBitmap != null)
                {

                    ViewModel.PropertyChanged += (sender, args) =>
                    {
                        if (args.PropertyName == nameof(ViewModel.IsBlurEffectEnabled) || args.PropertyName == nameof(ViewModel.IsBlurEffectEnabled))
                        {
                            UpdateUIWithNewBackground(_currentBitmap);
                        }

                        //probably a better way of doing this but this is very much bandaid fix
                        if (args.PropertyName == nameof(ViewModel.IsBackgroundEnabled))
                        {
                            if (ViewModel.IsBackgroundEnabled == false)
                            {
                                var blackBitmap = CreateBlackBitmap();
                                UpdateUIWithNewBackground(blackBitmap);
                                IsBlackBackgroundDisplayed = true;
                            }
                            else
                            {
                                UpdateUIWithNewBackground(_currentBitmap);
                            }
                        }
                    };
                }
            }

            if (_blurredBackground != null && _normalBackground != null)
            {
                _blurredBackground.IsVisible = ViewModel.IsBlurEffectEnabled;
                _normalBackground.IsVisible = !ViewModel.IsBlurEffectEnabled;
            }
        }
    }

    private SKBitmap ConvertToSKBitmap(Bitmap avaloniaBitmap)
    {
        using var memoryStream = new MemoryStream();
        avaloniaBitmap.Save(memoryStream);
        memoryStream.Seek(0, SeekOrigin.Begin);
        return SKBitmap.Decode(memoryStream);
    }

    private SKColor CalculateAverageColor(SKBitmap bitmap)
    {
        var width = bitmap.Width;
        var height = bitmap.Height;
        var chunkSize = height / Environment.ProcessorCount;
        var lockObj = new object();

        long totalR = 0, totalG = 0, totalB = 0;
        var pixelCount = width * height;

        Parallel.For(0, Environment.ProcessorCount, i =>
        {
            long localTotalR = 0, localTotalG = 0, localTotalB = 0;

            var startY = i * chunkSize;
            var endY = i == Environment.ProcessorCount - 1 ? height : startY + chunkSize;

            for (var y = startY; y < endY; y++)
            for (var x = 0; x < width; x++)
            {
                var pixel = bitmap.GetPixel(x, y);
                localTotalR += pixel.Red;
                localTotalG += pixel.Green;
                localTotalB += pixel.Blue;
            }

            lock (lockObj)
            {
                totalR += localTotalR;
                totalG += localTotalG;
                totalB += localTotalB;
            }
        });

        var avgR = (byte)(totalR / pixelCount);
        var avgG = (byte)(totalG / pixelCount);
        var avgB = (byte)(totalB / pixelCount);

        return new SKColor(avgR, avgG, avgB);
    }

    private SKColor InterpolateColor(SKColor from, SKColor to, float t)
    {
        var deltaR = to.Red - from.Red;
        var deltaG = to.Green - from.Green;
        var deltaB = to.Blue - from.Blue;
        var deltaA = to.Alpha - from.Alpha;

        var r = (byte)(from.Red + deltaR * t);
        var g = (byte)(from.Green + deltaG * t);
        var b = (byte)(from.Blue + deltaB * t);
        var a = (byte)(from.Alpha + deltaA * t);

        return new SKColor(r, g, b, a);
    }

    private SKSvg LoadHighResolutionLogo(string resourceName)
    {
        try
        {
            var assembly = Assembly.GetExecutingAssembly();
            using var stream = assembly.GetManifestResourceStream(resourceName)
                               ?? throw new FileNotFoundException("Resource not found: " + resourceName);
            var svg = new SKSvg();
            svg.Load(stream);
            Console.WriteLine($"Successfully loaded SVG: {resourceName}");
            return svg;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERROR] Exception while loading SVG: {ex.Message}");
            throw;
        }
    }

    public Bitmap LoadEmbeddedResource(string resourceName)
    {
        const int maxRetries = 5;
        const int initialDelayMilliseconds = 500;
        var delay = initialDelayMilliseconds;

        for (var retryCount = 0; retryCount < maxRetries; retryCount++)
        {
            try
            {
                var assembly = Assembly.GetExecutingAssembly();
                using var resourceStream = assembly.GetManifestResourceStream(resourceName);
                if (resourceStream == null)
                {
                    throw new FileNotFoundException("Resource not found: " + resourceName);
                }

                var bitmap = new Bitmap(resourceStream);
                if (bitmap == null)
                {
                    throw new InvalidOperationException("Failed to create bitmap from resource stream.");
                }

                return bitmap;
            }
            catch (Exception ex)
            {
                Console.WriteLine(
                    $"[ERROR] Exception while loading embedded resource '{resourceName}': {ex.Message}. Attempt {retryCount + 1} of {maxRetries}.");

                if (retryCount >= maxRetries - 1)
                {
                    Console.WriteLine("[ERROR] Max retry attempts reached. Failing operation.");
                    throw;
                }

                // Exponential backoff
                Task.Delay(delay).Wait();
                delay *= 2;
            }
        }

        throw new InvalidOperationException("Failed to load embedded resource after multiple attempts.");
    }
   private async void InitializeLogo()
{
    try
    {
        var logoImage = await LoadLogoAsync("osuautodeafen.Resources.autodeafen.svg");
        UpdateViewModelWithLogo(logoImage);
        Console.WriteLine("SVG loaded successfully.");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Exception while loading logo image: {ex.Message}");
        await RetryLoadLogoAsync("osuautodeafen.Resources.autodeafen.svg");
    }

    await UpdateLogoAsync();
}

private async Task<Bitmap> LoadLogoAsync(string resourceName)
{
    return await Task.Run(() =>
    {
        var assembly = Assembly.GetExecutingAssembly();
        using var resourceStream = assembly.GetManifestResourceStream(resourceName);
        if (resourceStream == null)
        {
            throw new FileNotFoundException("Resource not found: " + resourceName);
        }

        var svg = new SKSvg();
        svg.Load(resourceStream);

        if (svg.Picture == null)
        {
            throw new InvalidOperationException("Failed to load SVG picture.");
        }

        return ConvertSvgToBitmap(svg, 100, 100);
    });
}

private async Task RetryLoadLogoAsync(string resourceName)
{
    const int maxRetries = 3;
    var retryCount = 0;
    var success = false;

    while (retryCount < maxRetries && !success)
    {
        try
        {
            retryCount++;
            Console.WriteLine($"Retrying to load SVG... Attempt {retryCount}");
            var logoImage = await LoadLogoAsync(resourceName);
            UpdateViewModelWithLogo(logoImage);
            success = true;
        }
        catch (Exception retryEx)
        {
            Console.WriteLine($"[ERROR] Retry {retryCount} failed: {retryEx.Message}");
            if (retryCount >= maxRetries)
            {
                Console.WriteLine($"[ERROR] Exception while loading SVG after {maxRetries} attempts: {retryEx.Message}");
                return; // Exit if loading the SVG fails after max retries
            }
        }
    }
}

private void UpdateViewModelWithLogo(Bitmap logoImage)
{
    var viewModel = DataContext as SharedViewModel;
    if (viewModel != null)
    {
        viewModel.ModifiedLogoImage = logoImage;
        Console.WriteLine("ModifiedLogoImage property set.");
    }
}


    //this is just here because i dont want to add a massive blocking call to wait for tosuapi with ui loading
    private async Task<string?> TryGetLowResBitmapPathAsync(int maxAttempts, int delayMilliseconds)
    {
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            var lowResBitmapPath = _getLowResBackground.GetLowResBitmapPath();
            if (!string.IsNullOrEmpty(lowResBitmapPath)) return lowResBitmapPath;
            Console.WriteLine($"Attempt {attempt} failed. Retrying in {delayMilliseconds}ms...");
            await Task.Delay(delayMilliseconds);
        }

        Console.WriteLine("[ERROR] Failed to get low resolution bitmap path after multiple attempts.");
        return null;
    }

    private Bitmap ConvertSvgToBitmap(SKSvg svg, int width, int height)
    {
        var info = new SKImageInfo(width, height);
        using var surface = SKSurface.Create(info);
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.Transparent);
        canvas.DrawPicture(svg.Picture);
        using var image = surface.Snapshot();
        using var data = image.Encode();
        using var stream = new MemoryStream(data.ToArray());
        return new Bitmap(stream);
    }


 private async Task UpdateLogoAsync()
{
    if (_getLowResBackground == null)
    {
        Console.WriteLine("[ERROR] _getLowResBackground is null");
        return;
    }

    var lowResBitmapPath = await TryGetLowResBitmapPathAsync(5, 1000);
    if (lowResBitmapPath == null)
    {
        Console.WriteLine("[ERROR] Failed to get low-resolution bitmap path");
        return;
    }

    _lowResBitmap = new Bitmap(lowResBitmapPath);
    if (_lowResBitmap == null)
    {
        Console.WriteLine("[ERROR] Failed to load low-resolution bitmap");
        return;
    }

    Console.WriteLine("Low resolution bitmap successfully loaded");

    const int maxRetries = 3;
    var retryCount = 0;
    var success = false;

    while (retryCount < maxRetries && !success)
    {
        try
        {
            retryCount++;
            Console.WriteLine($"Attempting to load high resolution logo... Attempt {retryCount}");
            _cachedLogoSvg = LoadHighResolutionLogo("osuautodeafen.Resources.autodeafen.svg");
            if (_cachedLogoSvg == null)
            {
                throw new Exception("Failed to load high-resolution logo");
            }
            Console.WriteLine("High resolution logo successfully loaded");
            success = true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERROR] Exception while loading high resolution logo: {ex.Message}");
            if (retryCount >= maxRetries)
            {
                Console.WriteLine($"[ERROR] Failed to load high resolution logo after {maxRetries} attempts.");
                return; // Exit if loading the SVG fails after max retries
            }
        }
    }

    var newAverageColor = SKColors.White;

    if (_lowResBitmap != null)
    {
        newAverageColor = await CalculateAverageColorAsync(ConvertToSKBitmap(_lowResBitmap));
    }
    else
    {
        Console.WriteLine("Low resolution bitmap is null.");
        // retry 3 times over 5 seconds to recalculate the average color

        for (var i = 0; i < 3; i++)
        {
            await Task.Delay(1000);
            if (_lowResBitmap != null)
            {
                newAverageColor = await CalculateAverageColorAsync(ConvertToSKBitmap(_lowResBitmap));
                break;
            }
        }
    }

    var steps = 40;
    var delay = 1f;

    await _animationManager.EnqueueAnimation(async () =>
    {
        for (var i = 0; i <= steps; i++)
        {
            var t = i / (float)steps;
            var interpolatedColor = InterpolateColor(_oldAverageColor, newAverageColor, t);

            var picture = _cachedLogoSvg?.Picture;
            if (picture != null)
            {
                var width = (int)picture.CullRect.Width;
                var height = (int)picture.CullRect.Height;
                var bitmap = new SKBitmap(width, height);

                await Task.Run(() =>
                {
                    using (var canvas = new SKCanvas(bitmap))
                    {
                        canvas.Clear(SKColors.Transparent);
                        canvas.DrawPicture(picture);

                        Parallel.For(0, bitmap.Height, y =>
                        {
                            for (var x = 0; x < bitmap.Width; x++)
                            {
                                var pixel = bitmap.GetPixel(x, y);
                                var newColor = new SKColor(
                                    interpolatedColor.Red,
                                    interpolatedColor.Green,
                                    interpolatedColor.Blue,
                                    pixel.Alpha);
                                bitmap.SetPixel(x, y, newColor);
                            }
                        });
                    }
                });

                // Handle null stream
                using var image = SKImage.FromBitmap(bitmap);
                using var data = image.Encode(SKEncodedImageFormat.Png, 100);
                if (data == null)
                {
                    Console.WriteLine("[ERROR] Data encoding failed");
                    continue;
                }

                using var stream = new MemoryStream();
                data.SaveTo(stream);
                stream.Seek(0, SeekOrigin.Begin);

                try
                {
                    _colorChangingImage = new Bitmap(stream);
                    //Console.WriteLine("Color changing image successfully loaded");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[ERROR] Exception while creating Bitmap from stream: {ex.Message}");
                }

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    var viewModel = DataContext as SharedViewModel;
                    if (viewModel != null)
                    {
                        try
                        {
                            viewModel.ModifiedLogoImage = new Bitmap(stream);
                            Console.WriteLine("Modified logo image updated in ViewModel");
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"[ERROR] Exception while setting ViewModel's ModifiedLogoImage: {ex.Message}");
                        }
                    }
                });
            }

            await Task.Delay((int)delay);
        }

        _oldAverageColor = newAverageColor;
    });
}

    private IImage ConvertSvgToAvaloniaImage(SKSvg svg, int width, int height)
    {
        var info = new SKImageInfo(width, height);
        using var surface = SKSurface.Create(info);
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.Transparent);
        canvas.DrawPicture(svg.Picture);
        using var image = surface.Snapshot();
        using var data = image.Encode();
        using var stream = new MemoryStream(data.ToArray());
        return new Bitmap(stream);
    }


    private async Task<SKColor> CalculateAverageColorAsync(SKBitmap bitmap)
    {
        return await Task.Run(() => CalculateAverageColor(bitmap));
    }

private async void UpdateUIWithNewBackground(Bitmap? bitmap)
{
    try
    {
        if (bitmap == null)
        {
            Console.WriteLine("Bitmap is null. Cannot update background.");
            return;
        }

        try
        {
            Console.WriteLine($"Bitmap size: {bitmap.PixelSize}");
        }
        catch (Exception ex)
        {
            Console.WriteLine("Failed to retrieve bitmap size: " + ex.Message);
            return;
        }

        var newImageControl = new Image
        {
            Source = bitmap,
            Stretch = Stretch.UniformToFill,
            Opacity = 0.2,
            ZIndex = -1
        };

        if (ViewModel.IsBlurEffectEnabled) newImageControl.Effect = new BlurEffect { Radius = 17.27 };

        var fadeInTransition = new DoubleTransition
        {
            Property = Visual.OpacityProperty,
            Duration = TimeSpan.FromSeconds(0.3),
            Easing = new QuarticEaseInOut()
        };

        var fadeOutTransition = new DoubleTransition
        {
            Property = Visual.OpacityProperty,
            Duration = TimeSpan.FromSeconds(0.3),
            Easing = new QuarticEaseInOut()
        };

        if (Content is Grid mainGrid)
        {
            var backgroundLayer = mainGrid.Children.OfType<Grid>().FirstOrDefault(g => g.Name == "BackgroundLayer");
            if (backgroundLayer == null)
            {
                backgroundLayer = new Grid { Name = "BackgroundLayer", ZIndex = -1 };
                mainGrid.Children.Insert(0, backgroundLayer);
            }
            else
            {
                backgroundLayer.Children.Clear();
            }

            var oldBackground = backgroundLayer.Children.OfType<Image>().FirstOrDefault();
            if (oldBackground != null)
            {
                oldBackground.Transitions = new Transitions { fadeOutTransition };
                oldBackground.Opacity = 0.2;
                // Remove the old background after the fade-out transition completes

                //100ms delay
                backgroundLayer.Children.Remove(oldBackground);
            }

            backgroundLayer.Children.Add(newImageControl);

            // Apply fade-in effect to the new background image
            newImageControl.Transitions = new Transitions { fadeInTransition };
            newImageControl.Opacity = 0.5;

            // Optional rendering effects and transformations
            backgroundLayer.RenderTransform = new ScaleTransform(1.05, 1.05);
            backgroundLayer.Opacity = _currentBackgroundOpacity;

        }
        else
        {
            // fallback: If the main content is not a grid or not structured as expected, replace it entirely
            var newContentGrid = new Grid();
            newContentGrid.Children.Add(newImageControl);
            Content = newContentGrid;
        }

        if (ParallaxToggle.IsChecked == true && BackgroundToggle.IsChecked == true) ApplyParallax(_mouseX, _mouseY);

        Console.WriteLine("UpdateUIWithNewBackground: Update completed.");
    }
    catch (Exception ex)
    {
        Console.WriteLine("Error updating background: " + ex.Message);
    }
}
    private Bitmap? CreateBlackBitmap(int width = 600, int height = 600)
    {
        var renderTargetBitmap = new RenderTargetBitmap(new PixelSize(width, height), new Vector(96, 96));
        using (var drawingContext = renderTargetBitmap.CreateDrawingContext())
        {
            var rect = new Rect(0, 0, width, height);
            var brush = new SolidColorBrush(Colors.Black);
            drawingContext.FillRectangle(brush, rect);
        }

        using (var stream = new MemoryStream())
        {
            renderTargetBitmap.Save(stream);
            stream.Position = 0; // reset stream position to the beginning

            return new Bitmap(stream);
        }
    }

    private Bitmap? DisplayBlackBackground()
    {
        var blackBitmap = CreateBlackBitmap();
        _currentBitmap = blackBitmap;
        UpdateUIWithNewBackground(blackBitmap);
        IsBlackBackgroundDisplayed = true;
        return blackBitmap;
    }

    private void ApplyParallax(double mouseX, double mouseY)
    {
        if (_currentBitmap == null || ParallaxToggle.IsChecked == false || BackgroundToggle.IsChecked == false) return;
        // if cursor isnt on window return
        if (mouseX < 0 || mouseY < 0 || mouseX > Width || mouseY > Height) return;
        var windowWidth = Width;
        var windowHeight = Height;

        var centerX = windowWidth / 2;
        var centerY = windowHeight / 2;

        var relativeMouseX = mouseX - centerX;
        var relativeMouseY = mouseY - centerY;

        // scaling factor to reduce movement intensity
        var scaleFactor = 0.015;

        var movementX = -(relativeMouseX * scaleFactor);
        var movementY = -(relativeMouseY * scaleFactor);

        // ensure movement doesn't exceed maximum allowed movement
        double maxMovement = 15;
        movementX = Math.Max(-maxMovement, Math.Min(maxMovement, movementX));
        movementY = Math.Max(-maxMovement, Math.Min(maxMovement, movementY));


        if (Content is Grid mainGrid)
        {
            var backgroundLayer = mainGrid.Children.OfType<Grid>().FirstOrDefault(g => g.Name == "BackgroundLayer");
            if (backgroundLayer != null && backgroundLayer.Children.Count > 0)
            {
                var background = backgroundLayer.Children[0] as Image;
                if (background != null)
                {
                    var translateTransform = new TranslateTransform(movementX, movementY);
                    background.RenderTransform = translateTransform;
                }
            }
        }
    }

    private void OnMouseMove(object sender, PointerEventArgs e)
    {
        if (ParallaxToggle.IsChecked == false || BackgroundToggle.IsChecked == false) return;

        var position = e.GetPosition(this);
        _mouseX = position.X;
        _mouseY = position.Y;

        ApplyParallax(_mouseX, _mouseY);
    }

    public void ResetButton_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.MinCompletionPercentage = 60;
        ViewModel.StarRating = 0;
        ViewModel.PerformancePoints = 0;

        SaveSettingsToFile(ViewModel.MinCompletionPercentage, "MinCompletionPercentage");
        SaveSettingsToFile(ViewModel.StarRating, "StarRating");
        SaveSettingsToFile(ViewModel.PerformancePoints, "PerformancePoints");
        SaveSettingsToFile(ViewModel.IsParallaxEnabled ? true : false, "IsParallaxEnabled");
        SaveSettingsToFile(ViewModel.IsBlurEffectEnabled ? true : false, "IsBlurEffectEnabled");

        CompletionPercentageTextBox.Text = ViewModel.MinCompletionPercentage.ToString();
        StarRatingTextBox.Text = ViewModel.StarRating.ToString();
        PPTextBox.Text = ViewModel.PerformancePoints.ToString();

        if (Graph != null) UpdateChart(Graph);
        isCompPctLostFocus = true;
    }

    private void UpdateErrorMessage(object? sender, EventArgs e)
    {
        var errorMessage = this.FindControl<TextBlock>("ErrorMessage");

        if (errorMessage != null) errorMessage.Text = _tosuApi.GetErrorMessage();
    }

    private void MainWindow_Closing(object? sender, CancelEventArgs e)
    {
        _tosuApi.Dispose();
    }

    private void TosuAPI_MessageReceived(double completionPercentage)
    {
        Console.WriteLine("Received: {0}", completionPercentage);
    }

    private async Task AdjustBackgroundOpacity(double targetOpacity, TimeSpan duration)
    {
        if (Content is Grid mainGrid)
        {
            var backgroundLayer = mainGrid.Children.OfType<Grid>().FirstOrDefault(g => g.Name == "BackgroundLayer");
            if (backgroundLayer != null)
            {
                var currentOpacity = backgroundLayer.Opacity;

                var animation = new Animation
                {
                    Duration = duration,
                    Easing = new QuarticEaseInOut()
                };

                animation.Children.Add(
                    new KeyFrame
                    {
                        Cue = new Cue(0),
                        Setters = { new Setter(Visual.OpacityProperty, currentOpacity) }
                    }
                );

                animation.Children.Add(
                    new KeyFrame
                    {
                        Cue = new Cue(1),
                        Setters = { new Setter(Visual.OpacityProperty, targetOpacity) }
                    }
                );

                // Explicitly set the final opacity to avoid visual flashing
                backgroundLayer.Opacity = targetOpacity;
                _currentBackgroundOpacity = targetOpacity;

                await animation.RunAsync(backgroundLayer, cancellationToken: default);

            }
        }
    }

private async void SettingsButton_Click(object? sender, RoutedEventArgs e)
{
    var updateBar = this.FindControl<Button>("UpdateNotificationBar");
    var isUpdateBarVisible = updateBar != null && updateBar.IsVisible;
    var settingsPanel = this.FindControl<DockPanel>("SettingsPanel");
    var settingsPanel2 = this.FindControl<DockPanel>("SettingsPanel2");
    var textBlockPanel = this.FindControl<StackPanel>("TextBlockPanel");
    var settingsPanelMargin = settingsPanel.Margin;
    var settingsPanel2Margin = settingsPanel2.Margin;
    var textBlockPanelMargin = textBlockPanel.Margin;

    settingsPanel.Transitions = new Transitions
    {
        new ThicknessTransition
        {
            Property = MarginProperty,
            Duration = TimeSpan.FromSeconds(0.25),
            Easing = new LinearEasing()
        }
    };

    textBlockPanel.Transitions = new Transitions
    {
        new ThicknessTransition
        {
            Property = MarginProperty,
            Duration = TimeSpan.FromSeconds(0.25),
            Easing = new CircularEaseInOut()
        }
    };

    if (settingsPanel.IsVisible)
    {
        settingsPanel.IsVisible = false;
        AdjustMargins(isUpdateBarVisible, settingsPanel, settingsPanel2, textBlockPanel, settingsPanelMargin, settingsPanel2Margin, textBlockPanelMargin);

        var adjustOpacityTask = AdjustBackgroundOpacity(1.0, TimeSpan.FromSeconds(0.3));
        var adjustTextBlockPanelMarginTask = InvokeOnUIThreadAsync(() =>
        {
            textBlockPanel.Margin = new Thickness(0, 42, 0, 0);
        });

        await Task.WhenAll(adjustOpacityTask, adjustTextBlockPanelMarginTask);
    }
    else
    {
        settingsPanel.IsVisible = true;
        AdjustMargins(isUpdateBarVisible, settingsPanel, settingsPanel2, textBlockPanel, settingsPanelMargin, settingsPanel2Margin, textBlockPanelMargin);

        var adjustOpacityTask = AdjustBackgroundOpacity(0.5, TimeSpan.FromSeconds(0.3));
        var adjustTextBlockPanelMarginTask = InvokeOnUIThreadAsync(() =>
        {
            textBlockPanel.Margin = new Thickness(0, 42, 225, 0);
        });

        await Task.WhenAll(adjustOpacityTask, adjustTextBlockPanelMarginTask);
    }
}

private void AdjustMargins(bool isUpdateBarVisible, DockPanel settingsPanel, DockPanel settingsPanel2, StackPanel textBlockPanel, Thickness settingsPanelMargin, Thickness settingsPanel2Margin, Thickness textBlockPanelMargin)
{
    if (isUpdateBarVisible)
    {
        settingsPanel.Margin = new Thickness(settingsPanelMargin.Left, settingsPanelMargin.Top, settingsPanelMargin.Right, 28);
        settingsPanel2.Margin = new Thickness(settingsPanel2Margin.Left, settingsPanel2Margin.Top, settingsPanel2Margin.Right, 28);
        textBlockPanel.Margin = new Thickness(textBlockPanelMargin.Left, textBlockPanelMargin.Top, textBlockPanelMargin.Right, 28);
    }
    else
    {
        settingsPanel.Margin = new Thickness(settingsPanelMargin.Left, settingsPanelMargin.Top, settingsPanelMargin.Right, 0);
        settingsPanel2.Margin = new Thickness(settingsPanel2Margin.Left, settingsPanel2Margin.Top, settingsPanel2Margin.Right, 0);
        textBlockPanel.Margin = new Thickness(textBlockPanelMargin.Left, textBlockPanelMargin.Top, textBlockPanelMargin.Right, 0);
    }
}

private Task InvokeOnUIThreadAsync(Action action)
{
    var tcs = new TaskCompletionSource<object?>();

    Dispatcher.UIThread.Post(() =>
    {
        try
        {
            action();
            tcs.SetResult(null);
        }
        catch (Exception ex)
        {
            tcs.SetException(ex);
        }
    });

    return tcs.Task;
}

    private async void SecondPage_Click(object sender, RoutedEventArgs e)
    {
        var secondPage = this.FindControl<DockPanel>("SettingsPanel2");
        var firstPage = this.FindControl<DockPanel>("SettingsPanel");

        secondPage.IsVisible = true;
        firstPage.IsVisible = false;
    }

    private async void FirstPage_Click(object sender, RoutedEventArgs e)
    {
        var secondPage = this.FindControl<DockPanel>("SettingsPanel2");
        var firstPage = this.FindControl<DockPanel>("SettingsPanel");

        secondPage.IsVisible = false;
        firstPage.IsVisible = true;
    }

    public void BlankEffectToggle_IsCheckedChanged(object? sender, RoutedEventArgs e)
    {
        if (sender is CheckBox checkBox)
        {
            if (_screenBlankerForm == null)
                // Initialize _screenBlankerForm if it is null
                _screenBlankerForm = new ScreenBlankerForm(this);


            if (checkBox.IsChecked == true)
                // Initialize blanking windows if not already initialized
                _screenBlankerForm.InitializeBlankingWindows();
            else
                // Unblank screens
                _screenBlankerForm.UnblankScreensAsync();
        }
    }

    public class HotKey
    {
        public Key Key { get; init; }
        public KeyModifiers ModifierKeys { get; init; }

        public override string ToString()
        {
            List<string> parts = [];

            if (ModifierKeys.HasFlag(KeyModifiers.Control))
                parts.Add("Ctrl");
            if (ModifierKeys.HasFlag(KeyModifiers.Alt))
                parts.Add("Alt");
            if (ModifierKeys.HasFlag(KeyModifiers.Shift))
                parts.Add("Shift");

            parts.Add(Key.ToString()); // always add the key last

            return string.Join("+", parts); // join all parts with '+'
        }

        public static HotKey Parse(string str)
        {
            if (string.IsNullOrEmpty(str))
                throw new ArgumentException("Invalid hotkey format. Expected 'KeyModifierKey'.");

            var parts = str.Split('+');
            if (parts.Length != 2) throw new ArgumentException("Invalid hotkey format. Expected 'KeyModifierKey'.");

            if (!Enum.TryParse(parts[0], true, out KeyModifiers modifierKeys))
                throw new ArgumentException($"Invalid modifier key: {parts[0]}");

            if (!Enum.TryParse(parts[1], true, out Key key)) throw new ArgumentException($"Invalid key: {parts[1]}");

            return new HotKey { Key = key, ModifierKeys = modifierKeys };
        }
    }
}