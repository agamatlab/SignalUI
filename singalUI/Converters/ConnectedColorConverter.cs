using Avalonia.Data.Converters;
using Avalonia.Media;
using System;
using System.Globalization;

namespace singalUI.Converters;

public class ConnectedColorConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool isConnected)
        {
            return isConnected
                ? new SolidColorBrush(Color.Parse("#22c55e"))
                : new SolidColorBrush(Color.Parse("#ef4444"));
        }
        return new SolidColorBrush(Color.Parse("#94a3b8"));
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
