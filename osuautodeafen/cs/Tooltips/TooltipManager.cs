using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;

namespace osuautodeafen.cs.Tooltips;

public class TooltipManager
{
    private const double TooltipOffset = 4;

    private const double TooltipTargetOpacity = 0.78;
    private bool _isTooltipHiding;

    private string? _lastTooltipText;

    private Tooltips.TooltipState _state;

    private Point _targetPosition;

    private (double width, double height)? _tooltipSize;

    private TextBlock? _tooltipText;

    private CancellationTokenSource? _visibilityCts;
    private double _windowHeight;

    private double _windowWidth;

    public Tooltips.TooltipType CurrentTooltipType;
    private Grid? Tooltip { get; set; }

    private Border? TooltipBackground { get; set; }

    private bool IsTooltipVisible { get; set; }

    /// <summary>
    ///     Used in app setup to get tooltips ready to be used
    /// </summary>
    /// <param name="tooltipRoot"></param>
    /// <param name="tooltipText"></param>
    /// <param name="windowWidth"></param>
    /// <param name="windowHeight"></param>
    public void SetTooltipControls(Grid tooltipRoot, TextBlock tooltipText, double windowWidth, double windowHeight)
    {
        Tooltip = tooltipRoot;
        _tooltipText = tooltipText;
        _windowWidth = windowWidth;
        _windowHeight = windowHeight;

        TooltipBackground = Tooltip.FindControl<Border>("TooltipBackground");
        Tooltip.FindControl<TextBlock>("TooltipText");

        // giant pita to figure out this is why tooltips would randomly stop...
        Tooltip.IsHitTestVisible = false;

        Tooltip.Opacity = 1;
        Tooltip.IsVisible = false;

        TooltipBackground!.Opacity = 0;

        Tooltip.ClipToBounds = true;
        _tooltipText.Width = double.NaN;

        _tooltipText.TextWrapping = TextWrapping.NoWrap;

        DispatcherTimer.Run(() =>
        {
            if (Tooltip == null) return true;
            if (IsTooltipVisible && !Tooltip.IsVisible && !_isTooltipHiding)
            {
                Tooltip.IsVisible = true;
                Tooltip.Opacity = TooltipTargetOpacity;
            }

            if (_tooltipSize.HasValue)
                MoveTooltipToPosition(_targetPosition);
            return true;
        }, TimeSpan.FromMilliseconds(16));


        Canvas.SetLeft(Tooltip, 0);
        Canvas.SetTop(Tooltip, 0);
    }

    /// <summary>
    ///     Shows a tooltip at a specified position with specified text
    /// </summary>
    /// <param name="pointerPos">The pointer position relative to the window</param>
    /// <param name="text">The text to display in the tooltip</param>
    /// <param name="target">The control sending the tooltip request</param>
    public void ShowTooltip(Control? target, Point pointerPos, string text)
    {
        if (Tooltip == null || _tooltipText == null) return;

        _visibilityCts?.Cancel();
        _visibilityCts = new CancellationTokenSource();
        CancellationToken token = _visibilityCts.Token;

        // basically if the control we're over is covered or we're not over one don't show a tooltip
        if (target != null && !Tooltips.IsPointerOverElement(target, pointerPos))
        {
            if (!IsTooltipVisible && !_isTooltipHiding) return;
            ForceHideTooltip();
            return;
        }

        _lastTooltipText ??= "";
        _tooltipText.Text = text;
        _tooltipText.Width = double.NaN;

        _tooltipText.Measure(new Size(_windowWidth * 0.5, double.PositiveInfinity));
        if (TooltipBackground != null)
        {
            double tooltipWidth = _tooltipText.DesiredSize.Width + TooltipBackground.Padding.Left +
                                  TooltipBackground.Padding.Right;
            double tooltipHeight =
                _tooltipText.DesiredSize.Height + TooltipBackground.Padding.Top + TooltipBackground.Padding.Bottom;

            _tooltipSize = (tooltipWidth, tooltipHeight);
            _targetPosition = pointerPos;

            Tooltip.Width = tooltipWidth;
            Tooltip.Height = tooltipHeight;
        }

        // this is just to prevent tooltips from perma-hiding if the previous one is fading out
        if (_isTooltipHiding)
        {
            _isTooltipHiding = false;
            _state = Tooltips.TooltipState.Showing;
        }

        if (!IsTooltipVisible || Tooltip.Opacity < TooltipTargetOpacity)
        {
            Tooltip.IsVisible = true;
            _ = SetTooltipVisibility(Tooltip, true, token);
        }
    }

    /// <summary>
    ///     Smoothly interpolates the tooltip position towards wanted position
    /// </summary>
    /// <param name="position"></param>
    /// <param name="width"></param>
    /// <param name="height"></param>
    private void SmoothUpdateTooltipPosition(Point position, double width, double height)
    {
        if (Tooltip == null) return;

        double left, top;

        double distanceToRight = _windowWidth - (position.X + width + TooltipOffset);

        // tldr we're moving the cursor from bottom right to top left if we're too close to the right edge
        // so we don't obfuscate text
        double factor = Math.Clamp(-distanceToRight / (width + TooltipOffset), 0, 1);
        if (position.X + width + TooltipOffset > _windowWidth)
        {
            left = position.X - width - TooltipOffset;
            top = position.Y - height - TooltipOffset;
        }
        else if (factor > 0)
        {
            left = (position.X + TooltipOffset) * (1 - factor) + (position.X - width - TooltipOffset) * factor;
            top = (position.Y + TooltipOffset) * (1 - factor) + (position.Y - height - TooltipOffset) * factor;
        }
        else
        {
            left = position.X + TooltipOffset;
            top = position.Y + TooltipOffset;
        }

        left = Math.Clamp(left, 0, _windowWidth - width);
        top = Math.Clamp(top, 0, _windowHeight - height);

        double currentLeft = Canvas.GetLeft(Tooltip);
        if (double.IsNaN(currentLeft)) currentLeft = 0;

        double currentTop = Canvas.GetTop(Tooltip);
        if (double.IsNaN(currentTop)) currentTop = 0;

        double smoothedLeft = currentLeft + (left - currentLeft) * 0.18;
        double smoothedTop = currentTop + (top - currentTop) * 0.18;

        Canvas.SetLeft(Tooltip, Math.Round(smoothedLeft));
        Canvas.SetTop(Tooltip, Math.Round(smoothedTop));
    }

    /// <summary>
    ///     Moves the tooltip to the specified point
    /// </summary>
    /// <param name="point"></param>
    public void MoveTooltipToPosition(Point point)
    {
        _targetPosition = point;
        if (_tooltipSize.HasValue && Tooltip != null)
            SmoothUpdateTooltipPosition(_targetPosition, _tooltipSize.Value.width, _tooltipSize.Value.height);
    }

    /// <summary>
    ///     Updates the text of an already visible tooltip
    /// </summary>
    /// <param name="newText">The new text to display</param>
    /// <param name="forceResize">Whether to force a new tooltip size</param>
    public void UpdateTooltipText(string newText, bool forceResize = false)
    {
        if (Tooltip == null || _tooltipText == null)
            return;

        if (!IsTooltipVisible || _isTooltipHiding)
            return;

        if (string.Equals(_lastTooltipText, newText, StringComparison.Ordinal))
            return;

        _lastTooltipText = newText;
        _tooltipText.Text = newText;

        if (!forceResize)
            return;

        _tooltipText.Width = double.NaN;
        _tooltipText.Measure(new Size(_windowWidth * 0.5, double.PositiveInfinity));
        if (TooltipBackground != null)
        {
            double tooltipWidth = _tooltipText.DesiredSize.Width + TooltipBackground.Padding.Left +
                                  TooltipBackground.Padding.Right;
            double tooltipHeight =
                _tooltipText.DesiredSize.Height + TooltipBackground.Padding.Top + TooltipBackground.Padding.Bottom;

            _tooltipSize = (tooltipWidth, tooltipHeight);
            Tooltip.Width = tooltipWidth;
            Tooltip.Height = tooltipHeight;
        }

        MoveTooltipToPosition(_targetPosition);
    }

    /// <summary>
    ///     Hides the currently showing Tooltip
    /// </summary>
    /// <param name="delayMs">how long it takes the Tooltip to fade out</param>
    public void HideTooltip(double delayMs = 200)
    {
        if (Tooltip == null || !IsTooltipVisible) return;

        _visibilityCts?.Cancel();
        _visibilityCts = new CancellationTokenSource();
        CancellationToken token = _visibilityCts.Token;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(TimeSpan.FromMilliseconds(delayMs), token);
            }
            catch (TaskCanceledException)
            {
                return;
            }

            await Dispatcher.UIThread.InvokeAsync(async () =>
            {
                if (token.IsCancellationRequested) return;
                await SetTooltipVisibility(Tooltip, false, token);
                _lastTooltipText = null;
                _state = Tooltips.TooltipState.Hidden;
            });
        }, token);
    }

    // i can see some use in this being public incase we need to hide a tooltip immediately for whatever reason
    // ReSharper disable once MemberCanBePrivate.Global
    public void ForceHideTooltip(double durationMs = 200)
    {
        _visibilityCts?.Cancel();
        IsTooltipVisible = false;
        _lastTooltipText = null;

        if (Tooltip == null) return;

        _isTooltipHiding = true;
        _state = Tooltips.TooltipState.Hiding;
        _ = SetTooltipVisibility(Tooltip, false, null, durationMs).ContinueWith(_ => _isTooltipHiding = false);
    }

    /// <summary>
    ///     Sets the tooltip visibility between visible and hidden
    /// </summary>
    /// <param name="tooltip"></param>
    /// <param name="visible"></param>
    /// <param name="token"></param>
    /// <param name="durationMs"></param>
    private async Task SetTooltipVisibility(Grid tooltip, bool visible, CancellationToken? token = null,
        double durationMs = 120)
    {
        try
        {
            if (TooltipBackground == null) return;

            if (visible)
            {
                tooltip.IsVisible = true;
                TooltipBackground.IsVisible = true;

                await FadeIn(TooltipBackground, durationMs, token);

                TooltipBackground.Opacity = TooltipTargetOpacity;

                IsTooltipVisible = true;
            }
            else
            {
                await FadeOut(TooltipBackground, _tooltipText, durationMs, token);

                TooltipBackground.IsVisible = false;
                tooltip.IsVisible = false;
                IsTooltipVisible = false;

                if (_tooltipText != null)
                    _tooltipText.Opacity = 1;
            }


            _isTooltipHiding = false;
        }
        catch
        {
            // ignored
        }
    }

    private static async Task FadeIn(Border border, double durationMs = 120, CancellationToken? token = null)
    {
        double startOpacity = border.Opacity;
        border.IsVisible = true;
        double elapsed = 0;
        const int interval = 10;

        try
        {
            while (elapsed < durationMs)
            {
                if (token?.IsCancellationRequested ?? false) return;

                await Task.Delay(interval);
                elapsed += interval;
                double progress = elapsed / durationMs;
                border.Opacity = startOpacity + (1 - startOpacity) * progress;
            }

            border.Opacity = TooltipTargetOpacity;
        }
        catch
        {
            // ignored
        }
    }

    private static async Task FadeOut(Border background, TextBlock? text, double durationMs = 120,
        CancellationToken? token = null)
    {
        double startBg = background.Opacity;
        double startText = text.Opacity;

        double elapsed = 0;
        const int interval = 10;

        try
        {
            while (elapsed < durationMs)
            {
                if (token?.IsCancellationRequested ?? false) return;

                await Task.Delay(interval);
                elapsed += interval;
                double progress = elapsed / durationMs;

                double inv = 1 - progress;

                background.Opacity = Math.Max(0, startBg * inv);
                text.Opacity = Math.Max(0, startText * inv);
            }

            background.Opacity = 0;
            text.Opacity = 0;

            background.IsVisible = false;
        }
        catch
        {
            // ignored
        }
    }
}