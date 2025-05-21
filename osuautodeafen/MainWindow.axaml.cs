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
using Avalonia.Layout;
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
    private readonly AnimationManager _animationManager = new();
    private readonly Queue<Bitmap> _bitmapQueue = new(1);
    private readonly BreakPeriodCalculator _breakPeriod;
    private readonly Deafen _deafen;
    private readonly DispatcherTimer _disposeTimer;
    private readonly GetLowResBackground? _getLowResBackground;
    private readonly DispatcherTimer _mainTimer;
    private readonly DispatcherTimer _parallaxCheckTimer;

    private readonly UpdateChecker _updateChecker = UpdateChecker.GetInstance();
    private readonly object _updateLock = new();


    private readonly object _updateLogoLock = new();

    private readonly SharedViewModel _viewModel;
    private BlurEffect? _backgroundBlurEffect;

    private PropertyChangedEventHandler? _backgroundPropertyChangedHandler;
    private Grid? _blackBackground;
    private Image? _blurredBackground;
    private SKSvg? _cachedLogoSvg;

    private double? _cachedMaxXLimit = null;
    private CancellationTokenSource _cancellationTokenSource = new();
    private readonly Bitmap _colorChangingImage = null!;

    private CancellationTokenSource? _colorTransitionCts;

    private string? _currentBackgroundDirectory;

    private double _currentBackgroundOpacity = 1.0;
    private Bitmap? _currentBitmap;
    private KeyModifiers _currentKeyModifiers = KeyModifiers.None;
    private HotKey? _deafenKeybind;
    private LineSeries<ObservablePoint> _deafenMarker = null!;
    private Thread _graphDataThread = null!;
    private bool _hasDisplayed = false;
    private bool _isConstructorFinished;
    private bool _isTransitioning = false;
    private double _lastCompletionPercentage = -1;

    private Key _lastKeyPressed = Key.None;
    private DateTime _lastKeyPressTime = DateTime.MinValue;
    private DateTime _lastUpdateCheck = DateTime.MinValue;

    private Bitmap? _lastValidBitmap;
    private LogoControl? _logoControl;

    private Animation? _logoSpinAnimation;
    private CancellationTokenSource? _logoSpinCts;

    private Bitmap? _lowResBitmap;
    private double _mouseX;
    private double _mouseY;
    private Image? _normalBackground;
    private SKColor _oldAverageColor = SKColors.Transparent;

    private SKColor _oldAverageColorPublic = SKColors.Transparent;

    private CancellationTokenSource? _opacityCts;
    private double? _originalLogoHeight;

    private double? _originalLogoWidth;

    private Canvas _progressIndicatorCanvas = null!;
    private Line _progressIndicatorLine = null!;
    private ScreenBlanker _screenBlanker = null!;
    private ScreenBlankerForm? _screenBlankerForm;

    private DispatcherTimer? _spinTimer;
    public TosuApi _tosuApi = new();
    private DispatcherTimer? _visibilityCheckTimer;
    private List<RectangularSection> cachedBreakPeriods = new();
    private string? cachedOsuFilePath;
    private double deafenProgressPercentage;
    private double deafenTimestamp;
    private double maxLimit;
    private double maxYValue;

    private LineSeries<ObservablePoint>? progressIndicator;
    private readonly SettingsPanel settingsPanel1 = null!;


    //<summary>
    // constructor for the ui and subsequent panels
    //</summary>
    public MainWindow()
    {
        InitializeComponent();

        // ViewModel and DataContext
        _viewModel = new SharedViewModel(_tosuApi);
        ViewModel = _viewModel;
        DataContext = _viewModel;

        // Settings and resources
        LoadSettings();
        Icon = new WindowIcon(LoadEmbeddedResource("osuautodeafen.Resources.oad.ico"));

        // Core services
        _getLowResBackground = new GetLowResBackground(_tosuApi);
        _breakPeriod = new BreakPeriodCalculator();

        // Settings panel
        var settingsPanel = new SettingsPanel();

        _deafen = new Deafen(_tosuApi, settingsPanel1, _breakPeriod, _viewModel);

        // Timers
        _disposeTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _disposeTimer.Tick += DisposeTimer_Tick;

        _parallaxCheckTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _parallaxCheckTimer.Tick += CheckParallaxSetting;
        _parallaxCheckTimer.Start();

        _mainTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        _mainTimer.Tick += MainTimer_Tick;
        _mainTimer.Start();
        _viewModel.PropertyChanged += ViewModel_PropertyChanged;

        // UI setup
        InitializeVisibilityCheckTimer();

        var oldContent = Content;
        Content = null;
        Content = new Grid
        {
            Children = { new ContentControl { Content = oldContent } }
        };

        InitializeViewModel();
        InitializeLogo();

        _tosuApi.BeatmapChanged += async () =>
        {
            var logoImage = this.FindControl<Image>("LogoImage");
            if (logoImage != null)
            {
                logoImage.Source = _colorChangingImage;
                logoImage.IsVisible = true;
            }

            await Dispatcher.UIThread.InvokeAsync(() => UpdateBackground(null, null));

            // Calculate the new average color from the background or logo
            var skBitmap = ConvertToSKBitmap(_currentBitmap);
            if (skBitmap != null)
            {
                var newAverageColor = CalculateAverageColor(skBitmap);
                await UpdateAverageColorAsync(newAverageColor);
            }

            var currentBeatmapSet = _tosuApi.GetBeatmapSetId();
            Console.WriteLine($"Current Beatmap Set ID: {currentBeatmapSet}");
            //if (currentBeatmapSet == 2058976)
                //HeatAbnormalEasterEgg();
                //sry too lazy to fix after LogoControl() refactor
           // else
                //ResetLogoSize();

            OnGraphDataUpdated(_tosuApi.GetGraphData());
        };

        PointerMoved += OnMouseMove;

        // Chart setup
        Series = [];
        XAxes = new[] { new Axis { LabelsPaint = new SolidColorPaint(SKColors.White) } };
        YAxes = new[] { new Axis { LabelsPaint = new SolidColorPaint(SKColors.White) } };
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

        settingsPanel.Transitions = new Transitions
        {
            new DoubleTransition
            {
                Property = OpacityProperty,
                Duration = TimeSpan.FromSeconds(0.5),
                Easing = new QuarticEaseInOut()
            }
        };

        // Window appearance and events
        ExtendClientAreaToDecorationsHint = true;
        ExtendClientAreaTitleBarHeightHint = 32;
        ExtendClientAreaChromeHints = ExtendClientAreaChromeHints.PreferSystemChrome;
        Background = Brushes.Black;
        BorderBrush = Brushes.Black;
        Width = 600;
        Height = 600;
        CanResize = false;
        Closing += MainWindow_Closing;

        PointerPressed += (sender, e) =>
        {
            var point = e.GetPosition(this);
            const int titleBarHeight = 34;
            if (point.Y <= titleBarHeight) BeginMoveDrag(e);
        };

        // UI textboxes and keybinds
        InitializeKeybindButtonText();
        UpdateDeafenKeybindDisplay();
        CompletionPercentageTextBox.Text = ViewModel.MinCompletionPercentage.ToString();
        StarRatingTextBox.Text = ViewModel.StarRating.ToString();
        PPTextBox.Text = ViewModel.PerformancePoints.ToString();

        _isConstructorFinished = true;

        // this is only here to replace that stupid timer logic :)
        // good sacrifice if you ask me
        Task.Run(async () =>
        {
            for (var i = 0; i < 4; i++) // 2 Seconds
            {
                await Dispatcher.UIThread.InvokeAsync(() => UpdateBackground(null, null));
                await Task.Delay(500);
            }
        });
    }

    private SharedViewModel ViewModel { get; }
    private bool IsBlackBackgroundDisplayed { get; set; }

    public GraphData? Graph { get; set; }
    public ISeries[] Series { get; set; }
    public Axis[] XAxes { get; set; }
    public Axis[] YAxes { get; set; }

    private void HeatAbnormalEasterEgg()
    {
        var logoImage = this.FindControl<Image>("LogoImage");
        if (logoImage != null)
        {
            if (_originalLogoWidth == null || _originalLogoHeight == null)
            {
                _originalLogoWidth = logoImage.Width;
                _originalLogoHeight = logoImage.Height;
            }

            logoImage.Width = 2 * _originalLogoWidth.Value;
            logoImage.Height = 2 * _originalLogoHeight.Value;
            logoImage.Margin = new Thickness(0, 0, 0, 0);
            logoImage.IsVisible = true;

            TransformGroup group;
            RotateTransform rotate;
            ScaleTransform scale;

            if (logoImage.RenderTransform is TransformGroup existingGroup &&
                existingGroup.Children.Count == 2 &&
                existingGroup.Children[0] is RotateTransform r &&
                existingGroup.Children[1] is ScaleTransform s)
            {
                group = existingGroup;
                rotate = r;
                scale = s;
            }
            else
            {
                rotate = new RotateTransform();
                scale = new ScaleTransform();
                group = new TransformGroup { Children = { rotate, scale } };
                logoImage.RenderTransform = group;
            }

            _spinTimer?.Stop();
            _spinTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
            double angle = 0;
            double t = 0;
            _spinTimer.Tick += (s, e) =>
            {
                angle = (angle + 6) % 360;
                rotate.Angle = angle;

                // Animate scale with a sine wave (pulsing effect)
                t += 0.1;
                var scaleValue = 1.0 + 0.2 * Math.Sin(t); // Range: 0.8 to 1.2
                scale.ScaleX = scale.ScaleY = scaleValue;
            };
            _spinTimer.Start();
        }
    }

    private void ResetLogoSize()
    {
        var logoImage = this.FindControl<Image>("LogoImage");
        if (logoImage != null && _originalLogoWidth != null && _originalLogoHeight != null)
        {
            logoImage.Width = _originalLogoWidth.Value;
            logoImage.Height = _originalLogoHeight.Value;
            logoImage.Margin = new Thickness(0, 0, 0, 0);

            // Stop the spin timer if running
            _spinTimer?.Stop();

            // Reset rotation and scale if using a TransformGroup
            if (logoImage.RenderTransform is TransformGroup group)
            {
                var rotate = group.Children.OfType<RotateTransform>().FirstOrDefault();
                if (rotate != null)
                    rotate.Angle = 0;

                var scale = group.Children.OfType<ScaleTransform>().FirstOrDefault();
                if (scale != null)
                {
                    scale.ScaleX = 1.0;
                    scale.ScaleY = 1.0;
                }
            }
            else if (logoImage.RenderTransform is RotateTransform rotate)
            {
                rotate.Angle = 0;
            }
            else if (logoImage.RenderTransform is ScaleTransform scale)
            {
                scale.ScaleX = 1.0;
                scale.ScaleY = 1.0;
            }
        }
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SharedViewModel.CompletionPercentage))
            Dispatcher.UIThread.InvokeAsync(() => UpdateProgressIndicator(_tosuApi.GetCompletionPercentage()));
    }

    private void OnGraphDataUpdated(GraphData? graphData)
    {
        if (graphData == null || graphData.Series.Count < 2)
            return;

        graphData.Series[0].Name = "aim";
        graphData.Series[1].Name = "speed";

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
        var smoothedData = new List<ObservablePoint>(data.Count);
        var adjustedWindow = Math.Max(1, (int)(windowSize * smoothingFactor));
        double sum = 0;
        var count = 0;
        var left = 0;

        for (var i = 0; i < data.Count; i++)
        {
            // Add new point to window
            sum += data[i].Y ?? 0.0;
            count++;

            // Remove old point if window exceeded
            if (i - left + 1 > adjustedWindow * 2 + 1)
            {
                sum -= data[left].Y ?? 0.0;
                left++;
                count--;
            }

            smoothedData.Add(new ObservablePoint(data[i].X, sum / count));
        }

        return smoothedData;
    }

    public void UpdateChart(GraphData? graphData)
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

            const int maxPoints = 1000;

            // Precompute maxYValue once for all series
            maxYValue = graphData.Series.SelectMany(s => s.Data).Where(value => value != -100).DefaultIfEmpty(0).Max();

            for (var sIdx = 0; sIdx < graphData.Series.Count; sIdx++)
            {
                var series = graphData.Series[sIdx];
                // Trim -100 from both ends efficiently
                int start = 0, end = series.Data.Count - 1;
                while (start <= end && series.Data[start] == -100) start++;
                while (end >= start && series.Data[end] == -100) end--;

                if (end < start) continue;

                var updatedValues = new List<ObservablePoint>(end - start + 1);
                for (var i = start; i <= end; i++)
                {
                    var value = series.Data[i];
                    if (value != -100) updatedValues.Add(new ObservablePoint(i - start, value));
                }

                maxLimit = updatedValues.Count;

                // Downsample before smoothing
                var downsampledValues = Downsample(updatedValues, maxPoints);
                var smoothedValues = SmoothData(downsampledValues, 10, 0.2);

                var color = series.Name == "aim"
                    ? new SKColor(0x00, 0xFF, 0x00, 192)
                    : new SKColor(0x00, 0x00, 0xFF, 140);
                var name = series.Name == "aim" ? "Aim" : "Speed";

                var existingLineSeries =
                    seriesList.OfType<LineSeries<ObservablePoint>>().FirstOrDefault(s => s.Name == name);
                if (existingLineSeries != null)
                {
                    existingLineSeries.Values = smoothedValues;
                    existingLineSeries.TooltipLabelFormatter = null;
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
                        TooltipLabelFormatter = null
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
                Fill = new SolidColorPaint { Color = new SKColor(0xFF, 0x00, 0x00, 64) }
            };

            var sections = new List<RectangularSection> { deafenRectangle };

            var osuFilePath = _tosuApi.GetFullFilePath();
            if (osuFilePath != null && osuFilePath != cachedOsuFilePath)
            {
                cachedBreakPeriods = _breakPeriod
                    .ParseBreakPeriods(osuFilePath, graphData.XAxis, graphData.Series[0].Data)
                    .Select(breakPeriod => new RectangularSection
                    {
                        Xi = breakPeriod.StartIndex / _tosuApi.GetRateAdjustRate(),
                        Xj = breakPeriod.EndIndex / _tosuApi.GetRateAdjustRate(),
                        Yi = 0,
                        Yj = maxYValue,
                        Fill = new SolidColorPaint { Color = new SKColor(0xFF, 0xFF, 0x00, 98) }
                    }).ToList();
                cachedOsuFilePath = osuFilePath;
            }

            sections.AddRange(cachedBreakPeriods);

            // Adjust deafen points to avoid overlap with break points
            foreach (var breakPeriod in sections)
                if (breakPeriod.Fill is SolidColorPaint paint &&
                    paint.Color == new SKColor(0xFF, 0xFF, 0x00, 64) &&
                    deafenRectangle.Xi < breakPeriod.Xj && deafenRectangle.Xj > breakPeriod.Xi)
                    deafenRectangle.Xi = breakPeriod.Xj;

            PlotView.Sections = sections;

            // Only add progressIndicator if not already present
            if (!seriesList.Contains(progressIndicator))
                seriesList.Add(progressIndicator);

            Series = seriesList.ToArray();
            PlotView.Series = seriesList.ToArray();

            // Always use the current data's length for MaxLimit so the right side is never empty
            XAxes = new[]
            {
                new Axis
                {
                    LabelsPaint = new SolidColorPaint(SKColors.Transparent),
                    MinLimit = 0,
                    MaxLimit = maxLimit,
                    Padding = new Padding(2),
                    TextSize = 12
                }
            };
            YAxes = new[]
            {
                new Axis
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

    private List<ObservablePoint> Downsample(List<ObservablePoint> data, int maxPoints)
    {
        if (data.Count <= maxPoints) return data;
        var result = new List<ObservablePoint>(maxPoints);
        var step = (double)data.Count / maxPoints;
        for (var i = 0; i < maxPoints; i++)
        {
            var idx = (int)(i * step);
            result.Add(data[idx]);
        }

        return result;
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
                return;

            if (_tosuApi.GetRawBanchoStatus() == 2)
            {
                ViewModel.StatusMessage = "Progress Indicator not updating while in game.";
                return;
            }

            ViewModel.StatusMessage = "";

            if (Math.Abs(completionPercentage - _lastCompletionPercentage) < 0.1) return;
            _lastCompletionPercentage = completionPercentage;

            var xAxis = XAxes[0];
            var maxXLimit = xAxis.MaxLimit;
            if (!maxXLimit.HasValue) return;

            var progressPosition = completionPercentage / 100 * maxXLimit.Value;
            var leftEdgePosition = Math.Max(progressPosition - 0.1, 0);

            // Get relevant series and cache sorted points
            var lineSeriesList = Series.OfType<LineSeries<ObservablePoint>>()
                .Where(s => s.Name == "Aim" || s.Name == "Speed")
                .ToArray();

            if (lineSeriesList.Length == 0) return;

            var sortedPointsCache =
                new Dictionary<LineSeries<ObservablePoint>, List<ObservablePoint>>(lineSeriesList.Length);
            foreach (var series in lineSeriesList)
            {
                var values = series.Values as List<ObservablePoint> ?? series.Values.ToList();
                if (values.Count > 1 && !IsSortedByX(values))
                    values.Sort((a, b) => Nullable.Compare(a.X, b.X));
                sortedPointsCache[series] = values;
            }

            const int steps = 8;
            var step = (progressPosition - leftEdgePosition) / steps;
            if (step <= 0) step = 0.1;

            // Reuse the list if possible
            var topContourPoints = progressIndicator.Values as List<ObservablePoint>;
            if (topContourPoints == null)
                topContourPoints = new List<ObservablePoint>(steps + 4);
            else
                topContourPoints.Clear();

            topContourPoints.Add(new ObservablePoint(leftEdgePosition, 0));

            for (var i = 0; i <= steps; i++)
            {
                var x = leftEdgePosition + i * step;
                if (x > progressPosition) x = progressPosition;

                double maxInterpolatedY = 0;
                foreach (var series in lineSeriesList)
                {
                    var points = sortedPointsCache[series];
                    if (points.Count == 0) continue;

                    var leftIndex = BinarySearchX(points, x);
                    var leftPoint = points[Math.Max(leftIndex, 0)];
                    var rightPoint = points[Math.Min(leftIndex + 1, points.Count - 1)];

                    var interpolatedY = InterpolateY(leftPoint, rightPoint, x);
                    if (interpolatedY > maxInterpolatedY)
                        maxInterpolatedY = interpolatedY;
                }

                topContourPoints.Add(new ObservablePoint(x, maxInterpolatedY));
            }

            // Right edge Y
            double rightEdgeY = 0;
            foreach (var series in lineSeriesList)
            {
                var points = sortedPointsCache[series];
                if (points.Count == 0) continue;

                var leftIndex = BinarySearchX(points, progressPosition);
                var leftPoint = points[Math.Max(leftIndex, 0)];
                var rightPoint = points[Math.Min(leftIndex + 1, points.Count - 1)];

                var interpolatedY = InterpolateY(leftPoint, rightPoint, progressPosition);
                if (interpolatedY > rightEdgeY)
                    rightEdgeY = interpolatedY;
            }

            topContourPoints.Add(new ObservablePoint(progressPosition, rightEdgeY));
            topContourPoints.Add(new ObservablePoint(progressPosition, 0));
            topContourPoints.Add(new ObservablePoint(leftEdgePosition, 0));

            progressIndicator.Values = topContourPoints;

            if (!Series.Contains(progressIndicator))
                Series = Series.Append(progressIndicator).ToArray();

            PlotView.InvalidateVisual();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"An error occurred while updating the progress indicator: {ex.Message}");
        }

        // Helper: binary search for X in sorted list
        static int BinarySearchX(List<ObservablePoint> points, double x)
        {
            int lo = 0, hi = points.Count - 1;
            while (lo <= hi)
            {
                var mid = lo + ((hi - lo) >> 1);
                if (points[mid].X < x) lo = mid + 1;
                else if (points[mid].X > x) hi = mid - 1;
                else return mid;
            }

            return lo - 1;
        }

        // Helper: check if list is sorted by X
        static bool IsSortedByX(List<ObservablePoint> points)
        {
            for (var i = 1; i < points.Count; i++)
                if (points[i - 1].X > points[i].X)
                    return false;
            return true;
        }
    }

    private double InterpolateY(ObservablePoint leftPoint, ObservablePoint rightPoint, double x)
    {
        var lx = leftPoint.X;
        var rx = rightPoint.X;
        var ly = leftPoint.Y ?? 0.0;
        var ry = rightPoint.Y ?? 0.0;

        if (lx == rx)
            return ly;

        return (double)(ly + (ry - ly) * (x - lx) / (rx - lx));
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
        _tosuApi.CheckForBeatmapChange();
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
        var button = this.FindControl<Button>("CheckForUpdatesButton");
        if (button == null) return;

        button.Content = "Checking for updates...";
        await Task.Delay(2000);

        await _updateChecker.FetchLatestVersionAsync();

        if (string.IsNullOrEmpty(_updateChecker.latestVersion))
        {
            button.Content = "No updates found";
            await Task.Delay(2000);
            button.Content = "Check for updates";
            return;
        }

        var currentVersion = new Version(UpdateChecker.currentVersion);
        var latestVersion = new Version(_updateChecker.latestVersion);

        if (latestVersion > currentVersion)
        {
            ShowUpdateNotification();
            button.Content = "Update available!";
            await Task.Delay(2000);
            button.Content = "Check for updates";
        }
        else
        {
            button.Content = "You are on the latest version";
            await Task.Delay(2000);
            button.Content = "Check for updates";
        }
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
        try
        {
            if (ViewModel == null)
            {
                Console.WriteLine("[ERROR] ViewModel is null.");
                return;
            }

            if (_tosuApi == null)
            {
                Console.WriteLine("[ERROR] _tosuApi is null.");
                return;
            }

            // Disable background if ViewModel indicates
            if (!ViewModel.IsBackgroundEnabled)
            {
                if (_blurredBackground != null) _blurredBackground.IsVisible = false;
                if (_normalBackground != null) _normalBackground.IsVisible = false;
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

                if (_currentBitmap != null)
                    try
                    {
                        _currentBitmap.Dispose();
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine("[WARN] Failed to dispose bitmap: " + ex);
                    }

                _currentBitmap = newBitmap;

                var bitmapForUI = _currentBitmap;
                await Dispatcher.UIThread.InvokeAsync(() => UpdateUIWithNewBackgroundAsync(bitmapForUI));
                await Dispatcher.UIThread.InvokeAsync(UpdateLogoAsync);
                IsBlackBackgroundDisplayed = false;
            }

            // Remove previous handler if exists
            if (_backgroundPropertyChangedHandler != null)
                ViewModel.PropertyChanged -= _backgroundPropertyChangedHandler;

            _backgroundPropertyChangedHandler = async void (s, args) =>
            {
                try
                {
                    if (args.PropertyName == nameof(ViewModel.IsParallaxEnabled) ||
                        args.PropertyName == nameof(ViewModel.IsBlurEffectEnabled))
                    {
                        var bitmapForUI = _currentBitmap;
                        await Dispatcher.UIThread.InvokeAsync(() => UpdateUIWithNewBackgroundAsync(bitmapForUI));
                    }

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
                            var bitmapForUI = _currentBitmap;
                            await Dispatcher.UIThread.InvokeAsync(() => UpdateUIWithNewBackgroundAsync(bitmapForUI));
                            IsBlackBackgroundDisplayed = false;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine("[ERROR] Exception in background property changed handler: " + ex);
                }
            };

            ViewModel.PropertyChanged += _backgroundPropertyChangedHandler;

            UpdateBackgroundVisibility();
        }
        catch (Exception ex)
        {
            Console.WriteLine("[ERROR] Exception in UpdateBackground: " + ex);
        }
    }

    private async Task<Bitmap?> LoadBitmapAsync(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            Console.WriteLine("LoadBitmapAsync: Provided path is null or empty.");
            return null;
        }

        return await Task.Run(() =>
        {
            if (!File.Exists(path))
            {
                Console.WriteLine($"Background file not found: {path}");
                return null;
            }

            try
            {
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    return new Bitmap(stream);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to load bitmap from {path}: {ex.Message}");
                return null;
            }
        });
    }

    private async Task AnimateBlurAsync(BlurEffect blurEffect, double from, double to, int durationMs,
        CancellationToken token)
    {
        try
        {
            const int steps = 30;
            var step = (to - from) / steps;
            var delay = durationMs / steps;

            for (var i = 0; i <= steps; i++)
            {
                token.ThrowIfCancellationRequested();
                blurEffect.Radius = from + step * i;
                await Task.Delay(delay, token);
            }

            blurEffect.Radius = to;
        }
        catch (TaskCanceledException)
        {
            // Expected, do not log
        }
    }

    private async Task UpdateUIWithNewBackgroundAsync(Bitmap? bitmap)
    {
        // Use last valid bitmap if null
        if (bitmap == null)
        {
            Console.WriteLine("[WARN] Bitmap is null, using last valid bitmap.");
            bitmap = _lastValidBitmap;
            if (bitmap == null)
            {
                Console.WriteLine("[ERROR] No valid bitmap available, cannot update UI.");
                return;
            }
        }
        else
        {
            _lastValidBitmap = bitmap;
        }

        try
        {
            var _ = bitmap.Size;
        }
        catch (Exception ex)
        {
            Console.WriteLine("[ERROR] Bitmap is invalid or disposed: " + ex);
            return;
        }

        CancellationTokenSource cts;
        lock (_updateLock)
        {
            _cancellationTokenSource?.Cancel();
            _cancellationTokenSource = new CancellationTokenSource();
            cts = _cancellationTokenSource;
        }

        var token = cts.Token;

        async Task UpdateUI()
        {
            if (token.IsCancellationRequested) return;

            try
            {
                // Ensure Content is a Grid
                if (Content is not Grid mainGrid)
                {
                    mainGrid = new Grid();
                    Content = mainGrid;
                }

                // Calculate bounds
                var bounds = mainGrid.Bounds;
                var width = Math.Max(1, bounds.Width);
                var height = Math.Max(1, bounds.Height);

                // Reuse or create blur effect
                if (_backgroundBlurEffect == null)
                    _backgroundBlurEffect = new BlurEffect();

                // Set initial radius to current value (or 0 if first time)
                var currentRadius = _backgroundBlurEffect.Radius;
                var targetRadius = ViewModel?.IsBlurEffectEnabled == true ? 17.27 : 0;

                var gpuBackground = new GpuBackgroundControl
                {
                    Bitmap = bitmap,
                    Opacity = 0.5,
                    ZIndex = -1,
                    Stretch = Stretch.UniformToFill,
                    Effect = _backgroundBlurEffect,
                    Clip = new RectangleGeometry(new Rect(0, 0, width * 1.05, height * 1.05)),
                    Transitions = new Transitions
                    {
                        new DoubleTransition
                        {
                            Property = OpacityProperty,
                            Duration = TimeSpan.FromSeconds(0.3),
                            Easing = new QuarticEaseInOut()
                        }
                    }
                };

                // Find or create background layer
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

                backgroundLayer.RenderTransform = new ScaleTransform(1.05, 1.05);

                // Remove old background with fade
                var oldBackground = backgroundLayer.Children.OfType<Control>().FirstOrDefault();
                if (oldBackground != null)
                {
                    oldBackground.Opacity = 0.0;
                    await Task.Delay(250, token).ContinueWith(_ => { }, TaskContinuationOptions.OnlyOnRanToCompletion);
                    if (!token.IsCancellationRequested)
                        backgroundLayer.Children.Remove(oldBackground);
                }

                if (token.IsCancellationRequested) return;

                backgroundLayer.Children.Add(gpuBackground);
                backgroundLayer.Opacity = _currentBackgroundOpacity;

                // Parallax first, then animate blur for smoothness
                if (ViewModel?.IsParallaxEnabled == true &&
                    this.FindControl<CheckBox>("ParallaxToggle")?.IsChecked == true &&
                    this.FindControl<CheckBox>("BackgroundToggle")?.IsChecked == true)
                    try
                    {
                        ApplyParallax(_mouseX, _mouseY);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine("[WARN] Parallax failed: " + ex);
                    }

                await AnimateBlurAsync(_backgroundBlurEffect, currentRadius, targetRadius, 100, token);
            }
            catch (TaskCanceledException)
            {
                // Expected, do not log
            }
            catch (Exception ex)
            {
                Console.WriteLine("[ERROR] Error updating UI: " + ex);
            }
        }

        try
        {
            if (Dispatcher.UIThread.CheckAccess())
                await UpdateUI();
            else
                await Dispatcher.UIThread.InvokeAsync(UpdateUI);
        }
        catch (TaskCanceledException)
        {
            // Expected, do not log
        }
        catch (Exception ex)
        {
            Console.WriteLine("[ERROR] Exception dispatching UpdateUI: " + ex);
        }
    }

    private void UpdateBackgroundVisibility()
    {
        if (_blurredBackground != null && _normalBackground != null)
        {
            _blurredBackground.IsVisible = ViewModel.IsBlurEffectEnabled;
            _normalBackground.IsVisible = !ViewModel.IsBlurEffectEnabled;
        }
    }

    private SKBitmap? ConvertToSKBitmap(Bitmap? avaloniaBitmap)
    {
        if (avaloniaBitmap == null)
            return null;

        var width = avaloniaBitmap.PixelSize.Width;
        var height = avaloniaBitmap.PixelSize.Height;

        if (width <= 0 || height <= 0)
            return null;

        SKBitmap? skBitmap = null;
        var pixelDataPtr = IntPtr.Zero;

        try
        {
            skBitmap = new SKBitmap(width, height);

            using (var renderTargetBitmap = new RenderTargetBitmap(new PixelSize(width, height), new Vector(96, 96)))
            {
                using (var drawingContext = renderTargetBitmap.CreateDrawingContext())
                {
                    drawingContext.DrawImage(avaloniaBitmap, new Rect(0, 0, width, height),
                        new Rect(0, 0, width, height));
                }

                var pixelDataSize = width * height * 4;
                pixelDataPtr = Marshal.AllocHGlobal(pixelDataSize);

                var rect = new PixelRect(0, 0, width, height);
                renderTargetBitmap.CopyPixels(rect, pixelDataPtr, pixelDataSize, width * 4);

                var pixelData = new byte[pixelDataSize];
                Marshal.Copy(pixelDataPtr, pixelData, 0, pixelDataSize);

                var destPtr = skBitmap.GetPixels();
                Marshal.Copy(pixelData, 0, destPtr, pixelDataSize);
            }

            return skBitmap;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"ConvertToSKBitmap failed: {ex.Message}");
            skBitmap?.Dispose();
            return null;
        }
        finally
        {
            if (pixelDataPtr != IntPtr.Zero)
                Marshal.FreeHGlobal(pixelDataPtr);
        }
    }

    private SKColor CalculateAverageColor(SKBitmap bitmap)
    {
        if (bitmap == null) throw new ArgumentNullException(nameof(bitmap), "Bitmap cannot be null");

        var width = bitmap.Width;
        var height = bitmap.Height;
        if (width == 0 || height == 0) throw new ArgumentException("Bitmap dimensions cannot be zero", nameof(bitmap));

        long totalR = 0, totalG = 0, totalB = 0;
        var pixelCount = (long)width * height;

        var pixels = bitmap.Pixels;
        if (pixels == null || pixels.Length != pixelCount)
            throw new InvalidOperationException("Bitmap pixel data is not accessible or corrupted.");

        Parallel.For(0, pixels.Length, () => (0L, 0L, 0L), (i, state, local) =>
            {
                var pixel = pixels[i];
                local.Item1 += pixel.Red;
                local.Item2 += pixel.Green;
                local.Item3 += pixel.Blue;
                return local;
            },
            local =>
            {
                Interlocked.Add(ref totalR, local.Item1);
                Interlocked.Add(ref totalG, local.Item2);
                Interlocked.Add(ref totalB, local.Item3);
            });

        var avgR = (byte)Math.Clamp(totalR / pixelCount, 0, 255);
        var avgG = (byte)Math.Clamp(totalG / pixelCount, 0, 255);
        var avgB = (byte)Math.Clamp(totalB / pixelCount, 0, 255);

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
        const string resourceName = "osuautodeafen.Resources.autodeafen.svg";
        try
        {
            var svg = await Task.Run(() => LoadHighResolutionLogo(resourceName));

            if (_logoControl == null)
                _logoControl = new LogoControl
                {
                    Width = 240,
                    Height = 72,
                    VerticalAlignment = VerticalAlignment.Center
                };
            _logoControl.Svg = svg;
            _logoControl.ModulateColor = SKColors.White;

            var logoHost = this.FindControl<ContentControl>("LogoHost");
            if (logoHost != null)
                logoHost.Content = _logoControl;

            Console.WriteLine("SVG loaded successfully.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Exception while loading logo image: {ex.Message}");
            try
            {
                await RetryLoadLogoAsync(resourceName);
            }
            catch (Exception retryEx)
            {
                Console.WriteLine($"RetryLoadLogoAsync failed: {retryEx.Message}");
            }
        }
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
        if (maxAttempts <= 0) throw new ArgumentOutOfRangeException(nameof(maxAttempts));
        if (delayMilliseconds < 0) throw new ArgumentOutOfRangeException(nameof(delayMilliseconds));

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                var lowResBitmapPath = _getLowResBackground?.GetLowResBitmapPath();
                if (!string.IsNullOrEmpty(lowResBitmapPath))
                    return lowResBitmapPath;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[WARN] Exception on attempt {attempt}: {ex.Message}");
            }

            Console.WriteLine($"Attempt {attempt} failed. Retrying in {delayMilliseconds}ms...");
            await Task.Delay(delayMilliseconds).ConfigureAwait(false);
        }

        Console.WriteLine("[ERROR] Failed to get low resolution bitmap path after multiple attempts.");
        return null;
    }

    private Bitmap ConvertSvgToBitmap(SKSvg svg, int width, int height)
    {
        if (svg == null)
            throw new ArgumentNullException(nameof(svg));
        if (svg.Picture == null)
            throw new InvalidOperationException("SVG does not contain a valid picture.");

        var info = new SKImageInfo(width, height);

        try
        {
            using var surface = SKSurface.Create(info);
            if (surface == null)
                throw new InvalidOperationException("Failed to create SKSurface.");

            var canvas = surface.Canvas;
            canvas.Clear(SKColors.Transparent);
            canvas.DrawPicture(svg.Picture);

            using var image = surface.Snapshot();
            using var data = image.Encode();
            using var stream = new MemoryStream(data.ToArray());
            return new Bitmap(stream);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERROR] ConvertSvgToBitmap failed: {ex.Message}");
            throw;
        }
    }

    public async Task UpdateAverageColorAsync(SKColor newColor)
    {
        _colorTransitionCts?.Cancel();
        _colorTransitionCts = new CancellationTokenSource();
        var token = _colorTransitionCts.Token;

        var steps = 20;
        var delay = 10; // ms

        var from = _oldAverageColorPublic;
        var to = newColor;

        try
        {
            for (var i = 0; i <= steps; i++)
            {
                token.ThrowIfCancellationRequested();
                var t = i / (float)steps;
                var interpolated = InterpolateColor(from, to, t);
                var avaloniaColor = Color.FromArgb(interpolated.Alpha, interpolated.Red, interpolated.Green,
                    interpolated.Blue);

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    ViewModel.AverageColorBrush = new SolidColorBrush(avaloniaColor);
                });

                await Task.Delay(delay, token);
            }
        }
        catch (OperationCanceledException)
        {
            // Swallow the exception, as cancellation is expected
        }

        _oldAverageColorPublic = newColor;
    }

    private async Task UpdateLogoAsync()
    {
        if (_getLowResBackground == null)
        {
            Console.WriteLine("[ERROR] _getLowResBackground is null");
            return;
        }

        try
        {
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
            if (string.IsNullOrWhiteSpace(lowResBitmapPath) || !File.Exists(lowResBitmapPath))
            {
                Console.WriteLine("[ERROR] Low-resolution bitmap path is invalid or does not exist");
                return;
            }

            Bitmap? lowResBitmap = null;
            try
            {
                using var stream =
                    new FileStream(lowResBitmapPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                lowResBitmap = new Bitmap(stream);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] Failed to load low-resolution bitmap: {ex.Message}");
                return;
            }

            if (lowResBitmap == null)
            {
                Console.WriteLine("[ERROR] Low-resolution bitmap is null after loading");
                return;
            }

            _lowResBitmap = lowResBitmap;
            Console.WriteLine("Low resolution bitmap successfully loaded");

            var highResLogoSvg = await highResLogoTask;
            if (highResLogoSvg == null || highResLogoSvg.Picture == null)
            {
                Console.WriteLine("[ERROR] Failed to load high-resolution logo or picture is null");
                return;
            }

            _cachedLogoSvg = highResLogoSvg;

            var skBitmap = ConvertToSKBitmap(_lowResBitmap);
            var newAverageColor = SKColors.White;
            if (skBitmap != null)
                try
                {
                    newAverageColor = await CalculateAverageColorAsync(skBitmap);
                    await UpdateAverageColorAsync(newAverageColor);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[ERROR] Failed to calculate average color: {ex.Message}");
                }
                finally
                {
                    skBitmap.Dispose();
                }

            if (_oldAverageColor == newAverageColor)
                return;

            const int steps = 10;
            const int delay = 16;

            await _animationManager.EnqueueAnimation(async () =>
            {
                if (_cachedLogoSvg?.Picture == null)
                {
                    Console.WriteLine("[ERROR] Cached logo SVG or its picture is null");
                    return;
                }

                for (var i = 0; i <= steps; i++)
                {
                    var t = i / (float)steps;
                    var interpolatedColor = InterpolateColor(_oldAverageColor, newAverageColor, t);

                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        if (_logoControl is { } skiaLogo)
                        {
                            skiaLogo.Svg = _cachedLogoSvg;
                            skiaLogo.ModulateColor = interpolatedColor;
                            skiaLogo.InvalidateVisual();
                        }

                        if (DataContext is SharedViewModel viewModel) viewModel.ModifiedLogoImage = null;
                    });

                    await Task.Delay(delay);
                }

                _oldAverageColor = newAverageColor;
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERROR] Exception in UpdateLogoAsync: {ex}");
        }
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
        _ = UpdateUIWithNewBackgroundAsync(blackBitmap);
        IsBlackBackgroundDisplayed = true;
        return Task.CompletedTask;
    }

    private void ApplyParallax(double mouseX, double mouseY)
    {
        if (_currentBitmap == null || ParallaxToggle.IsChecked == false || BackgroundToggle.IsChecked == false) return;
        if (mouseX < 0 || mouseY < 0 || mouseX > Width || mouseY > Height) return;

        var windowWidth = Width;
        var windowHeight = Height;
        var centerX = windowWidth / 2;
        var centerY = windowHeight / 2;

        var relativeMouseX = mouseX - centerX;
        var relativeMouseY = mouseY - centerY;

        var scaleFactor = 0.015;
        var movementX = -(relativeMouseX * scaleFactor);
        var movementY = -(relativeMouseY * scaleFactor);

        double maxMovement = 15;
        movementX = Math.Max(-maxMovement, Math.Min(maxMovement, movementX));
        movementY = Math.Max(-maxMovement, Math.Min(maxMovement, movementY));

        if (Content is Grid mainGrid)
        {
            var backgroundLayer = mainGrid.Children.OfType<Grid>().FirstOrDefault(g => g.Name == "BackgroundLayer");
            if (backgroundLayer != null && backgroundLayer.Children.Count > 0)
            {
                var gpuBackground = backgroundLayer.Children.OfType<GpuBackgroundControl>().FirstOrDefault();
                if (gpuBackground != null) gpuBackground.RenderTransform = new TranslateTransform(movementX, movementY);
            }
        }
    }

    private void OnMouseMove(object? sender, PointerEventArgs e)
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

    private DispatcherTimer? _cogSpinTimer;
    private double _cogCurrentAngle = 0;

    private async void SettingsButton_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            var settingsPanel = this.FindControl<DockPanel>("SettingsPanel");
            var buttonContainer = this.FindControl<Border>("SettingsButtonContainer");
            var cogImage = this.FindControl<Image>("SettingsCogImage");
            if (settingsPanel == null || buttonContainer == null || cogImage == null) return;

            var showMargin = new Thickness(0, 42, 0, 0);
            var hideMargin = new Thickness(200, 42, -200, 0);

            settingsPanel.Transitions = new Transitions
            {
                new ThicknessTransition
                {
                    Property = DockPanel.MarginProperty,
                    Duration = TimeSpan.FromMilliseconds(250),
                    Easing = new QuarticEaseInOut()
                }
            };

            buttonContainer.Transitions = new Transitions
            {
                new ThicknessTransition
                {
                    Property = Border.MarginProperty,
                    Duration = TimeSpan.FromMilliseconds(250),
                    Easing = new QuarticEaseInOut()
                }
            };

            var buttonRightMargin = new Thickness(0, 42, 0, 10);
            var buttonLeftMargin = new Thickness(0, 42, 200, 10);
            
            Task EnsureCogCenterAsync()
            {

                // Set the rotation origin to the center
                cogImage.RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative);

                if (cogImage.RenderTransform is not RotateTransform)
                    cogImage.RenderTransform = new RotateTransform(0);
                return Task.CompletedTask;
            }

            if (!settingsPanel.IsVisible)
            {
                settingsPanel.Margin = hideMargin;
                buttonContainer.Margin = buttonRightMargin;
                settingsPanel.IsVisible = true;
                await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Render);

                settingsPanel.Margin = showMargin;
                buttonContainer.Margin = buttonLeftMargin;

                await EnsureCogCenterAsync();

                var rotate = (RotateTransform)cogImage.RenderTransform!;
                _cogSpinTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
                _cogSpinTimer.Tick += (s, ev) =>
                {
                    _cogCurrentAngle = (_cogCurrentAngle + 4) % 360;
                    rotate.Angle = _cogCurrentAngle;
                };
                _cogSpinTimer.Start();
            }
            else
            {
                settingsPanel.Margin = hideMargin;
                buttonContainer.Margin = buttonRightMargin;
                await Task.Delay(250);
                settingsPanel.IsVisible = false;

                _cogSpinTimer?.Stop();
                if (cogImage.RenderTransform is RotateTransform rotate)
                {
                    double start = _cogCurrentAngle;
                    double end = 0;
                    int duration = 250;
                    int steps = 20;
                    double step = (end - start) / steps;
                    for (int i = 1; i <= steps; i++)
                    {
                        await Task.Delay(duration / steps);
                        rotate.Angle = start + step * i;
                    }
                    rotate.Angle = 0;
                    _cogCurrentAngle = 0;
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERROR] Exception in SettingsButton_Click: {ex.Message}");
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

    public class HotKey
    {
        public Key Key { get; init; }
        public KeyModifiers ModifierKeys { get; init; }
        public string FriendlyName { get; init; }

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
    }
}