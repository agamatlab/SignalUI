using Avalonia.Data.Converters;
using System;
using System.Globalization;

namespace singalUI.Converters;

public class UnitToIndexConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is int unitIndex)
        {
            return unitIndex;
        }
        return 0;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is int index)
        {
            return index;
        }
        return 0;
    }
}
