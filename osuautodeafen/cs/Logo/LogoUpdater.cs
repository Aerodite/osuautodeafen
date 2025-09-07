using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using osuautodeafen.cs.Log;
using SkiaSharp;
using Svg.Skia;

namespace osuautodeafen.cs.Logo;

public class LogoUpdater
{
    private readonly AnimationManager _animationManager;
    private readonly GetLowResBackground _getLowResBackground;
    private readonly Func<string, SKSvg> _loadHighResLogo;
    private readonly LogImportant _logImportant;
    private readonly LogoControl _logoControl;
    private readonly SharedViewModel _viewModel;
    private string? _cachedBitmapPath;
    private SKSvg? _cachedLogoSvg;
    private SKBitmap? _cachedSKBitmap;
    private CancellationTokenSource? _colorTransitionCts;

    private SKColor _currentColor;
    private Bitmap? _lowResBitmap;
    private SKColor _oldAverageColor;

    private List<SKColor> _sectionColors = new();
    private int _currentSectionIndex;
    private SKColor _lastRenderedColor = new(255, 255, 255, 255);
    public LogoUpdater(
        GetLowResBackground getLowResBackground,
        LogoControl logoControl,
        AnimationManager animationManager,
        SharedViewModel viewModel,
        Func<string, SKSvg> loadHighResLogo, LogImportant logImportant)
    {
        _getLowResBackground = getLowResBackground;
        _logoControl = logoControl;
        _animationManager = animationManager;
        _viewModel = viewModel;
        _loadHighResLogo = loadHighResLogo;
        _logImportant = logImportant;
    }

    public SKColor AverageColor { get; private set; }

    /// <summary>
    ///   Smoothly interpolates from one color to another over a set duration
    /// </summary>
    /// <param name="from"></param>
    /// <param name="to"></param>
    /// <param name="token"></param>
    private async Task InterpolateToFirstColorAsync(SKColor from, SKColor to, CancellationToken token)
    {
        int steps = 60, delay = 16;
        for (int i = 0; i <= steps; i++)
        {
            if (token.IsCancellationRequested) return;
            float t = i / (float)steps;
            var interpolatedColor = InterpolateColor(from, to, t);

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (_logoControl is { } skiaLogo)
                {
                    skiaLogo.ModulateColor = interpolatedColor;
                    skiaLogo.InvalidateVisual();
                }
                _viewModel.AverageColorBrush = new SolidColorBrush(
                    Color.FromArgb(interpolatedColor.Alpha, interpolatedColor.Red, interpolatedColor.Green, interpolatedColor.Blue));
                _lastRenderedColor = interpolatedColor;
            }, DispatcherPriority.Render);

            await Task.Delay(delay, token);
        }
    }
    
    /// <summary>
    ///   Updates the logo based on the current low-res background image
    /// </summary>
    public async Task UpdateLogoAsync()
    {
        try
        {
            var lowResBitmapPathTask = GetLowResBitmapPathAsync();
            var highResLogoTask = LoadHighResLogoAsync();

            var lowResBitmapPath = await lowResBitmapPathTask.ConfigureAwait(false);
            if (lowResBitmapPath == null) return;

            if (_cachedBitmapPath != lowResBitmapPath)
            {
                var lowResBitmap = await LoadLowResBitmapAsync(lowResBitmapPath).ConfigureAwait(false);
                _cachedBitmapPath = lowResBitmapPath;
                _cachedSKBitmap?.Dispose();
                _cachedSKBitmap = ConvertToSKBitmap(lowResBitmap);
            }

            if (_cachedSKBitmap == null) return;

            var highResLogoSvg = await highResLogoTask.ConfigureAwait(false);
            if (highResLogoSvg == null) return;
            _cachedLogoSvg = highResLogoSvg;

            var newSectionColors = CalculateSectionColors(_cachedSKBitmap);

            _colorTransitionCts?.Cancel();
            _colorTransitionCts = new CancellationTokenSource();
        
            int closestIndex = FindClosestColorIndex(_lastRenderedColor, newSectionColors);
            var firstColor = newSectionColors[closestIndex];
        
            await InterpolateToFirstColorAsync(_lastRenderedColor, firstColor, _colorTransitionCts.Token);

            _currentSectionIndex = closestIndex;
            _sectionColors = newSectionColors;

            _ = AnimateLogoColorsLoopAsync(_colorTransitionCts.Token);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERROR] Exception in UpdateLogoAsync: {ex}");
        }
    }
    /// <summary>
    ///  Continuously animates the logo colors by interpolating between section colors
    /// </summary>
    /// <param name="token"></param>
    private async Task AnimateLogoColorsLoopAsync(CancellationToken token)
    {
        if (_sectionColors.Count < 3) return;
        int steps = 120, delay = 16;

        while (!token.IsCancellationRequested)
        {
            var from = _sectionColors[_currentSectionIndex];
            var to = _sectionColors[(_currentSectionIndex + 1) % _sectionColors.Count];

            for (int i = 0; i <= steps; i++)
            {
                if (token.IsCancellationRequested) return;
                float t = i / (float)steps;
                var interpolatedColor = InterpolateColor(from, to, t);

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (_logoControl is { } skiaLogo)
                    {
                        skiaLogo.ModulateColor = interpolatedColor;
                        skiaLogo.InvalidateVisual();
                    }
                    _viewModel.AverageColorBrush = new SolidColorBrush(
                        Color.FromArgb(interpolatedColor.Alpha, interpolatedColor.Red, interpolatedColor.Green, interpolatedColor.Blue));
                    _lastRenderedColor = interpolatedColor;
                }, DispatcherPriority.Render);

                await Task.Delay(delay, token);
            }

            _currentSectionIndex = (_currentSectionIndex + 1) % _sectionColors.Count;
        }
    }

    /// <summary>
    ///  Divides the bitmap into three horizontal sections and calculates the average color for each section
    /// </summary>
    /// <param name="bitmap"></param>
    /// <returns></returns>
    private List<SKColor> CalculateSectionColors(SKBitmap bitmap)
    {
        int width = bitmap.Width, height = bitmap.Height;
        int sectionHeight = height / 3;
        var colors = new List<SKColor>();
        for (int section = 0; section < 3; section++)
        {
            int yStart = section * sectionHeight;
            int yEnd = (section == 2) ? height : yStart + sectionHeight;
            var color = CalculateAverageColor(bitmap, yStart, yEnd);
            
            byte max = Math.Max(color.Red, Math.Max(color.Green, color.Blue));
            if (max > 0)
            {
                float scale = 180f / max;
                color = new SKColor(
                    (byte)Math.Clamp(color.Red * scale, 32, 180),
                    (byte)Math.Clamp(color.Green * scale, 32, 180),
                    (byte)Math.Clamp(color.Blue * scale, 32, 180),
                    color.Alpha
                );
            }

            colors.Add(color);
        }
        return colors;
    }
    
    /// <summary>
    /// Calculates the average color of a specified section of the bitmap
    /// </summary>
    /// <param name="bitmap"></param>
    /// <param name="yStart"></param>
    /// <param name="yEnd"></param>
    /// <returns></returns>
    private unsafe SKColor CalculateAverageColor(SKBitmap bitmap, int yStart, int yEnd)
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
                for (int x = 0; x < width; x++)
                {
                    uint pixel = pixels[y * width + x];
                    totalB += pixel & 0xFF;
                    totalG += (pixel >> 8) & 0xFF;
                    totalR += (pixel >> 16) & 0xFF;
                }
            }
        }

        byte avgR = (byte)Math.Clamp(totalR / pixelCount, 0, 255);
        byte avgG = (byte)Math.Clamp(totalG / pixelCount, 0, 255);
        byte avgB = (byte)Math.Clamp(totalB / pixelCount, 0, 255);

        return new SKColor(avgR, avgG, avgB);
    }

    /// <summary>
    /// Finds the index of the color in the list that is closest to the target color
    /// </summary>
    /// <param name="target"></param>
    /// <param name="colors"></param>
    /// <returns></returns>
    private int FindClosestColorIndex(SKColor target, List<SKColor> colors)
    {
        int closestIndex = 0;
        double minDistance = double.MaxValue;
        for (int i = 0; i < colors.Count; i++)
        {
            double distance = Math.Pow(target.Red - colors[i].Red, 2) +
                              Math.Pow(target.Green - colors[i].Green, 2) +
                              Math.Pow(target.Blue - colors[i].Blue, 2);
            if (distance < minDistance)
            {
                minDistance = distance;
                closestIndex = i;
            }
        }
        return closestIndex;
    }
    
    /// <summary>
    ///  Attempts to get the path of the low-resolution bitmap, retrying if necessary
    /// </summary>
    /// <returns></returns>
    private async Task<string?> GetLowResBitmapPathAsync()
    {
        var lowResBitmapPath = await TryGetLowResBitmapPathAsync(5, 1000).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(lowResBitmapPath) || !File.Exists(lowResBitmapPath))
            return null;
        return lowResBitmapPath;
    }

    /// <summary>
    /// Loads a low-resolution bitmap from the specified path
    /// </summary>
    /// <param name="path"></param>
    /// <returns></returns>
    private async Task<Bitmap?> LoadLowResBitmapAsync(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 4096,
                FileOptions.Asynchronous);
            return await Task.Run(() => new Bitmap(stream)).ConfigureAwait(false);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Loads the high-resolution SVG logo
    /// </summary>
    /// <returns></returns>
    private async Task<SKSvg?> LoadHighResLogoAsync()
    {
        return await Task.Run(() =>
        {
            try
            {
                return _loadHighResLogo("osuautodeafen.Resources.autodeafen.svg");
            }
            catch
            {
                return null;
            }
        }).ConfigureAwait(false);
    }
    
    /// <summary>
    /// Tries to get the low-resolution bitmap path with retries
    /// </summary>
    /// <param name="maxAttempts"></param>
    /// <param name="delayMilliseconds"></param>
    /// <returns></returns>
    private async Task<string?> TryGetLowResBitmapPathAsync(int maxAttempts, int delayMilliseconds)
    {
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                var lowResBitmapPath = _getLowResBackground?.GetLowResBitmapPath();
                if (!string.IsNullOrEmpty(lowResBitmapPath))
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

    /// <summary>
    /// Converts an Avalonia Bitmap to a SkiaSharp SKBitmap
    /// </summary>
    /// <param name="avaloniaBitmap"></param>
    /// <returns></returns>
    public SKBitmap? ConvertToSKBitmap(Bitmap? avaloniaBitmap)
    {
        if (avaloniaBitmap == null) return null;
        var width = avaloniaBitmap.PixelSize.Width;
        var height = avaloniaBitmap.PixelSize.Height;
        if (width <= 0 || height <= 0) return null;

        SKBitmap? skBitmap = null;
        var pixelDataPtr = IntPtr.Zero;

        try
        {
            skBitmap = new SKBitmap(width, height);
            using (var renderTargetBitmap = new RenderTargetBitmap(new PixelSize(width, height), new Vector(96, 96)))
            {
                using (var drawingContext = renderTargetBitmap.CreateDrawingContext())
                {
                    drawingContext.DrawImage(avaloniaBitmap, new Rect(0, 0, width, height),
                        new Rect(0, 0, width, height));
                }

                var pixelDataSize = width * height * 4;
                pixelDataPtr = Marshal.AllocHGlobal(pixelDataSize);

                var rect = new PixelRect(0, 0, width, height);
                renderTargetBitmap.CopyPixels(rect, pixelDataPtr, pixelDataSize, width * 4);

                var pixelData = new byte[pixelDataSize];
                Marshal.Copy(pixelDataPtr, pixelData, 0, pixelDataSize);

                var destPtr = skBitmap.GetPixels();
                Marshal.Copy(pixelData, 0, destPtr, pixelDataSize);
            }

            return skBitmap;
        }
        catch
        {
            skBitmap?.Dispose();
            return null;
        }
        finally
        {
            if (pixelDataPtr != IntPtr.Zero)
                Marshal.FreeHGlobal(pixelDataPtr);
        }
    }

    /// <summary>
    /// Linearly interpolates between two SKColor values
    /// </summary>
    /// <param name="from"></param>
    /// <param name="to"></param>
    /// <param name="t"></param>
    /// <returns></returns>
    private SKColor InterpolateColor(SKColor from, SKColor to, float t)
    {
        byte InterpolateComponent(byte start, byte end, float factor)
        {
            return (byte)(start + (end - start) * factor);
        }

        var r = InterpolateComponent(from.Red, to.Red, t);
        var g = InterpolateComponent(from.Green, to.Green, t);
        var b = InterpolateComponent(from.Blue, to.Blue, t);
        var a = InterpolateComponent(from.Alpha, to.Alpha, t);
        return new SKColor(r, g, b, a);
    }
}