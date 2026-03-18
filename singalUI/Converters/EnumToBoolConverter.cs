using Avalonia.Data.Converters;
using singalUI.Models;
using System;
using System.Globalization;

namespace singalUI.Converters;

public class EnumToBoolConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is SamplingMode mode && parameter is string param)
        {
            return mode.ToString().Equals(param, StringComparison.OrdinalIgnoreCase);
        }
        return false;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool isChecked && isChecked && parameter is string param)
        {
            return Enum.Parse(typeof(SamplingMode), param, true);
        }
        return Avalonia.AvaloniaProperty.UnsetValue;
    }
}
