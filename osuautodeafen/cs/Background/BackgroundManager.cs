using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using osuautodeafen.cs.Logo;

namespace osuautodeafen.cs.Background;

public class BackgroundManager(MainWindow window, SharedViewModel viewModel, TosuApi tosuApi)
{
    private readonly Dictionary<string, OpacityRequest> _opacityRequests = new();
    private readonly Dictionary<string, BackgroundOverlayRequest> _overlayRequests = new();
    private readonly TimeSpan _parallaxInterval = TimeSpan.FromMilliseconds(16);
    public BlurEffect? _backgroundBlurEffect;

    private Rectangle? _backgroundOverlayRect;
    private PropertyChangedEventHandler? _backgroundPropertyChangedHandler;
    private double _cachedDownscale = 1.0;

    private Bitmap? _cachedDownscaledBitmap;

    private GpuBackgroundControl? _cachedGpuBackground;
    private Bitmap? _cachedSourceBitmap;
    private CancellationTokenSource _cancellationTokenSource = new();
    private string? _currentBackgroundDirectory;
    private double _currentBackgroundOpacity = 1.0;
    private Bitmap? _currentBitmap;

    private string? _currentOverlayKey;
    private bool _isBlackBackgroundDisplayed;
    private double _lastMovementX, _lastMovementY;
    private DateTime _lastUpdate = DateTime.MinValue;
    private Bitmap? _lastValidBitmap;
    public required LogoUpdater? _logoUpdater;
    private double _mouseX;
    private double _mouseY;
    private CancellationTokenSource? _overlayAnimationCts;

    public double GetBackgroundOpacity()
    {
        return _currentBackgroundOpacity;
    }

    public async Task SetBackgroundOpacity(double targetOpacity, int durationMs = 0)
    {
        targetOpacity = Math.Clamp(targetOpacity, 0, 1);

        if (durationMs <= 0)
        {
            _currentBackgroundOpacity = targetOpacity;
            UpdateBackgroundLayerOpacity();
            return;
        }

        var start = _currentBackgroundOpacity;
        var end = targetOpacity;
        var steps = Math.Max(1, durationMs / 16);
        var step = (end - start) / steps;
        var delay = durationMs / steps;

        for (var i = 1; i <= steps; i++)
        {
            _currentBackgroundOpacity = start + step * i;
            UpdateBackgroundLayerOpacity();
            await Task.Delay(delay);
        }

        _currentBackgroundOpacity = end;
        UpdateBackgroundLayerOpacity();
    }

    private void UpdateBackgroundLayerOpacity()
    {
        if (window.Content is Grid mainGrid)
        {
            var backgroundLayer = mainGrid.Children.OfType<Grid>().FirstOrDefault(g => g.Name == "BackgroundLayer");
            if (backgroundLayer != null)
                backgroundLayer.Opacity = _currentBackgroundOpacity;
        }
    }

    public async Task UpdateBackground(object? sender, EventArgs? e)
    {
        try
        {
            if (!viewModel.IsBackgroundEnabled)
            {
                window._blurredBackground?.SetValueSafe(x => x.IsVisible = false);
                window._normalBackground?.SetValueSafe(x => x.IsVisible = false);
                return;
            }

            var backgroundPath = tosuApi.GetBackgroundPath();
            if (_currentBitmap == null || backgroundPath != _currentBackgroundDirectory)
            {
                _currentBackgroundDirectory = backgroundPath;
                var newBitmap = await LoadBitmapAsync(backgroundPath);
                if (newBitmap == null)
                {
                    Console.WriteLine($"Failed to load background: {backgroundPath}");
                    return;
                }
                
                _currentBitmap?.Dispose();
                _currentBitmap = newBitmap;

                await Dispatcher.UIThread.InvokeAsync(() => UpdateUIWithNewBackgroundAsync(_currentBitmap));
                await Dispatcher.UIThread.InvokeAsync(_logoUpdater.UpdateLogoAsync);
                _isBlackBackgroundDisplayed = false;
            }

            if (_backgroundPropertyChangedHandler == null)
            {
                _backgroundPropertyChangedHandler = async void (s, args) =>
                {
                    try
                    {
                        switch (args.PropertyName)
                        {
                            case nameof(viewModel.IsBackgroundEnabled) when !viewModel.IsBackgroundEnabled:
                                if (!_isBlackBackgroundDisplayed)
                                {
                                    await RequestBackgroundOpacity("noBackground", 0.0, 100);
                                    _isBlackBackgroundDisplayed = true;
                                }

                                break;
                            case nameof(viewModel.IsBackgroundEnabled):
                                if (_isBlackBackgroundDisplayed)
                                {
                                    RemoveBackgroundOpacityRequest("noBackground");
                                    await Dispatcher.UIThread.InvokeAsync(() =>
                                        UpdateUIWithNewBackgroundAsync(_currentBitmap));
                                    _isBlackBackgroundDisplayed = false;
                                }

                                break;
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine("[ERROR] Exception in background property changed handler: " + ex);
                    }
                };
                viewModel.PropertyChanged += _backgroundPropertyChangedHandler;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("[ERROR] Exception in UpdateBackground: " + ex);
        }
    }
    
    private async Task<Bitmap?> LoadBitmapAsync(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            Console.WriteLine("LoadBitmapAsync: Provided path is null or empty.");
            return null;
        }

        return await Task.Run(() =>
        {
            if (!File.Exists(path))
            {
                Console.WriteLine($"Background file not found: {path}");
                return null;
            }

            try
            {
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                return new Bitmap(stream);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to load bitmap from {path}: {ex.Message}");
                return null;
            }
        });
    }
    
    public async Task BlurBackgroundAsync(BlurEffect blurEffect, double radius, CancellationToken token)
    {
        if (token.IsCancellationRequested)
            return;

        blurEffect.Radius = radius;
        await Task.CompletedTask;
    }

    // Implementation for everything below is largely based off of https://github.com/ppy/osu/blob/master/osu.Game/Graphics/Backgrounds/Background.cs
    // (thanks peppy :D)
    private double CalculateBlurDownscale(double sigma)
    {
        var sw = Stopwatch.StartNew();
        if (sigma <= 1)
        {
            sw.Stop();
            //Console.WriteLine($"CalculateBlurDownscale elapsed: {sw.ElapsedMilliseconds} ms");
            return 1;
        }

        var scale = -0.18 * Math.Log(0.004 * sigma);
        var result = Math.Max(0.1, Math.Round(scale / 0.2, MidpointRounding.AwayFromZero) * 0.2);
        sw.Stop();
        //Console.WriteLine($"CalculateBlurDownscale elapsed: {sw.ElapsedMilliseconds} ms");
        return result;
    }

    private async Task<Bitmap> CreateDownscaledBitmapAsync(Bitmap source, double scale)
    {
        var sw = Stopwatch.StartNew();
        if (scale >= 1.0)
        {
            sw.Stop();
            //Console.WriteLine($"CreateDownscaledBitmapAsync elapsed: {sw.ElapsedMilliseconds} ms");
            return source;
        }

        if (_cachedDownscaledBitmap != null &&
            _cachedSourceBitmap == source &&
            Math.Abs(_cachedDownscale - scale) < 0.01)
        {
            sw.Stop();
            //Console.WriteLine($"CreateDownscaledBitmapAsync elapsed: {sw.ElapsedMilliseconds} ms (cached)");
            return _cachedDownscaledBitmap;
        }

        _cachedDownscaledBitmap?.Dispose();

        var width = Math.Max(1, (int)(source.PixelSize.Width * scale));
        var height = Math.Max(1, (int)(source.PixelSize.Height * scale));

        var target = await Task.Run(() =>
        {
            var bmp = new RenderTargetBitmap(new PixelSize(width, height));
            using (var ctx = bmp.CreateDrawingContext(false))
            {
                ctx.DrawImage(source, new Rect(0, 0, source.PixelSize.Width, source.PixelSize.Height),
                    new Rect(0, 0, width, height));
            }

            return bmp;
        });

        _cachedDownscaledBitmap = target;
        _cachedDownscale = scale;
        _cachedSourceBitmap = source;
        sw.Stop();
        //Console.WriteLine($"CreateDownscaledBitmapAsync elapsed: {sw.ElapsedMilliseconds} ms");
        return target;
    }


    private async Task UpdateUIWithNewBackgroundAsync(Bitmap? bitmap)
    {
        if (bitmap == null)
        {
            bitmap = _lastValidBitmap;
            if (bitmap == null) return;
        }
        else
        {
            _lastValidBitmap = bitmap;
        }

        var cts = Interlocked.Exchange(ref _cancellationTokenSource, new CancellationTokenSource());
        await cts?.CancelAsync()!;
        var token = _cancellationTokenSource.Token;

        if (Dispatcher.UIThread.CheckAccess())
            await UpdateUI();
        else
            await Dispatcher.UIThread.InvokeAsync(UpdateUI);
        return;

        Task<Bitmap> ResizeBitmapCoverAsync(Bitmap source, int targetWidth, int targetHeight)
        {
            if (source.PixelSize.Width == targetWidth && source.PixelSize.Height == targetHeight)
                return Task.FromResult(source);

            var scale = Math.Max(
                (double)targetWidth / source.PixelSize.Width,
                (double)targetHeight / source.PixelSize.Height);

            var drawWidth = (int)(source.PixelSize.Width * scale);
            var drawHeight = (int)(source.PixelSize.Height * scale);

            var offsetX = (targetWidth - drawWidth) / 2;
            var offsetY = (targetHeight - drawHeight) / 2;

            var resized = new RenderTargetBitmap(new PixelSize(targetWidth, targetHeight));
            using (var ctx = resized.CreateDrawingContext(false))
            {
                ctx.DrawImage(
                    source,
                    new Rect(0, 0, source.PixelSize.Width, source.PixelSize.Height),
                    new Rect(offsetX, offsetY, drawWidth, drawHeight)
                );
            }

            return Task.FromResult<Bitmap>(resized);
        }

        async Task UpdateUI()
        {
            if (token.IsCancellationRequested) return;

            if (window.Content is not Grid mainGrid)
            {
                mainGrid = new Grid();
                window.Content = mainGrid;
            }
            
            var bounds = mainGrid.Bounds;
            var width = Math.Min(800, Math.Max(1, (int)Math.Ceiling(bounds.Width)));
            var height = Math.Min(800, Math.Max(1, (int)Math.Ceiling(bounds.Height)));
            
            var displayBitmap = await ResizeBitmapCoverAsync(bitmap, width, height);

            _backgroundBlurEffect ??= new BlurEffect();
            _backgroundBlurEffect.Radius = viewModel?.BlurRadius ?? 0;

            var gpuBackground = new GpuBackgroundControl
            {
                Bitmap = displayBitmap,
                Opacity = 0.5,
                ZIndex = -1,
                Stretch = Stretch.UniformToFill,
                Effect = _backgroundBlurEffect,
                Clip = new RectangleGeometry(new Rect(0, 0, 800 * 1.05, 800 * 1.05))
            };

            var backgroundLayer = mainGrid.Children.Count > 0 && mainGrid.Children[0] is Grid g &&
                                  g.Name == "BackgroundLayer"
                ? g
                : new Grid { Name = "BackgroundLayer", ZIndex = -1 };
            //this just ensures that parallax doesnt rubber band to the center of the screen.
            gpuBackground.RenderTransform = new TranslateTransform(_lastMovementX, _lastMovementY);
            if (!mainGrid.Children.Contains(backgroundLayer))
                mainGrid.Children.Insert(0, backgroundLayer);

            backgroundLayer.Children.Clear();
            backgroundLayer.RenderTransform = new ScaleTransform(1.05, 1.05);

            backgroundLayer.Children.Add(gpuBackground);
            backgroundLayer.Opacity = _currentBackgroundOpacity;

            if (viewModel?.IsParallaxEnabled == true && viewModel?.IsBackgroundEnabled == true)
                try
                {
                    ApplyParallax(_mouseX, _mouseY);
                }
                catch (Exception ex)
                {
                    Console.WriteLine("[ERROR] Exception in ApplyParallax: " + ex);
                }
            else
                ApplyParallax(315, 315);
        }
    }

    private async Task AnimateBackgroundToCenterAsync(int durationMs = 50)
    {
        if (_cachedGpuBackground == null)
            return;

        var startX = _lastMovementX;
        var startY = _lastMovementY;
        double endX = 0;
        double endY = 0;
        var steps = Math.Max(1, durationMs / 16);
        var stepX = (endX - startX) / steps;
        var stepY = (endY - startY) / steps;

        for (var i = 1; i <= steps; i++)
        {
            var newX = startX + stepX * i;
            var newY = startY + stepY * i;
            _cachedGpuBackground.RenderTransform = new TranslateTransform(newX, newY);
            await Task.Delay(durationMs / steps);
        }

        _cachedGpuBackground.RenderTransform = new TranslateTransform(0, 0);
        _lastMovementX = 0;
        _lastMovementY = 0;
    }

    
private void ApplyParallax(double mouseX, double mouseY)
{
    try
    {
        if (_currentBitmap == null)
            return;

        // Cache mainGrid and backgroundLayer for reuse
        if (window.Content is not Grid mainGrid)
            return;

        var backgroundLayer = mainGrid.Children.OfType<Grid>().FirstOrDefault(g => g.Name == "BackgroundLayer");
        if (backgroundLayer == null)
            return;

        if (window.ParallaxToggle?.IsChecked == false || window.BackgroundToggle?.IsChecked == false)
        {
            if (_cachedGpuBackground == null || !backgroundLayer.Children.Contains(_cachedGpuBackground))
                _cachedGpuBackground = backgroundLayer.Children.OfType<GpuBackgroundControl>().FirstOrDefault();

            if (_cachedGpuBackground != null && (Math.Abs(_lastMovementX) > 0.1 || Math.Abs(_lastMovementY) > 0.1))
                _ = AnimateBackgroundToCenterAsync();

            return;
        }

        if (mouseX < 0 || mouseY < 0 || mouseX > window.Width || mouseY > window.Height)
            return;

        if (DateTime.UtcNow - _lastUpdate < _parallaxInterval)
            return;
        _lastUpdate = DateTime.UtcNow;

        if (_cachedGpuBackground == null || !backgroundLayer.Children.Contains(_cachedGpuBackground))
            _cachedGpuBackground = backgroundLayer.Children.OfType<GpuBackgroundControl>().FirstOrDefault();

        if (_cachedGpuBackground == null)
            return;

        var centerX = window.Width / 2;
        var centerY = window.Height / 2;
        var movementX = -(mouseX - centerX) * 0.015;
        var movementY = -(mouseY - centerY) * 0.015;
        movementX = Math.Clamp(movementX, -15, 15);
        movementY = Math.Clamp(movementY, -15, 15);

        // Only update if movement changed
        if (Math.Abs(_lastMovementX - movementX) > 0.01 || Math.Abs(_lastMovementY - movementY) > 0.01)
        {
            _cachedGpuBackground.RenderTransform = new TranslateTransform(movementX, movementY);
            _lastMovementX = movementX;
            _lastMovementY = movementY;
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine("[ERROR] Exception in ApplyParallax: " + ex);
    }
}

    public void OnMouseMove(object? sender, PointerEventArgs e)
    {
        if (window.ParallaxToggle?.IsChecked == false || window.BackgroundToggle?.IsChecked == false)
            return;

        var position = e.GetPosition(window);
        if (position.X < 0 || position.Y < 0 || position.X > window.Width || position.Y > window.Height)
            return;

        _mouseX = position.X;
        _mouseY = position.Y;

        ApplyParallax(_mouseX, _mouseY);
    }

    public async Task RequestBackgroundOpacity(string key, double opacity, int priority, int durationMs = 200)
    {
        Console.WriteLine(
            $"[Opacity] Request: key={key}, opacity={opacity}, priority={priority}, durationMs={durationMs}");
        _opacityRequests[key] = new OpacityRequest(key, opacity, priority);
        await ApplyHighestPriorityOpacity(durationMs);
    }

    public void RemoveBackgroundOpacityRequest(string key)
    {
        if (_opacityRequests.Remove(key))
        {
            Console.WriteLine($"[Opacity] Remove request: key={key}");
            _ = ApplyHighestPriorityOpacity(200);
        }
        else
        {
            Console.WriteLine($"[Opacity] Remove request: key={key} (not found)");
        }
    }

    private async Task ApplyHighestPriorityOpacity(int durationMs)
    {
        if (_opacityRequests.Count == 0)
        {
            Console.WriteLine("[Opacity] No requests, setting opacity to 1.0");
            await SetBackgroundOpacity(1.0, durationMs);
            return;
        }

        var highest = _opacityRequests.Values.OrderByDescending(r => r.Priority).First();
        Console.WriteLine(
            $"[Opacity] Applying highest priority: opacity={highest.Opacity}, priority={highest.Priority}, durationMs={durationMs}");
        await SetBackgroundOpacity(highest.Opacity, durationMs);
    }

    public async Task RequestBackgroundOverlay(string key, Color overlayColor, double opacity, int priority,
        int durationMs = 200)
    {
        Console.WriteLine(
            $"[Overlay] Request: key={key}, color={overlayColor}, opacity={opacity}, priority={priority}, durationMs={durationMs}");
        _overlayRequests[key] = new BackgroundOverlayRequest(key, overlayColor, opacity, priority);
        await ApplyHighestPriorityOverlay(durationMs);
    }

    public void RemoveBackgroundOverlayRequest(string key)
    {
        if (_overlayRequests.Remove(key))
        {
            Console.WriteLine($"[Overlay] Remove request: key={key}");
            _ = ApplyHighestPriorityOverlay(200);
        }
        else
        {
            Console.WriteLine($"[Overlay] Remove request: key={key} (not found)");
        }
    }

    private async Task ApplyHighestPriorityOverlay(int durationMs)
    {
        if (_overlayRequests.Count == 0)
        {
            Console.WriteLine("[Overlay] No requests, setting overlay to transparent");
            await SetBackgroundOverlay(Colors.Transparent, 0.0, durationMs);
            return;
        }

        var highest = _overlayRequests.Values.OrderByDescending(r => r.Priority).First();
        Console.WriteLine(
            $"[Overlay] Applying highest priority: color={highest.OverlayColor}, opacity={highest.Opacity}, priority={highest.Priority}, durationMs={durationMs}");
        await SetBackgroundOverlay(highest.OverlayColor, highest.Opacity, durationMs, highest.Key);
    }

    private async Task SetBackgroundOverlay(Color color, double opacity, int durationMs, string? key = null)
    {
        if (key != null && _currentOverlayKey == key)
            _overlayAnimationCts?.Cancel();

        _overlayAnimationCts = new CancellationTokenSource();
        _currentOverlayKey = key;
        var token = _overlayAnimationCts.Token;


        await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            if (window.Content is not Grid mainGrid)
                return;

            var backgroundLayer = mainGrid.Children.OfType<Grid>().FirstOrDefault(g => g.Name == "BackgroundLayer");
            if (backgroundLayer == null)
                return;

            if (_backgroundOverlayRect == null)
            {
                _backgroundOverlayRect = new Rectangle
                {
                    Fill = new SolidColorBrush(color, opacity),
                    IsHitTestVisible = false,
                    ZIndex = 1000
                };
                backgroundLayer.Children.Add(_backgroundOverlayRect);
            }

            _backgroundOverlayRect.Width = backgroundLayer.Bounds.Width;
            _backgroundOverlayRect.Height = backgroundLayer.Bounds.Height;

            var brush = _backgroundOverlayRect.Fill as SolidColorBrush;
            if (brush == null)
            {
                brush = new SolidColorBrush(color, opacity);
                _backgroundOverlayRect.Fill = brush;
            }

            var startColor = brush.Color;
            var startOpacity = brush.Opacity;
            var endColor = color;
            var endOpacity = opacity;

            if (durationMs > 0)
            {
                var steps = Math.Max(1, durationMs / 16);
                for (var i = 1; i <= steps; i++)
                {
                    if (token.IsCancellationRequested) return;
                    var t = (double)i / steps;
                    brush.Color = LerpColor(startColor, endColor, t);
                    brush.Opacity = startOpacity + (endOpacity - startOpacity) * t;
                    await Task.Delay(durationMs / steps);
                }
            }
            else
            {
                brush.Color = color;
                brush.Opacity = opacity;
            }

            _backgroundOverlayRect.IsVisible = opacity > 0.01;
        });
    }

    private static Color LerpColor(Color a, Color b, double t)
    {
        return Color.FromArgb(
            (byte)(a.A + (b.A - a.A) * t),
            (byte)(a.R + (b.R - a.R) * t),
            (byte)(a.G + (b.G - a.G) * t),
            (byte)(a.B + (b.B - a.B) * t)
        );
    }

    private class OpacityRequest(string key, double opacity, int priority)
    {
        public string Key { get; } = key;
        public double Opacity { get; } = opacity;
        public int Priority { get; } = priority;
    }

    private class BackgroundOverlayRequest
    {
        public BackgroundOverlayRequest(string key, Color overlayColor, double opacity, int priority)
        {
            Key = key;
            OverlayColor = overlayColor;
            Opacity = opacity;
            Priority = priority;
        }

        public string Key { get; }
        public Color OverlayColor { get; }
        public double Opacity { get; }
        public int Priority { get; }
    }
}