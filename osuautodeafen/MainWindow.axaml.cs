using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
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
using Avalonia.Threading;
using LiveChartsCore.Defaults;
using osuautodeafen.cs;
using osuautodeafen.cs.Background;
using osuautodeafen.cs.Deafen;
using osuautodeafen.cs.Logo;
using osuautodeafen.cs.Settings;
using osuautodeafen.cs.StrainGraph;
using SkiaSharp;
using Svg.Skia;
using Vector = Avalonia.Vector;

namespace osuautodeafen;

public partial class MainWindow : Window
{
    private readonly AnimationManager _animationManager = new();
    private readonly BreakPeriodCalculator _breakPeriod;

    private readonly ChartManager _chartManager;
    private readonly GetLowResBackground? _getLowResBackground;
    private readonly DispatcherTimer _mainTimer;
    private readonly ProgressIndicatorHelper _progressIndicatorHelper;
    private readonly SettingsHandler? _settingsHandler;

    private readonly UpdateChecker _updateChecker = UpdateChecker.GetInstance();

    private readonly SharedViewModel _viewModel;
    
    private Bitmap? _currentBitmap;
    
    public Image? _blurredBackground;
    
    private double _cogCurrentAngle;

    private DispatcherTimer? _cogSpinTimer;

    private Key _lastKeyPressed = Key.None;
    private DateTime _lastKeyPressTime = DateTime.MinValue;
    
    private LogoControl? _logoControl;

    public Image? _normalBackground;
    
    public readonly TosuApi _tosuApi = new();

    public bool _isSettingsPanelOpen = false;
    
    private readonly BackgroundManager? _backgroundManager;
    
    private DispatcherTimer? _kiaiBrightnessTimer;
    private bool _isKiaiPulseHigh = false;

    private Action settingsButtonClicked;
    
    private double opacity = 1.00;


    //<summary>
    // constructor for the ui and subsequent panels
    //</summary>
    public MainWindow()
    {
        InitializeComponent();

        // ViewModel and DataContext
        _viewModel = new SharedViewModel(_tosuApi);
        ViewModel = _viewModel;
        DataContext = _viewModel;
        
        InitializeLogo();
        
        Icon = new WindowIcon(LoadEmbeddedResource("osuautodeafen.Resources.favicon.ico"));
        
        _getLowResBackground = new GetLowResBackground(_tosuApi);
        
        _backgroundManager = new BackgroundManager( this, _viewModel, _tosuApi)
        {
            _logoUpdater = null
        };
        
        // settings bs
        
        var settingsPanel = new SettingsHandler();
        _settingsHandler = settingsPanel;
        _settingsHandler.LoadSettings();
        
        _viewModel.MinCompletionPercentage = (int)Math.Round(_settingsHandler.MinCompletionPercentage);
        _viewModel.StarRating = _settingsHandler.StarRating;
        _viewModel.PerformancePoints = (int)Math.Round(_settingsHandler.PerformancePoints);
        
        _viewModel.IsFCRequired = _settingsHandler.IsFCRequired;
        _viewModel.UndeafenAfterMiss = _settingsHandler.UndeafenAfterMiss;
        _viewModel.IsBreakUndeafenToggleEnabled = _settingsHandler.IsBreakUndeafenToggleEnabled;
        
        _viewModel.IsBackgroundEnabled = _settingsHandler.IsBackgroundEnabled;
        _viewModel.IsParallaxEnabled = _settingsHandler.IsParallaxEnabled;
        _viewModel.IsBlurEffectEnabled = _settingsHandler.IsBlurEffectEnabled;
        
        CompletionPercentageSlider.ValueChanged -= CompletionPercentageSlider_ValueChanged;
        StarRatingSlider.ValueChanged -= StarRatingSlider_ValueChanged;
        PPSlider.ValueChanged -= PPSlider_ValueChanged;

        CompletionPercentageSlider.Value = _viewModel.MinCompletionPercentage;
        StarRatingSlider.Value = _viewModel.StarRating;
        PPSlider.Value = _viewModel.PerformancePoints;

        CompletionPercentageSlider.ValueChanged += CompletionPercentageSlider_ValueChanged;
        StarRatingSlider.ValueChanged += StarRatingSlider_ValueChanged;
        PPSlider.ValueChanged += PPSlider_ValueChanged;

        FCToggle.IsChecked = _viewModel.IsFCRequired;
        UndeafenOnMissToggle.IsChecked = _viewModel.UndeafenAfterMiss;
        BreakUndeafenToggle.IsChecked = _viewModel.IsBreakUndeafenToggleEnabled;

        BackgroundToggle.IsChecked = _viewModel.IsBackgroundEnabled;
        ParallaxToggle.IsChecked = _viewModel.IsParallaxEnabled;
        BlurEffectToggle.IsChecked = _viewModel.IsBlurEffectEnabled;
        
        // end of settings bs

        BackgroundManager.PrewarmRenderTarget();

        var oldContent = Content;
        Content = null;
        Content = new Grid
        {
            Children = { new ContentControl { Content = oldContent } }
        };

        InitializeViewModel();
        
        _breakPeriod = new BreakPeriodCalculator();
        _chartManager = new ChartManager(PlotView, _tosuApi, _viewModel, _breakPeriod);
        _progressIndicatorHelper = new ProgressIndicatorHelper(_chartManager, _tosuApi, _viewModel);
        
        // we just need to initialize it, no need for a global variable
        var deafen = new Deafen(_tosuApi, _settingsHandler, _viewModel);
        
        // ideally we could use no timers whatsoever but for now this works fine
        // because it really only checks if events should be triggered
        _mainTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        _mainTimer.Tick += MainTimer_Tick;
        _mainTimer.Start();
        _viewModel.PropertyChanged += ViewModel_PropertyChanged;
        
        ProgressOverlay.ChartXMin = _progressIndicatorHelper.ChartXMin;
        ProgressOverlay.ChartXMax = _progressIndicatorHelper.ChartXMax;
        ProgressOverlay.ChartYMin = _progressIndicatorHelper.ChartYMin;
        ProgressOverlay.ChartYMax = _progressIndicatorHelper.ChartYMax;

        ProgressOverlay.Points =
            _progressIndicatorHelper.CalculateSmoothProgressContour(_tosuApi.GetCompletionPercentage());
        _tosuApi.BeatmapChanged += async () =>
        {
            await Dispatcher.UIThread.InvokeAsync(() => OnGraphDataUpdated(_tosuApi.GetGraphData()));
            if (_backgroundManager != null)
                await _backgroundManager.UpdateBackground(null, null);
        };
        _tosuApi.HasModsChanged += async () =>
        {
            await _chartManager.UpdateChart(_tosuApi.GetGraphData(), ViewModel.MinCompletionPercentage, _breakPeriod);
        };
        _tosuApi.HasBPMChanged += async () =>
        {
            await Dispatcher.UIThread.InvokeAsync(UpdateCogSpinBpm);

            // Update kiai pulse interval if kiai is active
            if (_tosuApi._isKiai && _kiaiBrightnessTimer != null)
            {
                double bpm = _tosuApi.GetCurrentBpm();
                double intervalMs = 60000.0 / bpm;
                _kiaiBrightnessTimer.Interval = TimeSpan.FromMilliseconds(intervalMs);
            }
        };
        _breakPeriod.BreakPeriodEntered += async () =>
        {
          Console.WriteLine("Break period entered");
        };
        _breakPeriod.BreakPeriodExited += async () =>
        {
            Console.WriteLine("Break period exited");
        };
        _tosuApi.HasKiaiChanged += async () =>
        {
            // without this the background still shows in kiai time
            // not good.
            if (!_viewModel.IsBackgroundEnabled)
            {
                return;
            }
            if (_tosuApi._isKiai)
            {
                double bpm = _tosuApi.GetCurrentBpm();
                double intervalMs = (60000.0 / bpm);
                
                //if settings panel is open, dim
                opacity = _isSettingsPanelOpen ? 0.50 : 0;
                

                _kiaiBrightnessTimer?.Stop();
                _kiaiBrightnessTimer = new DispatcherTimer
                {
                    Interval = TimeSpan.FromMilliseconds(intervalMs)
                };
                _kiaiBrightnessTimer.Tick += async (_, _) =>
                {
                    _isKiaiPulseHigh = !_isKiaiPulseHigh;
                    if (_isKiaiPulseHigh)
                    {
                        await _backgroundManager.RequestBackgroundOpacity("kiai", 1.0 - opacity, 10000, (int)(intervalMs / 4));
                    }
                    else
                    {
                        await _backgroundManager.RequestBackgroundOpacity("kiai", 0.85 - opacity, 10000, (int)(intervalMs / 4));
                    }
                };
                _kiaiBrightnessTimer.Start();
            }
            else
            {
                _kiaiBrightnessTimer?.Stop();
                _kiaiBrightnessTimer = null;

                if (_isSettingsPanelOpen)
                {
                    await _backgroundManager.RequestBackgroundOpacity("settings", 0.5, 10, 150);
                }

                _backgroundManager.RemoveBackgroundOpacityRequest("kiai");
            }
        };
        settingsButtonClicked = () =>
        {
            if (_isSettingsPanelOpen)
            {
                opacity = 0;
            }
            else
            {
                opacity = 0.5;
            }
        };

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
        Width = _settingsHandler.WindowWidth;
        Height = _settingsHandler.WindowHeight;
        Title = "osu!autodeafen";
        MaxHeight = 800;
        MaxWidth = 800;
        MinHeight = 400;
        MinWidth = 550;
        CanResize = true;
        Closing += MainWindow_Closing;

        PointerPressed += (sender, e) =>
        {
            var point = e.GetPosition(this);
            const int titleBarHeight = 34;
            if (point.Y <= titleBarHeight) BeginMoveDrag(e);
        };

        PointerMoved += _backgroundManager.OnMouseMove;

        // Settings visuals and stuff
        InitializeKeybindButtonText();
        UpdateDeafenKeybindDisplay();
        CompletionPercentageSlider.Value = ViewModel.MinCompletionPercentage;
        StarRatingSlider.Value = ViewModel.StarRating;
        PPSlider.Value = ViewModel.PerformancePoints;
    }
    private async void ResetButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            _settingsHandler?.ResetToDefaults();

            if (_settingsHandler == null) return;

            // Reload settings into ViewModel
            _viewModel.MinCompletionPercentage = (int)Math.Round(_settingsHandler.MinCompletionPercentage);
            _viewModel.StarRating = _settingsHandler.StarRating;
            _viewModel.PerformancePoints = (int)Math.Round(_settingsHandler.PerformancePoints);

            _viewModel.IsFCRequired = _settingsHandler.IsFCRequired;
            _viewModel.UndeafenAfterMiss = _settingsHandler.UndeafenAfterMiss;
            _viewModel.IsBreakUndeafenToggleEnabled = _settingsHandler.IsBreakUndeafenToggleEnabled;

            _viewModel.IsBackgroundEnabled = _settingsHandler.IsBackgroundEnabled;
            _viewModel.IsParallaxEnabled = _settingsHandler.IsParallaxEnabled;
            _viewModel.IsBlurEffectEnabled = _settingsHandler.IsBlurEffectEnabled;

            // Update UI controls
            CompletionPercentageSlider.ValueChanged -= CompletionPercentageSlider_ValueChanged;
            StarRatingSlider.ValueChanged -= StarRatingSlider_ValueChanged;
            PPSlider.ValueChanged -= PPSlider_ValueChanged;

            CompletionPercentageSlider.Value = _viewModel.MinCompletionPercentage;
            StarRatingSlider.Value = _viewModel.StarRating;
            PPSlider.Value = _viewModel.PerformancePoints;

            CompletionPercentageSlider.ValueChanged += CompletionPercentageSlider_ValueChanged;
            StarRatingSlider.ValueChanged += StarRatingSlider_ValueChanged;
            PPSlider.ValueChanged += PPSlider_ValueChanged;

            FCToggle.IsChecked = _viewModel.IsFCRequired;
            UndeafenOnMissToggle.IsChecked = _viewModel.UndeafenAfterMiss;
            BreakUndeafenToggle.IsChecked = _viewModel.IsBreakUndeafenToggleEnabled;

            BackgroundToggle.IsChecked = _viewModel.IsBackgroundEnabled;
            ParallaxToggle.IsChecked = _viewModel.IsParallaxEnabled;
            BlurEffectToggle.IsChecked = _viewModel.IsBlurEffectEnabled;
            
            var keyStr = _settingsHandler?.Data["Hotkeys"]["DeafenKeybindKey"];
            var modStr = _settingsHandler?.Data["Hotkeys"]["DeafenKeybindModifiers"];
            if (int.TryParse(keyStr, out var keyVal) && int.TryParse(modStr, out var modVal))
            {
                var key = (Key)keyVal;
                var modifiers = (KeyModifiers)modVal;
                _viewModel.DeafenKeybind = new HotKey
                {
                    Key = key,
                    ModifierKeys = modifiers,
                    FriendlyName = GetFriendlyKeyName(key)
                };
            }
            else
            {
                _viewModel.DeafenKeybind = new HotKey { Key = Key.None, ModifierKeys = KeyModifiers.None, FriendlyName = "None" };
            }
        
            await _chartManager.UpdateChart(_tosuApi.GetGraphData(), _viewModel.MinCompletionPercentage, _breakPeriod);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERROR] Exception in ResetButton_Click: {ex}");
        }
    }
    
    private HotKey ParseHotKey(string? keybindString)
    {
        if (string.IsNullOrEmpty(keybindString))
            return new HotKey { Key = Key.None, ModifierKeys = KeyModifiers.None, FriendlyName = "None" };

        var parts = keybindString.Split('+');
        KeyModifiers modifiers = KeyModifiers.None;
        Key key = Key.None;

        foreach (var part in parts)
        {
            var trimmed = part.Trim();
            if (trimmed.Equals("Control", StringComparison.OrdinalIgnoreCase) || trimmed.Equals("Ctrl", StringComparison.OrdinalIgnoreCase))
                modifiers |= KeyModifiers.Control;
            else if (trimmed.Equals("Alt", StringComparison.OrdinalIgnoreCase))
                modifiers |= KeyModifiers.Alt;
            else if (trimmed.Equals("Shift", StringComparison.OrdinalIgnoreCase))
                modifiers |= KeyModifiers.Shift;
            else if (Enum.TryParse<Key>(trimmed, out var parsedKey))
                key = parsedKey;
        }

        return new HotKey
        {
            Key = key,
            ModifierKeys = modifiers,
            FriendlyName = key.ToString()
        };
    }
    private void CompletionPercentageSlider_PointerPressed(object sender, PointerPressedEventArgs e)
    {
        ToolTip.SetIsOpen(CompletionPercentageSlider, true);
    }
    private void CompletionPercentageSlider_PointerMoved(object? sender, PointerEventArgs e)
    {
        if (sender is Slider slider)
        {
            if (e.GetCurrentPoint(slider).Properties.IsLeftButtonPressed)
            {
                ToolTip.SetTip(slider, $"{slider.Value:0}%");
                ToolTip.SetPlacement(slider, PlacementMode.Pointer);
                ToolTip.SetVerticalOffset(slider, -30);
                ToolTip.SetIsOpen(slider, true);
            }
            else
            {
                ToolTip.SetIsOpen(slider, false);
            }
        }
    }
    private void CompletionPercentageSlider_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (sender is Slider slider)
            ToolTip.SetIsOpen(slider, false);
    }
    
    private void PPSlider_PointerPressed(object sender, PointerPressedEventArgs e)
    {
        ToolTip.SetIsOpen(CompletionPercentageSlider, true);
    }
    private void PPSlider_PointerMoved(object? sender, PointerEventArgs e)
    {
        if (sender is Slider slider)
        {
            if (e.GetCurrentPoint(slider).Properties.IsLeftButtonPressed)
            {
                ToolTip.SetTip(slider, $"{slider.Value:0}pp");
                ToolTip.SetPlacement(slider, PlacementMode.Pointer);
                ToolTip.SetVerticalOffset(slider, -30);
                ToolTip.SetIsOpen(slider, true);
            }
            else
            {
                ToolTip.SetIsOpen(slider, false);
            }
        }
    }
    private void PPSlider_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (sender is Slider slider)
            ToolTip.SetIsOpen(slider, false);
    }
    private void StarRatingSlider_PointerPressed(object sender, PointerPressedEventArgs e)
    {
        ToolTip.SetIsOpen(CompletionPercentageSlider, true);
    }
    private void StarRatingSlider_PointerMoved(object? sender, PointerEventArgs e)
    {
        if (sender is Slider slider)
        {
            if (e.GetCurrentPoint(slider).Properties.IsLeftButtonPressed)
            {
                ToolTip.SetTip(slider, $"{slider.Value:F1}*");
                ToolTip.SetPlacement(slider, PlacementMode.Pointer);
                ToolTip.SetVerticalOffset(slider, -30);
                ToolTip.SetIsOpen(slider, true);
            }
            else
            {
                ToolTip.SetIsOpen(slider, false);
            }
        }
    }
    private void StarRatingSlider_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (sender is Slider slider)
            ToolTip.SetIsOpen(slider, false);
    }

    private SharedViewModel ViewModel { get; }
    private bool IsBlackBackgroundDisplayed { get; set; }

    //Settings
    private async void CompletionPercentageSlider_ValueChanged(object? sender, RangeBaseValueChangedEventArgs e)
    {
        if (sender is not Slider slider || DataContext is not SharedViewModel vm) return;
        var roundedValue = (int)Math.Round(slider.Value);
        Console.WriteLine($"Min Comp. % Value: {roundedValue}");
        vm.MinCompletionPercentage = roundedValue;
        _settingsHandler?.SaveSetting("General", "MinCompletionPercentage", roundedValue);
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

    private void StarRatingSlider_ValueChanged(object? sender,
        RangeBaseValueChangedEventArgs rangeBaseValueChangedEventArgs)
    {
        if (sender is not Slider slider || DataContext is not SharedViewModel vm) return;
        var roundedValue = Math.Round(slider.Value, 1);
        Console.WriteLine($"Min SR Value: {roundedValue:F1}");
        vm.StarRating = roundedValue;
        _settingsHandler?.SaveSetting("General", "StarRating", roundedValue);
    }

    private void PPSlider_ValueChanged(object? sender, RangeBaseValueChangedEventArgs e)
    {
        if (sender is not Slider slider || DataContext is not SharedViewModel vm) return;
        var roundedValue = (int)Math.Round(slider.Value);
        Console.WriteLine($"Min PP Value: {roundedValue}");
        vm.PerformancePoints = roundedValue;
        _settingsHandler?.SaveSetting("General", "PerformancePoints", roundedValue);
    }

    private async void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e) 
    {
    if (e.PropertyName == nameof(SharedViewModel.CompletionPercentage))
    {
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (_progressIndicatorHelper == null || _tosuApi == null || ProgressOverlay == null)
                return;

            ProgressOverlay.ChartXMin = _progressIndicatorHelper.ChartXMin;
            ProgressOverlay.ChartXMax = _progressIndicatorHelper.ChartXMax;
            ProgressOverlay.ChartYMin = _progressIndicatorHelper.ChartYMin;
            ProgressOverlay.ChartYMax = _progressIndicatorHelper.ChartYMax;

            var points = _progressIndicatorHelper.CalculateSmoothProgressContour(_tosuApi.GetCompletionPercentage(), force:true);
            ProgressOverlay.Points = points;
        });
    }
}
    
    private void OnGraphDataUpdated(GraphData? graphData)
    {
        if (graphData == null || graphData.Series.Count < 2)
            return;

        var series0 = graphData.Series[0];
        var series1 = graphData.Series[1];
        series0.Name = "aim";
        series1.Name = "speed";

        var data0 = series0.Data;
        var data1 = series1.Data;

        var list0 = new List<ObservablePoint>(data0.Count);
        var list1 = new List<ObservablePoint>(data1.Count);

        for (var i = 0; i < data0.Count; i++)
            list0.Add(new ObservablePoint(i, data0[i]));
        for (var i = 0; i < data1.Count; i++)
            list1.Add(new ObservablePoint(i, data1[i]));

        ChartData.Series1Values = list0;
        ChartData.Series2Values = list1;
        
        Dispatcher.UIThread.InvokeAsync(() => _chartManager.UpdateChart(graphData, ViewModel.MinCompletionPercentage, _breakPeriod));
    }

    // show the update notification bar if an update is available
    private async void InitializeViewModel()
    {
        await CheckForUpdates();
        DataContext = ViewModel;
    }

    // grab the keybind from the settings file and update the display
    private void UpdateDeafenKeybindDisplay()
    {
        var currentKeybind = RetrieveKeybindFromSettings();
        DeafenKeybindButton.Content = currentKeybind;
    }

    // save the keybind to the settings file
    private void DeafenKeybindButton_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.IsKeybindCaptureFlyoutOpen = !ViewModel.IsKeybindCaptureFlyoutOpen;
        var flyout = Resources["KeybindCaptureFlyout"] as Flyout;
        if (flyout != null)
        {
            if (ViewModel.IsKeybindCaptureFlyoutOpen)
                flyout.ShowAt(DeafenKeybindButton);
            else
                flyout.Hide();
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        if (!ViewModel.IsKeybindCaptureFlyoutOpen) return;
        if (e.Key == Key.NumLock) return;

        if (e.Key == Key.Escape)
        {
            ViewModel.IsKeybindCaptureFlyoutOpen = false;
            (Resources["KeybindCaptureFlyout"] as Flyout)?.Hide();
            return;
        }

        var currentTime = DateTime.Now;
        if (e.Key == _lastKeyPressed && (currentTime - _lastKeyPressTime).TotalMilliseconds < 2500) return;
        _lastKeyPressed = e.Key;
        _lastKeyPressTime = currentTime;

        if (IsModifierKey(e.Key)) return;

        var modifiers = KeyModifiers.None;
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control)) modifiers |= KeyModifiers.Control;
        if (e.KeyModifiers.HasFlag(KeyModifiers.Alt)) modifiers |= KeyModifiers.Alt;
        if (e.KeyModifiers.HasFlag(KeyModifiers.Shift)) modifiers |= KeyModifiers.Shift;

        var friendlyKeyName = GetFriendlyKeyName(e.Key);
        var hotKey = new HotKey { Key = e.Key, ModifierKeys = modifiers, FriendlyName = friendlyKeyName };
        ViewModel.DeafenKeybind = hotKey;

        // Save to settings
        _settingsHandler?.SaveSetting("Hotkeys", "DeafenKeybindKey", (int)e.Key);
        _settingsHandler?.SaveSetting("Hotkeys", "DeafenKeybindModifiers", (int)modifiers);

        ViewModel.IsKeybindCaptureFlyoutOpen = false;
        (Resources["KeybindCaptureFlyout"] as Flyout)?.Hide();

        // Update display
        UpdateDeafenKeybindDisplay();

        e.Handled = true;
    }

    private string RetrieveKeybindFromSettings()
    {
        var keyStr = _settingsHandler?.Data["Hotkeys"]["DeafenKeybindKey"];
        var modStr = _settingsHandler?.Data["Hotkeys"]["DeafenKeybindModifiers"];
        if (string.IsNullOrEmpty(keyStr) || string.IsNullOrEmpty(modStr))
            return "Set Keybind";

        if (!int.TryParse(keyStr, out var keyVal) || !int.TryParse(modStr, out var modVal))
            return "Set Keybind";

        var key = (Key)keyVal;
        var modifiers = (KeyModifiers)modVal;

        var display = "";
        if (modifiers.HasFlag(KeyModifiers.Control)) display += "Ctrl+";
        if (modifiers.HasFlag(KeyModifiers.Alt)) display += "Alt+";
        if (modifiers.HasFlag(KeyModifiers.Shift)) display += "Shift+";
        display += GetFriendlyKeyName(key);
        return display;
    }


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
            Key.Oem8 => "Oem8",
            _ => key.ToString()
        };
    }

    public void ShowUpdateNotification()
    {
        Console.WriteLine("Showing Update Notification");
        var notificationBar = this.FindControl<Button>("UpdateNotificationBar");
        if (notificationBar != null)
            notificationBar.IsVisible = true;
        else
            Console.WriteLine("Notification bar control not found.");
    }

    private void UpdateNotificationBar_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(ViewModel.UpdateUrl))
            Process.Start(new ProcessStartInfo
            {
                FileName = ViewModel.UpdateUrl,
                UseShellExecute = true
            });
    }

    private void MainTimer_Tick(object? sender, EventArgs? e)
    {
        _tosuApi.CheckForBeatmapChange();
        _tosuApi.CheckForModChange();
        _tosuApi.CheckForBPMChange();
        _tosuApi.CheckForKiaiChange();
        _breakPeriod.UpdateBreakPeriodState(_tosuApi);
    }

    public async void CheckForUpdatesButton_Click(object sender, RoutedEventArgs e)
    {
        var button = this.FindControl<Button>("CheckForUpdatesButton");
        if (button == null) return;

        button.Content = "Checking for updates...";
        await Task.Delay(1000);

        await _updateChecker.FetchLatestVersionAsync();

        if (string.IsNullOrEmpty(_updateChecker.latestVersion))
        {
            button.Content = "No updates found";
            await Task.Delay(1000);
            button.Content = "Check for updates";
            return;
        }

        var currentVersion = new Version(UpdateChecker.currentVersion);
        var latestVersion = new Version(_updateChecker.latestVersion);

        if (latestVersion > currentVersion)
        {
            Console.WriteLine($"Update available: {latestVersion}");
            ShowUpdateNotification();
            button.Content = "Update available!";
            await Task.Delay(2000);
            button.Content = "Check for updates";
        }
        else
        {
            Console.WriteLine("You are on the latest version.");
            button.Content = "You are on the latest version";
            await Task.Delay(2000);
            button.Content = "Check for updates";
        }
    }

    private async Task CheckForUpdates()
    {
        await _updateChecker.FetchLatestVersionAsync();

        if (string.IsNullOrEmpty(_updateChecker.latestVersion))
        {
            Console.WriteLine("No updates found");
            return;
        }

        var currentVersion = new Version(UpdateChecker.currentVersion);
        var latestVersion = new Version(_updateChecker.latestVersion);

        if (latestVersion > currentVersion)
        {
            Console.WriteLine($"Update available: {latestVersion}");
            ShowUpdateNotification();
        }
        else
        {
            Console.WriteLine("You are on the latest version.");
        }
    }

    private bool IsModifierKey(Key key)
    {
        return key == Key.LeftCtrl || key == Key.RightCtrl ||
               key == Key.LeftAlt || key == Key.RightAlt ||
               key == Key.LeftShift || key == Key.RightShift;
    }

    public Bitmap LoadEmbeddedResource(string resourceName)
    {
        const int maxRetries = 5;
        const int initialDelayMilliseconds = 500;
        var delay = initialDelayMilliseconds;

        for (var retryCount = 0; retryCount < maxRetries; retryCount++)
            try
            {
                using var resourceStream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName)
                                           ?? throw new FileNotFoundException("Resource not found: " + resourceName);

                return new Bitmap(resourceStream)
                       ?? throw new InvalidOperationException("Failed to create bitmap from resource stream.");
            }
            catch (Exception ex)
            {
                Console.WriteLine(
                    $"[ERROR] Exception while loading embedded resource '{resourceName}': {ex.Message}. Attempt {retryCount + 1} of {maxRetries}.");

                if (retryCount >= maxRetries - 1)
                {
                    Console.WriteLine("[ERROR] Max retry attempts reached. Failing operation.");
                    throw;
                }

                // Exponential backoff
                Task.Delay(delay).Wait();
                delay *= 2;
            }

        throw new InvalidOperationException("Failed to load embedded resource after multiple attempts.");
    }

    public SKSvg LoadHighResolutionLogo(string resourceName)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName)
                           ?? throw new FileNotFoundException("Resource not found: " + resourceName);
        var svg = new SKSvg();
        svg.Load(stream);
        return svg;
    }

    private async void InitializeLogo()
    {
        const string resourceName = "osuautodeafen.Resources.autodeafen.svg";
        try
        {
            var svg = await Task.Run(() => LoadHighResolutionLogo(resourceName));

            if (_logoControl == null)
                _logoControl = new LogoControl
                {
                    Width = 240,
                    Height = 72,
                    VerticalAlignment = VerticalAlignment.Center
                };
            _logoControl.Svg = svg;
            _logoControl.ModulateColor = SKColors.White;

            var logoHost = this.FindControl<ContentControl>("LogoHost");
            if (logoHost != null)
                logoHost.Content = _logoControl;

            _backgroundManager!._logoUpdater = new LogoUpdater(
                _getLowResBackground!,
                _logoControl,
                _animationManager,
                ViewModel,
                LoadHighResolutionLogo
            );

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

    private async Task<Bitmap> LoadLogoAsync(string resourceName)
    {
        using var resourceStream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName)
                                   ?? throw new FileNotFoundException("Resource not found: " + resourceName);

        var svg = new SKSvg();
        svg.Load(resourceStream);

        return svg.Picture == null
            ? throw new InvalidOperationException("Failed to load SVG picture.")
            : ConvertSvgToBitmap(svg, 100, 100);
    }

    private Bitmap ConvertSvgToBitmap(SKSvg svg, int width, int height)
    {
        if (svg == null)
            throw new ArgumentNullException(nameof(svg));
        if (svg.Picture == null)
            throw new InvalidOperationException("SVG does not contain a valid picture.");

        var info = new SKImageInfo(width, height);

        try
        {
            using var surface = SKSurface.Create(info);
            if (surface == null)
                throw new InvalidOperationException("Failed to create SKSurface.");

            var canvas = surface.Canvas;
            canvas.Clear(SKColors.Transparent);
            canvas.DrawPicture(svg.Picture);

            using var image = surface.Snapshot();
            using var data = image.Encode();
            using var stream = new MemoryStream(data.ToArray());
            return new Bitmap(stream);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERROR] ConvertSvgToBitmap failed: {ex.Message}");
            throw;
        }
    }

    private async Task RetryLoadLogoAsync(string resourceName)
    {
        const int maxRetries = 3;
        var retryCount = 0;
        var success = false;

        while (retryCount < maxRetries && !success)
            try
            {
                retryCount++;
                Console.WriteLine($"Retrying to load SVG... Attempt {retryCount}");
                var logoImage = await LoadLogoAsync(resourceName);
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
                    return; // Exit if loading the SVG fails after max retries
                }
            }
    }

    private void UpdateViewModelWithLogo(Bitmap logoImage)
    {
        var viewModel = DataContext as SharedViewModel;
        if (viewModel != null)
        {
            viewModel.ModifiedLogoImage = logoImage;
            Console.WriteLine("ModifiedLogoImage property set.");
        }
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

    private void MainWindow_Closing(object? sender, CancelEventArgs e)
    {
        if (_settingsHandler != null)
        {
            _settingsHandler.WindowWidth = Width;
            _settingsHandler.WindowHeight = Height;
        }

        _tosuApi.Dispose();
    }
    
    private async void SettingsButton_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            settingsButtonClicked?.Invoke();
            var settingsPanel = this.FindControl<DockPanel>("SettingsPanel");
            var buttonContainer = this.FindControl<Border>("SettingsButtonContainer");
            var cogImage = this.FindControl<Image>("SettingsCogImage");
            var textBlockPanel = this.FindControl<StackPanel>("TextBlockPanel");
            var versionPanel = textBlockPanel?.FindControl<TextBlock>("VersionPanel");
            if (settingsPanel == null || buttonContainer == null || cogImage == null ||
                textBlockPanel == null || osuautodeafenLogoPanel == null || versionPanel == null)
                return;

            var showMargin = new Thickness(0, 42, 0, 0);
            var hideMargin = new Thickness(200, 42, -200, 0);
            var buttonRightMargin = new Thickness(0, 42, 0, 10);
            var buttonLeftMargin = new Thickness(0, 42, 200, 10);

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
            }
            else
            {
                await Task.WhenAll(
                    StopCogSpinAsync(cogImage),
                    AnimatePanelOutAsync(settingsPanel, buttonContainer, versionPanel, hideMargin, buttonRightMargin)
                );
                settingsPanel.IsVisible = false;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERROR] Exception in SettingsButton_Click: {ex.Message}");
        }
    }

    private Task EnsureCogCenterAsync(Image cogImage)
    {
        return Dispatcher.UIThread.InvokeAsync(() =>
        {
            cogImage.RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative);
            if (cogImage.RenderTransform is not RotateTransform)
                cogImage.RenderTransform = new RotateTransform(0);
        }).GetTask();
    }

    private DateTime _cogSpinStartTime;
    private double _cogSpinStartAngle;
    private double _cogSpinBpm = 140; // Default BPM

    private const double beatsPerRotation = 4;

    private double CalculateCogSpinInterval(double bpm, double updatesPerBeat = 60, double minMs = 4, double maxMs = 50)
    {
        if (bpm <= 0) bpm = 140;
        double msPerBeat = 60000.0 / bpm;
        double intervalMs = msPerBeat / updatesPerBeat;
        Console.WriteLine($"Calculated interval: {intervalMs}ms for BPM: {bpm}");
        return Math.Clamp(intervalMs, minMs, maxMs);
    }

private bool _isCogSpinning = false;
private readonly object _cogSpinLock = new();

private void StartCogSpin(Image cogImage)
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

        var rotate = (RotateTransform)cogImage.RenderTransform!;
        _cogSpinStartTime = DateTime.UtcNow;
        _cogSpinStartAngle = _cogCurrentAngle;
        _cogSpinBpm = _tosuApi.GetCurrentBpm() > 0 ? _tosuApi.GetCurrentBpm() : 140;

        double intervalMs = CalculateCogSpinInterval(_cogSpinBpm);

        _cogSpinTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(intervalMs) };
        _cogSpinTimer.Tick += (s, ev) =>
        {
            var elapsed = (DateTime.UtcNow - _cogSpinStartTime).TotalMinutes;
            var angle = (_cogSpinStartAngle + (elapsed * _cogSpinBpm * 360 / beatsPerRotation)) % 360;
            _cogCurrentAngle = angle;
            rotate.Angle = angle;
        };
        _cogSpinTimer.Start();
    }
}

private async Task StopCogSpinAsync(Image cogImage)
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
        var start = _cogCurrentAngle;
        double end = 0;
        var duration = 250;
        var steps = 20;
        var step = (end - start) / steps;
        await Task.Run(async () =>
        {
            for (var i = 1; i <= steps; i++)
            {
                await Task.Delay(duration / steps);
                var angle = start + (step * i);
                await Dispatcher.UIThread.InvokeAsync(() => rotate.Angle = angle).GetTask();
            }
            await Dispatcher.UIThread.InvokeAsync(() => rotate.Angle = 0).GetTask();
            _cogCurrentAngle = 0;
        });
    }
}

    private void UpdateCogSpinBpm()
    {
        if (_cogSpinTimer != null && _cogSpinTimer.IsEnabled)
        {
            var elapsed = (DateTime.UtcNow - _cogSpinStartTime).TotalMinutes;
            _cogSpinStartAngle = (_cogSpinStartAngle + (elapsed * _cogSpinBpm * 360 / beatsPerRotation)) % 360;
            _cogSpinStartTime = DateTime.UtcNow;
            _cogSpinBpm = _tosuApi.GetCurrentBpm() > 0 ? _tosuApi.GetCurrentBpm() : 140;

            double intervalMs = CalculateCogSpinInterval(_cogSpinBpm);
            _cogSpinTimer.Interval = TimeSpan.FromMilliseconds(intervalMs);
        }
    }

    private async Task SetupPanelTransitionsAsync(DockPanel settingsPanel, Border buttonContainer, TextBlock versionPanel)
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

    private readonly SemaphoreSlim _panelAnimationLock = new(1, 1);

    private async Task AnimatePanelInAsync(DockPanel settingsPanel, Border buttonContainer, TextBlock versionPanel, Thickness showMargin, Thickness buttonLeftMargin)
    {
        await _panelAnimationLock.WaitAsync();
        var shouldAnimate = !_isSettingsPanelOpen;
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
            osuautodeafenLogoPanel.Margin = new Thickness(0, 0, 225, 0);
            versionPanel.Margin = new Thickness(0, 0, 225, 0);
        }).GetTask());

        await Task.WhenAll(tasks);
    }

    private async Task AnimatePanelOutAsync(DockPanel settingsPanel, Border buttonContainer, TextBlock versionPanel, Thickness hideMargin, Thickness buttonRightMargin)
    {
        await _panelAnimationLock.WaitAsync();
        var shouldAnimate = _isSettingsPanelOpen;
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
                osuautodeafenLogoPanel.Margin = new Thickness(0, 0, 0, 0);
                versionPanel.Margin = new Thickness(0, 0, 0, 0);
            }).GetTask());
    }

    private void InitializeKeybindButtonText()
    {
        var currentKeybind = RetrieveKeybindFromSettings();
        var deafenKeybindButton = this.FindControl<Button>("DeafenKeybindButton");
        if (deafenKeybindButton != null) deafenKeybindButton.Content = currentKeybind;
    }

    public class HotKey
    {
        public Key Key { get; init; }
        public KeyModifiers ModifierKeys { get; init; }
        public string FriendlyName { get; init; }

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

            return string.Join("+", parts).Replace("==", "="); // Fix for equal key
        }
    }

    private void OpenFileLocationButton_Click(object? sender, RoutedEventArgs e)
    {
        var appPath = _settingsHandler?.GetPath();
        if (appPath != null)
        {
            if (Directory.Exists(appPath))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = appPath,
                    UseShellExecute = true
                });
            }
            else
            {
                Console.WriteLine($"[ERROR] Directory does not exist: {appPath}");
            }
        }
        else
        {
            Console.WriteLine("[ERROR] App path is null.");
        }
    }

    private void ReportIssueButton_Click(object? sender, RoutedEventArgs e)
    {
        var issueUrl =
            "https://github.com/aerodite/osuautodeafen/issues/new?template=help.md&title=[BUG]%20Something%20Broke&body=help&labels=bug";
        Process.Start(new ProcessStartInfo 
        {
            FileName = issueUrl,
            UseShellExecute = true
        });
    }
}

public static class Extensions
{
    public static void SetValueSafe<T>(this T? obj, Action<T> setter) where T : class
    {
        if (obj != null) setter(obj);
    }
}