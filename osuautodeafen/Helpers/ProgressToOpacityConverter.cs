using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace osuautodeafen.cs.Helpers;

public sealed class ProgressToOpacityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is double d && d < 1.0)
            return 1.0;

        return 0.0;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}