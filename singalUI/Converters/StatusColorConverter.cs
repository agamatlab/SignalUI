using Avalonia.Data.Converters;
using Avalonia.Media;
using System;
using System.Globalization;

namespace singalUI.Converters;

public class StatusColorConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string status)
        {
            return status.ToLower() switch
            {
                "ready" => new SolidColorBrush(Color.Parse("#4caf50")),
                "initialization failed" => new SolidColorBrush(Color.Parse("#f44336")),
                "error" => new SolidColorBrush(Color.Parse("#f44336")),
                "not initialized" => new SolidColorBrush(Color.Parse("#707070")),
                _ => new SolidColorBrush(Color.Parse("#94a3b8"))
            };
        }
        return new SolidColorBrush(Color.Parse("#94a3b8"));
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
