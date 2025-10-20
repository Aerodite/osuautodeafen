using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Threading;
using LiveChartsCore;
using LiveChartsCore.Defaults;
using LiveChartsCore.Drawing;
using LiveChartsCore.Measure;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Avalonia;
using LiveChartsCore.SkiaSharpView.Painting;
using osuautodeafen.cs.StrainGraph.Sections;
using osuautodeafen.cs.Tooltips;
using osuautodeafen.cs.Tosu;
using SkiaSharp;
// ReSharper disable CompareOfFloatsByEqualityOperator

namespace osuautodeafen.cs.StrainGraph;

public class ChartManager
{
    private static readonly SKColor ProgressIndicatorColor = new(0xFF, 0xFF, 0xFF, 192);
    private static readonly SKColor AimColor = new(0x00, 0xFF, 0x00, 192);
    private static readonly SKColor SpeedColor = new(0x00, 0x00, 0xFF, 140);
    private static readonly SKColor BreakColor = new(0xFF, 0xFF, 0x00, 90);
    private static readonly SKColor KiaiColor = new(0xA0, 0x40, 0xFF, 98);
    private static readonly SKColor DeafenOverlayColor = new(0xFF, 0x00, 0x00, 64);

    private readonly BreakPeriodCalculator _breakPeriod = new();
    private readonly List<AnnotatedSection> _cachedBreakPeriods = [];
    private readonly List<AnnotatedSection> _cachedKiaiPeriods = [];

    private readonly KiaiTimes _kiaiTimes;

    private readonly LineSeries<ObservablePoint> _progressIndicator;
    private readonly SectionManager _sectionManager = new();
    private readonly TosuApi _tosuApi;
    private readonly SharedViewModel _viewModel;

    private CancellationTokenSource? _deafenOverlayCts;
    private RectangularSection? _draggedDeafenSection;
    private bool _isDraggingDeafenEdge;
    
    private readonly TooltipManager _tooltipManager;

    private bool _isHoveringDeafenEdge;
    private List<BreakPeriod>? _lastBreaks;
    private double _lastDeafenOverlayValue = -1;
    private GraphData? _lastGraphData;
    private double _lastMinCompletionPercentage = -1;
    private string? _lastOsuFilePath;
    private List<double>? _lastSeriesData;

    private AnnotatedSection? _lastTooltipSection;
    private string? _lastTooltipText;
    private List<double>? _lastXAxis;
    private List<double>? _currentXAxis;

    public ChartManager(CartesianChart plotView, TosuApi tosuApi, SharedViewModel viewModel, KiaiTimes kiaiTimes,
        TooltipManager tooltipManager)
    {
        PlotView = plotView ?? throw new ArgumentNullException(nameof(plotView));
        _tosuApi = tosuApi ?? throw new ArgumentNullException(nameof(tosuApi));
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        _kiaiTimes = kiaiTimes ?? throw new ArgumentNullException(nameof(kiaiTimes));
        _tooltipManager = tooltipManager ?? throw new ArgumentNullException(nameof(tooltipManager));
        
        _progressIndicator = new LineSeries<ObservablePoint>
        {
            Stroke = new SolidColorPaint { Color = ProgressIndicatorColor, StrokeThickness = 5 },
            GeometryFill = null,
            GeometryStroke = null,
            LineSmoothness = 0,
            Values = [],
            Name = "ProgressIndicator"
        };
        // for some reason we need this to initialize break color correctly -_-
        ViewModel_PropertyChanged(this, new PropertyChangedEventArgs(nameof(_viewModel.IsBreakUndeafenToggleEnabled)));
        SetupPlotViewEvents();
        InitializeChart();
    }

    private bool AudibleBreaksEnabled { get; set; }
    public ISeries[]? Series { get; private set; }
    public Axis[] XAxes { get; private set; } = Array.Empty<Axis>();
    public Axis[] YAxes { get; private set; } = Array.Empty<Axis>();
    private double MaxYValue { get; set; }

    private double MaxLimit { get; set; }

    private CartesianChart PlotView { get; }

    /// <summary>
    ///     Initializes the chart with default settings and series.
    /// </summary>
    private void InitializeChart()
    {
        Series =
        [
            new StackedAreaSeries<ObservablePoint>
            {
                Values = ChartData.Series1Values,
                Fill = new SolidColorPaint { Color = new SKColor(0xFF, 0x00, 0x00) },
                Stroke = new SolidColorPaint { Color = new SKColor(0xFF, 0x00, 0x00) },
                Name = "Aim"
            },
            new StackedAreaSeries<ObservablePoint>
            {
                Values = ChartData.Series2Values,
                Fill = new SolidColorPaint { Color = new SKColor(0x00, 0xFF, 0x00) },
                Stroke = new SolidColorPaint { Color = new SKColor(0x00, 0xFF, 0x00) },
                Name = "Speed"
            },
            _progressIndicator
        ];
        MaxYValue = 1;
        MaxLimit = 1;
        PlotView.Series = Series;
        PlotView.DrawMargin = new Margin(0, 0, 0, 0);
        UpdateAxes();
        var currentSections = PlotView.Sections.ToList();
        PlotView.Sections = currentSections;
        AddDeafenOverlaySection(PlotView.Sections.OfType<AnnotatedSection>().ToList(),
            _viewModel.MinCompletionPercentage);
        PlotView.InvalidateVisual();
    }

    /// <summary>
    ///     Updates the chart axes based on current data ranges.
    /// </summary>
    private void SetupPlotViewEvents()
    {
        PlotView.PointerPressed += PlotView_PointerPressed;
        PlotView.PointerReleased += PlotView_PointerReleased;
        PlotView.PointerExited += (_, _) => PlotView_PointerExited(_tooltipManager);

        _viewModel.PropertyChanged += ViewModel_PropertyChanged;
    }

    /// <summary>
    ///     Grabs the PropertyChanged event from the ViewModel to update break colors when the setting changes.
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="e"></param>
    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(_viewModel.IsBreakUndeafenToggleEnabled))
        {
            AudibleBreaksEnabled = _viewModel.IsBreakUndeafenToggleEnabled;
            foreach (CoreSection viewSection in PlotView.Sections)
                if (viewSection is AnnotatedSection section && section.SectionType == "Break")
                    _sectionManager.AnimateSectionFill(
                        section,
                        AudibleBreaksEnabled ? BreakColor : SKColors.LightSkyBlue,
                        AudibleBreaksEnabled ? SKColors.LightSkyBlue : BreakColor,
                        AudibleBreaksEnabled, 0, 0,
                        (_, fill) => section.Fill = fill,
                        PlotView.InvalidateVisual
                    );
        }
    }

    /// <summary>
    /// Tries to show a tooltip on the given poistion
    /// </summary>
    /// <param name="dataPoint"></param>
    /// <param name="pixelPoint"></param>
    /// <param name="tooltipManager"></param>
    /// <returns></returns>
    public void TryShowTooltip(LvcPointD dataPoint, Point pixelPoint, TooltipManager tooltipManager)
    {
        if (_isDraggingDeafenEdge && _draggedDeafenSection != null)
        {
            double newXi = Math.Max(0, Math.Min(dataPoint.X, Math.Min((_draggedDeafenSection.Xj ?? 0) - 1, MaxLimit)));
            _draggedDeafenSection.Xi = newXi;
            double newPercentage = Math.Min(100.0, 100.0 * newXi / MaxLimit);
            _viewModel.MinCompletionPercentage = (int)newPercentage;

            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop &&
                desktop.MainWindow is MainWindow { CompletionPercentageSlider: not null } mainWindow)
            {
                mainWindow.CompletionPercentageSlider.Value = newPercentage;
                mainWindow.CompletionPercentageSlider_ValueChanged(null, new RangeBaseValueChangedEventArgs(newPercentage, newPercentage, null));
            }

            tooltipManager.ShowTooltip(pixelPoint, $"Deafen Min %:\n{newPercentage:F2}%");
            PlotView.InvalidateVisual();
            _tooltipManager.CurrentTooltipType = Tooltips.Tooltips.TooltipType.Deafen;
            return;
        }

        double? currentTime = null;
        if (PlotView.Bounds.Width > 0 && _currentXAxis != null)
        {
            LvcPointD mapped = PlotView.ScalePixelsToData(new LvcPointD(pixelPoint.X, pixelPoint.Y));
            int index = (int)Math.Round(mapped.X);
            index = Math.Max(0, Math.Min(index, _currentXAxis.Count - 1));
            currentTime = _currentXAxis.ElementAtOrDefault(index);
        }

        AnnotatedSection? activeSection = null;
        string? tooltipText = null;
        foreach (AnnotatedSection section in PlotView.Sections.OfType<AnnotatedSection>())
        {
            if (!section.Tooltip || section.Xi == null || section.Xj == null)
                continue;
            if (dataPoint.X >= section.Xi && dataPoint.X <= section.Xj)
            {
                activeSection = section;
                tooltipText = FormatSectionTooltip(section, currentTime);
                break;
            }
        }

        if (activeSection != null && tooltipText != null)
        {
            tooltipManager.ShowTooltip(pixelPoint, tooltipText);
            _lastTooltipSection = activeSection;
            _lastTooltipText = tooltipText;
            _tooltipManager.CurrentTooltipType = Tooltips.Tooltips.TooltipType.Section;
            return;
        }

        if (currentTime.HasValue)
        {
            TimeSpan ts = TimeSpan.FromMilliseconds(currentTime.Value);
            string timeText = $"{ts.Minutes:D2}:{ts.Seconds:D2}";
            tooltipManager.ShowTooltip(pixelPoint, timeText);
            _lastTooltipSection = null;
            _lastTooltipText = null;
            _tooltipManager.CurrentTooltipType = Tooltips.Tooltips.TooltipType.Time;
            return;
        }

        _lastTooltipSection = null;
        _lastTooltipText = null;
        _tooltipManager.CurrentTooltipType = Tooltips.Tooltips.TooltipType.None;
    }


    /// <summary>
    ///     Formats the tooltip text for a given section.
    /// </summary>
    /// <param name="section"></param>
    /// <param name="currentTimeMs"></param>
    /// <returns></returns>
    private string FormatSectionTooltip(AnnotatedSection section, double? currentTimeMs = null)
    {
        // Compact adaptive format: minutes:seconds with tenths of a second
        string start = TimeSpan.FromMilliseconds(section.StartTime).ToString(@"mm\:ss\:ff");
        string end = TimeSpan.FromMilliseconds(section.EndTime).ToString(@"mm\:ss\:ff");

        string? current = currentTimeMs.HasValue
            ? TimeSpan.FromMilliseconds(currentTimeMs.Value).ToString(@"mm\:ss")
            : null;

        string? sectionText = section.SectionType == "Break"
            ? $"Break ({(AudibleBreaksEnabled ? "Undeafened" : "Deafened")})"
            : section.SectionType;

        if (current != null)
            return $"{sectionText} ({current})\n{start}-{end}";

        return $"{sectionText}\n{start}-{end}";
    }


    /// <summary>
    ///     Animates the stroke thickness of a rectangular section over a specified duration (used for deafen section)
    /// </summary>
    /// <param name="rs"></param>
    /// <param name="from"></param>
    /// <param name="to"></param>
    /// <param name="durationMs"></param>
    private async Task AnimateStrokeThickness(RectangularSection rs, float from, float to, int durationMs)
    {
        int steps = 6;
        for (int i = 0; i <= steps; i++)
        {
            float thickness = from + ((to - from) * i / steps);
            rs.Stroke = new SolidColorPaint { Color = SKColors.DarkRed, StrokeThickness = thickness };
            PlotView.InvalidateVisual();
            await Task.Delay(durationMs / steps);
        }
    }

    /// <summary>
    ///     Updates the cursor if it has changed
    /// </summary>
    /// <param name="cursor"></param>
    private void UpdateCursor(Cursor cursor)
    {
        if (PlotView.Cursor != cursor)
            PlotView.Cursor = cursor;
    }

    /// <summary>
    ///     Handles pointer press events for initiating drag on the deafen overlay edge
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="e"></param>
    private void PlotView_PointerPressed(object? sender, PointerEventArgs e)
    {
        Point pixelPoint = e.GetPosition(PlotView);
        LvcPointD lvcPoint = new(pixelPoint.X, pixelPoint.Y);
        LvcPointD dataPoint = PlotView.ScalePixelsToData(lvcPoint);

        foreach (CoreSection section in PlotView.Sections)
            if (section is RectangularSection rs && rs.Fill is SolidColorPaint paint &&
                paint.Color == DeafenOverlayColor)
                if (rs.Xi != null && Math.Abs(dataPoint.X - rs.Xi.Value) < 5)
                {
                    _isDraggingDeafenEdge = true;
                    _draggedDeafenSection = rs;
                    e.Handled = true;
                    break;
                }
    }

    /// <summary>
    ///     Handles pointer release events to stop dragging the deafen overlay edge
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="e"></param>
    private void PlotView_PointerReleased(object? sender, PointerEventArgs e)
    {
        if (_isDraggingDeafenEdge)
        {
            _isDraggingDeafenEdge = false;
            _draggedDeafenSection = null;
            _isHoveringDeafenEdge = false;
            UpdateCursor(new Cursor(StandardCursorType.Arrow));
            e.Handled = true;
        }
    }

    /// <summary>
    /// Handles pointer exit events to reset hover states and hide tooltips with a short delay
    /// </summary>
    /// <param name="tooltipManager"></param>
    private async void PlotView_PointerExited(TooltipManager tooltipManager)
    {
      
    }


    /// <summary>
    ///     Updates the deafen overlay section to reflect the current minimum completion percentage.
    /// </summary>
    /// <param name="minCompletionPercentage"></param>
    /// <param name="durationMs"></param>
    /// <param name="steps"></param>
    public async Task UpdateDeafenOverlayAsync(double? minCompletionPercentage, int durationMs = 60, int steps = 4)
    {
        if (Math.Abs((double)(_lastDeafenOverlayValue - minCompletionPercentage)!) < 0.001)
            return;

        _lastDeafenOverlayValue = (double)minCompletionPercentage!;

        try
        {
            _deafenOverlayCts?.Cancel();
            _deafenOverlayCts = new CancellationTokenSource();
            CancellationToken token = _deafenOverlayCts.Token;

            var sections = PlotView.Sections.ToList();
            RectangularSection? deafenRect = null;
            foreach (CoreSection s in sections)
                if (s is RectangularSection rs && rs.Fill is SolidColorPaint paint && paint.Color == DeafenOverlayColor)
                {
                    deafenRect = rs;
                    break;
                }

            double? newXi = minCompletionPercentage * MaxLimit / 100.0;

            if (deafenRect == null)
            {
                deafenRect = new RectangularSection
                {
                    Xi = newXi,
                    Xj = MaxLimit,
                    Yi = 0,
                    Yj = MaxYValue,
                    Fill = new SolidColorPaint { Color = DeafenOverlayColor }
                };
                sections.Add(deafenRect);
                PlotView.Sections = sections;
                PlotView.InvalidateVisual();
                return;
            }

            double oldXi = deafenRect.Xi ?? 0;
            if (Math.Abs((double)(oldXi - newXi)) < 0.001)
                return;

            for (int i = 1; i <= steps; i++)
            {
                token.ThrowIfCancellationRequested();
                deafenRect.Xi = oldXi + ((newXi - oldXi) * i / steps);
                PlotView.InvalidateVisual();
                await Task.Delay(durationMs / steps, token);
            }

            deafenRect.Xi = newXi;
            PlotView.InvalidateVisual();
        }
        catch (TaskCanceledException)
        {
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error updating deafen overlay: {ex.Message}");
        }
    }

    /// <summary>
    ///     Updates the chart axes based on current data ranges.
    /// </summary>
    /// <param name="sections"></param>
    /// <param name="minCompletionPercentage"></param>
    private void AddDeafenOverlaySection(List<AnnotatedSection> sections, double minCompletionPercentage)
    {
        double newXi = minCompletionPercentage * MaxLimit / 100.0;
        AnnotatedSection deafenSection = new()
        {
            Xi = newXi,
            Xj = MaxLimit,
            Yi = 0,
            Yj = MaxYValue,
            Fill = new SolidColorPaint { Color = DeafenOverlayColor },
            Tooltip = false
        };
        sections.RemoveAll(s => s is { Fill: SolidColorPaint paint } && paint.Color == DeafenOverlayColor);
        sections.Add(deafenSection);
    }

    /// <summary>
    ///     Updates the deafen overlay section position based on the minimum completion percentage
    /// </summary>
    /// <param name="graphData"></param>
    /// <param name="minCompletionPercentage"></param>
    public async Task UpdateChart(GraphData? graphData, double minCompletionPercentage)
    {
        Stopwatch sw = Stopwatch.StartNew();
        if (graphData == null) return;

        bool graphChanged = !ReferenceEquals(graphData, _lastGraphData);
        bool minCompletionChanged = Math.Abs(minCompletionPercentage - _lastMinCompletionPercentage) > 0.001;
        string? osuFilePath = _tosuApi.GetFullFilePath();
        bool fileChanged = osuFilePath != _lastOsuFilePath;

        if (!graphChanged && !minCompletionChanged && !fileChanged) return;

        _lastGraphData = graphData;
        _lastMinCompletionPercentage = minCompletionPercentage;
        _lastOsuFilePath = osuFilePath;

        MaxYValue = GetMaxYValue(graphData);

        bool seriesUpdated = false;
        var seriesArr = PlotView.Series as List<ISeries> ?? PlotView.Series.ToList();

        if (graphChanged)
        {
            seriesArr = await UpdateSeries(graphData, seriesArr);
            seriesUpdated = true;
        }

        if (osuFilePath != null && (fileChanged || graphChanged)) await UpdateSectionsAsync(graphData, osuFilePath);

        UpdateDeafenOverlaySection(minCompletionPercentage);

        if (!seriesArr.Contains(_progressIndicator))
        {
            seriesArr.Add(_progressIndicator);
            seriesUpdated = true;
        }

        if (seriesUpdated)
        {
            Series = seriesArr.ToArray();
            PlotView.Series = Series;
        }

        if (graphChanged || fileChanged)
            UpdateAxes();

        if (minCompletionChanged)
            await UpdateDeafenOverlayAsync(minCompletionPercentage);

        PlotView.TooltipPosition = TooltipPosition.Hidden;
        PlotView.InvalidateVisual();
        sw.Stop();
        Console.WriteLine($"Chart updated in {sw.ElapsedMilliseconds} ms");
    }

    /// <summary>
    ///     Gets the maximum Y value from the graph data series
    /// </summary>
    /// <param name="graphData"></param>
    /// <returns></returns>
    private static double GetMaxYValue(GraphData graphData)
    {
        double maxY = 0.0;
        foreach (Series s in graphData.Series)
        foreach (double v in s.Data)
            if (v != -100 && v > maxY)
                maxY = v;
        return maxY;
    }

    /// <summary>
    ///     Updates the data series in the chart based on the provided graph data
    /// </summary>
    /// <param name="graphData"></param>
    /// <param name="seriesArr"></param>
    /// <returns></returns>
    private async Task<List<ISeries>> UpdateSeries(GraphData graphData, List<ISeries> seriesArr)
    {
        const int maxPoints = 1000;
        var newSeriesList = new List<ISeries>();
        int maxLimit = 0;

        var existingSeriesDict = seriesArr
            .OfType<LineSeries<ObservablePoint>>()
            .Where(ls => ls.Name != null)
            .ToDictionary(ls => ls.Name!, ls => ls);

        foreach (Series series in graphData.Series)
        {
            int start = series.Data.FindIndex(v => v != -100);
            int end = series.Data.FindLastIndex(v => v != -100);
            if (start == -1 || end == -1 || end < start) continue;

            int updatedCount = end - start + 1;
            var updatedValues = new ObservablePoint[updatedCount];
            int idx = 0;
            for (int i = start; i <= end; i++)
                if (series.Data[i] != -100)
                    updatedValues[idx++] = new ObservablePoint(i - start, series.Data[i]);
            maxLimit = Math.Max(maxLimit, idx);

            var downsampled = Downsample(updatedValues, maxPoints);
            var smoothed = SmoothData(downsampled, 10, 0.2);

            SKColor color = series.Name == "aim" ? AimColor : SpeedColor;
            string name = series.Name == "aim" ? "Aim" : "Speed";

            if (existingSeriesDict.TryGetValue(name, out var existing))
            {
                if (!ReferenceEquals(existing.Values, smoothed))
                    existing.Values = smoothed;
                existing.XToolTipLabelFormatter = _ => "";
                existing.YToolTipLabelFormatter = _ => "";
                newSeriesList.Add(existing);
            }
            else
            {
                newSeriesList.Add(new LineSeries<ObservablePoint>
                {
                    Values = smoothed,
                    Fill = new SolidColorPaint { Color = color },
                    Stroke = new SolidColorPaint { Color = color },
                    Name = name,
                    GeometryFill = null,
                    GeometryStroke = null,
                    LineSmoothness = 1,
                    EasingFunction = EasingFunctions.ExponentialOut,
                    XToolTipLabelFormatter = _ => "",
                    YToolTipLabelFormatter = _ => ""
                });
            }
        }

        MaxLimit = maxLimit;

        if (!newSeriesList.Contains(_progressIndicator))
            newSeriesList.Add(_progressIndicator);

        Series = newSeriesList.ToArray();
        PlotView.Series = Series;

        try
        {
            PlotView.Sections = new List<CoreSection>();
            MaxYValue = GetMaxYValue(graphData);
            MaxLimit = maxLimit;
            PlotView.Series = Series;
            PlotView.DrawMargin = new Margin(0, 0, 0, 0);
            UpdateAxes();
            if (_lastOsuFilePath != null) 
                await UpdateSectionsAsync(graphData, _lastOsuFilePath);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Exception: {ex}");
        }

        var allSections = PlotView.Sections.ToList();

        PlotView.Sections = allSections;
        PlotView.InvalidateVisual();

        return newSeriesList;
    }

    /// <summary>
    ///     Updates the break and kiai sections on the chart based on the provided graph data and osu! file path
    /// </summary>
    /// <param name="graphData"></param>
    /// <param name="osuFilePath"></param>
    // Only use indices, avoid copying large lists
    private async Task UpdateSectionsAsync(GraphData graphData, string osuFilePath)
    {
        double rate = _tosuApi.GetRateAdjustRate();
        var xAxis = graphData.XAxis;
        _currentXAxis = xAxis;
        var seriesData = graphData.Series[0].Data;
        int firstValidIdx = seriesData.FindIndex(y => y != -100);

        int dataStart = Math.Max(0, firstValidIdx);
        int dataCount = seriesData.Count - dataStart;

        // Use indices for break/kiai calculations
        var breaks = await GetBreakPeriodsAsync(osuFilePath, xAxis, seriesData);

        _cachedBreakPeriods.Clear();
        foreach (BreakPeriod breakPeriod in breaks)
        {
            double breakStart = _tosuApi.GetRawBanchoStatus() != 2
                ? breakPeriod.Start / rate
                : breakPeriod.Start;
            double breakEnd = _tosuApi.GetRawBanchoStatus() != 2
                ? breakPeriod.End / rate
                : breakPeriod.End;

            int startIdx = FindClosestIndex(xAxis, breakStart, dataStart, dataCount);
            int endIdx = FindClosestIndex(xAxis, breakEnd, dataStart, dataCount);
            AnnotatedSection breakSection = new()
            {
                Xi = startIdx,
                Xj = endIdx,
                Yi = 0,
                Yj = MaxYValue,
                Fill = AudibleBreaksEnabled
                    ? new LinearGradientPaint(
                        new[] { new SKColor(0x00, 0x80, 0xFF, 255) },
                        new SKPoint(0, 0),
                        new SKPoint(endIdx - startIdx, (float)MaxYValue)
                    )
                    : new SolidColorPaint { Color = BreakColor },
                SectionType = "Break",
                StartTime = breakPeriod.Start,
                EndTime = breakPeriod.End,
                Tooltip = true
            };
            _cachedBreakPeriods.Add(breakSection);
        }

        var kiaiList = await _kiaiTimes.ParseKiaiTimesAsync(osuFilePath);
        _cachedKiaiPeriods.Clear();
        foreach (KiaiTime kiai in kiaiList)
        {
            double kiaiStart = _tosuApi.GetRawBanchoStatus() != 2
                ? kiai.Start / rate
                : kiai.Start;
            double kiaiEnd = _tosuApi.GetRawBanchoStatus() != 2
                ? kiai.End / rate
                : kiai.End;

            int startIdx = FindClosestIndex(xAxis, kiaiStart, dataStart, dataCount);
            int endIdx = FindClosestIndex(xAxis, kiaiEnd, dataStart, dataCount);
            _cachedKiaiPeriods.Add(new AnnotatedSection
            {
                Xi = startIdx,
                Xj = endIdx,
                Yi = 0,
                Yj = MaxYValue,
                Fill = new SolidColorPaint { Color = KiaiColor },
                SectionType = "Kiai",
                StartTime = kiai.Start,
                EndTime = kiai.End,
                Tooltip = true
            });
        }

        var combinedSections = _cachedBreakPeriods.Concat(_cachedKiaiPeriods).ToList();
        AddDeafenOverlaySection(combinedSections, _viewModel.MinCompletionPercentage);
        PlotView.Sections = combinedSections;
        PlotView.InvalidateVisual();
    }


    /// <summary>
    ///     Updates the deafen overlay section position based on the minimum completion percentage
    /// </summary>
    /// <param name="minCompletionPercentage"></param>
    public void UpdateDeafenOverlaySection(double minCompletionPercentage)
    {
        double newXi = minCompletionPercentage * MaxLimit / 100.0;
        var sections = PlotView.Sections.ToList();

        RectangularSection? deafenSection = sections
            .OfType<RectangularSection>()
            .FirstOrDefault(s => s.Fill is SolidColorPaint paint && paint.Color == DeafenOverlayColor);

        if (deafenSection != null)
        {
            deafenSection.Xi = newXi;
            deafenSection.Xj = MaxLimit;
            deafenSection.Yi = 0;
            deafenSection.Yj = MaxYValue;
        }
        else
        {
            deafenSection = new RectangularSection
            {
                Xi = newXi,
                Xj = MaxLimit,
                Yi = 0,
                Yj = MaxYValue,
                Fill = new SolidColorPaint { Color = DeafenOverlayColor }
            };
            sections.Add(deafenSection);
        }

        PlotView.Sections = sections;
    }

    /// <summary>
    ///     Updates the chart axes based on current data ranges
    /// </summary>
    private void UpdateAxes()
    {
        XAxes =
        [
            new Axis
            {
                LabelsPaint = new SolidColorPaint(SKColors.Transparent),
                MinLimit = 0,
                MaxLimit = MaxLimit,
                Padding = new Padding(2),
                TextSize = 12
            }
        ];
        YAxes =
        [
            new Axis
            {
                LabelsPaint = new SolidColorPaint(SKColors.Transparent),
                MinLimit = 0,
                MaxLimit = MaxYValue,
                Padding = new Padding(2),
                SeparatorsPaint = new SolidColorPaint(SKColors.Transparent)
            }
        ];

        PlotView.XAxes = XAxes;
        PlotView.YAxes = YAxes;
    }

    /// <summary>
    ///     Finds the index of the closest value in the xAxis list to the specified value using binary search
    /// </summary>
    /// <param name="xAxis"></param>
    /// <param name="value"></param>
    /// <param name="start"></param>
    /// <param name="count"></param>
    /// <returns></returns>
    private int FindClosestIndex(List<double>? xAxis, double value, int start, int count)
    {
        if (xAxis == null || count == 0) return 0;

        int left = start, right = start + count - 1;
        while (left < right)
        {
            int mid = (left + right) / 2;
            if (xAxis[mid] < value)
                left = mid + 1;
            else
                right = mid;
        }

        if (left == start) return start;
        if (left == start + count) return start + count - 1;

        double diffLeft = Math.Abs(xAxis[left] - value);
        double diffPrev = Math.Abs(xAxis[left - 1] - value);
        return diffLeft < diffPrev ? left : left - 1;
    }

    /// <summary>
    ///     Downsamples the data to a maximum number of points using a simple averaging method
    /// </summary>
    /// <param name="data"></param>
    /// <param name="maxPoints"></param>
    /// <returns></returns>
    private static ObservablePoint[] Downsample(ObservablePoint[] data, int maxPoints)
    {
        int dataCount = data.Length;
        if (dataCount <= maxPoints) return data;

        var result = new ObservablePoint[maxPoints];
        double step = (double)(dataCount - 1) / (maxPoints - 1);

        for (int i = 0; i < maxPoints; i++)
        {
            int idx = (int)Math.Round(i * step);
            if (idx >= dataCount) idx = dataCount - 1;
            result[i] = data[idx];
        }

        return result;
    }

    /// <summary>
    ///     Applies a simple moving average smoothing to the data
    /// </summary>
    /// <param name="data"></param>
    /// <param name="windowSize"></param>
    /// <param name="smoothingFactor"></param>
    /// <returns></returns>
    private static ObservablePoint[] SmoothData(ObservablePoint[] data, int windowSize, double smoothingFactor)
    {
        int n = data.Length;
        var smoothedData = new ObservablePoint[n];
        int halfWindow = Math.Max(1, (int)(windowSize * smoothingFactor));
        double[] prefixSum = new double[n + 1];

        for (int i = 0; i < n; i++)
            prefixSum[i + 1] = prefixSum[i] + (data[i].Y ?? 0.0);

        for (int i = 0; i < n; i++)
        {
            int left = Math.Max(0, i - halfWindow);
            int right = Math.Min(n - 1, i + halfWindow);
            int count = right - left + 1;
            double sum = prefixSum[right + 1] - prefixSum[left];
            smoothedData[i] = new ObservablePoint(data[i].X, sum / count);
        }

        return smoothedData;
    }

    /// <summary>
    ///     Compares two lists for equality
    /// </summary>
    /// <param name="a"></param>
    /// <param name="b"></param>
    /// <typeparam name="T"></typeparam>
    /// <returns></returns>
    private static bool AreListsEqual<T>(List<T>? a, List<T>? b)
    {
        if (ReferenceEquals(a, b)) return true;
        if (a == null || b == null || a.Count != b.Count) return false;
        return a.SequenceEqual(b);
    }

    /// <summary>
    ///     Gets the break periods from the .osu file
    /// </summary>
    /// <param name="osuFilePath"></param>
    /// <param name="xAxis"></param>
    /// <param name="seriesData"></param>
    /// <returns></returns>
    private async Task<List<BreakPeriod>> GetBreakPeriodsAsync(string? osuFilePath, List<double>? xAxis, List<double> seriesData)
    {
        if (osuFilePath == _lastOsuFilePath &&
            AreListsEqual(xAxis, _lastXAxis) &&
            AreListsEqual(seriesData, _lastSeriesData))
            return _lastBreaks ?? [];

        var breaks = await _breakPeriod.ParseBreakPeriodsAsync(osuFilePath, xAxis, seriesData);
        _lastOsuFilePath = osuFilePath;
        _lastXAxis = xAxis != null ? [..xAxis] : [];
        _lastSeriesData = new List<double>(seriesData);
        _lastBreaks = breaks;
        return breaks;
    }
}