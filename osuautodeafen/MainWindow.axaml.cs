using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
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
using Avalonia.Threading;
using LiveChartsCore;
using LiveChartsCore.Defaults;
using LiveChartsCore.Drawing;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using osuautodeafen.cs;
using osuautodeafen.cs.Screen;
using SkiaSharp;
using Path = System.IO.Path;

namespace osuautodeafen;

public partial class MainWindow : Window
{
    public static bool isCompPctLostFocus;

    //testing out a capacity of 0 for now, this means a ram-usage reduction of 50% 🤯
    private readonly Queue<Bitmap> _bitmapQueue = new(0);
    private readonly DispatcherTimer _disposeTimer;
    private readonly DispatcherTimer _mainTimer;
    private readonly DispatcherTimer _parallaxCheckTimer;
    private readonly TosuApi _tosuApi;
    private Grid? _blackBackground;
    private Image? _blurredBackground;
    private ScreenBlanker _screenBlanker;
    private ScreenBlankerForm _screenBlankerForm;
    private Deafen _deafen;

    private string? _currentBackgroundDirectory;
    private Bitmap? _currentBitmap;
    private KeyModifiers _currentKeyModifiers = KeyModifiers.None;
    private HotKey? _deafenKeybind;
    private LineSeries<ObservablePoint> _deafenMarker;
    private Thread _graphDataThread;
    private bool _hasDisplayed = false;
    private bool _isConstructorFinished;

    private Key _lastKeyPressed = Key.None;
    private DateTime _lastKeyPressTime = DateTime.MinValue;
    private DateTime _lastUpdate = DateTime.Now;
    private DateTime _lastUpdateCheck = DateTime.MinValue;
    private double _mouseX;
    private double _mouseY;
    private Image? _normalBackground;


    private readonly UpdateChecker _updateChecker = UpdateChecker.GetInstance();
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

        SettingsPanel settingsPanel = new SettingsPanel();

        var settingsPanel1 = new SettingsPanel();

        LoadSettings();

        this.Icon = new WindowIcon("Resources/oad.ico");

        _tosuApi = new TosuApi();

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
            Interval = TimeSpan.FromMilliseconds(200)
        };
        _mainTimer.Tick += MainTimer_Tick;
        _mainTimer.Start();

        InitializeVisibilityCheckTimer();

        var stringBuilder = new StringBuilder();

        var oldContent = this.Content;

        this.Content = null;

        this.Content = new Grid
        {
            Children =
            {
                new ContentControl { Content = oldContent }
            }
        };

        InitializeViewModel();

        this.PointerMoved += OnMouseMove;

        DataContext = ViewModel;

        InitializeGraphDataThread();

        _tosuApi.GraphDataUpdated += OnGraphDataUpdated;


        Series = [];
        XAxes = new Axis[] { new Axis { LabelsPaint = new SolidColorPaint(SKColors.White) } };
        YAxes = new Axis[] { new Axis { LabelsPaint = new SolidColorPaint(SKColors.White) } };
        PlotView.Series = Series;
        PlotView.XAxes = XAxes;
        PlotView.YAxes = YAxes;

        var series1 = new StackedAreaSeries<ObservablePoint>
        {
            Values = ChartData.Series1Values,
            Fill = new SolidColorPaint { Color = new SkiaSharp.SKColor(0xFF, 0x00, 0x00) },
            Stroke = new SolidColorPaint { Color = new SkiaSharp.SKColor(0xFF, 0x00, 0x00) }
        };

        var series2 = new StackedAreaSeries<ObservablePoint>
        {
            Values = ChartData.Series2Values,
            Fill = new SolidColorPaint { Color = new SkiaSharp.SKColor(0x00, 0xFF, 0x00) },
            Stroke = new SolidColorPaint { Color = new SkiaSharp.SKColor(0x00, 0xFF, 0x00) }
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

        this.DataContext = settingsPanel1;
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
            if (point.Y <= titleBarHeight)
            {
                BeginMoveDrag(e);
            }
        };

        InitializeKeybindButtonText();
        UpdateDeafenKeybindDisplay();

        CompletionPercentageTextBox.Text = ViewModel.MinCompletionPercentage.ToString();
        StarRatingTextBox.Text = ViewModel.StarRating.ToString();
        PPTextBox.Text = ViewModel.PerformancePoints.ToString();

        BorderBrush = Brushes.Black;
        this.Width = 600;
        this.Height = 600;
        this.CanResize = false;
        this.Closing += MainWindow_Closing;
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
        {
            if (Graph != null) await Dispatcher.UIThread.InvokeAsync(() => UpdateChart(Graph));
            Thread.Sleep(1000);
        }
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
    public void UpdateChart(GraphData graphData)
    {
        var seriesList = new List<ISeries>();

        foreach (var series in graphData.Series)
        {
            var updatedValues = series.Data.ToList();

            var color = series.Name == "aim"
                ? new SKColor(0x00, 0xFF, 0x00, 192)
                : new SKColor(0x00, 0x00, 0xFF, 140); // Adjust transparency
            var name = series.Name == "aim" ? "Aim" : "Speed";

            var lineSeries = new LineSeries<ObservablePoint>
            {
                Values = updatedValues.Select((value, index) => new ObservablePoint(index, value)).ToList(),
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
        var maxLimit = Math.Ceiling((double)graphData.XAxis.Last() / 1000);

        deafenProgressPercentage = _deafen.MinCompletionPercentage / 100.0;
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
                LabelsPaint = new SolidColorPaint(SKColors.White),
                MinLimit = 0, // ensure the x-axis starts from 0
                MaxLimit = maxLimit,
                Padding = new Padding(2),
                TextSize = 12,
                Labeler = value =>
                {
                    var timeSpan = TimeSpan.FromMinutes(value / 60);
                    return $"{(int)timeSpan.TotalMinutes}:{timeSpan.Seconds:D2}";
                }
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
        ;

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

        if (!ViewModel.IsBackgroundEnabled)
        {
            _currentBitmap?.Dispose();
            _currentBitmap = null; // ensure _currentBitmap is null after disposal to avoid accessing a disposed object

            var newBitmap =
                DisplayBlackBackground(); // create a new Bitmap and assign it to _currentBitmap inside DisplayBlackBackground
            UpdateUIWithNewBackground(newBitmap); // pass the new Bitmap to UpdateUIWithNewBackground
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

        UpdateUIWithNewBackground(_currentBitmap);
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
            }

            if (_blurredBackground != null && _normalBackground != null)
            {
                _blurredBackground.IsVisible = ViewModel.IsBlurEffectEnabled;
                _normalBackground.IsVisible = !ViewModel.IsBlurEffectEnabled;
            }
        }
    }

    private void UpdateUIWithNewBackground(Bitmap bitmap)
    {
        var imageControl = new Image
        {
            Source = bitmap,
            Stretch = Stretch.UniformToFill,
            Opacity = 0.5,
            ZIndex = -1
        };

        if (ViewModel.IsBlurEffectEnabled) imageControl.Effect = new BlurEffect { Radius = 17.27 };

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

            backgroundLayer.Children.Add(imageControl);
            backgroundLayer.RenderTransform = new ScaleTransform(1.05, 1.05);
        }
        else
        {
            // fallback: If the main content is not a grid or not structured as expected, replace it entirely
            var newContentGrid = new Grid();
            newContentGrid.Children.Add(imageControl);
            Content = newContentGrid;
        }

        if (ParallaxToggle.IsChecked == true && BackgroundToggle.IsChecked == true) ApplyParallax(_mouseX, _mouseY);
    }

    private Bitmap CreateBlackBitmap(int width = 600, int height = 600)
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

    private Bitmap DisplayBlackBackground()
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

    private async void SettingsButton_Click(object? sender, RoutedEventArgs e)
    {
        var updateBar = this.FindControl<Button>("UpdateNotificationBar");
        var isUpdateBarVisible = updateBar != null && updateBar.IsVisible;
        var settingsPanel = this.FindControl<DockPanel>("SettingsPanel");
        var textBlockPanel = this.FindControl<StackPanel>("TextBlockPanel");
        var settingsPanelMargin = settingsPanel.Margin;
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
            if (isUpdateBarVisible)
            {
                settingsPanel.Margin = new Thickness(settingsPanelMargin.Left, settingsPanelMargin.Top,
                    settingsPanelMargin.Right, 28);
                textBlockPanel.Margin = new Thickness(textBlockPanelMargin.Left, textBlockPanelMargin.Top,
                    textBlockPanelMargin.Right, 28);
            }
            else
            {
                settingsPanel.Margin = new Thickness(settingsPanelMargin.Left, settingsPanelMargin.Top,
                    settingsPanelMargin.Right, 0);
                textBlockPanel.Margin = new Thickness(textBlockPanelMargin.Left, textBlockPanelMargin.Top,
                    textBlockPanelMargin.Right, 0);
            }
        }
        else
        {
            settingsPanel.IsVisible = true;
            if (isUpdateBarVisible)
            {
                settingsPanel.Margin = new Thickness(settingsPanelMargin.Left, settingsPanelMargin.Top,
                    settingsPanelMargin.Right, 28);
                textBlockPanel.Margin = new Thickness(textBlockPanelMargin.Left, textBlockPanelMargin.Top,
                    textBlockPanelMargin.Right, 28);
            }
            else
            {
                settingsPanel.Margin = new Thickness(settingsPanelMargin.Left, settingsPanelMargin.Top,
                    settingsPanelMargin.Right, 0);
                textBlockPanel.Margin = new Thickness(textBlockPanelMargin.Left, textBlockPanelMargin.Top,
                    textBlockPanelMargin.Right, 0);
            }
        }

        textBlockPanel.Margin = settingsPanel.IsVisible ? new Thickness(0, 42, 225, 0) : new Thickness(0, 42, 0, 0);
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

    public async void BlankEffectToggleDeafen()
    {

    }

    public void BlankEffectToggle_IsCheckedChanged(object? sender, RoutedEventArgs e)
    {

    }
}