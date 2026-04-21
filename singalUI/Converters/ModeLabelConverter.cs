using Avalonia.Data.Converters;
using System;
using System.Globalization;

namespace singalUI.Converters;

public class ModeLabelConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool isAbsolute)
        {
            return isAbsolute ? "Target Position" : "Relative Distance (+/-)";
        }
        return "Target Position";
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
