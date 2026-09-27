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
    private string? _currentBackgroundDirectory;
    private double _currentBackgroundOpacity = 0.5f;

    private bool _hasBeenInitialized;

    private double _lastMouseX;
    private double _lastMouseY;

    private CancellationTokenSource? _opacityCts;

    private CancellationTokenSource? _parallaxCts;

    private CancellationTokenSource? _parallaxResetCts;

    private double _parallaxTargetX;
    private double _parallaxTargetY;

    private bool _showingA = true;

    private bool _wasParallaxEnabled;

    public async Task SetBackgroundOpacity(double targetOpacity, int durationMs = 0)
    {
        Grid layer = EnsureBackgroundLayerExists();

        targetOpacity = Math.Clamp(targetOpacity, 0f, 0.5f);

        _opacityCts?.Cancel();
        _opacityCts = new CancellationTokenSource();
        CancellationToken token = _opacityCts.Token;

        if (durationMs <= 0)
        {
            _currentBackgroundOpacity = targetOpacity;
            layer.Opacity = _currentBackgroundOpacity;
            return;
        }

        double start = _currentBackgroundOpacity;

        for (int i = 0; i <= FadeSteps; i++)
        {
            if (token.IsCancellationRequested)
                return;

            double t = (double)i / FadeSteps;

            _currentBackgroundOpacity = start + (targetOpacity - start) * t;

            await Dispatcher.UIThread.InvokeAsync(() => { layer.Opacity = _currentBackgroundOpacity; });

            await Task.Delay(durationMs / FadeSteps, token);
        }

        _currentBackgroundOpacity = targetOpacity;
        layer.Opacity = _currentBackgroundOpacity;
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

    /// <summary>
    ///     Sets the default properties for the backgrounds' appearance
    /// </summary>
    /// <param name="image"></param>
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
        if (window.Content is not Grid mainGrid)
        {
            mainGrid = new Grid();
            window.Content = mainGrid;
        }

        Grid? layer = mainGrid.Children
            .OfType<Grid>()
            .FirstOrDefault(x => x.Name == "BackgroundLayer");

        if (layer == null)
        {
            layer = new Grid
            {
                Name = "BackgroundLayer",
                ZIndex = -1
            };

            mainGrid.Children.Insert(0, layer);
        }

        if (_parallaxContainer.Parent == null)
            layer.Children.Add(_parallaxContainer);

        return layer;
    }

    private async Task SwapBackgroundAsync(Bitmap bitmap)
    {
        EnsureInitialized();

        IEasing easing = new CubicEaseOut();

        Image incoming = _showingA ? _secondBackground : _firstBackground;
        Image outgoing = _showingA ? _firstBackground : _secondBackground;

        incoming.Source = bitmap;
        incoming.Opacity = 0;

        const int duration = 150;
        const int steps = 15;

        for (int i = 0; i <= steps; i++)
        {
            double t = (double)i / steps;
            double eased = easing.Ease(t);

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                incoming.Opacity = eased;
                outgoing.Opacity = 1 - eased;
            });

            await Task.Delay(duration / steps);
        }

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            Bitmap? oldBitmap = outgoing.Source as Bitmap;

            outgoing.Source = null;

            oldBitmap?.Dispose();
        });

        _showingA = !_showingA;
    }

    public async Task UpdateBackground(bool isPanelOpen)
    {
        try
        {
            await SetBackgroundEnabledState(viewModel.IsBackgroundEnabled, isPanelOpen);

            if (!viewModel.IsBackgroundEnabled)
                return;

            string path = tosuApi.GetBackgroundPath() ?? string.Empty;

            if (string.IsNullOrWhiteSpace(path))
                return;

            if (path == _currentBackgroundDirectory)
                return;

            _currentBackgroundDirectory = path;

            Bitmap? newBitmap = await LoadBitmapAsync(path);
            if (newBitmap == null)
                return;

            await SwapBackgroundAsync(newBitmap);

            if (LogoUpdater != null)
                await LogoUpdater.UpdateLogoAsync();
        }
        catch (Exception ex)
        {
            Log.Error("UpdateBackground exited with exception: " + ex);
        }
    }

    private static async Task<Bitmap?> LoadBitmapAsync(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return null;

        await using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);

        return await Task.Run(() =>
        {
            Bitmap bmp = Bitmap.DecodeToWidth(stream, 1024, BitmapInterpolationMode.LowQuality);
            return bmp;
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
        _lastMouseX = mouseX;
        _lastMouseY = mouseY;

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