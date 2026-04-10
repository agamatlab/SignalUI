using Avalonia.Data.Converters;
using System;
using System.Globalization;
using Avalonia.Media;

namespace singalUI.Converters;

/// <summary>
/// Converts boolean IsConnected to a color (green for connected, red for disconnected)
/// </summary>
public class ConnectionStatusColorConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool isConnected)
        {
            return isConnected
                ? Brushes.LimeGreen  // Connected = Green
                : Brushes.Red;        // Disconnected = Red
        }
        return Brushes.Gray; // Unknown state
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
