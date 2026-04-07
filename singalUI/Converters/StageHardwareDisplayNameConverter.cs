using Avalonia.Data.Converters;
using System;
using System.Globalization;
using singalUI.Models;

namespace singalUI.Converters;

/// <summary>
/// Convert StageHardwareType to display name
/// </summary>
public class StageHardwareDisplayNameConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is StageHardwareType hardware)
        {
            return hardware switch
            {
                StageHardwareType.PI => "PI",
                StageHardwareType.Sigmakoki => "Sigma Koki",
                StageHardwareType.SigmakokiRotationStage => "Sigma Koki rotation (HSC-103)",
                _ => "None"
            };
        }
        return "None";
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
