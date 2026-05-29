using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace osuautodeafen.Helpers;

public class BrushToCssConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is SolidColorBrush solidBrush)
        {
            var color = solidBrush.Color;
            
            string hexColor = $"#{color.R:X2}{color.G:X2}{color.B:X2}";
            
            return $"path {{ fill: {hexColor}; }}";
        }
        
        return "path { fill: #FFFFFF; }";
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}