using Avalonia.Data.Converters;
using System;
using System.Globalization;

namespace singalUI.Converters;

public class CompareToZeroConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is int intValue)
        {
            return intValue == 0; // Show when Checker Board (index 0) is selected
        }
        return false;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
