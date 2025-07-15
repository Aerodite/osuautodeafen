using System;
using System.Collections.Generic;
using System.Linq;
using LiveChartsCore.Defaults;
using LiveChartsCore.SkiaSharpView;

namespace osuautodeafen.cs.StrainGraph;

public class ProgressIndicatorHelper
{
    private readonly ChartManager _chartManager;
    private readonly TosuApi _tosuApi;
    private readonly SharedViewModel _viewModel;
    private double _lastCompletionPercentage = -1;

    private List<ObservablePoint> _lastContour = new();

    public ProgressIndicatorHelper(
        ChartManager chartManager,
        TosuApi tosuApi,
        SharedViewModel viewModel)
    {
        _chartManager = chartManager ?? throw new ArgumentNullException(nameof(chartManager));
        _tosuApi = tosuApi ?? throw new ArgumentNullException(nameof(tosuApi));
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
    }

    public double ChartXMin
    {
        get
        {
            var xAxes = _chartManager.XAxes;
            if (xAxes.Length == 0) return 0;
            var min = xAxes[0].MinLimit;
            return min ?? 0;
        }
    }

    public double ChartXMax
    {
        get
        {
            var xAxes = _chartManager.XAxes;
            if (xAxes.Length == 0) return 0;
            var max = xAxes[0].MaxLimit;
            return max ?? 0;
        }
    }

    public double ChartYMin
    {
        get
        {
            var yAxes = _chartManager.YAxes;
            if (yAxes.Length == 0) return 0;
            var min = yAxes[0].MinLimit;
            return min ?? 0;
        }
    }

    public double ChartYMax
    {
        get
        {
            var yAxes = _chartManager.YAxes;
            if (yAxes.Length == 0) return 0;
            var max = yAxes[0].MaxLimit;
            return max ?? 0;
        }
    }

    // Old method retained for compatibility
    public List<ObservablePoint> CalculateProgressIndicatorPoints(double completionPercentage, bool force = false)
    {
        var XAxes = _chartManager.XAxes;
        if (XAxes.Length == 0 || completionPercentage < 0 || completionPercentage > 100)
            return new List<ObservablePoint>();

        if (!force && Math.Abs(completionPercentage - _lastCompletionPercentage) < 0.1)
            return new List<ObservablePoint>();
        _lastCompletionPercentage = completionPercentage;

        var xAxis = XAxes[0];
        var maxXLimit = xAxis.MaxLimit;
        if (!maxXLimit.HasValue) return new List<ObservablePoint>();

        var progressPosition = completionPercentage / 100 * maxXLimit.Value;
        var leftEdgePosition = Math.Max(progressPosition - 0.1, 0);

        var lineSeriesList = _chartManager.Series
            .OfType<LineSeries<ObservablePoint>>()
            .Where(s => s.Name == "Aim" || s.Name == "Speed")
            .ToArray();

        if (lineSeriesList.Length == 0) return new List<ObservablePoint>();

        var sortedPointsCache =
            new Dictionary<LineSeries<ObservablePoint>, List<ObservablePoint>>(lineSeriesList.Length);
        foreach (var series in lineSeriesList)
        {
            var values = series.Values as List<ObservablePoint> ?? series.Values.ToList();
            if (values.Count > 1 && !IsSortedByX(values))
                values.Sort((a, b) => Nullable.Compare(a.X, b.X));
            sortedPointsCache[series] = values;
        }

        var steps = Math.Max(32, (int)(_chartManager.PlotView.Bounds.Width / 5));
        var step = (progressPosition - leftEdgePosition) / steps;
        if (step <= 0) step = 0.1;

        var topContourPoints = new List<ObservablePoint>(steps + 4);
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

        return topContourPoints;

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


    public List<ObservablePoint> CalculateOverlayPoints()
    {
        var XAxes = _chartManager.XAxes;
        if (XAxes.Length == 0)
            return new List<ObservablePoint>();

        var xAxis = XAxes[0];
        var minX = xAxis.MinLimit ?? 0;
        var maxX = xAxis.MaxLimit ?? 0;
        if (maxX <= minX) return new List<ObservablePoint>();

        var lineSeriesList = _chartManager.Series
            .OfType<LineSeries<ObservablePoint>>()
            .Where(s => s.Name == "Aim" || s.Name == "Speed")
            .ToArray();

        if (lineSeriesList.Length == 0) return new List<ObservablePoint>();

        var steps = Math.Max(32, (int)(_chartManager.PlotView.Bounds.Width / 5));
        var step = (maxX - minX) / steps;

        var points = new List<ObservablePoint>();

        // Top edge (left to right)
        for (var i = 0; i <= steps; i++)
        {
            var x = minX + i * step;
            double? maxY = null;
            foreach (var series in lineSeriesList)
            {
                var values = series.Values as List<ObservablePoint> ?? series.Values.ToList();
                if (values.Count == 0) continue;
                var idx = BinarySearchX(values, x);
                var left = values[Math.Max(idx, 0)];
                var right = values[Math.Min(idx + 1, values.Count - 1)];
                var y = InterpolateY(left, right, x);
                if (maxY == null || y > maxY) maxY = y;
            }

            if (maxY != null)
                points.Add(new ObservablePoint(x, maxY.Value));
        }

        // Right edge up to ChartYMax, then left edge at ChartYMax
        for (var i = steps; i >= 0; i--)
        {
            var x = minX + i * step;
            points.Add(new ObservablePoint(x, ChartYMax));
        }

        return points;

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
    }

    public List<ObservablePoint> CalculateSmoothProgressContour(double completionPercentage, int steps = 400,
        bool force = false)
    {
        var XAxes = _chartManager.XAxes;
        if (XAxes.Length == 0 || completionPercentage < 0 || completionPercentage > 100)
            return new List<ObservablePoint>();

        if (!force && Math.Abs(completionPercentage - _lastCompletionPercentage) < 0.1)
            return _lastContour;

        _lastCompletionPercentage = completionPercentage;

        var xAxis = XAxes[0];
        var maxXLimit = xAxis.MaxLimit;
        if (!maxXLimit.HasValue) return new List<ObservablePoint>();

        var progressPosition = completionPercentage / 100 * maxXLimit.Value;
        var window = Math.Max(maxXLimit.Value * 0.002, 0.002);
        var leftEdgePosition = Math.Max(progressPosition - window, 0);
        if (progressPosition <= leftEdgePosition)
            leftEdgePosition = Math.Max(progressPosition - 0.01, 0);

        var lineSeriesList = _chartManager.Series
            .OfType<LineSeries<ObservablePoint>>()
            .Where(s => s.Name == "Aim" || s.Name == "Speed")
            .ToArray();

        if (lineSeriesList.Length == 0) return new List<ObservablePoint>();

        var sortedPointsCache =
            new Dictionary<LineSeries<ObservablePoint>, List<ObservablePoint>>(lineSeriesList.Length);
        foreach (var series in lineSeriesList)
        {
            var values = series.Values as List<ObservablePoint> ?? series.Values.ToList();
            if (values.Count > 1 && !IsSortedByX(values))
                values.Sort((a, b) => Nullable.Compare(a.X, b.X));
            sortedPointsCache[series] = values;
        }

        steps = Math.Max(8, (int)(_chartManager.PlotView.Bounds.Width / 5));
        var range = progressPosition - leftEdgePosition;
        var step = range / steps;
        if (step <= 0) step = Math.Max(0.001, range / 8);

        var contour = new List<ObservablePoint>();

        for (var i = 0; i <= steps; i++)
        {
            var x = leftEdgePosition + i * step;
            if (x > progressPosition) x = progressPosition;

            double? maxY = null;
            foreach (var series in lineSeriesList)
            {
                var points = sortedPointsCache[series];
                if (points.Count < 2) continue;

                var leftIndex = BinarySearchX(points, x);
                var leftPoint = points[Math.Max(leftIndex, 0)];
                var rightPoint = points[Math.Min(leftIndex + 1, points.Count - 1)];

                var y = InterpolateY(leftPoint, rightPoint, x);
                if (maxY == null || y > maxY) maxY = y;
            }

            contour.Add(new ObservablePoint(x, maxY ?? 0));
            if (x >= progressPosition) break;
        }

        if (contour.Count < 2) contour.Add(new ObservablePoint(progressPosition, 0));

        // Add multiple horizontal extension points before dropping to zero
        var extraPoints = 10;
        var extensionStep = step * 0.1;

        var last = contour.Last();
        for (var j = 1; j <= extraPoints; j++)
        {
            var extX = (last.X ?? progressPosition) + extensionStep * j;
            contour.Add(new ObservablePoint(extX, last.Y));
        }

        // Drop vertically to zero at the last extension
        var finalX = (last.X ?? progressPosition) + extensionStep * extraPoints;
        contour.Add(new ObservablePoint(finalX, 0));

        // Close the contour
        var leftX = contour.First().X ?? leftEdgePosition;
        contour.Add(new ObservablePoint(leftX, 0));
        contour.Add(contour.First());

        _lastContour = contour;
        return contour;

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
}