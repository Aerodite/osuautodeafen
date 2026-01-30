using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace osuautodeafen.cs.Helpers;

public sealed class BoolToOpacityConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c)
    {
        return value is true ? 0.0 : 1.0;
    }

    public object ConvertBack(object value, Type t, object p, CultureInfo c)
    {
        throw new NotSupportedException();
    }
}