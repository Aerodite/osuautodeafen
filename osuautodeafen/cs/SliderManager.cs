using System;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using osuautodeafen.cs;

public class SliderManager
{
    private readonly SettingsPanel _settingsPanel;
    private readonly Slider _slider;

    public SliderManager(SettingsPanel settingsPanel, Slider slider)
    {
        _settingsPanel = settingsPanel ?? throw new ArgumentNullException(nameof(settingsPanel));
        _slider = slider;
    }

    public void Initialize()
    {
        _slider.ValueChanged += Slider_ValueChanged;
    }

    private void Slider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        _settingsPanel.MinCompletionPercentage = _slider.Value;
        _settingsPanel.SaveSettings();
    }
}