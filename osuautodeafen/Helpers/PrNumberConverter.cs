using System;
using System.Globalization;
using System.Text.RegularExpressions;
using Avalonia.Data.Converters;

namespace osuautodeafen.cs.Helpers;

public class PrNumberConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string url)
            return "";

        Match match = Regex.Match(url, @"pull/(\d+)");
        return match.Success ? $"#{match.Groups[1].Value}" : "";
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}