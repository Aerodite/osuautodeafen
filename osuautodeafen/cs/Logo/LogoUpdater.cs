using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using SkiaSharp;
using Svg.Skia;

namespace osuautodeafen.cs.Logo;

public class LogoUpdater
{
    private readonly SharedViewModel _viewModel;
    private readonly GetLowResBackground _getLowResBackground;
    private readonly LogoControl _logoControl;
    private readonly AnimationManager _animationManager;

    private Bitmap? _lowResBitmap;
    private SKSvg? _cachedLogoSvg;
    private CancellationTokenSource? _colorTransitionCts;
    private SKColor _oldAverageColor;
    private readonly Func<string, SKSvg> _loadHighResLogo;

    public LogoUpdater(
        GetLowResBackground getLowResBackground,
        LogoControl logoControl,
        AnimationManager animationManager,
        SharedViewModel viewModel,
        Func<string, SKSvg> loadHighResLogo)
    {
        _getLowResBackground = getLowResBackground;
        _logoControl = logoControl;
        _animationManager = animationManager;
        _viewModel = viewModel;
        _loadHighResLogo = loadHighResLogo;
    }

    #region Public API
    
    public void SetOldAverageColor(SKColor color)
    {
        _oldAverageColor = color;
    }

    //TODO
    //make this actually slowly extrapolate the color again im just lazy
public async Task UpdateLogoAsync()
{
    Console.WriteLine("UpdateLogoAsync started");
    if (_getLowResBackground == null)
    {
        Console.WriteLine("[ERROR] _getLowResBackground is null");
        return;
    }

    try
    {
        Console.WriteLine("Starting lowResBitmapPathTask and highResLogoTask");
        var lowResBitmapPathTask = TryGetLowResBitmapPathAsync(5, 1000);
        var highResLogoTask = Task.Run(() =>
        {
            try
            {
                Console.WriteLine("Loading high-res logo");
                return _loadHighResLogo("osuautodeafen.Resources.autodeafen.svg");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] Exception while loading high-resolution logo: {ex.Message}");
                return null;
            }
        });

        var lowResBitmapPath = await lowResBitmapPathTask.ConfigureAwait(false);
        Console.WriteLine($"lowResBitmapPath: {lowResBitmapPath}");
        if (string.IsNullOrWhiteSpace(lowResBitmapPath) || !File.Exists(lowResBitmapPath))
        {
            Console.WriteLine("[ERROR] Low-resolution bitmap path is invalid or does not exist");
            return;
        }

        Bitmap? lowResBitmap;
        try
        {
            Console.WriteLine("Opening low-res bitmap file");
            using var stream = new FileStream(lowResBitmapPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            lowResBitmap = new Bitmap(stream);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERROR] Failed to load low-resolution bitmap: {ex.Message}");
            return;
        }

        if (lowResBitmap == null)
        {
            Console.WriteLine("[ERROR] Low-resolution bitmap is null after loading");
            return;
        }

        _lowResBitmap = lowResBitmap;
        Console.WriteLine("Low resolution bitmap successfully loaded");

        var highResLogoSvg = await highResLogoTask.ConfigureAwait(false);
        Console.WriteLine($"highResLogoSvg loaded: {highResLogoSvg != null}");
        if (highResLogoSvg?.Picture == null)
        {
            Console.WriteLine("[ERROR] Failed to load high-resolution logo or picture is null");
            return;
        }

        _cachedLogoSvg = highResLogoSvg;

        Console.WriteLine("Converting to SKBitmap");
        using var skBitmap = ConvertToSKBitmap(_lowResBitmap);
        if (skBitmap == null)
        {
            Console.WriteLine("[ERROR] Failed to convert bitmap for color calculation");
            return;
        }

        Console.WriteLine("Calculating average color");
        var newAverageColor = await CalculateAverageColorAsync(skBitmap).ConfigureAwait(false);
        Console.WriteLine($"newAverageColor: {newAverageColor}");

        // Only animate the brush color if the color changed
        if (_oldAverageColor == newAverageColor)
        {
            Console.WriteLine("Average color unchanged, skipping animation");
            return;
        }

        Console.WriteLine("Updating average color brush");
        await UpdateAverageColorAsync(newAverageColor);

        Console.WriteLine("Enqueuing color animation");
        await _animationManager.EnqueueAnimation(async () =>
        {
            if (_cachedLogoSvg?.Picture == null)
            {
                Console.WriteLine("[ERROR] Cached logo SVG or its picture is null");
                return;
            }

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (_logoControl is { } skiaLogo)
                    skiaLogo.Svg = _cachedLogoSvg;
                _viewModel.ModifiedLogoImage = null;
            });

            const int steps = 10;
            const int delay = 16;
            var fromColor = _oldAverageColor; 
            var toColor = newAverageColor;

            for (var i = 0; i <= steps; i++)
            {
                var t = i / (float)steps;
                var interpolatedColor = InterpolateColor(fromColor, toColor, t);

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (_logoControl is { } skiaLogo)
                    {
                        Console.WriteLine($"Setting ModulateColor: {interpolatedColor}");
                        skiaLogo.ModulateColor = interpolatedColor;
                        skiaLogo.InvalidateVisual();
                    }
                }, DispatcherPriority.Render);

                await Task.Delay(delay).ConfigureAwait(false);
            }

            _oldAverageColor = newAverageColor; // Only update after animation
            Console.WriteLine("Color animation complete");
        }).ConfigureAwait(false);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[ERROR] Exception in UpdateLogoAsync: {ex}");
    }
    Console.WriteLine("UpdateLogoAsync finished");
}

    private async Task<SKColor> CalculateAverageColorAsync(SKBitmap bitmap)
    {
        return await Task.Run(() => CalculateAverageColor(bitmap));
    }

    private Bitmap? CreateBlackBitmap(int width = 600, int height = 600)
    {
        var renderTargetBitmap = new RenderTargetBitmap(new PixelSize(width, height), new Vector(96, 96));
        using (var drawingContext = renderTargetBitmap.CreateDrawingContext())
        {
            var rect = new Rect(0, 0, width, height);
            var brush = new SolidColorBrush(Colors.Black);
            drawingContext.FillRectangle(brush, rect);
        }

        using (var stream = new MemoryStream())
        {
            renderTargetBitmap.Save(stream);
            stream.Position = 0; // reset stream position to the beginning

            return new Bitmap(stream);
        }
    }

    public async Task UpdateAverageColorAsync(SKColor newColor)
    {
        _colorTransitionCts?.Cancel();
        _colorTransitionCts = new CancellationTokenSource();
        var token = _colorTransitionCts.Token;

        const int steps = 10;
        const int delay = 16;

        var from = _oldAverageColor;
        var to = newColor;

        try
        {
            for (var i = 0; i <= steps; i++)
            {
                token.ThrowIfCancellationRequested();
                var t = i / (float)steps;
                var interpolated = InterpolateColor(from, to, t);
                var avaloniaColor = Color.FromArgb(interpolated.Alpha, interpolated.Red, interpolated.Green, interpolated.Blue);

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    _viewModel.AverageColorBrush = new SolidColorBrush(avaloniaColor);
                });

                await Task.Delay(delay, token);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on cancellation
        }

        _oldAverageColor = newColor;
    }

    #endregion

    #region Helpers

    private async Task<string?> TryGetLowResBitmapPathAsync(int maxAttempts, int delayMilliseconds)
    {
        if (maxAttempts <= 0) throw new ArgumentOutOfRangeException(nameof(maxAttempts));
        if (delayMilliseconds < 0) throw new ArgumentOutOfRangeException(nameof(delayMilliseconds));

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                var lowResBitmapPath = _getLowResBackground?.GetLowResBitmapPath();
                if (!string.IsNullOrEmpty(lowResBitmapPath))
                    return lowResBitmapPath;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[WARN] Exception on attempt {attempt}: {ex.Message}");
            }

            Console.WriteLine($"Attempt {attempt} failed. Retrying in {delayMilliseconds}ms...");
            await Task.Delay(delayMilliseconds).ConfigureAwait(false);
        }

        Console.WriteLine("[ERROR] Failed to get low resolution bitmap path after multiple attempts.");
        return null;
    }

    public SKBitmap? ConvertToSKBitmap(Bitmap? avaloniaBitmap)
    {
        if (avaloniaBitmap == null)
            return null;

        var width = avaloniaBitmap.PixelSize.Width;
        var height = avaloniaBitmap.PixelSize.Height;

        if (width <= 0 || height <= 0)
            return null;

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
        catch (Exception ex)
        {
            Console.WriteLine($"ConvertToSKBitmap failed: {ex.Message}");
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
        if (bitmap == null) throw new ArgumentNullException(nameof(bitmap));
        int width = bitmap.Width, height = bitmap.Height;
        if (width == 0 || height == 0) throw new ArgumentException("Bitmap dimensions cannot be zero");

        long totalR = 0, totalG = 0, totalB = 0;
        var pixelCount = (long)width * height;
        
        var info = bitmap.Info;
        if (!bitmap.IsImmutable)
            bitmap.SetImmutable(); // Ensure safe access

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
        if (t < 0f || t > 1f)
            throw new ArgumentOutOfRangeException(nameof(t), "Interpolation factor must be between 0 and 1");

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


    #endregion
}