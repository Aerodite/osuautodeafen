using System;
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
    private SKSvg? _cachedLogoSvg;
    private CancellationTokenSource? _colorTransitionCts;

    private SKColor _currentColor;
    private Bitmap? _lowResBitmap;
    private SKColor _oldAverageColor;
    private string? _cachedBitmapPath;
    private SKBitmap? _cachedSKBitmap;

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
                _lowResBitmap = await LoadLowResBitmapAsync(lowResBitmapPath).ConfigureAwait(false);
                _cachedBitmapPath = lowResBitmapPath;
                _cachedSKBitmap?.Dispose();
                _cachedSKBitmap = ConvertToSKBitmap(_lowResBitmap);
            }

            if (_cachedSKBitmap == null) return;

            var highResLogoSvg = await highResLogoTask.ConfigureAwait(false);
            if (highResLogoSvg == null) return;
            _cachedLogoSvg = highResLogoSvg;

            var newAverageColor = await CalculateAverageColorAsync(_cachedSKBitmap).ConfigureAwait(false);

            if (_oldAverageColor == default)
                _oldAverageColor = SKColors.White;

            if (_oldAverageColor == newAverageColor)
                return;

            await UpdateAverageColorAsync(newAverageColor);
            await AnimateLogoColorAsync(newAverageColor);

            _oldAverageColor = newAverageColor;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERROR] Exception in UpdateLogoAsync: {ex}");
        }
    }

    private async Task<string?> GetLowResBitmapPathAsync()
    {
        var lowResBitmapPath = await TryGetLowResBitmapPathAsync(5, 1000).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(lowResBitmapPath) || !File.Exists(lowResBitmapPath))
            return null;
        return lowResBitmapPath;
    }

    private async Task<Bitmap?> LoadLowResBitmapAsync(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 4096, FileOptions.Asynchronous);
            return await Task.Run(() => new Bitmap(stream)).ConfigureAwait(false);
        }
        catch
        {
            return null;
        }
    }

    private async Task<SKSvg?> LoadHighResLogoAsync()
    {
        return await Task.Run(() =>
        {
            try { return _loadHighResLogo("osuautodeafen.Resources.autodeafen.svg"); }
            catch { return null; }
        }).ConfigureAwait(false);
    }

    private async Task AnimateLogoColorAsync(SKColor newAverageColor)
    {
        _colorTransitionCts?.Cancel();
        _colorTransitionCts = new CancellationTokenSource();
        var token = _colorTransitionCts.Token;

        await _animationManager.EnqueueAnimation(async () =>
        {
            if (_cachedLogoSvg?.Picture == null) return;

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (_logoControl is { } skiaLogo && skiaLogo.Svg != _cachedLogoSvg)
                    skiaLogo.Svg = _cachedLogoSvg;
                _viewModel.ModifiedLogoImage = null;
            });

            const int steps = 10, delay = 16;
            var fromColor = _currentColor == default ? _oldAverageColor : _currentColor;
            var toColor = newAverageColor;

            for (var i = 0; i <= steps; i++)
            {
                if (token.IsCancellationRequested) return;
                var t = i / (float)steps;
                var interpolatedColor = InterpolateColor(fromColor, toColor, t);
                _currentColor = interpolatedColor;

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (_logoControl is { } skiaLogo)
                    {
                        skiaLogo.ModulateColor = interpolatedColor;
                        skiaLogo.InvalidateVisual();
                    }
                }, DispatcherPriority.Render);

                await Task.Delay(delay, token).ContinueWith(_ => { });
            }

            _oldAverageColor = toColor;
            _currentColor = toColor;
        }).ConfigureAwait(false);
    }

    private async Task<SKColor> CalculateAverageColorAsync(SKBitmap bitmap)
    {
        return await Task.Run(() => CalculateAverageColor(bitmap));
    }

    public async Task UpdateAverageColorAsync(SKColor newColor)
    {
        _colorTransitionCts?.Cancel();
        _colorTransitionCts = new CancellationTokenSource();
        var token = _colorTransitionCts.Token;

        const int steps = 10, delay = 16;
        var fromColor = _currentColor == default ? _oldAverageColor : _currentColor;
        var toColor = newColor;

        for (var i = 0; i <= steps; i++)
        {
            if (token.IsCancellationRequested) return;
            var t = i / (float)steps;
            var interpolatedColor = InterpolateColor(fromColor, toColor, t);
            _currentColor = interpolatedColor;
            _logImportant.logImportant("Average Color: " + interpolatedColor, false, "AverageColor");
            var avaloniaColor = Color.FromArgb(interpolatedColor.Alpha, interpolatedColor.Red, interpolatedColor.Green, interpolatedColor.Blue);

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                _viewModel.AverageColorBrush = new SolidColorBrush(avaloniaColor);
                if (_logoControl is { } skiaLogo)
                {
                    skiaLogo.ModulateColor = interpolatedColor;
                    skiaLogo.InvalidateVisual();
                }
            }, DispatcherPriority.Render);

            await Task.Delay(delay, token);
        }

        _oldAverageColor = toColor;
        _currentColor = toColor;
    }

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
                    drawingContext.DrawImage(avaloniaBitmap, new Rect(0, 0, width, height), new Rect(0, 0, width, height));
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

    private unsafe SKColor CalculateAverageColor(SKBitmap bitmap)
    {
        int width = bitmap.Width, height = bitmap.Height;
        long totalR = 0, totalG = 0, totalB = 0;
        var pixelCount = (long)width * height;

        if (!bitmap.IsImmutable)
            bitmap.SetImmutable();

        fixed (void* ptr = &bitmap.GetPixelSpan()[0])
        {
            var pixels = (uint*)ptr;
            var length = (int)pixelCount;
            var processorCount = Environment.ProcessorCount;
            var rSums = new long[processorCount];
            var gSums = new long[processorCount];
            var bSums = new long[processorCount];

            Parallel.For(0, processorCount, workerId =>
            {
                var start = workerId * length / processorCount;
                var end = (workerId + 1) * length / processorCount;
                long r = 0, g = 0, b = 0;
                for (var i = start; i < end; i++)
                {
                    var pixel = pixels[i];
                    b += pixel & 0xFF;
                    g += (pixel >> 8) & 0xFF;
                    r += (pixel >> 16) & 0xFF;
                }
                rSums[workerId] = r;
                gSums[workerId] = g;
                bSums[workerId] = b;
            });

            for (var i = 0; i < processorCount; i++)
            {
                totalR += rSums[i];
                totalG += gSums[i];
                totalB += bSums[i];
            }
        }

        var avgR = (byte)Math.Clamp(totalR / pixelCount, 0, 255);
        var avgG = (byte)Math.Clamp(totalG / pixelCount, 0, 255);
        var avgB = (byte)Math.Clamp(totalB / pixelCount, 0, 255);

        return new SKColor(avgR, avgG, avgB);
    }

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