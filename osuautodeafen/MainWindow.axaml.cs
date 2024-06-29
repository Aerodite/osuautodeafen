using System;
using System.Collections.Generic;
using System.IO;
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

    private readonly DispatcherTimer _timer;
    private readonly TosuAPI _tosuAPI;
    private readonly FrostedGlassEffect _frostedGlassEffect;
    private SettingsPanel _settingsPanel;
    private bool _isConstructorFinished = false;
    private double _mouseX;
    private double _mouseY;
    private readonly DispatcherTimer _disposeTimer;
    private readonly DispatcherTimer _settingsUpdateTimer;
    private TextBlock _completionPercentageText;
    public SharedViewModel ViewModel { get; } = new SharedViewModel();



    private Bitmap? _currentBitmap;
    private Bitmap? _previousBitmap;
    private BitmapHolder? _bitmapHolder;
    private Queue<Bitmap> _bitmapQueue = new Queue<Bitmap>(2);

    private readonly DispatcherTimer _backgroundUpdateTimer;
    private string? _currentBackgroundDirectory;
    public double MinCompletionPercentage { get; set; }

    public MainWindow()
    {
        InitializeComponent();

        DataContext = ViewModel;

        SettingsPanel settingsPanel = new SettingsPanel();

        _settingsPanel = new SettingsPanel();

        this.Icon = new WindowIcon(new Bitmap("Resources/oad.ico"));

        _tosuAPI = new TosuAPI();

        _backgroundUpdateTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(250)
        };
        _backgroundUpdateTimer.Tick += UpdateBackground;
        _backgroundUpdateTimer.Start();

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

        var slider = this.FindControl<Slider>("Slider");

        var sliderManager = new SliderManager(_settingsPanel, slider);;

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

        string settingsFilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "osuautodeafen", "settings.txt");
        if (File.Exists(settingsFilePath))
        {
            string text = File.ReadAllText(settingsFilePath);
            var settings = text.Split('=');
            if (settings.Length == 2 && settings[0].Trim() == "MinCompletionPercentage" && int.TryParse(settings[1], out int parsedPercentage))
            {
                ViewModel.MinCompletionPercentage = parsedPercentage;
            }
        }
        else
        {
            Directory.CreateDirectory(Path.GetDirectoryName(settingsFilePath));
            ViewModel.MinCompletionPercentage = 75;
            File.WriteAllText(settingsFilePath, $"MinCompletionPercentage={ViewModel.MinCompletionPercentage}");
        }

        CompletionPercentageTextBox.Text = ViewModel.MinCompletionPercentage.ToString();


        BorderBrush = Brushes.Black;
        this.Width = 600;
        this.Height = 600;
        this.CanResize = false;
        this.Closing += MainWindow_Closing;
        _timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _timer.Tick += UpdateErrorMessage;
        _timer.Start();
        _isConstructorFinished = true;
    }

    private void DisposeTimer_Tick(object? sender, EventArgs e)
    {
        // Dispose of the old bitmap
        if (_bitmapQueue.Count > 0)
        {
            _bitmapQueue.Dequeue().Dispose();
        }
        _disposeTimer.Stop();
    }

    private void CompletionPercentageTextBox_TextInput(object sender, Avalonia.Input.TextInputEventArgs e)
    {
        // Only allow two-digit numbers
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
            // Check if the parsed value is a two-digit number
            if (parsedPercentage >= 0 && parsedPercentage <= 99)
            {
                ViewModel.MinCompletionPercentage = parsedPercentage;
                SaveSettingsToFile(ViewModel.MinCompletionPercentage);
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

    private void SaveSettingsToFile(int minCompletionPercentage)
    {
        string SettingsFilePath =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "osuautodeafen",
                "settings.txt");

        try
        {
            // Write the MinCompletionPercentage to the settings file in the format `MinCompletionPercentage=value`
            File.WriteAllText(SettingsFilePath, $"MinCompletionPercentage={minCompletionPercentage}");
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex);
        }
    }

    private async void UpdateBackground(object? sender, EventArgs e)
    {
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

            // If the directory hasn't changed, no need to update the background
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

            Bitmap? newBitmap = File.Exists(fullBackgroundDirectory)
                ? new Bitmap(fullBackgroundDirectory)
                : CreateBlackBitmap();

            _currentBitmap = newBitmap;

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

            var frostedGlassGrid = new Grid();
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

    private Bitmap? CreateBlackBitmap()
    {
        //im not even sure if theres a line of code that uses this as a backup. oh well.
        var blackBitmap = new Bitmap("Resources/BlackBackground.png");
        return blackBitmap;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);

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
    }

    public void ResetButton_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.MinCompletionPercentage = 75;

        SaveSettingsToFile(ViewModel.MinCompletionPercentage);

        CompletionPercentageTextBox.Text = ViewModel.MinCompletionPercentage.ToString();
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
                Easing = new LinearEasing()
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

        await Task.Delay(TimeSpan.FromSeconds(0.25));

        textBlockPanel.Margin = settingsPanel.IsVisible ? new Thickness(0, 42, 200, 0) : new Thickness(0, 52, 0, 0);
    }
}