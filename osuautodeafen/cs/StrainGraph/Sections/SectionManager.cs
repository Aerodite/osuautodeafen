using System;
using System.Collections.Generic;
using Avalonia.Threading;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView.Painting;
using SkiaSharp;

namespace osuautodeafen.cs.StrainGraph.Sections;

public class SectionManager
{
    private readonly Dictionary<object?, DispatcherTimer> _sectionFillTimers = new();

    public void AnimateSectionFill(
        object? section,
        SKColor startColor,
        SKColor endColor,
        bool useGradient,
        float gradientWidth,
        float gradientHeight,
        Action<object, Paint> setFill,
        Action invalidateVisual,
        int durationMs = 800)
    {
        if (_sectionFillTimers.TryGetValue(section, out var oldTimer))
        {
            oldTimer.Stop();
            _sectionFillTimers.Remove(section);
        }

        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        var startTime = DateTime.UtcNow;

        timer.Tick += (_, _) =>
        {
            var elapsed = (DateTime.UtcNow - startTime).TotalMilliseconds;
            var t = Math.Min(1, elapsed / durationMs);
            t = EasingFunctions.ExponentialOut((float)t);

            var r = (byte)(startColor.Red + (endColor.Red - startColor.Red) * t);
            var g = (byte)(startColor.Green + (endColor.Green - startColor.Green) * t);
            var b = (byte)(startColor.Blue + (endColor.Blue - startColor.Blue) * t);
            var a = (byte)(startColor.Alpha + (endColor.Alpha - startColor.Alpha) * t);

            Paint fill;
            if (useGradient)
                fill = new LinearGradientPaint(
                    new[] { new SKColor(r, g, b, a) },
                    new SKPoint(0, 0),
                    new SKPoint(gradientWidth, gradientHeight)
                );
            else
                fill = new SolidColorPaint { Color = new SKColor(r, g, b, a) };

            setFill(section, fill);
            invalidateVisual();

            if (t >= 1)
            {
                timer.Stop();
                _sectionFillTimers.Remove(section);
            }
        };
        _sectionFillTimers[section] = timer;
        timer.Start();
    }
}