using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
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

public class BackgroundManager(MainWindow window, SharedViewModel viewModel, TosuApi tosuApi, SettingsHandler settingsHandler)
{
    private string? _currentBackgroundDirectory;
    public BlurEffect? BackgroundBlurEffect;
    public required LogoUpdater? LogoUpdater;

    private CancellationTokenSource? _opacityCts;

    private readonly Grid _parallaxContainer = new();

    private readonly GpuBackgroundControl _bgControl = new();

    private Stopwatch _blendSw = null!;
    private DispatcherTimer _blendTimer = null!;
    
    private double _targetBlend;
    private double _startBlend;
    private bool _isBlending;
    
    private double _backgroundOpacity = 1.0f;
    private const int FadeMs = 200;
    private const int FadeSteps = 5;
    
    private bool _hasBeenInitialized;
    
    /// <summary>
    ///  We want the background to be zoomed in a bit so that parallax doesn't go out of bounds
    /// </summary>
    private const double BitmapZoom = 1.05f;
    
    private void ApplyBackgroundOpacity()
    {
        Grid layer = EnsureBackgroundLayerExists();
        layer.Opacity = _backgroundOpacity;
    }
    
    public async Task SetBackgroundOpacity(double targetOpacity, int durationMs = 0)
    {
        targetOpacity = Math.Clamp(targetOpacity, 0f, 1f);
        
        _opacityCts?.Cancel();
        _opacityCts = new CancellationTokenSource();
        CancellationToken token = _opacityCts.Token;

        if (durationMs <= 0)
        {
            _backgroundOpacity = targetOpacity;
            ApplyBackgroundOpacity();
            return;
        }

        double start = _backgroundOpacity;

        for (int i = 0; i <= FadeSteps; i++)
        {
            if (token.IsCancellationRequested)
                return;

            double t = (double)i / FadeSteps;
            _backgroundOpacity = start + (targetOpacity - start) * t;

            await Dispatcher.UIThread.InvokeAsync(ApplyBackgroundOpacity);

            await Task.Delay(16, token);
        }

        _backgroundOpacity = targetOpacity;
        ApplyBackgroundOpacity();
    }
    
    public async Task SetBackgroundEnabledState(bool enabled, bool? isPanelOpen)
    {
        double newOpacity = 0.0f;
        
        if(isPanelOpen != null)
            newOpacity = (bool)isPanelOpen ? 0.5f : 1f;
        
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

        _blendTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(16)
        };

        _blendTimer.Tick += OnRender;
        _blendTimer.Start();

        Grid layer = EnsureBackgroundLayerExists();

        _bgControl.RenderTransform = new ScaleTransform(BitmapZoom, BitmapZoom);
        _bgControl.RenderTransformOrigin =
            new RelativePoint(0.5, 0.5, RelativeUnit.Relative);
        
        BackgroundBlurEffect ??= new BlurEffect();
        _parallaxContainer.Effect = BackgroundBlurEffect;
        BackgroundBlurEffect.Radius = settingsHandler.BlurRadius;
        
        _bgControl.Opacity = 0.5;
        
        if (!_parallaxContainer.Children.Contains(_bgControl))
            _parallaxContainer.Children.Add(_bgControl);

        _bgControl.Width = window.Width;
        _bgControl.Height = window.Height;

        Dispatcher.UIThread.Post(() =>
        {
            _bgControl.InvalidateMeasure();
            _bgControl.InvalidateArrange();
            _bgControl.InvalidateVisual();
        }, DispatcherPriority.Loaded);
        
        if (!layer.Children.Contains(_parallaxContainer))
            layer.Children.Add(_parallaxContainer);

        _hasBeenInitialized = true;
    }
    
    private void OnRender(object? sender, EventArgs e)
    {
        if (!_isBlending)
            return;

        double t = _blendSw.ElapsedMilliseconds / (double)FadeMs;

        if (t >= 1.0)
        {
            _bgControl.Blend = 1;

            _bgControl.TextureA = _bgControl.TextureB ?? _bgControl.TextureA;
            _bgControl.TextureB = null;

            _isBlending = false;

            _bgControl.InvalidateVisual();
            return;
        }

        double eased = EaseInOutCubic(t);
        _bgControl.Blend = _startBlend + (_targetBlend - _startBlend) * eased;
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
    
    private async Task SwapBackgroundAsync(Bitmap? newBitmap)
    {
        if (newBitmap == null)
            return;

        EnsureInitialized();

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (_bgControl.TextureA == null)
            {
                _bgControl.TextureA = newBitmap;
                _bgControl.TextureB = null;
                _bgControl.Blend = 1;
                _bgControl.InvalidateVisual();
                return;
            }
            
            _bgControl.TextureA = _bgControl.TextureB ?? _bgControl.TextureA;
            _bgControl.TextureB = newBitmap;

            _startBlend = 0;
            _targetBlend = 1;

            _bgControl.Blend = 0;

            _blendSw = Stopwatch.StartNew();
            _isBlending = true;
        });
    }
    
    private static double EaseInOutCubic(double t)
    {
        return t < 0.5
            ? 4 * t * t * t
            : 1 - Math.Pow(-2 * t + 2, 3) / 2;
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
            Log.Error("UpdateBackground error: " + ex);
        }
    }

    private static async Task<Bitmap?> LoadBitmapAsync(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return null;

        await using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        
        return await Task.Run(() =>
        {
            Bitmap bmp = Bitmap.DecodeToWidth(stream, 2560);
            return bmp;
        });
    }
    
    public static async Task BlurBackgroundAsync(BlurEffect blurEffect, double radius, CancellationToken token)
    {
        if (token.IsCancellationRequested)
            return;

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            blurEffect.Radius = radius;
        });
    }
    
    internal void ApplyParallax(double mouseX, double mouseY)
    {
        if (!viewModel.IsParallaxEnabled || !viewModel.IsBackgroundEnabled)
        {
            _parallaxContainer.RenderTransform = new TranslateTransform(0, 0);
            return;
        }

        if (window.Content is not Grid mainGrid)
            return;

        Grid? layer = mainGrid.Children
            .OfType<Grid>()
            .FirstOrDefault(x => x.Name == "BackgroundLayer");

        if (layer == null)
            return;

        double centerX = window.Width / 2;
        double centerY = window.Height / 2;

        double movementX = -(mouseX - centerX) * 0.015;
        double movementY = -(mouseY - centerY) * 0.015;

        movementX = Math.Clamp(movementX, -15, 15);
        movementY = Math.Clamp(movementY, -15, 15);

        _parallaxContainer.RenderTransform =
            new TranslateTransform(movementX, movementY);
    }
    
}