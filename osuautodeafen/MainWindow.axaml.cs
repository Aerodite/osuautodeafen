using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.JavaScript;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using osuautodeafen.cs;

namespace osuautodeafen;

public partial class MainWindow : Window
{
    Image blackBackground;
    private bool BlurEffectUpdate { get; set; }
    private readonly DispatcherTimer _mainTimer;
    private readonly DispatcherTimer _disposeTimer;
    private readonly DispatcherTimer _parallaxCheckTimer;
    private Grid _blackBackground;
    private HotKey _deafenKeybind;
    private readonly TosuApi _tosuAPI;
    private readonly FrostedGlassEffect _frostedGlassEffect;
    private SettingsPanel _settingsPanel;
    private bool _isConstructorFinished = false;
    private double _mouseX;
    private double _mouseY;
    private TextBlock _completionPercentageText;
    public SharedViewModel ViewModel { get; set; } = new SharedViewModel();
    private readonly StringBuilder _keyInput;
    public static bool updateButtonClicked = false;
    private bool IsBlackBackgroundDisplayed { get; set; }
    private DateTime _lastUpdate = DateTime.Now;
    private DateTime _lastUpdateCheck = DateTime.MinValue;
    private DispatcherTimer _visibilityCheckTimer;
    private Image blurredBackground;
    private Image normalBackground;
    bool hasDisplayed = false;

    private UpdateChecker _updateChecker = UpdateChecker.GetInstance();
    private Bitmap? _currentBitmap;
    private Bitmap? _previousBitmap;
    private BitmapHolder? _bitmapHolder;
    private Queue<Bitmap> _bitmapQueue = new Queue<Bitmap>(2);

    public TimeSpan Interval { get; set; }

    private string? _currentBackgroundDirectory;
    public double MinCompletionPercentage { get; set; }
    public object UpdateUrl { get; private set; }

    public MainWindow()
    {
        InitializeComponent();

        SettingsPanel settingsPanel = new SettingsPanel();

        _settingsPanel = new SettingsPanel();

        LoadSettings();

        this.Icon = new WindowIcon(new Bitmap("Resources/oad.ico"));

        _tosuAPI = new TosuApi();

        _frostedGlassEffect = new FrostedGlassEffect
        {
            HorizontalAlignment = HorizontalAlignment,
            VerticalAlignment = VerticalAlignment
        };

        _disposeTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(500)
        };
        _disposeTimer.Tick += DisposeTimer_Tick;

        _parallaxCheckTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _parallaxCheckTimer.Tick += CheckParallaxSetting;
        _parallaxCheckTimer.Start();
        {
            Interval = TimeSpan.FromMilliseconds(100);
        };
        _mainTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(100)
        };
        _mainTimer.Tick += MainTimer_Tick;
        _mainTimer.Start();

        InitializeVisibilityCheckTimer();

        _keyInput = new StringBuilder();

        var oldContent = this.Content;

        this.Content = null;

        this.Content = new Grid
        {
            Children =
            {
                _frostedGlassEffect,
                new ContentControl { Content = oldContent },
            }
        };

        InitializeViewModel();

        DataContext = ViewModel;

        var slider = this.FindControl<Slider>("Slider");

        var sliderManager = new SliderManager(_settingsPanel, slider);

        settingsPanel.Transitions = new Transitions
        {
            new DoubleTransition
            {
                Property = OpacityProperty,
                Duration = TimeSpan.FromSeconds(0.5),
                Easing = new QuarticEaseInOut()
            }
        };

        Deafen deafen = new Deafen(_tosuAPI, _settingsPanel);
        this.DataContext = _settingsPanel;
        ExtendClientAreaToDecorationsHint = true;
        ExtendClientAreaTitleBarHeightHint = -1;
        ExtendClientAreaChromeHints = ExtendClientAreaChromeHints.PreferSystemChrome;
        ExtendClientAreaToDecorationsHint = true;
        ExtendClientAreaTitleBarHeightHint = 32;
        ExtendClientAreaChromeHints = ExtendClientAreaChromeHints.PreferSystemChrome;
        Background = Brushes.Black;
        PointerPressed += (sender, e) =>
        {
            var point = e.GetPosition(this);
            const int titleBarHeight = 34; // Height of the title bar + an extra 2px of wiggle room
            if (point.Y <= titleBarHeight)
            {
                BeginMoveDrag(e);
            }
        };

        InitializeKeybindButtonText();
        UpdateDeafenKeybindDisplay();

        CompletionPercentageTextBox.Text = ViewModel.MinCompletionPercentage.ToString();
        StarRatingTextBox.Text = ViewModel.StarRating.ToString();
        PPTextBox.Text = ViewModel.PerformancePoints.ToString();

        BorderBrush = Brushes.Black;
        this.Width = 600;
        this.Height = 600;
        this.CanResize = false;
        this.Closing += MainWindow_Closing;
        _isConstructorFinished = true;


    }

    private void InitializeVisibilityCheckTimer()
    {
        _visibilityCheckTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(0.5)
        };
        _visibilityCheckTimer.Tick += VisibilityCheckTimer_Tick;
        _visibilityCheckTimer.Start();
    }

    private void VisibilityCheckTimer_Tick(object? sender, EventArgs e)
    {
        blurredBackground.IsVisible = ViewModel.IsBlurEffectEnabled;
        normalBackground.IsVisible = !ViewModel.IsBlurEffectEnabled;
    }

    private async void InitializeViewModel()
    {
        await _updateChecker.FetchLatestVersionAsync();

        var viewModel = new SharedViewModel();
        {
           //UpdateStatusMessage = "v" + UpdateChecker.currentVersion,
        };

        DataContext = ViewModel;
    }
    private async void CheckForUpdatesIfNeeded()
    {
        // Avoid checking for updates too frequently
        if ((DateTime.Now - _lastUpdateCheck).TotalMinutes > 20)
        {
            _lastUpdateCheck = DateTime.Now;
            await _updateChecker.FetchLatestVersionAsync();

            if (string.IsNullOrEmpty(_updateChecker.latestVersion))
            {
                Console.WriteLine("Latest version string is null or empty.");
                return;
            }

            Version currentVersion = new Version(UpdateChecker.currentVersion);
            Version latestVersion = new Version(_updateChecker.latestVersion);

            if (latestVersion > currentVersion)
            {
                ShowUpdateNotification();
            }
        }
    }

    private void UpdateDeafenKeybindDisplay()
    {
        var currentKeybind = RetrieveKeybindFromSettings();
        DeafenKeybindButton.Content = currentKeybind.ToString();
    }

    private void DeafenKeybindButton_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.IsKeybindCaptureFlyoutOpen = !ViewModel.IsKeybindCaptureFlyoutOpen;
        var flyout = this.Resources["KeybindCaptureFlyout"] as Flyout;
        if (flyout != null)
        {
            if (ViewModel.IsKeybindCaptureFlyoutOpen)
            {
                flyout.ShowAt(DeafenKeybindButton);
            }
            else
            {
                flyout.Hide();
            }
        }
    }
    private Key _lastKeyPressed = Key.None;
    private DateTime _lastKeyPressTime = DateTime.MinValue;

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        if (!ViewModel.IsKeybindCaptureFlyoutOpen)
        {
            return;
        }

        if(e.Key == Key.NumLock)
        {
            return;
        }

        Flyout? flyout;
        if(e.Key == Key.Escape)
        {
            ViewModel.IsKeybindCaptureFlyoutOpen = false;
            flyout = this.Resources["KeybindCaptureFlyout"] as Flyout;
            if (flyout != null)
            {
                flyout.Hide();
            }
            return;
        }

        var currentTime = DateTime.Now;

        if (e.Key == _lastKeyPressed && (currentTime - _lastKeyPressTime).TotalMilliseconds < 2500)
        {
            return; // Considered a repeat key press, ignore it
        }

        _lastKeyPressed = e.Key;
        _lastKeyPressTime = currentTime;

        if (IsModifierKey(e.Key))
        {
            return;
        }

        // Capture the key and its modifiers
        KeyModifiers modifiers = KeyModifiers.None;
        if (e.KeyModifiers.HasFlag(Avalonia.Input.KeyModifiers.Control))
        {
            modifiers |= KeyModifiers.Control;
        }
        if (e.KeyModifiers.HasFlag(Avalonia.Input.KeyModifiers.Alt))
        {
            modifiers |= KeyModifiers.Alt;
        }
        if (e.KeyModifiers.HasFlag(Avalonia.Input.KeyModifiers.Shift))
        {
            modifiers |= KeyModifiers.Shift;
        }

        // Create and set the new hotkey
        ViewModel.DeafenKeybind = new HotKey { Key = e.Key, ModifierKeys = modifiers };

        // Save the new hotkey to settings
        SaveSettingsToFile(ViewModel.DeafenKeybind.ToString(), "Hotkey");

        e.Handled = true;

    }

    private void InitializeKeybindButtonText()
    {
        var currentKeybind = RetrieveKeybindFromSettings();
        DeafenKeybindButton.Content = currentKeybind.ToString();
    }

    private string RetrieveKeybindFromSettings()
    {
        string settingsFilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "osuautodeafen", "settings.txt");
        if (File.Exists(settingsFilePath))
        {
            var lines = File.ReadAllLines(settingsFilePath);
            var keybindLine = lines.FirstOrDefault(line => line.StartsWith("Hotkey="));
            if (keybindLine != null)
            {
                return keybindLine.Split('=')[1];
            }
        }
        return "Set Keybind";
    }

    public event PropertyChangedEventHandler PropertyChanged;
    protected virtual void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }


    public HotKey DeafenKeybind
    {
        get { return _deafenKeybind; }
        set
        {
            if (_deafenKeybind != value)
            {
                _deafenKeybind = value;
                OnPropertyChanged(nameof(DeafenKeybind));
                var button = this.FindControl<Button>("DeafenKeybindButton");
                if (button != null)
                {
                    button.Content = value.ToString();
                }
            }
        }
    }

    public void ShowUpdateNotification()
    {
        var notificationBar = this.FindControl<Button>("UpdateNotificationBar");
        if (notificationBar != null)
        {
            notificationBar.IsVisible = true;
        }
        else
        {
            Console.WriteLine("Notification bar control not found.");
        }
    }
    private void UpdateNotificationBar_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(ViewModel.UpdateUrl))
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = ViewModel.UpdateUrl,
                UseShellExecute = true
            });
        }
    }

    private void MainTimer_Tick(object? sender, EventArgs? e)
    {
        Dispatcher.UIThread.InvokeAsync(() => UpdateBackground(sender, e));
        Dispatcher.UIThread.InvokeAsync(() => CheckIsFCRequiredSetting(sender, e));
        Dispatcher.UIThread.InvokeAsync(() => CheckBackgroundSetting(sender, e));
        Dispatcher.UIThread.InvokeAsync(() => CheckParallaxSetting(sender, e));
        Dispatcher.UIThread.InvokeAsync(() => UpdateErrorMessage(sender, e));
        Dispatcher.UIThread.InvokeAsync(() => CheckBlurEffectSetting(sender, e));
        Dispatcher.UIThread.InvokeAsync(() => UpdateDeafenKeybindDisplay());
        Dispatcher.UIThread.InvokeAsync(CheckForUpdatesIfNeeded);
    }
    private void CheckIsFCRequiredSetting(object? sender, EventArgs? e)
    {
        {
            string settingsFilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "osuautodeafen", "settings.txt");

            if (File.Exists(settingsFilePath))
            {
                string[] lines = File.ReadAllLines(settingsFilePath);
                var fcSettingLine = Array.Find(lines, line => line.StartsWith("IsFCRequired"));
                if (fcSettingLine != null)
                {
                    var settings = fcSettingLine.Split('=');
                    if (settings.Length == 2 && bool.TryParse(settings[1], out bool parsedisFcRequired))
                    {
                        ViewModel.IsFCRequired = parsedisFcRequired;
                        this.FindControl<CheckBox>("FCToggle").IsChecked = parsedisFcRequired;
                    }
                }
                else
                {
                    ViewModel.IsFCRequired = false;
                    SaveSettingsToFile(false, "IsFcRequired");
                    this.FindControl<CheckBox>("FCToggle").IsChecked = false;
                }
            }
            else
            {
                Directory.CreateDirectory(Path.GetDirectoryName(settingsFilePath));
                ViewModel.IsFCRequired = false;
                SaveSettingsToFile(false, "IsFCRequired");
                this.FindControl<CheckBox>("FCToggle").IsChecked = false;
            }
        }
    }

    public async void CheckForUpdatesButton_Click(object sender, RoutedEventArgs e)
    {
        await _updateChecker.FetchLatestVersionAsync();

        if (string.IsNullOrEmpty(_updateChecker.latestVersion))
        {
            Console.WriteLine("Latest version string is null or empty.");
            return;
        }

        Version currentVersion = new Version(UpdateChecker.currentVersion);
        Version latestVersion = new Version(_updateChecker.latestVersion);

        if (latestVersion > currentVersion)
        {
            ShowUpdateNotification();
        }
        if (string.IsNullOrEmpty(_updateChecker.latestVersion))
        {
            ViewModel.UpdateStatusMessage = "Failed to check for updates.";
        }
        else if (latestVersion > currentVersion)
        {
            ViewModel.UpdateStatusMessage = $"Update available: v{_updateChecker.latestVersion}";
        }
        else
        {
            ViewModel.UpdateStatusMessage = "No updates available.";
        }
    }

    private void LoadSettings()
    {
    string settingsFilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "osuautodeafen", "settings.txt");

    if (!File.Exists(settingsFilePath))
    {
        ViewModel.MinCompletionPercentage = 60;
        ViewModel.StarRating = 0;
        ViewModel.PerformancePoints = 0;
        ViewModel.IsParallaxEnabled = true;
        ViewModel.IsBlurEffectEnabled = true;
        ViewModel.IsFCRequired = true;
        SaveSettingsToFile();
        return;
    }

    string[] settingsLines = File.ReadAllLines(settingsFilePath);
    foreach (var line in settingsLines)
    {
        var settings = line.Split('=');
        if (settings.Length != 2) continue;

        switch (settings[0].Trim())
        {
            case "Hotkey":
                break;
            case "MinCompletionPercentage":
                if (int.TryParse(settings[1], out int parsedPercentage))
                {
                    ViewModel.MinCompletionPercentage = parsedPercentage;
                }
                break;
            case "StarRating":
                if (int.TryParse(settings[1], out int parsedRating))
                {
                    ViewModel.StarRating = parsedRating;
                }
                break;
            case "PerformancePoints":
                if (int.TryParse(settings[1], out int parsedPP))
                {
                    ViewModel.PerformancePoints = parsedPP;
                }
                break;
            case "IsParallaxEnabled":
                if (bool.TryParse(settings[1], out bool parsedIsParallaxEnabled))
                {
                    ViewModel.IsParallaxEnabled = parsedIsParallaxEnabled;
                }
                break;
            case "IsBlurEffectEnabled":
                if (bool.TryParse(settings[1], out bool parsedIsBlurEffectEnabled))
                {
                    ViewModel.IsBlurEffectEnabled = parsedIsBlurEffectEnabled;
                }
                break;
        }
    }
}

private void SaveSettingsToFile()
{
    string settingsFilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "osuautodeafen", "settings.txt");

    Directory.CreateDirectory(Path.GetDirectoryName(settingsFilePath));

    string[] settingsLines = new string[]
    {
        $"MinCompletionPercentage={ViewModel.MinCompletionPercentage}",
        $"StarRating={ViewModel.StarRating}",
        $"PerformancePoints={ViewModel.PerformancePoints}",
        $"IsParallaxEnabled={ViewModel.IsParallaxEnabled}",
        $"IsBlurEffectEnabled={ViewModel.IsBlurEffectEnabled}",
        $"Hotkey={ViewModel.DeafenKeybind}"
    };

    //update updatedeafenkeybinddisplay with hotkey
    if (ViewModel.DeafenKeybind != null)
    {
        UpdateDeafenKeybindDisplay();
    }


    File.WriteAllLines(settingsFilePath, settingsLines);
}


    public class HotKey
    {
        public Key Key { get; set; }
        public KeyModifiers ModifierKeys { get; set; }
        public override string ToString()
        {
            List<string> parts = new List<string>();

            if (ModifierKeys.HasFlag(KeyModifiers.Control))
                parts.Add("Ctrl");
            if (ModifierKeys.HasFlag(KeyModifiers.Alt))
                parts.Add("Alt");
            if (ModifierKeys.HasFlag(KeyModifiers.Shift))
                parts.Add("Shift");

            parts.Add(Key.ToString()); // Always add the key last

            return string.Join("+", parts); // Join all parts with '+'
        }
        public static HotKey Parse(string str)
        {
            if (string.IsNullOrEmpty(str))
            {
                throw new ArgumentException("Invalid hotkey format. Expected 'KeyModifierKey'.");
            }

            string[] parts = str.Split('+');
            if (parts.Length != 2)
            {
                throw new ArgumentException("Invalid hotkey format. Expected 'KeyModifierKey'.");
            }

            if (!Enum.TryParse(parts[0], true, out KeyModifiers modifierKeys))
            {
                throw new ArgumentException($"Invalid modifier key: {parts[0]}");
            }

            if (!Enum.TryParse(parts[1], true, out Key key))
            {
                throw new ArgumentException($"Invalid key: {parts[1]}");
            }

            return new HotKey { Key = key, ModifierKeys = modifierKeys };
        }

    }

    private KeyModifiers _currentKeyModifiers = KeyModifiers.None;


    private bool IsModifierKey(Key key)
    {
        return key == Key.LeftCtrl || key == Key.RightCtrl ||
               key == Key.LeftAlt || key == Key.RightAlt ||
               key == Key.LeftShift || key == Key.RightShift;
    }

    private void CheckBackgroundSetting(object? sender, EventArgs? e)
    {
        string settingsFilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "osuautodeafen", "settings.txt");

        if (File.Exists(settingsFilePath))
        {
            string[] lines = File.ReadAllLines(settingsFilePath);
            var backgroundSettingLine = Array.Find(lines, line => line.StartsWith("IsBackgroundEnabled"));
            if (backgroundSettingLine != null)
            {
                var settings = backgroundSettingLine.Split('=');
                if (settings.Length == 2 && bool.TryParse(settings[1], out bool parsedIsBackgroundEnabled))
                {
                    ViewModel.IsBackgroundEnabled = parsedIsBackgroundEnabled;
                    this.FindControl<CheckBox>("BackgroundToggle").IsChecked = parsedIsBackgroundEnabled;
                }
            }
            else
            {
                ViewModel.IsBackgroundEnabled = true;
                SaveSettingsToFile(true, "IsBackgroundEnabled");
                this.FindControl<CheckBox>("BackgroundToggle").IsChecked = true;
            }
        }
        else
        {
            Directory.CreateDirectory(Path.GetDirectoryName(settingsFilePath));
            ViewModel.IsBackgroundEnabled = true;
            SaveSettingsToFile(true, "IsBackgroundEnabled");
            this.FindControl<CheckBox>("BackgroundToggle").IsChecked = true;
        }
    }

    private void CheckParallaxSetting(object? sender, EventArgs? e)
    {
        string settingsFilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "osuautodeafen", "settings.txt");

        if (File.Exists(settingsFilePath))
        {
            string[] lines = File.ReadAllLines(settingsFilePath);
            var parallaxSettingLine = Array.Find(lines, line => line.StartsWith("IsParallaxEnabled"));
            if (parallaxSettingLine != null)
            {
                var settings = parallaxSettingLine.Split('=');
                if (settings.Length == 2 && bool.TryParse(settings[1], out bool parsedIsParallaxEnabled))
                {
                    ViewModel.IsParallaxEnabled = parsedIsParallaxEnabled;
                    this.FindControl<CheckBox>("ParallaxToggle").IsChecked = parsedIsParallaxEnabled;
                }
            }
            else
            {
                ViewModel.IsParallaxEnabled = true;
                SaveSettingsToFile(true, "IsParallaxEnabled");
                this.FindControl<CheckBox>("ParallaxToggle").IsChecked = true;
            }
        }
        else
        {
            Directory.CreateDirectory(Path.GetDirectoryName(settingsFilePath));
            ViewModel.IsParallaxEnabled = true;
            SaveSettingsToFile(true, "IsParallaxEnabled");
            this.FindControl<CheckBox>("ParallaxToggle").IsChecked = true;
        }
    }

    private void CheckBlurEffectSetting(object? sender, EventArgs? e)
    {
        string settingsFilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "osuautodeafen", "settings.txt");

        bool defaultBlurEffectEnabled = true;

        if (File.Exists(settingsFilePath))
        {
            try
            {
                string[] lines = File.ReadAllLines(settingsFilePath);
                string blurEffectSettingLine = Array.Find(lines, line => line.StartsWith("IsBlurEffectEnabled"));
                if (blurEffectSettingLine != null)
                {
                    string[] parts = blurEffectSettingLine.Split('=');
                    if (parts.Length == 2 && bool.TryParse(parts[1], out bool parsedIsBlurEffectEnabled))
                    {
                        ViewModel.IsBlurEffectEnabled = parsedIsBlurEffectEnabled;
                    }
                    else
                    {
                        ViewModel.IsBlurEffectEnabled = defaultBlurEffectEnabled;
                    }
                }
                else
                {
                    ViewModel.IsBlurEffectEnabled = defaultBlurEffectEnabled;
                }
            }
            catch
            {
                ViewModel.IsBlurEffectEnabled = defaultBlurEffectEnabled;
            }
        }
        else
        {
            ViewModel.IsBlurEffectEnabled = defaultBlurEffectEnabled;
        }

        this.FindControl<CheckBox>("BlurEffectToggle").IsChecked = ViewModel.IsBlurEffectEnabled;

        UpdateBackground();
    }
    private void DisposeTimer_Tick(object? sender, EventArgs e)
    {
        if (_bitmapQueue.Count > 0)
        {
            _bitmapQueue.Dequeue().Dispose();
        }
        _disposeTimer.Stop();
    }

    private void CompletionPercentageTextBox_TextInput(object sender, Avalonia.Input.TextInputEventArgs e)
    {
        Regex regex = new Regex("^[0-9]{1,2}$");
        if (!regex.IsMatch(e.Text))
        {
            e.Handled = true;
        }
    }
    private void CompletionPercentageTextBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (int.TryParse(CompletionPercentageTextBox.Text, out int parsedPercentage))
        {
            if (parsedPercentage >= 0 && parsedPercentage <= 99)
            {
                ViewModel.MinCompletionPercentage = parsedPercentage;
                SaveSettingsToFile(ViewModel.MinCompletionPercentage, "MinCompletionPercentage");
            }
            else
            {
                CompletionPercentageTextBox.Text = ViewModel.MinCompletionPercentage.ToString();
            }
        }
        else
        {
            CompletionPercentageTextBox.Text = ViewModel.MinCompletionPercentage.ToString();
        }
    }

    private void StarRatingTextBox_TextInput(object sender, Avalonia.Input.TextInputEventArgs e)
    {
        Regex regex = new Regex("^[0-9]{1,2}$");
        if (!regex.IsMatch(e.Text))
        {
            e.Handled = true;
        }
    }
    private void StarRatingTextBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (int.TryParse(StarRatingTextBox.Text, out int parsedRating))
        {
            if (parsedRating >= 0 && parsedRating <= 15)
            {
                ViewModel.StarRating = parsedRating;
                SaveSettingsToFile(ViewModel.StarRating, "StarRating");
            }
            else
            {
                StarRatingTextBox.Text = ViewModel.StarRating.ToString();
            }
        }
        else
        {
            StarRatingTextBox.Text = ViewModel.StarRating.ToString();
        }
    }

    private void PPTextBox_TextInput(object sender, Avalonia.Input.TextInputEventArgs e)
    {
        Regex regex = new Regex("^[0-9]{1,4}$");
        if (!regex.IsMatch(e.Text))
        {
            e.Handled = true;
        }
    }
    private void PPTextBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (int.TryParse(PPTextBox.Text, out int parsedPP))
        {
            if (parsedPP >= 0 && parsedPP <= 9999)
            {
                ViewModel.PerformancePoints = parsedPP;
                SaveSettingsToFile(ViewModel.PerformancePoints, "PerformancePoints");
            }
            else
            {
                PPTextBox.Text = ViewModel.PerformancePoints.ToString();
            }
        }
        else
        {
            PPTextBox.Text = ViewModel.PerformancePoints.ToString();
        }
    }

    public void SaveSettingsToFile(object value, string settingName)
    {
        string settingsFilePath =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "osuautodeafen",
                "settings.txt");
        try
        {
            var lines = File.ReadAllLines(settingsFilePath);

            var index = Array.FindIndex(lines, line => line.StartsWith(settingName));

            string? valueString = value is bool b ? (b ? "true" : "false") : value.ToString();

            if (index != -1)
            {
                lines[index] = $"{settingName}={valueString}";
            }
            else
            {
                var newLines = new List<string>(lines) { $"{settingName}={valueString}" };
                lines = newLines.ToArray();
            }

            File.WriteAllLines(settingsFilePath, lines);
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex);
        }
    }

     public async void UpdateBackground(object? sender, EventArgs? e)
    {
        //await UpdateChecker.CheckForUpdates();
        if (!ViewModel.IsBackgroundEnabled)
        {
            if (!ViewModel.IsBackgroundEnabled && !IsBlackBackgroundDisplayed)
            {
                DisplayBlackBackground();
                IsBlackBackgroundDisplayed = true;

            }
            return;
        }


        if (_blackBackground != null)
        {
            _blackBackground.Children.Clear();
            _blackBackground = null;
        }

        try
        {

            var background = new Background();
            var fullBackgroundDirectory = _tosuAPI.GetBackgroundPath();

            if ((fullBackgroundDirectory == _currentBackgroundDirectory) && !IsBlackBackgroundDisplayed)
            {
                return;
            }

            //Console.WriteLine(fullBackgroundDirectory);

            if (!File.Exists(fullBackgroundDirectory))
            {
                Console.WriteLine("The file does not exist: " + fullBackgroundDirectory);
                return;
            }

            if (fullBackgroundDirectory == null)
            {
                return;
            }

            Bitmap? newBitmap = File.Exists(fullBackgroundDirectory)
                ? new Bitmap(fullBackgroundDirectory)
                : CreateBlackBitmap();

            _currentBitmap = newBitmap;

            IsBlackBackgroundDisplayed = false;

            BlurEffect blur = null;


                blur = new BlurEffect
                {
                    Radius = 17.27,
                };


            blurredBackground = new Image
            {
                Source = _currentBitmap,
                Stretch = Stretch.UniformToFill,
                Opacity = 0.5,
                Effect = blur,
                ZIndex = -1
            };

            normalBackground = new Image
            {
                Source = _currentBitmap,
                Stretch = Stretch.UniformToFill,
                Opacity = 0.5,
                ZIndex = -1
            };

            //dont move this to a higher line or the program will crash and memory leak. lol.
            //that was a fun 2 hours :)
            if (this.Content is Grid mainGrid && mainGrid.Children[1] is Grid innerGrid)
            {
                innerGrid.Children.Clear();
            }


            //grid hell down below

            var oldContent = this.Content;

            this.Content = null;

            var imageGrid = new Grid();

            if (_blackBackground != null)
            {
                _blackBackground.Children.Clear();
                _blackBackground = null;
            }
            
            
            imageGrid.Children.Add(blurredBackground);
            imageGrid.Children.Add(normalBackground);

            blurredBackground.IsVisible = ViewModel.IsBlurEffectEnabled;
            normalBackground.IsVisible = !ViewModel.IsBlurEffectEnabled;


            double offsetX = (_mouseX - this.Width / 2) / 20;
            double offsetY = (_mouseY - this.Height / 2) / 20;

            var transformGroup = new TransformGroup();
            transformGroup.Children.Add(new ScaleTransform(1.5, 1.5));

            var grid = new Grid();
            grid.ZIndex = -1;

            if (oldContent is ContentControl oldContentControl)
            {
                oldContentControl.Content = null;
            }

            if (_frostedGlassEffect.Parent is Grid frostedGlassParent)
            {
                frostedGlassParent.Children.Remove(_frostedGlassEffect);
            }

            grid.Children.Add(new ContentControl { Content = oldContent });
            grid.Children.Add(imageGrid);
            imageGrid.ZIndex = -1;

            var frostedGlassGrid = new Grid();
            frostedGlassGrid.ZIndex = -1;
            frostedGlassGrid.Children.Add(_frostedGlassEffect);

            grid.Children.Add(frostedGlassGrid);
            frostedGlassGrid.ZIndex = -1;

            this.Content = grid;
            grid.ZIndex = -1;

            _currentBackgroundDirectory = fullBackgroundDirectory;
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex);
        }
    }
    public void UpdateBackground()
    {
        UpdateBackground(null, null);
    }

    private Bitmap? CreateBlackBitmap()
    {
        //im not even sure if theres a line of code that uses this as a backup. oh well.
        var blackBitmap = new Bitmap("Resources/BlackBackground.png");
        return blackBitmap;
    }

    private void DisplayBlackBackground()
    {
        var blackBitmap = CreateBlackBitmap();

        var imageGrid = new Grid();
        imageGrid.Children.Add(new Image
        {
            Source = blackBitmap,
            Stretch = Stretch.UniformToFill,
            Opacity = 0.5,
            ZIndex = -1
        });

        if (this.Content is Grid mainGrid && mainGrid.Children[1] is Grid innerGrid)
        {
            innerGrid.Children.Clear();
        }

        var oldContent = this.Content;

        this.Content = null;

        var grid = new Grid();
        grid.ZIndex = -1;

        if (oldContent is ContentControl oldContentControl)
        {
            oldContentControl.Content = null;
        }

        if (_frostedGlassEffect.Parent is Grid frostedGlassParent)
        {
            frostedGlassParent.Children.Remove(_frostedGlassEffect);
        }

        grid.Children.Add(new ContentControl { Content = oldContent });
        grid.Children.Add(imageGrid);
        imageGrid.ZIndex = -1;

        var frostedGlassGrid = new Grid();
        frostedGlassGrid.ZIndex = -1;
        frostedGlassGrid.Children.Add(_frostedGlassEffect);

        grid.Children.Add(frostedGlassGrid);
        frostedGlassGrid.ZIndex = -1;

        this.Content = grid;
        grid.ZIndex = -1;

        _blackBackground = imageGrid;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        if (!ViewModel.IsParallaxEnabled)
        {
            return;
        }

        base.OnPointerMoved(e);

        if ((DateTime.Now - _lastUpdate).TotalMilliseconds < 20)
        {
            return;
        }

        var position = e.GetPosition(this);
        _mouseX = position.X;
        _mouseY = position.Y;

        double offsetX = (_mouseX - this.Width / 2) / 20;
        double offsetY = (_mouseY - this.Height / 2) / 20;

        if (this.Content is Grid grid && grid.Children[1] is Grid imageGrid)
        {
            var transformGroup = new TransformGroup();

            //without this the image will have an ugly black border if you move your cursor
            transformGroup.Children.Add(new ScaleTransform(1.2, 1.2));

            transformGroup.Children.Add(new TranslateTransform(-offsetX, -offsetY));

            imageGrid.RenderTransform = transformGroup;

            imageGrid.ZIndex = -1;
        }

        _lastUpdate = DateTime.Now;
    }

    public void ResetButton_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.MinCompletionPercentage = 60;
        ViewModel.StarRating = 0;
        ViewModel.PerformancePoints = 0;

        SaveSettingsToFile(ViewModel.MinCompletionPercentage, "MinCompletionPercentage");
        SaveSettingsToFile(ViewModel.StarRating, "StarRating");
        SaveSettingsToFile(ViewModel.PerformancePoints, "PerformancePoints");
        SaveSettingsToFile(ViewModel.IsParallaxEnabled ? true : false, "IsParallaxEnabled");
        SaveSettingsToFile(ViewModel.IsBlurEffectEnabled ? true : false, "IsBlurEffectEnabled");

        CompletionPercentageTextBox.Text = ViewModel.MinCompletionPercentage.ToString();
        StarRatingTextBox.Text = ViewModel.StarRating.ToString();
        PPTextBox.Text = ViewModel.PerformancePoints.ToString();
    }

    private void UpdateErrorMessage(object? sender, EventArgs e)
    {
        var errorMessage = this.FindControl<TextBlock>("ErrorMessage");

        errorMessage.Text = _tosuAPI.GetErrorMessage();
    }

    private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        _tosuAPI.Dispose();
    }

    private void TosuAPI_MessageReceived(double completionPercentage)
    {
        Console.WriteLine("Received: {0}", completionPercentage);
    }

    private async void SettingsButton_Click(object? sender, RoutedEventArgs e)
    {
        var updateBar = this.FindControl<Button>("UpdateNotificationBar");
        bool isUpdateBarVisible = updateBar != null && updateBar.IsVisible;
        var settingsPanel = this.FindControl<DockPanel>("SettingsPanel");
        var textBlockPanel = this.FindControl<StackPanel>("TextBlockPanel");
        Thickness settingsPanelMargin = settingsPanel.Margin;
        Thickness textBlockPanelMargin = textBlockPanel.Margin;


        settingsPanel.Transitions = new Transitions
        {
            new ThicknessTransition
            {
                Property = MarginProperty,
                Duration = TimeSpan.FromSeconds(0.25),
                Easing = new LinearEasing()
            }
        };

        textBlockPanel.Transitions = new Transitions
        {
            new ThicknessTransition
            {
                Property = MarginProperty,
                Duration = TimeSpan.FromSeconds(0.25),
                Easing = new CircularEaseInOut()
            }
        };

        if (settingsPanel.IsVisible)
        {
            settingsPanel.IsVisible = false;
            if (isUpdateBarVisible)
            {
                settingsPanel.Margin = new Thickness(settingsPanelMargin.Left, settingsPanelMargin.Top, settingsPanelMargin.Right, 28);
                textBlockPanel.Margin = new Thickness(textBlockPanelMargin.Left, textBlockPanelMargin.Top, textBlockPanelMargin.Right, 28);
            }
            else
            {
                settingsPanel.Margin = new Thickness(settingsPanelMargin.Left, settingsPanelMargin.Top, settingsPanelMargin.Right, 0);
                textBlockPanel.Margin = new Thickness(textBlockPanelMargin.Left, textBlockPanelMargin.Top, textBlockPanelMargin.Right, 0);
            }
        }
        else
        {
            settingsPanel.IsVisible = true;
            if (isUpdateBarVisible)
            {
                settingsPanel.Margin = new Thickness(settingsPanelMargin.Left, settingsPanelMargin.Top, settingsPanelMargin.Right, 28);
                textBlockPanel.Margin = new Thickness(textBlockPanelMargin.Left, textBlockPanelMargin.Top, textBlockPanelMargin.Right, 28);
            }
            else
            {
                settingsPanel.Margin = new Thickness(settingsPanelMargin.Left, settingsPanelMargin.Top, settingsPanelMargin.Right, 0);
                textBlockPanel.Margin = new Thickness(textBlockPanelMargin.Left, textBlockPanelMargin.Top, textBlockPanelMargin.Right, 0);
            }
        }

        textBlockPanel.Margin = settingsPanel.IsVisible ? new Thickness(0, 42, 225, 0) : new Thickness(0, 42, 0, 0);
    }
}
