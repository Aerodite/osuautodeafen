using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media;
using Avalonia.Threading;
using osuautodeafen.Background;
using osuautodeafen.ViewModels;
using SkiaSharp;

namespace osuautodeafen.Logo;

public class LogoUpdater(GetLowResBackground getLowResBackground, SharedViewModel viewModel)
{
    private string? _cachedBitmapPath;
    private SKBitmap? _cachedSKBitmap;
    private CancellationTokenSource? _colorTransitionCts;

    private int _currentSectionIndex;
    private SKColor _lastRenderedColor = new(255, 255, 255, 255);
    private List<SKColor> _sectionColors = new();
    public SKColor AverageColor1 { get; private set; }
    public SKColor AverageColor2 { get; private set; }
    public SKColor AverageColor3 { get; private set; }

    public async Task UpdateLogoAsync()
    {
        try
        {
            string? lowResBitmapPath = await GetLowResBitmapPath().ConfigureAwait(false);
            if (lowResBitmapPath == null) return;

            if (_cachedBitmapPath != lowResBitmapPath)
            {
                SKBitmap? newSkiaBitmap =
                    await Task.Run(() => LoadSKBitmap(lowResBitmapPath)).ConfigureAwait(false);
                if (newSkiaBitmap != null)
                {
                    _cachedBitmapPath = lowResBitmapPath;
                    _cachedSKBitmap?.Dispose();
                    _cachedSKBitmap = newSkiaBitmap;
                }
            }

            if (_cachedSKBitmap == null) return;

            var newSectionColors = CalculateSectionColors(_cachedSKBitmap);

            if (newSectionColors.Count >= 3)
            {
                AverageColor1 = newSectionColors[0];
                AverageColor2 = newSectionColors[1];
                AverageColor3 = newSectionColors[2];
            }
            else
            {
                AverageColor1 = AverageColor2 = AverageColor3 = new SKColor(0, 0, 0);
            }

            if (_colorTransitionCts != null)
                await _colorTransitionCts.CancelAsync();

            _colorTransitionCts = new CancellationTokenSource();

            int closestIndex = FindClosestColorIndex(_lastRenderedColor, newSectionColors);
            SKColor firstColor = newSectionColors[closestIndex];

            await InterpolateColor(_lastRenderedColor, firstColor, _colorTransitionCts.Token);

            _currentSectionIndex = closestIndex;
            _sectionColors = newSectionColors;

            _ = InterpolateColorLoop(_colorTransitionCts.Token);
        }
        catch (Exception)
        {
            // ignored
        }
    }
    
    private static SKBitmap? LoadSKBitmap(string path)
    {
        try
        {
            using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using SKManagedStream managedStream = new(stream);
            return SKBitmap.Decode(managedStream);
        }
        catch
        {
            return null;
        }
    }

    private async Task InterpolateColor(SKColor from, SKColor to, CancellationToken token)
    {
        const int steps = 60;
        const int delay = 16;
        for (int i = 0; i <= steps; i++)
        {
            if (token.IsCancellationRequested) return;
            float t = i / (float)steps;
            SKColor interpolatedColor = InterpolateColor(from, to, t);

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                UpdateViewModelColors(interpolatedColor);
            }, DispatcherPriority.Render);

            await Task.Delay(delay, token);
        }
    }

    private async Task InterpolateColorLoop(CancellationToken token)
    {
        if (_sectionColors.Count < 3) return;
        const int steps = 120;
        const int delay = 16;

        while (!token.IsCancellationRequested)
        {
            SKColor from = _sectionColors[_currentSectionIndex];
            SKColor to = _sectionColors[(_currentSectionIndex + 1) % _sectionColors.Count];

            for (int i = 0; i <= steps; i++)
            {
                if (token.IsCancellationRequested) return;
                float t = i / (float)steps;
                SKColor interpolatedColor = InterpolateColor(from, to, t);

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    UpdateViewModelColors(interpolatedColor);
                }, DispatcherPriority.Render);

                await Task.Delay(delay, token);
            }

            _currentSectionIndex = (_currentSectionIndex + 1) % _sectionColors.Count;
        }
    }

    private void UpdateViewModelColors(SKColor color)
    {
        Color avaloniaColor = Color.FromArgb(color.Alpha, color.Red, color.Green, color.Blue);
        
        viewModel.AverageColorBrush = new SolidColorBrush(avaloniaColor);
        viewModel.TooltipAcrylicMaterial = new ExperimentalAcrylicMaterial
        {
            TintColor = avaloniaColor,
            TintOpacity = 0.25,
            MaterialOpacity = 0.2
        };
        _lastRenderedColor = color;
    }

    private static List<SKColor> CalculateSectionColors(SKBitmap bitmap)
    {
        int height = bitmap.Height;
        int sectionHeight = height / 3;
        var colors = new List<SKColor>();
        for (int section = 0; section < 3; section++)
        {
            int yStart = section * sectionHeight;
            int yEnd = section == 2 ? height : yStart + sectionHeight;
            SKColor color = CalculateAverageColor(bitmap, yStart, yEnd);

            byte max = Math.Max(color.Red, Math.Max(color.Green, color.Blue));
            if (max > 0)
            {
                float scale = 200f / max;
                color = new SKColor(
                    (byte)Math.Clamp(color.Red * scale, 16, 200),
                    (byte)Math.Clamp(color.Green * scale, 16, 200),
                    (byte)Math.Clamp(color.Blue * scale, 16, 200),
                    color.Alpha
                );
            }
            colors.Add(color);
        }
        return colors;
    }

    private static unsafe SKColor CalculateAverageColor(SKBitmap bitmap, int yStart, int yEnd)
    {
        int width = bitmap.Width;
        long totalR = 0, totalG = 0, totalB = 0;
        long pixelCount = (long)width * (yEnd - yStart);

        if (!bitmap.IsImmutable)
            bitmap.SetImmutable();

        fixed (void* ptr = &bitmap.GetPixelSpan()[0])
        {
            uint* pixels = (uint*)ptr;
            for (int y = yStart; y < yEnd; y++)
            {
                int rowOffset = y * width;
                for (int x = 0; x < width; x++)
                {
                    uint pixel = pixels[rowOffset + x];
                    totalB += pixel & 0xFF;
                    totalG += (pixel >> 8) & 0xFF;
                    totalR += (pixel >> 16) & 0xFF;
                }
            }
        }

        return new SKColor(
            (byte)Math.Clamp(totalR / pixelCount, 0, 255),
            (byte)Math.Clamp(totalG / pixelCount, 0, 255),
            (byte)Math.Clamp(totalB / pixelCount, 0, 255)
        );
    }

    private static int FindClosestColorIndex(SKColor target, List<SKColor> colors)
    {
        int closestIndex = 0;
        double minDistance = double.MaxValue;
        for (int i = 0; i < colors.Count; i++)
        {
            double distance = Math.Pow(target.Red - colors[i].Red, 2) +
                              Math.Pow(target.Green - colors[i].Green, 2) +
                              Math.Pow(target.Blue - colors[i].Blue, 2);
            if (!(distance < minDistance)) 
                continue;
            minDistance = distance;
            closestIndex = i;
        }
        return closestIndex;
    }

    private async Task<string?> GetLowResBitmapPath()
    {
        return await TryGetLowResBitmapPath(5, 1000).ConfigureAwait(false);
    }

    private async Task<string?> TryGetLowResBitmapPath(int maxAttempts, int delayMilliseconds)
    {
        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                string? lowResBitmapPath = getLowResBackground?.GetLowResBitmapPath();
                if (!string.IsNullOrEmpty(lowResBitmapPath) && File.Exists(lowResBitmapPath))
                    return lowResBitmapPath;
            }
            catch
            {
                // ignored
            }

            await Task.Delay(delayMilliseconds).ConfigureAwait(false);
        }
        return null;
    }

    private static SKColor InterpolateColor(SKColor from, SKColor to, float t)
    {
        return new SKColor(
            (byte)(from.Red + (to.Red - from.Red) * t),
            (byte)(from.Green + (to.Green - from.Green) * t),
            (byte)(from.Blue + (to.Blue - from.Blue) * t),
            (byte)(from.Alpha + (to.Alpha - from.Alpha) * t)
        );
    }
}