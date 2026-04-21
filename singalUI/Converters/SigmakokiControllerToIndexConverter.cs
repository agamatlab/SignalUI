using Avalonia.Data.Converters;
using System;
using System.Globalization;
using singalUI.Models;

namespace singalUI.Converters;

public class SigmakokiControllerToIndexConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is SigmakokiControllerType controller)
            return (int)controller;
        return 3; // Default to HSC_103 (index 3)
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is int index && index >= 0 && index <= 6)
            return (SigmakokiControllerType)index;
        return SigmakokiControllerType.HSC_103;
    }
}
