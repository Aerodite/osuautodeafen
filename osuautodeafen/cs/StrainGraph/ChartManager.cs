using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using LiveChartsCore;
using LiveChartsCore.Defaults;
using LiveChartsCore.Drawing;
using LiveChartsCore.Measure;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Avalonia;
using LiveChartsCore.SkiaSharpView.Painting;
using osuautodeafen;
using osuautodeafen.cs;
using osuautodeafen.cs.Background;
using osuautodeafen.cs.StrainGraph;
using SkiaSharp;

public class ChartManager
{
    private const double TooltipOffset = 0;
    private const double SpringFrequency = 10;
    private const double SpringDamping = 1.5;
    private static readonly RectangularSectionComparer SectionComparer = new();
    private static readonly SKColor ProgressIndicatorColor = new(0xFF, 0xFF, 0xFF, 192);
    private static readonly SKColor AimColor = new(0x00, 0xFF, 0x00, 192);
    private static readonly SKColor SpeedColor = new(0x00, 0x00, 0xFF, 140);
    private static readonly SKColor BreakColor = new(0xFF, 0xFF, 0x00, 90);
    private static readonly SKColor KiaiColor = new(0xA0, 0x40, 0xFF, 98);
    private static readonly SKColor DeafenOverlayColor = new(0xFF, 0x00, 0x00, 64);
    private readonly BackgroundManager _backgroundManager;
    private readonly BreakPeriodCalculator _breakPeriod = new();
    private readonly KiaiTimes _kiaiTimes;

    private readonly LineSeries<ObservablePoint> _progressIndicator;
    private readonly TosuApi _tosuApi;
    private readonly SharedViewModel _viewModel;
    private readonly List<RectangularSection> cachedBreakPeriods = new();
    private readonly List<RectangularSection> cachedKiaiPeriods = new();

    private Border? _customTooltip;
    private CancellationTokenSource? _deafenOverlayCts;
    private RectangularSection? _draggedDeafenSection;
    private bool _isDraggingDeafenEdge;
    private List<BreakPeriod>? _lastBreaks;
    private List<RectangularSection> _lastCombinedSections = new();
    private double _lastDeafenOverlayValue = -1;
    private GraphData? _lastGraphData;
    private double _lastMinCompletionPercentage = -1;
    private string? _lastOsuFilePath;
    private List<double>? _lastSeriesData;
    private List<double>? _lastXAxis;
    private Canvas? _tooltipCanvas;

    private double _tooltipLeft;
    private TextBlock? _tooltipText;
    private double _tooltipTop;
    private double _tooltipVelocityX;
    private double _tooltipVelocityY;
    
    private bool _isHoveringDeafenEdge = false;



    public ChartManager(CartesianChart plotView, TosuApi tosuApi, SharedViewModel viewModel, KiaiTimes kiaiTimes)
    {
        PlotView = plotView ?? throw new ArgumentNullException(nameof(plotView));
        _tosuApi = tosuApi ?? throw new ArgumentNullException(nameof(tosuApi));
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        _kiaiTimes = kiaiTimes ?? throw new ArgumentNullException(nameof(kiaiTimes));

        _progressIndicator = new LineSeries<ObservablePoint>
        {
            Stroke = new SolidColorPaint { Color = ProgressIndicatorColor, StrokeThickness = 5 },
            GeometryFill = null,
            GeometryStroke = null,
            LineSmoothness = 0,
            Values = Array.Empty<ObservablePoint>(),
            Name = "ProgressIndicator"
        };

        _tooltipCanvas = new Canvas();

PlotView.PointerMoved += (s, e) =>
{
    var pixelPoint = e.GetPosition(PlotView);
    var lvcPoint = new LvcPointD(pixelPoint.X, pixelPoint.Y);
    var dataPoint = PlotView.ScalePixelsToData(lvcPoint);

    // Annotated section tooltip
    foreach (var section in PlotView.Sections.OfType<AnnotatedSection>())
        if (dataPoint.X >= section.Xi && dataPoint.X <= section.Xj)
        {
            var start = TimeSpan.FromMilliseconds(section.StartTime).ToString(@"mm\:ss\:ff");
            var end = TimeSpan.FromMilliseconds(section.EndTime).ToString(@"mm\:ss\:ff");
            var tooltipText = $"{section.SectionType}\n{start}-{end}";
            ShowCustomTooltip(pixelPoint, tooltipText);
            return;
        }

    bool hovered = false;
    foreach (var section in PlotView.Sections)
    {
        if (section is RectangularSection rs && rs.Fill is SolidColorPaint paint && paint.Color == DeafenOverlayColor)
        {
            // only highlight if cursor is near the left edge (Xi)
            if (Math.Abs((double)(dataPoint.X - rs.Xi)) < 5)
            {
                PlotView.Cursor = new Cursor(StandardCursorType.Hand);
                if (!_isHoveringDeafenEdge)
                {
                    _isHoveringDeafenEdge = true;
                    rs.Stroke = new SolidColorPaint
                    {
                        Color = SKColors.Red,
                        StrokeThickness = 14
                    };
                    rs.Yi = -MaxYValue * 0.6;
                    rs.Yj = MaxYValue * 1.5;
                    rs.Xj = MaxLimit * 1.5;
                    PlotView.InvalidateVisual();
                }
                hovered = true;
                break;
            }
            else if (_isHoveringDeafenEdge)
            {
                // Reset to normal
                rs.Yi = 0;
                rs.Yj = MaxYValue;
                rs.Stroke = new SolidColorPaint { Color = DeafenOverlayColor, StrokeThickness = 2 };
                _isHoveringDeafenEdge = false;
                PlotView.InvalidateVisual();
            }
        }
    }

    if (!hovered)
        HideCustomTooltip();
};
        PlotView.PointerPressed += (s, e) =>
        {
            var pixelPoint = e.GetPosition(PlotView);
            var lvcPoint = new LvcPointD(pixelPoint.X, pixelPoint.Y);
            var dataPoint = PlotView.ScalePixelsToData(lvcPoint);

            foreach (var section in PlotView.Sections)
                if (section is RectangularSection rs && rs.Fill is SolidColorPaint paint &&
                    paint.Color == DeafenOverlayColor)
                    if (Math.Abs((double)(dataPoint.X - rs.Xi)) < 5)
                    {
                        _isDraggingDeafenEdge = true;
                        _draggedDeafenSection = rs;
                        e.Handled = true;
                        break;
                    }
        };

        PlotView.PointerReleased += (s, e) =>
        {
            if (_isDraggingDeafenEdge)
            {
                _isDraggingDeafenEdge = false;
                _draggedDeafenSection = null;
                HideCustomTooltip();
                e.Handled = true;
            }
        };

        PlotView.PointerMoved += async (s, e) =>
        {
            var pixelPoint = e.GetPosition(PlotView);
            var lvcPoint = new LvcPointD(pixelPoint.X, pixelPoint.Y);
            var dataPoint = PlotView.ScalePixelsToData(lvcPoint);

            if (_isDraggingDeafenEdge && _draggedDeafenSection != null)
            {
                var newXi = Math.Max(0, Math.Min(dataPoint.X, (_draggedDeafenSection.Xj ?? 0) - 1));
                _draggedDeafenSection.Xi = newXi;

                var newPercentage = 100.0 * newXi / MaxLimit;
                _viewModel.MinCompletionPercentage = (int)newPercentage;
                
                //hacky asffffff please ignore
                MainWindow _mainWindow = Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
                    ? desktop.MainWindow as MainWindow
                    : null;
                
                if (_mainWindow?.CompletionPercentageSlider != null)
                    _mainWindow.CompletionPercentageSlider.Value = newPercentage;

                var oldValue = _viewModel.MinCompletionPercentage;
                var args = new Avalonia.Controls.Primitives.RangeBaseValueChangedEventArgs(oldValue, newPercentage, null);
                _mainWindow?.CompletionPercentageSlider_ValueChanged(null, args);
                
                await UpdateDeafenOverlayAsync(newPercentage);
                ShowCustomTooltip(pixelPoint, $"Deafen %: \n{newPercentage:F1}%");
                PlotView.InvalidateVisual();
                e.Handled = true;
                return;
            }
            
            foreach (var section in PlotView.Sections)
                if (section is RectangularSection rs && rs.Fill is SolidColorPaint paint &&
                    paint.Color == DeafenOverlayColor)
                    if (Math.Abs((double)(dataPoint.X - rs.Xj)) < 5)
                    {
                        PlotView.Cursor = new Cursor(StandardCursorType.SizeWestEast);
                        return;
                    }

            PlotView.Cursor = new Cursor(StandardCursorType.Arrow);
        };
        PlotView.PointerExited += (s, e) =>
        {
            HideCustomTooltip();
            if (_isHoveringDeafenEdge)
            {
                foreach (var section in PlotView.Sections)
                    if (section is RectangularSection rs && rs.Fill is SolidColorPaint paint &&
                        paint.Color == DeafenOverlayColor)
                    {
                        rs.Yi = 0;
                        rs.Yj = MaxYValue;
                        rs.Stroke = new SolidColorPaint { Color = DeafenOverlayColor, StrokeThickness = 2 };
                    }
                _isHoveringDeafenEdge = false;
                PlotView.InvalidateVisual();
            }
        };
        Series = new ISeries[]
        {
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
        };
        PlotView.Series = Series;
        PlotView.DrawMargin = new Margin(0, 0, 0, 0);
    }

    public ISeries[] Series { get; private set; } = Array.Empty<ISeries>();
    public Axis[] XAxes { get; private set; } = Array.Empty<Axis>();
    public Axis[] YAxes { get; private set; } = Array.Empty<Axis>();
    public double MaxYValue { get; private set; }

    public double MaxLimit { get; private set; }

    public CartesianChart PlotView { get; }

    public void SetTooltipControls(Border customTooltip, TextBlock tooltipText)
    {
        _customTooltip = customTooltip;
        _tooltipText = tooltipText;
    }

    private void ShowCustomTooltip(Point position, string text)
    {
        if (_customTooltip == null || _tooltipText == null) return;
        _customTooltip.IsVisible = true;
        _tooltipText.Text = text;

        _customTooltip.Measure(Size.Infinity);

        var chartBounds = PlotView.Bounds;
        var tooltipWidth = _customTooltip.Bounds.Width;

        var leftCandidate = position.X - tooltipWidth - TooltipOffset;
        var rightCandidate = position.X + TooltipOffset;
        var maxLeft = chartBounds.Width - tooltipWidth;

        leftCandidate = Math.Max(0, Math.Min(leftCandidate, maxLeft));
        rightCandidate = Math.Max(0, Math.Min(rightCandidate, maxLeft));

        var targetLeft = leftCandidate == 0 ? rightCandidate : leftCandidate;
        var targetTop = position.Y - _customTooltip.Bounds.Height;

        var dt = 1.0 / 60.0;
        var dx = targetLeft - _tooltipLeft;
        var ax = SpringFrequency * SpringFrequency * dx - 2.0 * SpringDamping * SpringFrequency * _tooltipVelocityX;
        _tooltipVelocityX += ax * dt;
        _tooltipLeft += _tooltipVelocityX * dt;

        var dy = targetTop - _tooltipTop;
        var ay = SpringFrequency * SpringFrequency * dy - 2.0 * SpringDamping * SpringFrequency * _tooltipVelocityY;
        _tooltipVelocityY += ay * dt;
        _tooltipTop += _tooltipVelocityY * dt;

        Canvas.SetLeft(_customTooltip, _tooltipLeft);
        Canvas.SetTop(_customTooltip, _tooltipTop);
    }

    private void HideCustomTooltip()
    {
        if (_customTooltip != null)
            _customTooltip.IsVisible = false;
    }

    public void EnsureProgressIndicator(LineSeries<ObservablePoint> indicator)
    {
        if (!Series.Contains(indicator))
        {
            var list = Series.ToList();
            list.Add(indicator);
            Series = list.ToArray();
            PlotView.Series = Series;
        }
    }

    public async Task UpdateDeafenOverlayAsync(double? minCompletionPercentage, int durationMs = 60, int steps = 4)
    {
        if (Math.Abs((double)(_lastDeafenOverlayValue - minCompletionPercentage)) < 0.001)
            return;

        _lastDeafenOverlayValue = (double)minCompletionPercentage;

        try
        {
            _deafenOverlayCts?.Cancel();
            _deafenOverlayCts = new CancellationTokenSource();
            var token = _deafenOverlayCts.Token;

            var sections = PlotView.Sections.ToList();
            RectangularSection? deafenRect = null;
            foreach (var s in sections)
                if (s is RectangularSection rs && rs.Fill is SolidColorPaint paint && paint.Color == DeafenOverlayColor)
                {
                    deafenRect = rs;
                    break;
                }

            var newXi = minCompletionPercentage * MaxLimit / 100.0;

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

            var oldXi = deafenRect.Xi;
            if (Math.Abs((double)(oldXi - newXi)) < 0.001)
                return;

            for (var i = 1; i <= steps; i++)
            {
                token.ThrowIfCancellationRequested();
                deafenRect.Xi = oldXi + (newXi - oldXi) * i / steps;
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

    public async Task UpdateChart(GraphData? graphData, double minCompletionPercentage)
    {
        var sw = Stopwatch.StartNew();
        if (graphData == null)
            return;

        var graphChanged = !ReferenceEquals(graphData, _lastGraphData);
        var minCompletionChanged = Math.Abs(minCompletionPercentage - _lastMinCompletionPercentage) > 0.001;
        var osuFilePath = _tosuApi.GetFullFilePath();
        var fileChanged = osuFilePath != _lastOsuFilePath;

        if (!graphChanged && !minCompletionChanged && !fileChanged)
            return;

        _lastGraphData = graphData;
        _lastMinCompletionPercentage = minCompletionPercentage;
        _lastOsuFilePath = osuFilePath;

        var maxPoints = 1000;
        var maxY = 0.0;
        foreach (var s in graphData.Series)
        foreach (var v in s.Data)
            if (v != -100 && v > maxY)
                maxY = v;
        MaxYValue = maxY;

        int start, end;
        var seriesArr = PlotView.Series?.ToList() ?? new List<ISeries>();

        if (graphChanged)
        {
            var newSeriesList = new List<ISeries>();
            foreach (var series in graphData.Series)
            {
                var data = series.Data;
                start = 0;
                end = data.Count - 1;
                while (start <= end && data[start] == -100) start++;
                while (end >= start && data[end] == -100) end--;
                if (end < start) continue;

                var updatedCount = end - start + 1;
                var updatedValues = new ObservablePoint[updatedCount];
                var idx = 0;
                for (var i = start; i <= end; i++)
                    if (data[i] != -100)
                        updatedValues[idx++] = new ObservablePoint(i - start, data[i]);
                MaxLimit = idx;

                var downsampled = Downsample(updatedValues, idx, maxPoints);
                var smoothed = SmoothData(downsampled, 10, 0.2);

                var color = series.Name == "aim" ? AimColor : SpeedColor;
                var name = series.Name == "aim" ? "Aim" : "Speed";

                LineSeries<ObservablePoint>? existing = null;
                foreach (var s in seriesArr)
                    if (s is LineSeries<ObservablePoint> ls && ls.Name == name)
                    {
                        existing = ls;
                        break;
                    }

                if (existing != null)
                {
                    existing.Values = smoothed;
                    existing.TooltipLabelFormatter = _ => "";
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
                        TooltipLabelFormatter = _ => ""
                    });
                }
            }

            if (!newSeriesList.Contains(_progressIndicator))
                newSeriesList.Add(_progressIndicator);

            Series = newSeriesList.ToArray();
            PlotView.Series = Series;
            seriesArr = newSeriesList;
        }

        if (osuFilePath != null && (fileChanged || graphChanged))
        {
            var rate = _tosuApi.GetRateAdjustRate();
            var xAxis = graphData.XAxis;
            var seriesData = graphData.Series[0].Data;
            var firstValidIdx = seriesData.FindIndex(y => y != -100);
            if (firstValidIdx > 0)
            {
                xAxis = xAxis.Skip(firstValidIdx).ToList();
                seriesData = seriesData.Skip(firstValidIdx).ToList();
            }

            var breaks = await GetBreakPeriodsAsync(osuFilePath, xAxis, seriesData);
            cachedBreakPeriods.Clear();
            foreach (var breakPeriod in breaks)
            {
                var breakStart = breakPeriod.Start / rate;
                var breakEnd = breakPeriod.End / rate;
                var startIdx = FindClosestIndex(xAxis, breakStart);
                var endIdx = FindClosestIndex(xAxis, breakEnd);
                cachedBreakPeriods.Add(new AnnotatedSection
                {
                    Xi = startIdx,
                    Xj = endIdx,
                    Yi = 0,
                    Yj = MaxYValue,
                    Fill = new SolidColorPaint { Color = BreakColor },
                    SectionType = "Break",
                    StartTime = breakPeriod.Start,
                    EndTime = breakPeriod.End
                });
            }

            var kiaiList = await _kiaiTimes.ParseKiaiTimesAsync(osuFilePath);
            //_kiaiTimes.ResetKiaiState();
            //await _kiaiTimes.UpdateKiaiPeriodState(_tosuApi, _backgroundManager, _viewModel);

            cachedKiaiPeriods.Clear();
            foreach (var kiai in kiaiList)
            {
                var startIdx = FindClosestIndex(xAxis, kiai.Start / rate);
                var endIdx = FindClosestIndex(xAxis, kiai.End / rate);
                cachedKiaiPeriods.Add(new AnnotatedSection
                {
                    Xi = startIdx,
                    Xj = endIdx,
                    Yi = 0,
                    Yj = MaxYValue,
                    Fill = new SolidColorPaint { Color = KiaiColor },
                    SectionType = "Kiai",
                    StartTime = kiai.Start,
                    EndTime = kiai.End
                });
            }
        }

        var combinedSections = cachedBreakPeriods.Concat(cachedKiaiPeriods).ToList();

        // Always create/update the deafen overlay section
        var newXi = minCompletionPercentage * MaxLimit / 100.0;
        var deafenSection = new RectangularSection
        {
            Xi = newXi,
            Xj = MaxLimit,
            Yi = 0,
            Yj = MaxYValue,
            Fill = new SolidColorPaint { Color = DeafenOverlayColor }
        };

        combinedSections.RemoveAll(s => s is { Fill: SolidColorPaint paint } && paint.Color == DeafenOverlayColor);

        combinedSections.Add(deafenSection);

        if (!_lastCombinedSections.SequenceEqual(combinedSections, SectionComparer))
        {
            PlotView.Sections = combinedSections;
            _lastCombinedSections = new List<RectangularSection>(combinedSections);
        }

        if (!seriesArr.Contains(_progressIndicator))
        {
            seriesArr.Add(_progressIndicator);
            Series = seriesArr.ToArray();
            PlotView.Series = Series;
        }

        XAxes = new[]
        {
            new Axis
            {
                LabelsPaint = new SolidColorPaint(SKColors.Transparent),
                MinLimit = 0,
                MaxLimit = MaxLimit,
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
                MaxLimit = MaxYValue,
                Padding = new Padding(2),
                SeparatorsPaint = new SolidColorPaint(SKColors.Transparent)
            }
        };

        PlotView.XAxes = XAxes;
        PlotView.YAxes = YAxes;

        if (minCompletionChanged)
            await UpdateDeafenOverlayAsync(minCompletionPercentage);

        PlotView.TooltipPosition = TooltipPosition.Hidden;
        PlotView.InvalidateVisual();
        sw.Stop();
        Console.WriteLine($"Chart updated in {sw.ElapsedMilliseconds} ms");
    }

    private int FindClosestIndex(List<double> xAxis, double value)
    {
        var closestIndex = 0;
        var smallestDifference = double.MaxValue;
        for (var i = 0; i < xAxis.Count; i++)
        {
            var difference = Math.Abs(xAxis[i] - value);
            if (difference < smallestDifference)
            {
                smallestDifference = difference;
                closestIndex = i;
            }
        }

        return closestIndex;
    }

    private static ObservablePoint[] Downsample(ObservablePoint[] data, int dataCount, int maxPoints)
    {
        if (dataCount <= maxPoints) return data;
        var result = new ObservablePoint[maxPoints];
        var step = (double)dataCount / maxPoints;
        for (var i = 0; i < maxPoints; i++)
        {
            var idx = (int)(i * step);
            result[i] = data[idx];
        }

        return result;
    }

    private static ObservablePoint[] SmoothData(ObservablePoint[] data, int windowSize, double smoothingFactor)
    {
        var n = data.Length;
        var smoothedData = new ObservablePoint[n];
        var adjustedWindow = Math.Max(1, (int)(windowSize * smoothingFactor));
        var sum = 0.0;
        var count = 0;
        var left = 0;

        for (var i = 0; i < n; i++)
        {
            var y = data[i].Y ?? 0.0;
            sum += y;
            count++;

            if (i - left + 1 > adjustedWindow * 2 + 1)
            {
                sum -= data[left].Y ?? 0.0;
                left++;
                count--;
            }

            smoothedData[i] = new ObservablePoint(data[i].X, sum / count);
        }

        return smoothedData;
    }

    private bool AreListsEqual<T>(List<T>? a, List<T>? b)
    {
        if (a == null || b == null || a.Count != b.Count) return false;
        for (var i = 0; i < a.Count; i++)
            if (!EqualityComparer<T>.Default.Equals(a[i], b[i]))
                return false;
        return true;
    }

    public async Task<List<BreakPeriod>> GetBreakPeriodsAsync(
        string osuFilePath, List<double> xAxis, List<double> seriesData)
    {
        if (osuFilePath == _lastOsuFilePath &&
            AreListsEqual(xAxis, _lastXAxis) &&
            AreListsEqual(seriesData, _lastSeriesData))
            return _lastBreaks ?? new List<BreakPeriod>();

        var breaks = await _breakPeriod.ParseBreakPeriodsAsync(osuFilePath, xAxis, seriesData);
        _lastOsuFilePath = osuFilePath;
        _lastXAxis = new List<double>(xAxis);
        _lastSeriesData = new List<double>(seriesData);
        _lastBreaks = breaks;
        return breaks;
    }

    public class AnnotatedSection : RectangularSection
    {
        public string SectionType { get; set; } // "Break" or "Kiai"
        public double StartTime { get; set; }
        public double EndTime { get; set; }
    }
}

public class RectangularSectionComparer : IEqualityComparer<RectangularSection>
{
    public bool Equals(RectangularSection? x, RectangularSection? y)
    {
        if (x == null || y == null) return false;
        return x.Xi == y.Xi && x.Xj == y.Xj && x.Yi == y.Yi && x.Yj == y.Yj;
    }

    public int GetHashCode(RectangularSection obj)
    {
        return HashCode.Combine(obj.Xi, obj.Xj, obj.Yi, obj.Yj);
    }
}