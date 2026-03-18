using Avalonia.Data.Converters;
using System;
using System.Globalization;
using singalUI.Models;

namespace singalUI.Converters;

public class StageHardwareToIndexConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is StageHardwareType hardware)
            return (int)hardware;
        return 0;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is int index && index >= 0 && index <= 2)
            return (StageHardwareType)index;
        return StageHardwareType.None;
    }
}
