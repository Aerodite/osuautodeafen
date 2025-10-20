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

        private CancellationTokenSource? _hideCts;
        private CancellationTokenSource? _sizeAnimationCts;

        private double _windowWidth;
        private double _windowHeight;

        public Tooltips.TooltipType CurrentTooltipType;

        private (double width, double height)? _animationTarget;

        public bool IsTooltipVisible => _isTooltipShowing;

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
        }

        /// <summary>
        /// Shows a tooltip at a specified position with specified text
        /// </summary>
        /// <param name="position">The position where the tooltip will appear.</param>
        /// <param name="text">The text to display in the tooltip.</param>
        public void ShowTooltip(Point position, string text)
        {
            if (CustomTooltip == null || _tooltipText == null) return;

            CancelHide();

            _lastTooltipText ??= "";
            _tooltipText.Text = text;
            
            TextBlock measure = new()
            {
                Text = text,
                FontSize = _tooltipText.FontSize,
                FontFamily = _tooltipText.FontFamily,
                TextWrapping = TextWrapping.Wrap
            };
            measure.Measure(new Size(_windowWidth * 0.5, double.PositiveInfinity));

            double tooltipWidth = measure.DesiredSize.Width + CustomTooltip.Padding.Left + CustomTooltip.Padding.Right;
            double tooltipHeight = measure.DesiredSize.Height + CustomTooltip.Padding.Top + CustomTooltip.Padding.Bottom;

            const double minPixelDifference = 5;
            bool widthChanged = Math.Abs(CustomTooltip.Width - tooltipWidth) > minPixelDifference;
            bool heightChanged = Math.Abs(CustomTooltip.Height - tooltipHeight) > minPixelDifference;
            bool significantSizeChange = widthChanged || heightChanged;

            if (!_isTooltipShowing)
            {
                CustomTooltip.Width = tooltipWidth;
                CustomTooltip.Height = tooltipHeight;
                CustomTooltip.IsVisible = true;
                CustomTooltip.Opacity = 0;
                _ = FadeIn(CustomTooltip);

                _animationTarget = (tooltipWidth, tooltipHeight);
                _ = AnimateTooltipSize(CustomTooltip, tooltipWidth, tooltipHeight, position);
            }
            else
            {
                UpdateTooltipPosition(position);
                
                if (significantSizeChange &&
                    (_animationTarget == null ||
                     _animationTarget.Value.width != tooltipWidth ||
                     _animationTarget.Value.height != tooltipHeight))
                {
                    _sizeAnimationCts?.Cancel();
                    _animationTarget = (tooltipWidth, tooltipHeight);
                    _ = AnimateTooltipSize(CustomTooltip, tooltipWidth, tooltipHeight, position);
                }
            }

            _isTooltipShowing = true;
        }

        private async Task AnimateTooltipSize(Border border, double targetWidth, double targetHeight, Point position, double durationMs = 120)
        {
            _sizeAnimationCts?.Cancel();
            _sizeAnimationCts = new CancellationTokenSource();
            CancellationToken token = _sizeAnimationCts.Token;

            if (_tooltipText == null) return;

            double startWidth = border.Width;
            
            _tooltipText.Width = targetWidth;
            _tooltipText.Measure(new Size(targetWidth, double.PositiveInfinity));
            double fixedHeight = _tooltipText.DesiredSize.Height + border.Padding.Top + border.Padding.Bottom;

            bool widthChanged = Math.Abs(targetWidth - startWidth) > 0.5;

            _tooltipText.Clip ??= new RectangleGeometry(new Rect(0, 0, targetWidth, 0));

            double left = position.X + TooltipOffset;
            double top = position.Y - fixedHeight;

            if (left + targetWidth > _windowWidth) left = _windowWidth - targetWidth;
            if (top < 0) top = position.Y + TooltipOffset;

            Canvas.SetLeft(border, Math.Round(left));
            Canvas.SetTop(border, Math.Round(top));

            double elapsed = 0;
            const int interval = 10;

            try
            {
                while (elapsed < durationMs)
                {
                    if (token.IsCancellationRequested) return;

                    await Task.Delay(interval, token);
                    elapsed += interval;

                    double progress = Math.Min(elapsed / durationMs, 1);
                    double easedProgress = 1 - Math.Pow(1 - progress, 3);

                    if (widthChanged)
                        border.Width = startWidth + (targetWidth - startWidth) * easedProgress;
                    
                    border.Height = fixedHeight;

                    _tooltipText.Clip = new RectangleGeometry(new Rect(0, 0, border.Width, fixedHeight));
                }

                border.Width = targetWidth;
                border.Height = fixedHeight;
                _tooltipText.Clip = null;

                _animationTarget = null;
            }
            catch
            {
                // ignored
            }
        }

        /// <summary>
        /// Hides the currently showing Tooltip
        /// </summary>
        /// <param name="delayMs">how long it takes the Tooltip to fade out</param>
        public void HideTooltip(double delayMs = 200)
        {
            if (CustomTooltip == null || !_isTooltipShowing) return;

            if (_hideCts != null && !_hideCts.IsCancellationRequested)
                _hideCts.Cancel();

            _hideCts = new CancellationTokenSource();
            CancellationToken token = _hideCts.Token;

            _ = Task.Run(async () =>
            {
                try { await Task.Delay(TimeSpan.FromMilliseconds(delayMs), token); }
                catch (TaskCanceledException) { return; }

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (!_isTooltipShowing) return;
                    _ = FadeOut(CustomTooltip);
                    _isTooltipShowing = false;
                    _lastTooltipText = null;
                });
            }, token);
        }

        private void CancelHide() => _hideCts?.Cancel();

        /// <summary>
        /// Moves the Tooltip from its current position to a new one, with a specified width and height to make sure it stays clamped to window bounds.
        /// </summary>
        /// <param name="position">The position where the tooltip will move to.</param>
        /// <param name="tooltipWidth">The current width of the tooltip</param>
        /// <param name="tooltipHeight">The current height of the tooltip</param>
        public void UpdateTooltipPosition(Point position, double? tooltipWidth = null, double? tooltipHeight = null)
        {
            if (CustomTooltip == null) return;

            double width = tooltipWidth ?? CustomTooltip.DesiredSize.Width;
            double height = tooltipHeight ?? CustomTooltip.DesiredSize.Height;
            
            double left = position.X + TooltipOffset;
            double top = position.Y - height;
            
            if (left + width > _windowWidth)
                left = _windowWidth - width;

            if (left < 0)
                left = 0;

            if (top < 0)
                top = position.Y + TooltipOffset;

            if (top + height > _windowHeight)
                top = _windowHeight - height;
            

            Canvas.SetLeft(CustomTooltip, Math.Round(left));
            Canvas.SetTop(CustomTooltip, Math.Round(top));
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