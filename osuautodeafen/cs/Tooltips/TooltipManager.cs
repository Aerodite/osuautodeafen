using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;

namespace osuautodeafen.cs.Tooltips
{
    public class TooltipManager
    {
        private const double TooltipOffset = 4;

        private bool _isTooltipShowing;
        private string? _lastTooltipText;

        private TextBlock? _tooltipText;
        private Border? CustomTooltip { get; set; }

        private CancellationTokenSource? _visibilityCts;

        private double _windowWidth;
        private double _windowHeight;

        public Tooltips.TooltipType CurrentTooltipType;

        private (double width, double height)? _tooltipSize;
        public bool IsTooltipVisible => _isTooltipShowing;
        private bool _isTooltipHiding;

        private Tooltips.TooltipState _state;

        private Point _targetPosition;

        /// <summary>
        ///  Used in app setup to get tooltips ready to be used
        /// </summary>
        /// <param name="customTooltip"></param>
        /// <param name="tooltipText"></param>
        /// <param name="windowWidth"></param>
        /// <param name="windowHeight"></param>
        public void SetTooltipControls(Border customTooltip, TextBlock tooltipText, double windowWidth, double windowHeight)
        {
            CustomTooltip = customTooltip;
            _tooltipText = tooltipText;
            _windowWidth = windowWidth;
            _windowHeight = windowHeight;

            // giant pita to figure out this is why tooltips would randomly stop...
            CustomTooltip.IsHitTestVisible = false;

            CustomTooltip.Opacity = 0;
            CustomTooltip.IsVisible = false;

            CustomTooltip.ClipToBounds = true;
            _tooltipText.Width = double.NaN;

            _tooltipText.TextWrapping = TextWrapping.NoWrap;

            DispatcherTimer.Run(() =>
            {
                if (CustomTooltip == null) return true;
                if (_isTooltipShowing && !CustomTooltip.IsVisible && !_isTooltipHiding)
                {
                    CustomTooltip.IsVisible = true;
                    CustomTooltip.Opacity = 1;
                }

                if (_tooltipSize.HasValue)
                    MoveTooltipToPosition(_targetPosition);
                return true;
            }, TimeSpan.FromMilliseconds(16));


            Canvas.SetLeft(CustomTooltip, 0);
            Canvas.SetTop(CustomTooltip, 0);
        }

        /// <summary>
        /// Shows a tooltip at a specified position with specified text
        /// </summary>
        /// <param name="pointerInWindow">The pointer position relative to the window</param>
        /// <param name="text">The text to display in the tooltip</param>
        /// <param name="target">The control sending the tooltip request</param>
        public void ShowTooltip(Control? target, Point pointerInWindow, string text)
        {
            if (CustomTooltip == null || _tooltipText == null) return;
            
            _visibilityCts?.Cancel();
            _visibilityCts = new CancellationTokenSource();
            var token = _visibilityCts.Token;
            
            // basically if the control we're over is covered or we're not over one don't show a tooltip
            if (!Tooltips.IsPointerOverElement(target, pointerInWindow))
            {
                if (!_isTooltipShowing && !_isTooltipHiding) return;
                ForceHideTooltip();
                return;
            }

            _lastTooltipText ??= "";
            _tooltipText.Text = text;
            _tooltipText.Width = double.NaN;

            _tooltipText.Measure(new Size(_windowWidth * 0.5, double.PositiveInfinity));
            double tooltipWidth = _tooltipText.DesiredSize.Width + CustomTooltip.Padding.Left + CustomTooltip.Padding.Right;
            double tooltipHeight = _tooltipText.DesiredSize.Height + CustomTooltip.Padding.Top + CustomTooltip.Padding.Bottom;

            _tooltipSize = (tooltipWidth, tooltipHeight);
            _targetPosition = pointerInWindow;

            CustomTooltip.Width = tooltipWidth;
            CustomTooltip.Height = tooltipHeight;

            // this is just to prevent tooltips from perma-hiding if the previous one is fading out
            if (_isTooltipHiding)
            {
                _isTooltipHiding = false;
                _state = Tooltips.TooltipState.Showing;
            }

            if (!_isTooltipShowing || CustomTooltip.Opacity < 1)
            {
                CustomTooltip.IsVisible = true;
                _ = SetTooltipVisibility(CustomTooltip, true, token);
            }
        }
        
        /// <summary>
        /// Smoothly interpolates the tooltip position towards wanted position
        /// </summary>
        /// <param name="position"></param>
        /// <param name="width"></param>
        /// <param name="height"></param>
        private void SmoothUpdateTooltipPosition(Point position, double width, double height)
        {
            if (CustomTooltip == null) return;

            double left, top;
            
            double distanceToRight = _windowWidth - (position.X + width + TooltipOffset);
            
            // tldr we're moving the cursor from bottom right to top left if we're too close to the right edge
            // so we don't obfuscate text
            double factor = Math.Clamp(- distanceToRight / (width + TooltipOffset), 0, 1);
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

            double currentLeft = Canvas.GetLeft(CustomTooltip);
            if (double.IsNaN(currentLeft)) currentLeft = 0;

            double currentTop = Canvas.GetTop(CustomTooltip);
            if (double.IsNaN(currentTop)) currentTop = 0;
            
            double smoothedLeft = currentLeft + (left - currentLeft) * 0.18;
            double smoothedTop = currentTop + (top - currentTop) * 0.18;

            Canvas.SetLeft(CustomTooltip, Math.Round(smoothedLeft));
            Canvas.SetTop(CustomTooltip, Math.Round(smoothedTop));
        }
        
        /// <summary>
        /// Moves the tooltip to the specified point
        /// </summary>
        /// <param name="point"></param>
        public void MoveTooltipToPosition(Point point)
        {
            _targetPosition = point;
            if (_tooltipSize.HasValue && CustomTooltip != null)
                SmoothUpdateTooltipPosition(_targetPosition, _tooltipSize.Value.width, _tooltipSize.Value.height);
        }

        /// <summary>
        /// Updates the text of an already visible tooltip
        /// </summary>
        /// <param name="newText">The new text to display</param>
        /// <param name="forceResize">Whether to force a new tooltip size</param>
        public void UpdateTooltipText(string newText, bool forceResize = false)
        {
            if (CustomTooltip == null || _tooltipText == null)
                return;

            if (!_isTooltipShowing || _isTooltipHiding)
                return;

            if (string.Equals(_lastTooltipText, newText, StringComparison.Ordinal))
                return;

            _lastTooltipText = newText;
            _tooltipText.Text = newText;
            
            if (!forceResize)
                return;
            
            _tooltipText.Width = double.NaN;
            _tooltipText.Measure(new Size(_windowWidth * 0.5, double.PositiveInfinity));
            double tooltipWidth = _tooltipText.DesiredSize.Width + CustomTooltip.Padding.Left + CustomTooltip.Padding.Right;
            double tooltipHeight = _tooltipText.DesiredSize.Height + CustomTooltip.Padding.Top + CustomTooltip.Padding.Bottom;

            _tooltipSize = (tooltipWidth, tooltipHeight);
            CustomTooltip.Width = tooltipWidth;
            CustomTooltip.Height = tooltipHeight;
            
            MoveTooltipToPosition(_targetPosition);
        }
        
        /// <summary>
        /// Hides the currently showing Tooltip
        /// </summary>
        /// <param name="delayMs">how long it takes the Tooltip to fade out</param>
        public void HideTooltip(double delayMs = 200)
        {
            if (CustomTooltip == null || !_isTooltipShowing) return;

            _visibilityCts?.Cancel();
            _visibilityCts = new CancellationTokenSource();
            CancellationToken token = _visibilityCts.Token;

            _ = Task.Run(async () =>
            {
                try { await Task.Delay(TimeSpan.FromMilliseconds(delayMs), token); }
                catch (TaskCanceledException) { return; }

                await Dispatcher.UIThread.InvokeAsync(async () =>
                {
                    if (token.IsCancellationRequested) return;
                    await SetTooltipVisibility(CustomTooltip, false, token);
                    _lastTooltipText = null;
                    _state = Tooltips.TooltipState.Hidden;
                });
            }, token);
        }

        public void ForceHideTooltip(double durationMs = 200)
        {
            _visibilityCts?.Cancel();
            _isTooltipShowing = false;
            _lastTooltipText = null;

            if (CustomTooltip == null) return;

            _isTooltipHiding = true;
            _state = Tooltips.TooltipState.Hiding;
            _ = SetTooltipVisibility(CustomTooltip, false, null, durationMs).ContinueWith(_ => _isTooltipHiding = false);
        }
        
        /// <summary>
        /// Sets the tooltip visibility between visible and hidden 
        /// </summary>
        /// <param name="tooltip"></param>
        /// <param name="visible"></param>
        /// <param name="token"></param>
        /// <param name="durationMs"></param>
        private async Task SetTooltipVisibility(Border tooltip, bool visible, CancellationToken? token = null, double durationMs = 120)
        {
            try
            {
                if (visible)
                {
                    tooltip.IsVisible = true;
                    await FadeIn(tooltip, durationMs, token);
                    _isTooltipShowing = true;
                }
                else
                {
                    await FadeOut(tooltip, durationMs, token);
                    tooltip.IsVisible = false;
                    _isTooltipShowing = false;
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

                border.Opacity = 1;
            }
            catch
            {
                // ignored
            }
        }

        private static async Task FadeOut(Border border, double durationMs = 120, CancellationToken? token = null)
        {
            double startOpacity = border.Opacity;
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
                    border.Opacity = Math.Max(0, startOpacity * (1 - progress));
                }

                border.Opacity = 0;
                border.IsVisible = false;
            }
            catch
            {
                // ignored
            }
        }
    }
}