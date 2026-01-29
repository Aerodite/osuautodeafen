using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace osuautodeafen.cs.Helpers;

public sealed class TextBeforePrConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var text = value as string ?? string.Empty;

        int idx = text.IndexOf("(#");
        return idx >= 0 ? text[..idx] : text;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public sealed class TextAfterPrConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var text = value as string ?? string.Empty;

        int idx = text.IndexOf(")");
        return idx >= 0 ? ")" : string.Empty;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}