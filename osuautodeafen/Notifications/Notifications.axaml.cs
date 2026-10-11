using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Styling;

namespace osuautodeafen.Notifications;

public partial class Notifications : UserControl
{
    private readonly Border _tosuWarning;
    private CancellationTokenSource? _tosuAnimationCts;

    public Notifications()
    {
        InitializeComponent();

        _tosuWarning = this.FindControl<Border>("TosuWarningPill")!;
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
                                Visual.OpacityProperty,
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
                                Visual.OpacityProperty,
                                disconnected ? 1.0 : 0.0),

                            new Setter(
                                TranslateTransform.XProperty,
                                disconnected ? 0.0 : -260.0)
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
            // A newer animation replaced this one.
        }
        finally
        {
            if (ReferenceEquals(_tosuAnimationCts, cts))
                _tosuAnimationCts = null;

            cts.Dispose();
        }
    }
}