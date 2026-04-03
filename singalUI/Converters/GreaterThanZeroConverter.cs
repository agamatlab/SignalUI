using Avalonia.Data.Converters;
using System;
using System.Globalization;

namespace singalUI.Converters;

public class GreaterThanZeroConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value == null)
        {
            return false;
        }
        
        if (value is int intValue)
        {
            return intValue > 0;
        }
        
        if (value is double doubleValue)
        {
            return doubleValue > 0;
        }
        
        if (value is long longValue)
        {
            return longValue > 0;
        }
        
        // Try to convert to int
        if (int.TryParse(value.ToString(), out int parsedInt))
        {
            return parsedInt > 0;
        }
        
        return false;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
