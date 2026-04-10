using Avalonia.Data.Converters;
using System;
using System.Globalization;
using singalUI.Models;

namespace singalUI.Converters;

/// <summary>
/// Shows/hides element based on whether Sigma Koki is NOT selected (inverse of SigmakokiVisibleConverter)
/// </summary>
public class SigmakokiVisibleInvertedConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is StageHardwareType hardware)
        {
            return hardware != StageHardwareType.Sigmakoki;
        }
        return true;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
