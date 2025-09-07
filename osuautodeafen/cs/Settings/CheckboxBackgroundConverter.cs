using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace osuautodeafen.cs.Settings;

public class CheckBoxBackgroundConverter : IMultiValueConverter
{
    /// <summary>
    ///  Converts the checkbox state and brush to determine the background color
    /// </summary>
    /// <param name="values"></param>
    /// <param name="targetType"></param>
    /// <param name="parameter"></param>
    /// <param name="culture"></param>
    /// <returns></returns>
    public object Convert(IList<object> values, Type targetType, object parameter, CultureInfo culture)
    {
        var isChecked = values[0] as bool?;
        var brush = values[1] as IBrush;
        return isChecked == true ? brush : Brushes.Transparent;
    }
}