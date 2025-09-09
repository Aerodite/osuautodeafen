using System;
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
    private Point _lastTooltipPosition;
    private string? _lastTooltipText;
    private double _tooltipLeft;
    private TextBlock? _tooltipText;
    private double _tooltipTop;
    public Border? CustomTooltip { get; private set; }

    /// <summary>
    ///     Sets the custom tooltip controls to be managed.
    /// </summary>
    /// <param name="customTooltip"></param>
    /// <param name="tooltipText"></param>
    public void SetTooltipControls(Border customTooltip, TextBlock tooltipText)
    {
        CustomTooltip = customTooltip;
        _tooltipText = tooltipText;
    }

    /// <summary>
    ///     Shows a custom tooltip at the specified position with the given text.
    ///     Handles fade-in and size animation only when necessary.
    /// </summary>
    /// <param name="position"></param>
    /// <param name="text"></param>
    /// <param name="chartBounds"></param>
    public void ShowCustomTooltip(Point position, string text, Rect chartBounds)
    {
        if (CustomTooltip == null || _tooltipText == null) return;

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
            _tooltipText.Text = text;
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

    /// <summary>
    ///     Hides the custom tooltip if it is currently visible.
    /// </summary>
    public void HideCustomTooltip()
    {
        if (CustomTooltip != null && _isTooltipShowing)
        {
            if (_isAnimating)
            {
                // Stop any running animation
                _isAnimating = false;
                CustomTooltip.Width = double.NaN;
                CustomTooltip.Height = double.NaN;
                _tooltipText!.Opacity = 1;
            }

            CustomTooltip.IsVisible = false;
            _isTooltipShowing = false;
            _lastTooltipText = null;
        }
    }

    /// <summary>
    ///     Fades in the specified border over the given duration.
    /// </summary>
    /// <param name="border"></param>
    /// <param name="durationMs"></param>
    private void FadeIn(Border border, double durationMs = 150)
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
    private void FadeOut(Border border, double durationMs = 150)
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

        _lastTooltipPosition = position;
    }

    /// <summary>
    ///     Animates tooltip size and cross-fades text when content changes.
    /// </summary>
    /// <param name="newText"></param>
    /// <param name="durationMs"></param>
    private void AnimateTooltipSizeChange(string newText, double durationMs = 120)
    {
        if (CustomTooltip == null || _tooltipText == null) return;
        if (_isAnimating) return;
        _isAnimating = true;

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
}