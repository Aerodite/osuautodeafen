using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using LiveChartsCore;
using LiveChartsCore.Defaults;
using LiveChartsCore.Drawing;
using LiveChartsCore.Measure;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Avalonia;
using LiveChartsCore.SkiaSharpView.Painting;
using osuautodeafen.cs.StrainGraph.Sections;
using osuautodeafen.cs.StrainGraph.Tooltips;
using SkiaSharp;

namespace osuautodeafen.cs.StrainGraph;

public class ChartManager
{
    private static readonly RectangularSectionComparer SectionComparer = new();
    private static readonly SKColor ProgressIndicatorColor = new(0xFF, 0xFF, 0xFF, 192);
    private static readonly SKColor AimColor = new(0x00, 0xFF, 0x00, 192);
    private static readonly SKColor SpeedColor = new(0x00, 0x00, 0xFF, 140);
    private static readonly SKColor BreakColor = new(0xFF, 0xFF, 0x00, 90);
    private static readonly SKColor KiaiColor = new(0xA0, 0x40, 0xFF, 98);
    private static readonly SKColor DeafenOverlayColor = new(0xFF, 0x00, 0x00, 64);
    private readonly BreakPeriodCalculator _breakPeriod = new();
    private readonly List<RectangularSection> _cachedBreakPeriods = new();
    private readonly List<RectangularSection> _cachedKiaiPeriods = new();

    private readonly KiaiTimes _kiaiTimes;

    private readonly LineSeries<ObservablePoint> _progressIndicator;
    private readonly SectionManager _sectionManager = new();
    private readonly TosuApi _tosuApi;
    private readonly SharedViewModel _viewModel;

    private CancellationTokenSource? _deafenOverlayCts;
    private RectangularSection? _draggedDeafenSection;
    private bool _isDraggingDeafenEdge;

    private bool _isHoveringDeafenEdge;
    private List<BreakPeriod>? _lastBreaks;
    private List<RectangularSection> _lastCombinedSections = new();
    private double _lastDeafenOverlayValue = -1;
    private GraphData? _lastGraphData;
    private double _lastMinCompletionPercentage = -1;
    private string? _lastOsuFilePath;
    private List<double>? _lastSeriesData;
    private List<double>? _lastXAxis;
    private double breakStart, breakEnd;


    public ChartManager(CartesianChart plotView, TosuApi tosuApi, SharedViewModel viewModel,
        KiaiTimes kiaiTimes, TooltipManager tooltipManager)
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

        PlotView.PointerMoved += (s, e) =>
        {
            var pixelPoint = e.GetPosition(PlotView);
            var lvcPoint = new LvcPointD(pixelPoint.X, pixelPoint.Y);
            var dataPoint = PlotView.ScalePixelsToData(lvcPoint);

            foreach (var section in PlotView.Sections.OfType<AnnotatedSection>())
                if (dataPoint.X >= section.Xi && dataPoint.X <= section.Xj)
                {
                    var start = TimeSpan.FromMilliseconds(section.StartTime).ToString(@"mm\:ss\:ff");
                    var end = TimeSpan.FromMilliseconds(section.EndTime).ToString(@"mm\:ss\:ff");
                    string tooltipText;

                    if (section.SectionType == "Break" && AudibleBreaksEnabled)
                        tooltipText = $"Break (Undeafened)\n{start}-{end}";
                    else if (section.SectionType == "Break")
                        tooltipText = $"Break (Deafened)\n{start}-{end}";
                    else
                        tooltipText = $"{section.SectionType}\n{start}-{end}";

                    tooltipManager.ShowCustomTooltip(pixelPoint, tooltipText, PlotView.Bounds);
                    return;
                }

            var hovered = false;
            foreach (var section in PlotView.Sections)
                if (section is RectangularSection rs && rs.Fill is SolidColorPaint paint &&
                    paint.Color == DeafenOverlayColor)
                {
                    rs.Yi = -MaxYValue * 0.6;
                    rs.Yj = MaxYValue * 1.5;
                    rs.Stroke = new SolidColorPaint
                    {
                        Color = _isHoveringDeafenEdge ? SKColors.DarkRed : DeafenOverlayColor,
                        StrokeThickness = _isHoveringDeafenEdge ? 7 : 6
                    };
                    rs.Xj = MaxLimit * 1.5;

                    if (Math.Abs((double)(dataPoint.X - rs.Xi)) < 15)
                    {
                        if ((PlotView.Cursor = Cursor.Default) != null)
                            PlotView.Cursor = new Cursor(StandardCursorType.Hand);
                        _isHoveringDeafenEdge = true;
                        hovered = true;
                        break;
                    }

                    if (PlotView.Cursor != null && PlotView.Cursor != new Cursor(StandardCursorType.Arrow))
                        PlotView.Cursor = new Cursor(StandardCursorType.Arrow);
                    _isHoveringDeafenEdge = false;
                }

            if (!hovered)
                tooltipManager.HideCustomTooltip();
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
                tooltipManager.HideCustomTooltip();
                e.Handled = true;
            }
        };
        //makes the deafen section save to settings when dragged
        PlotView.PointerMoved += async (s, e) =>
        {
            var pixelPoint = e.GetPosition(PlotView);
            var lvcPoint = new LvcPointD(pixelPoint.X, pixelPoint.Y);
            var dataPoint = PlotView.ScalePixelsToData(lvcPoint);

            if (_isDraggingDeafenEdge && _draggedDeafenSection != null)
            {
                var maxXi = MaxLimit;
                var newXi = Math.Max(0, Math.Min(dataPoint.X, Math.Min((_draggedDeafenSection.Xj ?? 0) - 1, maxXi)));
                _draggedDeafenSection.Xi = newXi;

                var newPercentage = Math.Min(100.0, 100.0 * newXi / MaxLimit); // Clamp to 100%
                _viewModel.MinCompletionPercentage = (int)newPercentage;


                //hacky asffffff please ignore
                var mainWindow =
                    Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
                        ? desktop.MainWindow as MainWindow
                        : null;

                if (mainWindow?.CompletionPercentageSlider != null)
                    mainWindow.CompletionPercentageSlider.Value = newPercentage;

                var oldValue = _viewModel.MinCompletionPercentage;
                var args = new RangeBaseValueChangedEventArgs(oldValue, newPercentage,
                    null);
                mainWindow?.CompletionPercentageSlider_ValueChanged(null, args);

                await UpdateDeafenOverlayAsync(newPercentage);

                tooltipManager.ShowCustomTooltip(pixelPoint, $"Deafen Min %: \n{newPercentage:F1}%", PlotView.Bounds);
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
            tooltipManager.HideCustomTooltip();
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
        AudibleBreaksEnabled = _viewModel.IsBreakUndeafenToggleEnabled;
        _viewModel.PropertyChanged += (sender, e) =>
        {
            if (e.PropertyName == nameof(_viewModel.IsBreakUndeafenToggleEnabled))
            {
                AudibleBreaksEnabled = _viewModel.IsBreakUndeafenToggleEnabled;
                foreach (var viewSection in PlotView.Sections)
                {
                    var section = viewSection as AnnotatedSection;
                    if (section != null)
                        if (section.SectionType == "Break")
                            _sectionManager.AnimateSectionFill(section,
                                AudibleBreaksEnabled ? BreakColor : SKColors.LightSkyBlue,
                                AudibleBreaksEnabled ? SKColors.LightSkyBlue : BreakColor, AudibleBreaksEnabled, 0, 0,
                                (s, fill) => section.Fill = fill, PlotView.InvalidateVisual);
                }
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

    public bool AudibleBreaksEnabled { get; set; }
    public Canvas IconOverlay { get; set; }

    public ISeries[] Series { get; private set; }
    public Axis[] XAxes { get; private set; } = Array.Empty<Axis>();
    public Axis[] YAxes { get; private set; } = Array.Empty<Axis>();
    public double MaxYValue { get; private set; }

    public double MaxLimit { get; private set; }

    public CartesianChart PlotView { get; }

    public async Task UpdateDeafenOverlayAsync(double? minCompletionPercentage, int durationMs = 60, int steps = 4)
    {
        if (Math.Abs((double)(_lastDeafenOverlayValue - minCompletionPercentage)!) < 0.001)
            return;

        _lastDeafenOverlayValue = (double)minCompletionPercentage!;

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

    private void AddDeafenOverlaySection(List<RectangularSection> sections, double minCompletionPercentage)
    {
        var newXi = minCompletionPercentage * MaxLimit / 100.0;
        var deafenSection = new RectangularSection
        {
            Xi = newXi,
            Xj = MaxLimit,
            Yi = 0,
            Yj = MaxYValue,
            Fill = new SolidColorPaint { Color = DeafenOverlayColor }
        };
        sections.RemoveAll(s => s is { Fill: SolidColorPaint paint } && paint.Color == DeafenOverlayColor);
        sections.Add(deafenSection);
    }

    public async Task UpdateChart(GraphData? graphData, double minCompletionPercentage)
    {
        var sw = Stopwatch.StartNew();
        if (graphData == null) return;

        var graphChanged = !ReferenceEquals(graphData, _lastGraphData);
        var minCompletionChanged = Math.Abs(minCompletionPercentage - _lastMinCompletionPercentage) > 0.001;
        var osuFilePath = _tosuApi.GetFullFilePath();
        var fileChanged = osuFilePath != _lastOsuFilePath;

        if (!graphChanged && !minCompletionChanged && !fileChanged) return;

        _lastGraphData = graphData;
        _lastMinCompletionPercentage = minCompletionPercentage;
        _lastOsuFilePath = osuFilePath;

        MaxYValue = GetMaxYValue(graphData);

        var seriesArr = PlotView.Series.ToList();
        if (graphChanged)
            seriesArr = UpdateSeries(graphData, seriesArr);

        if (osuFilePath != null && (fileChanged || graphChanged))
            await UpdateSectionsAsync(graphData, osuFilePath);

        UpdateDeafenOverlaySection(minCompletionPercentage);

        if (!seriesArr.Contains(_progressIndicator))
        {
            seriesArr.Add(_progressIndicator);
            Series = seriesArr.ToArray();
            PlotView.Series = Series;
        }

        UpdateAxes();

        if (minCompletionChanged)
            await UpdateDeafenOverlayAsync(minCompletionPercentage);

        PlotView.TooltipPosition = TooltipPosition.Hidden;
        PlotView.InvalidateVisual();
        sw.Stop();
        Console.WriteLine($"Chart updated in {sw.ElapsedMilliseconds} ms");
    }

    private double GetMaxYValue(GraphData graphData)
    {
        var maxY = 0.0;
        foreach (var s in graphData.Series)
        foreach (var v in s.Data)
            if (v != -100 && v > maxY)
                maxY = v;
        return maxY;
    }

    private List<ISeries> UpdateSeries(GraphData graphData, List<ISeries> seriesArr)
    {
        var maxPoints = 1000;
        var newSeriesList = new List<ISeries>();
        foreach (var series in graphData.Series)
        {
            int start = 0, end = series.Data.Count - 1;
            while (start <= end && series.Data[start] == -100) start++;
            while (end >= start && series.Data[end] == -100) end--;
            if (end < start) continue;

            var updatedCount = end - start + 1;
            var updatedValues = new ObservablePoint[updatedCount];
            var idx = 0;
            for (var i = start; i <= end; i++)
                if (series.Data[i] != -100)
                    updatedValues[idx++] = new ObservablePoint(i - start, series.Data[i]);
            MaxLimit = idx;

            var downsampled = Downsample(updatedValues, idx, maxPoints);
            var smoothed = SmoothData(downsampled, 10, 0.2);

            var color = series.Name == "aim" ? AimColor : SpeedColor;
            var name = series.Name == "aim" ? "Aim" : "Speed";

            var existing =
                seriesArr.OfType<LineSeries<ObservablePoint>>().FirstOrDefault(ls => ls.Name == name);

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
        return newSeriesList;
    }

    private async Task UpdateSectionsAsync(GraphData graphData, string osuFilePath)
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
        _cachedBreakPeriods.Clear();
        foreach (var breakPeriod in breaks)
        {
            // for whatever reason when going into gameplay
            // the x-axis values go back to how they should be
            // (i.e., rate is already applied)
            // that triggers UpdateChart() and makes everything
            // update with those x-values.
            // so this is just to make sure the break periods are
            // in the right place in that case 😑
            if (_tosuApi.GetRawBanchoStatus() != 2)
            {
                breakStart = breakPeriod.Start / rate;
                breakEnd = breakPeriod.End / rate;
            }
            else
            {
                breakStart = breakPeriod.Start;
                breakEnd = breakPeriod.End;
            }

            var startIdx = FindClosestIndex(xAxis, breakStart);
            var endIdx = FindClosestIndex(xAxis, breakEnd);
            _cachedBreakPeriods.Add(new AnnotatedSection
            {
                Xi = startIdx,
                Xj = endIdx,
                Yi = 0,
                Yj = MaxYValue,
                Fill = AudibleBreaksEnabled
                    ? new LinearGradientPaint(
                        new[]
                        {
                            new SKColor(0x00, 0x80, 0xFF, 255) // Blue
                        },
                        new SKPoint(0, 0),
                        new SKPoint(endIdx - startIdx, (float)MaxYValue)
                    )
                    : new SolidColorPaint { Color = BreakColor },
                SectionType = "Break",
                StartTime = breakPeriod.Start,
                EndTime = breakPeriod.End
            });
        }

        var kiaiList = await _kiaiTimes.ParseKiaiTimesAsync(osuFilePath);
        _cachedKiaiPeriods.Clear();
        foreach (var kiai in kiaiList)
        {
            double kiaiStart, kiaiEnd;
            if (_tosuApi.GetRawBanchoStatus() != 2)
            {
                kiaiStart = kiai.Start / rate;
                kiaiEnd = kiai.End / rate;
            }
            else
            {
                kiaiStart = kiai.Start;
                kiaiEnd = kiai.End;
            }

            var startIdx = FindClosestIndex(xAxis, kiaiStart);
            var endIdx = FindClosestIndex(xAxis, kiaiEnd);
            _cachedKiaiPeriods.Add(new AnnotatedSection
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

        var combinedSections = _cachedBreakPeriods.Concat(_cachedKiaiPeriods).ToList();
        AddDeafenOverlaySection(combinedSections, _viewModel.MinCompletionPercentage);
        PlotView.Sections = combinedSections;
    }

    private void UpdateDeafenOverlaySection(double minCompletionPercentage)
    {
        var newXi = minCompletionPercentage * MaxLimit / 100.0;
        var deafenSection = new RectangularSection
        {
            Xi = newXi,
            Xj = MaxLimit,
            Yi = 0,
            Yj = MaxYValue,
            Fill = new SolidColorPaint { Color = DeafenOverlayColor }
        };

        var combinedSections = PlotView.Sections.ToList();
        combinedSections.RemoveAll(s => s is { Fill: SolidColorPaint paint } && paint.Color == DeafenOverlayColor);
        combinedSections.Add(deafenSection);

        if (!_lastCombinedSections.SequenceEqual(combinedSections.OfType<RectangularSection>(),
                SectionComparer))
        {
            PlotView.Sections = combinedSections;
            _lastCombinedSections = combinedSections.OfType<RectangularSection>().ToList();
        }
    }

    private void UpdateAxes()
    {
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
}