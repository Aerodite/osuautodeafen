using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using osuautodeafen.Logo;
using osuautodeafen.Settings;
using osuautodeafen.Tosu;
using osuautodeafen.ViewModels;
using Serilog;

// ReSharper disable MethodHasAsyncOverload

namespace osuautodeafen.Background;

public class BackgroundManager(
    MainWindow window,
    SharedViewModel viewModel,
    TosuApi tosuApi,
    SettingsHandler settingsHandler)
{
    private const int FadeMs = 200;
    private const int FadeSteps = 20;

    private const double BackgroundZoom = 1.05f;

    private const double BackgroundOpacity = 0.5f;

    private static readonly IEasing BackgroundFadeEase =
        new CubicEaseOut();

    private readonly SemaphoreSlim _backgroundSwapLock = new(1, 1);

    private readonly Image _firstBackground = new()
    {
        Stretch = Stretch.UniformToFill,
        Opacity = 1
    };

    private readonly Grid _parallaxContainer = new();

    private readonly TranslateTransform _parallaxTransform = new();

    private readonly Image _secondBackground = new()
    {
        Stretch = Stretch.UniformToFill,
        Opacity = 0
    };

    public BlurEffect? BackgroundBlurEffect;
    public required LogoUpdater? LogoUpdater;

    private Grid? _backgroundLayer;
    private string? _currentBackgroundDirectory;
    private double _currentBackgroundOpacity = 0.5f;

    private bool _hasBeenInitialized;

    private CancellationTokenSource? _opacityCts;

    private CancellationTokenSource? _parallaxCts;

    private double _parallaxTargetX;
    private double _parallaxTargetY;

    private Bitmap? _pendingBackground;
    private string? _pendingBackgroundPath;

    private bool _showingA = true;

    public async Task SetBackgroundOpacity(
        double targetOpacity,
        int durationMs = 0)
    {
        Grid layer = EnsureBackgroundLayerExists();

        targetOpacity = Math.Clamp(targetOpacity, 0.0, 0.5);

        _opacityCts?.Cancel();
        _opacityCts?.Dispose();

        _opacityCts = new CancellationTokenSource();
        CancellationToken token = _opacityCts.Token;

        if (durationMs <= 0)
        {
            _currentBackgroundOpacity = targetOpacity;
            layer.Opacity = targetOpacity;
            return;
        }

        double start = _currentBackgroundOpacity;

        try
        {
            for (int i = 1; i <= FadeSteps; i++)
            {
                double t = (double)i / FadeSteps;

                _currentBackgroundOpacity =
                    start + (targetOpacity - start) * t;

                await Dispatcher.UIThread.InvokeAsync(() =>
                    layer.Opacity = _currentBackgroundOpacity);

                if (i < FadeSteps)
                    await Task.Delay(durationMs / FadeSteps, token);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    public async Task SetBackgroundEnabledState(bool enabled, bool? isPanelOpen)
    {
        double newOpacity = 0.0f;

        if (isPanelOpen != null)
            newOpacity = (bool)isPanelOpen ? 0.25f : 0.5f;

        if (!enabled)
        {
            await SetBackgroundOpacity(0.0f, FadeMs);
            _parallaxContainer.IsVisible = false;
            return;
        }

        _parallaxContainer.IsVisible = true;
        await SetBackgroundOpacity(newOpacity, FadeMs);
    }

    private void EnsureInitialized()
    {
        if (_hasBeenInitialized)
            return;

        EnsureBackgroundLayerExists();

        ConfigureImage(_firstBackground);
        ConfigureImage(_secondBackground);

        _parallaxContainer.Children.Add(_firstBackground);
        _parallaxContainer.Children.Add(_secondBackground);

        _parallaxContainer.RenderTransform = _parallaxTransform;

        BackgroundBlurEffect ??= new BlurEffect();
        BackgroundBlurEffect.Radius = settingsHandler.BlurRadius;

        _parallaxContainer.Effect = BackgroundBlurEffect;

        viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SharedViewModel.IsParallaxEnabled))
                SetParallaxEnabled(viewModel.IsParallaxEnabled);
        };

        _hasBeenInitialized = true;
    }
    
    private static void ConfigureImage(Image image)
    {
        image.Stretch = Stretch.UniformToFill;

        image.HorizontalAlignment = HorizontalAlignment.Stretch;
        image.VerticalAlignment = VerticalAlignment.Stretch;

        image.RenderTransform =
            new ScaleTransform(BackgroundZoom, BackgroundZoom);

        image.RenderTransformOrigin =
            new RelativePoint(0.5, 0.5, RelativeUnit.Relative);

        image.Opacity = BackgroundOpacity;
    }

    private Grid EnsureBackgroundLayerExists()
    {
        if (_backgroundLayer != null)
            return _backgroundLayer;

        if (window.Content is not Grid mainGrid)
        {
            mainGrid = new Grid();
            window.Content = mainGrid;
        }

        _backgroundLayer = mainGrid.Children
            .OfType<Grid>()
            .FirstOrDefault(x => x.Name == "BackgroundLayer");

        if (_backgroundLayer == null)
        {
            _backgroundLayer = new Grid
            {
                Name = "BackgroundLayer",
                ZIndex = -1
            };

            mainGrid.Children.Insert(0, _backgroundLayer);
        }

        if (_parallaxContainer.Parent == null)
            _backgroundLayer.Children.Add(_parallaxContainer);

        return _backgroundLayer;
    }

    private async Task SwapBackgroundAsync(Bitmap bitmap)
    {
        EnsureInitialized();

        Image incoming = _showingA
            ? _secondBackground
            : _firstBackground;

        Image outgoing = _showingA
            ? _firstBackground
            : _secondBackground;

        incoming.Source = bitmap;
        incoming.Opacity = 0;

        const int duration = 250;
        const int steps = 25;

        for (int i = 1; i <= steps; i++)
        {
            double t = (double)i / steps;
            double eased = BackgroundFadeEase.Ease(t);

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                incoming.Opacity = eased;
                outgoing.Opacity = 1.0 - eased;
            });

            if (i < steps)
                await Task.Delay(duration / steps);
        }

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            Bitmap? oldBitmap = outgoing.Source as Bitmap;

            outgoing.Source = null;
            outgoing.Opacity = 0;

            oldBitmap?.Dispose();

            incoming.Opacity = 1;
        });

        _showingA = !_showingA;
    }

    public async Task UpdateBackground(bool isPanelOpen)
    {
        try
        {
            if (!viewModel.IsBackgroundEnabled)
            {
                await SetBackgroundEnabledState(false, isPanelOpen);
                return;
            }

            string path = tosuApi.GetBackgroundPath() ?? string.Empty;

            if (string.IsNullOrWhiteSpace(path))
                return;

            Task opacityTask =
                SetBackgroundEnabledState(true, isPanelOpen);

            if (path == _currentBackgroundDirectory ||
                path == _pendingBackgroundPath)
            {
                await opacityTask;
                return;
            }

            Bitmap? bitmap = await LoadBitmapAsync(path);

            if (bitmap == null)
                return;
            
            if (!_backgroundSwapLock.Wait(0))
            {
                Bitmap? oldPending =
                    Interlocked.Exchange(ref _pendingBackground, bitmap);

                oldPending?.Dispose();

                _pendingBackgroundPath = path;

                await opacityTask;
                return;
            }

            try
            {
                Bitmap? nextBitmap = bitmap;
                string nextPath = path;

                while (nextBitmap != null)
                {
                    await SwapBackgroundAsync(nextBitmap);

                    _currentBackgroundDirectory = nextPath;
                    
                    nextBitmap = Interlocked.Exchange(ref _pendingBackground, null);

                    if (nextBitmap != null)
                    {
                        nextPath =
                            _pendingBackgroundPath ?? nextPath;

                        _pendingBackgroundPath = null;
                    }
                }
            }
            finally
            {
                _backgroundSwapLock.Release();
            }

            await opacityTask;

            if (LogoUpdater != null)
                await LogoUpdater.UpdateLogoAsync();
        }
        catch (Exception ex)
        {
            Log.Error(
                ex,
                "UpdateBackground exited with exception");
        }
    }

    private static Task<Bitmap?> LoadBitmapAsync(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return Task.FromResult<Bitmap?>(null);

        return Task.Run<Bitmap?>(() =>
        {
            try
            {
                using FileStream stream = new(
                    path,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.ReadWrite);

                return Bitmap.DecodeToWidth(
                    stream,
                    1024,
                    BitmapInterpolationMode.MediumQuality);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to load background {Path}", path);
                return null;
            }
        });
    }

    public static async Task BlurBackgroundAsync(BlurEffect blurEffect, double radius, CancellationToken token)
    {
        if (token.IsCancellationRequested)
            return;

        await Dispatcher.UIThread.InvokeAsync(() => { blurEffect.Radius = radius; });
    }

    internal void SetParallaxEnabled(bool enabled)
    {
        if (!enabled)
            SetParallaxTarget(0, 0);
    }

    private void SetParallaxTarget(double x, double y)
    {
        _parallaxTargetX = x;
        _parallaxTargetY = y;

        if (_parallaxCts != null)
            return;

        _parallaxCts = new CancellationTokenSource();
        _ = SmoothParallaxMovement(_parallaxCts.Token);
    }

    private async Task SmoothParallaxMovement(CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                double dx = _parallaxTargetX - _parallaxTransform.X;
                double dy = _parallaxTargetY - _parallaxTransform.Y;

                if (Math.Abs(dx) < 0.01 &&
                    Math.Abs(dy) < 0.01)
                {
                    _parallaxTransform.X = _parallaxTargetX;
                    _parallaxTransform.Y = _parallaxTargetY;
                    break;
                }

                const double smoothing = 0.18;

                _parallaxTransform.X += dx * smoothing;
                _parallaxTransform.Y += dy * smoothing;

                await Task.Delay(16, token);
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            _parallaxCts?.Dispose();
            _parallaxCts = null;

            if (Math.Abs(_parallaxTargetX - _parallaxTransform.X) >= 0.01 ||
                Math.Abs(_parallaxTargetY - _parallaxTransform.Y) >= 0.01)
                SetParallaxTarget(_parallaxTargetX, _parallaxTargetY);
        }
    }

    internal void ApplyParallax(double mouseX, double mouseY)
    {
        if (!viewModel.IsParallaxEnabled ||
            !viewModel.IsBackgroundEnabled)
            return;

        SetParallaxTargetFromMouse(mouseX, mouseY);
    }

    private void SetParallaxTargetFromMouse(double mouseX, double mouseY)
    {
        double centerX = window.Width / 2;
        double centerY = window.Height / 2;

        double movementX = Math.Clamp(
            -(mouseX - centerX) * 0.015,
            -15,
            15);

        double movementY = Math.Clamp(
            -(mouseY - centerY) * 0.015,
            -15,
            15);

        SetParallaxTarget(movementX, movementY);
    }
}