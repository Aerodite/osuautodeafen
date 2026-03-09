using System;
using System.Collections.Generic;
using System.Linq;
using LiveChartsCore.Defaults;
using LiveChartsCore.SkiaSharpView;

namespace osuautodeafen.cs.StrainGraph.ProgressIndicator;

public class ProgressIndicatorHelper(ChartManager chartManager)
{
    private readonly ChartManager _chartManager = chartManager ?? throw new ArgumentNullException(nameof(chartManager));
    private double _lastCompletionPercentage = -1;
    private List<ObservablePoint> _lastContour = [];

    public double ChartXMin
    {
        get
        {
            var xAxes = _chartManager.XAxes;
            if (xAxes.Length == 0) return 0;
            double? min = xAxes[0].MinLimit;
            return min ?? 0;
        }
    }

    public double ChartXMax
    {
        get
        {
            var xAxes = _chartManager.XAxes;
            if (xAxes.Length == 0) return 0;
            double? max = xAxes[0].MaxLimit;
            return max ?? 0;
        }
    }

    public double ChartYMin
    {
        get
        {
            var yAxes = _chartManager.YAxes;
            if (yAxes.Length == 0) return 0;
            double? min = yAxes[0].MinLimit;
            return min ?? 0;
        }
    }

    public double ChartYMax
    {
        get
        {
            var yAxes = _chartManager.YAxes;
            if (yAxes.Length == 0) return 0;
            double? max = yAxes[0].MaxLimit;
            return max ?? 0;
        }
    }

    /// <summary>
    ///     Calculates a smooth contour representing the maximum Y values of the Strain Graph
    /// </summary>
    /// <param name="completionPercentage"></param>
    /// <param name="steps"></param>
    /// <param name="force"></param>
    /// <returns></returns>
    public List<ObservablePoint> CalculateSmoothProgressContour(double completionPercentage, int steps = 250,
        bool force = false)
    {
        var xAxes = _chartManager.XAxes;
        if (xAxes.Length == 0 || completionPercentage < 0 || completionPercentage > 100)
            return new List<ObservablePoint>();

        if (!force && Math.Abs(completionPercentage - _lastCompletionPercentage) < 0.1)
            return _lastContour;

        _lastCompletionPercentage = completionPercentage;

        Axis xAxis = xAxes[0];
        double? maxXLimit = xAxis.MaxLimit;
        if (!maxXLimit.HasValue) return new List<ObservablePoint>();

        double progressPosition = completionPercentage / 100 * maxXLimit.Value;
        double window = Math.Max(maxXLimit.Value * 0.002, 0.002);
        double leftEdgePosition = Math.Max(progressPosition - window, 0);
        if (progressPosition <= leftEdgePosition)
            leftEdgePosition = Math.Max(progressPosition - 0.01, 0);

        if (_chartManager.Series != null)
        {
            var lineSeriesList = _chartManager.Series
                .OfType<LineSeries<ObservablePoint>>()
                .Where(s => s.Name == "Aim" || s.Name == "Speed")
                .ToArray();

            if (lineSeriesList.Length == 0) return new List<ObservablePoint>();

            var sortedPointsCache =
                new Dictionary<LineSeries<ObservablePoint>, List<ObservablePoint>>(lineSeriesList.Length);
            foreach (var series in lineSeriesList)
                if (series.Values != null)
                {
                    var values = series.Values as List<ObservablePoint> ?? series.Values.ToList();
                    if (values.Count > 1 && !IsSortedByX(values))
                        values.Sort((a, b) => Nullable.Compare(a.X, b.X));
                    sortedPointsCache[series] = values;
                }

            double range = progressPosition - leftEdgePosition;
            double step = range / steps;
            if (step <= 0) step = Math.Max(0.001, range / 8);

            var contour = new List<ObservablePoint>();

            for (int i = 0; i <= steps; i++)
            {
                double x = leftEdgePosition + i * step;
                if (x > progressPosition) x = progressPosition;

                double? maxY = null;
                foreach (var series in lineSeriesList)
                {
                    var points = sortedPointsCache[series];
                    if (points.Count < 2) continue;

                    int leftIndex = BinarySearchX(points, x);
                    ObservablePoint leftPoint = points[Math.Max(leftIndex, 0)];
                    ObservablePoint rightPoint = points[Math.Min(leftIndex + 1, points.Count - 1)];

                    double y = InterpolateY(leftPoint, rightPoint, x);
                    if (maxY == null || y > maxY) maxY = y;
                }

                contour.Add(new ObservablePoint(x, maxY ?? 0));
                if (x >= progressPosition) break;
            }

            if (contour.Count < 2) contour.Add(new ObservablePoint(progressPosition, 0));

            const int extraPoints = 10;
            double extensionStep = step * 0.1;

            ObservablePoint last = contour.Last();
            for (int j = 1; j <= extraPoints; j++)
            {
                double extX = (last.X ?? progressPosition) + extensionStep * j;
                contour.Add(new ObservablePoint(extX, last.Y));
            }

            double finalX = (last.X ?? progressPosition) + extensionStep * extraPoints;
            contour.Add(new ObservablePoint(finalX, 0));

            double leftX = contour.First().X ?? leftEdgePosition;
            contour.Add(new ObservablePoint(leftX, 0));
            contour.Add(contour.First());

            _lastContour = contour;
            return contour;
        }

        return [];

        static bool IsSortedByX(List<ObservablePoint> points)
        {
            for (int i = 1; i < points.Count; i++)
                if (points[i - 1].X > points[i].X)
                    return false;
            return true;
        }

        static int BinarySearchX(List<ObservablePoint> points, double x)
        {
            int lo = 0, hi = points.Count - 1;
            while (lo <= hi)
            {
                int mid = lo + ((hi - lo) >> 1);
                if (points[mid].X < x) lo = mid + 1;
                else if (points[mid].X > x) hi = mid - 1;
                else return mid;
            }

            return lo - 1;
        }
    }

    /// <summary>
    ///     Linearly interpolates the Y value at a given X between two points
    /// </summary>
    /// <param name="leftPoint"></param>
    /// <param name="rightPoint"></param>
    /// <param name="x"></param>
    /// <returns></returns>
    private static double InterpolateY(ObservablePoint leftPoint, ObservablePoint rightPoint, double x)
    {
        double? lx = leftPoint.X;
        double? rx = rightPoint.X;
        double ly = leftPoint.Y ?? 0.0;
        double ry = rightPoint.Y ?? 0.0;

        const double epsilon = 1e-8;
        if (!lx.HasValue || !rx.HasValue || Math.Abs(lx.Value - rx.Value) < epsilon)
            return ly;

        return ly + (ry - ly) * (x - lx.Value) / (rx.Value - lx.Value);
    }
}