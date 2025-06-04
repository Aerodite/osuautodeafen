﻿using System;
using System.Collections.Generic;
using System.Linq;
using LiveChartsCore.Defaults;
using LiveChartsCore.SkiaSharpView;

namespace osuautodeafen.cs.StrainGraph;

public class ProgressIndicatorHelper
{
    private readonly ChartManager _chartManager;
    private readonly LineSeries<ObservablePoint> _progressIndicator;
    private readonly TosuApi _tosuApi;
    private readonly SharedViewModel _viewModel;
    private double _lastCompletionPercentage = -1;

    public ProgressIndicatorHelper(
        ChartManager chartManager,
        TosuApi tosuApi,
        SharedViewModel viewModel,
        LineSeries<ObservablePoint> progressIndicator)
    {
        _chartManager = chartManager ?? throw new ArgumentNullException(nameof(chartManager));
        _tosuApi = tosuApi ?? throw new ArgumentNullException(nameof(tosuApi));
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        _progressIndicator = progressIndicator ?? throw new ArgumentNullException(nameof(progressIndicator));
    }

    public void UpdateProgressIndicator(double completionPercentage)
    {
        var XAxes = _chartManager.XAxes;
        var PlotView = _chartManager.PlotView;
        try
        {
            if (XAxes.Length == 0 || completionPercentage < 0 || completionPercentage > 100)
                return;
            // if (_tosuApi.GetRawBanchoStatus() == 2)
            // {
            //     _viewModel.StatusMessage = "Progress Indicator not updating while in game.";
            //     return;
            // }

            _viewModel.StatusMessage = "";

            if (Math.Abs(completionPercentage - _lastCompletionPercentage) < 0.1) return;
            _lastCompletionPercentage = completionPercentage;

            var xAxis = XAxes[0];
            var maxXLimit = xAxis.MaxLimit;
            if (!maxXLimit.HasValue) return;

            var progressPosition = completionPercentage / 100 * maxXLimit.Value;
            var leftEdgePosition = Math.Max(progressPosition - 0.1, 0);

            var lineSeriesList = _chartManager.Series
                .OfType<LineSeries<ObservablePoint>>()
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

            var topContourPoints = _progressIndicator.Values as List<ObservablePoint>;
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

            _progressIndicator.Values = topContourPoints;

            _chartManager.EnsureProgressIndicator(_progressIndicator);

            PlotView.InvalidateVisual();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"An error occurred while updating the progress indicator: {ex.Message}");
        }

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

    public LineSeries<ObservablePoint> GetIndicatorSeries()
    {
        return _progressIndicator;
    }
}