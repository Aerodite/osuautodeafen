using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
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

    private readonly Queue<Bitmap> _bitmapQueue = new(1);
    private readonly Deafen _deafen;
    private readonly DispatcherTimer _disposeTimer;
    private readonly GetLowResBackground? _getLowResBackground;
    private readonly DispatcherTimer _mainTimer;
    private readonly DispatcherTimer _parallaxCheckTimer;
    private readonly TosuApi _tosuApi;

    private readonly UpdateChecker _updateChecker = UpdateChecker.GetInstance();
    private readonly object _updateLock = new();


    private readonly object _updateLogoLock = new();
    private readonly SharedViewModel _viewModel;
    private Grid? _blackBackground;
    private Image? _blurredBackground;
    private readonly BreakPeriodCalculator _breakPeriod;
    private SKSvg? _cachedLogoSvg;

    private double? _cachedMaxXLimit = null;
    private CancellationTokenSource _cancellationTokenSource = new();
    private Bitmap _colorChangingImage;

    private string? _currentBackgroundDirectory;

    private double _currentBackgroundOpacity = 1.0;
    private Bitmap? _currentBitmap;
    private KeyModifiers _currentKeyModifiers = KeyModifiers.None;
    private HotKey? _deafenKeybind;
    private LineSeries<ObservablePoint> _deafenMarker;
    private Thread _graphDataThread;
    private bool _hasDisplayed = false;
    private bool _isConstructorFinished;
    private bool _isTransitioning = false;
    private double _lastCompletionPercentage = -1;

    private Key _lastKeyPressed = Key.None;
    private DateTime _lastKeyPressTime = DateTime.MinValue;
    private DateTime _lastUpdateCheck = DateTime.MinValue;

    private Bitmap? _lastValidBitmap;

    //_lowres
    private Bitmap? _lowResBitmap;
    private double _mouseX;
    private double _mouseY;
    private Image? _normalBackground;
    private SKColor _oldAverageColor = SKColors.Transparent;

    private Canvas _progressIndicatorCanvas;
    private Line _progressIndicatorLine;
    private ScreenBlanker _screenBlanker;
    private ScreenBlankerForm? _screenBlankerForm;
    private DispatcherTimer? _visibilityCheckTimer;
    private List<RectangularSection> cachedBreakPeriods = new();
    private string? cachedOsuFilePath;
    private double deafenProgressPercentage;
    private double deafenTimestamp;
    private double maxLimit;
    private double maxYValue;

    private LineSeries<ObservablePoint>? progressIndicator;
    private SettingsPanel settingsPanel1;


    //<summary>
    // constructor for the ui and subsequent panels
    //</summary>
    public MainWindow()
    {
        InitializeComponent();

        _viewModel = new SharedViewModel();

        var settingsPanel = new SettingsPanel();

        var settingsPanel1 = new SettingsPanel();

        LoadSettings();

        Icon = new WindowIcon(LoadEmbeddedResource("osuautodeafen.Resources.oad.ico"));

        _tosuApi = new TosuApi();

        _getLowResBackground = new GetLowResBackground(_tosuApi);

        _breakPeriod = new BreakPeriodCalculator();

        _deafen = new Deafen(_tosuApi, settingsPanel1, _breakPeriod);

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
            Interval = TimeSpan.FromMilliseconds(20)
        };
        _mainTimer.Tick += MainTimer_Tick;
        _mainTimer.Start();

        _viewModel.PropertyChanged += ViewModel_PropertyChanged;

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

        InitializeProgressIndicator();


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

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SharedViewModel.CompletionPercentage))
            Dispatcher.UIThread.InvokeAsync(() => UpdateProgressIndicator(_tosuApi.GetCompletionPercentage()));
    }

    private void OnGraphDataUpdated(GraphData graphData)
    {
        if (graphData.Series.Count > 1)
        {
            graphData.Series[0].Name = "aim";
            graphData.Series[1].Name = "speed";
        }

        ChartData.Series1Values = graphData.Series[0].Data
            .Select((value, index) => new ObservablePoint(index, value))
            .ToList();
        ChartData.Series2Values = graphData.Series[1].Data
            .Select((value, index) => new ObservablePoint(index, value))
            .ToList();

        Dispatcher.UIThread.InvokeAsync(() => UpdateChart(graphData));

        _deafen.MinCompletionPercentage = ViewModel.MinCompletionPercentage;
    }

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
        try
        {
            if (progressIndicator == null)
                progressIndicator = new LineSeries<ObservablePoint>
                {
                    Stroke = new SolidColorPaint { Color = new SKColor(0xFF, 0xFF, 0xFF, 192), StrokeThickness = 5 },
                    GeometryFill = null,
                    GeometryStroke = null,
                    LineSmoothness = 0,
                    Values = new List<ObservablePoint>()
                };

            var seriesList = PlotView.Series?.Where(s => s.Name != "Progress Indicator").ToList() ??
                             new List<ISeries>();
            PlotView.DrawMargin = new Margin(0, 0, 0, 0);

            foreach (var series in graphData.Series)
            {
                var trimmedData = series.Data.SkipWhile(value => value == -100).ToList();
                trimmedData.Reverse();
                trimmedData = trimmedData.SkipWhile(value => value == -100).ToList();
                trimmedData.Reverse();

                var updatedValues = new List<ObservablePoint>(trimmedData.Count);

                double maxXValue = trimmedData.Count;
                maxLimit = maxXValue;
                maxYValue = graphData.Series.SelectMany(s => s.Data).Where(value => value != -100).Max();

                for (var i = 0; i < trimmedData.Count; i++)
                {
                    var value = trimmedData[i];
                    if (value != -100) updatedValues.Add(new ObservablePoint(i, value));
                }

                var smoothedValues = SmoothData(updatedValues, 10, 0.2);
                var color = series.Name == "aim"
                    ? new SKColor(0x00, 0xFF, 0x00, 192)
                    : new SKColor(0x00, 0x00, 0xFF, 140);
                var name = series.Name == "aim" ? "Aim" : "Speed";

                var existingLineSeries =
                    seriesList.OfType<LineSeries<ObservablePoint>>().FirstOrDefault(s => s.Name == name);
                if (existingLineSeries != null)
                {
                    existingLineSeries.Values = smoothedValues;
                    existingLineSeries.TooltipLabelFormatter = null; // Disable tooltips
                }
                else
                {
                    seriesList.Add(new LineSeries<ObservablePoint>
                    {
                        Values = smoothedValues,
                        Fill = new SolidColorPaint { Color = color },
                        Stroke = new SolidColorPaint { Color = color },
                        Name = name,
                        GeometryFill = null,
                        GeometryStroke = null,
                        LineSmoothness = 1,
                        EasingFunction = EasingFunctions.ExponentialOut,
                        TooltipLabelFormatter = null // Disable tooltips
                    });
                }
            }

            var deafenStart = ViewModel.MinCompletionPercentage * maxLimit / 100.0;
            var deafenRectangle = new RectangularSection
            {
                Xi = deafenStart,
                Xj = maxLimit,
                Yi = 0,
                Yj = maxYValue,
                Fill = new SolidColorPaint { Color = new SKColor(0xFF, 0x00, 0x00, 64) } // Semi-transparent red
            };

            var sections = new List<RectangularSection> { deafenRectangle };

            var osuFilePath = _tosuApi.GetFullFilePath();
            if (osuFilePath != null && osuFilePath != cachedOsuFilePath)
            {
                cachedBreakPeriods = _breakPeriod
                    .ParseBreakPeriods(osuFilePath, graphData.XAxis, graphData.Series[0].Data)
                    .Select(breakPeriod => new RectangularSection
                    {
                        Xi = breakPeriod.StartIndex / _tosuApi.RateAdjustRate(),
                        Xj = breakPeriod.EndIndex / _tosuApi.RateAdjustRate(),
                        Yi = 0,
                        Yj = maxYValue,
                        Fill = new SolidColorPaint
                            { Color = new SKColor(0xFF, 0xFF, 0x00, 98) } // Semi-transparent yellow
                    }).ToList();
                cachedOsuFilePath = osuFilePath;
            }

            sections.AddRange(cachedBreakPeriods);

            // Adjust deafen points to avoid overlap with break points
            foreach (var breakPeriod in sections.Where(s =>
                         s.Fill.Equals(new SolidColorPaint { Color = new SKColor(0xFF, 0xFF, 0x00, 64) })))
                if (deafenRectangle.Xi < breakPeriod.Xj && deafenRectangle.Xj > breakPeriod.Xi)
                    deafenRectangle.Xi = breakPeriod.Xj;

            PlotView.Sections = sections;

            seriesList.Add(progressIndicator);

            Series = seriesList.ToArray();
            PlotView.Series = seriesList.ToArray();

            XAxes = new Axis[]
            {
                new()
                {
                    LabelsPaint = new SolidColorPaint(SKColors.Transparent),
                    MinLimit = 0,
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
                    MinLimit = 0,
                    MaxLimit = maxYValue,
                    Padding = new Padding(2),
                    SeparatorsPaint = new SolidColorPaint(SKColors.Transparent)
                }
            };

            PlotView.XAxes = XAxes;
            PlotView.YAxes = YAxes;

            UpdateProgressIndicator(_tosuApi.GetCompletionPercentage());
            PlotView.InvalidateVisual();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"An error occurred while updating the chart: {ex.Message}");
        }
    }

    private void InitializeProgressIndicator()
    {
        _progressIndicatorCanvas = new Canvas
        {
            IsHitTestVisible = false,
            Background = Brushes.Transparent
        };

        _progressIndicatorLine = new Line
        {
            Stroke = new SolidColorBrush(Color.FromArgb(192, 255, 255, 255)),
            StrokeThickness = 5
        };

        _progressIndicatorCanvas.Children.Add(_progressIndicatorLine);
        (PlotView.Parent as Grid)?.Children.Add(_progressIndicatorCanvas);
    }

    private void UpdateProgressIndicator(double completionPercentage)
    {
        try
        {
            if (XAxes.Length == 0 || completionPercentage < 0 || completionPercentage > 100)
            {
                Console.WriteLine("No valid XAxes found in the chart or the completion percentage is out of range.");
                return;
            }

            // Skip update if there's no significant change in completion percentage
            if (Math.Abs(completionPercentage - _lastCompletionPercentage) < 0.1) return;

            _lastCompletionPercentage = completionPercentage;

            // Cache the first X-axis limit and line series
            var xAxis = XAxes.Length > 0 ? XAxes[0] : null;
            var maxXLimit = xAxis?.MaxLimit;
            if (!maxXLimit.HasValue)
            {
                Console.WriteLine("Max X-axis limit not defined.");
                return;
            }

            // Calculate the progress position and left edge
            var progressPosition = completionPercentage / 100 * maxXLimit.Value;
            var leftEdgePosition = Math.Max(progressPosition - 0.1, 0);

            // Get the relevant line series (Aim and Speed)
            var lineSeriesList = Series.OfType<LineSeries<ObservablePoint>>()
                .Where(s => s.Name == "Aim" || s.Name == "Speed")
                .ToList();

            if (lineSeriesList.Count == 0)
            {
                Console.WriteLine("No line series found in the chart.");
                return;
            }

            // Precompute the sorted points for each series
            var sortedPointsCache = lineSeriesList.ToDictionary(
                series => series,
                series => series.Values.OrderBy(p => p.X).ToList()
            );

            // Create a list to hold the top contour points for the progress indicator
            var topContourPoints = new List<ObservablePoint>
            {
                new(leftEdgePosition, 0) // Bottom-left corner
            };

            // Calculate the interpolated points along the top contour
            var step = Math.Max((progressPosition - leftEdgePosition) / 10, 0.1);
            for (var x = leftEdgePosition; x <= progressPosition; x += step)
            {
                double maxInterpolatedY = 0;

                foreach (var series in lineSeriesList)
                {
                    var points = sortedPointsCache[series];
                    if (points.Count == 0) continue;

                    // Perform a binary search to find the closest points for interpolation
                    var leftIndex = points.BinarySearch(new ObservablePoint(x, 0), new ObservablePointComparer());
                    if (leftIndex < 0) leftIndex = ~leftIndex - 1;

                    var leftPoint = points[Math.Max(leftIndex, 0)];
                    var rightPoint = points[Math.Min(leftIndex + 1, points.Count - 1)];

                    // Interpolate the Y value based on the X position
                    var interpolatedY = InterpolateY(leftPoint, rightPoint, x);
                    maxInterpolatedY = Math.Max(maxInterpolatedY, interpolatedY);
                }

                topContourPoints.Add(new ObservablePoint(x, maxInterpolatedY));
            }

            // Calculate the Y value at the right edge of the progress indicator
            var rightEdgeY = lineSeriesList.Max(series =>
            {
                var points = sortedPointsCache[series];
                if (points.Count == 0) return 0;

                var leftIndex = points.BinarySearch(new ObservablePoint(progressPosition, 0),
                    new ObservablePointComparer());
                if (leftIndex < 0) leftIndex = ~leftIndex - 1;
                var leftPoint = points[Math.Max(leftIndex, 0)];
                var rightPoint = points[Math.Min(leftIndex + 1, points.Count - 1)];

                return InterpolateY(leftPoint, rightPoint, progressPosition);
            });

            // Add the right edge and closure points to complete the contour
            topContourPoints.Add(new ObservablePoint(progressPosition, rightEdgeY)); // Top-right corner
            topContourPoints.Add(new ObservablePoint(progressPosition, 0)); // Bottom-right corner
            topContourPoints.Add(new ObservablePoint(leftEdgePosition, 0)); // Bottom-left corner

            // Set the top contour points as the values for the progress indicator
            progressIndicator.Values = topContourPoints;

            // Ensure the progress indicator is added to the series list
            if (!Series.Contains(progressIndicator)) Series = Series.Append(progressIndicator).ToArray();

            // Invalidate the visual to trigger a redraw
            PlotView.InvalidateVisual();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"An error occurred while updating the progress indicator: {ex.Message}");
        }
    }

    private double InterpolateY(ObservablePoint leftPoint, ObservablePoint rightPoint, double x)
    {
        if (leftPoint.X == rightPoint.X)
            return (double)leftPoint.Y;

        return (double)(leftPoint.Y + (rightPoint.Y - leftPoint.Y) * (x - leftPoint.X) / (rightPoint.X - leftPoint.X));
    }

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
        var friendlyKeyName = GetFriendlyKeyName(e.Key);
        ViewModel.DeafenKeybind = new HotKey { Key = e.Key, ModifierKeys = modifiers, FriendlyName = friendlyKeyName };

        // save the new hotkey to settings
        SaveSettingsToFile(ViewModel.DeafenKeybind.ToString(), "Hotkey");

        e.Handled = true;
    }

    private string GetFriendlyKeyName(Key key)
    {
        return key switch
        {
            Key.D0 => "0",
            Key.D1 => "1",
            Key.D2 => "2",
            Key.D3 => "3",
            Key.D4 => "4",
            Key.D5 => "5",
            Key.D6 => "6",
            Key.D7 => "7",
            Key.D8 => "8",
            Key.D9 => "9",
            Key.OemOpenBrackets => "[",
            Key.OemCloseBrackets => "]",
            Key.OemComma => ",",
            Key.OemPeriod => ".",
            Key.OemMinus => "-",
            Key.OemPlus => "+",
            Key.OemQuestion => "/",
            Key.OemSemicolon => ";",
            Key.OemQuotes => "'",
            Key.OemBackslash => "\\",
            Key.OemPipe => "|",
            Key.OemTilde => "`",
            Key.Oem8 => "Oem8",
            _ => key.ToString()
        };
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
        Dispatcher.UIThread.InvokeAsync(() => CheckBreakUndeafenEnabled(sender, e));
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

    private void CheckBreakUndeafenEnabled(object? sender, EventArgs? e)
    {
        var settingsFilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "osuautodeafen", "settings.txt");

        if (File.Exists(settingsFilePath))
        {
            var lines = File.ReadAllLines(settingsFilePath);
            var breakUndeafenSettingLine = Array.Find(lines, line => line.StartsWith("BreakUndeafenEnabled"));
            if (breakUndeafenSettingLine != null)
            {
                var settings = breakUndeafenSettingLine.Split('=');
                if (settings.Length == 2 && bool.TryParse(settings[1], out var parsedIsBreakUndeafenEnabled))
                {
                    ViewModel.BreakUndeafenEnabled = parsedIsBreakUndeafenEnabled;
                    this.FindControl<CheckBox>("BreakUndeafenToggle")!.IsChecked = parsedIsBreakUndeafenEnabled;
                }
            }
            else
            {
                ViewModel.BreakUndeafenEnabled = true;
                SaveSettingsToFile(true, "BreakUndeafenEnabled");
                this.FindControl<CheckBox>("BreakUndeafenToggle")!.IsChecked = true;
            }
        }
        else
        {
            Directory.CreateDirectory(Path.GetDirectoryName(settingsFilePath) ?? throw new InvalidOperationException());
            ViewModel.BreakUndeafenEnabled = true;
            SaveSettingsToFile(true, "BreakUndeafenEnabled");
            this.FindControl<CheckBox>("BreakUndeafenToggle")!.IsChecked = true;
        }
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


    private async void UpdateBackground(object? sender, EventArgs? e)
    {
        // Disable background if ViewModel indicates
        if (!ViewModel.IsBackgroundEnabled)
        {
            if (!ViewModel.IsBackgroundEnabled)
            {
                if (_blurredBackground != null) _blurredBackground.IsVisible = false;
                if (_normalBackground != null) _normalBackground.IsVisible = false;
            }

            return;
        }

        var backgroundPath = _tosuApi.GetBackgroundPath();

        if (_currentBitmap == null || backgroundPath != _currentBackgroundDirectory)
        {
            _currentBackgroundDirectory = backgroundPath;

            var newBitmap = await LoadBitmapAsync(backgroundPath);
            if (newBitmap == null)
            {
                Console.WriteLine($"Failed to load background: {backgroundPath}");
                return;
            }

            _currentBitmap?.Dispose();
            _currentBitmap = newBitmap;

            await Dispatcher.UIThread.InvokeAsync(() => UpdateUIWithNewBackgroundAsync(_currentBitmap));
            await Task.Run(() => UpdateLogoAsync());
            IsBlackBackgroundDisplayed = false;
        }

        if (_currentBitmap != null)
            ViewModel.PropertyChanged += async (sender, args) =>
            {
                if (args.PropertyName == nameof(ViewModel.IsParallaxEnabled) ||
                    args.PropertyName == nameof(ViewModel.IsBlurEffectEnabled))
                    await Dispatcher.UIThread.InvokeAsync(() => UpdateUIWithNewBackgroundAsync(_currentBitmap));

                //probably a better way of doing this but this is very much bandaid fix
                if (args.PropertyName == nameof(ViewModel.IsBackgroundEnabled))
                {
                    if (ViewModel.IsBackgroundEnabled == false)
                    {
                        if (IsBlackBackgroundDisplayed) return;
                        await DisplayBlackBackground();
                    }
                    else
                    {
                        if (!IsBlackBackgroundDisplayed) return;
                        await Dispatcher.UIThread.InvokeAsync(() => UpdateUIWithNewBackgroundAsync(_currentBitmap));
                        IsBlackBackgroundDisplayed = false;
                    }
                }
            };

        UpdateBackgroundVisibility();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ViewModel.IsParallaxEnabled) ||
            e.PropertyName == nameof(ViewModel.IsBlurEffectEnabled))
            _ = Dispatcher.UIThread.InvokeAsync(() => UpdateUIWithNewBackgroundAsync(_currentBitmap));
        else if (e.PropertyName == nameof(ViewModel.IsBackgroundEnabled))
            _ = Dispatcher.UIThread.InvokeAsync(async () =>
            {
                if (!ViewModel.IsBackgroundEnabled)
                {
                    if (!IsBlackBackgroundDisplayed)
                        await DisplayBlackBackground();
                }
                else
                {
                    if (IsBlackBackgroundDisplayed)
                    {
                        await UpdateUIWithNewBackgroundAsync(_currentBitmap);
                        IsBlackBackgroundDisplayed = false;
                    }
                }
            });
    }

    private async Task<Bitmap?> LoadBitmapAsync(string path)
    {
        return await Task.Run(() =>
        {
            if (File.Exists(path))
                try
                {
                    return new Bitmap(path);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Failed to load bitmap from {path}: {ex.Message}");
                }
            else
                Console.WriteLine($"Background file not found: {path}");

            return null;
        });
    }

    private async Task UpdateUIWithNewBackgroundAsync(Bitmap? bitmap)
    {
        if (bitmap == null)
        {
            Console.WriteLine("Bitmap is null, using the last valid bitmap.");
            bitmap = _lastValidBitmap;
            if (bitmap == null)
            {
                Console.WriteLine("No valid bitmap available, cannot update UI.");
                return;
            }
        }
        else
        {
            _lastValidBitmap = bitmap;
        }

        lock (_updateLock)
        {
            _cancellationTokenSource.Cancel();
            _cancellationTokenSource = new CancellationTokenSource();
        }

        var token = _cancellationTokenSource.Token;

        await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            if (token.IsCancellationRequested)
                return;

            try
            {
                if (Content is not Grid mainGrid)
                {
                    Content = new Grid();
                    mainGrid = (Grid)Content;
                }

                var newImageControl = new Image
                {
                    Source = bitmap,
                    Stretch = Stretch.UniformToFill,
                    Opacity = 0.25,
                    ZIndex = -1,
                    Effect = ViewModel?.IsBlurEffectEnabled == true ? new BlurEffect { Radius = 17.27 } : null,
                    Clip = new RectangleGeometry(new Rect(0, 0, mainGrid.Bounds.Width * 1.05,
                        mainGrid.Bounds.Height * 1.05))
                };

                var transition = new DoubleTransition
                {
                    Property = OpacityProperty,
                    Duration = TimeSpan.FromSeconds(0.3),
                    Easing = new QuarticEaseInOut()
                };

                var backgroundLayer = mainGrid.Children.OfType<Grid>().FirstOrDefault(g => g.Name == "BackgroundLayer")
                                      ?? new Grid { Name = "BackgroundLayer", ZIndex = -1 };

                if (!mainGrid.Children.Contains(backgroundLayer))
                    mainGrid.Children.Insert(0, backgroundLayer);
                else
                    backgroundLayer.Children.Clear();

                backgroundLayer.RenderTransform = new ScaleTransform(1.05, 1.05);

                var oldBackground = backgroundLayer.Children.OfType<Image>().FirstOrDefault();
                if (oldBackground != null)
                {
                    oldBackground.Transitions = new Transitions { transition };
                    oldBackground.Opacity = 0.25;

                    await Task.Delay(250, token);

                    if (!token.IsCancellationRequested)
                    {
                        backgroundLayer.Children.Remove(oldBackground);
                        oldBackground.Source = null;
                    }
                }

                if (token.IsCancellationRequested)
                    return;

                backgroundLayer.Children.Add(newImageControl);
                newImageControl.Transitions = new Transitions { transition };
                newImageControl.Opacity = 0.5;
                backgroundLayer.Opacity = _currentBackgroundOpacity;

                if (ViewModel != null && ParallaxToggle?.IsChecked == true && BackgroundToggle?.IsChecked == true)
                    ApplyParallax(_mouseX, _mouseY);

                Console.WriteLine("UpdateUIWithNewBackground: Update completed.");
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error updating UI: " + ex.Message);
            }
        });
    }

    private void UpdateBackgroundVisibility()
    {
        if (_blurredBackground != null && _normalBackground != null)
        {
            _blurredBackground.IsVisible = ViewModel.IsBlurEffectEnabled;
            _normalBackground.IsVisible = !ViewModel.IsBlurEffectEnabled;
        }
    }

    private SKBitmap ConvertToSKBitmap(Bitmap avaloniaBitmap)
    {
        var width = avaloniaBitmap.PixelSize.Width;
        var height = avaloniaBitmap.PixelSize.Height;
        var skBitmap = new SKBitmap(width, height);

        using (var renderTargetBitmap = new RenderTargetBitmap(new PixelSize(width, height), new Vector(96, 96)))
        {
            using (var drawingContext = renderTargetBitmap.CreateDrawingContext())
            {
                drawingContext.DrawImage(avaloniaBitmap, new Rect(0, 0, width, height), new Rect(0, 0, width, height));
            }

            var pixelDataSize = width * height * 4; // Assuming 4 bytes per pixel (BGRA)
            var pixelDataPtr = Marshal.AllocHGlobal(pixelDataSize);

            try
            {
                var rect = new PixelRect(0, 0, width, height);
                renderTargetBitmap.CopyPixels(rect, pixelDataPtr, pixelDataSize, width * 4);

                var pixelData = new byte[pixelDataSize];
                Marshal.Copy(pixelDataPtr, pixelData, 0, pixelDataSize);

                var destPtr = skBitmap.GetPixels();
                Marshal.Copy(pixelData, 0, destPtr, pixelDataSize);
            }
            finally
            {
                Marshal.FreeHGlobal(pixelDataPtr);
            }
        }

        return skBitmap;
    }

    private SKColor CalculateAverageColor(SKBitmap bitmap)
    {
        if (bitmap == null) throw new ArgumentNullException(nameof(bitmap), "Bitmap cannot be null");

        var width = bitmap.Width;
        var height = bitmap.Height;

        if (width == 0 || height == 0) throw new ArgumentException("Bitmap dimensions cannot be zero", nameof(bitmap));

        var pixelCount = width * height;

        long totalR = 0, totalG = 0, totalB = 0;

        Parallel.For(0, height, y =>
        {
            long localTotalR = 0, localTotalG = 0, localTotalB = 0;

            for (var x = 0; x < width; x++)
            {
                var pixel = bitmap.GetPixel(x, y);
                localTotalR += pixel.Red;
                localTotalG += pixel.Green;
                localTotalB += pixel.Blue;
            }

            Interlocked.Add(ref totalR, localTotalR);
            Interlocked.Add(ref totalG, localTotalG);
            Interlocked.Add(ref totalB, localTotalB);
        });

        var avgR = (byte)(totalR / pixelCount);
        var avgG = (byte)(totalG / pixelCount);
        var avgB = (byte)(totalB / pixelCount);

        return new SKColor(avgR, avgG, avgB);
    }

    private SKColor InterpolateColor(SKColor from, SKColor to, float t)
    {
        if (t < 0f || t > 1f)
            throw new ArgumentOutOfRangeException(nameof(t), "Interpolation factor must be between 0 and 1");

        byte InterpolateComponent(byte start, byte end, float factor)
        {
            return (byte)(start + (end - start) * factor);
        }

        var r = InterpolateComponent(from.Red, to.Red, t);
        var g = InterpolateComponent(from.Green, to.Green, t);
        var b = InterpolateComponent(from.Blue, to.Blue, t);
        var a = InterpolateComponent(from.Alpha, to.Alpha, t);

        return new SKColor(r, g, b, a);
    }

    private SKSvg LoadHighResolutionLogo(string resourceName)
    {
        try
        {
            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName)
                               ?? throw new FileNotFoundException("Resource not found: " + resourceName);
            var svg = new SKSvg();
            svg.Load(stream);
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
            try
            {
                using var resourceStream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName)
                                           ?? throw new FileNotFoundException("Resource not found: " + resourceName);

                return new Bitmap(resourceStream)
                       ?? throw new InvalidOperationException("Failed to create bitmap from resource stream.");
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
        using var resourceStream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName)
                                   ?? throw new FileNotFoundException("Resource not found: " + resourceName);

        var svg = new SKSvg();
        svg.Load(resourceStream);

        return svg.Picture == null
            ? throw new InvalidOperationException("Failed to load SVG picture.")
            : ConvertSvgToBitmap(svg, 100, 100);
    }

    private async Task RetryLoadLogoAsync(string resourceName)
    {
        const int maxRetries = 3;
        var retryCount = 0;
        var success = false;

        while (retryCount < maxRetries && !success)
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
                    Console.WriteLine(
                        $"[ERROR] Exception while loading SVG after {maxRetries} attempts: {retryEx.Message}");
                    return; // Exit if loading the SVG fails after max retries
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

        // Start loading low-res bitmap and high-res SVG in parallel
        var lowResBitmapTask = TryGetLowResBitmapPathAsync(5, 1000);
        var highResLogoTask = Task.Run(() =>
        {
            try
            {
                return LoadHighResolutionLogo("osuautodeafen.Resources.autodeafen.svg");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] Exception while loading high-resolution logo: {ex.Message}");
                return null;
            }
        });

        var lowResBitmapPath = await lowResBitmapTask;
        if (lowResBitmapPath == null)
        {
            Console.WriteLine("[ERROR] Failed to get low-resolution bitmap path");
            return;
        }

        if (!File.Exists(lowResBitmapPath))
        {
            Console.WriteLine("[ERROR] Low-resolution bitmap path does not exist");
            return;
        }

        _lowResBitmap = new Bitmap(lowResBitmapPath);
        if (_lowResBitmap == null)
        {
            Console.WriteLine("[ERROR] Failed to load low-resolution bitmap");
            return;
        }

        Console.WriteLine("Low resolution bitmap successfully loaded");

        _cachedLogoSvg = await highResLogoTask;
        if (_cachedLogoSvg == null)
        {
            Console.WriteLine("[ERROR] Failed to load high-resolution logo");
            return;
        }

        var newAverageColor = SKColors.White;

        // Calculate newAverageColor only if _lowResBitmap is not null
        if (_lowResBitmap != null) newAverageColor = await CalculateAverageColorAsync(ConvertToSKBitmap(_lowResBitmap));

        var steps = 20; // Increased steps for smoother gradient
        var delay = 1; // Fastest possible delay in milliseconds

        await _animationManager.EnqueueAnimation(async () =>
        {
            if (_cachedLogoSvg?.Picture == null)
            {
                Console.WriteLine("[ERROR] Cached logo SVG or its picture is null");
                return;
            }

            var originalPicture = _cachedLogoSvg.Picture;
            var width = (int)originalPicture.CullRect.Width;
            var height = (int)originalPicture.CullRect.Height;

            for (var i = 0; i <= steps; i++)
            {
                var t = i / (float)steps;
                var interpolatedColor = InterpolateColor(_oldAverageColor, newAverageColor, t);

                var bitmap = await Task.Run(() =>
                {
                    var tempBitmap = new SKBitmap(width, height);
                    using (var canvas = new SKCanvas(tempBitmap))
                    {
                        canvas.Clear(SKColors.Transparent);
                        using var paint = new SKPaint();
                        canvas.DrawPicture(originalPicture, paint);

                        // Apply the interpolated color to each pixel
                        for (var y = 0; y < tempBitmap.Height; y++)
                        for (var x = 0; x < tempBitmap.Width; x++)
                        {
                            var pixel = tempBitmap.GetPixel(x, y);
                            var newColor = new SKColor(
                                interpolatedColor.Red,
                                interpolatedColor.Green,
                                interpolatedColor.Blue,
                                pixel.Alpha
                            );
                            tempBitmap.SetPixel(x, y, newColor);
                        }
                    }

                    return tempBitmap;
                });

                using var image = SKImage.FromBitmap(bitmap);
                using var data = image.Encode(SKEncodedImageFormat.Png, 100);
                if (data == null)
                {
                    Console.WriteLine("[ERROR] Data encoding failed");
                    continue;
                }

                await using var stream = new MemoryStream(data.ToArray());

                try
                {
                    _colorChangingImage = new Bitmap(stream);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[ERROR] Exception while creating Bitmap from stream: {ex.Message}");
                }

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (DataContext is SharedViewModel viewModel)
                        try
                        {
                            viewModel.ModifiedLogoImage = new Bitmap(stream);
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine(
                                $"[ERROR] Exception while setting ViewModel's ModifiedLogoImage: {ex.Message}");
                        }
                });

                await Task.Delay(delay);
            }

            _oldAverageColor = newAverageColor;
        });
    }

    private async Task<SKColor> CalculateAverageColorAsync(SKBitmap bitmap)
    {
        return await Task.Run(() => CalculateAverageColor(bitmap));
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

    private Task DisplayBlackBackground()
    {
        var blackBitmap = CreateBlackBitmap();
        UpdateUIWithNewBackgroundAsync(blackBitmap);
        IsBlackBackgroundDisplayed = true;
        return Task.CompletedTask;
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

        // Check if the mouse is within the window bounds
        if (_mouseX < 0 || _mouseY < 0 || _mouseX > Width || _mouseY > Height) return;

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

        if (Graph != null)
        {
            // Clear and re-add the series to force the chart to refresh
            var tempSeries = PlotView.Series.ToList();
            PlotView.Series = new List<ISeries>();
            PlotView.Series = tempSeries;
            UpdateChart(Graph);
        }

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
                        Setters = { new Setter(OpacityProperty, currentOpacity) }
                    }
                );

                animation.Children.Add(
                    new KeyFrame
                    {
                        Cue = new Cue(1),
                        Setters = { new Setter(OpacityProperty, targetOpacity) }
                    }
                );

                // Explicitly set the final opacity to avoid visual flashing
                backgroundLayer.Opacity = targetOpacity;
                _currentBackgroundOpacity = targetOpacity;

                await animation.RunAsync(backgroundLayer);
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
            AdjustMargins(isUpdateBarVisible, settingsPanel, settingsPanel2, textBlockPanel, settingsPanelMargin,
                settingsPanel2Margin, textBlockPanelMargin);

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
            AdjustMargins(isUpdateBarVisible, settingsPanel, settingsPanel2, textBlockPanel, settingsPanelMargin,
                settingsPanel2Margin, textBlockPanelMargin);

            var adjustOpacityTask = AdjustBackgroundOpacity(0.5, TimeSpan.FromSeconds(0.3));
            var adjustTextBlockPanelMarginTask = InvokeOnUIThreadAsync(() =>
            {
                textBlockPanel.Margin = new Thickness(0, 42, 225, 0);
            });

            await Task.WhenAll(adjustOpacityTask, adjustTextBlockPanelMarginTask);
        }
    }

    private void AdjustMargins(bool isUpdateBarVisible, DockPanel settingsPanel, DockPanel settingsPanel2,
        StackPanel textBlockPanel, Thickness settingsPanelMargin, Thickness settingsPanel2Margin,
        Thickness textBlockPanelMargin)
    {
        if (isUpdateBarVisible)
        {
            settingsPanel.Margin = new Thickness(settingsPanelMargin.Left, settingsPanelMargin.Top,
                settingsPanelMargin.Right, 28);
            settingsPanel2.Margin = new Thickness(settingsPanel2Margin.Left, settingsPanel2Margin.Top,
                settingsPanel2Margin.Right, 28);
            textBlockPanel.Margin = new Thickness(textBlockPanelMargin.Left, textBlockPanelMargin.Top,
                textBlockPanelMargin.Right, 28);
        }
        else
        {
            settingsPanel.Margin = new Thickness(settingsPanelMargin.Left, settingsPanelMargin.Top,
                settingsPanelMargin.Right, 0);
            settingsPanel2.Margin = new Thickness(settingsPanel2Margin.Left, settingsPanel2Margin.Top,
                settingsPanel2Margin.Right, 0);
            textBlockPanel.Margin = new Thickness(textBlockPanelMargin.Left, textBlockPanelMargin.Top,
                textBlockPanelMargin.Right, 0);
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

    public async void BlankEffectToggle_IsCheckedChanged(object? sender, RoutedEventArgs e)
    {
        if (!OperatingSystem.IsWindows())
        {
            Console.WriteLine("Blank effect is only supported on Windows.");
            return;
        }

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
                await _screenBlankerForm.UnblankScreensAsync();
        }
    }

    private void InitializeKeybindButtonText()
    {
        var currentKeybind = RetrieveKeybindFromSettings();
        var deafenKeybindButton = this.FindControl<Button>("DeafenKeybindButton");
        if (deafenKeybindButton != null) deafenKeybindButton.Content = currentKeybind;
    }

    private void BreakUndeafenToggle_IsCheckChanged(object? sender, RoutedEventArgs e)
    {
        if (sender is CheckBox checkBox)
        {
            ViewModel.BreakUndeafenEnabled = checkBox.IsChecked == true;
            SaveSettingsToFile(ViewModel.BreakUndeafenEnabled, "BreakUndeafenEnabled");
        }
    }

    private class ObservablePointComparer : IComparer<ObservablePoint>
    {
        public int Compare(ObservablePoint x, ObservablePoint y)
        {
            if (x.X < y.X) return -1;
            if (x.X > y.X) return 1;
            return 0;
        }
    }

    public class HotKey
    {
        public Key Key { get; init; }
        public KeyModifiers ModifierKeys { get; init; }
        public string FriendlyName { get; set; }

        public override string ToString()
        {
            List<string> parts = new();

            if (ModifierKeys.HasFlag(KeyModifiers.Control))
                parts.Add("Ctrl");
            if (ModifierKeys.HasFlag(KeyModifiers.Alt))
                parts.Add("Alt");
            if (ModifierKeys.HasFlag(KeyModifiers.Shift))
                parts.Add("Shift");

            parts.Add(FriendlyName);

            return string.Join("+", parts).Replace("==", "="); // Fix for equal key
        }

        public static HotKey Parse(string str)
        {
            if (string.IsNullOrEmpty(str))
                throw new ArgumentException("Invalid hotkey format. Expected 'KeyModifierKey'.");

            var parts = str.Split('+');
            if (parts.Length < 2) throw new ArgumentException("Invalid hotkey format. Expected 'KeyModifierKey'.");

            var modifiers = KeyModifiers.None;
            var key = Key.None;
            var friendlyName = "";

            foreach (var part in parts)
                if (Enum.TryParse(part, true, out KeyModifiers modifier))
                {
                    modifiers |= modifier;
                }
                else if (Enum.TryParse(part, true, out Key parsedKey))
                {
                    key = parsedKey;
                    friendlyName = part;
                }
                else
                {
                    friendlyName = part;
                }

            return new HotKey { Key = key, ModifierKeys = modifiers, FriendlyName = friendlyName };
        }
    }
}