using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
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
        private CancellationTokenSource? _fadeCts;
        
        private Task? _fadeTask;
        
        private double _windowWidth;
        private double _windowHeight;
        
        public Tooltips.TooltipType CurrentTooltipType;
        
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
            
            if (_isTooltipShowing && _lastTooltipText == text)
            {
                UpdateTooltipPosition(position);
                return;
            }

            _lastTooltipText = text;
            
            TextBlock measure = new() { Text = text, FontSize = _tooltipText.FontSize, FontFamily = _tooltipText.FontFamily };
            measure.Measure(Size.Infinity);
            double tooltipWidth = measure.DesiredSize.Width + CustomTooltip.Padding.Left + CustomTooltip.Padding.Right;
            double tooltipHeight = measure.DesiredSize.Height + CustomTooltip.Padding.Top + CustomTooltip.Padding.Bottom;
            
            UpdateTooltipPosition(position, tooltipWidth, tooltipHeight);
            
            _tooltipText.Text = text;
            
            if (!_isTooltipShowing)
            {
                CustomTooltip.Opacity = 0;
                CustomTooltip.IsVisible = true;
                _ = FadeIn(CustomTooltip);
            }

            _isTooltipShowing = true;
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
            var token = _hideCts.Token;

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
            });
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

            double width, height;

            if (tooltipWidth.HasValue && tooltipHeight.HasValue)
            {
                width = tooltipWidth.Value;
                height = tooltipHeight.Value;
            }
            else
            {
                CustomTooltip.Measure(Size.Infinity);
                width = CustomTooltip.DesiredSize.Width;
                height = CustomTooltip.DesiredSize.Height;
            }

            double left = position.X - width / 2;
            double top = position.Y - height - TooltipOffset;

            left = Math.Max(0, Math.Min(left, _windowWidth - width));

            Canvas.SetLeft(CustomTooltip, left);
            Canvas.SetTop(CustomTooltip, top);
        }
        
        private async Task FadeIn(Border border, double durationMs = 120, CancellationToken? token = null)
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

        private async Task FadeOut(Border border, double durationMs = 120, CancellationToken? token = null)
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