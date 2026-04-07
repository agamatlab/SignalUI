using Avalonia.Data.Converters;
using System;
using System.Globalization;
using singalUI.Models;

namespace singalUI.Converters;

/// <summary>Visible when hardware is Sigma Koki HSC-103 rotation (COM) stage.</summary>
public class SigmakokiRotationVisibleConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is StageHardwareType hardware)
            return hardware == StageHardwareType.SigmakokiRotationStage;
        return false;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
