using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia.Data.Converters;
using Avalonia.Media;

public class AverageColorCycleConverter : IMultiValueConverter
{
    /// <summary>
    ///     Converts an array of three color strings and an index to a SolidColorBrush of the selected color
    /// </summary>
    /// <param name="values"></param>
    /// <param name="targetType"></param>
    /// <param name="parameter"></param>
    /// <param name="culture"></param>
    /// <returns></returns>
    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        return Convert(values?.ToArray() ?? Array.Empty<object>(), targetType, parameter, culture);
    }

    /// <summary>
    ///     Converts an array of three color strings and an index to a SolidColorBrush of the selected color
    /// </summary>
    /// <param name="values"></param>
    /// <param name="targetType"></param>
    /// <param name="parameter"></param>
    /// <param name="culture"></param>
    /// <returns></returns>
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length < 4) return Brushes.Transparent;

        string?[] colors = new[] { values[0]?.ToString(), values[1]?.ToString(), values[2]?.ToString() };
        if (!int.TryParse(values[3]?.ToString(), out int index)) return Brushes.Transparent;

        string? colorStr = colors[index % 3];
        if (string.IsNullOrEmpty(colorStr)) return Brushes.Transparent;

        try
        {
            Color color = ParseColor(colorStr);
            return new SolidColorBrush(color);
        }
        catch
        {
            return Brushes.Transparent;
        }
    }

    /// <summary>
    ///     Parses a hex color string to a Color object
    /// </summary>
    /// <param name="colorStr"></param>
    /// <returns></returns>
    /// <exception cref="FormatException"></exception>
    private static Color ParseColor(string colorStr)
    {
        if (colorStr.StartsWith("#"))
        {
            colorStr = colorStr.Substring(1);
            if (colorStr.Length == 6)
            {
                byte r = byte.Parse(colorStr.Substring(0, 2), NumberStyles.HexNumber);
                byte g = byte.Parse(colorStr.Substring(2, 2), NumberStyles.HexNumber);
                byte b = byte.Parse(colorStr.Substring(4, 2), NumberStyles.HexNumber);
                return Color.FromRgb(r, g, b);
            }

            if (colorStr.Length == 8)
            {
                byte a = byte.Parse(colorStr.Substring(0, 2), NumberStyles.HexNumber);
                byte r = byte.Parse(colorStr.Substring(2, 2), NumberStyles.HexNumber);
                byte g = byte.Parse(colorStr.Substring(4, 2), NumberStyles.HexNumber);
                byte b = byte.Parse(colorStr.Substring(6, 2), NumberStyles.HexNumber);
                return Color.FromArgb(a, r, g, b);
            }
        }

        throw new FormatException("Invalid color format");
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}