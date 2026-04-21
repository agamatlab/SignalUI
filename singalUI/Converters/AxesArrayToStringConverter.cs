using Avalonia.Data.Converters;
using System;
using System.Globalization;
using System.Linq;

namespace singalUI.Converters;

public class AxesArrayToStringConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string[] axes && axes.Length > 0)
            return string.Join(", ", axes);
        return "No axes";
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
