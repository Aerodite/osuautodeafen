using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using osuautodeafen.cs.Logo;

namespace osuautodeafen.cs.Background;

public class BackgroundManager(MainWindow window, SharedViewModel viewModel, TosuApi tosuApi)
{
    private string? _currentBackgroundDirectory;
    private Bitmap? _currentBitmap;
    private Bitmap? _lastValidBitmap;
    private BlurEffect? _backgroundBlurEffect;
    private CancellationTokenSource _cancellationTokenSource = new();
    private PropertyChangedEventHandler? _backgroundPropertyChangedHandler;
    private bool _isBlackBackgroundDisplayed;
    private double _currentBackgroundOpacity = 1.0;
    private double _mouseX;
    private double _mouseY;
    public LogoUpdater _logoUpdater;
    private double _targetOpacity = 1.0;
    private readonly Dictionary<string, OpacityRequest> _opacityRequests = new();
    
    public double GetBackgroundOpacity() => _currentBackgroundOpacity;

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
                            case nameof(viewModel.IsParallaxEnabled):
                            case nameof(viewModel.IsBlurEffectEnabled):
                                await Dispatcher.UIThread.InvokeAsync(() => UpdateUIWithNewBackgroundAsync(_currentBitmap));
                                break;
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

            UpdateBackgroundVisibility();
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

    private Task AnimateBlurAsync(BlurEffect blurEffect, double from, double to, int durationMs, CancellationToken token)
    {
        var tcs = new TaskCompletionSource();
        const int steps = 10;
        var step = (to - from) / steps;
        var delay = durationMs / steps;
        int i = 0;
        double lastRadius = blurEffect.Radius;

        IDisposable? timer = null;
        timer = DispatcherTimer.Run(() =>
        {
            if (token.IsCancellationRequested)
            {
                timer?.Dispose();
                tcs.TrySetCanceled(token);
                return false;
            }

            double radius = from + step * i;
            if (Math.Abs(radius - lastRadius) > 0.01)
            {
                blurEffect.Radius = radius;
                lastRadius = radius;
            }

            i++;
            if (i > steps)
            {
                blurEffect.Radius = to;
                timer?.Dispose();
                tcs.TrySetResult();
                return false;
            }
            return true;
        }, TimeSpan.FromMilliseconds(delay));

        return tcs.Task;
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

        async Task UpdateUI()
        {
            if (token.IsCancellationRequested) return;

            if (window.Content is not Grid mainGrid)
            {
                mainGrid = new Grid();
                window.Content = mainGrid;
            }

            var bounds = mainGrid.Bounds;
            var width = Math.Max(1, bounds.Width);
            var height = Math.Max(1, bounds.Height);

            _backgroundBlurEffect ??= new BlurEffect();
            var currentRadius = _backgroundBlurEffect.Radius;
            var targetRadius = viewModel?.IsBlurEffectEnabled == true ? 15 : 0;

            var gpuBackground = new GpuBackgroundControl
            {
                Bitmap = bitmap,
                Opacity = 0.5,
                ZIndex = -1,
                Stretch = Stretch.UniformToFill,
                Effect = _backgroundBlurEffect,
                Clip = new RectangleGeometry(new Rect(0, 0, width * 1.05, height * 1.05))
            };

            var backgroundLayer = mainGrid.Children.Count > 0 && mainGrid.Children[0] is Grid g &&
                                  g.Name == "BackgroundLayer"
                ? g
                : new Grid { Name = "BackgroundLayer", ZIndex = -1 };
            if (!mainGrid.Children.Contains(backgroundLayer))
                mainGrid.Children.Insert(0, backgroundLayer);

            backgroundLayer.Children.Clear();
            backgroundLayer.RenderTransform = new ScaleTransform(1.05, 1.05);

            backgroundLayer.Children.Add(gpuBackground);
            backgroundLayer.Opacity = _currentBackgroundOpacity;

            if (viewModel?.IsParallaxEnabled == true)
                try
                {
                    ApplyParallax(_mouseX, _mouseY);
                }
                catch (Exception ex)
                {
                    Console.WriteLine("[ERROR] Exception in ApplyParallax: " + ex);
                }

            await AnimateBlurAsync(_backgroundBlurEffect, currentRadius, targetRadius, 150, token);
        }
    }

    private void ApplyParallax(double mouseX, double mouseY)
    {
        if (_currentBitmap == null || window.ParallaxToggle.IsChecked == false || window.BackgroundToggle.IsChecked == false) return;
        if (mouseX < 0 || mouseY < 0 || mouseX > window.Width || mouseY > window.Height) return;

        var windowWidth = window.Width;
        var windowHeight = window.Height;
        var centerX = windowWidth / 2;
        var centerY = windowHeight / 2;

        var relativeMouseX = mouseX - centerX;
        var relativeMouseY = mouseY - centerY;

        var scaleFactor = 0.015;
        var movementX = -(relativeMouseX * scaleFactor);
        var movementY = -(relativeMouseY * scaleFactor);

        double maxMovement = 15;
        movementX = Math.Max(-maxMovement, Math.Min(maxMovement, movementX));
        movementY = Math.Max(-maxMovement, Math.Min(maxMovement, movementY));

        if (window.Content is Grid mainGrid)
        {
            var backgroundLayer = mainGrid.Children.OfType<Grid>().FirstOrDefault(g => g.Name == "BackgroundLayer");
            if (backgroundLayer != null && backgroundLayer.Children.Count > 0)
            {
                var gpuBackground = backgroundLayer.Children.OfType<GpuBackgroundControl>().FirstOrDefault();
                if (gpuBackground != null) gpuBackground.RenderTransform = new TranslateTransform(movementX, movementY);
            }
        }
    }

    public void OnMouseMove(object? sender, PointerEventArgs e)
    {
        if (window.ParallaxToggle.IsChecked == false || window.BackgroundToggle.IsChecked == false) return;

        var position = e.GetPosition(window);
        _mouseX = position.X;
        _mouseY = position.Y;

        if (_mouseX < 0 || _mouseY < 0 || _mouseX > window.Width || _mouseY > window.Height) return;

        ApplyParallax(_mouseX, _mouseY);
    }

    private void UpdateBackgroundVisibility()
    {
        if (window._blurredBackground != null && window._normalBackground != null)
        {
            window._blurredBackground.IsVisible = viewModel.IsBlurEffectEnabled;
            window._normalBackground.IsVisible = !viewModel.IsBlurEffectEnabled;
        }
    }
    
    private class OpacityRequest(string key, double opacity, int priority)
    {
        public string Key { get; } = key;
        public double Opacity { get; set; } = opacity;
        public int Priority { get; } = priority;
    }
    
    public async Task RequestBackgroundOpacity(string key, double opacity, int priority, int durationMs = 200)
    {
        _opacityRequests[key] = new OpacityRequest(key, opacity, priority);
        await ApplyHighestPriorityOpacity(durationMs);
    }

    public void RemoveBackgroundOpacityRequest(string key)
    {
        if (_opacityRequests.Remove(key))
            _ = ApplyHighestPriorityOpacity(200);
    }

    private async Task ApplyHighestPriorityOpacity(int durationMs)
    {
        if (_opacityRequests.Count == 0)
        {
            await SetBackgroundOpacity(1.0, durationMs);
            return;
        }
        var highest = _opacityRequests.Values.OrderByDescending(r => r.Priority).First();
        await SetBackgroundOpacity(highest.Opacity, durationMs);
    }
}