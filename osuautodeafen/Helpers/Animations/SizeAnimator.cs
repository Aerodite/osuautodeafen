using System;
using System.Threading;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Media;
using Avalonia.Styling;

namespace osuautodeafen.Helpers.Animations;

public class SizeAnimator : IDisposable
{
    private CancellationTokenSource? _scaleCancelToken;
    private bool _isDisposed;

    /// <summary>
    /// Takes an Avalonia Control and scales it with the defined parameters
    /// </summary>
    public void AnimateScale(Visual targetElement, double targetScale, double ms, Easing ease)
    {
        if (_isDisposed) throw new ObjectDisposedException(nameof(SizeAnimator));
        if (targetElement == null) throw new ArgumentNullException(nameof(targetElement));
        
        if (targetElement.RenderTransform is not TransformGroup transformGroup) return;

        ScaleTransform? scaleTransform = null;
        foreach (Transform? transform in transformGroup.Children)
        {
            if (transform is not ScaleTransform sTransform) continue;
            scaleTransform = sTransform;
            break;
        }
        
        if (scaleTransform == null) return;
        
        if (_scaleCancelToken != null)
        {
            _scaleCancelToken.Cancel();
            _scaleCancelToken.Dispose();
        }
        
        _scaleCancelToken = new CancellationTokenSource();

        double startingScaleX = scaleTransform.ScaleX;
        double startingScaleY = scaleTransform.ScaleY;
        
        scaleTransform.ScaleX = targetScale;
        scaleTransform.ScaleY = targetScale;

        Animation scaleAnim = new()
        {
            Duration = TimeSpan.FromMilliseconds(ms),
            Easing = ease
        };

        scaleAnim.Children.Add(new KeyFrame 
        { 
            Cue = new Cue(0.0), 
            Setters = 
            { 
                new Setter(ScaleTransform.ScaleXProperty, startingScaleX),
                new Setter(ScaleTransform.ScaleYProperty, startingScaleY)
            } 
        });

        scaleAnim.Children.Add(new KeyFrame 
        { 
            Cue = new Cue(1.0), 
            Setters = 
            { 
                new Setter(ScaleTransform.ScaleXProperty, targetScale),
                new Setter(ScaleTransform.ScaleYProperty, targetScale)
            } 
        });
        
        scaleAnim.RunAsync(targetElement, cancellationToken: _scaleCancelToken.Token);
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        
        _scaleCancelToken?.Cancel();
        _scaleCancelToken?.Dispose();
        _scaleCancelToken = null;
        
        _isDisposed = true;
        GC.SuppressFinalize(this);
    }
}