using Avalonia.Data.Converters;
using Avalonia.Media;
using System;
using System.Globalization;

namespace singalUI.Converters
{
    public class FocusLevelColorConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is double focusLevel)
            {
                if (focusLevel >= 80)
                    return new SolidColorBrush(Color.Parse("#4CAF50")); // Green
                else if (focusLevel >= 50)
                    return new SolidColorBrush(Color.Parse("#FFC107")); // Amber/Yellow
                else
                    return new SolidColorBrush(Color.Parse("#F44336")); // Red
            }
            return new SolidColorBrush(Color.Parse("#4CAF50"));
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
