using Avalonia.Data.Converters;
using singalUI.Models;
using System;
using System.Globalization;

namespace singalUI.Converters;

public class TriggeredModeVisibleConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is SamplingMode mode)
        {
            return mode == SamplingMode.Triggered;
        }
        return false;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
