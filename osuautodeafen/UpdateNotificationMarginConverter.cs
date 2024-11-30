using System;
using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;

namespace osuautodeafen;

public class UpdateNotificationMarginConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var isVisible = (bool)value;
        return isVisible ? new Thickness(0, 0, 0, 0) : new Thickness(0, 0, 0, 0);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}