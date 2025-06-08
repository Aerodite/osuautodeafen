﻿﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LiveChartsCore;
using LiveChartsCore.Defaults;
using LiveChartsCore.Drawing;
using LiveChartsCore.Measure;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Avalonia;
using LiveChartsCore.SkiaSharpView.Painting;
using osuautodeafen.cs;
using osuautodeafen.cs.StrainGraph;
using SkiaSharp;

public class ChartManager
{
    private readonly LineSeries<ObservablePoint> _progressIndicator;
    private readonly ProgressIndicatorHelper _progressIndicatorHelper;
    private readonly TosuApi _tosuApi;
    private readonly SharedViewModel _viewModel;
    private readonly List<RectangularSection> cachedBreakPeriods = new();
    private CancellationTokenSource? _deafenOverlayCts;
    private string? cachedOsuFilePath;
    private double maxLimit;
    private double maxYValue;

    public ChartManager(CartesianChart plotView, TosuApi tosuApi, SharedViewModel viewModel, BreakPeriodCalculator _breakPeriod)
    {
        PlotView = plotView ?? throw new ArgumentNullException(nameof(plotView));
        _tosuApi = tosuApi ?? throw new ArgumentNullException(nameof(tosuApi));
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        _breakPeriod = new BreakPeriodCalculator();

        _progressIndicator = new LineSeries<ObservablePoint>
        {
            Stroke = new SolidColorPaint { Color = new SKColor(0xFF, 0xFF, 0xFF, 192), StrokeThickness = 5 },
            GeometryFill = null,
            GeometryStroke = null,
            LineSmoothness = 0,
            Values = Array.Empty<ObservablePoint>(),
            Name = "ProgressIndicator"
        };

        // Initial chart setup
        var series1 = new StackedAreaSeries<ObservablePoint>
        {
            Values = ChartData.Series1Values,
            Fill = new SolidColorPaint { Color = new SKColor(0xFF, 0x00, 0x00) },
            Stroke = new SolidColorPaint { Color = new SKColor(0xFF, 0x00, 0x00) },
            Name = "Aim"
        };
        var series2 = new StackedAreaSeries<ObservablePoint>
        {
            Values = ChartData.Series2Values,
            Fill = new SolidColorPaint { Color = new SKColor(0x00, 0xFF, 0x00) },
            Stroke = new SolidColorPaint { Color = new SKColor(0x00, 0xFF, 0x00) },
            Name = "Speed"
        };
        Series = new ISeries[] { series1, series2, _progressIndicator };
        PlotView.Series = Series;
        PlotView.DrawMargin = new Margin(0, 0, 0, 0);
    }

    public ISeries[] Series { get; private set; } = Array.Empty<ISeries>();
    public Axis[] XAxes { get; private set; } = Array.Empty<Axis>();
    public Axis[] YAxes { get; private set; } = Array.Empty<Axis>();
    public double MaxYValue { get; private set; }
    public double MaxLimit { get; private set; }
    public CartesianChart PlotView { get; }

    public void EnsureProgressIndicator(LineSeries<ObservablePoint> indicator)
    {
        if (!Series.Contains(indicator))
        {
            Series = Series.Append(indicator).ToArray();
            PlotView.Series = Series;
        }
    }

    public async Task UpdateDeafenOverlayAsync(double minCompletionPercentage, int durationMs = 60, int steps = 4)
    {
        // Cancel any previous animation
        _deafenOverlayCts?.Cancel();
        _deafenOverlayCts = new CancellationTokenSource();
        var token = _deafenOverlayCts.Token;

        var color = new SKColor(0xFF, 0x00, 0x00, 64);
        var sections = PlotView.Sections.ToList();
        var deafenRect = sections
            .OfType<RectangularSection>()
            .FirstOrDefault(rs => rs.Fill is SolidColorPaint paint && paint.Color == color);

        var newXi = minCompletionPercentage * maxLimit / 100.0;

        if (deafenRect == null)
        {
            deafenRect = new RectangularSection
            {
                Xi = newXi,
                Xj = maxLimit,
                Yi = 0,
                Yj = maxYValue,
                Fill = new SolidColorPaint { Color = color }
            };
            sections.Add(deafenRect);
            PlotView.Sections = sections;
            PlotView.InvalidateVisual();
            return;
        }

        var oldXi = deafenRect.Xi;
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

    public async Task UpdateChart(GraphData? graphData, double minCompletionPercentage, BreakPeriodCalculator _breakPeriod)
    {
        if (graphData == null) return;

        var seriesArr = PlotView.Series?.ToArray() ?? Array.Empty<ISeries>();
        var maxPoints = 1000;

        // Find max Y
        var maxY = 0.0;
        foreach (var s in graphData.Series)
        foreach (var v in s.Data)
            if (v != -100 && v > maxY)
                maxY = v;
        maxYValue = maxY;

        // Update or add series
        foreach (var series in graphData.Series)
        {
            var data = series.Data;
            int start = 0, end = data.Count - 1;
            while (start <= end && data[start] == -100) start++;
            while (end >= start && data[end] == -100) end--;
            if (end < start) continue;

            var updatedCount = end - start + 1;
            var updatedValues = new ObservablePoint[updatedCount];
            var idx = 0;
            for (var i = start; i <= end; i++)
                if (data[i] != -100)
                    updatedValues[idx++] = new ObservablePoint(i - start, data[i]);
            maxLimit = idx;

            var downsampled = Downsample(updatedValues, idx, maxPoints);
            var smoothed = SmoothData(downsampled, 10, 0.2);

            var color = series.Name == "aim"
                ? new SKColor(0x00, 0xFF, 0x00, 192)
                : new SKColor(0x00, 0x00, 0xFF, 140);
            var name = series.Name == "aim" ? "Aim" : "Speed";

            var existing = seriesArr.OfType<LineSeries<ObservablePoint>>().FirstOrDefault(ls => ls.Name == name);
            if (existing != null)
            {
                existing.Values = smoothed;
                existing.TooltipLabelFormatter = _ => "";
            }
            else
            {
                var newSeries = new LineSeries<ObservablePoint>
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
                };
                seriesArr = seriesArr.Append(newSeries).ToArray();
            }
        }

        // Break periods only
        var osuFilePath = _tosuApi.GetFullFilePath();
        if (osuFilePath != null && osuFilePath != cachedOsuFilePath)
        {
            var breaks =
                await _breakPeriod.ParseBreakPeriodsAsync(osuFilePath, graphData.XAxis, graphData.Series[0].Data);
            cachedBreakPeriods.Clear();
            foreach (var breakPeriod in breaks)
                cachedBreakPeriods.Add(new RectangularSection
                {
                    Xi = breakPeriod.StartIndex / _tosuApi.GetRateAdjustRate(),
                    Xj = breakPeriod.EndIndex / _tosuApi.GetRateAdjustRate(),
                    Yi = 0,
                    Yj = maxYValue,
                    Fill = new SolidColorPaint { Color = new SKColor(0xFF, 0xFF, 0x00, 98) }
                });
            cachedOsuFilePath = osuFilePath;
        }

        PlotView.Sections = cachedBreakPeriods.ToList();

        // Ensure progress indicator is present
        if (!seriesArr.Contains(_progressIndicator))
            seriesArr = seriesArr.Append(_progressIndicator).ToArray();

        Series = seriesArr;
        PlotView.Series = seriesArr;

        PlotView.TooltipPosition = TooltipPosition.Hidden;

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

        await UpdateDeafenOverlayAsync(minCompletionPercentage);

        PlotView.InvalidateVisual();
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
}