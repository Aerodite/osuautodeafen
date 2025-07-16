using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using LiveChartsCore.Measure;

namespace osuautodeafen.cs.StrainGraph;

public class TooltipManager
{
    private Border? _customTooltip;
    private TextBlock? _tooltipText;
    private double _tooltipLeft;
    private double _tooltipTop;
    private double _tooltipVelocityX;
    private double _tooltipVelocityY;

    private const double TooltipOffset = 0;
    private const double SpringFrequency = 10;
    private const double SpringDamping = 1.5;

    public void SetTooltipControls(Border customTooltip, TextBlock tooltipText)
    {
        _customTooltip = customTooltip;
        _tooltipText = tooltipText;
    }

    public void ShowCustomTooltip(Point position, string text, Rect chartBounds)
    {
        if (_customTooltip == null || _tooltipText == null) return;
        _customTooltip.IsVisible = true;
        _tooltipText.Text = text;

        _customTooltip.Measure(Size.Infinity);

        var tooltipWidth = _customTooltip.Bounds.Width;
        var leftCandidate = position.X - tooltipWidth - TooltipOffset;
        var rightCandidate = position.X + TooltipOffset;
        var maxLeft = chartBounds.Width - tooltipWidth;
        
        leftCandidate = Math.Max(0, Math.Min(leftCandidate, maxLeft));
        rightCandidate = Math.Max(0, Math.Min(rightCandidate, maxLeft));

        var targetLeft = leftCandidate == 0 ? rightCandidate : leftCandidate;
        var targetTop = position.Y - _customTooltip.Bounds.Height;

        var dt = 1.0 / 60.0;
        var dx = targetLeft - _tooltipLeft;
        var ax = SpringFrequency * SpringFrequency * dx - 2.0 * SpringDamping * SpringFrequency * _tooltipVelocityX;
        _tooltipVelocityX += ax * dt;
        _tooltipLeft += _tooltipVelocityX * dt;

        var dy = targetTop - _tooltipTop;
        var ay = SpringFrequency * SpringFrequency * dy - 2.0 * SpringDamping * SpringFrequency * _tooltipVelocityY;
        _tooltipVelocityY += ay * dt;
        _tooltipTop += _tooltipVelocityY * dt;

        Canvas.SetLeft(_customTooltip, _tooltipLeft);
        Canvas.SetTop(_customTooltip, _tooltipTop);
    }

    public void HideCustomTooltip()
    {
        if (_customTooltip != null)
            _customTooltip.IsVisible = false;
    }
}