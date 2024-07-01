using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.Visuals;
using DynamicData.Binding;

namespace osuautodeafen;

public partial class MainWindow : Window
{

    private readonly DispatcherTimer _mainTimer;
    private readonly DispatcherTimer _disposeTimer;
    private readonly DispatcherTimer _parallaxCheckTimer;
    private Grid _blackBackground;
    private readonly TosuAPI _tosuAPI;
    private readonly FrostedGlassEffect _frostedGlassEffect;
    private SettingsPanel _settingsPanel;
    private bool _isConstructorFinished = false;
    private double _mouseX;
    private double _mouseY;
    private TextBlock _completionPercentageText;
    public SharedViewModel ViewModel { get; } = new SharedViewModel();
    private readonly StringBuilder _keyInput;
    private bool IsBlackBackgroundDisplayed { get; set; }
    private DateTime _lastUpdate = DateTime.Now;


    private Bitmap? _currentBitmap;
    private Bitmap? _previousBitmap;
    private BitmapHolder? _bitmapHolder;
    private Queue<Bitmap> _bitmapQueue = new Queue<Bitmap>(2);

    private string? _currentBackgroundDirectory;
    public double MinCompletionPercentage { get; set; }

    public MainWindow()
    {
        InitializeComponent();

        //UpdateChecker.OnUpdateAvailable += ShowUpdateNotification;

        SettingsPanel settingsPanel = new SettingsPanel();

        _settingsPanel = new SettingsPanel();

        LoadSettings();

        this.Icon = new WindowIcon(new Bitmap("Resources/oad.ico"));

        _tosuAPI = new TosuAPI();

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
            Interval = TimeSpan.FromMilliseconds(100)
        };
        _mainTimer.Tick += MainTimer_Tick;
        _mainTimer.Start();

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

        // Set the DataContext after reading the settings
        DataContext = ViewModel;

        ViewModel.BackgroundEnabledChanged += UpdateBackground;

        var slider = this.FindControl<Slider>("Slider");

        var sliderManager = new SliderManager(_settingsPanel, slider);
        ;

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

        CompletionPercentageTextBox.Text = ViewModel.MinCompletionPercentage.ToString();
        StarRatingTextBox.Text = ViewModel.StarRating.ToString();
        PPTextBox.Text = ViewModel.PerformancePoints.ToString();

        BorderBrush = Brushes.Black;
        this.Width = 600;
        this.Height = 600;
        this.CanResize = false;
        this.Closing += MainWindow_Closing;
        _isConstructorFinished = true;

        // Handle the PointerPressed event on the parent control
        //((Control)DeafenKeybindTextBox.Parent).PointerPressed += (sender, e) =>
        {
            // When the parent control is clicked, move the focus to the parent control
            //((Control)sender).Focus();
        };
    }

    private void MainTimer_Tick(object? sender, EventArgs? e)
    {
        Dispatcher.UIThread.InvokeAsync(() => UpdateBackground(sender, e));
        Dispatcher.UIThread.InvokeAsync(() => CheckIsFCRequiredSetting(sender, e));
        Dispatcher.UIThread.InvokeAsync(() => CheckBackgroundSetting(sender, e));
        Dispatcher.UIThread.InvokeAsync(() => CheckParallaxSetting(sender, e));
        Dispatcher.UIThread.InvokeAsync(() => UpdateErrorMessage(sender, e));
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

    private void LoadSettings()
    {
    string settingsFilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "osuautodeafen", "settings.txt");

    if (!File.Exists(settingsFilePath))
    {
        // If the settings file does not exist, create it with default settings
        ViewModel.MinCompletionPercentage = 75;
        ViewModel.StarRating = 0;
        ViewModel.PerformancePoints = 0;
        ViewModel.IsParallaxEnabled = true;
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
                // Set the TextBox text to the loaded hotkey
                //DeafenKeybindTextBox.Text = settings[1];
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
        "Hotkey=Control+P"
    };

    File.WriteAllLines(settingsFilePath, settingsLines);
}

    private void ShowUpdateNotification(string latestVersion, string latestReleaseUrl)
    {
        var updateNotificationWindow = new UpdateNotificationWindow(latestVersion, latestReleaseUrl);
        updateNotificationWindow.ShowDialog(this);
    }

    private void DeafenKeybindTextBox_PointerLeave(object? sender, PointerEventArgs e)
    {
        this.Focus();
    }

    public class HotKey
    {
        public Key Key { get; set; }
        public KeyModifiers ModifierKeys { get; set; }

        public override string ToString()
        {
            return $"{ModifierKeys} + {Key}";
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
        public static KeyModifiers KeyToKeyModifiers(Key key)
        {
            switch (key)
            {
                case Key.LeftCtrl:
                case Key.RightCtrl:
                    return KeyModifiers.Control;
                case Key.LeftAlt:
                case Key.RightAlt:
                    return KeyModifiers.Alt;
                case Key.LeftShift:
                case Key.RightShift:
                    return KeyModifiers.Shift;
                default:
                    return KeyModifiers.None;
            }
        }

        //handle key down events
    }

    private KeyModifiers _currentKeyModifiers = KeyModifiers.None;

    private void DeafenKeybindTextBox_KeyDown(object sender, Avalonia.Input.KeyEventArgs e)
    {
        _currentKeyModifiers = KeyModifiers.None;
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            _currentKeyModifiers |= KeyModifiers.Control;
        }
        if (e.KeyModifiers.HasFlag(KeyModifiers.Alt))
        {
            _currentKeyModifiers |= KeyModifiers.Alt;
        }
        if (e.KeyModifiers.HasFlag(KeyModifiers.Shift))
        {
            _currentKeyModifiers |= KeyModifiers.Shift;
        }

        if (e.Key != Key.LeftCtrl && e.Key != Key.RightCtrl && e.Key != Key.LeftAlt && e.Key != Key.RightAlt && e.Key != Key.LeftShift && e.Key != Key.RightShift)
        {
            HotKey hotKey = new HotKey { Key = e.Key, ModifierKeys = _currentKeyModifiers };
            HandleHotkeyInput(hotKey);
        }
    }

    private void HandleHotkeyInput(HotKey hotKey)
    {
        Console.WriteLine($"Hotkey {hotKey.Key} with modifiers {hotKey.ModifierKeys} pressed");

        SaveSettingsToFile(hotKey.ToString(), "Hotkey");
    }
    private void DeafenKeybindTextBox_LostFocus(object? sender, RoutedEventArgs e)
    {
        // Check if the TextBox text is not null
      //  if (!string.IsNullOrEmpty(DeafenKeybindTextBox.Text))
        {
            try
            {
               // ViewModel.DeafenKeybind = HotKey.Parse(DeafenKeybindTextBox.Text);

                // Save the valid hotkey to the settings.txt file
                SaveSettingsToFile(ViewModel.DeafenKeybind.ToString(), "DeafenKeybind");
            }
            catch (ArgumentException)
            {

                //DeafenKeybindTextBox.Text = ViewModel.DeafenKeybind.ToString();
            }
        }
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
            if (!IsBlackBackgroundDisplayed)
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
            var json = await _tosuAPI.ConnectAsync();

            if (json.Contains("\"error\":"))
            {
                Console.WriteLine("An error occurred while connecting to the API.");
                return;
            }


            var background = new Background();
            var fullBackgroundDirectory = background.GetFullBackgroundDirectory(json);

            if (fullBackgroundDirectory == _currentBackgroundDirectory)
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

            var blur = new BlurEffect
            {
                Radius = 17.27,
            };

            var blurredBackground = new Image
            {
                Source = _currentBitmap,
                Stretch = Stretch.UniformToFill,
                Effect = blur,
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

            imageGrid.Children.Add(blurredBackground);

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
        base.OnPointerMoved(e);

        if (!ViewModel.IsParallaxEnabled)
        {
            return;
        }

        if ((DateTime.Now - _lastUpdate).TotalMilliseconds < 25)
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
        ViewModel.MinCompletionPercentage = 75;
        ViewModel.StarRating = 0;
        ViewModel.PerformancePoints = 0;

        SaveSettingsToFile(ViewModel.MinCompletionPercentage, "MinCompletionPercentage");
        SaveSettingsToFile(ViewModel.StarRating, "StarRating");
        SaveSettingsToFile(ViewModel.PerformancePoints, "PerformancePoints");
        SaveSettingsToFile(ViewModel.IsParallaxEnabled ? 1 : 0, "IsParallaxEnabled");

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
        var settingsPanel = this.FindControl<StackPanel>("SettingsPanel");
        var textBlockPanel = this.FindControl<StackPanel>("TextBlockPanel");

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
            settingsPanel.Margin = new Thickness(0, 42, 0, 0);
            textBlockPanel.Margin = new Thickness(0, 42, 0, 0);
        }
        else
        {
            settingsPanel.IsVisible = true;
            settingsPanel.Margin = new Thickness(0, 42, 0, 0);
            textBlockPanel.Margin = new Thickness(0, 42, 0, 0);
        }

        textBlockPanel.Margin = settingsPanel.IsVisible ? new Thickness(0, 42, 225, 0) : new Thickness(0, 42, 0, 0);
    }
}
