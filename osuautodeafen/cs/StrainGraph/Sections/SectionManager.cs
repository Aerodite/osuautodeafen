using System;
using System.Collections.Generic;
using Avalonia.Threading;
using LiveChartsCore;
using LiveChartsCore.Painting;
using LiveChartsCore.SkiaSharpView.Painting;
using SkiaSharp;

namespace osuautodeafen.cs.StrainGraph.Sections;

public class SectionManager
{
    private readonly Dictionary<object, DispatcherTimer> _sectionFillTimers = new();

    /// <summary>
    ///     Animates the fill color of a section from startColor to endColor over durationMs milliseconds.
    /// </summary>
    /// <param name="section"></param>
    /// <param name="startColor"></param>
    /// <param name="endColor"></param>
    /// <param name="useGradient"></param>
    /// <param name="gradientWidth"></param>
    /// <param name="gradientHeight"></param>
    /// <param name="setFill"></param>
    /// <param name="invalidateVisual"></param>
    /// <param name="durationMs"></param>
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
        if (_sectionFillTimers.TryGetValue(section, out DispatcherTimer? oldTimer))
        {
            oldTimer.Stop();
            _sectionFillTimers.Remove(section);
        }

        DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(16) };
        DateTime startTime = DateTime.UtcNow;

        timer.Tick += (_, _) =>
        {
            double elapsed = (DateTime.UtcNow - startTime).TotalMilliseconds;
            double t = Math.Min(1, elapsed / durationMs);
            t = EasingFunctions.ExponentialOut((float)t);

            byte r = (byte)(startColor.Red + ((endColor.Red - startColor.Red) * t));
            byte g = (byte)(startColor.Green + ((endColor.Green - startColor.Green) * t));
            byte b = (byte)(startColor.Blue + ((endColor.Blue - startColor.Blue) * t));
            byte a = (byte)(startColor.Alpha + ((endColor.Alpha - startColor.Alpha) * t));

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