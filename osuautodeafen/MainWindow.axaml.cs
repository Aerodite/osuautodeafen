using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.Threading;
using IniParser.Model;
using LiveChartsCore.Defaults;
using LiveChartsCore.Drawing;
using LiveChartsCore.Measure;
using osuautodeafen.cs;
using osuautodeafen.cs.Background;
using osuautodeafen.cs.Deafen;
using osuautodeafen.cs.Log;
using osuautodeafen.cs.Logo;
using osuautodeafen.cs.Settings;
using osuautodeafen.cs.Settings.Keybinds;
using osuautodeafen.cs.Settings.Presets;
using osuautodeafen.cs.StrainGraph;
using osuautodeafen.cs.StrainGraph.ProgressIndicator;
using osuautodeafen.cs.Tooltips;
using osuautodeafen.cs.Tosu;
using osuautodeafen.cs.Update;
using SkiaSharp;
using Svg.Skia;
using Animation = Avalonia.Animation.Animation;
using KeyFrame = Avalonia.Animation.KeyFrame;

namespace osuautodeafen;

public partial class MainWindow : Window
{
    private const double BeatsPerRotation = 4;
    private readonly AnimationManager _animationManager = new();
    private readonly BackgroundManager? _backgroundManager;

    private readonly BreakPeriodCalculator _breakPeriod;
    private readonly ChartManager _chartManager;
    private readonly Lock _cogSpinLock = new();
    private readonly Stopwatch _frameStopwatch = new();
    private readonly GetLowResBackground? _getLowResBackground;
    private readonly KiaiTimes _kiaiTimes = new();
    private readonly LogImportant _logImportant = new();
    private readonly DispatcherTimer _mainTimer;
    private readonly SemaphoreSlim _panelAnimationLock = new(1, 1);

    private readonly HashSet<Key> _pressedKeys = new();
    private readonly ProgressIndicatorHelper _progressIndicatorHelper;
    private readonly Action _settingsButtonClicked;
    private readonly SettingsHandler? _settingsHandler;
    private readonly TooltipManager _tooltipManager = new();
    private readonly TosuApi _tosuApi;
    private readonly UpdateChecker _updateChecker = new();
    private readonly SemaphoreSlim _updateCheckLock = new(1, 1);
    private readonly SharedViewModel _viewModel;
    private CancellationTokenSource? _blurCts;
    private double _cogCurrentAngle;
    private double _cogSpinBpm = 140;
    private double _cogSpinStartAngle;
    private DateTime _cogSpinStartTime;
    private DispatcherTimer? _cogSpinTimer;

    private DispatcherTimer? _completionPercentageSaveTimer;
    
    private bool _tooltipOutsideBounds = false;

    private CancellationTokenSource? _frameCts;
    private bool _isCogSpinning;
    private bool _isKiaiPulseHigh;
    private bool _isSettingsPanelOpen;
    private DispatcherTimer? _kiaiBrightnessTimer;
    private List<string> _lastDisplayedLogs = [];
    private GraphData? _lastGraphData;
    private Key _lastKeyPressed = Key.None;
    private DateTime _lastKeyPressTime = DateTime.MinValue;
    private LogoControl? _logoControl;
    private DispatcherTimer? _logUpdateTimer;
    private double _opacity = 1.00;
    private double _pendingCompletionPercentage;
    private int _pendingPP;
    private double _pendingStarRating;
    
    private DispatcherTimer? _modifierOnlyTimer;

    private DispatcherTimer? _ppSaveTimer;

    private DispatcherTimer? _starRatingSaveTimer;

    private Button? _updateNotificationBarButton;
    private ProgressBar? _updateProgressBar;

    public Image? NormalBackground;
    
    private readonly Dictionary<Control, Task> _toggleQueues = new();

    /// <summary>
    ///     Primary Constructor for MainWindow
    /// </summary>
    /// <exception cref="FileNotFoundException"></exception>
    public MainWindow()
    {
        InitializeComponent();

        string appPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "osuautodeafen");
        Directory.CreateDirectory(appPath);

        string logFile = Path.Combine(appPath, "osuautodeafen.log");
        LogFileManager.CreateLogFile(logFile);
        LogFileManager.ClearLogFile(logFile);

        LogFileManager.InitializeLogging(logFile);

        _tosuApi = new TosuApi();

        _viewModel = new SharedViewModel(_tosuApi, _tooltipManager);
        ViewModel = _viewModel;
        DataContext = _viewModel;

        _settingsHandler = new SettingsHandler();
        _settingsHandler.LoadSettings();
        InitializeSettings();

        PresetInfo presetInfo = new();

        InitializeLogo();

        Opened += async (_, __) =>
        {
            await _updateChecker.CheckForUpdatesAsync();
            if (_updateChecker.UpdateInfo != null) ShowUpdateNotification();
        };

        string resourceName = "osuautodeafen.Resources.favicon.ico";
        string deafenResourceName = "osuautodeafen.Resources.favicon_d.ico";
        string startupIconPath = Path.Combine(Path.GetTempPath(), "osuautodeafen_favicon.ico");
        string deafenIconPath = Path.Combine(Path.GetTempPath(), "osuautodeafen_favicon_d.ico");
        using (Stream? resourceStream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName))
        {
            if (resourceStream == null)
                throw new FileNotFoundException("Embedded icon not found: " + resourceName);

            using (FileStream iconFileStream = new(startupIconPath, FileMode.Create, FileAccess.Write))
            {
                resourceStream.CopyTo(iconFileStream);
            }

            using (Stream? deafenResourceStream =
                   Assembly.GetExecutingAssembly().GetManifestResourceStream(deafenResourceName))
            {
                if (deafenResourceStream == null)
                    throw new FileNotFoundException("Embedded deafen icon not found: " + deafenResourceName);

                using (FileStream deafenIconFileStream = new(deafenIconPath, FileMode.Create, FileAccess.Write))
                {
                    deafenResourceStream.CopyTo(deafenIconFileStream);
                }
            }
        }

        TaskbarIconChanger.SetTaskbarIcon(this, startupIconPath);
        Icon = new WindowIcon(startupIconPath);

        _getLowResBackground = new GetLowResBackground(_tosuApi);

        _backgroundManager = new BackgroundManager(this, _viewModel, _tosuApi)
        {
            LogoUpdater = null
        };

        object? oldContent = Content;
        Content = null;
        Content = new Grid
        {
            Children = { new ContentControl { Content = oldContent } }
        };

        InitializeViewModel();

        _breakPeriod = new BreakPeriodCalculator();
        _chartManager = new ChartManager(PlotView, _tosuApi, _viewModel, _kiaiTimes, _tooltipManager);
        _progressIndicatorHelper = new ProgressIndicatorHelper(_chartManager);

        Deafen deafen = new(_tosuApi, _settingsHandler, _viewModel);

        // im d1 lazy so ill do this in 1.0.9 :tf:
        // deafen.Deafened += () =>
        // {
        //     TaskbarIconChanger.SetTaskbarIcon(this, deafenIconPath);
        //     Icon = new WindowIcon(deafenIconPath);
        // };
        // deafen.Undeafened += () =>
        // {
        //     TaskbarIconChanger.SetTaskbarIcon(this, startupIconPath);
        //     Icon = new WindowIcon(startupIconPath);
        // };

        // ideally we could use no timers whatsoever but for now this works fine
        // because it really only checks if events should be triggered
        // updated to 16ms from 100ms since apparently it takes 1ms to run anyways
        _mainTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _mainTimer.Tick += MainTimer_Tick;
        _mainTimer.Start();
        _viewModel.PropertyChanged += ViewModel_PropertyChanged;

        if (_backgroundManager.BackgroundBlurEffect != null)
            _backgroundManager.BackgroundBlurEffect.Radius = _viewModel.BlurRadius;

        ProgressOverlay.ChartXMin = _progressIndicatorHelper.ChartXMin;
        ProgressOverlay.ChartXMax = _progressIndicatorHelper.ChartXMax;
        ProgressOverlay.ChartYMin = _progressIndicatorHelper.ChartYMin;
        ProgressOverlay.ChartYMax = _progressIndicatorHelper.ChartYMax;

        ProgressOverlay.Points =
            _progressIndicatorHelper.CalculateSmoothProgressContour(_tosuApi.GetCompletionPercentage());
        
        _tosuApi.BeatmapChanged += async () =>
        {
            string checksum = _tosuApi.GetBeatmapChecksum();
            string presetsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "osuautodeafen", "presets");
            string presetFilePath = Path.Combine(presetsPath, $"{checksum}.preset");
            _viewModel.PresetExistsForCurrentChecksum = File.Exists(presetFilePath);
            foreach (PresetInfo preset in _viewModel.Presets ?? Enumerable.Empty<PresetInfo>())
            {
                preset.IsCurrentPreset = preset.Checksum == _tosuApi.GetBeatmapChecksum();
                //Console.WriteLine($"Preset {preset.BeatmapName} IsCurrentPreset: {preset.IsCurrentPreset}");
            }

            if (_viewModel.PresetExistsForCurrentChecksum)
            {
                _settingsHandler.ActivatePreset(presetFilePath);
            }
            else
            {
                _settingsHandler.DeactivatePreset();
                _settingsHandler.LoadSettings();
            }

            UpdateDeafenKeybindDisplay();
            UpdateViewModel();

            _logImportant.logImportant("Client/Server: " + _tosuApi.GetClient() + "/" + _tosuApi.GetServer(), false,
                "Client");
            // thanks a lot take a hint for letting me figure this one out 😔
            // if a map is over 70 characters it overflows to the next line (on 630 width)
            // so this just ensures its not ugly for people (me) looking at the debug menu
            _viewModel.BeatmapName = _tosuApi.GetBeatmapTitle();
            _viewModel.FullBeatmapName = _tosuApi.GetBeatmapArtist() + " - " + _tosuApi.GetBeatmapTitle();
            _viewModel.BeatmapDifficulty = _tosuApi.GetBeatmapDifficulty();
            string mapInfo = _tosuApi.GetBeatmapArtist() + " - " + _tosuApi.GetBeatmapTitle();
            if (mapInfo.Length > 67)
                mapInfo = mapInfo.Substring(0, 67) + "...";

            _logImportant.logImportant("Mapset: " + mapInfo, false, "Beatmap changed",
                $"https://osu.ppy.sh/b/{_tosuApi.GetBeatmapId()}");
            _logImportant.logImportant("Current PP: " + _tosuApi.GetCurrentPP(), false, "CurrentPP");
            _logImportant.logImportant("Max PP: " + _tosuApi.GetMaxPP(), false, "Max PP");
            _logImportant.logImportant("Max Combo: " + _tosuApi.GetMaxCombo(), false, "Max Combo");
            _logImportant.logImportant("Star Rating: " + _tosuApi.GetFullSR(), false, "Star Rating");
            _logImportant.logImportant("Mods: " + _tosuApi.GetSelectedMods(), false, "Mods");
            _logImportant.logImportant("Ranked Status: " + _tosuApi.GetRankedStatus(), false, "Ranked Status");
            _logImportant.logImportant("Beatmap ID: " + _tosuApi.GetBeatmapId(), false, "Beatmap ID");
            _logImportant.logImportant("Beatmap Set ID: " + _tosuApi.GetBeatmapSetId(), false, "Beatmap Set ID");
            _logImportant.logImportant("Break: " + _tosuApi.IsBreakPeriod(), false, "Break");
            _logImportant.logImportant("Kiai: " + _kiaiTimes.IsKiaiPeriod(_tosuApi.GetCurrentTime()), false, "Kiai");
            Task graphTask = Dispatcher.UIThread.InvokeAsync(() => OnGraphDataUpdated(_tosuApi.GetGraphData()))
                .GetTask();
            Task bgTask = _backgroundManager != null
                ? _backgroundManager.UpdateBackground(null, null)
                : Task.CompletedTask;
            await Task.WhenAll(graphTask, bgTask);
        };
        _tosuApi.HasRateChanged += async () =>
        {
            _logImportant.logImportant("Max PP: " + _tosuApi.GetMaxPP(), false, "Max PP");
            _logImportant.logImportant("Star Rating: " + _tosuApi.GetFullSR(), false, "Star Rating");
            await Dispatcher.UIThread.InvokeAsync(() => OnGraphDataUpdated(_tosuApi.GetGraphData()));
        };
        _tosuApi.HasModsChanged += async () =>
        {
            _logImportant.logImportant("Max PP: " + _tosuApi.GetMaxPP(), false, "Max PP");
            _logImportant.logImportant("Star Rating: " + _tosuApi.GetFullSR(), false, "Star Rating");
            _logImportant.logImportant("Mods: " + _tosuApi.GetSelectedMods(), false, "Mods");
            _viewModel.UpdateMinPPValue();
            _viewModel.UpdateMinSRValue();
            await Dispatcher.UIThread.InvokeAsync(() => OnGraphDataUpdated(_tosuApi.GetGraphData()));
        };
        _tosuApi.HasPercentageChanged += async () =>
        {
            // might as well not update the debug menu if it isnt even visible
            StackPanel? debugConsolePanel = this.FindControl<StackPanel>("DebugConsolePanel");
            if (debugConsolePanel == null || !debugConsolePanel.IsVisible)
                return;
            _logImportant.logImportant($"Progress %: {_tosuApi.GetCompletionPercentage():F2}%", false, "Map Progress");
            double rate = _tosuApi.GetRateAdjustRate();
            if (rate <= 0 || double.IsNaN(rate) || double.IsInfinity(rate))
                rate = 1;

            double currentMs = _tosuApi.GetCurrentTime() / rate;
            double fullMs = _tosuApi.GetFullTime() / rate;

            if (double.IsNaN(currentMs) || double.IsInfinity(currentMs) || currentMs < 0)
                currentMs = 0;
            if (double.IsNaN(fullMs) || double.IsInfinity(fullMs) || fullMs < 0)
                fullMs = 0;

            _logImportant.logImportant(
                $"Progress (mm:ss): {TimeSpan.FromMilliseconds(currentMs):mm\\:ss}/{TimeSpan.FromMilliseconds(fullMs):mm\\:ss}",
                false,
                "Current Time"
            );
            _logImportant.logImportant("Max PP: " + _tosuApi.GetMaxPP(), false, "Max PP");
            _logImportant.logImportant("Star Rating: " + _tosuApi.GetFullSR(), false, "Star Rating");
            _logImportant.logImportant("Current PP: " + _tosuApi.GetCurrentPP(), false, "CurrentPP");
            _logImportant.logImportant("isDeafened: " + deafen._deafened, false, "isDeafened");
            _logImportant.logImportant("Deafen Start Percentage: " + _viewModel.MinCompletionPercentage + "%", false,
                "Min Deafen Percentage");
            _logImportant.logImportant("Min Star Rating: " + _viewModel.StarRating, false, "Min Star Rating");
            _logImportant.logImportant("Min SS PP: " + _viewModel.PerformancePoints, false,
                "Min SS PP");
        };
        _tosuApi.HasBPMChanged += async () =>
        {
            await Dispatcher.UIThread.InvokeAsync(UpdateCogSpinBpm);
            _tosuApi.RaiseKiaiChanged();

            if (_tosuApi._isKiai && _kiaiBrightnessTimer != null)
            {
                double bpm = _tosuApi.GetCurrentBpm();
            }

            _logImportant.logImportant("BPM: " + _tosuApi.GetCurrentBpm(), false, "BPM Changed");
        };
        _tosuApi.HasStateChanged += async () =>
        {
            _logImportant.logImportant("State: " + _tosuApi.GetRawBanchoStatus(), false, "State");
        };
        _tosuApi.HasKiaiChanged += async (sender, e) =>
        {
            if (!_viewModel.IsBackgroundEnabled || !_viewModel.IsKiaiEffectEnabled)
            {
                _kiaiBrightnessTimer?.Stop();
                _kiaiBrightnessTimer = null;
                _backgroundManager.RemoveBackgroundOpacityRequest("kiai");
                return;
            }

            _opacity = _isSettingsPanelOpen ? 0.50 : 0;

            if (_tosuApi._isKiai)
            {
                double bpm = _tosuApi.GetCurrentBpm();
                double intervalMs = 60000.0 / bpm;

                _kiaiBrightnessTimer?.Stop();
                _kiaiBrightnessTimer = new DispatcherTimer
                {
                    Interval = TimeSpan.FromMilliseconds(intervalMs)
                };
                _kiaiBrightnessTimer.Tick += async (_, _) =>
                {
                    _isKiaiPulseHigh = !_isKiaiPulseHigh;
                    if (_isKiaiPulseHigh)
                        await _backgroundManager.RequestBackgroundOpacity("kiai", 1.0 - _opacity, 10000,
                            (int)(intervalMs / 4));
                    else
                        await _backgroundManager.RequestBackgroundOpacity("kiai", 0.95 - _opacity, 10000,
                            (int)(intervalMs / 4));
                };
                _kiaiBrightnessTimer.Start();
            }
            else
            {
                _kiaiBrightnessTimer?.Stop();
                _kiaiBrightnessTimer = null;

                if (_isSettingsPanelOpen) await _backgroundManager.RequestBackgroundOpacity("settings", 0.5, 10, 150);

                _backgroundManager.RemoveBackgroundOpacityRequest("kiai");
            }
        };
        _breakPeriod.BreakPeriodEntered += async () => { _logImportant.logImportant("Break: True", false, "Break"); };
        _breakPeriod.BreakPeriodExited += async () => { _logImportant.logImportant("Break: False", false, "Break"); };
        _kiaiTimes.KiaiPeriodEntered += async () => { _logImportant.logImportant("Kiai: True", false, "Kiai"); };
        _kiaiTimes.KiaiPeriodExited += async () => { _logImportant.logImportant("Kiai: False", false, "Kiai"); };

        _settingsButtonClicked = async () =>
        {
            if (_isSettingsPanelOpen)
            {
                _opacity = 0;
                _backgroundManager?.RemoveBackgroundOpacityRequest("settings");
            }
            else
            {
                _opacity = 0.5;
                if (!_tosuApi._isKiai || !_viewModel.IsKiaiEffectEnabled)
                    await _backgroundManager?.RequestBackgroundOpacity("settings", 0.5, 10, 150);
            }
        };
        DockPanel? settingsPanel = this.FindControl<DockPanel>("SettingsPanel");
        settingsPanel.Transitions = new Transitions
        {
            new DoubleTransition
            {
                Property = OpacityProperty,
                Duration = TimeSpan.FromSeconds(0.5),
                Easing = new QuarticEaseInOut()
            }
        };


        ExtendClientAreaToDecorationsHint = true;
        ExtendClientAreaTitleBarHeightHint = 32;
        ExtendClientAreaChromeHints = ExtendClientAreaChromeHints.PreferSystemChrome;

        Background = Brushes.Black;
        BorderBrush = Brushes.Black;

        /*
        to anyone looking through the code yes you can make the window width and height
        anything you want between 400x550 and 800x800, i was going to make this resizable
        but that ended up being a massive pita because of the debug menu and the background didn't
        really play nice with being resized (unless it started off at 800x800 (but thats stupid)
        anyways have fun it should still technically work if you manually set this in the settings.ini
        */
        Width = _settingsHandler.WindowWidth;
        Height = _settingsHandler.WindowHeight;
        Title = "osuautodeafen";
        MaxHeight = 800;
        MaxWidth = 800;
        MinHeight = 400;
        MinWidth = 550;
        CanResize = false;
        Closing += MainWindow_Closing;

        _tooltipManager.SetTooltipControls(CustomTooltip, TooltipText, _settingsHandler.WindowWidth, _settingsHandler.WindowHeight);

        PointerPressed += (sender, e) =>
        {
            Point point = e.GetPosition(this);
            const int titleBarHeight = 34;
            if (point.Y <= titleBarHeight) BeginMoveDrag(e);
        };
        
        var FCToggle = this.FindControl<CheckBox>("FCToggle");
        var undeafenPanel = this.FindControl<StackPanel>("UndeafenOnMissPanel");

        if (FCToggle != null && undeafenPanel != null)
        {
            if (FCToggle.IsChecked == true)
            {
                undeafenPanel.IsVisible = true;
                undeafenPanel.Opacity = 1;
                undeafenPanel.RenderTransform ??= new TranslateTransform();
                ((TranslateTransform)undeafenPanel.RenderTransform).Y = 0;
            }

            FCToggle.IsCheckedChanged += async (sender, _) =>
            {
                CheckBox? check = sender as CheckBox;
                bool isChecked = check?.IsChecked == true;
                await EnqueueShowSubToggle(undeafenPanel, isChecked);
            };
        }
        else
        {
            Console.WriteLine("FCToggle or UndeafenOnMissPanel not found in XAML");
        }
        
        this.FindControl<StackPanel>("UndeafenOnMissPanel")!.IsVisible = false;
        this.FindControl<StackPanel>("UndeafenOnMissPanel")!.Opacity = 0;
        
        if (_settingsHandler.IsFCRequired)
        {
            UndeafenOnMissPanel.IsVisible = true;
            UndeafenOnMissPanel.Opacity = 1;
            (((TranslateTransform)UndeafenOnMissPanel.RenderTransform)!).Y = 0;
        }
        
        StackPanel? parallaxPanel = this.FindControl<StackPanel>("ParallaxTogglePanel");
        StackPanel? kiaiPanel = this.FindControl<StackPanel>("KiaiTogglePanel");
        StackPanel? blurPanel = this.FindControl<StackPanel>("BlurEffectPanel");

        if (BackgroundToggle != null && parallaxPanel != null && kiaiPanel != null && blurPanel != null)
        {
            if (parallaxPanel.RenderTransform == null) parallaxPanel.RenderTransform = new TranslateTransform();
            if (kiaiPanel.RenderTransform == null) kiaiPanel.RenderTransform = new TranslateTransform();
            if (blurPanel.RenderTransform == null) blurPanel.RenderTransform = new TranslateTransform();

            if (BackgroundToggle.IsChecked == true)
            {
                parallaxPanel.IsVisible = true;
                parallaxPanel.Opacity = 1;
                ((TranslateTransform)parallaxPanel.RenderTransform).Y = 0;

                kiaiPanel.IsVisible = true;
                kiaiPanel.Opacity = 1;
                ((TranslateTransform)kiaiPanel.RenderTransform).Y = 0;
                
                blurPanel.IsVisible = true;
                blurPanel.Opacity = 1;
                ((TranslateTransform)blurPanel.RenderTransform).Y = 0;
            }

            BackgroundToggle.IsCheckedChanged += async (sender, _) =>
            {
                CheckBox? check = sender as CheckBox;
                bool isChecked = check?.IsChecked == true;

                if (isChecked)
                {
                    await EnqueueShowSubToggle(parallaxPanel, true);
                    await EnqueueShowSubToggle(kiaiPanel, true);
                    await EnqueueShowSubToggle(blurPanel, true);
                }
                else
                {
                    await EnqueueShowSubToggle(blurPanel, false);
                    await EnqueueShowSubToggle(kiaiPanel, false);
                    await EnqueueShowSubToggle(parallaxPanel, false);
                }
            };
            
            if (_settingsHandler.IsBackgroundEnabled)
            {
                parallaxPanel.IsVisible = true;
                parallaxPanel.Opacity = 1;
                ((TranslateTransform)parallaxPanel.RenderTransform).Y = 0;

                kiaiPanel.IsVisible = true;
                kiaiPanel.Opacity = 1;
                ((TranslateTransform)kiaiPanel.RenderTransform).Y = 0;
                
                blurPanel.IsVisible = true;
                blurPanel.Opacity = 1;
                ((TranslateTransform)blurPanel.RenderTransform).Y = 0;
            }
            else
            {
                parallaxPanel.IsVisible = false;
                parallaxPanel.Opacity = 0;
                ((TranslateTransform)parallaxPanel.RenderTransform).Y = -20;
                kiaiPanel.IsVisible = false;
                kiaiPanel.Opacity = 0;
                ((TranslateTransform)kiaiPanel.RenderTransform).Y = -20;
                blurPanel.IsVisible = false;
                blurPanel.Opacity = 0;
                ((TranslateTransform)blurPanel.RenderTransform).Y = -20;
            }
        }
        else
        {
            Console.WriteLine("BackgroundToggle or ParallaxTogglePanel or KiaiTogglePanel not found in XAML");
        }

        
        PointerMoved += MainWindow_PointerMoved;

        InitializeKeybindButtonText();
        UpdateDeafenKeybindDisplay();
        CompletionPercentageSlider.Value = ViewModel.MinCompletionPercentage;
        StarRatingSlider.Value = ViewModel.StarRating;
        PPSlider.Value = ViewModel.PerformancePoints;
        BlurEffectSlider.Value = ViewModel.BlurRadius;
        _viewModel.RefreshPresets();
    }

    private void MainWindow_PointerMoved(object? sender, PointerEventArgs e)
    {
        _backgroundManager?.OnMouseMove(sender, e);

        Point pixelPoint = e.GetPosition(PlotView);
        LvcPointD dataPoint = PlotView.ScalePixelsToData(new LvcPointD(pixelPoint.X, pixelPoint.Y));

        Tooltips.TooltipType currentTooltipType = _tooltipManager.CurrentTooltipType;

        if (currentTooltipType == Tooltips.TooltipType.Deafen)
        {
            _ = _chartManager.UpdateDeafenOverlayAsync(_viewModel.MinCompletionPercentage);
            e.Handled = true;
        }

        MainWindow window = this;
        PixelPoint screenPoint = PlotView.PointToScreen(pixelPoint);
        Point windowPoint = new(screenPoint.X - window.Position.X, screenPoint.Y - window.Position.Y);
        
        // this is really stupid but it just prevents the case where the straingraph's tooltips attempt to show in areas outside of the straingraph
        if (currentTooltipType == Tooltips.TooltipType.Time || currentTooltipType == Tooltips.TooltipType.Section)
        {
            bool belowBottomLimit = pixelPoint.Y >= PlotView.Bounds.Height - 120;
            bool withinRightLimit = !_isSettingsPanelOpen || pixelPoint.X <= PlotView.Bounds.Width - 200;

            if (!belowBottomLimit || !withinRightLimit)
            {
                if (!_tooltipOutsideBounds)
                {
                    _tooltipManager.HideTooltip();
                    _tooltipOutsideBounds = true;
                }

                _tooltipManager.MoveTooltipToPosition(windowPoint);
                return;
            }
        }
        else
        {
            if (pixelPoint.Y < PlotView.Bounds.Height - 120)
            {
                if (!_tooltipOutsideBounds)
                {
                    _tooltipManager.HideTooltip();
                    _tooltipOutsideBounds = true;
                }

                _tooltipManager.MoveTooltipToPosition(windowPoint);
                return;
            }
        }

        _tooltipOutsideBounds = false;
        _chartManager.TryShowTooltip(dataPoint, windowPoint, _tooltipManager);
    }
    
    private SharedViewModel ViewModel { get; }
    
    private void UpdateViewModel()
    {
        if (_settingsHandler != null)
        {
            _viewModel.MinCompletionPercentage = _settingsHandler.MinCompletionPercentage;
            _viewModel.StarRating = _settingsHandler.StarRating;
            _viewModel.PerformancePoints = (int)Math.Round(_settingsHandler.PerformancePoints);
            _viewModel.BlurRadius = _settingsHandler.BlurRadius;

            _viewModel.IsFCRequired = _settingsHandler.IsFCRequired;
            _viewModel.UndeafenAfterMiss = _settingsHandler.UndeafenAfterMiss;
            _viewModel.IsBreakUndeafenToggleEnabled = _settingsHandler.IsBreakUndeafenToggleEnabled;

            _viewModel.IsBackgroundEnabled = _settingsHandler.IsBackgroundEnabled;
            _viewModel.IsParallaxEnabled = _settingsHandler.IsParallaxEnabled;
            _viewModel.IsKiaiEffectEnabled = _settingsHandler.IsKiaiEffectEnabled;
        }

        CompletionPercentageSlider.ValueChanged -= CompletionPercentageSlider_ValueChanged;
        StarRatingSlider.ValueChanged -= StarRatingSlider_ValueChanged;
        PPSlider.ValueChanged -= PPSlider_ValueChanged;
        BlurEffectSlider.ValueChanged -= BlurEffectSlider_ValueChanged;

        CompletionPercentageSlider.Value = _viewModel.MinCompletionPercentage;
        StarRatingSlider.Value = _viewModel.StarRating;
        PPSlider.Value = _viewModel.PerformancePoints;
        BlurEffectSlider.Value = _viewModel.BlurRadius;

        CompletionPercentageSlider.ValueChanged += CompletionPercentageSlider_ValueChanged;
        StarRatingSlider.ValueChanged += StarRatingSlider_ValueChanged;
        PPSlider.ValueChanged += PPSlider_ValueChanged;
        BlurEffectSlider.ValueChanged += BlurEffectSlider_ValueChanged;

        FCToggle.IsChecked = _viewModel.IsFCRequired;
        UndeafenOnMissToggle.IsChecked = _viewModel.UndeafenAfterMiss;
        BreakUndeafenToggle.IsChecked = _viewModel.IsBreakUndeafenToggleEnabled;

        BackgroundToggle.IsChecked = _viewModel.IsBackgroundEnabled;
        ParallaxToggle.IsChecked = _viewModel.IsParallaxEnabled;
        KiaiEffectToggle.IsChecked = _viewModel.IsKiaiEffectEnabled;
    }

    private void InitializeSettings()
    {
        _viewModel.MinCompletionPercentage = _settingsHandler.MinCompletionPercentage;
        _viewModel.StarRating = _settingsHandler.StarRating;
        _viewModel.PerformancePoints = (int)Math.Round(_settingsHandler.PerformancePoints);
        _viewModel.BlurRadius = _settingsHandler.BlurRadius;

        _viewModel.IsFCRequired = _settingsHandler.IsFCRequired;
        _viewModel.UndeafenAfterMiss = _settingsHandler.UndeafenAfterMiss;
        _viewModel.IsBreakUndeafenToggleEnabled = _settingsHandler.IsBreakUndeafenToggleEnabled;

        _viewModel.IsBackgroundEnabled = _settingsHandler.IsBackgroundEnabled;
        _viewModel.IsParallaxEnabled = _settingsHandler.IsParallaxEnabled;
        _viewModel.IsKiaiEffectEnabled = _settingsHandler.IsKiaiEffectEnabled;

        CompletionPercentageSlider.ValueChanged -= CompletionPercentageSlider_ValueChanged;
        StarRatingSlider.ValueChanged -= StarRatingSlider_ValueChanged;
        PPSlider.ValueChanged -= PPSlider_ValueChanged;
        BlurEffectSlider.ValueChanged -= BlurEffectSlider_ValueChanged;

        CompletionPercentageSlider.Value = _viewModel.MinCompletionPercentage;
        StarRatingSlider.Value = _viewModel.StarRating;
        PPSlider.Value = _viewModel.PerformancePoints;
        BlurEffectSlider.Value = _viewModel.BlurRadius;

        CompletionPercentageSlider.ValueChanged += CompletionPercentageSlider_ValueChanged;
        StarRatingSlider.ValueChanged += StarRatingSlider_ValueChanged;
        PPSlider.ValueChanged += PPSlider_ValueChanged;
        BlurEffectSlider.ValueChanged += BlurEffectSlider_ValueChanged;

        FCToggle.IsChecked = _viewModel.IsFCRequired;
        UndeafenOnMissToggle.IsChecked = _viewModel.UndeafenAfterMiss;
        BreakUndeafenToggle.IsChecked = _viewModel.IsBreakUndeafenToggleEnabled;

        BackgroundToggle.IsChecked = _viewModel.IsBackgroundEnabled;
        ParallaxToggle.IsChecked = _viewModel.IsParallaxEnabled;
        KiaiEffectToggle.IsChecked = _viewModel.IsKiaiEffectEnabled;

        _viewModel.DeafenKeybind = new HotKey
        {
            Key = _settingsHandler.Data["Hotkeys"]["DeafenKeybindKey"] is { } keyStr &&
                  int.TryParse(keyStr, out int keyVal)
                ? (Key)keyVal
                : Key.None,
            FriendlyName = GetFriendlyKeyName(
                _settingsHandler.Data["Hotkeys"]["DeafenKeybindKey"] is { } keyStr2 &&
                int.TryParse(keyStr2, out int keyVal2)
                    ? (Key)keyVal2
                    : Key.None)
        };
    }

    /// <summary>
    ///     Resets all settings to their default values on click
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="e"></param>
    private async void ResetButton_Click(object sender, RoutedEventArgs e)
    {
        _settingsHandler?.ResetToDefaults();
        UpdateViewModel();
        UpdateDeafenKeybindDisplay();
        try
        {
            _chartManager.UpdateDeafenOverlaySection(_viewModel.MinCompletionPercentage);
        }
        catch (Exception ex)
        {
            Console.WriteLine("[ERROR] Exception when updating Deafen Section after reset: " + ex.Message);
        }
    }
    
    /// <summary>
    /// Slides the sub-toggle control in or out of view
    /// </summary>
    /// <param name="target"></param>
    /// <param name="show"></param>
    private static async Task ShowSubToggle(Control target, bool show)
    {
        if (target == null) return;

        await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            if (target.RenderTransform == null || target.RenderTransform is not TranslateTransform)
                target.RenderTransform = new TranslateTransform();

            var transform = (TranslateTransform)target.RenderTransform;

            if (show)
            {
                target.IsVisible = true;
                target.Opacity = 0;
                transform.Y = -20;
            }

            var animation = new Animation
            {
                Duration = TimeSpan.FromMilliseconds(200),
                Easing = new CubicEaseInOut(),
                FillMode = FillMode.Forward,
                Children =
                {
                    new KeyFrame
                    {
                        Cue = new Cue(0),
                        Setters =
                        {
                            new Setter(OpacityProperty, show ? 0.0 : 1.0),
                            new Setter(TranslateTransform.YProperty, show ? -20.0 : 0.0)
                        }
                    },
                    new KeyFrame
                    {
                        Cue = new Cue(1),
                        Setters =
                        {
                            new Setter(OpacityProperty, show ? 1.0 : 0.0),
                            new Setter(TranslateTransform.YProperty, show ? 0.0 : -20.0)
                        }
                    }
                }
            };

            await animation.RunAsync(target, CancellationToken.None);

            if (!show)
            {
                target.IsVisible = false;
                target.Opacity = 1;
                transform.Y = 0;
            }
        });
    }

    
    private Task EnqueueShowSubToggle(Control? target, bool show)
    {
        if (target == null) return Task.CompletedTask;
        
        if (!_toggleQueues.TryGetValue(target, out var previousTask))
            previousTask = Task.CompletedTask;
        
        var newTask = previousTask.ContinueWith(async _ =>
        {
            await ShowSubToggle(target, show);
        }).Unwrap();

        _toggleQueues[target] = newTask;
        return newTask;
    }
    
    /// <summary>
    ///     Starts the frame timer for debug panel to measure frametimes and framerate
    /// </summary>
    /// <remarks>
    ///  ideally this shouldn't be used elsewhere because this might be a bit more resource-intensive than necessary,
    ///  but for debugging purposes its good enough
    /// </remarks>
    /// <param name="targetFps"></param>
    private void StartStableFrameTimer(int targetFps = 1000)
    {
        StopStableFrameTimer();

        targetFps = Math.Clamp(targetFps, 1, 2000);
        double intervalMs = 1000.0 / targetFps;

        _frameCts = new CancellationTokenSource();
        _frameStopwatch.Restart();

        double minFrame = double.MaxValue, maxFrame = double.MinValue, sumFrame = 0;
        int frameCount = 0, statsWindow = 100;
        long lastFrameTicks = _frameStopwatch.ElapsedTicks;
        double tickMs = 1000.0 / Stopwatch.Frequency;

        Task.Run(async () =>
        {
            try
            {
                while (!_frameCts!.IsCancellationRequested)
                {
                    long frameStartTicks = _frameStopwatch.ElapsedTicks;
                    double frameInterval = (frameStartTicks - lastFrameTicks) * tickMs;
                    lastFrameTicks = frameStartTicks;
                    
                    frameInterval = Math.Max(frameInterval, 0.01);
                    minFrame = Math.Min(minFrame, frameInterval);
                    maxFrame = Math.Max(maxFrame, frameInterval);
                    sumFrame += frameInterval;
                    frameCount++;

                    if (frameCount % statsWindow == 0)
                    {
                        double avgFrame = sumFrame / frameCount;
                        await Dispatcher.UIThread.InvokeAsync(() =>
                        {
                            _logImportant.logImportant(
                                $"Frame: {frameInterval:F3}ms/{1000.0 / avgFrame:F0}fps",
                                false, "FrameLatency");
                            _logImportant.logImportant(
                                $"Min/Max/Avg: {minFrame:F3}/{maxFrame:F3}/{avgFrame:F3}ms",
                                false, "FrameStats");
                        });

                        minFrame = double.MaxValue;
                        maxFrame = double.MinValue;
                        sumFrame = 0;
                        frameCount = 0;
                    }
                    
                    while (true)
                    {
                        double elapsedMs = (_frameStopwatch.ElapsedTicks - frameStartTicks) * tickMs;
                        double remaining = intervalMs - elapsedMs;

                        if (remaining <= 0)
                            break;
                        
                        if (remaining > 2.0)
                        {
                            await Task.Delay(1, _frameCts.Token);
                        }
                        else
                        {
                            Thread.SpinWait(100);
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
            }
            finally
            {
                _frameStopwatch.Stop();
            }
        }, _frameCts.Token);
    }


    /// <summary>
    ///     Stops the frame timer for debug panel
    /// </summary>
    private void StopStableFrameTimer()
    {
        _frameCts?.Cancel();
        _frameStopwatch.Stop();
    }
    
    /*
        we need to start handling all of this slider bullshit somewhere else bro istg
        ESPECIALLY for tooltips because holy hell they are kind of a mess
        i tried making custom tooltips in https://github.com/Aerodite/osuautodeafen/commit/3adb7ab579152bbdc3fb624077bb82c628db6dfc
        but that ultimately ended up being a buggy mess.

        maybe we could grab from the really simple new tooltip system used in the straingraph by TooltipManager.cs,
        but honestly i feel like that entire system might need to be expanded upon because those tooltips are pretty barebones
    */
    /*
     10/20/25 update: yes we are now using TooltipManager.cs for all of these tooltips hiphiphurray
     */
    
    private void SettingsButton_PointerEnter(object sender, PointerEventArgs e)
    {
        if (sender is not Border) return;
        Point point = Tooltips.GetWindowRelativePointer(this, e);
        bool isOpen = _isSettingsPanelOpen;
        _tooltipManager.ShowTooltip(this, point, isOpen ? "Close Settings" : "Open Settings");
    }
    
    private void SettingsButton_PointerLeave(object sender, PointerEventArgs e)
    {
        _tooltipManager.HideTooltip();
    }

    private void CompletionPercentageImage_PointerEnter(object sender, PointerEventArgs e)
    {
        if (sender is not Avalonia.Svg.Svg) return;
        Point point = Tooltips.GetWindowRelativePointer(this, e);
        _tooltipManager.ShowTooltip(this, point, "Minimum Map \nProgress to Deafen");
    }
    
    private void CompletionPercentageImage_PointerLeave(object sender, PointerEventArgs e)
    {
        _tooltipManager.HideTooltip();
    }
    private void CompletionPercentageSlider_PointerEnter(object sender, PointerEventArgs e)
    {
        if (sender is not Slider slider) return;
        Point point = Tooltips.GetWindowRelativePointer(slider, e);
        _tooltipManager.ShowTooltip(this, point, $"{slider.Value:0.00}%");
    }
    
    private void CompletionPercentageSlider_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Slider slider) return;
        Point point = Tooltips.GetWindowRelativePointer(slider, e);
        _tooltipManager.ShowTooltip(this, point, $"{slider.Value:0.00}%");
    }

    private void CompletionPercentageSlider_PointerMove(object? sender, PointerEventArgs e)
    {
        if (sender is not Slider slider) return;
        Point point = Tooltips.GetWindowRelativePointer(slider, e);
        _tooltipManager.ShowTooltip(this, point, $"{slider.Value:0.00}%");
    }
    
    private void CompletionPercentageSlider_PointerLeave(object sender, PointerEventArgs e)
    {
        _tooltipManager.HideTooltip();
    }
    
    private void PPSlider_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Slider slider) return;
        Point point = Tooltips.GetWindowRelativePointer(slider, e);
        _tooltipManager.ShowTooltip(this, point, $"{slider.Value:0}pp");
    }
    
    private void PPSlider_PointerEnter(object sender, PointerEventArgs e)
    {
        if (sender is not Slider slider) return;
        Point point = Tooltips.GetWindowRelativePointer(slider, e);
        _tooltipManager.ShowTooltip(this, point, $"{slider.Value:0}pp");
    }

    private void PPSlider_PointerMove(object? sender, PointerEventArgs e)
    {
        if (sender is not Slider slider) return;
        Point point = Tooltips.GetWindowRelativePointer(slider, e);
        _tooltipManager.ShowTooltip(this, point, $"{slider.Value:0}pp");
    }

    private void PPSlider_PointerLeave(object sender, PointerEventArgs e)
    {
        _tooltipManager.HideTooltip();
    }
    
    private void PPImage_PointerEnter(object sender, PointerEventArgs e)
    {
        if (sender is not Avalonia.Svg.Svg) return;
        Point point = Tooltips.GetWindowRelativePointer(this, e);
        _tooltipManager.ShowTooltip(this, point, "Minimum SS PP to Deafen\n (" + _tosuApi.GetMaxPP() + "pp for this map)");
    }
    
    private void PPImage_PointerLeave(object sender, PointerEventArgs e)
    {
        _tooltipManager.HideTooltip();
    }
    
    private void StarRatingSlider_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Slider slider) return;
        Point point = Tooltips.GetWindowRelativePointer(slider, e);
        _tooltipManager.ShowTooltip(this, point, $"{slider.Value:F1}*");
    }
    
    private void StarRatingSlider_PointerMove(object? sender, PointerEventArgs e)
    {
        if (sender is not Slider slider) return;
        Point point = Tooltips.GetWindowRelativePointer(slider, e);
        _tooltipManager.ShowTooltip(this, point, $"{slider.Value:F1}*");
    }
    
    private void StarRatingSlider_PointerEnter(object? sender, PointerEventArgs e)
    {
        if (sender is not Slider slider) return;
        Point point = Tooltips.GetWindowRelativePointer(slider, e);
        _tooltipManager.ShowTooltip(this, point, $"{slider.Value:F1}*");
    }
    
    private void StarRatingSlider_PointerLeave(object? sender, PointerEventArgs e)
    {
        _tooltipManager.HideTooltip();
    }
    
    private void StarRatingImage_PointerEnter(object sender, PointerEventArgs e)
    {
        if (sender is not Avalonia.Svg.Svg) return;
        Point point = Tooltips.GetWindowRelativePointer(this, e);
        _tooltipManager.ShowTooltip(this, point, "Minimum SR to Deafen\n(" + _tosuApi.GetFullSR() + "* for this map)");
    }
    
    private void StarRatingImage_PointerLeave(object sender, PointerEventArgs e)
    {
        _tooltipManager.HideTooltip();
    }
    
    private void BlurEffectImage_PointerEnter(object sender, PointerEventArgs e)
    {
        if (sender is not Avalonia.Svg.Svg) return;
        Point point = Tooltips.GetWindowRelativePointer(this, e);
        _tooltipManager.ShowTooltip(this, point, "Background Blur Radius\n(0-20 multiplied by 5)");
    }
    
    private void BlurEffectImage_PointerLeave(object sender, PointerEventArgs e)
    {
        _tooltipManager.HideTooltip();
    }
    
    private void BlurEffectSlider_PointerPressed(object sender, PointerPressedEventArgs e)
    {
        if (sender is not Slider slider) return;
        Point point = Tooltips.GetWindowRelativePointer(slider, e);
        _tooltipManager.ShowTooltip(this, point, $"{slider.Value*5:F0}% Blur");
    }
    
    private void BlurEffectSlider_PointerEnter(object sender, PointerEventArgs e)
    {
        if (sender is not Slider slider) return;
        Point point = Tooltips.GetWindowRelativePointer(slider, e);
        _tooltipManager.ShowTooltip(this, point, $"{slider.Value*5:F0}% Blur");
    }
    
    private void BlurEffectSlider_PointerLeave(object sender, PointerEventArgs e)
    {
        _tooltipManager.HideTooltip();
    }
    
    private void FCToggle_PointerEnter(object sender, PointerEventArgs e)
    {
        if (sender is not StackPanel) return;
        Point point = Tooltips.GetWindowRelativePointer(this, e);
        bool isEnabled = FCToggle.IsChecked ?? false;
        _tooltipManager.ShowTooltip(this, point, "" + (isEnabled ? "Disable" : "Enable") + " FC Requirement");
    }
    
    private void FCToggle_PointerLeave(object sender, PointerEventArgs e)
    {
        _tooltipManager.HideTooltip();
    }
    
    private void UndeafenOnMissToggle_PointerEnter(object sender, PointerEventArgs e)
    {
        if (sender is not StackPanel) return;
        Point point = Tooltips.GetWindowRelativePointer(this, e);
        bool isEnabled = UndeafenOnMissToggle.IsChecked ?? false;
        _tooltipManager.ShowTooltip(this, point, "" + (isEnabled ? "Disable" : "Enable") + " Undeafening after a miss");
    }
    
    private void UndeafenOnMissToggle_PointerLeave(object sender, PointerEventArgs e)
    {
        _tooltipManager.HideTooltip();
    }
    
    private void BreakUndeafenToggle_PointerEnter(object sender, PointerEventArgs e)
    {
        if (sender is not StackPanel) return;
        Point point = Tooltips.GetWindowRelativePointer(this, e);
        bool isEnabled = BreakUndeafenToggle.IsChecked ?? false;
        _tooltipManager.ShowTooltip(this, point, "" + (isEnabled ? "Disable" : "Enable") + " Undeafening during breaks");
    }
    
    private void BreakUndeafenToggle_PointerLeave(object sender, PointerEventArgs e)
    {
        _tooltipManager.HideTooltip();
    }
    
    private void BlurEffectSlider_PointerMoved(object? sender, PointerEventArgs e)
    {
        if (sender is not Slider slider) return;
        Point point = Tooltips.GetWindowRelativePointer(slider, e);
        _tooltipManager.ShowTooltip(this, point, $"{slider.Value*5:F0}% Blur");
    }
    
    private void ResetButton_PointerEnter(object sender, PointerEventArgs e)
    {
        if (sender is not Button) 
            return;

        Point pointerPosition = Tooltips.GetWindowRelativePointer(this, e);

        string tooltipText = _settingsHandler!.IsPresetActive
            ? "Reset current preset to default settings"
            : "Reset global settings to default";

        _tooltipManager.ShowTooltip(this, pointerPosition, tooltipText);
    }
    
    private void ResetButton_PointerLeave(object sender, PointerEventArgs e)
    {
        _tooltipManager.HideTooltip();
    }
    
    private void PresetCreate_PointerEnter(object sender, PointerEventArgs e)
    {
        if (sender is not Button) return;
        Point point = Tooltips.GetWindowRelativePointer(this, e);
        _tooltipManager.ShowTooltip(this, point, "Create Preset for\n" + _viewModel.FullBeatmapName);
    }
    
    private void PresetCreate_PointerLeave(object sender, PointerEventArgs e)
    {
        _tooltipManager.HideTooltip();
    }
    
    private void PresetDelete_PointerEnter(object sender, PointerEventArgs e)
    {
        if (sender is not Button) return;
        Point point = Tooltips.GetWindowRelativePointer(this, e);
        _tooltipManager.ShowTooltip(this, point, "Delete Preset for\n" + _viewModel.FullBeatmapName);
    }
    
    private void PresetDelete_PointerLeave(object sender, PointerEventArgs e)
    {
        _tooltipManager.HideTooltip();
    }
    
    private void LoadPresetButton_PointerEnter(object sender, PointerEventArgs e)
    {
        if (sender is not Button) return;
        Point point = Tooltips.GetWindowRelativePointer(this, e);
        _tooltipManager.ShowTooltip(this, point, "Load a different map's preset on to\n" + _viewModel.FullBeatmapName);
    }
    private void LoadPresetButton_PointerLeave(object sender, PointerEventArgs e)
    {
        _tooltipManager.HideTooltip();
    }
    
    private void DeleteAllPresetsButton_PointerEnter(object sender, PointerEventArgs e)
    {
        if (sender is not Button) return;
        Point point = Tooltips.GetWindowRelativePointer(this, e);
        _tooltipManager.ShowTooltip(this, point, "Delete All Presets\n(CAN NOT BE UNDONE)");
    }
    
    private void DeleteAllPresetsButton_PointerLeave(object sender, PointerEventArgs e)
    {
        _tooltipManager.HideTooltip();
    }
    
    private void DeafenKeybindPanel_PointerEnter(object sender, PointerEventArgs e)
    {
        if (sender is not StackPanel) return;
        Point point = Tooltips.GetWindowRelativePointer(this, e);
        _tooltipManager.ShowTooltip(this, point, "Set Deafen Keybind");
    }
    
    private void DeafenKeybindPanel_PointerLeave(object sender, PointerEventArgs e)
    {
        _tooltipManager.HideTooltip();
    }
    
    private void BGToggle_PointerEnter(object sender, PointerEventArgs e)
    {
        if (sender is not StackPanel) return;
        Point point = Tooltips.GetWindowRelativePointer(this, e);
        bool isEnabled = BackgroundToggle.IsChecked ?? false;
        _tooltipManager.ShowTooltip(this, point, "" + (isEnabled ? "Disable" : "Enable") + " Beatmap Background");
    }
    
    private void BGToggle_PointerLeave(object sender, PointerEventArgs e)
    {
        _tooltipManager.HideTooltip();
    }
    
    private void ParallaxToggle_PointerEnter(object sender, PointerEventArgs e)
    {
        if (sender is not StackPanel) return;
        Point point = Tooltips.GetWindowRelativePointer(this, e);
        bool isEnabled = ParallaxToggle.IsChecked ?? false;
        _tooltipManager.ShowTooltip(this, point, "" + (isEnabled ? "Disable" : "Enable") + " Parallax Effect");
    }
    
    private void ParallaxToggle_PointerLeave(object sender, PointerEventArgs e)
    {
        _tooltipManager.HideTooltip();
    }
    
    private void KiaiEffectToggle_PointerEnter(object sender, PointerEventArgs e)
    {
        if (sender is not StackPanel) return;
        Point point = Tooltips.GetWindowRelativePointer(this, e);
        bool isEnabled = KiaiEffectToggle.IsChecked ?? false;
        _tooltipManager.ShowTooltip(this, point, "" + (isEnabled ? "Disable" : "Enable") + " Kiai Effect");
    }
    
    private void KiaiEffectToggle_PointerLeave(object sender, PointerEventArgs e)
    {
        _tooltipManager.HideTooltip();
    }
    
    private void CheckForUpdatesButton_PointerEnter(object sender, PointerEventArgs e)
    {
        if (sender is not Button) return;
        Point point = Tooltips.GetWindowRelativePointer(this, e);
        _tooltipManager.ShowTooltip(this, point, "Check for New Updates");
    }
    
    private void CheckForUpdatesButton_PointerLeave(object sender, PointerEventArgs e)
    {
        _tooltipManager.HideTooltip();
    }
    
    private void FileLocationButton_PointerEnter(object sender, PointerEventArgs e)
    {
        if (sender is not Button) return;
        Point point = Tooltips.GetWindowRelativePointer(this, e);
        _tooltipManager.ShowTooltip(this, point, "Open AppData File Location\n(" + _settingsHandler!.GetPath(true) + ")");
        OpenFileLocationImage.Path = "Icons/folder-open.svg";
    }

    private void FileLocationButton_PointerLeave(object sender, PointerEventArgs e)
    {
        _tooltipManager.HideTooltip();
        OpenFileLocationImage.Path = "Icons/folder.svg";
    }

    private void ReportIssueButton_PointerEnter(object sender, PointerEventArgs e)
    {
        if (sender is not Button) return;
        Point point = Tooltips.GetWindowRelativePointer(this, e);
        _tooltipManager.ShowTooltip(this, point, "Report an Issue on GitHub");
    }
    
    private void ReportIssueButton_PointerLeave(object sender, PointerEventArgs e)
    {
        _tooltipManager.HideTooltip();
    }
    
    private void DebugConsoleButton_PointerEnter(object sender, PointerEventArgs e)
    {
        if (sender is not Button) return;
        Point point = Tooltips.GetWindowRelativePointer(this, e);
        _tooltipManager.ShowTooltip(this, point, "Open Debug Console");
    }
    
    private void DebugConsoleButton_PointerLeave(object sender, PointerEventArgs e)
    {
        _tooltipManager.HideTooltip();
    }

    public async void CompletionPercentageSlider_ValueChanged(object? sender, RangeBaseValueChangedEventArgs e)
    {
        try
        {
            if (DataContext is not SharedViewModel vm) return;
            double roundedValue = Math.Round(e.NewValue, 2);
            vm.MinCompletionPercentage = roundedValue;
            _pendingCompletionPercentage = roundedValue;

            _completionPercentageSaveTimer?.Stop();
            _completionPercentageSaveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            _completionPercentageSaveTimer.Tick += (s, args) =>
            {
                _settingsHandler?.SaveSetting("General", "MinCompletionPercentage", _pendingCompletionPercentage);
                _completionPercentageSaveTimer?.Stop();
            };
            _completionPercentageSaveTimer.Start();

            try
            {
                await _chartManager.UpdateDeafenOverlayAsync(roundedValue);
            }
            catch (TaskCanceledException)
            {
                Console.WriteLine("Task was canceled while updating deafen overlay.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] Exception in CompletionPercentageSlider_ValueChanged: {ex}");
            }
        }
        catch (Exception ex)
        {
            throw new Exception($"Error in CompletionPercentageSlider_ValueChanged: {ex.Message}", ex);
        }
    }

    private void StarRatingSlider_ValueChanged(object? sender, RangeBaseValueChangedEventArgs e)
    {
        if (sender is not Slider slider || DataContext is not SharedViewModel vm) return;
        double roundedValue = Math.Round(slider.Value, 1);
        Console.WriteLine($"Min SR Value: {roundedValue:F1}");
        vm.StarRating = roundedValue;
        _pendingStarRating = roundedValue;

        _starRatingSaveTimer?.Stop();
        _starRatingSaveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _starRatingSaveTimer.Tick += (s, args) =>
        {
            _settingsHandler?.SaveSetting("General", "StarRating", _pendingStarRating);
            _starRatingSaveTimer?.Stop();
        };
        _starRatingSaveTimer.Start();
    }


    private void PPSlider_ValueChanged(object? sender, RangeBaseValueChangedEventArgs e)
    {
        if (sender is not Slider slider || DataContext is not SharedViewModel vm) return;
        int roundedValue = (int)Math.Round(slider.Value);
        Console.WriteLine($"Min PP Value: {roundedValue}");
        vm.PerformancePoints = roundedValue;
        _pendingPP = roundedValue;

        _ppSaveTimer?.Stop();
        _ppSaveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _ppSaveTimer.Tick += (s, args) =>
        {
            _settingsHandler?.SaveSetting("General", "PerformancePoints", _pendingPP);
            _ppSaveTimer?.Stop();
        };
        _ppSaveTimer.Start();
    }

    private void BlurEffectSlider_ValueChanged(object? sender, RangeBaseValueChangedEventArgs e)
    {
        if (sender is not Slider slider || DataContext is not SharedViewModel vm) return;
        double roundedValue = Math.Round(slider.Value, 1);
        Console.WriteLine($"Blur Radius: {roundedValue:F1}");
        vm.BlurRadius = roundedValue;
        _settingsHandler?.SaveSetting("UI", "BlurRadius", roundedValue);
    }

    /// <summary>
    ///     Deletes the preset for the current beatmap if "Yes" is clicked
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="e"></param>
    private void PresetButtonDeleteYes_Click(object sender, RoutedEventArgs e)
    {
        DeletePresetButton.Flyout?.Hide();
        string checksum = _tosuApi.GetBeatmapChecksum();
        string presetsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "osuautodeafen", "presets");
        string presetFilePath = Path.Combine(presetsPath, $"{checksum}.preset");
        if (File.Exists(presetFilePath))
            File.Delete(presetFilePath);
        _viewModel.PresetExistsForCurrentChecksum = false;
        _settingsHandler?.DeactivatePreset();
        CreatePresetButton.Flyout?.Hide();
        UpdateViewModel();
        UpdateDeafenKeybindDisplay();
        try
        {
            _chartManager.UpdateDeafenOverlaySection(_viewModel.MinCompletionPercentage);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERROR] Exception while updating Deafen Section after deleting preset: {ex}");
        }

        DeletePresetData();
        _viewModel.RefreshPresets();
    }

    /// <summary>
    ///     Creates a preset for the current beatmap if "Yes" is clicked
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="e"></param>
    private void PresetButtonYes_Click(object sender, RoutedEventArgs e)
    {
        CreatePresetButton.Flyout?.Hide();
        string checksum = _tosuApi.GetBeatmapChecksum();
        string presetsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "osuautodeafen", "presets");
        string presetFilePath = Path.Combine(presetsPath, $"{checksum}.preset");
        string settingsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "osuautodeafen", "settings.ini");
        File.Copy(settingsPath, presetFilePath, true);
        _viewModel.PresetExistsForCurrentChecksum = true;
        _settingsHandler?.ActivatePreset(presetFilePath);
        CreatePresetData();
        _viewModel.RefreshPresets();
    }

    /// <summary>
    ///     Closes the Create Preset flyout if the user clicked the button by mistake
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="e"></param>
    private void PresetButtonNo_Click(object sender, RoutedEventArgs e)
    {
        CreatePresetButton.Flyout?.Hide();
    }

    /// <summary>
    ///     Closes the Delete Preset flyout if the user clicked the button by mistake
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="e"></param>
    private void PresetButtonDeleteNo_Click(object sender, RoutedEventArgs e)
    {
        DeletePresetButton.Flyout?.Hide();
    }

    /// <summary>
    ///     Applies the selected preset when a preset item is clicked
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="e"></param>
    private void PresetItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is PresetInfo preset)
        {
            string selectedPresetPath = preset.FilePath;
            Console.WriteLine($"Selected Preset Path: {selectedPresetPath}");
            string currentChecksum = _tosuApi.GetBeatmapChecksum();
            string presetsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "osuautodeafen", "presets");
            string currentPresetPath = Path.Combine(presetsPath, $"{currentChecksum}.preset");

            // selectedPresetPath ends with .data because that is what is being used to display the background,
            // this just ensures we copy from the right file
            string presetSourcePath = selectedPresetPath.EndsWith(".data")
                ? selectedPresetPath.Substring(0, selectedPresetPath.Length - 5)
                : selectedPresetPath;

            File.Copy(presetSourcePath, currentPresetPath, true);
            _settingsHandler?.ActivatePreset(currentPresetPath);
            _viewModel.PresetExistsForCurrentChecksum = true;

            CreatePresetData();
            UpdateViewModel();
            UpdateDeafenKeybindDisplay();
            try
            {
                _chartManager.UpdateDeafenOverlaySection(_viewModel.MinCompletionPercentage);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] Exception while updating Deafen Section after applying preset: {ex}");
            }

            btn.Flyout?.Hide();
        }
    }

    /// <summary>
    ///     Opens the preset selection flyout and refreshes the presets list
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="e"></param>
    private void LoadPresetButton_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.RefreshPresets();
        if (sender is Button btn) btn.Flyout?.ShowAt(btn);
    }

    /// <summary>
    ///     Creates a .preset.data file that contains beatmap information for the current beatmap
    /// </summary>
    private void CreatePresetData()
    {
        string checksum = _tosuApi.GetBeatmapChecksum();
        string presetsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "osuautodeafen", "presets");
        string presetDataFilePath = Path.Combine(presetsPath, $"{checksum}.preset.data");

        string? artist = _tosuApi.GetBeatmapArtist();
        string? beatmapName = _tosuApi.GetBeatmapTitle();
        string fullBeatmapName = $"{artist} - {beatmapName}";
        string beatmapDifficulty = _tosuApi.GetBeatmapDifficulty();
        string backgroundPath = _tosuApi.GetBackgroundPath();
        string beatmapId = _tosuApi.GetBeatmapId().ToString();
        string rankedStatus = _tosuApi.GetRankedStatus().ToString(CultureInfo.InvariantCulture);
        string starRating = _tosuApi.GetFullSR().ToString("F1", CultureInfo.InvariantCulture);
        string mapper = _tosuApi.GetBeatmapMapper();


        LogoUpdater? logoUpdater = _backgroundManager?.LogoUpdater;
        string avgColor1 = logoUpdater?.AverageColor1.ToString() ?? "#000000";
        string avgColor2 = logoUpdater?.AverageColor2.ToString() ?? "#000000";
        string avgColor3 = logoUpdater?.AverageColor3.ToString() ?? "#000000";

        var lines = new List<string>
        {
            "[Preset]",
            $"FullBeatmapName={fullBeatmapName}",
            $"Artist={artist}",
            $"BeatmapName={beatmapName}",
            $"BeatmapDifficulty={beatmapDifficulty}",
            $"BeatmapID={beatmapId}",
            $"RankedStatus={rankedStatus}",
            $"BackgroundPath={backgroundPath}",
            $"Mapper={mapper}",
            $"Checksum={checksum}",
            $"StarRating={starRating}",
            $"AverageColor1={avgColor1}",
            $"AverageColor2={avgColor2}",
            $"AverageColor3={avgColor3}"
        };

        File.WriteAllLines(presetDataFilePath, lines);
    }

    /// <summary>
    ///     Deletes the .preset.data file for the current beatmap if it exists
    /// </summary>
    private void DeletePresetData()
    {
        string checksum = _tosuApi.GetBeatmapChecksum();
        string presetsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "osuautodeafen", "presets");
        string presetDataFilePath = Path.Combine(presetsPath, $"{checksum}.preset.data");

        if (File.Exists(presetDataFilePath))
            File.Delete(presetDataFilePath);
    }

    private void DeleteAllPresetData()
    {
        string presetsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "osuautodeafen", "presets");
        if (Directory.Exists(presetsPath))
        {
            string[] presetDataFiles = Directory.GetFiles(presetsPath, "*.preset.data");
            foreach (string file in presetDataFiles)
                try
                {
                    File.Delete(file);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[ERROR] Could not delete preset data file {file}: {ex}");
                }
        }

        _viewModel.RefreshPresets();
    }

    private void DeleteAllPresetsButtonYes_Click(object sender, RoutedEventArgs e)
    {
        DeleteAllPresetsButton.Flyout?.Hide();
        string presetsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "osuautodeafen", "presets");
        if (Directory.Exists(presetsPath))
        {
            string[] presetFiles = Directory.GetFiles(presetsPath, "*.preset");
            foreach (string file in presetFiles)
                try
                {
                    File.Delete(file);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[ERROR] Could not delete preset file {file}: {ex}");
                }
        }

        DeleteAllPresetData();
        _settingsHandler?.DeactivatePreset();
        _viewModel.PresetExistsForCurrentChecksum = false;
        UpdateViewModel();
        UpdateDeafenKeybindDisplay();
        try
        {
            _chartManager.UpdateDeafenOverlaySection(_viewModel.MinCompletionPercentage);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERROR] Exception while updating Deafen Section after deleting all presets: {ex}");
        }
    }

    private void DeleteAllPresetsButtonNo_Click(object sender, RoutedEventArgs e)
    {
        DeleteAllPresetsButton.Flyout?.Hide();
    }

    private async void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        try
        {
            switch (e.PropertyName)
            {
                case nameof(SharedViewModel.CompletionPercentage):
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        if (ProgressOverlay == null)
                            return;

                        ProgressOverlay.ChartXMin = _progressIndicatorHelper.ChartXMin;
                        ProgressOverlay.ChartXMax = _progressIndicatorHelper.ChartXMax;
                        ProgressOverlay.ChartYMin = _progressIndicatorHelper.ChartYMin;
                        ProgressOverlay.ChartYMax = _progressIndicatorHelper.ChartYMax;

                        var points =
                            _progressIndicatorHelper.CalculateSmoothProgressContour(_tosuApi.GetCompletionPercentage(),
                                force: true);
                        ProgressOverlay.Points = points;
                    });
                    break;
                case nameof(_viewModel.BlurRadius):
                {
                    BlurEffect? blurEffect = _backgroundManager?.BackgroundBlurEffect;
                    if (blurEffect != null && _backgroundManager != null)
                    {
                        if (_blurCts != null)
                            await _blurCts.CancelAsync();
                        _blurCts = new CancellationTokenSource();

                        double radius = _viewModel.BlurRadius;
                        CancellationToken token = _blurCts.Token;
                        await _backgroundManager.BlurBackgroundAsync(blurEffect, radius, token);
                    }

                    break;
                }
                case nameof(SharedViewModel.IsBackgroundEnabled):
                {
                    _settingsHandler?.SaveSetting("UI", "IsBackgroundEnabled", _viewModel.IsBackgroundEnabled);

                    if (!_viewModel.IsBackgroundEnabled)
                    {
                        _kiaiBrightnessTimer?.Stop();
                        _kiaiBrightnessTimer = null;
                        _backgroundManager?.RemoveBackgroundOpacityRequest("kiai");
                    }
                    else
                    {
                        await _backgroundManager?.UpdateBackground(null, null)!;

                        if (_tosuApi._isKiai && _viewModel.IsKiaiEffectEnabled)
                        {
                            double bpm = _tosuApi.GetCurrentBpm();
                            double intervalMs = 60000.0 / bpm;

                            _kiaiBrightnessTimer?.Stop();
                            _kiaiBrightnessTimer = new DispatcherTimer
                            {
                                Interval = TimeSpan.FromMilliseconds(intervalMs)
                            };
                            _kiaiBrightnessTimer.Tick += async (_, _) =>
                            {
                                _isKiaiPulseHigh = !_isKiaiPulseHigh;
                                double opacityValue = _isKiaiPulseHigh ? 1.0 - _opacity : 0.95 - _opacity;
                                await _backgroundManager.RequestBackgroundOpacity("kiai", opacityValue, 10000,
                                    (int)(intervalMs / 4));
                            };
                            _kiaiBrightnessTimer.Start();
                        }
                    }

                    break;
                }
                case nameof(SharedViewModel.IsParallaxEnabled):
                    _settingsHandler?.SaveSetting("UI", "IsParallaxEnabled", _viewModel.IsParallaxEnabled);
                    break;
                case nameof(SharedViewModel.IsKiaiEffectEnabled):
                    _settingsHandler?.SaveSetting("UI", "IsKiaiEffectEnabled", _viewModel.IsKiaiEffectEnabled);
                    _tosuApi.RaiseKiaiChanged();
                    break;
                case nameof(SharedViewModel.IsBreakUndeafenToggleEnabled):
                    _settingsHandler?.SaveSetting("Behavior", "IsBreakUndeafenToggleEnabled",
                        _viewModel.IsBreakUndeafenToggleEnabled);
                    break;
                case nameof(SharedViewModel.UndeafenAfterMiss):
                    _settingsHandler?.SaveSetting("Behavior", "UndeafenAfterMiss", _viewModel.UndeafenAfterMiss);
                    break;
                case nameof(SharedViewModel.IsFCRequired):
                    _settingsHandler?.SaveSetting("Behavior", "IsFCRequired", _viewModel.IsFCRequired);
                    break;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERROR] Exception in ViewModel_PropertyChanged: {ex}");
        }
    }

    /// <summary>
    ///     Handles updates to the graph data and refreshes the strain graph
    /// </summary>
    /// <param name="graphData"></param>
    private void OnGraphDataUpdated(GraphData? graphData)
    {
        if (graphData == null || graphData.Series.Count < 2)
            return;

        if (ReferenceEquals(graphData, _lastGraphData))
            return;
        _lastGraphData = graphData;

        Series series0 = graphData.Series[0];
        Series series1 = graphData.Series[1];
        series0.Name = "aim";
        series1.Name = "speed";

        if (ChartData.Series1Values.Count != series0.Data.Count)
        {
            var list0 = new List<ObservablePoint>(series0.Data.Count);
            for (int i = 0; i < series0.Data.Count; i++)
                list0.Add(new ObservablePoint(i, series0.Data[i]));
            ChartData.Series1Values = list0;
        }

        if (ChartData.Series2Values.Count != series1.Data.Count)
        {
            var list1 = new List<ObservablePoint>(series1.Data.Count);
            for (int i = 0; i < series1.Data.Count; i++)
                list1.Add(new ObservablePoint(i, series1.Data[i]));
            ChartData.Series2Values = list1;
        }

        Dispatcher.UIThread.InvokeAsync(() =>
            _chartManager.UpdateChart(graphData, ViewModel.MinCompletionPercentage));
    }

    private async void InitializeViewModel()
    {
        //await CheckForUpdates();
        DataContext = ViewModel;
    }

    /// <summary>
    ///     Initializes the button with the selected keybind from settings
    /// </summary>
    private void UpdateDeafenKeybindDisplay()
    {
        string currentKeybind = RetrieveKeybindFromSettings();
        DeafenKeybindButton.Content = currentKeybind;
    }

    /// <summary>
    ///     Shows a flyout for the user to set a new deafen keybind
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="e"></param>
    private void DeafenKeybindButton_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.IsKeybindCaptureFlyoutOpen = !ViewModel.IsKeybindCaptureFlyoutOpen;
        Flyout? flyout = DeafenKeybindButton.Flyout as Flyout;
        if (flyout != null)
        {
            if (ViewModel.IsKeybindCaptureFlyoutOpen)
                flyout.ShowAt(DeafenKeybindButton, true);
            else
                flyout.Hide();
        }
    }
    
    /// <summary>
    ///     Captures key presses when the keybind capture flyout is open
    /// </summary>
    /// <param name="e"></param>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        _pressedKeys.Add(e.Key);

        if (ViewModel.IsKeybindCaptureFlyoutOpen)
        {
            Flyout? flyout = DeafenKeybindButton.Flyout as Flyout;
            if (e.Key == Key.Escape)
            {
                ViewModel.IsKeybindCaptureFlyoutOpen = false;
                flyout?.Hide();
                return;
            }
            
            if (_pressedKeys.All(IsModifierKey))
            {
                if (_modifierOnlyTimer == null || !_modifierOnlyTimer.IsEnabled)
                {
                    _modifierOnlyTimer?.Stop();
                    _modifierOnlyTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
                    _modifierOnlyTimer.Start();
                }
                e.Handled = true;
                return;
            }
            else
            {
                _modifierOnlyTimer?.Stop();
            }
            
            DateTime currentTime = DateTime.Now;
            if (e.Key == _lastKeyPressed && (currentTime - _lastKeyPressTime).TotalMilliseconds < 2500)
                return;
            _lastKeyPressed = e.Key;
            _lastKeyPressTime = currentTime;
            
            if (IsModifierKey(e.Key))
                return;

            KeyModifiers modifiers = KeyModifiers.None;
            if (_pressedKeys.Contains(Key.LeftCtrl)) modifiers |= KeyModifiers.Control;
            if (_pressedKeys.Contains(Key.RightCtrl)) modifiers |= KeyModifiers.Control;
            if (_pressedKeys.Contains(Key.LeftAlt)) modifiers |= KeyModifiers.Alt;
            if (_pressedKeys.Contains(Key.RightAlt)) modifiers |= KeyModifiers.Alt;
            if (_pressedKeys.Contains(Key.LeftShift)) modifiers |= KeyModifiers.Shift;
            if (_pressedKeys.Contains(Key.RightShift)) modifiers |= KeyModifiers.Shift;

            Modifiers.ModifierSide controlSide = Modifiers.ModifierSide.None;
            if (_pressedKeys.Contains(Key.LeftCtrl)) controlSide = Modifiers.ModifierSide.Left;
            else if (_pressedKeys.Contains(Key.RightCtrl)) controlSide = Modifiers.ModifierSide.Right;

            Modifiers.ModifierSide altSide = Modifiers.ModifierSide.None;
            if (_pressedKeys.Contains(Key.LeftAlt)) altSide = Modifiers.ModifierSide.Left;
            else if (_pressedKeys.Contains(Key.RightAlt)) altSide = Modifiers.ModifierSide.Right;

            Modifiers.ModifierSide shiftSide = Modifiers.ModifierSide.None;
            if (_pressedKeys.Contains(Key.LeftShift)) shiftSide = Modifiers.ModifierSide.Left;
            else if (_pressedKeys.Contains(Key.RightShift)) shiftSide = Modifiers.ModifierSide.Right;

            string friendlyKeyName = GetFriendlyKeyName(e.Key);
            HotKey hotKey = new()
            {
                Key = e.Key,
                ModifierKeys = modifiers,
                ControlSide = controlSide,
                AltSide = altSide,
                ShiftSide = shiftSide,
                FriendlyName = friendlyKeyName
            };
            ViewModel.DeafenKeybind = hotKey;

            _settingsHandler?.SaveSetting("Hotkeys", "DeafenKeybindKey", (int)e.Key);
            _settingsHandler?.SaveSetting("Hotkeys", "DeafenKeybindControlSide", (int)controlSide);
            _settingsHandler?.SaveSetting("Hotkeys", "DeafenKeybindAltSide", (int)altSide);
            _settingsHandler?.SaveSetting("Hotkeys", "DeafenKeybindShiftSide", (int)shiftSide);

            ViewModel.IsKeybindCaptureFlyoutOpen = false;
            UpdateDeafenKeybindDisplay();

            e.Handled = true;
            flyout?.Hide();
            return;
        }

        switch (e.Key)
        {
            case Key.D when e.KeyModifiers.HasFlag(KeyModifiers.Control) && e.KeyModifiers.HasFlag(KeyModifiers.Shift):
                Dispatcher.UIThread.InvokeAsync(() => ToggleDebugConsole(null, null!));
                e.Handled = true;
                return;
            case Key.O when e.KeyModifiers.HasFlag(KeyModifiers.Control):
                Dispatcher.UIThread.InvokeAsync(() => SettingsButton_Click(null, null));
                e.Handled = true;
                break;
        }
    }

    /// <summary>
    ///    Captures key releases when the keybind capture flyout is open
    /// </summary>
    /// <param name="e"></param>
    protected override void OnKeyUp(KeyEventArgs e)
    {
        base.OnKeyUp(e);
        _pressedKeys.Remove(e.Key);
        
        if (ViewModel.IsKeybindCaptureFlyoutOpen && _modifierOnlyTimer?.IsEnabled == true && IsModifierKey(e.Key))
        {
            _modifierOnlyTimer.Stop();

            Flyout? flyout = DeafenKeybindButton.Flyout as Flyout;
            
            var allModifiers = new HashSet<Key>(_pressedKeys) { e.Key };

            KeyModifiers modifiers = KeyModifiers.None;
            if (allModifiers.Contains(Key.LeftCtrl)) modifiers |= KeyModifiers.Control;
            if (allModifiers.Contains(Key.RightCtrl)) modifiers |= KeyModifiers.Control;
            if (allModifiers.Contains(Key.LeftAlt)) modifiers |= KeyModifiers.Alt;
            if (allModifiers.Contains(Key.RightAlt)) modifiers |= KeyModifiers.Alt;
            if (allModifiers.Contains(Key.LeftShift)) modifiers |= KeyModifiers.Shift;
            if (allModifiers.Contains(Key.RightShift)) modifiers |= KeyModifiers.Shift;

            Modifiers.ModifierSide controlSide = Modifiers.ModifierSide.None;
            if (allModifiers.Contains(Key.LeftCtrl)) controlSide = Modifiers.ModifierSide.Left;
            else if (allModifiers.Contains(Key.RightCtrl)) controlSide = Modifiers.ModifierSide.Right;

            Modifiers.ModifierSide altSide = Modifiers.ModifierSide.None;
            if (allModifiers.Contains(Key.LeftAlt)) altSide = Modifiers.ModifierSide.Left;
            else if (allModifiers.Contains(Key.RightAlt)) altSide = Modifiers.ModifierSide.Right;

            Modifiers.ModifierSide shiftSide = Modifiers.ModifierSide.None;
            if (allModifiers.Contains(Key.LeftShift)) shiftSide = Modifiers.ModifierSide.Left;
            else if (allModifiers.Contains(Key.RightShift)) shiftSide = Modifiers.ModifierSide.Right;

            HotKey hotKey = new()
            {
                Key = Key.None,
                ModifierKeys = modifiers,
                ControlSide = controlSide,
                AltSide = altSide,
                ShiftSide = shiftSide,
                FriendlyName = modifiers.ToString()
            };
            ViewModel.DeafenKeybind = hotKey;

            _settingsHandler?.SaveSetting("Hotkeys", "DeafenKeybindKey", (int)Key.None);
            _settingsHandler?.SaveSetting("Hotkeys", "DeafenKeybindControlSide", (int)controlSide);
            _settingsHandler?.SaveSetting("Hotkeys", "DeafenKeybindAltSide", (int)altSide);
            _settingsHandler?.SaveSetting("Hotkeys", "DeafenKeybindShiftSide", (int)shiftSide);

            ViewModel.IsKeybindCaptureFlyoutOpen = false;
            UpdateDeafenKeybindDisplay();

            flyout?.Hide();
        }
    }

    /// <summary>
    ///     Retrieves the currently set keybind from settings
    /// </summary>
    /// <returns></returns>
    private string RetrieveKeybindFromSettings()
    {
        KeyDataCollection? hotkeys = _settingsHandler?.Data["Hotkeys"];
        if (hotkeys == null)
            return "Set Keybind";

        string? keyStr = hotkeys["DeafenKeybindKey"];
        string? controlSideStr = hotkeys["DeafenKeybindControlSide"];
        string? altSideStr = hotkeys["DeafenKeybindAltSide"];
        string? shiftSideStr = hotkeys["DeafenKeybindShiftSide"];

        if (string.IsNullOrEmpty(keyStr))
            return "Set Keybind";

        if (!int.TryParse(keyStr, out int keyVal))
            return "Set Keybind";

        string display = "";

        if (int.TryParse(controlSideStr, out int controlSide) && controlSide != 0)
            display += controlSide == 2 ? "RCtrl+" : "LCtrl+";
        if (int.TryParse(altSideStr, out int altSide) && altSide != 0)
            display += altSide == 2 ? "RAlt+" : "LAlt+";
        if (int.TryParse(shiftSideStr, out int shiftSide) && shiftSide != 0)
            display += shiftSide == 2 ? "RShift+" : "LShift+";

        if (keyVal == (int)Key.None)
        {
            // signifies that only modifiers are used, so we should remove the trailing +
            return display.EndsWith('+') ? display[..^1] : (display.Length > 0 ? display : "Set Keybind");
        }

        display += GetFriendlyKeyName((Key)keyVal);
        return display;
    }

    /// <summary>
    ///     Converts certain keys to more user-friendly names for display purposes
    /// </summary>
    /// <param name="key"></param>
    /// <returns></returns>
    private string GetFriendlyKeyName(Key key)
    {
        return key switch
        {
            Key.D0 => "0",
            Key.D1 => "1",
            Key.D2 => "2",
            Key.D3 => "3",
            Key.D4 => "4",
            Key.D5 => "5",
            Key.D6 => "6",
            Key.D7 => "7",
            Key.D8 => "8",
            Key.D9 => "9",
            Key.OemOpenBrackets => "[",
            Key.OemCloseBrackets => "]",
            Key.OemComma => ",",
            Key.OemPeriod => ".",
            Key.OemMinus => "-",
            Key.OemPlus => "+",
            Key.OemQuestion => "/",
            Key.OemSemicolon => ";",
            Key.OemQuotes => "'",
            Key.OemBackslash => "\\",
            Key.OemPipe => "|",
            Key.OemTilde => "`",
            Key.Oem8 => "`",
            _ => key.ToString()
        };
    }

    /// <summary>
    ///     Displays the update notification bar and initializes progress bar
    /// </summary>
    public async void ShowUpdateNotification()
    {
        Console.WriteLine("Showing Update Notification");
        _updateNotificationBarButton = this.FindControl<Button>("UpdateNotificationBar");
        _updateProgressBar = this.FindControl<ProgressBar>("UpdateProgressBar");

        if (_updateNotificationBarButton != null)
            _updateNotificationBarButton.IsVisible = true;
        else
            Console.WriteLine("Notification bar control not found.");

        if (_updateProgressBar != null)
        {
            _updateProgressBar.Value = 0;
            _updateProgressBar.Foreground = Brushes.Green;
        }
    }

    /// <summary>
    ///     Downloads the newest version from GitHub while ensuring a nice progress bar is visible
    /// </summary>
    private async Task DownloadUpdateWithProgressAsync()
    {
        int minDisplayMs = 1500;
        Stopwatch sw = Stopwatch.StartNew();
        Console.WriteLine("Starting update download...");
        if (_updateChecker.UpdateInfo == null)
            return;

        if (_updateProgressBar != null)
            _updateProgressBar.Value = 0;

        var progress = new Action<int>(p =>
        {
            if (_updateProgressBar != null)
                _updateProgressBar.Value = p;
        });

        Stopwatch displaySw = Stopwatch.StartNew();
        await _updateChecker.Mgr.DownloadUpdatesAsync(_updateChecker.UpdateInfo, progress);
        displaySw.Stop();

        // sorry guys sunk cost fallacy this took too long to implement
        // have fun with your "slow" wifi :tf:
        if (_updateProgressBar != null && _updateProgressBar.Value < 100)
        {
            int steps = 30;
            double start = _updateProgressBar.Value;
            double end = 100;
            int remainingMs = minDisplayMs - (int)displaySw.ElapsedMilliseconds;
            if (remainingMs < 0) remainingMs = 0;
            remainingMs /= 5;
            int delayPerStep = remainingMs / steps;

            for (int i = 1; i <= steps; i++)
            {
                _updateProgressBar.Value = start + ((end - start) * i / steps);
                await Task.Delay(delayPerStep);
            }
        }
        else
        {
            if (_updateProgressBar != null)
                _updateProgressBar.Value = 100;
        }

        if (_updateNotificationBarButton != null)
        {
            _updateNotificationBarButton.IsEnabled = true;
            _updateNotificationBarButton.Background = Brushes.Green;
        }

        sw.Stop();
        Console.WriteLine($"Update download completed in {sw.ElapsedMilliseconds} ms");
    }

    /// <summary>
    ///     Handles the click event on the update notification bar to start the update process
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="e"></param>
    private async void UpdateNotificationBar_Click(object sender, RoutedEventArgs e)
    {
        await DownloadUpdateWithProgressAsync();
        _updateChecker.Mgr.ApplyUpdatesAndRestart(_updateChecker.UpdateInfo);
    }

    /// <summary>
    ///     Main timer that handles checking states in the API
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="e"></param>
    private void MainTimer_Tick(object? sender, EventArgs? e)
    {
        _tosuApi.CheckForBeatmapChange();
        _tosuApi.CheckForModChange();
        _tosuApi.CheckForBPMChange();
        _tosuApi.CheckForKiaiChange();
        _tosuApi.CheckForRateAdjustChange();
        _tosuApi.CheckForStateChange();
        _breakPeriod.UpdateBreakPeriodState(_tosuApi);
        _tosuApi.CheckForPercentageChange();
        _kiaiTimes.UpdateKiaiPeriodState(_tosuApi.GetCurrentTime());
        _logImportant.logImportant("Velopack: " + _updateChecker.Mgr.IsInstalled, false, "Velopack");
        _logImportant.logImportant("Tosu Connected: " + _tosuApi.isWebsocketConnected, false, "Tosu Running");
    }

    /// <summary>
    ///     Checks for newer versions when the button is clicked
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="e"></param>
    public async void CheckForUpdatesButton_Click(object sender, RoutedEventArgs e)
    {
        if (!await _updateCheckLock.WaitAsync(0))
            return; // already running

        try
        {
            Button? button = this.FindControl<Button>("CheckForUpdatesButton");
            if (button == null) return;

            button.Content = "Checking for updates...";
            await Task.Delay(1000);

            await _updateChecker.CheckForUpdatesAsync();
            if (_updateChecker?.Mgr.IsInstalled == false)
            {
                button.Content = "Velopack not installed...";
                await Task.Delay(1000);
                button.Content = "Please reinstall osuautodeafen";
                await Task.Delay(1000);
                button.Content = "Check for Updates";
                return;
            }

            if (_updateChecker?.UpdateInfo == null)
            {
                button.Content = "No updates found";
                await Task.Delay(1000);
                button.Content = "Check for Updates";
                return;
            }

            button.Content = "Update available!";
            ShowUpdateNotification();
        }
        finally
        {
            _updateCheckLock.Release();
        }
    }

    /// <summary>
    ///     Determines if the pressed key is a modifier key
    /// </summary>
    /// <param name="key"></param>
    /// <returns></returns>
    private bool IsModifierKey(Key key)
    {
        return key == Key.LeftCtrl || key == Key.RightCtrl ||
               key == Key.LeftAlt || key == Key.RightAlt ||
               key == Key.LeftShift || key == Key.RightShift;
    }

    /// <summary>
    ///     Loads an SVG resource as an SKSvg object
    /// </summary>
    /// <param name="resourceName"></param>
    /// <returns></returns>
    /// <exception cref="FileNotFoundException"></exception>
    public SKSvg LoadSkSvgResource(string resourceName)
    {
        using Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName)
                              ?? throw new FileNotFoundException("Resource not found: " + resourceName);
        SKSvg svg = new();
        svg.Load(stream);
        return svg;
    }

    /// <summary>
    ///     Initializes the logo control
    /// </summary>
    private async void InitializeLogo()
    {
        const string resourceName = "osuautodeafen.Resources.autodeafen.svg";
        try
        {
            SKSvg svg = await Task.Run(() => LoadSkSvgResource(resourceName));

            if (_logoControl == null)
                _logoControl = new LogoControl
                {
                    Width = 240,
                    Height = 72,
                    VerticalAlignment = VerticalAlignment.Center
                };
            _logoControl.Svg = svg;
            _logoControl.ModulateColor = SKColors.White;

            ContentControl? logoHost = this.FindControl<ContentControl>("LogoHost");
            if (logoHost != null)
                logoHost.Content = _logoControl;

            _backgroundManager!.LogoUpdater = new LogoUpdater(_getLowResBackground, _logoControl, ViewModel, LoadSkSvgResource);

            Console.WriteLine("SVG loaded successfully.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Exception while loading logo image: {ex.Message}");
            try
            {
                await RetryLoadLogoAsync(resourceName);
            }
            catch (Exception retryEx)
            {
                Console.WriteLine($"RetryLoadLogoAsync failed: {retryEx.Message}");
            }
        }
    }

    /// <summary>
    ///     Loads an SVG logo from embedded resources and converts it to a Bitmap
    /// </summary>
    /// <param name="resourceName"></param>
    /// <returns></returns>
    /// <exception cref="FileNotFoundException"></exception>
    /// <exception cref="InvalidOperationException"></exception>
    private Task<Bitmap> LoadLogoAsync(string resourceName)
    {
        using Stream resourceStream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName)
                                      ?? throw new FileNotFoundException("Resource not found: " + resourceName);

        SKSvg svg = new();
        svg.Load(resourceStream);

        return Task.FromResult(svg.Picture == null
            ? throw new InvalidOperationException("Failed to load SVG picture.")
            : ConvertSvgToBitmap(svg, 100, 100));
    }

    /// <summary>
    ///     Converts an SKSvg object to a Bitmap with specified dimensions
    /// </summary>
    /// <param name="svg"></param>
    /// <param name="width"></param>
    /// <param name="height"></param>
    /// <returns></returns>
    /// <exception cref="ArgumentNullException"></exception>
    /// <exception cref="InvalidOperationException"></exception>
    private Bitmap ConvertSvgToBitmap(SKSvg svg, int width, int height)
    {
        if (svg == null)
            throw new ArgumentNullException(nameof(svg));
        if (svg.Picture == null)
            throw new InvalidOperationException("SVG does not contain a valid picture.");

        SKImageInfo info = new(width, height);

        try
        {
            using SKSurface? surface = SKSurface.Create(info);
            if (surface == null)
                throw new InvalidOperationException("Failed to create SKSurface.");

            SKCanvas? canvas = surface.Canvas;
            canvas.Clear(SKColors.Transparent);
            canvas.DrawPicture(svg.Picture);

            using SKImage? image = surface.Snapshot();
            using SKData? data = image.Encode();
            using MemoryStream stream = new(data.ToArray());
            return new Bitmap(stream);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERROR] ConvertSvgToBitmap failed: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    ///     Retries loading the SVG logo multiple times in case of failure
    /// </summary>
    /// <param name="resourceName"></param>
    private async Task RetryLoadLogoAsync(string resourceName)
    {
        const int maxRetries = 3;
        int retryCount = 0;
        bool success = false;

        while (retryCount < maxRetries && !success)
            try
            {
                retryCount++;
                Console.WriteLine($"Retrying to load SVG... Attempt {retryCount}");
                Bitmap logoImage = await LoadLogoAsync(resourceName);
                UpdateViewModelWithLogo(logoImage);
                success = true;
            }
            catch (Exception retryEx)
            {
                Console.WriteLine($"[ERROR] Retry {retryCount} failed: {retryEx.Message}");
                if (retryCount >= maxRetries)
                {
                    Console.WriteLine(
                        $"[ERROR] Exception while loading SVG after {maxRetries} attempts: {retryEx.Message}");
                    return;
                }
            }
    }

    /// <summary>
    ///     Updates the ViewModel with the loaded logo image
    /// </summary>
    /// <param name="logoImage"></param>
    private void UpdateViewModelWithLogo(Bitmap logoImage)
    {
        SharedViewModel? viewModel = DataContext as SharedViewModel;
        if (viewModel != null)
        {
            viewModel.ModifiedLogoImage = logoImage;
            Console.WriteLine("ModifiedLogoImage property set.");
        }
    }

    /// <summary>
    ///     Handles the closing event of the main window to clean up resources
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="e"></param>
    private void MainWindow_Closing(object? sender, CancelEventArgs e)
    {
        _mainTimer?.Stop();
        _cogSpinTimer?.Stop();
        _kiaiBrightnessTimer?.Stop();
        _tosuApi.Dispose();
    }

    /// <summary>
    ///     Shows or hides the settings panel if the button is clicked
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="e"></param>
    private async void SettingsButton_Click(object? sender, RoutedEventArgs? e)
    {
        try
        {
            _settingsButtonClicked?.Invoke();
            DockPanel? settingsPanel = this.FindControl<DockPanel>("SettingsPanel");
            Border? buttonContainer = this.FindControl<Border>("SettingsButtonContainer");
            Avalonia.Svg.Svg? cogImage = this.FindControl<Avalonia.Svg.Svg>("SettingsCogImage");
            StackPanel? textBlockPanel = this.FindControl<StackPanel>("TextBlockPanel");
            TextBlock? versionPanel = textBlockPanel?.FindControl<TextBlock>("VersionPanel");
            TextBlock? debugConsoleTextBlock = this.FindControl<TextBlock>("DebugConsoleTextBlock");
            if (settingsPanel == null || buttonContainer == null || cogImage == null ||
                textBlockPanel == null || osuautodeafenLogoPanel == null || versionPanel == null ||
                debugConsoleTextBlock == null)
                return;

            Thickness showMargin = new(0, 42, 0, 0);
            Thickness hideMargin = new(200, 42, -200, 0);
            Thickness buttonRightMargin = new(0, 42, 0, 10);
            Thickness buttonLeftMargin = new(0, 42, 200, 10);

            if (!settingsPanel.IsVisible)
            {
                await Task.WhenAll(
                    EnsureCogCenterAsync(cogImage),
                    SetupPanelTransitionsAsync(settingsPanel, buttonContainer, versionPanel)
                );
                StartCogSpin(cogImage);
                settingsPanel.IsVisible = true;
                await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Render).GetTask();
                await AnimatePanelInAsync(settingsPanel, buttonContainer, versionPanel, showMargin, buttonLeftMargin);

                osuautodeafenLogoPanel.Margin = new Thickness(0, 0, 225, 0);

                debugConsoleTextBlock.Margin = new Thickness(60, 32, 10, 250);
            }
            else
            {
                await Task.WhenAll(
                    StopCogSpinAsync(cogImage),
                    AnimatePanelOutAsync(settingsPanel, buttonContainer, versionPanel, hideMargin, buttonRightMargin)
                );
                settingsPanel.IsVisible = false;

                osuautodeafenLogoPanel.Margin = new Thickness(0, 0, 0, 0);

                debugConsoleTextBlock.Margin = new Thickness(60, 32, 10, 250);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERROR] Exception in SettingsButton_Click: {ex.Message}");
        }
    }

    /// <summary>
    ///     Sets up the initial state and transitions for the settings panel and related UI elements
    /// </summary>
    /// <param name="settingsPanel"></param>
    /// <param name="buttonContainer"></param>
    /// <param name="versionPanel"></param>
    private async Task SetupPanelTransitionsAsync(DockPanel settingsPanel, Border buttonContainer,
        TextBlock versionPanel)
    {
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            settingsPanel.Margin = new Thickness(200, 42, -200, 0);
            buttonContainer.Margin = new Thickness(0, 42, 0, 10);
            settingsPanel.Transitions = new Transitions
            {
                new ThicknessTransition
                {
                    Property = MarginProperty,
                    Duration = TimeSpan.FromMilliseconds(250),
                    Easing = new QuarticEaseInOut()
                }
            };
            buttonContainer.Transitions = new Transitions
            {
                new ThicknessTransition
                {
                    Property = MarginProperty,
                    Duration = TimeSpan.FromMilliseconds(250),
                    Easing = new QuarticEaseInOut()
                }
            };
            osuautodeafenLogoPanel.Transitions = new Transitions
            {
                new ThicknessTransition
                {
                    Property = MarginProperty,
                    Duration = TimeSpan.FromMilliseconds(500),
                    Easing = new BackEaseOut()
                }
            };
            versionPanel.Transitions = new Transitions
            {
                new ThicknessTransition
                {
                    Property = MarginProperty,
                    Duration = TimeSpan.FromMilliseconds(600),
                    Easing = new BackEaseOut()
                }
            };
        });
    }

    /// <summary>
    ///     Animates the settings panel into view
    /// </summary>
    /// <param name="settingsPanel"></param>
    /// <param name="buttonContainer"></param>
    /// <param name="versionPanel"></param>
    /// <param name="showMargin"></param>
    /// <param name="buttonLeftMargin"></param>
    private async Task AnimatePanelInAsync(DockPanel settingsPanel, Border buttonContainer, TextBlock versionPanel,
        Thickness showMargin, Thickness buttonLeftMargin)
    {
        await _panelAnimationLock.WaitAsync();
        bool shouldAnimate = !_isSettingsPanelOpen;
        if (shouldAnimate)
            _isSettingsPanelOpen = true;
        _panelAnimationLock.Release();

        if (!shouldAnimate) return;

        var tasks = new List<Task>();

        if (!_tosuApi._isKiai || !_viewModel.IsBackgroundEnabled)
            tasks.Add(_backgroundManager?.RequestBackgroundOpacity("settings", 0.5, 10, 150) ?? Task.CompletedTask);

        tasks.Add(Dispatcher.UIThread.InvokeAsync(() =>
        {
            settingsPanel.Margin = showMargin;
            buttonContainer.Margin = buttonLeftMargin;

            osuautodeafenLogoPanel.Transitions = new Transitions
            {
                new ThicknessTransition
                {
                    Property = MarginProperty,
                    Duration = TimeSpan.FromMilliseconds(500),
                    Easing = new BackEaseOut()
                }
            };
            osuautodeafenLogoPanel.Margin = new Thickness(0, 0, 225, 0);

            versionPanel.Margin = new Thickness(0, 0, 225, 0);
        }).GetTask());

        await Task.WhenAll(tasks);
    }

    /// <summary>
    ///     Animates the settings panel out of view
    /// </summary>
    /// <param name="settingsPanel"></param>
    /// <param name="buttonContainer"></param>
    /// <param name="versionPanel"></param>
    /// <param name="hideMargin"></param>
    /// <param name="buttonRightMargin"></param>
    private async Task AnimatePanelOutAsync(DockPanel settingsPanel, Border buttonContainer, TextBlock versionPanel,
        Thickness hideMargin, Thickness buttonRightMargin)
    {
        await _panelAnimationLock.WaitAsync();
        bool shouldAnimate = _isSettingsPanelOpen;
        if (shouldAnimate)
            _isSettingsPanelOpen = false;
        _panelAnimationLock.Release();

        if (!shouldAnimate) return;

        if (!_tosuApi._isKiai || !_viewModel.IsBackgroundEnabled)
            _backgroundManager?.RemoveBackgroundOpacityRequest("settings");

        await Task.WhenAll(
            Dispatcher.UIThread.InvokeAsync(() =>
            {
                settingsPanel.Margin = hideMargin;
                buttonContainer.Margin = buttonRightMargin;

                osuautodeafenLogoPanel.Transitions = new Transitions
                {
                    new ThicknessTransition
                    {
                        Property = MarginProperty,
                        Duration = TimeSpan.FromMilliseconds(500),
                        Easing = new BackEaseOut()
                    }
                };
                osuautodeafenLogoPanel.Margin = new Thickness(0, 0, 0, 0);

                versionPanel.Margin = new Thickness(0, 0, 0, 0);
            }).GetTask());
    }

    /// <summary>
    ///     Sets up the initial state and transitions for the debug console panel
    /// </summary>
    /// <param name="debugConsolePanel"></param>
    private static async Task SetupDebugConsoleTransitionsAsync(StackPanel debugConsolePanel)
    {
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            debugConsolePanel.Margin = new Thickness(-727, 0, 0, 0);
            debugConsolePanel.Transitions = new Transitions
            {
                new ThicknessTransition
                {
                    Property = MarginProperty,
                    Duration = TimeSpan.FromMilliseconds(400),
                    Easing = new QuarticEaseInOut()
                }
            };
        });
    }

    private static async Task AnimateDebugConsoleInAsync(StackPanel debugConsolePanel)
    {
        await Dispatcher.UIThread.InvokeAsync(() => { debugConsolePanel.Margin = new Thickness(0, 0, 0, 0); });
    }

    private static async Task AnimateDebugConsoleOutAsync(StackPanel debugConsolePanel)
    {
        await Dispatcher.UIThread.InvokeAsync(() => { debugConsolePanel.Margin = new Thickness(-727, 0, 0, 0); });
        await Task.Delay(400);
        await Dispatcher.UIThread.InvokeAsync(() => { debugConsolePanel.IsVisible = false; });
    }

    /// <summary>
    ///     Ensures the cog image is centered and has a RotateTransform applied (to make sure it rotates at it's center)
    /// </summary>
    /// <param name="cogImage"></param>
    /// <returns></returns>
    private Task EnsureCogCenterAsync(Avalonia.Svg.Svg cogImage)
    {
        return Dispatcher.UIThread.InvokeAsync(() =>
        {
            cogImage.RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative);
            if (cogImage.RenderTransform is not RotateTransform)
                cogImage.RenderTransform = new RotateTransform(0);
        }).GetTask();
    }

    /// <summary>
    ///     Calculates the interval for cog spinning based on the current BPM
    /// </summary>
    /// <param name="bpm"></param>
    /// <param name="updatesPerBeat"></param>
    /// <param name="minMs"></param>
    /// <param name="maxMs"></param>
    /// <returns></returns>
    private double CalculateCogSpinInterval(double bpm, double updatesPerBeat = 60, double minMs = 4, double maxMs = 50)
    {
        if (bpm <= 0) bpm = 140;
        double msPerBeat = 60000.0 / bpm;
        double intervalMs = msPerBeat / updatesPerBeat;
        return Math.Clamp(intervalMs, minMs, maxMs);
    }

    /// <summary>
    ///     Starts the cog spinning animation based on the current BPM
    /// </summary>
    /// <param name="cogImage"></param>
    private void StartCogSpin(Avalonia.Svg.Svg cogImage)
    {
        lock (_cogSpinLock)
        {
            if (_isCogSpinning)
                return;
            _isCogSpinning = true;

            if (_cogSpinTimer != null)
            {
                _cogSpinTimer.Stop();
                _cogSpinTimer = null;
            }

            RotateTransform rotate = (RotateTransform)cogImage.RenderTransform!;
            _cogSpinStartTime = DateTime.UtcNow;
            _cogSpinStartAngle = _cogCurrentAngle;
            _cogSpinBpm = _tosuApi.GetCurrentBpm() > 0 ? _tosuApi.GetCurrentBpm() : 140;

            double intervalMs = CalculateCogSpinInterval(_cogSpinBpm);

            _cogSpinTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(intervalMs) };
            _cogSpinTimer.Tick += (s, ev) =>
            {
                double elapsed = (DateTime.UtcNow - _cogSpinStartTime).TotalMinutes;
                double angle = (_cogSpinStartAngle + (elapsed * _cogSpinBpm * 360 / BeatsPerRotation)) % 360;
                _cogCurrentAngle = angle;
                rotate.Angle = angle;
            };
            _cogSpinTimer.Start();
        }
    }

    /// <summary>
    ///     Stops the cog spinning animation and returns it to the original position
    /// </summary>
    /// <param name="cogImage"></param>
    private async Task StopCogSpinAsync(Avalonia.Svg.Svg cogImage)
    {
        lock (_cogSpinLock)
        {
            if (!_isCogSpinning)
                return;
            _isCogSpinning = false;
            _cogSpinTimer?.Stop();
            _cogSpinTimer = null;
        }

        if (cogImage.RenderTransform is RotateTransform rotate)
        {
            double start = _cogCurrentAngle;
            double end = 0;
            int duration = 250;
            int steps = 20;
            double step = (end - start) / steps;
            await Task.Run(async () =>
            {
                for (int i = 1; i <= steps; i++)
                {
                    await Task.Delay(duration / steps);
                    double angle = start + (step * i);
                    await Dispatcher.UIThread.InvokeAsync(() => rotate.Angle = angle).GetTask();
                }

                await Dispatcher.UIThread.InvokeAsync(() => rotate.Angle = 0).GetTask();
                _cogCurrentAngle = 0;
            });
        }
    }

    /// <summary>
    ///     Update the cog spin BPM with the current beatmap BPM
    /// </summary>
    private void UpdateCogSpinBpm()
    {
        if (_cogSpinTimer != null && _cogSpinTimer.IsEnabled)
        {
            double elapsed = (DateTime.UtcNow - _cogSpinStartTime).TotalMinutes;
            _cogSpinStartAngle = (_cogSpinStartAngle + (elapsed * _cogSpinBpm * 360 / BeatsPerRotation)) % 360;
            _cogSpinStartTime = DateTime.UtcNow;
            _cogSpinBpm = _tosuApi.GetCurrentBpm() > 0 ? _tosuApi.GetCurrentBpm() : 140;

            double intervalMs = CalculateCogSpinInterval(_cogSpinBpm);
            _cogSpinTimer.Interval = TimeSpan.FromMilliseconds(intervalMs);
        }
    }

    private void InitializeKeybindButtonText()
    {
        string currentKeybind = RetrieveKeybindFromSettings();
        Button? deafenKeybindButton = this.FindControl<Button>("DeafenKeybindButton");
        if (deafenKeybindButton != null) deafenKeybindButton.Content = currentKeybind;
    }

    /// <summary>
    ///     Opens the file location of the osuautodeafen appdata folder
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="e"></param>
    private void OpenFileLocationButton_Click(object? sender, RoutedEventArgs e)
    {
        string? appPath = _settingsHandler?.GetPath();
        if (appPath != null)
        {
            if (Directory.Exists(appPath))
                Process.Start(new ProcessStartInfo
                {
                    FileName = appPath,
                    UseShellExecute = true
                });
            else
                Console.WriteLine($"[ERROR] Directory does not exist: {appPath}");
        }
        else
        {
            Console.WriteLine("[ERROR] App path is null.");
        }
    }

    /// <summary>
    ///     Opens the GitHub issues page in the default web browser with a default issue template
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="e"></param>
    private void ReportIssueButton_Click(object? sender, RoutedEventArgs e)
    {
        string issueUrl =
            "https://github.com/aerodite/osuautodeafen/issues/new?template=help.md&title=[BUG]%20Something%20Broke&body=help&labels=bug";
        Process.Start(new ProcessStartInfo
        {
            FileName = issueUrl,
            UseShellExecute = true
        });
    }

    /// <summary>
    ///     Shows or hides the debug console panel
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="e"></param>
    private async void ToggleDebugConsole(object? sender, RoutedEventArgs e)
    {
        try
        {
            StackPanel? debugConsolePanel = this.FindControl<StackPanel>("DebugConsolePanel");
            if (debugConsolePanel != null && !debugConsolePanel.IsVisible)
            {
                StartStableFrameTimer();

                await SetupDebugConsoleTransitionsAsync(debugConsolePanel);
                debugConsolePanel.IsVisible = true;
                await AnimateDebugConsoleInAsync(debugConsolePanel);

                _logUpdateTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
                _logUpdateTimer.Tick += (_, __) =>
                {
                    var currentLogs = _logImportant._importantLogs.Values.ToList();
                    UpdateDebugConsolePanel(debugConsolePanel, currentLogs);
                };
                _logUpdateTimer.Start();
            }
            else if (debugConsolePanel != null && debugConsolePanel.IsVisible)
            {
                StopStableFrameTimer();

                await AnimateDebugConsoleOutAsync(debugConsolePanel);
                debugConsolePanel.IsVisible = false;
                _logUpdateTimer?.Stop();
                _logUpdateTimer = null;
            }
            else
            {
                Console.WriteLine("[ERROR] Debug console not found.");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERROR] Exception in ToggleDebugConsole: {ex.Message}");
        }
    }

    /// <summary>
    ///     Updates the debug console panel with the current important logs
    /// </summary>
    /// <param name="debugConsolePanel"></param>
    /// <param name="currentLogs"></param>
    private void UpdateDebugConsolePanel(StackPanel debugConsolePanel, List<string> currentLogs)
    {
        if (debugConsolePanel.Children.Count != currentLogs.Count)
        {
            debugConsolePanel.Children.Clear();
            foreach (string logText in currentLogs)
                debugConsolePanel.Children.Add(CreateLogElement(logText));
        }
        else
        {
            for (int i = 0; i < currentLogs.Count; i++)
                if (_lastDisplayedLogs.Count <= i || _lastDisplayedLogs[i] != currentLogs[i])
                    debugConsolePanel.Children[i] = CreateLogElement(currentLogs[i]);
        }

        _lastDisplayedLogs = currentLogs;
    }

    /// <summary>
    ///    Creates a log textblock for the debug panel
    /// </summary>
    /// <param name="logText"></param>
    /// <remarks>
    /// This contains hyperlink support if the text contains a URL
    /// </remarks>
    /// <returns></returns>
    private Control CreateLogElement(string logText)
    {
        string? hyperlink = ExtractHyperlink(logText);

        if (!string.IsNullOrEmpty(hyperlink))
        {
            // remove the hyperlink from the displayed text
            string displayText = logText.Replace(hyperlink, "").TrimEnd();

            Button linkButton = new()
            {
                Content = new TextBlock
                {
                    Text = displayText,
                    Foreground = Brushes.LightBlue,
                    TextDecorations = TextDecorations.Underline,
                    Cursor = new Cursor(StandardCursorType.Hand)
                },
                Background = Brushes.Transparent,
                BorderBrush = Brushes.Transparent,
                Padding = new Thickness(0),
                Margin = new Thickness(0, 0, 4, 0)
            };
            linkButton.Click += (_, __) =>
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = hyperlink,
                    UseShellExecute = true
                });
            };
            return linkButton;
        }

        return new TextBlock { Text = logText, Foreground = Brushes.White };
    }
    
    public static string? ExtractHyperlink(string logText)
    {
        Regex urlRegex = new(@"https?://\S+");
        Match match = urlRegex.Match(logText);
        return match.Success ? match.Value : null;
    }
    
    public class HotKey
    {
        public Key Key { get; set; }
        public KeyModifiers ModifierKeys { get; set; }
        public Modifiers.ModifierSide ControlSide { get; set; }
        public Modifiers.ModifierSide AltSide { get; set; }
        public Modifiers.ModifierSide ShiftSide { get; set; }
        public string FriendlyName { get; set; } = "";

        public override string? ToString()
        {
            List<string> parts = new();

            if (ModifierKeys.HasFlag(KeyModifiers.Control))
                parts.Add("Ctrl");
            if (ModifierKeys.HasFlag(KeyModifiers.Alt))
                parts.Add("Alt");
            if (ModifierKeys.HasFlag(KeyModifiers.Shift))
                parts.Add("Shift");

            parts.Add(FriendlyName);

            return string.Join("+", parts).Replace("==", "=");
        }
    }
}