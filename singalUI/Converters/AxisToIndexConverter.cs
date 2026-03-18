using Avalonia.Data.Converters;
using System;
using System.Globalization;

namespace singalUI.Converters;

public class AxisToIndexConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string axis)
        {
            return axis switch
            {
                "1" => 0,
                "2" => 1,
                "3" => 2,
                _ => 0
            };
        }
        return 0;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is int index)
        {
            return index switch
            {
                0 => "1",
                1 => "2",
                2 => "3",
                _ => "1"
            };
        }
        return "1";
    }
}
