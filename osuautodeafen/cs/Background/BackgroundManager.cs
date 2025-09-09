﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
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
    public BlurEffect? BackgroundBlurEffect;

    public double GetBackgroundOpacity()
    {
        return _currentBackgroundOpacity;
    }

    /// <summary>
    ///     Sets the background opacity, optionally animating the transition over a specified duration
    /// </summary>
    /// <param name="targetOpacity"></param>
    /// <param name="durationMs"></param>
    public async Task SetBackgroundOpacity(double targetOpacity, int durationMs = 0)
    {
        targetOpacity = Math.Clamp(targetOpacity, 0, 1);

        if (durationMs <= 0)
        {
            _currentBackgroundOpacity = targetOpacity;
            UpdateBackgroundLayerOpacity();
            return;
        }

        double start = _currentBackgroundOpacity;
        double end = targetOpacity;
        int steps = Math.Max(1, durationMs / 16);
        double step = (end - start) / steps;
        int delay = durationMs / steps;

        for (int i = 1; i <= steps; i++)
        {
            _currentBackgroundOpacity = start + step * i;
            UpdateBackgroundLayerOpacity();
            await Task.Delay(delay);
        }

        _currentBackgroundOpacity = end;
        UpdateBackgroundLayerOpacity();
    }

    /// <summary>
    ///     Updates the opacity of the background layer in the UI
    /// </summary>
    private void UpdateBackgroundLayerOpacity()
    {
        if (window.Content is Grid mainGrid)
        {
            Grid? backgroundLayer = mainGrid.Children.OfType<Grid>().FirstOrDefault(g => g.Name == "BackgroundLayer");
            if (backgroundLayer != null)
                backgroundLayer.Opacity = _currentBackgroundOpacity;
        }
    }

    /// <summary>
    ///     Loads and updates the background image based on the current settings and beatmap
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="e"></param>
    public async Task UpdateBackground(object? sender, EventArgs? e)
    {
        try
        {
            if (!viewModel.IsBackgroundEnabled)
            {
                window.NormalBackground?.SetValueSafe(x => x.IsVisible = false);
                return;
            }

            string backgroundPath = tosuApi.GetBackgroundPath();
            if (_currentBitmap == null || backgroundPath != _currentBackgroundDirectory)
            {
                _currentBackgroundDirectory = backgroundPath;
                Bitmap? newBitmap = await LoadBitmapAsync(backgroundPath);
                if (newBitmap == null || newBitmap.PixelSize.Width == 0 || newBitmap.PixelSize.Height == 0)
                {
                    Console.WriteLine($"Failed to load valid background: {backgroundPath}");
                    return;
                }

                _currentBitmap?.Dispose();
                _currentBitmap = newBitmap;

                await Dispatcher.UIThread.InvokeAsync(() => UpdateUIWithNewBackgroundAsync(_currentBitmap));
                if (_logoUpdater != null) await Dispatcher.UIThread.InvokeAsync(_logoUpdater.UpdateLogoAsync);
                _isBlackBackgroundDisplayed = false;
            }
            else
            {
                if (_currentBitmap == null || _currentBitmap.PixelSize.Width == 0 ||
                    _currentBitmap.PixelSize.Height == 0)
                {
                    Console.WriteLine("Current bitmap is null or invalid.");
                    return;
                }

                await Dispatcher.UIThread.InvokeAsync(() => UpdateUIWithNewBackgroundAsync(_currentBitmap));
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
                                    if (_currentBitmap != null && _currentBitmap.PixelSize.Width > 0 &&
                                        _currentBitmap.PixelSize.Height > 0)
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

    /// <summary>
    ///     Loads a bitmap from the specified file path asynchronously
    /// </summary>
    /// <param name="path"></param>
    /// <returns></returns>
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
                using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                return new Bitmap(stream);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to load bitmap from {path}: {ex.Message}");
                return null;
            }
        });
    }

    /// <summary>
    ///     Applies a blur effect to the background asynchronously
    /// </summary>
    /// <param name="blurEffect"></param>
    /// <param name="radius"></param>
    /// <param name="token"></param>
    public async Task BlurBackgroundAsync(BlurEffect blurEffect, double radius, CancellationToken token)
    {
        if (token.IsCancellationRequested)
            return;

        blurEffect.Radius = radius;
        await Task.CompletedTask;
    }

    /// <summary>
    ///     Calculates an appropriate downscale factor based on the desired blur radius
    /// </summary>
    /// <param name="sigma"></param>
    /// <returns>
    ///     A downscale factor between 0.1 and 1.0, where 1.0 means no downscaling
    /// </returns>
    /// <remarks>
    ///     Implementation for everything is largely based off of
    ///     https://github.com/ppy/osu/blob/master/osu.Game/Graphics/Backgrounds/Background.cs
    ///     (thanks peppy :D)
    /// </remarks>
    private double CalculateBlurDownscale(double sigma)
    {
        if (sigma <= 1)
            //Console.WriteLine($"CalculateBlurDownscale elapsed: {sw.ElapsedMilliseconds} ms");
            return 1;

        double scale = -0.18 * Math.Log(0.004 * sigma);
        double result = Math.Max(0.1, Math.Round(scale / 0.2, MidpointRounding.AwayFromZero) * 0.2);
        //Console.WriteLine($"CalculateBlurDownscale elapsed: {sw.ElapsedMilliseconds} ms");
        return result;
    }

    private async Task<Bitmap> CreateDownscaledBitmapAsync(Bitmap source, double scale)
    {
        if (scale >= 1.0)
            //Console.WriteLine($"CreateDownscaledBitmapAsync elapsed: {sw.ElapsedMilliseconds} ms");
            return source;

        if (_cachedDownscaledBitmap != null &&
            _cachedSourceBitmap == source &&
            Math.Abs(_cachedDownscale - scale) < 0.01)
            //Console.WriteLine($"CreateDownscaledBitmapAsync elapsed: {sw.ElapsedMilliseconds} ms (cached)");
            return _cachedDownscaledBitmap;

        _cachedDownscaledBitmap?.Dispose();

        int width = Math.Max(1, (int)(source.PixelSize.Width * scale));
        int height = Math.Max(1, (int)(source.PixelSize.Height * scale));

        RenderTargetBitmap target = await Task.Run(() =>
        {
            RenderTargetBitmap bmp = new(new PixelSize(width, height));
            using (DrawingContext ctx = bmp.CreateDrawingContext(false))
            {
                ctx.DrawImage(source, new Rect(0, 0, source.PixelSize.Width, source.PixelSize.Height),
                    new Rect(0, 0, width, height));
            }

            return bmp;
        });

        _cachedDownscaledBitmap = target;
        _cachedDownscale = scale;
        _cachedSourceBitmap = source;
        //Console.WriteLine($"CreateDownscaledBitmapAsync elapsed: {sw.ElapsedMilliseconds} ms");
        return target;
    }

    /// <summary>
    ///     Updates the UI with a new background image
    /// </summary>
    /// <param name="bitmap"></param>
    private async Task UpdateUIWithNewBackgroundAsync(Bitmap? bitmap)
    {
        if (bitmap == null || bitmap.PixelSize.Width == 0 || bitmap.PixelSize.Height == 0)
        {
            bitmap = _lastValidBitmap;
            if (bitmap == null || bitmap.PixelSize.Width == 0 || bitmap.PixelSize.Height == 0)
                return;
        }
        else
        {
            _lastValidBitmap = bitmap;
        }

        CancellationTokenSource?
            cts = Interlocked.Exchange(ref _cancellationTokenSource, new CancellationTokenSource());
        await cts?.CancelAsync()!;
        CancellationToken token = _cancellationTokenSource.Token;

        if (Dispatcher.UIThread.CheckAccess())
            await UpdateUI();
        else
            await Dispatcher.UIThread.InvokeAsync(UpdateUI);
        return;

        Task<Bitmap> ResizeBitmapCoverAsync(Bitmap source, int targetWidth, int targetHeight)
        {
            if (source.PixelSize.Width == targetWidth && source.PixelSize.Height == targetHeight)
                return Task.FromResult(source);

            double scale = Math.Max(
                (double)targetWidth / source.PixelSize.Width,
                (double)targetHeight / source.PixelSize.Height);

            int drawWidth = (int)(source.PixelSize.Width * scale);
            int drawHeight = (int)(source.PixelSize.Height * scale);

            int offsetX = (targetWidth - drawWidth) / 2;
            int offsetY = (targetHeight - drawHeight) / 2;

            RenderTargetBitmap resized = new(new PixelSize(targetWidth, targetHeight));
            using (DrawingContext ctx = resized.CreateDrawingContext(false))
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

            double blurRadius = viewModel?.BlurRadius ?? 0;
            double downscale = CalculateBlurDownscale(blurRadius);
            Bitmap displayBitmap = await CreateDownscaledBitmapAsync(bitmap, downscale);
            BackgroundBlurEffect ??= new BlurEffect();
            BackgroundBlurEffect.Radius = blurRadius;

            GpuBackgroundControl gpuBackground = new()
            {
                Bitmap = displayBitmap,
                Opacity = 0.5,
                ZIndex = -1,
                Stretch = Stretch.UniformToFill,
                Effect = BackgroundBlurEffect,
                Clip = new RectangleGeometry(new Rect(0, 0, 800 * 1.05, 800 * 1.05))
            };

            Grid backgroundLayer = mainGrid.Children.Count > 0 && mainGrid.Children[0] is Grid g &&
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

    /// <summary>
    ///     Animates the background back to the center position over a specified duration for parallax
    /// </summary>
    /// <param name="durationMs"></param>
    private async Task AnimateBackgroundToCenterAsync(int durationMs = 50)
    {
        if (_cachedGpuBackground == null)
            return;

        double startX = _lastMovementX;
        double startY = _lastMovementY;
        double endX = 0;
        double endY = 0;
        int steps = Math.Max(1, durationMs / 16);
        double stepX = (endX - startX) / steps;
        double stepY = (endY - startY) / steps;

        for (int i = 1; i <= steps; i++)
        {
            double newX = startX + stepX * i;
            double newY = startY + stepY * i;
            _cachedGpuBackground.RenderTransform = new TranslateTransform(newX, newY);
            await Task.Delay(durationMs / steps);
        }

        _cachedGpuBackground.RenderTransform = new TranslateTransform(0, 0);
        _lastMovementX = 0;
        _lastMovementY = 0;
    }

    /// <summary>
    ///     Applies a parallax effect to the background based on mouse position
    /// </summary>
    /// <param name="mouseX"></param>
    /// <param name="mouseY"></param>
    private void ApplyParallax(double mouseX, double mouseY)
    {
        try
        {
            if (_currentBitmap == null)
                return;

            if (window.Content is not Grid mainGrid)
                return;

            Grid? backgroundLayer = mainGrid.Children.OfType<Grid>().FirstOrDefault(g => g.Name == "BackgroundLayer");
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

            double centerX = window.Width / 2;
            double centerY = window.Height / 2;
            double movementX = -(mouseX - centerX) * 0.015;
            double movementY = -(mouseY - centerY) * 0.015;
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

    /// <summary>
    ///     Handles mouse movement events to update the parallax effect
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="e"></param>
    public void OnMouseMove(object? sender, PointerEventArgs e)
    {
        if (window.ParallaxToggle?.IsChecked == false || window.BackgroundToggle?.IsChecked == false)
            return;

        Point position = e.GetPosition(window);
        if (position.X < 0 || position.Y < 0 || position.X > window.Width || position.Y > window.Height)
            return;

        _mouseX = position.X;
        _mouseY = position.Y;

        ApplyParallax(_mouseX, _mouseY);
    }

    /// <summary>
    ///     Requests a change in background opacity with a given priority
    /// </summary>
    /// <param name="key"></param>
    /// <param name="opacity"></param>
    /// <param name="priority"></param>
    /// <param name="durationMs"></param>
    public async Task RequestBackgroundOpacity(string key, double opacity, int priority, int durationMs = 200)
    {
        _opacityRequests[key] = new OpacityRequest(key, opacity, priority);
        await ApplyHighestPriorityOpacity(durationMs);
    }

    /// <summary>
    ///     Removes a previously made background opacity request
    /// </summary>
    /// <param name="key"></param>
    public void RemoveBackgroundOpacityRequest(string key)
    {
        if (_opacityRequests.Remove(key)) _ = ApplyHighestPriorityOpacity(200);
    }

    /// <summary>
    ///     Applies the highest priority opacity request
    /// </summary>
    /// <param name="durationMs"></param>
    private async Task ApplyHighestPriorityOpacity(int durationMs)
    {
        if (_opacityRequests.Count == 0)
        {
            await SetBackgroundOpacity(1.0, durationMs);
            return;
        }

        OpacityRequest highest = _opacityRequests.Values.OrderByDescending(r => r.Priority).First();
        await SetBackgroundOpacity(highest.Opacity, durationMs);
    }

    /// <summary>
    ///     Requests a background overlay with specified color, opacity, and priority
    /// </summary>
    /// <param name="key"></param>
    /// <param name="overlayColor"></param>
    /// <param name="opacity"></param>
    /// <param name="priority"></param>
    /// <param name="durationMs"></param>
    [Obsolete]
    public async Task RequestBackgroundOverlay(string key, Color overlayColor, double opacity, int priority,
        int durationMs = 200)
    {
        _overlayRequests[key] = new BackgroundOverlayRequest(key, overlayColor, opacity, priority);
        await ApplyHighestPriorityOverlay(durationMs);
    }


    /// <summary>
    ///     Removes a previously made background overlay request
    /// </summary>
    /// <param name="key"></param>
    [Obsolete]
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

    /// <summary>
    ///     Applies the highest priority background overlay request
    /// </summary>
    /// <param name="durationMs"></param>
    private async Task ApplyHighestPriorityOverlay(int durationMs)
    {
        if (_overlayRequests.Count == 0)
        {
            await SetBackgroundOverlay(Colors.Transparent, 0.0, durationMs);
            return;
        }

        BackgroundOverlayRequest highest = _overlayRequests.Values.OrderByDescending(r => r.Priority).First();
        await SetBackgroundOverlay(highest.OverlayColor, highest.Opacity, durationMs, highest.Key);
    }

    /// <summary>
    ///     Sets the background overlay color and opacity, optionally animating the transition
    /// </summary>
    /// <param name="color"></param>
    /// <param name="opacity"></param>
    /// <param name="durationMs"></param>
    /// <param name="key"></param>
    private async Task SetBackgroundOverlay(Color color, double opacity, int durationMs, string? key = null)
    {
        if (key != null && _currentOverlayKey == key)
            _overlayAnimationCts?.Cancel();

        _overlayAnimationCts = new CancellationTokenSource();
        _currentOverlayKey = key;
        CancellationToken token = _overlayAnimationCts.Token;


        await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            if (window.Content is not Grid mainGrid)
                return;

            Grid? backgroundLayer = mainGrid.Children.OfType<Grid>().FirstOrDefault(g => g.Name == "BackgroundLayer");
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

            SolidColorBrush? brush = _backgroundOverlayRect.Fill as SolidColorBrush;
            if (brush == null)
            {
                brush = new SolidColorBrush(color, opacity);
                _backgroundOverlayRect.Fill = brush;
            }

            Color startColor = brush.Color;
            double startOpacity = brush.Opacity;
            Color endColor = color;
            double endOpacity = opacity;

            if (durationMs > 0)
            {
                int steps = Math.Max(1, durationMs / 16);
                for (int i = 1; i <= steps; i++)
                {
                    if (token.IsCancellationRequested) return;
                    double t = (double)i / steps;
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

    /// <summary>
    ///     Linearly interpolates between two colors based on a parameter t (0.0 to 1.0)
    /// </summary>
    /// <param name="a"></param>
    /// <param name="b"></param>
    /// <param name="t"></param>
    /// <returns></returns>
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