using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using LiveChartsCore.Defaults;

namespace osuautodeafen.cs.StrainGraph;

public class ProgressIndicatorOverlay : Control
{
    public static readonly StyledProperty<List<ObservablePoint>?> PointsProperty =
        AvaloniaProperty.Register<ProgressIndicatorOverlay, List<ObservablePoint>?>(nameof(Points));

    public static readonly StyledProperty<double> ChartXMinProperty =
        AvaloniaProperty.Register<ProgressIndicatorOverlay, double>(nameof(ChartXMin));

    public static readonly StyledProperty<double> ChartXMaxProperty =
        AvaloniaProperty.Register<ProgressIndicatorOverlay, double>(nameof(ChartXMax));

    public static readonly StyledProperty<double> ChartYMinProperty =
        AvaloniaProperty.Register<ProgressIndicatorOverlay, double>(nameof(ChartYMin));

    public static readonly StyledProperty<double> ChartYMaxProperty =
        AvaloniaProperty.Register<ProgressIndicatorOverlay, double>(nameof(ChartYMax));

    public ProgressIndicatorOverlay()
    {
        this.GetObservable(PointsProperty).Subscribe(_ => InvalidateVisual());
        this.GetObservable(ChartXMinProperty).Subscribe(_ => InvalidateVisual());
        this.GetObservable(ChartXMaxProperty).Subscribe(_ => InvalidateVisual());
        this.GetObservable(ChartYMinProperty).Subscribe(_ => InvalidateVisual());
        this.GetObservable(ChartYMaxProperty).Subscribe(_ => InvalidateVisual());
    }

    public List<ObservablePoint>? Points
    {
        get => GetValue(PointsProperty);
        set => SetValue(PointsProperty, value);
    }

    public double ChartXMin
    {
        get => GetValue(ChartXMinProperty);
        set => SetValue(ChartXMinProperty, value);
    }

    public double ChartXMax
    {
        get => GetValue(ChartXMaxProperty);
        set => SetValue(ChartXMaxProperty, value);
    }

    public double ChartYMin
    {
        get => GetValue(ChartYMinProperty);
        set => SetValue(ChartYMinProperty, value);
    }

    public double ChartYMax
    {
        get => GetValue(ChartYMaxProperty);
        set => SetValue(ChartYMaxProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        if (Points == null || Points.Count < 2)
            return;

        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            var first = Points[0];
            ctx.BeginFigure(
                new Point(
                    MapChartXToCanvas(first.X ?? 0),
                    MapChartYToCanvas(first.Y ?? 0, ChartYMax)
                ),
                false
            );

            for (var i = 1; i < Points.Count; i++)
            {
                var p = Points[i];
                ctx.LineTo(
                    new Point(
                        MapChartXToCanvas(p.X ?? 0),
                        MapChartYToCanvas(p.Y ?? 0, ChartYMax)
                    )
                );
            }
        }

        context.DrawGeometry(null, new Pen(Brushes.White, 4), geometry);
    }

    private double MapChartXToCanvas(double chartX)
    {
        if (ChartXMax == ChartXMin)
            //Console.WriteLine($"[MapChartXToCanvas] ChartXMax == ChartXMin ({ChartXMax}), returning 0");
            return 0;
        var result = (chartX - ChartXMin) / (ChartXMax - ChartXMin) * Bounds.Width;
        //Console.WriteLine($"[MapChartXToCanvas] chartX={chartX}, ChartXMin={ChartXMin}, ChartXMax={ChartXMax}, Bounds.Width={Bounds.Width} => {result}");
        return result;
    }

    private double MapChartYToCanvas(double chartY, double localYMax)
    {
        if (localYMax == ChartYMin)
            //Console.WriteLine($"[MapChartYToCanvas] localYMax == ChartYMin ({ChartYMin}), returning 0");
            return 0;
        var result = Bounds.Height - (chartY - ChartYMin) / (localYMax - ChartYMin) * Bounds.Height;
        //Console.WriteLine($"[MapChartYToCanvas] chartY={chartY}, ChartYMin={ChartYMin}, localYMax={localYMax}, Bounds.Height={Bounds.Height} => {result}");
        return result;
    }
}