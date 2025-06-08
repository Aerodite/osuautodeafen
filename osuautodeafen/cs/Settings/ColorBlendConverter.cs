using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace osuautodeafen.cs.Settings;

public class ColorBlendConverter : IMultiValueConverter
{
    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Count < 2 || values[0] is not Color baseColor || values[1] is not Color blendColor)
            return new SolidColorBrush(Colors.Transparent);

        double blendAmount = 0.1;
        byte a = (byte)(baseColor.A * (1 - blendAmount) + blendColor.A * blendAmount);
        byte r = (byte)(baseColor.R * (1 - blendAmount) + blendColor.R * blendAmount);
        byte g = (byte)(baseColor.G * (1 - blendAmount) + blendColor.G * blendAmount);
        byte b = (byte)(baseColor.B * (1 - blendAmount) + blendColor.B * blendAmount);

        return new SolidColorBrush(Color.FromArgb(a, r, g, b));
    }
}