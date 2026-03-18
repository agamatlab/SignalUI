using Avalonia.Data.Converters;
using singalUI.Models;
using System;
using System.Globalization;

namespace singalUI.Converters;

public class ControlModeVisibleConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is SamplingMode mode)
        {
            bool isControlMode = mode == SamplingMode.Control;

            // If parameter is "Invert", return true when NOT in control mode
            if (parameter?.ToString() == "Invert")
            {
                return !isControlMode;
            }

            return isControlMode;
        }
        return false;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
