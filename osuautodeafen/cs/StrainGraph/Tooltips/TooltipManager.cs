using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;

namespace osuautodeafen.cs.StrainGraph.Tooltips;

/// <summary>
///     Manages custom tooltip display and animation for chart overlays.
/// </summary>
public class TooltipManager
{
    private const double TooltipOffset = 0;
    private bool _isAnimating;

    private bool _isTooltipShowing;
    private string? _lastTooltipText;
    private double _tooltipLeft;
    private TextBlock? _tooltipText;
    private double _tooltipTop;
    
    DispatcherTimer? _textAnimTimer;
    DispatcherTimer? _sizeAnimTimer;
    public Border? CustomTooltip { get; private set; }
    
    private CancellationTokenSource? _hideCts;


    /// <summary>
    ///     Sets the custom tooltip controls to be managed.
    /// </summary>
    /// <param name="customTooltip"></param>
    /// <param name="tooltipText"></param>
    public void SetTooltipControls(Border customTooltip, TextBlock tooltipText)
    {
        CustomTooltip = customTooltip;
        _tooltipText = tooltipText;
        
        // without this we can go over the tooltip and force it to hide which is REALLY BAD
        CustomTooltip.IsHitTestVisible = false;
    }

    /// <summary>
    ///     Shows a custom tooltip at the specified position with the given text.
    /// </summary>
    /// <param name="position"></param>
    /// <param name="text"></param>
    /// <param name="chartBounds"></param>
    public void ShowCustomTooltip(Point position, string text, Rect chartBounds)
    {
        if (CustomTooltip == null || _tooltipText == null) return;
        
        _hideCts?.Cancel();
        
        _hideCts?.Cancel();
        _textAnimTimer?.Stop();
        _sizeAnimTimer?.Stop();

        
        bool isSameText = _isTooltipShowing && _lastTooltipText == text;

        TextBlock measureBlock = new()
            { Text = text, FontSize = _tooltipText.FontSize, FontFamily = _tooltipText.FontFamily };
        measureBlock.Measure(Size.Infinity);
        double newWidth = measureBlock.DesiredSize.Width + CustomTooltip.Padding.Left + CustomTooltip.Padding.Right;
        double newHeight = measureBlock.DesiredSize.Height + CustomTooltip.Padding.Top + CustomTooltip.Padding.Bottom;

        double oldWidth = CustomTooltip.Bounds.Width;
        double oldHeight = CustomTooltip.Bounds.Height;
        bool isSameSize = Math.Abs(newWidth - oldWidth) < 0.5 && Math.Abs(newHeight - oldHeight) < 0.5;

        if (!CustomTooltip.IsVisible)
        {
            CustomTooltip.Opacity = 0;
            CustomTooltip.IsVisible = true;
            FadeIn(CustomTooltip);
            if (_tooltipText.Text != text)
                UpdateTooltipText(text);
            _isTooltipShowing = true;
            _lastTooltipText = text;
        }
        else if (!isSameText)
        {
            if (!isSameSize && !_isAnimating)
                AnimateTooltipSizeChange(text);
            else
                _tooltipText.Text = text;

            _isTooltipShowing = true;
            _lastTooltipText = text;
        }

        UpdateCustomTooltipPosition(position, chartBounds);
    }

    public async void HideCustomTooltip(double delayMs = 100)
    {
        if (CustomTooltip == null || !_isTooltipShowing) return;
    
        _hideCts?.Cancel();
        _hideCts = new CancellationTokenSource();
        var token = _hideCts.Token;
    
        _textAnimTimer?.Stop();
        _sizeAnimTimer?.Stop();
        _textAnimTimer = null;
        _sizeAnimTimer = null;

        try
        {
            await Task.Delay(TimeSpan.FromMilliseconds(delayMs), token);
        }
        catch (TaskCanceledException)
        {
            return;
        }

        if (!_isTooltipShowing)
            return;

        if (_isAnimating)
        {
            _isAnimating = false;
            CustomTooltip.Width = double.NaN;
            CustomTooltip.Height = double.NaN;
            _tooltipText!.Opacity = 1;
        }
        
        FadeOut(CustomTooltip);
        
        _isTooltipShowing = false;
        _lastTooltipText = null;
    }

    /// <summary>
    ///     Fades in the specified border over the given duration.
    /// </summary>
    /// <param name="border"></param>
    /// <param name="durationMs"></param>
    private void FadeIn(Border border, double durationMs = 100)
    {
        border.Opacity = 0;
        border.IsVisible = true;
        DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(10) };
        double elapsed = 0;
        timer.Tick += (_, _) =>
        {
            if (!_isTooltipShowing)
            {
                timer.Stop();
                return;
            }

            elapsed += 10;
            border.Opacity = Math.Min(1, elapsed / durationMs);
            if (elapsed >= durationMs)
                timer.Stop();
        };
        timer.Start();
    }

    /// <summary>
    ///     Fades out the specified border over the given duration.
    /// </summary>
    /// <param name="border"></param>
    /// <param name="durationMs"></param>
    private void FadeOut(Border border, double durationMs = 100)
    {
        DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(10) };
        double elapsed = 0;
        double startOpacity = border.Opacity;
        timer.Tick += (_, _) =>
        {
            elapsed += 10;
            border.Opacity = Math.Max(0, startOpacity * (1 - (elapsed / durationMs)));
            if (elapsed >= durationMs)
            {
                border.IsVisible = false;
                timer.Stop();
            }
        };
        timer.Start();
    }

    /// <summary>
    ///     Updates the tooltip position instantly.
    /// </summary>
    /// <param name="position"></param>
    /// <param name="chartBounds"></param>
    public void UpdateCustomTooltipPosition(Point position, Rect chartBounds)
    {
        if (CustomTooltip == null || !_isTooltipShowing) return;

        CustomTooltip.Measure(Size.Infinity);
        double tooltipWidth = CustomTooltip.Bounds.Width;
        double maxLeft = chartBounds.Width - tooltipWidth;
        double leftCandidate = position.X - tooltipWidth - TooltipOffset;
        double rightCandidate = position.X + TooltipOffset;
        double unclampedTargetLeft = leftCandidate >= 0 ? leftCandidate : rightCandidate;
        double targetLeft = Math.Max(0, Math.Min(unclampedTargetLeft, maxLeft));
        double targetTop = position.Y - CustomTooltip.Bounds.Height;

        _tooltipLeft = targetLeft;
        _tooltipTop = targetTop;

        CustomTooltip.IsVisible = true;
        Canvas.SetLeft(CustomTooltip, _tooltipLeft);
        Canvas.SetTop(CustomTooltip, _tooltipTop);
    }

    /// <summary>
    ///     Animates tooltip size and cross-fades text when content changes.
    /// </summary>
    /// <param name="newText"></param>
    /// <param name="durationMs"></param>
    private void AnimateTooltipSizeChange(string newText, double durationMs = 25)
    {
        if (CustomTooltip == null || _tooltipText == null) return;
        if (_isAnimating) return;
        _isAnimating = true;
        
        _sizeAnimTimer?.Stop();
        _sizeAnimTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(10) };

        double oldWidth = CustomTooltip.Bounds.Width;
        double oldHeight = CustomTooltip.Bounds.Height;

        TextBlock measureBlock = new()
            { Text = newText, FontSize = _tooltipText.FontSize, FontFamily = _tooltipText.FontFamily };
        measureBlock.Measure(Size.Infinity);
        double newWidth = measureBlock.DesiredSize.Width + CustomTooltip.Padding.Left + CustomTooltip.Padding.Right;
        double newHeight = measureBlock.DesiredSize.Height + CustomTooltip.Padding.Top + CustomTooltip.Padding.Bottom;

        double widthDiff = newWidth - oldWidth;
        double heightDiff = newHeight - oldHeight;

        TextBlock fadeBlock = new()
        {
            Text = newText,
            FontSize = _tooltipText.FontSize,
            FontFamily = _tooltipText.FontFamily,
            Opacity = 0
        };
        if (CustomTooltip.Child is Panel panel)
            panel.Children.Add(fadeBlock);

        DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(10) };
        double elapsed = 0;
        timer.Tick += (_, _) =>
        {
            if (!_isTooltipShowing)
            {
                timer.Stop();
                _isAnimating = false;
                return;
            }

            elapsed += 10;
            double progress = Math.Min(1, elapsed / durationMs);

            CustomTooltip.Width = oldWidth + (widthDiff * progress);
            CustomTooltip.Height = oldHeight + (heightDiff * progress);

            _tooltipText.Opacity = 1 - progress;
            fadeBlock.Opacity = progress;

            if (progress >= 1)
            {
                timer.Stop();
                CustomTooltip.Width = double.NaN;
                CustomTooltip.Height = double.NaN;
                _tooltipText.Text = newText;
                _tooltipText.Opacity = 1;
                if (CustomTooltip.Child is Panel p)
                    p.Children.Remove(fadeBlock);
                _isAnimating = false;
            }
        };
        timer.Start();
    }
    
    private void UpdateTooltipText(string newText)
    {
        if (_tooltipText == null || CustomTooltip == null) return;
        
        _textAnimTimer?.Stop();

        TextBlock fadeBlock = new()
        {
            Text = newText,
            FontSize = _tooltipText.FontSize,
            FontFamily = _tooltipText.FontFamily,
            Opacity = 0
        };

        if (CustomTooltip.Child is Panel panel)
            panel.Children.Add(fadeBlock);

        _textAnimTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(10) };
        double elapsed = 0;
        const double durationMs = 120;
        _textAnimTimer.Tick += (_, _) =>
        {
            elapsed += 10;
            double progress = Math.Min(1, elapsed / durationMs);
            _tooltipText.Opacity = 1 - progress;
            fadeBlock.Opacity = progress;

            if (progress >= 1)
            {
                _textAnimTimer.Stop();
                _textAnimTimer = null;

                _tooltipText.Text = newText;
                _tooltipText.Opacity = 1;
                if (CustomTooltip.Child is Panel p)
                    p.Children.Remove(fadeBlock);
            }
        };
        _textAnimTimer.Start();
    }
}