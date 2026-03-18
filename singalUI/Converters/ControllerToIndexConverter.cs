using Avalonia.Data.Converters;
using singalUI.Models;
using System;
using System.Globalization;

namespace singalUI.Converters;

public class ControllerToIndexConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is ControllerType controller)
        {
            return (int)controller;
        }
        return 0;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is int index && index >= 0 && index <= 6)
        {
            return (ControllerType)index;
        }
        return ControllerType.None;
    }
}
