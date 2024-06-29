using System;
using System.IO;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
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
    private SettingsPanel _settingsPanel;
    private bool _isConstructorFinished = false;

public MainWindow()
{
    InitializeComponent();

    _tosuAPI = new TosuAPI();
    Deafen deafen = new Deafen(_tosuAPI);
    _settingsPanel = new SettingsPanel(_tosuAPI, deafen);
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

    this.FindControl<Slider>("CompletionPercentageSlider").ValueChanged += CompletionPercentageSlider_ValueChanged;


    string settingsFilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "osuautodeafen", "settings.txt");
    if (File.Exists(settingsFilePath))
    {
        string savedPercentage = File.ReadAllText(settingsFilePath);
        if (double.TryParse(savedPercentage, out double parsedPercentage))
        {
            _settingsPanel.ChangeMinCompletionPercentage(parsedPercentage);
        }
        else
        {
            _settingsPanel.ChangeMinCompletionPercentage(75);
        }
    }
    else
    {
        Directory.CreateDirectory(Path.GetDirectoryName(settingsFilePath));
        _settingsPanel.ChangeMinCompletionPercentage(75);
        File.WriteAllText(settingsFilePath, "75");
    }

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

    public void CompletionPercentageSlider_ValueChanged(object sender, Avalonia.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (_isConstructorFinished)
        {
            var slider = (Slider)sender;
            _settingsPanel.MinCompletionPercentage = slider.Value;
            _settingsPanel.ChangeMinCompletionPercentage(slider.Value);
        }
    }

    public void ResetButton_Click(object sender, RoutedEventArgs e)
    {
        _settingsPanel.ChangeMinCompletionPercentage(75);
    }


    public object MinCompletionPercentage { get; }

    private void UpdateErrorMessage(object? sender, EventArgs e)
    {
        var errorMessage = this.FindControl<TextBlock>("ErrorMessage");

        errorMessage.Text = _tosuAPI.GetErrorMessage();
    }

    private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        _settingsPanel.ChangeMinCompletionPercentage(_settingsPanel.MinCompletionPercentage);
        _tosuAPI.Dispose();
    }

    private void TosuAPI_MessageReceived(double completionPercentage)
    {
        Console.WriteLine("Received: {0}", completionPercentage);
    }

    private void SettingsButton_Click(object? sender, RoutedEventArgs e)
    {
        var animation = new Animation
        {
            Duration = TimeSpan.FromSeconds(0.5),
            Easing = new QuarticEaseInOut(),
            FillMode = FillMode.Forward
        };

        animation.Children.Add(new KeyFrame
        {
            Cue = new Cue(0),
            Setters =
            {
                new Setter
                {
                    Property = ColumnDefinition.WidthProperty,
                    Value = new GridLength(0)
                }
            }
        });

        var settingsPanel = this.FindControl<StackPanel>("SettingsPanel");

        settingsPanel.IsVisible = !settingsPanel.IsVisible;
    }
}