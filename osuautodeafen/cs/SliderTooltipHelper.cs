using Avalonia.Controls;
using Avalonia.Input;
using System;
using Avalonia.Controls.Primitives;

public class SliderTooltipHelper
{
    private bool _isDragging;
    private readonly Slider _slider;
    private readonly Popup _popup;
    private readonly Window _window;

    public SliderTooltipHelper(Window window, Slider slider, Popup popup)
    {
        _window = window;
        _slider = slider;
        _popup = popup;

        _slider.AddHandler(InputElement.PointerEnteredEvent, (s, e) => ShowTooltip(e));
        _slider.AddHandler(InputElement.PointerCaptureLostEvent, (s, e) => EndDrag());
        _slider.PointerPressed += (s, e) => StartDrag(e);
        _slider.PointerReleased += (s, e) => EndDrag();
        _slider.PointerMoved += (s, e) => { if (_isDragging || _popup.IsOpen) UpdateTooltipPosition(e); };
    }

    private void StartDrag(PointerEventArgs e)
    {
        _isDragging = true;
        ShowTooltip(e);
        _window.PointerMoved += Window_PointerMoved;
    }

    private void EndDrag()
    {
        _isDragging = false;
        HideTooltip();
        _window.PointerMoved -= Window_PointerMoved;
    }

    private void Window_PointerMoved(object? sender, PointerEventArgs e)
    {
        if (_isDragging)
            UpdateTooltipPosition(e);
    }

    private void ShowTooltip(PointerEventArgs e)
    {
        _popup.IsOpen = true;
        UpdateTooltipPosition(e);
    }

    private void HideTooltip()
    {
        if (!_isDragging)
            _popup.IsOpen = false;
    }

    private void UpdateTooltipPosition(PointerEventArgs e)
    {
        double percent = (_slider.Value - _slider.Minimum) / (_slider.Maximum - _slider.Minimum);
        double thumbX = percent * (_slider.Bounds.Width - 16); // 16 = thumb width approx
        _popup.HorizontalOffset = thumbX;
    }
}