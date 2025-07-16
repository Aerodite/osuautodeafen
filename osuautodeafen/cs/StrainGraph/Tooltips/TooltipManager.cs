using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;

namespace osuautodeafen.cs.StrainGraph.Tooltips;

public class TooltipManager
{
    private const double TooltipOffset = 0;
    private Border? _customTooltip;


    private bool _isTooltipShowing;
    private Point _lastTooltipPosition;

    private string? _lastTooltipText;
    private double _tooltipLeft;
    private TextBlock? _tooltipText;
    private double _tooltipTop;

    public void SetTooltipControls(Border customTooltip, TextBlock tooltipText)
    {
        _customTooltip = customTooltip;
        _tooltipText = tooltipText;
    }

    public void ShowCustomTooltip(Point position, string text, Rect chartBounds)
    {
        if (_customTooltip == null || _tooltipText == null) return;

        var isSameTooltip = _isTooltipShowing &&
                            _lastTooltipText == text &&
                            _lastTooltipPosition == position;

        if (!isSameTooltip)
        {
            if (!_isTooltipShowing)
            {
                _customTooltip.Opacity = 0;
                _customTooltip.IsVisible = true;
                FadeIn(_customTooltip);
            }

            _isTooltipShowing = true;
            _tooltipText.Text = text;
            _lastTooltipText = text;
            _lastTooltipPosition = position;
        }

        _customTooltip.Measure(Size.Infinity);
        var tooltipWidth = _customTooltip.Bounds.Width;
        var maxLeft = chartBounds.Width - tooltipWidth;
        var leftCandidate = position.X - tooltipWidth - TooltipOffset;
        var rightCandidate = position.X + TooltipOffset;
        var unclampedTargetLeft = leftCandidate >= 0 ? leftCandidate : rightCandidate;
        var targetLeft = Math.Max(0, Math.Min(unclampedTargetLeft, maxLeft));
        var targetTop = position.Y - _customTooltip.Bounds.Height;

        _tooltipLeft = targetLeft;
        _tooltipTop = targetTop;

        Canvas.SetLeft(_customTooltip, _tooltipLeft);
        Canvas.SetTop(_customTooltip, _tooltipTop);
    }

    public void HideCustomTooltip()
    {
        if (_customTooltip != null && _isTooltipShowing)
        {
            _customTooltip.IsVisible = false;
            _isTooltipShowing = false;
            _lastTooltipText = null;
        }
    }

    private void FadeIn(Border border, double durationMs = 150)
    {
        border.Opacity = 0;
        border.IsVisible = true;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(10) };
        double elapsed = 0;
        timer.Tick += (_, _) =>
        {
            elapsed += 10;
            border.Opacity = Math.Min(1, elapsed / durationMs);
            if (elapsed >= durationMs)
                timer.Stop();
        };
        timer.Start();
    }

    // this is finnicky but leaving here for now i guess
    private void FadeOut(Border border, double durationMs = 150)
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(10) };
        double elapsed = 0;
        var startOpacity = border.Opacity;
        timer.Tick += (_, _) =>
        {
            elapsed += 10;
            border.Opacity = Math.Max(0, startOpacity * (1 - elapsed / durationMs));
            if (elapsed >= durationMs)
            {
                border.IsVisible = false;
                timer.Stop();
            }
        };
        timer.Start();
    }
}