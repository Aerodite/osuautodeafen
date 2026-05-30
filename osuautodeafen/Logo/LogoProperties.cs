using System;
using System.Reactive.Linq;
using System.Reactive.Disposables;
using System.Threading;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Controls;
using Avalonia.Styling;
using Avalonia.Threading;
using osuautodeafen.Helpers.Animations;

namespace osuautodeafen.Logo;

/// <summary>
/// a class which modifies the osuautodeafen logo to let it have different behavior depending on how we're interacting with it
/// </summary>
/// the implementation for this was heavily based on the original lazer cookie implementation here:
/// https://github.com/ppy/osu/blob/master/osu.Game/Screens/Menu/OsuLogo.cs
public class LogoProperties : IDisposable
{
    private readonly StackPanel _logoStackPanel;
    private readonly StackPanel _osuautodeafenLogoPanel;
    private readonly CompositeDisposable _disposables = new();
    
    private bool _isHovering;
    private bool _isDragging;
    
    private Point _startMousePos;
    public event Action<bool>? DragStateChanged;
    
    private CancellationTokenSource? _animCancelToken;
    
    private readonly SizeAnimator _sizeAnimator = new();

    public LogoProperties(StackPanel logoStackPanel, StackPanel osuautodeafenLogoPanel)
    {
        _logoStackPanel = logoStackPanel ?? throw new ArgumentNullException(nameof(logoStackPanel));
        _osuautodeafenLogoPanel = osuautodeafenLogoPanel ?? throw new ArgumentNullException(nameof(osuautodeafenLogoPanel));

        var pressedStream = Observable.FromEventPattern<PointerPressedEventArgs>(osuautodeafenLogoPanel, nameof(osuautodeafenLogoPanel.PointerPressed));
        var movedStream = Observable.FromEventPattern<PointerEventArgs>(osuautodeafenLogoPanel, nameof(osuautodeafenLogoPanel.PointerMoved));
        var releasedStream = Observable.FromEventPattern<PointerReleasedEventArgs>(osuautodeafenLogoPanel, nameof(osuautodeafenLogoPanel.PointerReleased));
        var exitedStream = Observable.FromEventPattern<PointerEventArgs>(osuautodeafenLogoPanel, nameof(osuautodeafenLogoPanel.PointerExited));
        
        pressedStream
            .Subscribe(ep => Dispatcher.UIThread.Post(() => OnPointerPressed(ep.EventArgs), DispatcherPriority.Input))
            .DisposeWith(_disposables);
        
        releasedStream
            .Subscribe(ep => Dispatcher.UIThread.Post(() => OnPointerReleased(ep.EventArgs), DispatcherPriority.Input))
            .DisposeWith(_disposables);
        
        movedStream
            .Subscribe(ep => Dispatcher.UIThread.Post(() => OnPointerMoved(ep.EventArgs), DispatcherPriority.Input))
            .DisposeWith(_disposables);
        
        exitedStream
            .Subscribe(ep => Dispatcher.UIThread.Post(() => OnPointerExited(ep.EventArgs), DispatcherPriority.Input))
            .DisposeWith(_disposables);
    }

    private void OnPointerPressed(PointerPressedEventArgs e)
    {
        PointerPoint pointerProperties = e.GetCurrentPoint(_osuautodeafenLogoPanel);
        if (!pointerProperties.Properties.IsLeftButtonPressed) return;
        
        if (_animCancelToken != null)
        {
            _animCancelToken.Cancel();
            _animCancelToken.Dispose();
            _animCancelToken = null;
        }
    
        _isDragging = true;
        _startMousePos = e.GetPosition(_osuautodeafenLogoPanel);
        
        _logoStackPanel.Transitions = null;

        DragStateChanged?.Invoke(true);
    
        e.Pointer.Capture(_osuautodeafenLogoPanel);
        e.Handled = true;
    }

    private void OnPointerMoved(PointerEventArgs e)
    {
        if (!_isDragging)
        {
            if (!_isHovering)
            {
                _isHovering = true;
                
                // osu! uses 1.1f but it feels like a bit too big of a difference
                //https://github.com/ppy/osu/blob/master/osu.Game/Screens/Menu/OsuLogo.cs#L419
                _sizeAnimator.AnimateScale(_logoStackPanel,1.04f, 500, new ElasticEaseOut());
            }
            return;
        }
        
        _isHovering = false; 

        Point currentMousePos = e.GetPosition(_osuautodeafenLogoPanel);
        
        Vector change = currentMousePos - _startMousePos;
        double changeDistance = change.Length;

        if (changeDistance > 0)
        {
            //https://github.com/ppy/osu/blob/master/osu.Game/Screens/Menu/OsuLogo.cs#L445
            double scaleMultiplier = Math.Pow(changeDistance, 0.6) / changeDistance;
            Vector targetPosition = change * scaleMultiplier;
            MoveTo(targetPosition);
        }
        else
        {
            MoveTo(Vector.Zero);
        }
    }

    private void OnPointerReleased(PointerReleasedEventArgs e)
    {
        if (!_isDragging) return;
    
        _isDragging = false;
        e.Pointer.Capture(null);
        
        DragStateChanged?.Invoke(false);

        ReturnToCenter();
        
        Point pointerPos = e.GetPosition(_osuautodeafenLogoPanel);
        Rect bounds = _osuautodeafenLogoPanel.Bounds;
    
        if (bounds.Contains(pointerPos))
        {
            _isHovering = true;
            _sizeAnimator.AnimateScale(_logoStackPanel,1.04f, 500, new ElasticEaseOut());
        }
        else
        {
            _isHovering = false;
            _sizeAnimator.AnimateScale(_logoStackPanel,1.0f, 500, new ElasticEaseOut());
        }
    }
    
    private void OnPointerExited(PointerEventArgs e)
    {
        if (_isDragging) return;
    
        if (_isHovering)
        {
            _isHovering = false;
            _sizeAnimator.AnimateScale(_logoStackPanel,1.0f, 500, new ElasticEaseOut());
        }
    }
    
    /// <summary>
    /// Specifies a position and tells the logo should move to it
    /// </summary>
    /// <param name="position"></param>
    private void MoveTo(Vector position)
    {
        if (_logoStackPanel.RenderTransform is not TransformGroup transformGroup) return;
    
        foreach (Transform? transform in transformGroup.Children)
        {
            if (transform is not TranslateTransform translateTransform) continue;
            
            translateTransform.X = position.X;
            translateTransform.Y = position.Y;
            break;
        }
    }

    /// <summary>
    /// Moves the logo back to its origin from its current position
    /// </summary>
    private void ReturnToCenter()
    {
        if (_logoStackPanel.RenderTransform is not TransformGroup transformGroup) return;

        TranslateTransform? translateTransform = null;
        foreach (Transform? transform in transformGroup.Children)
        {
            if (transform is not TranslateTransform tTransform) continue;
            translateTransform = tTransform;
            break;
        }

        if (translateTransform == null) return;
    
        double startingX = translateTransform.X;
        double startingY = translateTransform.Y;
        
        translateTransform.X = 0;
        translateTransform.Y = 0;

        _animCancelToken = new CancellationTokenSource();
        
        Animation centerReturnAnim = new()
        {
            Duration = TimeSpan.FromMilliseconds(800), 
            Easing = new ElasticEaseOut()
        };
        
        centerReturnAnim.Children.Add(new KeyFrame 
        { 
            Cue = new Cue(0.0), 
            Setters = 
            { 
                new Setter(TranslateTransform.XProperty, startingX),
                new Setter(TranslateTransform.YProperty, startingY)
            } 
        });
        
        centerReturnAnim.Children.Add(new KeyFrame 
        { 
            Cue = new Cue(1.0), 
            Setters = 
            { 
                new Setter(TranslateTransform.XProperty, 0.0),
                new Setter(TranslateTransform.YProperty, 0.0)
            } 
        });
        
        centerReturnAnim.RunAsync(_logoStackPanel, cancellationToken: _animCancelToken.Token);
    }
    
    public void Dispose()
    {
        _disposables.Dispose();
    }
}