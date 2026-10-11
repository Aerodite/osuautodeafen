using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Fluid.Avalonia.Acrylic;
using osuautodeafen.Tooltips;
using osuautodeafen.Update;
using osuautodeafen.Helpers;
using Serilog;

namespace osuautodeafen.Notifications;

public partial class Notifications : UserControl
{
    private readonly Border _tosuWarning;
    private CancellationTokenSource? _tosuAnimationCts;
    
    private readonly Border _updateNotification;
    private readonly Button _updateButton;
    private readonly ProgressBar _updateProgressBar;
    private readonly TextBlock _updateTitle;
    private readonly TextBlock _updateDescription;

    private TooltipManager _tooltipManager;

    private CancellationTokenSource? _updateAnimationCts;
    private UpdateChecker? _updateChecker;
    private bool _isInstallingUpdate;

    public Notifications(TooltipManager tooltipManager)
    {
        _tooltipManager = tooltipManager;
        
    }

    public Notifications()
    {
        InitializeComponent();
        _tosuWarning = this.FindControl<Border>("TosuWarningPill")!;
        _updateNotification = this.FindControl<Border>("UpdateNotificationPill")!;
        _updateButton = this.FindControl<Button>("UpdateNotificationButton")!;
        _updateProgressBar = this.FindControl<ProgressBar>("UpdateProgressBar")!;
        _updateTitle = this.FindControl<TextBlock>("UpdateTitle")!;
        _updateDescription = this.FindControl<TextBlock>("UpdateDescription")!;
    }
    
    public async Task ShowNotificationAsync(NotificationType type, string title, string description, TimeSpan? duration = null)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            await Dispatcher.UIThread.InvokeAsync(() => ShowNotificationAsync(type, title, description, duration));

            return;
        }

        duration ??= TimeSpan.FromSeconds(4);

        var accent = type switch
        {
            NotificationType.Error => Color.Parse("#FF6464"),
            NotificationType.Success => Color.Parse("#60D394"),
            NotificationType.Warning => Color.Parse("#FFCB66"),
            _ => Color.Parse("#80CFFF")
        };
        
        var content = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("3,*"),
            Children =
            {
                new Border
                {
                    Background = new SolidColorBrush(accent)
                },
                new StackPanel
                {
                    Margin = new Thickness(10, 8),
                    Spacing = 2,
                    [Grid.ColumnProperty] = 1,
                    Children =
                    {
                        new TextBlock
                        {
                            Text = title,
                            FontSize = 12,
                            FontWeight = FontWeight.SemiBold,
                            Foreground = Brushes.White,
                            TextWrapping = TextWrapping.Wrap
                        },
                        new TextBlock
                        {
                            Text = description,
                            FontSize = 10,
                            Foreground = new SolidColorBrush(
                                Color.Parse("#CCE0F2FF")),
                            TextWrapping = TextWrapping.Wrap
                        }
                    }
                }
            }
        };

        var card = new Border
        {
            Width = 170,
            CornerRadius = new CornerRadius(10),
            ClipToBounds = true,
            Opacity = 0,
            RenderTransform = new TranslateTransform(-170, 0),

            Child = new AcrylicInteractiveSurface
            {
                CornerRadius = new CornerRadius(10),
                TintColor = Color.Parse("#202E35"),
                BlurRadius = 12,
                Vibrancy = 0.12,
                HighlightEnabled = true,
                ShadowEnabled = true,
                RevealBorderEnabled = true,
                RevealBorderIntensity = 0.2,
                RevealBorderRadius = 72,
                InteractiveHighlightEnabled = true,
                IsInteractive = false,

                Content = content
            }
        };

        var container = this.FindControl<StackPanel>("CustomNotifications");

        if (container is null)
            return;

        container.Children.Add(card);

        async Task AnimateAsync(bool show)
        {
            var animation = new Animation
            {
                Duration = TimeSpan.FromMilliseconds(350),
                Easing = new CubicEaseOut(),
                FillMode = FillMode.Forward,
                Children =
                {
                    new KeyFrame
                    {
                        Cue = new Cue(0),
                        Setters =
                        {
                            new Setter(OpacityProperty, show ? 0d : 1d),
                            new Setter(
                                TranslateTransform.XProperty,
                                show ? -170d : 0d)
                        }
                    },
                    new KeyFrame
                    {
                        Cue = new Cue(1),
                        Setters =
                        {
                            new Setter(OpacityProperty, show ? 1d : 0d),
                            new Setter(
                                TranslateTransform.XProperty,
                                show ? 0d : -170d)
                        }
                    }
                }
            };

            await animation.RunAsync(card);
        }

        await AnimateAsync(true);

        await Task.Delay(duration.Value);

        await AnimateAsync(false);

        container.Children.Remove(card);
    }

    public void SetUpdateChecker(UpdateChecker checker)
    {
        _updateChecker = checker;
    }
    
    public async Task SetUpdateAvailableAsync(bool available)
    {
        _updateAnimationCts?.Cancel();

        var cts = new CancellationTokenSource();
        _updateAnimationCts = cts;

        try
        {
            if (_updateNotification.RenderTransform is not TranslateTransform transform)
                return;

            if (available)
            {
                _updateTitle.Text = "Update available!";
                _updateDescription.Text = "Click here to install";
                _updateNotification.IsVisible = true;
            }

            Animation animation = new()
            {
                Duration = TimeSpan.FromMilliseconds(350),
                Easing = new CubicEaseInOut(),
                FillMode = FillMode.Forward,
                Children =
                {
                    new KeyFrame
                    {
                        Cue = new Cue(0),
                        Setters =
                        {
                            new Setter(
                                Visual.OpacityProperty,
                                _updateNotification.Opacity),

                            new Setter(
                                TranslateTransform.XProperty,
                                transform.X)
                        }
                    },
                    new KeyFrame
                    {
                        Cue = new Cue(1),
                        Setters =
                        {
                            new Setter(
                                Visual.OpacityProperty,
                                available ? 1.0 : 0.0),

                            new Setter(
                                TranslateTransform.XProperty,
                                available ? 0.0 : -240.0)
                        }
                    }
                }
            };

            await animation.RunAsync(_updateNotification, cts.Token);

            if (!available && !cts.IsCancellationRequested)
                _updateNotification.IsVisible = false;
        }
        catch (OperationCanceledException)
        {
            // ignored
        }
        finally
        {
            if (ReferenceEquals(_updateAnimationCts, cts))
                _updateAnimationCts = null;

            cts.Dispose();
        }
    }
    
    private async void UpdateNotification_Click(
        object? sender,
        RoutedEventArgs e)
    {
        if (_updateChecker?.UpdateInfo == null || _isInstallingUpdate)
            return;

        _isInstallingUpdate = true;
        _updateButton.IsEnabled = false;

        try
        {
            _updateProgressBar.Value = 0;
            _updateProgressBar.IsVisible = true;

            var progress = new Progress<int>(value =>
            {
                _updateProgressBar.Value = Math.Clamp(value, 0, 100);
            });

            await _updateChecker.DownloadUpdateAsync(value =>
            {
                Dispatcher.UIThread.Post(() =>
                {
                    ((IProgress<int>)progress).Report(value);
                });
            });

            _updateProgressBar.Value = 100;

            _updateChecker.ApplyUpdateAndRestart();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to install update");

            _updateButton.IsEnabled = true;
            _isInstallingUpdate = false;
            
            await ShowNotificationAsync(NotificationType.Error,
                "Update failed",
                "Could not install the update. Please try again.",
                TimeSpan.FromSeconds(5));
        }
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public async void SetTosuDisconnected(bool disconnected)
    {
        _tosuAnimationCts?.Cancel();

        var cts = new CancellationTokenSource();
        _tosuAnimationCts = cts;

        CancellationToken token = cts.Token;

        try
        {
            if (_tosuWarning.RenderTransform is not TranslateTransform transform)
                return;

            if (disconnected)
                _tosuWarning.IsVisible = true;

            Animation animation = new()
            {
                Duration = TimeSpan.FromMilliseconds(350),
                Easing = new CubicEaseInOut(),
                FillMode = FillMode.Forward,
                Children =
                {
                    new KeyFrame
                    {
                        Cue = new Cue(0),
                        Setters =
                        {
                            new Setter(
                                OpacityProperty,
                                _tosuWarning.Opacity),

                            new Setter(
                                TranslateTransform.XProperty,
                                transform.X)
                        }
                    },

                    new KeyFrame
                    {
                        Cue = new Cue(1),
                        Setters =
                        {
                            new Setter(
                                OpacityProperty,
                                disconnected ? 1.0 : 0.0),

                            new Setter(
                                TranslateTransform.XProperty,
                                disconnected ? 0.0 : -170.0)
                        }
                    }
                }
            };

            await animation.RunAsync(_tosuWarning, token);

            if (!disconnected && !token.IsCancellationRequested)
                _tosuWarning.IsVisible = false;
        }
        catch (OperationCanceledException)
        {
            // ignored
        }
        finally
        {
            if (ReferenceEquals(_tosuAnimationCts, cts))
                _tosuAnimationCts = null;

            cts.Dispose();
        }
    }

    private void UpdateButton_PointerEnter(object? sender, PointerEventArgs e)
    {
        if (sender is not Border)
            return;

        if (_updateChecker?.NewestAvailableVersion is not { } version)
            return;

        if (TopLevel.GetTopLevel(this) is not MainWindow window)
            return;

        Point point = Extensions.GetWindowRelativePointer(window, e);

        MainWindow.Tooltips.ShowTooltip(window, point,
            $"Update to osuautodeafen v{version}");
    }

    private void UpdateButton_PointerExit(object? sender, PointerEventArgs e)
    {
        MainWindow.Tooltips?.HideTooltip();
    }
}